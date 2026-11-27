using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.JSInterop;

namespace web.Services;

/// <summary>
/// The update prompt's browser-local state. A new service worker waits until
/// the user accepts (or the automatic idle path fires); this service owns the
/// preference, the idle countdown, and the shared-link pause.
/// </summary>
public sealed class UpdateState(IJSRuntime js) : IDisposable
{
    public const string ModeKey = "poe-regex.update-mode";
    public const string AskMode = "ask";
    public const string IdleMode = "idle";

    private DotNetObjectReference<UpdateState>? _reference;
    private bool _loaded;
    private bool _bound;
    private string _mode = AskMode;
    private string _status = "idle";
    private string _error = "";
    private string _current = "";
    private string _latest = "";
    private bool _waiting;
    private bool _reloadOnly;
    private bool _dismissed;
    private bool _suspended;
    private bool _applyPending;
    private bool _checking;
    private bool _applying;
    private int? _countdown;
    private int _countdownSeconds = 5;
    private Timer? _countdownTimer;

    public event Action? Changed;

    public string Mode => _mode;
    public string CurrentCommit => _current;
    public string LatestCommit => _latest;
    public bool Available => Status is "available" or "countdown" or "downloading" or "applying";
    public bool Suspended => _suspended;
    public bool Checking => _checking;
    public bool CanCheck => _bound && !_checking && !_applying && !_suspended;
    public bool CanApply => _bound && !_suspended && Status is "available" or "countdown";

    public string Status =>
        _applying ? "applying" :
        _countdown is not null ? "countdown" :
        _status;

    public int? Countdown => _countdown;

    public bool Visible =>
        !_suspended && !_dismissed &&
        Status is "available" or "countdown" or "applying" or "downloading" or "error";

    public string Title => Status switch
    {
        "countdown" => "Updating automatically",
        "applying" => "Applying update",
        "downloading" => "Preparing update",
        "error" => "Update check failed",
        _ => "Update available",
    };

    public string Detail => Status switch
    {
        "countdown" => $"Reloading in {_countdown ?? 0} second{(_countdown == 1 ? "" : "s")}. Shared picks finish first.",
        "applying" => "The page will reload with the new version.",
        "downloading" => "The new version is downloading.",
        "error" => string.IsNullOrWhiteSpace(_error) ? "Try again." : _error,
        _ => "A new version is ready. Your picks are saved.",
    };

    public string CheckLabel => Status switch
    {
        "checking" => "Checking…",
        "uptodate" => "Up to date",
        "available" or "countdown" or "applying" => "Update available",
        "downloading" => "Preparing…",
        "error" => "Check failed",
        _ => "Not checked yet",
    };

    public string BuildLabel
    {
        get
        {
            var current = Short(CurrentCommit);
            var latest = Short(LatestCommit);
            if (current.Length == 0) return "Build unknown";
            if (latest.Length == 0 || latest == current) return $"Build {current}";
            return $"Build {current} → {latest}";
        }
    }

    public async Task EnsureLoadedAsync()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            var stored = await js.InvokeAsync<string?>("localStorage.getItem", ModeKey);
            if (stored is AskMode or IdleMode) _mode = stored;
        }
        catch
        {
            // Storage unavailable: keep Ask.
        }
        Changed?.Invoke();
    }

    public async Task BindAsync()
    {
        await EnsureLoadedAsync();
        if (_bound) return;
        _reference = DotNetObjectReference.Create(this);
        try
        {
            await js.InvokeVoidAsync("poeRegexUpdates.bind", _reference);
            _bound = true;
        }
        catch
        {
            _reference.Dispose();
            _reference = null;
            Changed?.Invoke();
            return;
        }
        try { await js.InvokeVoidAsync("poeRegexUpdates.setAutoMode", _mode); }
        catch { /* the default Ask mode still works */ }
        try
        {
            var config = await js.InvokeAsync<UpdateTestConfig>("poeRegexUpdates.testConfig");
            if (config.CountdownSeconds > 0) _countdownSeconds = config.CountdownSeconds;
        }
        catch { /* production uses the five-second default */ }
        Changed?.Invoke();
    }

    public async Task CheckAsync()
    {
        if (!CanCheck) return;
        _checking = true;
        Changed?.Invoke();
        try
        {
            await js.InvokeVoidAsync("poeRegexUpdates.check", true);
        }
        catch
        {
            _status = "error";
            _error = "Could not check for updates.";
        }
        finally
        {
            _checking = false;
            Changed?.Invoke();
        }
    }

    public async Task ApplyAsync()
    {
        if (_suspended)
        {
            _applyPending = true;
            return;
        }
        if (_applying) return;
        _applying = true;
        StopCountdown();
        Changed?.Invoke();
        try
        {
            var started = await js.InvokeAsync<bool>("poeRegexUpdates.apply");
            if (!started)
            {
                _applying = false;
                _status = "error";
                _error = "No update is ready yet.";
                Changed?.Invoke();
            }
        }
        catch
        {
            _applying = false;
            _status = "error";
            _error = "The update could not be applied.";
            Changed?.Invoke();
        }
    }

    public async Task SetModeAsync(string mode)
    {
        if (mode is not (AskMode or IdleMode)) return;
        if (_mode == mode) return;
        _mode = mode;
        StopCountdown();
        Changed?.Invoke();
        try { await js.InvokeVoidAsync("localStorage.setItem", ModeKey, mode); }
        catch { /* the session still honors the choice */ }
        try { await js.InvokeVoidAsync("poeRegexUpdates.setAutoMode", mode); }
        catch { }
    }

    public void Dismiss()
    {
        _dismissed = true;
        StopCountdown();
        try { _ = js.InvokeVoidAsync("poeRegexUpdates.dismissAuto"); } catch { }
        Changed?.Invoke();
    }

    /// <summary>A share link is being applied; do not reload the tab yet.</summary>
    public void SuspendAuto()
    {
        _suspended = true;
        StopCountdown();
        Changed?.Invoke();
    }

    public async Task ResumeAutoAsync()
    {
        _suspended = false;
        Changed?.Invoke();
        if (_applyPending)
        {
            _applyPending = false;
            await ApplyAsync();
            return;
        }
        try { await js.InvokeVoidAsync("poeRegexUpdates.resumeAuto"); }
        catch { }
    }

    [JSInvokable]
    public void OnUpdateState(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var latest = GetString(root, "latest");
            if (!string.Equals(latest, _latest, StringComparison.Ordinal))
            {
                _dismissed = false;
            }
            _current = GetString(root, "current");
            _latest = latest;
            _status = GetString(root, "status");
            _waiting = root.TryGetProperty("waiting", out var waiting) &&
                       waiting.ValueKind == JsonValueKind.True;
            _reloadOnly = root.TryGetProperty("reloadOnly", out var reloadOnly) &&
                          reloadOnly.ValueKind == JsonValueKind.True;
            _error = GetString(root, "error");
        }
        catch
        {
            _status = "error";
            _error = "Could not read the update state.";
        }
        Changed?.Invoke();
    }

    [JSInvokable]
    public void OnAutoApplyDue()
    {
        if (_suspended || _dismissed || !(_waiting || _reloadOnly) || _mode != IdleMode) return;
        StartCountdown();
    }

    [JSInvokable]
    public void OnUserActivity() => StopCountdown();

    private void StartCountdown()
    {
        if (_countdown is not null) return;
        _countdown = _countdownSeconds;
        _countdownTimer?.Dispose();
        _countdownTimer = new Timer(_ => TickCountdown(), null, 1000, 1000);
        Changed?.Invoke();
    }

    private void TickCountdown()
    {
        if (_countdown is null)
        {
            _countdownTimer?.Dispose();
            _countdownTimer = null;
            return;
        }
        _countdown--;
        if (_countdown <= 0)
        {
            StopCountdown();
            _ = ApplyAsync();
            return;
        }
        Changed?.Invoke();
    }

    private void StopCountdown()
    {
        if (_countdown is null && _countdownTimer is null) return;
        _countdown = null;
        _countdownTimer?.Dispose();
        _countdownTimer = null;
        Changed?.Invoke();
    }

    private static string GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static string Short(string commit) =>
        commit.Length > 12 ? commit[..12] : commit;

    public sealed record UpdateTestConfig(
        [property: JsonPropertyName("countdownSeconds")] int CountdownSeconds);

    public void Dispose()
    {
        _reference?.Dispose();
        _reference = null;
        _countdownTimer?.Dispose();
        _countdownTimer = null;
    }
}

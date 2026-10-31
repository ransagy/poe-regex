using Microsoft.JSInterop;

namespace web.Services;

/// <summary>
/// Browser-local preferences that more than one component needs. The header
/// (Options popover) owns the controls; the search bar reacts to auto-copy.
/// Theme and display scale stay in the layout because only it uses them.
/// </summary>
public sealed class PreferencesState(IJSRuntime js)
{
    public const string AutoCopyKey = "poe-regex.auto-copy";

    private Task? _load;

    public bool AutoCopy { get; private set; }

    public event Action? Changed;

    public Task EnsureLoadedAsync() => _load ??= LoadAsync();

    private async Task LoadAsync()
    {
        try
        {
            AutoCopy = await js.InvokeAsync<string?>("localStorage.getItem", AutoCopyKey) == "1";
        }
        catch
        {
            AutoCopy = false; // storage unavailable: keep the in-memory default
        }
        Changed?.Invoke();
    }

    public async Task SetAutoCopyAsync(bool value)
    {
        if (AutoCopy == value) return;
        AutoCopy = value;
        Changed?.Invoke();
        try
        {
            await js.InvokeVoidAsync("localStorage.setItem", AutoCopyKey, value ? "1" : "0");
        }
        catch
        {
            // The session still honors the toggle; only persistence failed.
        }
    }
}

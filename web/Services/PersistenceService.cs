using System.Text.Json;
using Microsoft.JSInterop;
using PoeRegex.Web.Core;

namespace web.Services;

/// <summary>localStorage persistence behind a seam for future URL/account stores.</summary>
public sealed class PersistenceService(IJSRuntime js)
{
    // Legacy PoE2 keys, kept as a one-release fallback.
    public const string StorageKey = "poe-regex.workbench.v1";
    private const string LegacySavedPicksKey = "poe-regex.recipes.v1";
    private const string SavedPicksKeyV1 = "poe-regex.saved-picks.v1";
    public const string HandoffKey = "poe-regex.handoff.v1";
    private const string IntroKey = "poe-regex.intro.v1";
    private const string LastGameKey = "poe-regex.game.v1";

    private static string WorkbenchKey(string game) => $"poe-regex.workbench.v2.{game}";
    private static string SavedPicksKey(string game) => $"poe-regex.saved-picks.v2.{game}";

    public async Task<WorkbenchSnapshot?> LoadAsync() => await LoadAsync("poe2");

    public async Task<WorkbenchSnapshot?> LoadAsync(string game)
    {
        try
        {
            var raw = await js.InvokeAsync<string?>("localStorage.getItem", WorkbenchKey(game));
            if (string.IsNullOrEmpty(raw) && game == "poe2")
                raw = await js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
            if (string.IsNullOrEmpty(raw)) return null;
            var snap = JsonSerializer.Deserialize(raw, WebJsonContext.Default.WorkbenchSnapshot);
            if (!SnapshotValidator.IsValid(snap)) return null;
            return snap! with { Game = game };
        }
        catch
        {
            return null; // corrupt or unavailable storage never blocks boot
        }
    }

    public async Task SaveAsync(WorkbenchSnapshot snap) => await SaveAsync(snap, "poe2");

    public async Task SaveAsync(WorkbenchSnapshot snap, string game)
    {
        var raw = JsonSerializer.Serialize(snap with { Game = game }, WebJsonContext.Default.WorkbenchSnapshot);
        try
        {
            await js.InvokeVoidAsync("poeRegexSetWorkbench", WorkbenchKey(game), raw);
        }
        catch
        {
            try { await js.InvokeVoidAsync("localStorage.setItem", WorkbenchKey(game), raw); } catch { }
        }
    }

    public async Task<string> LoadLastGameAsync()
    {
        try
        {
            var game = await js.InvokeAsync<string?>("localStorage.getItem", LastGameKey);
            return game is "poe1" or "poe2" ? game : "poe2";
        }
        catch
        {
            return "poe2";
        }
    }

    public async Task SaveLastGameAsync(string game)
    {
        try { await js.InvokeVoidAsync("localStorage.setItem", LastGameKey, game); } catch { }
    }

    /// <summary>Tell other tabs that a share link replaced the workbench.</summary>
    public async Task MarkSharedHandoffAsync()
    {
        try
        {
            var marker = $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}:{Guid.NewGuid():N}";
            await js.InvokeVoidAsync("localStorage.setItem", HandoffKey, marker);
        }
        catch
        {
            // Cross-tab handoff is best-effort.
        }
    }

    public async Task<List<SavedPick>> LoadSavedPicksAsync() => await LoadSavedPicksAsync("poe2");

    public async Task<List<SavedPick>> LoadSavedPicksAsync(string game)
    {
        try
        {
            var raw = await js.InvokeAsync<string?>("localStorage.getItem", SavedPicksKey(game));
            if (string.IsNullOrEmpty(raw) && game == "poe2")
            {
                raw = await js.InvokeAsync<string?>("localStorage.getItem", SavedPicksKeyV1);
                if (string.IsNullOrEmpty(raw))
                    raw = await js.InvokeAsync<string?>("localStorage.getItem", LegacySavedPicksKey);
            }
            if (string.IsNullOrEmpty(raw)) return [];
            var savedPicks = JsonSerializer.Deserialize(raw, WebJsonContext.Default.ListSavedPick) ?? [];
            return savedPicks
                .Where(SnapshotValidator.IsValid)
                .Select(p => p.Game is "poe1" or "poe2" ? p : p with { Game = "poe2" })
                .Where(p => p.Game == game && p.Snapshot.Game == game)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    public async Task SaveSavedPicksAsync(List<SavedPick> savedPicks) => await SaveSavedPicksAsync(savedPicks, "poe2");

    public async Task SaveSavedPicksAsync(List<SavedPick> savedPicks, string game)
    {
        try
        {
            var raw = JsonSerializer.Serialize(savedPicks, WebJsonContext.Default.ListSavedPick);
            await js.InvokeVoidAsync("localStorage.setItem", SavedPicksKey(game), raw);
        }
        catch
        {
        }
    }

    public async Task<bool> LoadIntroDismissedAsync()
    {
        try
        {
            return await js.InvokeAsync<string?>("localStorage.getItem", IntroKey) == "1";
        }
        catch
        {
            return false;
        }
    }

    public async Task SaveIntroDismissedAsync()
    {
        try
        {
            await js.InvokeVoidAsync("localStorage.setItem", IntroKey, "1");
        }
        catch
        {
        }
    }

    public async Task ClearAsync() => await ClearAsync("poe2");

    public async Task ClearAsync(string game)
    {
        try
        {
            await js.InvokeVoidAsync("localStorage.removeItem", WorkbenchKey(game));
            if (game == "poe2") await js.InvokeVoidAsync("localStorage.removeItem", StorageKey);
        }
        catch
        {
        }
    }
}

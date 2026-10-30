using System.Text.Json;
using Microsoft.JSInterop;
using PoeRegex.Web.Core;

namespace web.Services;

/// <summary>localStorage persistence behind a seam for future URL/account stores.</summary>
public sealed class PersistenceService(IJSRuntime js)
{
    public const string StorageKey = "poe-regex.workbench.v1";
    public const string HandoffKey = "poe-regex.handoff.v1";
    private const string IntroKey = "poe-regex.intro.v1";
    private const string RecipesKey = "poe-regex.recipes.v1";

    public async Task<WorkbenchSnapshot?> LoadAsync()
    {
        try
        {
            var raw = await js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
            if (string.IsNullOrEmpty(raw)) return null;
            var snap = JsonSerializer.Deserialize(raw, WebJsonContext.Default.WorkbenchSnapshot);
            return SnapshotValidator.IsValid(snap) ? snap : null;
        }
        catch
        {
            return null; // corrupt or unavailable storage never blocks boot
        }
    }

    public async Task SaveAsync(WorkbenchSnapshot snap)
    {
        var raw = JsonSerializer.Serialize(snap, WebJsonContext.Default.WorkbenchSnapshot);
        try
        {
            await js.InvokeVoidAsync("poeRegexSetWorkbench", StorageKey, raw);
        }
        catch
        {
            // Older cached shell or unavailable helper: fall back to the raw key.
            try { await js.InvokeVoidAsync("localStorage.setItem", StorageKey, raw); } catch { }
        }
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

    public async Task<List<SavedRecipe>> LoadRecipesAsync()
    {
        try
        {
            var raw = await js.InvokeAsync<string?>("localStorage.getItem", RecipesKey);
            if (string.IsNullOrEmpty(raw)) return [];
            var recipes = JsonSerializer.Deserialize(raw, WebJsonContext.Default.ListSavedRecipe) ?? [];
            return recipes.Where(SnapshotValidator.IsValid).ToList();
        }
        catch
        {
            return [];
        }
    }

    public async Task SaveRecipesAsync(List<SavedRecipe> recipes)
    {
        try
        {
            var raw = JsonSerializer.Serialize(recipes, WebJsonContext.Default.ListSavedRecipe);
            await js.InvokeVoidAsync("localStorage.setItem", RecipesKey, raw);
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

    public async Task ClearAsync()
    {
        try
        {
            await js.InvokeVoidAsync("localStorage.removeItem", StorageKey);
        }
        catch
        {
        }
    }
}

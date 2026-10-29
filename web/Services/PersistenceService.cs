using System.Text.Json;
using Microsoft.JSInterop;
using PoeRegex.Web.Core;

namespace web.Services;

/// <summary>localStorage persistence behind a seam for future URL/account stores.</summary>
public sealed class PersistenceService(IJSRuntime js)
{
    public const string StorageKey = "poe-regex.workbench.v1";
    private const string IntroKey = "poe-regex.intro.v1";

    public async Task<WorkbenchSnapshot?> LoadAsync()
    {
        try
        {
            var raw = await js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
            if (string.IsNullOrEmpty(raw)) return null;
            return JsonSerializer.Deserialize(raw, WebJsonContext.Default.WorkbenchSnapshot);
        }
        catch
        {
            return null; // corrupt or unavailable storage never blocks boot
        }
    }

    public async Task SaveAsync(WorkbenchSnapshot snap)
    {
        try
        {
            var raw = JsonSerializer.Serialize(snap, WebJsonContext.Default.WorkbenchSnapshot);
            await js.InvokeVoidAsync("localStorage.setItem", StorageKey, raw);
        }
        catch
        {
            // best-effort: private mode / quota / unavailable JS
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

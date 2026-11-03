using System.Text.Json;
using Bunit;
using PoeRegex.Web.Core;
using web.Services;

namespace PoeRegex.Web.Tests;

public class PersistenceServiceTests
{
    private static SavedPick Pick(string name) =>
        new(name, DateTimeOffset.UnixEpoch, SnapshotCodec.CaptureCriteria(new WorkbenchState()));

    private static string Json(params SavedPick[] picks) =>
        JsonSerializer.Serialize(picks.ToList(), WebJsonContext.Default.ListSavedPick);

    [Fact]
    public async Task LoadSavedPicks_MigratesLegacyPicksToTheNewKey()
    {
        using var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.JSInterop.Setup<string?>("localStorage.getItem", "poe-regex.saved-picks.v1").SetResult(null);
        ctx.JSInterop.Setup<string?>("localStorage.getItem", "poe-regex.recipes.v1").SetResult(Json(Pick("Legacy")));
        var service = new PersistenceService(ctx.JSInterop.JSRuntime);

        var loaded = await service.LoadSavedPicksAsync();

        loaded.Should().ContainSingle().Which.Name.Should().Be("Legacy");
        ctx.JSInterop.VerifyInvoke("localStorage.setItem");
        ctx.JSInterop.VerifyInvoke("localStorage.removeItem");
    }

    [Fact]
    public async Task LoadSavedPicks_PrefersTheNewKeyAndLeavesTheLegacyKeyAlone()
    {
        using var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.JSInterop.Setup<string?>("localStorage.getItem", "poe-regex.saved-picks.v1").SetResult(Json(Pick("Current")));
        var service = new PersistenceService(ctx.JSInterop.JSRuntime);

        var loaded = await service.LoadSavedPicksAsync();

        loaded.Should().ContainSingle().Which.Name.Should().Be("Current");
        ctx.JSInterop.VerifyNotInvoke("localStorage.setItem");
        ctx.JSInterop.VerifyNotInvoke("localStorage.removeItem");
    }
}

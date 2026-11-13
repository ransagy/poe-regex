using System.Text.Json;
using Bunit;
using PoeRegex.Web.Core;
using web.Services;

namespace PoeRegex.Web.Tests;

public class PersistenceServiceTests
{
    private static SavedPick Pick(string name, string game = "poe2") =>
        new(name, DateTimeOffset.UnixEpoch,
            SnapshotCodec.CaptureCriteria(new WorkbenchState(), game), game);

    private static string Json(params SavedPick[] picks) =>
        JsonSerializer.Serialize(picks.ToList(), WebJsonContext.Default.ListSavedPick);

    [Fact]
    public async Task LoadSavedPicks_ReadsLegacyPicksWithoutDeletingTheKey()
    {
        using var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.JSInterop.Setup<string?>("localStorage.getItem", "poe-regex.saved-picks.v1").SetResult(null);
        ctx.JSInterop.Setup<string?>("localStorage.getItem", "poe-regex.recipes.v1").SetResult(Json(Pick("Legacy")));
        var service = new PersistenceService(ctx.JSInterop.JSRuntime);

        var loaded = await service.LoadSavedPicksAsync();

        loaded.Should().ContainSingle().Which.Name.Should().Be("Legacy");
        // Legacy keys are retained as the one-release fallback.
        ctx.JSInterop.VerifyNotInvoke("localStorage.setItem");
        ctx.JSInterop.VerifyNotInvoke("localStorage.removeItem");
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

    [Fact]
    public async Task ClearAsync_RemovesOnlyTheRequestedGame()
    {
        using var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        var service = new PersistenceService(ctx.JSInterop.JSRuntime);

        await service.ClearAsync("poe1");

        var call = ctx.JSInterop.Invocations
            .Single(i => i.Identifier == "localStorage.removeItem");
        call.Arguments.Should().ContainSingle()
            .Which.Should().Be("poe-regex.workbench.v2.poe1");
    }

    [Fact]
    public async Task LoadSavedPicks_KeepsGameListsSeparate()
    {
        using var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.JSInterop.Setup<string?>("localStorage.getItem", "poe-regex.saved-picks.v2.poe1")
            .SetResult(Json(Pick("Poe1", "poe1")));
        ctx.JSInterop.Setup<string?>("localStorage.getItem", "poe-regex.saved-picks.v2.poe2")
            .SetResult(Json(Pick("Poe2", "poe2")));
        var service = new PersistenceService(ctx.JSInterop.JSRuntime);

        (await service.LoadSavedPicksAsync("poe1")).Should().ContainSingle()
            .Which.Name.Should().Be("Poe1");
        (await service.LoadSavedPicksAsync("poe2")).Should().ContainSingle()
            .Which.Name.Should().Be("Poe2");
    }

    [Fact]
    public async Task LoadSavedPicks_DropsCrossGameEntries()
    {
        using var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        var mismatched = new SavedPick("Bad", DateTimeOffset.UnixEpoch,
            SnapshotCodec.CaptureCriteria(new WorkbenchState(), "poe2"), "poe1");
        ctx.JSInterop.Setup<string?>("localStorage.getItem", "poe-regex.saved-picks.v2.poe1")
            .SetResult(Json(mismatched));
        var service = new PersistenceService(ctx.JSInterop.JSRuntime);

        (await service.LoadSavedPicksAsync("poe1")).Should().BeEmpty();
    }
}

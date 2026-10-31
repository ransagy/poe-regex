using Bunit;
using Microsoft.Extensions.DependencyInjection;
using PoeRegex.Web.Core;
using web.Components;
using web.Layout;
using web.Services;

namespace PoeRegex.Web.Tests;

public class WebComponentTests
{
    private static BunitContext Context()
    {
        var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton(new EngineService());
        ctx.Services.AddSingleton(new PersistenceService(ctx.JSInterop.JSRuntime));
        ctx.Services.AddSingleton(new PreferencesState(ctx.JSInterop.JSRuntime));
        return ctx;
    }

    private static (EngineService Engine, WorkbenchState State, SearchModel Model) Picked()
    {
        var engine = new EngineService();
        var state = new WorkbenchState();
        state.AddInclude(null, new WantedMod("IncreasedLife6"));
        state.AddInclude(null, new WantedMod("Strength1"));
        var model = new SearchModel(engine, state);
        model.Update(engine.CompilePicks(state.Global.Included, [], false, null), null);
        return (engine, state, model);
    }

    [Fact]
    public void SearchModel_MapsSimplePicks()
    {
        var (engine, state, model) = Picked();
        model.PickBlock(null, state.Global.Included[0]).Should().Be(-1, "any-of picks share one block");

        state.SetMatchAll(null, true);
        model.Update(engine.CompilePicks(state.Global.Included, [], true, null), null);
        model.Query.Should().NotBeEmpty();
        model.Blocks.Should().Contain(b => b.Role == "inc");
        model.PickBlock(null, state.Global.Included[0]).Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void PickBreakdown_UsesUniqueNamePickIdsAndTogglesAllOf()
    {
        using var ctx = Context();
        var engine = ctx.Services.GetRequiredService<EngineService>();
        var state = new WorkbenchState();
        state.AddInclude(null, new WantedMod("ColdDamagePrefixOnWeapon4", ["Snowy"]));
        state.AddInclude(null, new WantedMod("ColdDamagePrefixOnWeapon4", ["Hailing"]));
        var model = new SearchModel(engine, state);
        model.Update(engine.CompilePicks(state.Global.Included, [], false, null), null);

        var cut = ctx.Render<PickBreakdown>(p => p
            .Add(x => x.Model, model)
            .Add(x => x.State, state));

        var ids = cut.FindAll(".bd-row.inc").Select(e => e.Id).ToList();
        ids.Should().HaveCount(2);
        ids.Should().OnlyHaveUniqueItems();
        cut.Find(".bd-row.inc .bd-n").TextContent.Should().Contain("chars");
        cut.Find("#logic-all-global").Click();
        state.Global.MatchAll.Should().BeTrue();
    }

    [Fact]
    public void SearchBar_RendersQueryAndCopyControl()
    {
        using var ctx = Context();
        var (_, state, model) = Picked();
        var cut = ctx.Render<SearchBar>(p => p
            .Add(x => x.Model, model)
            .Add(x => x.State, state));

        cut.Find("#slot-query-simple").TextContent.Should().Contain("\\+.*ife");
        cut.Find("#slot-copy-simple").Should().NotBeNull();
        cut.FindAll("#auto-copy").Should().BeEmpty(); // moved into the header Options popover
    }

    [Fact]
    public void IntroPanel_ExplainsExactAndShortStrings()
    {
        using var ctx = Context();
        var dismissed = false;
        var cut = ctx.Render<IntroPanel>(p => p
            .Add(x => x.OnDismiss, () => dismissed = true));

        cut.Find(".intro-title").TextContent.Should().Be("What is this?");
        cut.FindAll(".intro-goals li").Should().HaveCount(2);
        cut.Find("#intro-dismiss").Click();
        dismissed.Should().BeTrue();
    }

    [Fact]
    public void MainLayout_OffersEveryThemeAndPersistsTheChoice()
    {
        using var ctx = Context();
        var prefs = new PreferencesState(ctx.JSInterop.JSRuntime);
        ctx.Services.AddSingleton(prefs);
        ctx.Services.AddSingleton(new IntroState(new PersistenceService(ctx.JSInterop.JSRuntime)));
        var cut = ctx.Render<MainLayout>(p => p
            .Add(x => x.Body, b => b.AddMarkupContent(0, "<p>body</p>")));

        cut.Find("#options-open").Click();

        var options = cut.FindAll("#theme-select option");
        options.Select(o => o.GetAttribute("value"))
            .Should().ContainInOrder("forge", "bench-amber", "system");
        cut.Find("#scale-select").Should().NotBeNull();

        cut.Find("#theme-select").Change("system");

        var set = ctx.JSInterop.Invocations.Last(i => i.Identifier == "poeRegexTheme.set");
        set.Arguments.Should().ContainSingle().Which.Should().Be("system");

        cut.Find("#auto-copy-on").Click();
        prefs.AutoCopy.Should().BeTrue();

        cut.Find(".options-wrap").KeyDown("Escape");
        cut.WaitForAssertion(() => cut.FindAll("#options-pop").Should().BeEmpty());
    }

    [Fact]
    public void PaperDoll_HasNoPerSlotClearButtons()
    {
        using var ctx = Context();
        var state = new WorkbenchState();
        state.AddInclude("helmet", new WantedMod("IncreasedLife6"));
        var cut = ctx.Render<PaperDoll>(p => p.Add(x => x.State, state));

        cut.FindAll(".slot-clear").Should().BeEmpty("clearing lives in Your picks");
        cut.Find("#slot-helmet").Should().NotBeNull();
    }

    [Fact]
    public void ModPicker_HintsAtCommonPicksAndCanShowAll()
    {
        using var ctx = Context();
        var engine = ctx.Services.GetRequiredService<EngineService>();
        var state = new WorkbenchState();
        var cut = ctx.Render<ModPicker>(p => p
            .Add(x => x.State, state)
            .Add(x => x.Mods, engine.PlayableMods()));

        cut.Find("#mod-search").GetAttribute("placeholder").Should().Be("Search mods…");
        cut.Find("#mod-show-all").TextContent.Should().Contain("Show all");
        cut.Find("#mod-show-all").Click();
        cut.WaitForAssertion(() => cut.Find("#mod-show-all").TextContent.Should().Contain("Show common only"));
    }

    [Fact]
    public void RecipeMenu_OpensAccessibleDialogAndSaves()
    {
        using var ctx = Context();
        var state = new WorkbenchState();
        state.AddInclude(null, new WantedMod("IncreasedLife6"));
        var cut = ctx.Render<RecipeMenu>(p => p.Add(x => x.State, state));

        cut.Find("#recipes-open").Click();
        cut.WaitForAssertion(() => cut.Find(".recipes-pop"));
        var dialog = cut.Find(".recipes-pop");
        dialog.GetAttribute("role").Should().Be("dialog");
        dialog.GetAttribute("aria-modal").Should().Be("true");
        cut.Find("#recipe-name").GetAttribute("maxlength").Should().Be("100");
        cut.Find("#share-link").HasAttribute("disabled").Should().BeFalse();

        cut.Find("#recipe-name").Input("Round trip");
        cut.Find("form").Submit();
        cut.WaitForAssertion(() => cut.FindAll(".recipe-name").Any(e => e.TextContent == "Round trip"));
    }
}

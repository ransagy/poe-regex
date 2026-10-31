using Bunit;
using Microsoft.AspNetCore.Components.Web;
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
    public async Task QueryImportPanel_ParsesAndAppliesASimpleString()
    {
        using var ctx = Context();
        var engine = ctx.Services.GetRequiredService<EngineService>();
        var state = new WorkbenchState();
        var compiled = engine.CompilePicks([new WantedMod("IncreasedLife6")], [], false, null);
        var closed = false;

        var cut = ctx.Render<QueryImportPanel>(p => p
            .Add(x => x.State, state)
            .Add(x => x.Open, true)
            .Add(x => x.OnClose, () => closed = true));

        cut.Find("#query-import-text").Input(compiled.Query);
        await cut.Find("#query-import-parse").ClickAsync(new MouseEventArgs());
        cut.WaitForElement("#query-import-apply:not([disabled])");
        await cut.Find("#query-import-apply").ClickAsync(new MouseEventArgs());

        closed.Should().BeTrue();
        state.Mode.Should().Be("simple");
        state.Global.Included.Should().ContainSingle();
        engine.CompilePicks(state.Global.Included, state.Global.Excluded, state.Global.MatchAll, null)
            .Query.Should().Be(compiled.Query);
    }

    [Fact]
    public async Task QueryImportPanel_AppliesAMultiClassPaste()
    {
        using var ctx = Context();
        var engine = ctx.Services.GetRequiredService<EngineService>();
        var state = new WorkbenchState();
        var combined = engine.Combine(
        [
            ("weapon", "Crossbow", new[] { ClassFamilyPick(engine, "Crossbow") }),
            ("offhand", "Shield", new[] { ClassFamilyPick(engine, "Shield") }),
        ], []);
        var closed = false;

        var cut = ctx.Render<QueryImportPanel>(p => p
            .Add(x => x.State, state)
            .Add(x => x.Open, true)
            .Add(x => x.OnClose, () => closed = true));

        cut.Find("#query-import-text").Input(combined.Query);
        await cut.Find("#query-import-parse").ClickAsync(new MouseEventArgs());
        cut.WaitForElement("#query-import-apply:not([disabled])");
        await cut.Find("#query-import-apply").ClickAsync(new MouseEventArgs());

        closed.Should().BeTrue();
        state.Mode.Should().Be("advanced");
        state.ActiveSlots().Should().HaveCount(2);
        var recompiled = engine.Combine(
            state.ActiveSlots().Select(s => (s, state.CriteriaFor(s).ClassId,
                (IEnumerable<WantedMod>)state.CriteriaFor(s).Included,
                state.CriteriaFor(s).MatchAll)),
            state.Global.Excluded, allowOverflow: true).Query;
        recompiled.Should().Be(combined.Query);
    }

    [Fact]
    public async Task QueryImportPanel_PreviewsExactlyTheAppliedPicks()
    {
        using var ctx = Context();
        var engine = ctx.Services.GetRequiredService<EngineService>();
        var state = new WorkbenchState();
        const string query =
            "\"Blasting|Carbonising|Cremating|Flaming|Incinerating|Scorching|\\+.*ife|^\\+.*cc|o Int|o cr.*ha\"";

        var cut = ctx.Render<QueryImportPanel>(p => p
            .Add(x => x.State, state)
            .Add(x => x.Open, true)
            .Add(x => x.OnClose, () => { }));

        cut.Find("#query-import-text").Input(query);
        await cut.Find("#query-import-parse").ClickAsync(new MouseEventArgs());
        cut.WaitForElement("#query-import-preview");

        // The preview is read-only and uses the real Your-picks rows.
        state.Global.Included.Should().BeEmpty("nothing is applied until Apply");
        var preview = cut.Find("#query-import-preview");
        preview.TextContent.Should().Contain("\"Flaming\" or better");
        preview.TextContent.Should().Contain("Adds 20–24 to 32–37 Fire Damage");
        preview.TextContent.Should().NotContain("name:");
        cut.FindAll("#query-import-preview .bd-row.inc").Count.Should().Be(5);
        cut.FindAll("#query-import-preview .bd-x").Should().BeEmpty();
        cut.FindAll("#query-import-preview .bd-min").Should().BeEmpty();
        cut.FindAll("#query-import-preview .bd-clear").Should().BeEmpty();

        await cut.Find("#query-import-apply").ClickAsync(new MouseEventArgs());

        state.Global.Included.Should().HaveCount(5);
        engine.CompilePicks(state.Global.Included, state.Global.Excluded, state.Global.MatchAll, null)
            .Query.Should().Be(query);
    }

    private static WantedMod ClassFamilyPick(EngineService engine, string classId)
    {
        var family = ModFamilies.Group(engine.ModsForClasses([classId]))
            .First(f => f.Tiers.Count >= 2 && f.DisplayLines.Count > 0);
        return family.Pick();
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
    public void IntroPanel_IdSuffixKeepsRenderedCopiesUnique()
    {
        using var ctx = Context();
        var cut = ctx.Render<IntroPanel>(p => p.Add(x => x.IdSuffix, "-top"));

        cut.Find("#intro-card-top").Should().NotBeNull();
        cut.Find("#intro-dismiss-top").Should().NotBeNull();
        cut.FindAll("#intro-card").Should().BeEmpty();
        cut.FindAll("#intro-dismiss").Should().BeEmpty();
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

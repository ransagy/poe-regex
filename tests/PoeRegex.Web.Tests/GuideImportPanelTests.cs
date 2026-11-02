using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using PoeRegex.Web.Core;
using web.Components;
using web.Services;

namespace PoeRegex.Web.Tests;

public class GuideImportPanelTests
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

    [Fact]
    public async Task GuideMode_PreviewsAndAppliesMappedBranches()
    {
        using var ctx = Context();
        var state = new WorkbenchState();
        var closed = false;

        var cut = ctx.Render<QueryImportPanel>(p => p
            .Add(x => x.State, state)
            .Add(x => x.Open, true)
            .Add(x => x.OnClose, () => closed = true));

        cut.Find("#query-import-mode-guide").Click();
        cut.Find("#query-import-text").Input("maximum Life|of the Bear|zzzz");
        await cut.Find("#query-import-parse").ClickAsync(new MouseEventArgs());
        cut.WaitForElement("#query-import-reconcile");

        cut.Find(".panel-head").TextContent.Should().Be("Import string");
        cut.Find("#query-import-guide-note").TextContent.Should().Contain("Best-effort");
        cut.Find("#query-import-summary").TextContent.Should().Contain("2 of 3");
        cut.Find("#query-import-summary").TextContent.Should().Contain("1 dropped");
        cut.Find("#query-import-reconcile").TagName.Should().Be("DIV");
        cut.Find("#query-import-dropped").TagName.Should().Be("DIV");
        cut.Find("#query-import-dropped").TextContent.Should().Contain("zzzz");
        cut.FindAll("#query-import-preview .bd-row.inc").Count.Should().Be(2);

        await cut.Find("#query-import-apply").ClickAsync(new MouseEventArgs());

        closed.Should().BeTrue();
        state.Mode.Should().Be("simple");
        state.Global.Included.Should().HaveCount(2);
    }

    [Fact]
    public async Task GuideMode_CandidateSelectReimports()
    {
        using var ctx = Context();
        var state = new WorkbenchState();

        var cut = ctx.Render<QueryImportPanel>(p => p
            .Add(x => x.State, state)
            .Add(x => x.Open, true)
            .Add(x => x.OnClose, () => { }));

        cut.Find("#query-import-mode-guide").Click();
        cut.Find("#query-import-text").Input("life");
        await cut.Find("#query-import-parse").ClickAsync(new MouseEventArgs());
        cut.WaitForElement("#guide-branch-0");

        cut.Find("#guide-branch-0").Click();
        cut.WaitForElement("#guide-pick-pop-0");
        cut.FindAll("#guide-pick-pop-0 input[type=checkbox]").Count.Should().BeGreaterThan(1);
        cut.Find("#guide-branch-0").TextContent.Should().Contain("picked out of");

        cut.Find("#guide-pick-pop-0 input[value='LifeGainPerTarget']").Change(true);
        cut.WaitForAssertion(() =>
            cut.Find("#guide-branch-0").GetAttribute("data-selected")
                .Should().Contain("LifeGainPerTarget"));
        cut.FindAll("#guide-pick-pop-0 input[type=checkbox]").Should().NotBeEmpty();
    }

    [Fact]
    public async Task GuideMode_AppliesExclusionGroup()
    {
        using var ctx = Context();
        var state = new WorkbenchState();
        var closed = false;

        var cut = ctx.Render<QueryImportPanel>(p => p
            .Add(x => x.State, state)
            .Add(x => x.Open, true)
            .Add(x => x.OnClose, () => closed = true));

        cut.Find("#query-import-mode-guide").Click();
        cut.Find("#query-import-text").Input("\"!parry|bow\" \"max.*life\"");
        await cut.Find("#query-import-parse").ClickAsync(new MouseEventArgs());
        cut.WaitForElement("#query-import-exclusions");

        cut.Find("#query-import-imported").TextContent.Should().Contain("None of");
        cut.Find("#query-import-exclusions").TextContent.Should().Contain("parry|bow");
        cut.Find("#query-import-exclusions").TextContent.Should().NotContain("Imported");
        cut.FindAll("#query-import-preview .bd-row.imported.exc").Count.Should().Be(1);
        cut.FindAll("#query-import-preview .bd-tag").Should().BeEmpty();

        await cut.Find("#query-import-apply").ClickAsync(new MouseEventArgs());

        closed.Should().BeTrue();
        state.Global.Excluded.Should().Contain(RawExclusion.Encode("parry|bow"));
    }

    [Fact]
    public async Task GuideMode_AppliesExternalClassFilter()
    {
        using var ctx = Context();
        var state = new WorkbenchState();
        var closed = false;

        var cut = ctx.Render<QueryImportPanel>(p => p
            .Add(x => x.State, state)
            .Add(x => x.Open, true)
            .Add(x => x.OnClose, () => closed = true));

        cut.Find("#query-import-mode-guide").Click();
        cut.Find("#query-import-text").Input("\"s: (am|ri|be|Cross)\"");
        await cut.Find("#query-import-parse").ClickAsync(new MouseEventArgs());
        cut.WaitForElement("#query-import-external");

        cut.Find("#query-import-imported").TextContent.Should().Contain("Must match");
        cut.Find("#query-import-external").TextContent.Should().Contain("Item Class: (am|ri|be|Cross)");
        cut.Find("#query-import-external").TextContent.Should().NotContain("Imported");
        cut.FindAll("#query-import-preview .bd-row.imported").Count.Should().Be(1);
        cut.FindAll("#query-import-preview .bd-tag").Should().BeEmpty();

        await cut.Find("#query-import-apply").ClickAsync(new MouseEventArgs());

        closed.Should().BeTrue();
        state.ExternalIncludes.Should().Contain("Item Class: (am|ri|be|Cross)");
    }

    [Fact]
    public async Task ImportedIncludesAndExclusionsShareOneSection()
    {
        using var ctx = Context();
        var state = new WorkbenchState();

        var cut = ctx.Render<QueryImportPanel>(p => p
            .Add(x => x.State, state)
            .Add(x => x.Open, true)
            .Add(x => x.OnClose, () => { }));

        cut.Find("#query-import-mode-guide").Click();
        cut.Find("#query-import-text").Input(
            "\"!parry|bow\" \"s: (am|ri|be|Cross)|max.*life\"");
        await cut.Find("#query-import-parse").ClickAsync(new MouseEventArgs());
        cut.WaitForElement("#query-import-imported");

        var imported = cut.Find("#query-import-imported");
        imported.TextContent.Should().Contain("Must match");
        imported.TextContent.Should().Contain("None of");
        imported.TextContent.Should().Contain("Item Class: (am|ri|be|Cross)");
        imported.TextContent.Should().Contain("parry|bow");

        cut.FindAll("#query-import-preview .bd-row.imported").Count.Should().Be(2);
        cut.FindAll("#query-import-preview .bd-row.imported.exc").Count.Should().Be(1);
        cut.FindAll("#query-import-preview .bd-tag").Should().BeEmpty();
    }

    [Fact]
    public async Task GuideMode_NativeMultiSelectReimports()
    {
        using var ctx = Context();
        var state = new WorkbenchState();

        var cut = ctx.Render<QueryImportPanel>(p => p
            .Add(x => x.State, state)
            .Add(x => x.Open, true)
            .Add(x => x.OnClose, () => { }));

        cut.Find("#query-import-mode-guide").Click();
        cut.Find("#query-import-text").Input("life");
        await cut.Find("#query-import-parse").ClickAsync(new MouseEventArgs());
        cut.WaitForElement("#guide-branch-native-0");

        cut.Find("#guide-branch-native-0").Change(new ChangeEventArgs { Value = new[] { "LifeGainPerTarget" } });
        cut.WaitForAssertion(() =>
            cut.Find("#guide-branch-0").GetAttribute("data-selected")
                .Should().Contain("LifeGainPerTarget"));
    }

    [Fact]
    public async Task ClosingClearsTheDialogForTheNextOpening()
    {
        using var ctx = Context();
        var state = new WorkbenchState();
        var cut = ctx.Render<QueryImportPanel>(p => p
            .Add(x => x.State, state)
            .Add(x => x.Open, true)
            .Add(x => x.OnClose, () => { }));

        cut.Find("#query-import-mode-guide").Click();
        cut.Find("#query-import-text").Input("maximum Life|zzzz");
        await cut.Find("#query-import-parse").ClickAsync(new MouseEventArgs());
        cut.WaitForElement("#query-import-summary");

        await cut.Find("#query-import-cancel").ClickAsync(new MouseEventArgs());

        cut.Find("#query-import-text").GetAttribute("value").Should().BeNullOrEmpty();
        cut.Find("#query-import-mode-app").GetAttribute("aria-pressed").Should().Be("true");
        cut.FindAll("#query-import-summary").Should().BeEmpty();
        cut.FindAll("#query-import-guide-note").Should().BeEmpty();

        // Reopening renders this same reset state; the component keeps its
        // caches but no user-visible text, mode, or preview.
    }
}

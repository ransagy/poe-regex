using Bunit;
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

        cut.Find("#query-import-summary").TextContent.Should().Contain("2 of 3");
        cut.Find("#query-import-summary").TextContent.Should().Contain("1 dropped");
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

        cut.FindAll("#guide-branch-0 option").Count.Should().BeGreaterThan(1);

        cut.Find("#guide-branch-0").Change("LifeGainPerTarget");
        cut.WaitForElement("#guide-branch-0");
        cut.WaitForAssertion(() =>
            cut.Find("#guide-branch-0").GetAttribute("data-selected")
                .Should().Contain("LifeGainPerTarget"));
    }
}

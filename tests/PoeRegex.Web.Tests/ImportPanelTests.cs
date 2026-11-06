using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using PoeRegex.Web.Core;
using web.Components;

namespace PoeRegex.Web.Tests;

/// <summary>Item import preview annotations for modeled source tags.</summary>
public class ImportPanelTests
{
    private static BunitContext Context()
    {
        var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton(new EngineService());
        return ctx;
    }

    [Fact]
    public async Task DesecratedAnnotation_PutsTheFactionTagAfterTheName()
    {
        using var ctx = Context();
        var cut = ctx.Render<ImportPanel>(p => p
            .Add(x => x.State, new WorkbenchState())
            .Add(x => x.Open, true)
            .Add(x => x.OnClose, EventCallback.Empty));

        cut.Find("#import-text").Input(FixtureTexts.DesecratedRing);
        await cut.Find("#import-parse").ClickAsync(new MouseEventArgs());

        var annotation = cut.Find(".iprev-annot.map-one");
        annotation.TextContent.Trim().Should().Be("of Ulaman");

        var tag = annotation.NextElementSibling as IElement;
        tag.Should().NotBeNull("the source tag follows the resolved mod name");
        tag!.ClassList.Should().Contain("mod-source");
        tag.TextContent.Trim().Should().Be("Ulaman");
    }

    [Fact]
    public async Task CraftedAnnotation_PutsTheCraftedTagAfterTheName()
    {
        using var ctx = Context();
        var cut = ctx.Render<ImportPanel>(p => p
            .Add(x => x.State, new WorkbenchState())
            .Add(x => x.Open, true)
            .Add(x => x.OnClose, EventCallback.Empty));

        cut.Find("#import-text").Input(FixtureTexts.CraftedHelmet);
        await cut.Find("#import-parse").ClickAsync(new MouseEventArgs());

        var annotation = cut.Find(".iprev-annot.map-one");
        annotation.TextContent.Trim().Should().Be("of Blurring");

        var tag = annotation.NextElementSibling as IElement;
        tag.Should().NotBeNull("the source tag follows the resolved mod name");
        tag!.ClassList.Should().Contain("mod-source");
        tag.ClassList.Should().Contain("source-crafted");
        tag.TextContent.Trim().Should().Be("Crafted");
    }
}

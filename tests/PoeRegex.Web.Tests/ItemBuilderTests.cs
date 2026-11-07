using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using PoeRegex.Web.Core;
using web.Components;

namespace PoeRegex.Web.Tests;

/// <summary>Item-builder labels for desecrated tiers.</summary>
public class ItemBuilderTests
{
    private static BunitContext Context()
    {
        var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton(new EngineService());
        return ctx;
    }

    [Fact]
    public void DesecratedTier_ShowsItsRangeAndKeepsTheSourceOnTheFamilyRow()
    {
        using var ctx = Context();
        var cut = ctx.Render<ItemBuilder>(p => p
            .Add(x => x.State, new WorkbenchState())
            .Add(x => x.Open, true)
            .Add(x => x.OnClose, EventCallback.Empty));

        cut.Find("#builder-class").Change("One Hand Mace");
        cut.Find("#builder-search").Input("Break Armour");

        var tier = cut.FindAll(".tier-row")
            .Single(r => r.TextContent.Contains("Break Armour", StringComparison.Ordinal));
        tier.QuerySelector(".tier-name")!.TextContent.Trim().Should().Be("2–4%");
        tier.QuerySelectorAll(".mod-source").Should().BeEmpty(
            "the source tag belongs on the family row, not the tier rows");

        cut.Find(".fam-row .mod-source").TextContent.Trim().Should().Be("Amanamu");
    }

    [Fact]
    public void ImplicitTier_ShowsImplicitKindAndCanBeAdded()
    {
        using var ctx = Context();
        var cut = ctx.Render<ItemBuilder>(p => p
            .Add(x => x.State, new WorkbenchState())
            .Add(x => x.Open, true)
            .Add(x => x.OnClose, EventCallback.Empty));

        cut.Find("#builder-class").Change("One Hand Mace");
        cut.Find("#builder-search").Input("Always Hits");

        var row = cut.Find(".fam-row");
        row.QuerySelector(".gen-tag")!.TextContent.Trim().Should().Be("Implicit");
        row.QuerySelector(".mod-source")!.TextContent.Trim().Should().Be("Implicit");
        var add = cut.Find("#builder-add-MaceImplicitAlwaysHit1");
        add.HasAttribute("disabled").Should().BeFalse();
        add.Click();
        cut.Find("#builder-preview").TextContent.Should().Contain("Implicit Modifier");
    }
}

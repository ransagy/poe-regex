using AngleSharp.Dom;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using PoeRegex.Core.Data;
using PoeRegex.Web.Core;
using web.Components;
using web.Services;

namespace PoeRegex.Web.Tests;

/// <summary>
/// Mod/family pick highlighting in the picker. Family grouping is
/// scope-dependent, so an imported or global family pick can carry a
/// different anchor and variant set than the slot-scoped family; identity
/// must fall back to family membership/key rather than exact
/// <see cref="WantedMod.SamePick"/> equality.
/// </summary>
public class ModPickerHighlightTests
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

    private static ModFamily Family(IReadOnlyList<ModEntry> mods, string key) =>
        ModFamilies.Group(mods).First(f => f.Key == key);

    private static IRenderedComponent<ModPicker> Render(
        BunitContext ctx, WorkbenchState state, IReadOnlyList<ModEntry> mods, string? scope = null) =>
        ctx.Render<ModPicker>(p =>
        {
            p.Add(x => x.State, state).Add(x => x.Mods, mods);
            if (scope is not null) p.Add(x => x.Scope, scope);
        });

    private static IElement FamilyRow(IRenderedComponent<ModPicker> cut, ModFamily fam) =>
        cut.Find($"#fam-toggle-{fam.Anchor.Id}").ParentElement!;

    private static IElement TierRow(IRenderedComponent<ModPicker> cut, ModTier tier) =>
        cut.Find($"#pick-tier-{tier.Anchor.Id}").ParentElement!;

    [Fact]
    public void PlainFamilyPick_HighlightsFamilyRow_InGlobalMode()
    {
        using var ctx = Context();
        var engine = ctx.Services.GetRequiredService<EngineService>();
        var mods = engine.PlayableMods();
        var state = new WorkbenchState();
        var family = Family(mods, "IncreasedLife");
        state.AddInclude(null, family.Pick());

        var cut = Render(ctx, state, mods);

        FamilyRow(cut, family).ClassList.Should().Contain("picked");
        cut.Find($"#pick-inc-{family.Anchor.Id}").HasAttribute("disabled").Should().BeTrue();
        cut.Find($"#pick-inc-{family.Anchor.Id}").TextContent.Should().Be("✓");
    }

    [Fact]
    public void PlainFamilyPick_HighlightsFamilyRow_InAdvancedSlot()
    {
        using var ctx = Context();
        var engine = ctx.Services.GetRequiredService<EngineService>();
        var mods = engine.ModsForClass("Helmet");
        var state = new WorkbenchState();
        state.SetMode("advanced");
        state.SelectSlot("helmet");
        var family = Family(mods, "IncreasedLife");
        state.AddInclude("helmet", family.Pick());

        var cut = Render(ctx, state, mods, "helmet");

        FamilyRow(cut, family).ClassList.Should().Contain("picked");
        cut.Find($"#pick-inc-{family.Anchor.Id}").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void ImportedFamilyPick_WithOtherScopeVariants_StillHighlightsFamilyRow()
    {
        using var ctx = Context();
        var engine = ctx.Services.GetRequiredService<EngineService>();
        var mods = engine.ModsForClass("Helmet");
        var state = new WorkbenchState();
        state.SetMode("advanced");
        state.SelectSlot("helmet");

        var globalFamily = Family(engine.PlayableMods(), "IncreasedLife");
        var slotFamily = Family(mods, "IncreasedLife");
        // Imported picks are built from the global family, then re-anchored to
        // a member that is legal in the slot (QueryReconstruction.FamilyPickFor).
        var imported = globalFamily.Pick() with { ModId = slotFamily.Anchor.Id };
        slotFamily.Pick().SamePick(imported).Should().BeFalse(
            "the global variant list differs from the slot-scoped one; exact equality was the bug");
        state.AddInclude("helmet", imported);

        var cut = Render(ctx, state, mods, "helmet");

        FamilyRow(cut, slotFamily).ClassList.Should().Contain("picked",
            "family-key membership, not variant-list equality, decides the highlight");
    }

    [Fact]
    public void TierOrBetterNamePick_HighlightsCoveredTierRowsAndPickedCount()
    {
        using var ctx = Context();
        var engine = ctx.Services.GetRequiredService<EngineService>();
        var mods = engine.ModsForClass("Helmet");
        var state = new WorkbenchState();
        state.SetMode("advanced");
        state.SelectSlot("helmet");
        var family = Family(mods, "IncreasedLife");
        var tier = family.Tiers[family.Tiers.Count / 2];
        var names = family.NamesFrom(tier);
        state.AddInclude("helmet", new WantedMod(tier.Anchor.Id, names));

        var cut = Render(ctx, state, mods, "helmet");
        cut.Find($"#fam-toggle-{family.Anchor.Id}").Click();

        var covered = family.Tiers
            .Where(t => names.Contains(t.Name, StringComparer.Ordinal))
            .ToList();
        covered.Should().HaveCountGreaterThan(1, "a middle tier has better tiers above it");

        var row = FamilyRow(cut, family);
        row.ClassList.Should().Contain("picked");
        row.TextContent.Should().Contain($"{covered.Count} picked");

        foreach (var t in family.Tiers)
        {
            var tierClass = TierRow(cut, t).ClassList;
            if (covered.Contains(t))
                tierClass.Should().Contain("picked", $"\"{t.Name}\" is in the picked name set");
            else
                tierClass.Should().NotContain("picked", $"\"{t.Name}\" is a worse tier");
        }
    }

    [Fact]
    public void PlainTierAnchorPick_HighlightsTierRowAndCountsIt()
    {
        using var ctx = Context();
        var engine = ctx.Services.GetRequiredService<EngineService>();
        var mods = engine.ModsForClass("Helmet");
        var state = new WorkbenchState();
        state.SetMode("advanced");
        state.SelectSlot("helmet");
        var family = Family(mods, "IncreasedLife");
        var tier = family.Tiers[family.Tiers.Count / 2];
        // A fragment import can produce a plain tier-anchor pick (no names,
        // no family variants); it must still light up its own tier row.
        state.AddInclude("helmet", new WantedMod(tier.Anchor.Id));

        var cut = Render(ctx, state, mods, "helmet");
        cut.Find($"#fam-toggle-{family.Anchor.Id}").Click();

        TierRow(cut, tier).ClassList.Should().Contain("picked");
        var row = FamilyRow(cut, family);
        row.ClassList.Should().Contain("picked");
        row.TextContent.Should().Contain("1 picked");

        foreach (var other in family.Tiers.Where(t => t != tier))
            TierRow(cut, other).ClassList.Should().NotContain("picked",
                "a single tier anchor does not select the rest of the family");
    }
}

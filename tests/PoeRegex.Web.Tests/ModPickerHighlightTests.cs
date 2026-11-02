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
        var include = cut.Find($"#pick-inc-{family.Anchor.Id}");
        include.HasAttribute("disabled").Should().BeFalse("selected buttons stay clickable");
        include.GetAttribute("aria-pressed").Should().Be("true");
        include.ClassList.Should().Contain("active");
        include.TextContent.Should().Be("✓");
        FamilyRow(cut, family).TextContent.Should().NotContain("✓ Included",
            "the family row's picked count replaces the redundant tag");
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
        var include = cut.Find($"#pick-inc-{family.Anchor.Id}");
        include.HasAttribute("disabled").Should().BeFalse();
        include.GetAttribute("aria-pressed").Should().Be("true");
        include.TextContent.Should().Be("✓");
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

        var upButton = cut.Find($"#pick-tier-up-{tier.Anchor.Id}");
        upButton.GetAttribute("aria-pressed").Should().Be("true");
        upButton.ClassList.Should().Contain("active");
        upButton.TextContent.Should().Be("✓");
        upButton.HasAttribute("disabled").Should().BeFalse();

        foreach (var t in family.Tiers)
        {
            var tierRow = TierRow(cut, t);
            if (covered.Contains(t))
            {
                tierRow.ClassList.Should().Contain("covered");
                tierRow.ClassList.Should().NotContain("picked",
                    "or-better tiers are covered, not manually picked");
                tierRow.TextContent.Should().NotContain("✓ Included");
            }
            else
            {
                tierRow.ClassList.Should().NotContain("covered");
                tierRow.ClassList.Should().NotContain("picked");
            }
        }

        TierRow(cut, tier).TextContent.Should().Contain("↑ Or better");
        TierRow(cut, covered[^1]).TextContent.Should().Contain("↑ Covered");
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

    private static (EngineService Engine, IReadOnlyList<ModEntry> Mods, WorkbenchState State)
        AdvancedPickler(BunitContext ctx, out ModFamily family)
    {
        var engine = ctx.Services.GetRequiredService<EngineService>();
        var mods = engine.ModsForClass("Helmet");
        var state = new WorkbenchState();
        state.SetMode("advanced");
        state.SelectSlot("helmet");
        family = Family(mods, "IncreasedLife");
        return (engine, mods, state);
    }

    private static void Expand(IRenderedComponent<ModPicker> cut, ModFamily family) =>
        cut.Find($"#fam-toggle-{family.Anchor.Id}").Click();

    [Fact]
    public void ExcludedFamily_IsMarkedAndExcludeButtonIsPressed()
    {
        using var ctx = Context();
        var (_, mods, state) = AdvancedPickler(ctx, out var family);
        state.AddExclude("helmet", family.Anchor.Id);

        var cut = Render(ctx, state, mods, "helmet");

        var row = FamilyRow(cut, family);
        row.ClassList.Should().Contain("excluded");
        row.TextContent.Should().Contain("⊘ Excluded", "state must not rely on color alone");

        var button = cut.Find($"#pick-exc-{family.Anchor.Id}");
        button.GetAttribute("aria-pressed").Should().Be("true");
        button.GetAttribute("aria-label").Should().Contain("Remove exclusion");
        button.ClassList.Should().Contain("active");
        button.TextContent.Should().Be("⊘");
        row.TextContent.Should().NotContain("✓ Included");
    }

    [Fact]
    public void ExcludedTier_IsMarkedAndDoesNotMarkItsFamily()
    {
        using var ctx = Context();
        var (_, mods, state) = AdvancedPickler(ctx, out var family);
        var tier = family.Tiers[family.Tiers.Count / 2];
        state.AddExclude("helmet", tier.Name);

        var cut = Render(ctx, state, mods, "helmet");
        Expand(cut, family);

        var row = TierRow(cut, tier);
        row.ClassList.Should().Contain("excluded");
        row.TextContent.Should().Contain("⊘ Excluded");

        FamilyRow(cut, family).ClassList.Should().NotContain("excluded",
            "a tier name exclusion is not a family exclusion");

        var button = cut.Find($"#pick-tier-exc-{tier.Anchor.Id}");
        button.GetAttribute("aria-pressed").Should().Be("true");
        button.GetAttribute("aria-label").Should().Contain("Remove exclusion");
        button.ClassList.Should().Contain("active");
        button.TextContent.Should().Be("⊘");
    }

    [Fact]
    public void FamilyIncluded_TierExcluded_ShowsBothStatesAndCounts()
    {
        using var ctx = Context();
        var (_, mods, state) = AdvancedPickler(ctx, out var family);
        var tier = family.Tiers[family.Tiers.Count / 2];
        state.AddInclude("helmet", family.Pick());
        state.AddExclude("helmet", tier.Name);

        var cut = Render(ctx, state, mods, "helmet");
        Expand(cut, family);

        var familyRow = FamilyRow(cut, family);
        familyRow.ClassList.Should().Contain("picked");
        familyRow.TextContent.Should().NotContain("✓ Included",
            "the family picked count replaces the redundant tag");
        familyRow.TextContent.Should().Contain("1 excluded", "the family summary keeps both counts");
        cut.Find($"#pick-inc-{family.Anchor.Id}").GetAttribute("aria-pressed").Should().Be("true");

        var tierRow = TierRow(cut, tier);
        tierRow.ClassList.Should().Contain("excluded");
        tierRow.TextContent.Should().Contain("⊘ Excluded");
        tierRow.ClassList.Should().NotContain("picked",
            "a whole-family pick does not count as that tier being picked");
    }

    [Fact]
    public void FamilyExcluded_TierIncluded_ShowsBothStates()
    {
        using var ctx = Context();
        var (_, mods, state) = AdvancedPickler(ctx, out var family);
        var tier = family.Tiers[family.Tiers.Count / 2];
        state.AddExclude("helmet", family.Anchor.Id);
        state.AddInclude("helmet", new WantedMod(tier.Anchor.Id, [tier.Name]));

        var cut = Render(ctx, state, mods, "helmet");
        Expand(cut, family);

        var familyRow = FamilyRow(cut, family);
        familyRow.ClassList.Should().Contain("excluded");
        familyRow.ClassList.Should().Contain("picked");
        familyRow.TextContent.Should().Contain("⊘ Excluded");
        familyRow.TextContent.Should().Contain("1 picked");
        familyRow.TextContent.Should().NotContain("✓ Included");

        var tierRow = TierRow(cut, tier);
        tierRow.ClassList.Should().Contain("picked");
        tierRow.TextContent.Should().Contain("✓ Included");
        tierRow.ClassList.Should().NotContain("excluded");
    }

    [Fact]
    public void TierIncludedAndExcluded_ShowsBothBadges()
    {
        using var ctx = Context();
        var (_, mods, state) = AdvancedPickler(ctx, out var family);
        var tier = family.Tiers[family.Tiers.Count / 2];
        state.AddInclude("helmet", new WantedMod(tier.Anchor.Id, [tier.Name]));
        state.AddExclude("helmet", tier.Name);

        var cut = Render(ctx, state, mods, "helmet");
        Expand(cut, family);

        var row = TierRow(cut, tier);
        row.ClassList.Should().Contain("picked");
        row.ClassList.Should().Contain("excluded");
        row.TextContent.Should().Contain("✓ Included");
        row.TextContent.Should().Contain("⊘ Excluded");
        cut.Find($"#pick-tier-exc-{tier.Anchor.Id}").GetAttribute("aria-pressed").Should().Be("true");
        var exact = cut.Find($"#pick-tier-{tier.Anchor.Id}");
        exact.GetAttribute("aria-pressed").Should().Be("true");
        exact.HasAttribute("disabled").Should().BeFalse();
        exact.TextContent.Should().Be("✓");
    }

    [Fact]
    public void ExcludeButtons_ToggleTheirOwnExclusionOff()
    {
        using var ctx = Context();
        var (_, mods, state) = AdvancedPickler(ctx, out var family);
        var tier = family.Tiers[family.Tiers.Count / 2];
        state.AddExclude("helmet", family.Anchor.Id);
        state.AddExclude("helmet", tier.Name);

        var cut = Render(ctx, state, mods, "helmet");
        Expand(cut, family);

        cut.Find($"#pick-exc-{family.Anchor.Id}").Click();
        state.CriteriaFor("helmet").Excluded.Should().Equal([tier.Name],
            "the family toggle removes only the family exclusion");
        FamilyRow(cut, family).ClassList.Should().NotContain("excluded");
        cut.Find($"#pick-exc-{family.Anchor.Id}").GetAttribute("aria-pressed").Should().Be("false");

        cut.Find($"#pick-tier-exc-{tier.Anchor.Id}").Click();
        state.CriteriaFor("helmet").Excluded.Should().BeEmpty();
        TierRow(cut, tier).ClassList.Should().NotContain("excluded");
        cut.Find($"#pick-tier-exc-{tier.Anchor.Id}").GetAttribute("aria-pressed").Should().Be("false");
    }

    [Fact]
    public void FamilyIncludeAndExclude_AreMutuallyExclusive_AndToggleOff()
    {
        using var ctx = Context();
        var (_, mods, state) = AdvancedPickler(ctx, out var family);
        var crit = state.CriteriaFor("helmet");
        state.AddExclude("helmet", family.Anchor.Id);

        var cut = Render(ctx, state, mods, "helmet");
        var include = cut.Find($"#pick-inc-{family.Anchor.Id}");
        var exclude = cut.Find($"#pick-exc-{family.Anchor.Id}");

        include.Click(); // include replaces the family exclusion
        crit.Excluded.Should().BeEmpty();
        crit.Included.Should().Contain(pick => pick.ModId == family.Anchor.Id);
        include.GetAttribute("aria-pressed").Should().Be("true");
        exclude.GetAttribute("aria-pressed").Should().Be("false");

        include.Click(); // active selection toggles off
        crit.Included.Should().BeEmpty();
        include.GetAttribute("aria-pressed").Should().Be("false");

        include.Click();
        exclude.Click(); // exclude replaces the family inclusion
        crit.Excluded.Should().Contain(family.Anchor.Id);
        crit.Included.Should().BeEmpty();
        include.GetAttribute("aria-pressed").Should().Be("false");
        exclude.GetAttribute("aria-pressed").Should().Be("true");
    }

    [Fact]
    public void TierExactInclude_RemovesUpAndExclude_AndTogglesOff()
    {
        using var ctx = Context();
        var (_, mods, state) = AdvancedPickler(ctx, out var family);
        var tier = family.Tiers[family.Tiers.Count / 2];
        var crit = state.CriteriaFor("helmet");
        state.AddInclude("helmet", new WantedMod(tier.Anchor.Id, family.NamesFrom(tier)));
        state.AddExclude("helmet", tier.Name);

        var cut = Render(ctx, state, mods, "helmet");
        Expand(cut, family);
        var exact = cut.Find($"#pick-tier-{tier.Anchor.Id}");
        exact.Click();

        crit.Included.Should().ContainSingle(pick =>
            pick.IsNamePick && pick.Names!.Count == 1 && pick.Names[0] == tier.Name);
        crit.Excluded.Should().BeEmpty();
        exact.GetAttribute("aria-pressed").Should().Be("true");
        exact.TextContent.Should().Be("✓");
        cut.Find($"#pick-tier-up-{tier.Anchor.Id}").GetAttribute("aria-pressed").Should().Be("false");

        exact.Click(); // active selection toggles off
        crit.Included.Should().BeEmpty();
        exact.GetAttribute("aria-pressed").Should().Be("false");
    }

    [Fact]
    public void TierUp_RemovesExactAndExclude_AndMarksOnlyAnchorUpButton()
    {
        using var ctx = Context();
        var (_, mods, state) = AdvancedPickler(ctx, out var family);
        var tier = family.Tiers[family.Tiers.Count / 2];
        var covered = family.Tiers.Where(t => family.NamesFrom(tier).Contains(t.Name)).ToList();
        covered.Should().HaveCountGreaterThan(1);
        var crit = state.CriteriaFor("helmet");
        state.AddInclude("helmet", new WantedMod(tier.Anchor.Id, [tier.Name]));
        state.AddExclude("helmet", tier.Name);

        var cut = Render(ctx, state, mods, "helmet");
        Expand(cut, family);
        var up = cut.Find($"#pick-tier-up-{tier.Anchor.Id}");
        up.Click();

        crit.Included.Should().ContainSingle(pick =>
            pick.IsNamePick && pick.Names!.Count > 1 && pick.ModId == tier.Anchor.Id);
        crit.Excluded.Should().BeEmpty();
        up.GetAttribute("aria-pressed").Should().Be("true");
        up.ClassList.Should().Contain("active");
        up.TextContent.Should().Be("✓");
        up.HasAttribute("disabled").Should().BeFalse();
        cut.Find($"#pick-tier-{tier.Anchor.Id}").GetAttribute("aria-pressed").Should().Be("false");

        TierRow(cut, tier).ClassList.Should().Contain("covered");
        TierRow(cut, tier).ClassList.Should().NotContain("picked");
        TierRow(cut, tier).TextContent.Should().Contain("↑ Or better");
        TierRow(cut, covered[^1]).ClassList.Should().Contain("covered");
        TierRow(cut, covered[^1]).ClassList.Should().NotContain("picked");
        TierRow(cut, covered[^1]).TextContent.Should().Contain("↑ Covered");
        TierRow(cut, family.Tiers[0]).ClassList.Should().NotContain("covered",
            "tiers below the chosen one are not covered");

        up.Click(); // active selection toggles off
        crit.Included.Should().BeEmpty();
        TierRow(cut, tier).ClassList.Should().NotContain("covered");
    }

    [Fact]
    public void TierExclude_RemovesExactAndUp()
    {
        using var ctx = Context();
        var (_, mods, state) = AdvancedPickler(ctx, out var family);
        var tier = family.Tiers[family.Tiers.Count / 2];
        var crit = state.CriteriaFor("helmet");
        state.AddInclude("helmet", new WantedMod(tier.Anchor.Id, [tier.Name]));
        state.AddInclude("helmet", new WantedMod(tier.Anchor.Id, family.NamesFrom(tier)));

        var cut = Render(ctx, state, mods, "helmet");
        Expand(cut, family);
        var exclude = cut.Find($"#pick-tier-exc-{tier.Anchor.Id}");
        exclude.Click();

        crit.Included.Should().BeEmpty();
        crit.Excluded.Should().Contain(tier.Name);
        exclude.GetAttribute("aria-pressed").Should().Be("true");
        exclude.TextContent.Should().Be("⊘");
        cut.Find($"#pick-tier-{tier.Anchor.Id}").GetAttribute("aria-pressed").Should().Be("false");
        cut.Find($"#pick-tier-up-{tier.Anchor.Id}").GetAttribute("aria-pressed").Should().Be("false");
        TierRow(cut, tier).ClassList.Should().Contain("excluded");
        TierRow(cut, tier).ClassList.Should().NotContain("picked");
        TierRow(cut, tier).ClassList.Should().NotContain("covered");
    }

    [Fact]
    public void MixedFamilyStates_PreserveBothButOnlyShowExcludedTag()
    {
        using var ctx = Context();
        var (_, mods, state) = AdvancedPickler(ctx, out var family);
        state.AddInclude("helmet", family.Pick());
        state.AddExclude("helmet", family.Anchor.Id);

        var cut = Render(ctx, state, mods, "helmet");

        var row = FamilyRow(cut, family);
        row.ClassList.Should().Contain("picked");
        row.ClassList.Should().Contain("excluded");
        row.TextContent.Should().Contain("⊘ Excluded");
        row.TextContent.Should().NotContain("✓ Included",
            "family rows have no Included tag; the picked count and row state carry it");
        cut.Find($"#pick-inc-{family.Anchor.Id}").GetAttribute("aria-pressed").Should().Be("true");
        cut.Find($"#pick-exc-{family.Anchor.Id}").GetAttribute("aria-pressed").Should().Be("true");
    }
}

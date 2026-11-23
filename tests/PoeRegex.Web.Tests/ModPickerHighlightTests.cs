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
    public void BootsMovementSpeed_FourExactTiers_CoalesceToOneRangePick()
    {
        using var ctx = Context();
        var engine = ctx.Services.GetRequiredService<EngineService>();
        var mods = engine.ModsForClass("Boots");
        var family = ModFamilies.Group(mods).First(f => f.Key == "MovementVelocity");
        var state = new WorkbenchState();
        var cut = Render(ctx, state, mods, "boots");

        cut.Find("#mod-search").Input("Movement Speed");
        var toggle = cut.Find($"#fam-toggle-{family.Anchor.Id}");
        if (toggle.GetAttribute("aria-expanded") != "true") toggle.Click();

        foreach (var id in new[]
                 {
                     "MovementVelocity3", "MovementVelocity4",
                     "MovementVelocity5", "MovementVelocity6",
                 })
        {
            cut.Find($"#pick-tier-{id}").Click();
        }

        var pick = state.CriteriaFor("boots").Included.Should().ContainSingle().Subject;
        pick.IsTierRange.Should().BeTrue();
        pick.RangeModIds.Should().HaveCount(4);
        pick.MinValue.Should().Be(20);
        pick.MaxValue.Should().Be(35);
    }

    [Fact]
    public void TierUp_MiddleTierUnpick_ShowsRangeAndUpAnchors()
    {
        using var ctx = Context();
        var engine = ctx.Services.GetRequiredService<EngineService>();
        var mods = engine.ModsForClass("Boots");
        var family = ModFamilies.Group(mods).First(f => f.Key == "MovementVelocity");
        var state = new WorkbenchState();
        var cut = Render(ctx, state, mods, "boots");

        cut.Find("#mod-search").Input("Movement Speed");
        var toggle = cut.Find($"#fam-toggle-{family.Anchor.Id}");
        if (toggle.GetAttribute("aria-expanded") != "true") toggle.Click();
        cut.Find("#pick-tier-up-MovementVelocity2").Click();
        cut.Find("#pick-tier-MovementVelocity4").Click();

        var crit = state.CriteriaFor("boots");
        crit.Excluded.Should().BeEmpty();
        crit.Included.Should().HaveCount(2);
        var lower = crit.Included.Single(p =>
            p.RangeModIds!.SequenceEqual(["MovementVelocity2", "MovementVelocity3"]));
        lower.ModId.Should().Be("MovementVelocity2");
        var higher = crit.Included.Single(p =>
            p.RangeModIds!.SequenceEqual(["MovementVelocity5", "MovementVelocity6"]));
        higher.ModId.Should().Be("MovementVelocity5");

        var anchor = cut.Find("#pick-tier-MovementVelocity2");
        anchor.TextContent.Should().Be("✓", "the lower range's first tier is its anchor");
        anchor.GetAttribute("aria-pressed").Should().Be("true");
        TierRow(cut, family.Tiers.Single(t => t.Anchor.Id == "MovementVelocity2"))
            .ClassList.Should().Contain("picked");

        var covered = cut.Find("#pick-tier-MovementVelocity3");
        covered.TextContent.Should().Be("⇄", "the range-covered tier shows the switch state");
        covered.ClassList.Should().Contain("switch");
        var coveredRow = TierRow(cut, family.Tiers.Single(t => t.Anchor.Id == "MovementVelocity3"));
        coveredRow.ClassList.Should().Contain("range-covered");
        coveredRow.QuerySelector(".covered-note")!.TextContent.Should().Be("range");

        var upAnchor = cut.Find("#pick-tier-up-MovementVelocity5");
        upAnchor.GetAttribute("aria-pressed").Should().Be("true");
        upAnchor.ClassList.Should().Contain("active");
        TierRow(cut, family.Tiers.Single(t => t.Anchor.Id == "MovementVelocity6"))
            .ClassList.Should().Contain("up-covered");

        cut.Find("#pick-tier-MovementVelocity4").TextContent.Should().Be("+");
    }

    [Fact]
    public void BootsMovementSpeed_NonConsecutiveTiers_StayDiscrete()
    {
        using var ctx = Context();
        var engine = ctx.Services.GetRequiredService<EngineService>();
        var mods = engine.ModsForClass("Boots");
        var family = ModFamilies.Group(mods).First(f => f.Key == "MovementVelocity");
        var state = new WorkbenchState();
        var cut = Render(ctx, state, mods, "boots");

        cut.Find("#mod-search").Input("Movement Speed");
        var toggle = cut.Find($"#fam-toggle-{family.Anchor.Id}");
        if (toggle.GetAttribute("aria-expanded") != "true") toggle.Click();

        cut.Find("#pick-tier-MovementVelocity2").Click();
        cut.Find("#pick-tier-MovementVelocity4").Click();

        var picks = state.CriteriaFor("boots").Included;
        picks.Should().HaveCount(2, "15 and 25 are not a consecutive tier run");
        picks.Select(p => p.MinValue).Should().BeEquivalentTo([15d, 25d]);
        picks.Should().OnlyContain(p => p.RangeModIds!.Count == 1);

        var query = engine.CompilePicks(picks, [], false, "Boots").Query;
        ItemMatcher.Match(query, ["Item Class: Boots", "15% increased Movement Speed"])
            .Match.Should().BeTrue();
        ItemMatcher.Match(query, ["Item Class: Boots", "25% increased Movement Speed"])
            .Match.Should().BeTrue();
        ItemMatcher.Match(query, ["Item Class: Boots", "20% increased Movement Speed"])
            .Match.Should().BeFalse("the gap tier was not selected");

        var tier2 = family.Tiers.Single(t => t.Anchor.Id == "MovementVelocity2");
        var tier3 = family.Tiers.Single(t => t.Anchor.Id == "MovementVelocity3");
        var tier4 = family.Tiers.Single(t => t.Anchor.Id == "MovementVelocity4");
        TierRow(cut, tier2).ClassList.Should().Contain("picked");
        TierRow(cut, tier3).ClassList.Should().NotContain("picked")
            .And.NotContain("range-covered");
        TierRow(cut, tier4).ClassList.Should().Contain("picked");
    }

    [Fact]
    public void BootsMovementSpeed_ConsecutiveRun_MergesAndMarksEveryTier()
    {
        using var ctx = Context();
        var engine = ctx.Services.GetRequiredService<EngineService>();
        var mods = engine.ModsForClass("Boots");
        var family = ModFamilies.Group(mods).First(f => f.Key == "MovementVelocity");
        var state = new WorkbenchState();
        var cut = Render(ctx, state, mods, "boots");

        cut.Find("#mod-search").Input("Movement Speed");
        var toggle = cut.Find($"#fam-toggle-{family.Anchor.Id}");
        if (toggle.GetAttribute("aria-expanded") != "true") toggle.Click();

        cut.Find("#pick-tier-MovementVelocity2").Click();
        cut.Find("#pick-tier-MovementVelocity3").Click();
        cut.Find("#pick-tier-MovementVelocity4").Click();

        var pick = state.CriteriaFor("boots").Included.Should().ContainSingle().Subject;
        pick.IsTierRange.Should().BeTrue();
        pick.RangeModIds.Should().HaveCount(3);
        pick.MinValue.Should().Be(15);
        pick.MaxValue.Should().Be(25);

        var tier3 = family.Tiers.Single(t => t.Anchor.Id == "MovementVelocity3");
        TierRow(cut, tier3).ClassList.Should().Contain("range-covered");
        var tier5 = family.Tiers.Single(t => t.Anchor.Id == "MovementVelocity5");
        TierRow(cut, tier5).ClassList.Should().NotContain("range-covered");
    }

    [Fact]
    public void BootsMovementSpeed_HighestTier_CanBeToggledOff()
    {
        using var ctx = Context();
        var engine = ctx.Services.GetRequiredService<EngineService>();
        var mods = engine.ModsForClass("Boots");
        var family = ModFamilies.Group(mods).First(f => f.Key == "MovementVelocity");
        var state = new WorkbenchState();
        var cut = Render(ctx, state, mods, "boots");

        cut.Find("#mod-search").Input("Movement Speed");
        var toggle = cut.Find($"#fam-toggle-{family.Anchor.Id}");
        if (toggle.GetAttribute("aria-expanded") != "true") toggle.Click();
        var button = cut.Find("#pick-tier-MovementVelocity6");
        button.HasAttribute("disabled").Should().BeFalse(
            "the highest tier is exact, not an or-better anchor");
        button.Click();

        state.CriteriaFor("boots").Included.Should().ContainSingle()
            .Which.MaxValue.Should().Be(35);

        cut.Find("#pick-tier-MovementVelocity6").Click();
        state.CriteriaFor("boots").Included.Should().BeEmpty();
    }

    [Fact]
    public void BootsMovementSpeed_FullFamilyRange_OwnsTheFamilyRow()
    {
        using var ctx = Context();
        var engine = ctx.Services.GetRequiredService<EngineService>();
        var mods = engine.ModsForClass("Boots");
        var family = ModFamilies.Group(mods).First(f => f.Key == "MovementVelocity");
        var first = family.Tiers.OrderBy(t => t.RequiredLevel).First();
        var pick = engine.BestTierPick(family, first, up: true, "Boots")!;
        var state = new WorkbenchState();
        state.AddInclude("boots", pick);
        var cut = Render(ctx, state, mods, "boots");

        cut.Find("#mod-search").Input("Movement Speed");
        var button = cut.Find($"#pick-inc-{family.Anchor.Id}");
        button.TextContent.Should().Be("⇄",
            "all tiers are selected, but as an up range, not the family pick");
        button.GetAttribute("aria-pressed").Should().Be("false");

        button.Click();
        var familyPick = state.CriteriaFor("boots").Included.Should().ContainSingle().Subject;
        familyPick.IsTierRange.Should().BeFalse();
        familyPick.VariantModIds.Should().NotBeNull("the family row converts to the family pick");
    }

    [Fact]
    public void BootsMovementSpeed_ExactOnBoundedRange_SplitsWithoutExclusion()
    {
        using var ctx = Context();
        var engine = ctx.Services.GetRequiredService<EngineService>();
        var mods = engine.ModsForClass("Boots");
        var family = ModFamilies.Group(mods).First(f => f.Key == "MovementVelocity");
        var state = new WorkbenchState();
        var cut = Render(ctx, state, mods, "boots");

        cut.Find("#mod-search").Input("Movement Speed");
        var toggle = cut.Find($"#fam-toggle-{family.Anchor.Id}");
        if (toggle.GetAttribute("aria-expanded") != "true") toggle.Click();
        cut.Find("#pick-tier-MovementVelocity2").Click();
        cut.Find("#pick-tier-MovementVelocity3").Click();
        var anchor = cut.Find("#pick-tier-MovementVelocity2");
        anchor.TextContent.Should().Be("✓", "the range anchor is the explicit pick");
        anchor.GetAttribute("aria-pressed").Should().Be("true");
        var exact = cut.Find("#pick-tier-MovementVelocity3");
        exact.HasAttribute("disabled").Should().BeFalse();
        exact.TextContent.Should().Be("⇄",
            "a range-covered tier shows the switch state; clicking still splits the range");
        exact.ClassList.Should().Contain("switch");
        exact.Click();

        var crit = state.CriteriaFor("boots");
        crit.Excluded.Should().BeEmpty("exact removal does not create an exclusion");
        var pick = crit.Included.Should().ContainSingle().Subject;
        pick.RangeModIds.Should().Equal(["MovementVelocity2"]);
        pick.MinValue.Should().Be(15);
        pick.MaxValue.Should().Be(15);

        cut.Find("#pick-tier-MovementVelocity3").TextContent.Should().Be("+");
        cut.Find("#pick-tier-MovementVelocity3").Click();
        crit.Included.Should().ContainSingle()
            .Which.RangeModIds.Should().HaveCount(2, "adding the tier back merges the run");
    }

    [Fact]
    public void BootsMovementSpeed_ExcludeOnBoundedRange_KeepsRangeAndAddsExclusion()
    {
        using var ctx = Context();
        var engine = ctx.Services.GetRequiredService<EngineService>();
        var mods = engine.ModsForClass("Boots");
        var family = ModFamilies.Group(mods).First(f => f.Key == "MovementVelocity");
        var state = new WorkbenchState();
        var cut = Render(ctx, state, mods, "boots");

        cut.Find("#mod-search").Input("Movement Speed");
        var toggle = cut.Find($"#fam-toggle-{family.Anchor.Id}");
        if (toggle.GetAttribute("aria-expanded") != "true") toggle.Click();
        cut.Find("#pick-tier-MovementVelocity2").Click();
        cut.Find("#pick-tier-MovementVelocity3").Click();
        var exclude = cut.Find("#pick-tier-exc-MovementVelocity3");
        exclude.HasAttribute("disabled").Should().BeFalse();
        exclude.Click();

        var crit = state.CriteriaFor("boots");
        crit.Included.Should().ContainSingle(
            "the bounded range stays; the exclusion removes the tier");
        crit.Included[0].RangeModIds.Should().HaveCount(2);
        crit.Excluded.Should().Contain(
            family.Tiers.Single(t => t.Anchor.Id == "MovementVelocity3").Name);
        TierRow(cut, family.Tiers.Single(t => t.Anchor.Id == "MovementVelocity3"))
            .ClassList.Should().Contain("excluded");
    }

    [Fact]
    public void BootsMovementSpeed_UpOnBoundedRange_FillsTheSuffix()
    {
        using var ctx = Context();
        var engine = ctx.Services.GetRequiredService<EngineService>();
        var mods = engine.ModsForClass("Boots");
        var family = ModFamilies.Group(mods).First(f => f.Key == "MovementVelocity");
        var state = new WorkbenchState();
        var cut = Render(ctx, state, mods, "boots");

        cut.Find("#mod-search").Input("Movement Speed");
        var toggle = cut.Find($"#fam-toggle-{family.Anchor.Id}");
        if (toggle.GetAttribute("aria-expanded") != "true") toggle.Click();
        cut.Find("#pick-tier-MovementVelocity2").Click();
        cut.Find("#pick-tier-MovementVelocity3").Click();
        var up = cut.Find("#pick-tier-up-MovementVelocity3");
        up.HasAttribute("disabled").Should().BeFalse();
        up.Click();

        var crit = state.CriteriaFor("boots");
        crit.Included.Should().HaveCount(2,
            "the existing 15–20 range stays and the missing 25–35 tiers are added");
        crit.Excluded.Should().BeEmpty();
        var query = engine.CompilePicks(crit.Included, crit.Excluded, false, "Boots").Query;
        ItemMatcher.Match(query, ["Item Class: Boots", "15% increased Movement Speed"])
            .Match.Should().BeTrue();
        ItemMatcher.Match(query, ["Item Class: Boots", "35% increased Movement Speed"])
            .Match.Should().BeTrue();
    }

    [Fact]
    public void BootsMovementSpeed_FamilyPickReplacesBoundedRange()
    {
        using var ctx = Context();
        var engine = ctx.Services.GetRequiredService<EngineService>();
        var mods = engine.ModsForClass("Boots");
        var family = ModFamilies.Group(mods).First(f => f.Key == "MovementVelocity");
        var state = new WorkbenchState();
        var cut = Render(ctx, state, mods, "boots");

        cut.Find("#mod-search").Input("Movement Speed");
        var toggle = cut.Find($"#fam-toggle-{family.Anchor.Id}");
        if (toggle.GetAttribute("aria-expanded") != "true") toggle.Click();
        cut.Find("#pick-tier-MovementVelocity2").Click();
        cut.Find("#pick-tier-MovementVelocity3").Click();
        cut.Find($"#pick-inc-{family.Anchor.Id}").Click();

        var pick = state.CriteriaFor("boots").Included.Should().ContainSingle().Subject;
        pick.RangeModIds.Should().BeNull();
        pick.VariantModIds.Should().NotBeNull();
        pick.VariantModIds.Should().HaveCount(family.MemberIds.Count);
    }

    [Fact]
    public void BootsMovementSpeed_UpButton_AddsOneCombinedRangePick()
    {
        using var ctx = Context();
        var engine = ctx.Services.GetRequiredService<EngineService>();
        var mods = engine.ModsForClass("Boots");
        var family = ModFamilies.Group(mods).First(f => f.Key == "MovementVelocity");
        var tier = family.Tiers.Single(t => t.Name == "Stallion's");
        var state = new WorkbenchState();
        var cut = Render(ctx, state, mods, "boots");

        cut.Find("#mod-search").Input("Movement Speed");
        var toggle = cut.Find($"#fam-toggle-{family.Anchor.Id}");
        if (toggle.GetAttribute("aria-expanded") != "true") toggle.Click();
        cut.Find($"#pick-tier-up-{tier.Anchor.Id}").Click();

        var pick = state.CriteriaFor("boots").Included.Should().ContainSingle().Subject;
        pick.IsTierRange.Should().BeTrue();
        pick.RangeModIds.Should().HaveCount(4);
        pick.MinValue.Should().Be(20);
        pick.MaxValue.Should().Be(35);
        pick.Names.Should().BeNull();
    }

    [Fact]
    public void BootsMovementSpeed_ExactButton_AddsTheShorterRangePick()
    {
        using var ctx = Context();
        var engine = ctx.Services.GetRequiredService<EngineService>();
        var mods = engine.ModsForClass("Boots");
        var family = ModFamilies.Group(mods).First(f => f.Key == "MovementVelocity");
        var tier = family.Tiers.Single(t => t.Name == "Stallion's");
        var state = new WorkbenchState();
        var cut = Render(ctx, state, mods, "boots");

        cut.Find("#mod-search").Input("Movement Speed");
        var toggle = cut.Find($"#fam-toggle-{family.Anchor.Id}");
        if (toggle.GetAttribute("aria-expanded") != "true") toggle.Click();
        cut.Find($"#pick-tier-{tier.Anchor.Id}").Click();

        var pick = state.CriteriaFor("boots").Included.Should().ContainSingle().Subject;
        pick.IsTierRange.Should().BeTrue();
        pick.RangeModIds.Should().ContainSingle();
        pick.MinValue.Should().Be(20);
        pick.MaxValue.Should().Be(20);
    }

    [Fact]
    public void DesecratedFamily_ShowsPlainGreenSourceText()
    {
        using var ctx = Context();
        var engine = ctx.Services.GetRequiredService<EngineService>();
        var mods = engine.ModsForClass("Helmet");
        var state = new WorkbenchState();
        var cut = Render(ctx, state, mods);

        var sourceMod = mods.Single(m =>
            m.Id == "AbyssModHelmUlamanSuffixMarkedEnemyTakeIncreasedDamage");
        var family = ModFamilies.Group(mods)
            .Single(f => f.IsDesecrated && f.Members.Any(m => m.Id == sourceMod.Id));

        cut.Find("#mod-search").Input("of Ulaman");

        var row = cut.Find($"#pick-inc-{family.Anchor.Id}").ParentElement!;
        var status = row.QuerySelector(".fam-status")!;
        status.LastElementChild!.ClassList.Should().Contain("mod-source");
        status.LastElementChild!.TextContent.Trim().Should().Be("Ulaman");
        row.QuerySelector(".fam-text .mod-source").Should().BeNull(
            "the source tag belongs in the row's tag cluster, not inside the stat text");
        row.QuerySelector(".fam-caret").Should().BeNull(
            "a single-tier same-fragment family collapses without an expand control");
    }

    [Fact]
    public void CollapsedFamilyExclude_ShowsButtonWithoutCount()
    {
        using var ctx = Context();
        var engine = ctx.Services.GetRequiredService<EngineService>();
        var mods = engine.ModsForClass("Helmet");
        var sourceMod = mods.Single(m =>
            m.Id == "AbyssModHelmUlamanSuffixMarkedEnemyTakeIncreasedDamage");
        var family = ModFamilies.Group(mods)
            .Single(f => f.IsDesecrated && f.Members.Any(m => m.Id == sourceMod.Id));
        var state = new WorkbenchState();
        state.AddExclude("helmet", family.Anchor.Id);

        var cut = Render(ctx, state, mods, "helmet");
        cut.Find("#mod-search").Input("of Ulaman");

        var row = cut.Find($"#pick-inc-{family.Anchor.Id}").ParentElement!;
        row.ClassList.Should().Contain("excluded");
        row.QuerySelector(".fam-caret").Should().BeNull();
        row.QuerySelector(".excluded-count").Should().BeNull(
            "the ⊘ action already conveys the exclusion on a non-expanding row");
        cut.Find($"#pick-exc-{family.Anchor.Id}").TextContent.Should().Be("⊘");
        row.QuerySelector(".fam-status .mod-source")!.TextContent.Trim().Should().Be("Ulaman");
    }

    [Fact]
    public void ImplicitFamily_ShowsImplicitSectionAndTag()
    {
        using var ctx = Context();
        var engine = ctx.Services.GetRequiredService<EngineService>();
        var mods = engine.ModsForClass("One Hand Mace");
        var state = new WorkbenchState();
        var cut = Render(ctx, state, mods);

        cut.Find("#mod-search").Input("Always Hits");

        var family = ModFamilies.Group(mods)
            .Single(f => f.IsImplicit && f.Members.Any(m => m.Id == "MaceImplicitAlwaysHit1"));
        cut.FindAll(".mod-sec.implicit-sec").Should().ContainSingle();
        cut.FindAll(".gen-head").Select(x => x.TextContent.Trim())
            .Should().ContainSingle().Which.Should().Be("Implicits");

        var row = cut.Find($"#pick-inc-{family.Anchor.Id}").ParentElement!;
        var tag = row.QuerySelector(".mod-source")!;
        tag.TextContent.Trim().Should().Be("Implicit");
        tag.ClassList.Should().Contain("source-implicit");
        row.QuerySelector(".fam-text .mod-source").Should().BeNull();
    }

    [Fact]
    public void CorruptedFamily_ShowsCorruptedSectionAndTag()
    {
        using var ctx = Context();
        var engine = ctx.Services.GetRequiredService<EngineService>();
        var mods = engine.ModsForClass("Bow");
        var state = new WorkbenchState();
        var cut = Render(ctx, state, mods);

        cut.Find("#mod-search").Input("Bow Attacks fire an additional Arrow");

        var family = ModFamilies.Group(mods)
            .Single(f => f.IsCorrupted &&
                         f.Members.Any(m => m.Id == "CorruptionAdditionalArrows1"));
        cut.FindAll(".mod-sec.corrupted-sec").Should().ContainSingle();
        cut.FindAll(".gen-head").Select(x => x.TextContent.Trim())
            .Should().Contain("Corrupted");

        var row = cut.Find($"#pick-inc-{family.Anchor.Id}").ParentElement!;
        var tag = row.QuerySelector(".mod-source")!;
        tag.TextContent.Trim().Should().Be("Corrupted");
        tag.ClassList.Should().Contain("source-corrupted");
        row.QuerySelector(".fam-text .mod-source").Should().BeNull();
    }

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
        FamilyRow(cut, family).QuerySelectorAll(".state-badge").Should().BeEmpty(
            "family state lives on the buttons and counts, not pills");
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
        upButton.TextContent.Should().Be("≥");
        upButton.GetAttribute("title").Should().Contain("or better");
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

        TierRow(cut, tier).QuerySelectorAll(".state-badge").Should().BeEmpty();
        TierRow(cut, tier).QuerySelector(".covered-note").Should().BeNull(
            "the chosen tier's active ≥ button carries the selection");
        var coveredRow = TierRow(cut, covered[^1]);
        coveredRow.QuerySelectorAll(".state-badge").Should().BeEmpty();
        coveredRow.QuerySelector(".covered-note")!.TextContent.Should().Contain("≥ covered");
        var coveredExact = cut.Find($"#pick-tier-{covered[^1].Anchor.Id}");
        coveredExact.HasAttribute("disabled").Should().BeFalse();
        coveredExact.TextContent.Should().Be("+",
            "a covered up tier is not an explicit exact pick");
        coveredExact.ClassList.Should().NotContain("active",
            "covered actions are neutral; the covered text carries the state");
        coveredExact.ClassList.Should().Contain("remove");
        coveredExact.GetAttribute("title").Should().Contain("Remove");

        var coveredUpTier = covered.Skip(1)
            .FirstOrDefault(t => family.NamesFrom(t).Count > 1);
        if (coveredUpTier is not null)
        {
            var coveredUp = cut.Find($"#pick-tier-up-{coveredUpTier.Anchor.Id}");
            coveredUp.HasAttribute("disabled").Should().BeFalse();
            coveredUp.GetAttribute("aria-pressed").Should().Be("false",
                "only the actual anchor is active");
            coveredUp.ClassList.Should().Contain("replace")
                .And.NotContain("active");
            coveredUp.GetAttribute("title").Should().Contain("make");
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

    [Fact]
    public void BreakArmourGlobal_CollapsesToOneFamilyLine()
    {
        using var ctx = Context();
        var engine = ctx.Services.GetRequiredService<EngineService>();
        var mods = engine.PlayableMods();
        var state = new WorkbenchState();
        var family = ModFamilies.Group(mods, union: true)
            .Single(f => f.DisplayText == "Break Armour equal to % of Physical Damage dealt");
        family.Tiers.Should().ContainSingle();

        var cut = Render(ctx, state, mods);
        cut.Find("#mod-search").Input("Break Armour");

        var row = cut.Find($"#pick-inc-{family.Anchor.Id}").ParentElement!;
        row.ClassList.Should().Contain("fam-row");
        row.QuerySelector(".fam-caret").Should().BeNull(
            "one tier with the family's fragment collapses to a single line");
        row.TextContent.Should().Contain("Break Armour equal to 2–4 / 4–7%");
        row.QuerySelector(".mod-source")!.TextContent.Should().Be("Amanamu");
        cut.FindAll(".tier-row").Should().BeEmpty();
    }

    [Fact]
    public void DesecratedFamilyExclude_ShowsStateOnlyOnTheFamilyRow()
    {
        using var ctx = Context();
        var engine = ctx.Services.GetRequiredService<EngineService>();
        var mods = engine.PlayableMods();
        var state = new WorkbenchState();
        var family = ModFamilies.Group(mods, union: true)
            .Single(f => f.DisplayText == "Break Armour equal to % of Physical Damage dealt");
        state.AddExclude(null, family.Anchor.Id);

        var cut = Render(ctx, state, mods);
        cut.Find("#mod-search").Input("Break Armour");

        var row = cut.Find($"#pick-exc-{family.Anchor.Id}").ParentElement!;
        row.ClassList.Should().Contain("excluded");
        row.QuerySelector(".mod-source")!.TextContent.Should().Be("Amanamu");
        cut.FindAll(".tier-row").Should().BeEmpty();
        cut.FindAll(".tier-row .mod-source").Should().BeEmpty();
    }

    [Fact]
    public void GlobalProjectileSearch_UnionsFamiliesAndShowsRollAlternatives()
    {
        using var ctx = Context();
        var engine = ctx.Services.GetRequiredService<EngineService>();
        var mods = engine.PlayableMods();
        var family = ModFamilies.Group(mods, union: true)
            .Single(f => f.DisplayText == "+ to Level of all Projectile Skills");
        var state = new WorkbenchState();
        var cut = Render(ctx, state, mods);

        cut.Find("#mod-search").Input("Projectile Skills");

        cut.FindAll(".fam-row").Should().ContainSingle();
        cut.Find($"#fam-toggle-{family.Anchor.Id}").Click();
        cut.FindAll(".tier-row").Should().HaveCount(5);

        var fletcher = cut.FindAll(".tier-row")
            .Single(r => r.TextContent.Contains("of the Fletcher", StringComparison.Ordinal));
        fletcher.TextContent.Should().Contain("+1 / +2 to Level of all Projectile Skills");
        fletcher.QuerySelectorAll(".mod-line").Should().ContainSingle(
            "merged class variants render as roll alternatives, not a hybrid mod");
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
        row.QuerySelectorAll(".state-badge").Should().BeEmpty("exclusion is shown on the button, not a pill");
        row.GetAttribute("style").Should().BeNullOrEmpty("no inline side-border treatment");
        row.ClassList.Should().NotContain("side-border");

        var button = cut.Find($"#pick-exc-{family.Anchor.Id}");
        button.GetAttribute("aria-pressed").Should().Be("true");
        button.GetAttribute("aria-label").Should().Contain("Remove exclusion");
        button.ClassList.Should().Contain("active");
        button.TextContent.Should().Be("⊘");
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
        row.QuerySelectorAll(".state-badge").Should().BeEmpty("exclusion is shown on the button, not a pill");
        row.GetAttribute("style").Should().BeNullOrEmpty("no inline side-border treatment");
        row.ClassList.Should().NotContain("side-border");

        FamilyRow(cut, family).ClassList.Should().NotContain("excluded",
            "a tier name exclusion is not a family exclusion");

        var button = cut.Find($"#pick-tier-exc-{tier.Anchor.Id}");
        button.GetAttribute("aria-pressed").Should().Be("true");
        button.GetAttribute("aria-label").Should().Contain("Remove exclusion");
        button.ClassList.Should().Contain("active");
        button.TextContent.Should().Be("⊘");
    }

    [Fact]
    public void FamilyExclude_DoesNotMarkItsFirstTierExcluded()
    {
        using var ctx = Context();
        var (_, mods, state) = AdvancedPickler(ctx, out var family);
        state.AddExclude("helmet", family.Anchor.Id);

        var cut = Render(ctx, state, mods, "helmet");
        Expand(cut, family);

        FamilyRow(cut, family).ClassList.Should().Contain("excluded");
        FamilyRow(cut, family).QuerySelector(".excluded-count").Should().BeNull();
        cut.Find($"#pick-exc-{family.Anchor.Id}").GetAttribute("aria-pressed").Should().Be("true");

        foreach (var tier in family.Tiers)
        {
            TierRow(cut, tier).ClassList.Should().NotContain("excluded",
                "a family anchor exclusion is not a tier exclusion");
            cut.Find($"#pick-tier-exc-{tier.Anchor.Id}").GetAttribute("aria-pressed")
                .Should().Be("false", $"\"{tier.Name}\" is covered by the family exclusion, not excluded itself");
        }
    }

    [Fact]
    public void FirstTierExclude_MarksOnlyThatTier()
    {
        using var ctx = Context();
        var (_, mods, state) = AdvancedPickler(ctx, out var family);
        var first = family.Tiers[0];
        state.AddExclude("helmet", first.Name);

        var cut = Render(ctx, state, mods, "helmet");
        Expand(cut, family);

        FamilyRow(cut, family).ClassList.Should().NotContain("excluded");
        cut.Find($"#pick-exc-{family.Anchor.Id}").GetAttribute("aria-pressed").Should().Be("false");

        TierRow(cut, first).ClassList.Should().Contain("excluded");
        cut.Find($"#pick-tier-exc-{first.Anchor.Id}").TextContent.Should().Be("⊘");
        foreach (var other in family.Tiers.Skip(1))
            TierRow(cut, other).ClassList.Should().NotContain("excluded",
                "an explicit tier-name exclusion applies to that tier only");
    }

    [Fact]
    public void FamilyAndFirstTierExclude_ShowsBothExcluded()
    {
        using var ctx = Context();
        var (_, mods, state) = AdvancedPickler(ctx, out var family);
        var first = family.Tiers[0];
        state.AddExclude("helmet", family.Anchor.Id);
        state.AddExclude("helmet", first.Name);

        var cut = Render(ctx, state, mods, "helmet");
        Expand(cut, family);

        FamilyRow(cut, family).ClassList.Should().Contain("excluded");
        TierRow(cut, first).ClassList.Should().Contain("excluded");
        cut.Find($"#pick-tier-exc-{first.Anchor.Id}").GetAttribute("aria-pressed").Should().Be("true");
        foreach (var other in family.Tiers.Skip(1))
            TierRow(cut, other).ClassList.Should().NotContain("excluded");
    }

    [Fact]
    public void FamilyPick_TierExactSplitsAndFamilyReselects()
    {
        using var ctx = Context();
        var (_, mods, state) = AdvancedPickler(ctx, out var family);
        var tier = family.Tiers[family.Tiers.Count / 2];
        state.AddInclude("helmet", family.Pick());

        var cut = Render(ctx, state, mods, "helmet");
        Expand(cut, family);

        var exact = cut.Find($"#pick-tier-{tier.Anchor.Id}");
        exact.TextContent.Should().Be("+",
            "the family-covered tier is not an explicit exact pick");
        exact.Click();

        var crit = state.CriteriaFor("helmet");
        crit.Excluded.Should().BeEmpty("exact removal does not create an exclusion");
        crit.Included.Should().HaveCount(2,
            "the family splits into the runs below and above the removed tier");
        var include = cut.Find($"#pick-inc-{family.Anchor.Id}");
        include.TextContent.Should().Be("+");
        include.GetAttribute("aria-pressed").Should().Be("false");

        include.Click();
        state.CriteriaFor("helmet").Included.Should()
            .ContainSingle(pick => pick.VariantModIds != null && pick.VariantModIds.Count > 0);
        cut.Find($"#pick-inc-{family.Anchor.Id}").TextContent.Should().Be("✓");
    }

    [Fact]
    public void AllExactTiers_StayARangeUntilFamilyRowClicked()
    {
        using var ctx = Context();
        var engine = ctx.Services.GetRequiredService<EngineService>();
        var mods = engine.ModsForClass("Boots");
        var family = ModFamilies.Group(mods).First(f => f.Key == "MovementVelocity");
        var state = new WorkbenchState();
        var cut = Render(ctx, state, mods, "boots");

        cut.Find("#mod-search").Input("Movement Speed");
        var toggle = cut.Find($"#fam-toggle-{family.Anchor.Id}");
        if (toggle.GetAttribute("aria-expanded") != "true") toggle.Click();
        foreach (var tier in family.Tiers.OrderBy(t => t.RequiredLevel))
            cut.Find($"#pick-tier-{tier.Anchor.Id}").Click();

        var range = state.CriteriaFor("boots").Included.Should().ContainSingle().Subject;
        range.IsTierRange.Should().BeTrue(
            "all tiers picked individually stay a range, not the family pick");
        range.RangeModIds.Should().HaveCount(family.MemberIds.Count);

        var include = cut.Find($"#pick-inc-{family.Anchor.Id}");
        include.TextContent.Should().Be("⇄");
        include.GetAttribute("aria-pressed").Should().Be("false");
        include.Click();

        var familyPick = state.CriteriaFor("boots").Included.Should().ContainSingle().Subject;
        familyPick.IsTierRange.Should().BeFalse();
        familyPick.VariantModIds.Should().NotBeNull(
            "the family row is the explicit conversion");
    }

    [Fact]
    public void FilteredRange_TierExactSplitsAndKeepsFilter()
    {
        using var ctx = Context();
        var engine = ctx.Services.GetRequiredService<EngineService>();
        var mods = engine.ModsForClass("Boots");
        var family = ModFamilies.Group(mods).First(f => f.Key == "MovementVelocity");
        var tier = family.Tiers.Single(t => t.Anchor.Id == "MovementVelocity5");
        var range = new WantedMod(
            "MovementVelocity3",
            MinValue: 25,
            MaxValue: 35,
            RangeModIds:
            [
                "MovementVelocity3", "MovementVelocity4",
                "MovementVelocity5", "MovementVelocity6",
            ]);
        var state = new WorkbenchState();
        state.AddInclude("boots", range);
        var cut = Render(ctx, state, mods, "boots");

        cut.Find("#mod-search").Input("Movement Speed");
        var toggle = cut.Find($"#fam-toggle-{family.Anchor.Id}");
        if (toggle.GetAttribute("aria-expanded") != "true") toggle.Click();
        cut.Find($"#pick-tier-{tier.Anchor.Id}").Click();

        var crit = state.CriteriaFor("boots");
        crit.Excluded.Should().BeEmpty("exact removal does not create an exclusion");
        crit.Included.Should().HaveCount(2,
            "the filtered range splits around the removed tier");
        crit.Included.Should().OnlyContain(pick =>
            pick.MinValue == 25 && pick.MaxValue == 35);

        var query = engine.CompilePicks(crit.Included, crit.Excluded, false, "Boots").Query;
        ItemMatcher.Match(query, ["Item Class: Boots", "25% increased Movement Speed"])
            .Match.Should().BeTrue();
        ItemMatcher.Match(query, ["Item Class: Boots", "35% increased Movement Speed"])
            .Match.Should().BeTrue();
        ItemMatcher.Match(query, ["Item Class: Boots", "30% increased Movement Speed"])
            .Match.Should().BeFalse("the removed tier is gone");
    }

    [Fact]
    public void FilteredRange_TierUpCopiesFilterAndTrimsLowerRun()
    {
        using var ctx = Context();
        var engine = ctx.Services.GetRequiredService<EngineService>();
        var mods = engine.ModsForClass("Boots");
        var family = ModFamilies.Group(mods).First(f => f.Key == "MovementVelocity");
        var range = new WantedMod(
            "MovementVelocity3",
            MinValue: 25,
            MaxValue: 35,
            RangeModIds:
            [
                "MovementVelocity3", "MovementVelocity4", "MovementVelocity5",
            ]);
        var state = new WorkbenchState();
        state.AddInclude("boots", range);
        var cut = Render(ctx, state, mods, "boots");

        cut.Find("#mod-search").Input("Movement Speed");
        var toggle = cut.Find($"#fam-toggle-{family.Anchor.Id}");
        if (toggle.GetAttribute("aria-expanded") != "true") toggle.Click();
        cut.Find("#pick-tier-up-MovementVelocity4").Click();

        var crit = state.CriteriaFor("boots");
        crit.Excluded.Should().BeEmpty();
        crit.Included.Should().HaveCount(2);
        crit.Included.Should().OnlyContain(pick =>
            pick.MinValue == 25 && pick.MaxValue == 35);
        var query = engine.CompilePicks(crit.Included, crit.Excluded, false, "Boots").Query;
        foreach (var value in new[] { 25, 30, 35 })
        {
            ItemMatcher.Match(query,
                ["Item Class: Boots", $"{value}% increased Movement Speed"]).Match
                .Should().BeTrue($"{value}% is inside the copied filter");
        }
        ItemMatcher.Match(query,
            ["Item Class: Boots", "20% increased Movement Speed"]).Match
            .Should().BeFalse("20% is below the filter");
    }

    [Fact]
    public void FamilyPick_TierUp_IsDisabledWithHint()
    {
        using var ctx = Context();
        var (_, mods, state) = AdvancedPickler(ctx, out var family);
        var tier = family.Tiers.First(t =>
            family.NamesFrom(t).Count > 1 &&
            family.Tiers.Select((familyTier, index) => (familyTier, index))
                .First(x => x.familyTier.Anchor.Id == t.Anchor.Id).index <
            family.Tiers.Count - 1);
        state.AddInclude("helmet", family.Pick());

        var cut = Render(ctx, state, mods, "helmet");
        Expand(cut, family);
        var up = cut.Find($"#pick-tier-up-{tier.Anchor.Id}");
        up.HasAttribute("disabled").Should().BeTrue(
            "the family pick already covers the suffix; no surprise rewrite");
        up.GetAttribute("title").Should().Contain("Already covered");
        up.Click();

        var crit = state.CriteriaFor("helmet");
        crit.Included.Should().ContainSingle(pick => pick.VariantModIds != null && pick.VariantModIds.Count > 0);
        crit.Excluded.Should().BeEmpty();
        cut.Find($"#pick-inc-{family.Anchor.Id}").TextContent.Should().Be("✓");
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
        familyRow.QuerySelectorAll(".state-badge").Should().BeEmpty();
        familyRow.TextContent.Should().Contain("1 excluded", "the family summary keeps both counts");
        var include = cut.Find($"#pick-inc-{family.Anchor.Id}");
        include.GetAttribute("aria-pressed").Should().Be("false",
            "the family pick is active, but a tier exclusion makes the effective set partial");
        include.TextContent.Should().Be("+");

        var tierRow = TierRow(cut, tier);
        tierRow.ClassList.Should().Contain("excluded");
        tierRow.QuerySelectorAll(".state-badge").Should().BeEmpty();
        cut.Find($"#pick-tier-exc-{tier.Anchor.Id}").TextContent.Should().Be("⊘");
        tierRow.ClassList.Should().NotContain("picked",
            "a whole-family pick does not count as that tier being picked");

        include.Click();
        state.CriteriaFor("helmet").Excluded.Should().BeEmpty(
            "selecting the family again clears tier exclusions");
        cut.Find($"#pick-inc-{family.Anchor.Id}").GetAttribute("aria-pressed").Should().Be("true");
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
        familyRow.QuerySelectorAll(".state-badge").Should().BeEmpty();
        familyRow.TextContent.Should().Contain("1 picked");
        cut.Find($"#pick-exc-{family.Anchor.Id}").TextContent.Should().Be("⊘");

        var tierRow = TierRow(cut, tier);
        tierRow.ClassList.Should().Contain("picked");
        cut.Find($"#pick-tier-{tier.Anchor.Id}").GetAttribute("aria-pressed").Should().Be("true");
        tierRow.ClassList.Should().NotContain("excluded");
    }

    [Fact]
    public void TierIncludeAndExcludeSameClaim_NormalizesToInclude()
    {
        using var ctx = Context();
        var (_, mods, state) = AdvancedPickler(ctx, out var family);
        var tier = family.Tiers[family.Tiers.Count / 2];
        state.AddInclude("helmet", new WantedMod(tier.Anchor.Id, [tier.Name]));
        state.AddExclude("helmet", tier.Name);

        var cut = Render(ctx, state, mods, "helmet");
        Expand(cut, family);

        var crit = state.CriteriaFor("helmet");
        crit.Included.Should().ContainSingle();
        crit.Excluded.Should().BeEmpty("include and exclude cannot share a claim");

        var row = TierRow(cut, tier);
        row.ClassList.Should().Contain("picked");
        row.ClassList.Should().NotContain("excluded");
        var exact = cut.Find($"#pick-tier-{tier.Anchor.Id}");
        exact.GetAttribute("aria-pressed").Should().Be("true");
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
    public void TierExactInclude_RemovesExclude_WhenNotCovered_AndTogglesOff()
    {
        using var ctx = Context();
        var (_, mods, state) = AdvancedPickler(ctx, out var family);
        var tier = family.Tiers[family.Tiers.Count / 2];
        var crit = state.CriteriaFor("helmet");
        state.AddExclude("helmet", tier.Name);

        var cut = Render(ctx, state, mods, "helmet");
        Expand(cut, family);
        var exact = cut.Find($"#pick-tier-{tier.Anchor.Id}");
        exact.Click();

        crit.Included.Should().ContainSingle(pick =>
            pick.ModId == tier.Anchor.Id &&
            (pick.IsTierRange || (pick.IsNamePick && pick.Names!.Count == 1)));
        crit.Excluded.Should().BeEmpty();
        exact.GetAttribute("aria-pressed").Should().Be("true");
        exact.TextContent.Should().Be("✓");
        cut.Find($"#pick-tier-up-{tier.Anchor.Id}").GetAttribute("aria-pressed").Should().Be("false");

        exact.Click(); // active selection toggles off
        crit.Included.Should().BeEmpty();
        exact.GetAttribute("aria-pressed").Should().Be("false");
    }

    [Fact]
    public void TierUp_ReplacesSuffixAndUndoRestoresNothing()
    {
        using var ctx = Context();
        var (_, mods, state) = AdvancedPickler(ctx, out var family);
        var tier = family.Tiers[family.Tiers.Count / 2];
        var crit = state.CriteriaFor("helmet");
        state.AddInclude("helmet", new WantedMod(tier.Anchor.Id, [tier.Name]));
        state.AddExclude("helmet", tier.Name);

        var cut = Render(ctx, state, mods, "helmet");
        Expand(cut, family);
        var up = cut.Find($"#pick-tier-up-{tier.Anchor.Id}");
        up.Click();

        crit.Excluded.Should().BeEmpty();
        crit.Included.Should().ContainSingle(
            "the suffix is one up pick; the old exact pick is subsumed");
        up.GetAttribute("aria-pressed").Should().Be("true");
        up.HasAttribute("disabled").Should().BeFalse();
        cut.Find($"#pick-tier-{tier.Anchor.Id}").TextContent.Should().Be("+",
            "the up pick covers the tier; it is no longer an explicit exact pick");

        up.Click();
        crit.Included.Should().BeEmpty("undo removes the up pick");
        crit.Excluded.Should().BeEmpty("undo does not create exclusions");
    }

    [Fact]
    public void PickedFamily_SurvivesNonMatchingSearch_AndReturnsToFilterWhenUnpicked()
    {
        using var ctx = Context();
        var (_, mods, state) = AdvancedPickler(ctx, out var family);
        state.AddInclude("helmet", family.Pick());

        var cut = Render(ctx, state, mods, "helmet");
        cut.Find("#mod-search").Input("zzzz-no-match");

        var row = FamilyRow(cut, family);
        row.ClassList.Should().Contain("picked");
        var include = cut.Find($"#pick-inc-{family.Anchor.Id}");
        include.TextContent.Should().Be("✓");

        include.Click();
        cut.FindAll($"#pick-inc-{family.Anchor.Id}").Should().BeEmpty(
            "an unpicked row returns to normal search filtering");
    }

    [Fact]
    public void PickedTier_SurvivesNonMatchingSearch()
    {
        using var ctx = Context();
        var (_, mods, state) = AdvancedPickler(ctx, out var family);
        var tier = family.Tiers[2];
        state.AddInclude("helmet", new WantedMod(tier.Anchor.Id, [tier.Name]));

        var cut = Render(ctx, state, mods, "helmet");
        cut.Find("#mod-search").Input("zzzz-no-match");
        var row = cut.Find($"#pick-inc-{tier.Anchor.Id}").ParentElement!;
        row.ClassList.Should().Contain("picked");
        if (cut.Find($"#fam-toggle-{tier.Anchor.Id}").GetAttribute("aria-expanded") != "true")
            cut.Find($"#fam-toggle-{tier.Anchor.Id}").Click();
        TierRow(cut, tier).ClassList.Should().Contain("picked");

        cut.Find($"#pick-tier-{tier.Anchor.Id}").Click();
        cut.FindAll($"#pick-tier-{tier.Anchor.Id}").Should().BeEmpty();
    }

    [Fact]
    public void ExcludedFamily_SurvivesNonMatchingSearch()
    {
        using var ctx = Context();
        var (_, mods, state) = AdvancedPickler(ctx, out var family);
        state.AddExclude("helmet", family.Anchor.Id);

        var cut = Render(ctx, state, mods, "helmet");
        cut.Find("#mod-search").Input("zzzz-no-match");

        var row = FamilyRow(cut, family);
        row.ClassList.Should().Contain("excluded");
        var exclude = cut.Find($"#pick-exc-{family.Anchor.Id}");
        exclude.TextContent.Should().Be("⊘");

        exclude.Click();
        cut.FindAll($"#pick-exc-{family.Anchor.Id}").Should().BeEmpty(
            "an unexcluded row returns to normal search filtering");
    }

    [Fact]
    public void TierUp_OnCoveredTier_ReplacesTheAnchor()
    {
        using var ctx = Context();
        var engine = ctx.Services.GetRequiredService<EngineService>();
        var mods = engine.ModsForClass("Boots");
        var family = ModFamilies.Group(mods).First(f => f.Key == "MovementVelocity");
        var tier = family.Tiers.Single(t => t.Anchor.Id == "MovementVelocity3");
        var state = new WorkbenchState();
        var cut = Render(ctx, state, mods, "boots");

        cut.Find("#mod-search").Input("Movement Speed");
        var toggle = cut.Find($"#fam-toggle-{family.Anchor.Id}");
        if (toggle.GetAttribute("aria-expanded") != "true") toggle.Click();
        cut.Find($"#pick-tier-up-{tier.Anchor.Id}").Click();

        var coveredTier = family.Tiers.Single(t => t.Anchor.Id == "MovementVelocity4");
        var up = cut.Find($"#pick-tier-up-{coveredTier.Anchor.Id}");
        up.HasAttribute("disabled").Should().BeFalse();
        up.GetAttribute("aria-pressed").Should().Be("false");
        up.ClassList.Should().Contain("replace")
            .And.NotContain("active", "a covered action is not the active pick");
        up.Click();

        var crit = state.CriteriaFor("boots");
        crit.Excluded.Should().BeEmpty("replacing the anchor does not create exclusions");
        var replaced = crit.Included.Should().ContainSingle().Subject;
        replaced.MinValue.Should().Be(25);
        replaced.MaxValue.Should().Be(35);
        replaced.RangeModIds.Should().BeEquivalentTo(
            ["MovementVelocity4", "MovementVelocity5", "MovementVelocity6"]);
        cut.Find($"#pick-tier-up-{coveredTier.Anchor.Id}").GetAttribute("aria-pressed")
            .Should().Be("true");
        TierRow(cut, tier).ClassList.Should().NotContain("covered",
            "the lower part of the old up selection is dropped");

        cut.Find($"#pick-tier-up-{coveredTier.Anchor.Id}").Click();
        state.CriteriaFor("boots").Included.Should().BeEmpty();
    }

    [Fact]
    public void FilteredUp_TierUpOnCoveredTier_CopiesTheFilter()
    {
        using var ctx = Context();
        var engine = ctx.Services.GetRequiredService<EngineService>();
        var mods = engine.ModsForClass("Boots");
        var family = ModFamilies.Group(mods).First(f => f.Key == "MovementVelocity");
        var up = new WantedMod(
            "MovementVelocity3",
            MinValue: 30,
            MaxValue: 35,
            RangeModIds:
            [
                "MovementVelocity3", "MovementVelocity4",
                "MovementVelocity5", "MovementVelocity6",
            ]);
        var state = new WorkbenchState();
        state.AddInclude("boots", up);
        var cut = Render(ctx, state, mods, "boots");

        cut.Find("#mod-search").Input("Movement Speed");
        var toggle = cut.Find($"#fam-toggle-{family.Anchor.Id}");
        if (toggle.GetAttribute("aria-expanded") != "true") toggle.Click();
        cut.Find("#pick-tier-up-MovementVelocity4").Click();

        var crit = state.CriteriaFor("boots");
        crit.Excluded.Should().BeEmpty();
        var replaced = crit.Included.Should().ContainSingle().Subject;
        replaced.MinValue.Should().Be(30, "the old up pick's floor is copied");
        replaced.MaxValue.Should().Be(35);
        replaced.RangeModIds.Should().BeEquivalentTo(
            ["MovementVelocity4", "MovementVelocity5", "MovementVelocity6"]);

        var query = engine.CompilePicks(crit.Included, crit.Excluded, false, "Boots").Query;
        foreach (var value in new[] { 30, 35 })
        {
            ItemMatcher.Match(query,
                ["Item Class: Boots", $"{value}% increased Movement Speed"]).Match
                .Should().BeTrue($"{value}% is inside the copied filter");
        }
        ItemMatcher.Match(query, ["Item Class: Boots", "25% increased Movement Speed"])
            .Match.Should().BeFalse("the old lower tier stays outside the filter");
    }

    [Fact]
    public void TierUp_ClearsHigherTierExclusionsAndReplacesSuffix()
    {
        using var ctx = Context();
        var (_, mods, state) = AdvancedPickler(ctx, out var family);
        var tier = family.Tiers[family.Tiers.Count / 3];
        var higher = family.Tiers
            .Where(t => t.RequiredLevel > tier.RequiredLevel &&
                        t.Name.Length > 0 &&
                        family.NamesFrom(tier).Contains(t.Name))
            .ToList();
        higher.Should().HaveCountGreaterThan(1);

        var includedHigher = higher[0];
        var excludedHigher = higher.First(t => t.Name != includedHigher.Name);
        var crit = state.CriteriaFor("helmet");
        state.AddInclude("helmet", new WantedMod(includedHigher.Anchor.Id, [includedHigher.Name]));
        state.AddExclude("helmet", excludedHigher.Name);

        var cut = Render(ctx, state, mods, "helmet");
        Expand(cut, family);
        cut.Find($"#pick-tier-up-{tier.Anchor.Id}").Click();

        crit.Excluded.Should().BeEmpty();
        crit.Included.Should().ContainSingle(
            "the suffix is replaced by one up pick");
        TierRow(cut, includedHigher).ClassList.Should().NotContain("excluded");
    }

    [Fact]
    public void TierExclude_AddsExclusionAndKeepsExactInclude()
    {
        using var ctx = Context();
        var (_, mods, state) = AdvancedPickler(ctx, out var family);
        var tier = family.Tiers[family.Tiers.Count / 2];
        var crit = state.CriteriaFor("helmet");
        state.AddInclude("helmet", new WantedMod(tier.Anchor.Id, [tier.Name]));

        var cut = Render(ctx, state, mods, "helmet");
        Expand(cut, family);
        var exclude = cut.Find($"#pick-tier-exc-{tier.Anchor.Id}");
        exclude.Click();

        crit.Included.Should().ContainSingle(
            "the exact include stays; the exclusion removes it from the effective set");
        crit.Excluded.Should().Contain(tier.Name);
        exclude.GetAttribute("aria-pressed").Should().Be("true");
        exclude.TextContent.Should().Be("⊘");
        cut.Find($"#pick-tier-{tier.Anchor.Id}").GetAttribute("aria-pressed").Should().Be("false");
        cut.Find($"#pick-tier-{tier.Anchor.Id}").TextContent.Should().Be("+");
        cut.Find($"#pick-tier-up-{tier.Anchor.Id}").GetAttribute("aria-pressed").Should().Be("false");
        TierRow(cut, tier).ClassList.Should().Contain("excluded");
        TierRow(cut, tier).ClassList.Should().NotContain("picked");
        TierRow(cut, tier).ClassList.Should().NotContain("covered");
    }

    [Fact]
    public void FamilyIncludeAndExcludeSameClaim_NormalizesToInclude()
    {
        using var ctx = Context();
        var (_, mods, state) = AdvancedPickler(ctx, out var family);
        state.AddInclude("helmet", family.Pick());
        state.AddExclude("helmet", family.Anchor.Id);

        var cut = Render(ctx, state, mods, "helmet");

        var crit = state.CriteriaFor("helmet");
        crit.Included.Should().ContainSingle();
        crit.Excluded.Should().BeEmpty("include and exclude cannot share a claim");
        var row = FamilyRow(cut, family);
        row.ClassList.Should().Contain("picked");
        row.ClassList.Should().NotContain("excluded");
        cut.Find($"#pick-inc-{family.Anchor.Id}").GetAttribute("aria-pressed").Should().Be("true");
        cut.Find($"#pick-exc-{family.Anchor.Id}").GetAttribute("aria-pressed").Should().Be("false");
    }

    [Fact]
    public void CoveredTier_ExactRemovesAndExcludeToggles()
    {
        using var ctx = Context();
        var (_, mods, state) = AdvancedPickler(ctx, out var family);
        var tier = family.Tiers[family.Tiers.Count / 2];
        var covered = family.Tiers.Where(t => family.NamesFrom(tier).Contains(t.Name)).ToList();
        covered.Should().HaveCountGreaterThan(1);
        var higher = covered[^1];
        var crit = state.CriteriaFor("helmet");
        state.AddInclude("helmet", new WantedMod(tier.Anchor.Id, family.NamesFrom(tier)));

        var cut = Render(ctx, state, mods, "helmet");
        Expand(cut, family);

        var higherExact = cut.Find($"#pick-tier-{higher.Anchor.Id}");
        var higherExclude = cut.Find($"#pick-tier-exc-{higher.Anchor.Id}");
        higherExact.HasAttribute("disabled").Should().BeFalse();
        higherExact.TextContent.Should().Be("+",
            "the name-up pick covers the tier; the include action stays an add glyph");
        higherExact.GetAttribute("title").Should().Contain("Remove");

        higherExact.Click();
        crit.Excluded.Should().BeEmpty("exact removal does not create an exclusion");
        crit.Included.Should().ContainSingle("the lower part of the up/name pick stays");
        TierRow(cut, higher).ClassList.Should().NotContain("picked")
            .And.NotContain("covered");

        higherExclude.Click();
        crit.Excluded.Should().Contain(higher.Name);
        TierRow(cut, higher).ClassList.Should().Contain("excluded");
        higherExclude.Click();
        crit.Excluded.Should().BeEmpty();
    }

    [Fact]
    public void SearchClear_AppearsOnlyWithText_AndResetsFilter()
    {
        using var ctx = Context();
        var engine = ctx.Services.GetRequiredService<EngineService>();
        var mods = engine.ModsForClass("Ring");
        var state = new WorkbenchState();
        var cut = Render(ctx, state, mods);

        cut.FindAll("#mod-search-clear").Should().BeEmpty();

        cut.Find("#mod-search").Input("life");
        var clear = cut.Find("#mod-search-clear");
        clear.GetAttribute("aria-label").Should().Be("Clear filter");

        clear.Click();

        cut.Find("#mod-search").GetAttribute("value").Should().Be("");
        cut.FindAll("#mod-search-clear").Should().BeEmpty();
    }
}

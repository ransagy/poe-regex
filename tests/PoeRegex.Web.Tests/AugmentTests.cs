using Bunit;
using Microsoft.Extensions.DependencyInjection;
using PoeRegex.Core;
using PoeRegex.Core.Data;
using PoeRegex.Web.Core;
using web.Components;
using web.Services;

namespace PoeRegex.Web.Tests;

/// <summary>
/// Basic-rune augments are a separate non-affix source. The export's target
/// category decides which item classes can carry the effect; tier rows are
/// display-only in this first pass and the family + selects the effect across
/// every tier.
/// </summary>
public class AugmentTests
{
    private static readonly EngineService Engine = new();

    [Fact]
    public void Catalog_HasAugmentTypesAndMappedTargets()
    {
        // Use a few representative classes so the All-gear union cannot hide
        // a family that is still selectable in its per-slot view.
        var augments = new[] { "Helmet", "Focus", "Bow", "Wand", "Sceptre" }
            .SelectMany(cls => Engine.ModsForClass(cls))
            .Where(m => m.Source is "augment" or "bonded")
            .DistinctBy(m => m.Id)
            .ToList();

        augments.Should().NotBeEmpty();
        augments.Should().OnlyContain(m =>
            m.GenerationType == "augment" &&
            m.Classes.Count > 0 &&
            m.StatLines.Count > 0 &&
            m.AugmentType.Length > 0);
        augments.Select(m => m.Source).Distinct(StringComparer.Ordinal)
            .Should().BeEquivalentTo(["augment", "bonded"]);
        augments.Select(m => m.AugmentType).Distinct(StringComparer.Ordinal)
            .Should().Contain(["Rune", "Soul Core", "Abyssal Eye"]);

        var targets = augments.Select(m => m.AugmentTarget).Distinct(StringComparer.Ordinal).ToList();
        targets.Should().Contain([
            "Armour", "Martial Weapon", "Wand or Staff", "Weapon", "Helmets",
            "Sceptres", "Foci", "All Equipment",
        ]);

        var adept = Engine.ModsForClass("Focus")
            .Where(m => m.Source == "augment" &&
                        m.AugmentType == "Rune" &&
                        m.AugmentTarget == "All Equipment" &&
                        m.Name?.EndsWith("Adept Rune", StringComparison.Ordinal) == true)
            .ToList();
        adept.Should().HaveCount(4, "the four Adept tiers add a non-duplicate pattern on Focus");
        adept.Should().OnlyContain(m => m.Classes.Contains("Focus", StringComparer.Ordinal));
    }

    [Fact]
    public void AugmentFamilies_DoNotDuplicatePatternsWithinAClass()
    {
        var duplicates = new List<string>();
        var seen = new Dictionary<(string Class, string Source, string Patterns), string>();
        foreach (var cls in new[] { "Helmet", "Focus", "Bow", "Wand", "Sceptre" })
        {
            foreach (var family in ModFamilies.Group(Engine.ModsForClass(cls))
                         .Where(f => f.IsAugmentLike))
            {
                var patterns = string.Join("\u0001",
                    PatternSet(family).OrderBy(x => x, StringComparer.Ordinal));
                var key = (cls, family.Source, patterns);
                if (seen.TryGetValue(key, out var other))
                    duplicates.Add($"{cls}: {family.Key} duplicates {other}");
                else
                    seen[key] = family.Key;
            }
        }
        duplicates.Should().BeEmpty();
    }

    [Fact]
    public void BondedRune_IsASeparateSourceWithItsOwnTag()
    {
        var family = ModFamilies.Group(Engine.ModsForClass("Helmet"))
            .Single(f => f.Key == "bonded:rune:Ward Rune:armour");

        family.IsBonded.Should().BeTrue();
        family.IsAugment.Should().BeFalse();
        family.IsAugmentLike.Should().BeTrue();
        family.SourceTag.Should().Be("Bonded · Rune · Armour");
        family.SourceCssClass.Should().Be("source-bonded");
        family.Tiers.Should().HaveCount(4);
        Engine.CompilePicks([family.Pick()], [], false, "Helmet").Query
            .Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Augments_AreNotInTheDefaultCommonShortlist()
    {
        using var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton(Engine);
        ctx.Services.AddSingleton(new PersistenceService(ctx.JSInterop.JSRuntime));
        ctx.Services.AddSingleton(new PreferencesState(ctx.JSInterop.JSRuntime));
        var state = new WorkbenchState();
        var mods = Engine.ModsForClass("Helmet");

        var cut = ctx.Render<ModPicker>(p => p
            .Add(x => x.State, state)
            .Add(x => x.Mods, mods));

        cut.FindAll(".mod-sec.augment-sec").Should().BeEmpty(
            "basic runes are a separate source, not generic common picks");
        cut.Find(".mod-scope").TextContent.Should().Contain("Common picks");

        cut.Find("#mod-search").Input("stone");
        cut.FindAll(".mod-sec.augment-sec").Should().ContainSingle();
    }

    [Fact]
    public void AugmentUniqueFilter_RemovesCoveredFamiliesAndTrimsClasses()
    {
        Engine.FindMod("augment:RuneFireLesser:armour").Should().BeNull(
            "normal fire resistance already covers the armour rune effect");
        Engine.FindMod("augment:RuneEnhance:martial-weapon").Should().BeNull(
            "normal increased physical damage covers the martial Iron Rune");
        Engine.FindMod("augment:RuneFireLesser:wand-or-staff").Should().BeNull(
            "normal extra fire damage covers the caster rune effect");

        var iron = Engine.FindMod("augment:RuneEnhance:armour")!;
        iron.StatLines.Should().ContainSingle()
            .Which.Line.Should().Be("16% increased Armour, Evasion and Energy Shield");
        iron.Classes.Should().BeEquivalentTo(["Buckler", "Focus", "Shield"]);
        iron.Classes.Should().NotContain(["Body Armour", "Helmet", "Gloves", "Boots"]);

        var body = Engine.FindMod("augment:RuneLife:armour")!;
        body.StatLines.Should().ContainSingle().Which.Line.Should().Be("+45 to maximum Life");
        body.Classes.Should().Equal(["Focus"]);

        Engine.FindMod("augment:RuneStun:armour")!.Classes
            .Should().BeEquivalentTo(["Focus", "Gloves", "Helmet"]);
        Engine.FindMod("augment:RuneStun:martial-weapon")!.Classes
            .Should().BeEquivalentTo(["Bow", "Crossbow"]);
        Engine.FindMod("augment:RuneManaRecovery:armour")!.Classes
            .Should().BeEquivalentTo(["Boots", "Buckler", "Gloves", "Shield"]);
    }

    [Fact]
    public void AugmentFamily_IsItsOwnSourceAndGroupsRuneTiers()
    {
        var family = ModFamilies.Group(Engine.ModsForClass("Helmet"))
            .Single(f => f.Key == "augment:rune:Stone Rune:armour");

        family.IsAugment.Should().BeTrue();
        family.Source.Should().Be("augment");
        family.Kind.Should().Be("augment");
        family.SourceTag.Should().Be("Rune · Armour");
        family.SourceCssClass.Should().Be("source-augment");
        family.Tiers.Should().HaveCount(4);
        family.Tiers.Select(t => t.DisplayLabel).Should().Equal(
            "Lesser Stone Rune", "Stone Rune", "Greater Stone Rune", "Perfect Stone Rune");
        family.Tiers.Select(t => t.RequiredLevel).Should().BeInAscendingOrder();

        var compiled = Engine.CompilePicks([family.Pick()], [], false, "Helmet");
        compiled.Query.Should().NotBeNullOrWhiteSpace();
        foreach (var tier in family.Tiers)
        {
            var line = AdvancedStatLine.For(tier.Anchor.StatLines[0]);
            ItemMatcher.Match(compiled.Query, ["Item Class: Helmets", line]).Match
                .Should().BeTrue($"the family pick should match every rune tier: {line}");
        }
    }

    [Fact]
    public void AllGearUnion_HidesOnlyAugmentsAlreadyCoveredByOneFamily()
    {
        var all = Engine.PlayableMods();
        var raw = ModFamilies.Group(all);
        var union = ModFamilies.Group(all, union: true);

        var rawAugments = raw.Where(f => f.IsAugmentLike).ToList();
        rawAugments.Should().NotBeEmpty();
        union.Where(f => f.IsAugmentLike).Should().NotBeEmpty();

        var nonAugment = union.Where(f => !f.IsAugmentLike).ToList();
        foreach (var augment in rawAugments)
        {
            var patterns = PatternSet(augment);
            var covering = nonAugment
                .Where(f => PatternSet(f).IsSupersetOf(patterns))
                .ToList();
            var unionAugment = union.FirstOrDefault(f => f.IsAugmentLike &&
                f.Members.Any(m => m.Id == augment.Anchor.Id));
            var augmentQuery = Engine.CompilePicks([augment.Pick()], [], false, null).Query;

            if (covering.Count == 0)
            {
                unionAugment.Should().NotBeNull(
                    $"{augment.Key} has no single non-augment row that covers its patterns");
            }
            else
            {
                unionAugment.Should().BeNull($"{augment.Key} is redundant in the All-gear union");
                covering.Select(f => Engine.CompilePicks([f.Pick()], [], false, null).Query)
                    .Should().Contain(augmentQuery,
                        $"{augment.Key} is only safe to hide when a visible row compiles the same query");
            }
        }
    }

    [Fact]
    public void PerSlotViews_KeepAugmentsThatAreUniqueForTheirClass()
    {
        var bow = ModFamilies.Group(Engine.ModsForClass("Bow"));
        bow.Should().Contain(f => f.IsAugment &&
            f.Members.Any(m => m.Family == "rune:Stone Rune:martial-weapon"));

        var helmet = ModFamilies.Group(Engine.ModsForClass("Helmet"));
        helmet.Should().Contain(f => f.IsAugment &&
            f.Members.Any(m => m.Family == "rune:Stone Rune:armour"));
    }

    [Fact]
    public void AllGearPicker_SearchStun_KeepsTheDistinctRune()
    {
        using var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton(Engine);
        ctx.Services.AddSingleton(new PersistenceService(ctx.JSInterop.JSRuntime));
        ctx.Services.AddSingleton(new PreferencesState(ctx.JSInterop.JSRuntime));
        var state = new WorkbenchState();

        var cut = ctx.Render<ModPicker>(p => p
            .Add(x => x.State, state)
            .Add(x => x.Mods, Engine.PlayableMods()));

        cut.Find("#mod-search").Input("stun");
        var stunBuildup = cut.FindAll(".fam-row")
            .Where(r => r.QuerySelector(".fam-text")?.TextContent.Trim() ==
                        "Causes % increased Stun Buildup")
            .ToList();
        stunBuildup.Should().ContainSingle(
            "the normal suffix and the Stone Rune share one query");
        stunBuildup[0].QuerySelector(".mod-source").Should().BeNull(
            "the retained row is the normal suffix, not the rune");

        cut.FindAll(".fam-row .fam-text")
            .Select(e => e.TextContent.Trim())
            .Should().Contain("Gain additional Stun Threshold equal to % of maximum Energy Shield",
                "the Wand/Staff Stone Rune effect has no normal-family equivalent");

        cut.Find("#mod-search").Input("flask");
        var vision = cut.FindAll(".fam-row").Single(r =>
            r.QuerySelector(".fam-text")?.TextContent.Trim() ==
            "% increased Life and Mana Recovery from Flasks");
        vision.QuerySelector(".mod-source")?.TextContent.Trim().Should().Be("Rune · Armour",
            "the Vision Rune effect has no normal-family equivalent");
    }

    private static HashSet<string> PatternSet(ModFamily family) =>
        family.Members
            .SelectMany(m => m.StatLines)
            .Select(line => line.Pattern)
            .ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void Picker_ShowsBondedRowsInTheAugmentsSection()
    {
        using var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton(Engine);
        ctx.Services.AddSingleton(new PersistenceService(ctx.JSInterop.JSRuntime));
        ctx.Services.AddSingleton(new PreferencesState(ctx.JSInterop.JSRuntime));
        var state = new WorkbenchState();

        var cut = ctx.Render<ModPicker>(p => p
            .Add(x => x.State, state)
            .Add(x => x.Mods, Engine.ModsForClass("Helmet")));

        cut.Find("#mod-search").Input("ward");
        cut.FindAll(".mod-sec.augment-sec").Should().ContainSingle();
        var bonded = cut.FindAll(".mod-source.source-bonded");
        bonded.Should().NotBeEmpty();
        bonded.Select(x => x.TextContent.Trim())
            .Should().Contain("Bonded · Rune · Armour");
    }

    [Fact]
    public void AugmentTierRows_HaveNoExactTierControls()
    {
        using var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton(Engine);
        ctx.Services.AddSingleton(new PersistenceService(ctx.JSInterop.JSRuntime));
        ctx.Services.AddSingleton(new PreferencesState(ctx.JSInterop.JSRuntime));
        var state = new WorkbenchState();
        state.SetMode("advanced");
        state.SelectSlot("helmet");
        var mods = Engine.ModsForClass("Helmet");
        var family = ModFamilies.Group(mods)
            .Single(f => f.Key == "augment:rune:Stone Rune:armour");

        var cut = ctx.Render<ModPicker>(p => p
            .Add(x => x.State, state)
            .Add(x => x.Scope, "helmet")
            .Add(x => x.Mods, mods));

        cut.Find("#mod-search").Input("stone");
        var row = cut.Find($"[id='pick-inc-{family.Anchor.Id}']").ParentElement!;
        var status = row.QuerySelector(".fam-status")!;
        status.LastElementChild!.ClassList.Should().Contain("mod-source");
        status.LastElementChild!.TextContent.Trim().Should().Be("Rune · Armour");
        row.QuerySelector(".fam-toggle .mod-source").Should().BeNull(
            "the rune target tag belongs in the right-aligned metadata");
        row.QuerySelector(".fam-toggle .tier-count").Should().NotBeNull(
            "the tier count stays with the mod text");

        cut.Find($"[id='fam-toggle-{family.Anchor.Id}']").Click();
        cut.FindAll($"[id='pick-tier-{family.Tiers[0].Anchor.Id}']").Should().BeEmpty(
            "basic-rune tiers are display-only; the family + selects the effect");
        cut.FindAll($"[id='pick-tier-exc-{family.Tiers[0].Anchor.Id}']").Should().BeEmpty();

        cut.Find($"[id='pick-inc-{family.Anchor.Id}']").Click();
        cut.Find($"[id='pick-inc-{family.Anchor.Id}']").GetAttribute("aria-pressed").Should().Be("true");
        state.CriteriaFor("helmet").Included.Should().ContainSingle();
        cut.FindAll(".tier-row .covered-note").Should().BeEmpty(
            "rune tiers are informational; the family row carries the selection");
        cut.FindAll(".tier-row.picked, .tier-row.covered, .tier-row.excluded").Should().BeEmpty(
            "informational rune tiers do not show interactive state");
    }
}

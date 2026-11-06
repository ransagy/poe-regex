using PoeRegex.Core.Data;
using PoeRegex.Web.Core;

namespace PoeRegex.Web.Tests;

/// <summary>
/// Abyss desecrated mods are item prefixes/suffixes with their own source.
/// They share the picker/corpus path with explicit affixes but must never
/// merge into a normal family with the same raw family key or text.
/// </summary>
public class DesecratedTests
{
    private static readonly EngineService Engine = new();

    [Fact]
    public void Catalog_HasItemDesecratedModsWithClasses()
    {
        var desecrated = Engine.PlayableMods()
            .Where(m => m.Source == "desecrated")
            .ToList();

        desecrated.Should().HaveCount(197);
        desecrated.Should().OnlyContain(m =>
            m.GenerationType == "prefix" || m.GenerationType == "suffix");
        desecrated.Should().OnlyContain(m => m.Classes.Count > 0);

        var helmet = desecrated.Single(m => m.Id == "AbyssModHelmUlamanSuffixMarkedEnemyTakeIncreasedDamage");
        helmet.Classes.Should().Equal(["Helmet"]);
        helmet.GenerationType.Should().Be("suffix");
        helmet.StatLines.Should().ContainSingle();
    }

    [Fact]
    public void Families_KeepDesecratedRowsSeparate()
    {
        var families = ModFamilies.Group(Engine.ModsForClass("Helmet"));
        var normal = families.Single(f => f.Key == "IncreasedLife");
        var desecrated = families.Single(f =>
            f.Key == "desecrated:MarkedEnemyTakesIncreasedDamage");

        normal.Source.Should().BeEmpty();
        normal.IsDesecrated.Should().BeFalse();
        desecrated.Source.Should().Be("desecrated");
        desecrated.IsDesecrated.Should().BeTrue();
        desecrated.IsPrefix.Should().BeFalse();
        desecrated.DisplayText.Should().Contain("Mark");
    }

    [Fact]
    public void BreakArmourMaces_AreScopedToOneAndTwoHanded()
    {
        const string oneId = "AbyssMod1HMaceAmanamuSuffixBreakPercentArmourPhysicalDamage";
        const string twoId = "AbyssMod2HMaceAmanamuSuffixBreakPercentArmourPhysicalDamage";

        var one = Engine.FindMod(oneId)!;
        one.Classes.Should().Equal(["One Hand Mace"]);
        var two = Engine.FindMod(twoId)!;
        two.Classes.Should().Equal(["Two Hand Mace"]);

        var oneFamily = ModFamilies.Group(Engine.ModsForClass("One Hand Mace"))
            .Single(f => f.Key == "desecrated:ArmourPenetration");
        oneFamily.SourceTag.Should().Be("Desecrated · Amanamu");
        oneFamily.Tiers.Should().ContainSingle();
        oneFamily.Tiers[0].DisplayLines.Should().ContainSingle()
            .Which.Should().Contain("2–4");

        var twoFamily = ModFamilies.Group(Engine.ModsForClass("Two Hand Mace"))
            .Single(f => f.Key == "desecrated:ArmourPenetration");
        twoFamily.Tiers.Should().ContainSingle();
        twoFamily.Tiers[0].DisplayLines.Should().ContainSingle()
            .Which.Should().Contain("4–7");
    }

    [Fact]
    public void BreakArmourGlobal_CollapsesRangesSharingOneFragment()
    {
        var family = ModFamilies.Group(Engine.PlayableMods())
            .Single(f => f.Key == "desecrated:ArmourPenetration");

        family.SourceTag.Should().Be("Desecrated · Amanamu");
        family.Tiers.Should().ContainSingle();
        var tier = family.Tiers[0];
        tier.Members.Should().HaveCount(2);
        tier.DisplayLabel.Should().Be("2–4% / 4–7%");
        tier.DisplayLines.Should().ContainSingle().Which.Should().Contain("2–4");
        tier.UnionLines.Should().Equal(
            ["Break Armour equal to 2–4 / 4–7% of Physical Damage dealt"]);
    }

    [Fact]
    public void DesecratedFamilies_HaveNoTwoTiersWithTheSameFragment()
    {
        var scopes = new List<(string? Class, IReadOnlyList<ModEntry> Mods)>
        {
            (null, Engine.PlayableMods()),
        };
        scopes.AddRange(Engine.PlayableClasses()
            .Select(c => ((string?)c.Id, Engine.ModsForClass(c.Id))));

        foreach (var (cls, mods) in scopes)
        {
            foreach (var family in ModFamilies.Group(mods).Where(f => f.IsDesecrated))
            {
                var fragments = family.Tiers
                    .Select(t => Engine.PickFragment(new WantedMod(t.Anchor.Id), cls))
                    .ToList();
                fragments.Should().OnlyHaveUniqueItems(
                    $"{cls ?? "global"}/{family.Key} must not show two tiers for one fragment");
            }
        }
    }

    [Fact]
    public void DescendingRangeTier_KeepsItsDisplayedRange()
    {
        var family = ModFamilies.Group(Engine.ModsForClass("Boots"))
            .Single(f => f.Key == "desecrated:ReducedPoisonDuration");

        family.Tiers.Should().ContainSingle();
        family.Tiers[0].DisplayLabel.Should().Be("30–20%");
    }

    [Fact]
    public void BreakArmourGlobalFragment_MatchesBothRangeDisplays()
    {
        var mod = Engine.FindMod(
            "AbyssMod1HMaceAmanamuSuffixBreakPercentArmourPhysicalDamage")!;
        var compiled = Engine.CompilePicks(
            [new WantedMod(mod.Id)], [], matchAll: false, itemClass: null);

        foreach (var line in new[]
                 {
                     "Break Armour equal to 2(2-4)% of Physical Damage dealt",
                     "Break Armour equal to 4(2-4)% of Physical Damage dealt",
                     "Break Armour equal to 4(4-7)% of Physical Damage dealt",
                     "Break Armour equal to 7(4-7)% of Physical Damage dealt",
                 })
        {
            ItemMatcher.Match(compiled.Query, [line]).Match
                .Should().BeTrue($"{compiled.Query} should match '{line}'");
        }
    }

    [Fact]
    public void Compile_DesecratedHelmetPick_ProducesExactFragment()
    {
        var mod = Engine.ModsForClass("Helmet")
            .Single(m => m.Id == "AbyssModHelmUlamanSuffixMarkedEnemyTakeIncreasedDamage");

        var compiled = Engine.CompilePicks(
            [new WantedMod(mod.Id)], [], matchAll: false, itemClass: "Helmet");

        compiled.Query.Should().NotBeNullOrWhiteSpace();
        compiled.Exceeded.Should().BeFalse();
        var line = AdvancedStatLine.For(mod.StatLines[0]);
        ItemMatcher.Match(compiled.Query, ["Item Class: Helmets", line])
            .Match.Should().BeTrue();
    }
}

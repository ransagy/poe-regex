using PoeRegex.Core;
using PoeRegex.Core.Data;
using PoeRegex.Web.Core;

namespace PoeRegex.Web.Tests;

/// <summary>
/// Crafted mods share the explicit prefix/suffix path but carry their own
/// source, class mapping, and picker tag. Their classes are inferred from the
/// raw crafted records because those records carry no positive spawn weights.
/// </summary>
public class CraftedTests
{
    private static readonly EngineService Engine = new();

    [Fact]
    public void Catalog_HasCraftedModsWithSourceAndClasses()
    {
        var crafted = Engine.PlayableMods()
            .Where(m => m.Source == "crafted")
            .ToList();

        crafted.Should().HaveCount(9);
        crafted.Should().OnlyContain(m =>
            m.GenerationType == "prefix" || m.GenerationType == "suffix");
        crafted.Should().OnlyContain(m => m.Classes.Count > 0);

        var energyShield = crafted.Single(m => m.Id == "EnergyShieldPercentOffSlot");
        energyShield.Classes.Should().BeEquivalentTo(
            ["Body Armour", "Helmet", "Gloves", "Boots", "Shield", "Focus"]);

        var evasion = crafted.Single(m => m.Id == "StrIntMasterCraftedEvasionRating");
        evasion.Classes.Should().BeEquivalentTo(
            ["Body Armour", "Helmet", "Gloves", "Boots", "Shield", "Buckler"]);

        var armour = crafted.Single(m => m.Id == "StrIntMasterCraftedArmourRating");
        armour.Classes.Should().BeEquivalentTo(
            ["Body Armour", "Helmet", "Gloves", "Boots", "Shield"]);
    }

    [Fact]
    public void GenericMetaCrafts_ApplyToEveryNormalEquipmentClass()
    {
        var equipment = Engine.InGameClasses()
            .Select(c => c.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        foreach (var id in new[]
                 {
                     "StrMasterItemGenerationCannotChangePrefixes",
                     "DexMasterItemGenerationCannotChangeSuffixes",
                     "IntMasterItemGenerationCannotRollAttackAffixes",
                     "StrDexMasterItemGenerationCannotRollCasterAffixes",
                     "StrIntMasterItemGenerationCanHaveMultipleCraftedMods",
                     "CraftedTravelSkillsCannotBeExerted",
                 })
        {
            var mod = Engine.FindMod(id)!;
            mod.Source.Should().Be("crafted");
            mod.Classes.OrderBy(x => x, StringComparer.Ordinal)
                .Should().BeEquivalentTo(equipment, id);
        }
    }

    [Fact]
    public void Families_KeepCraftedRowsSeparateFromNormal()
    {
        var families = ModFamilies.Group(Engine.ModsForClass("Helmet"));
        var crafted = families.Single(f => f.Key == "crafted:GlobalEvasionRatingPercent");
        var normal = families.Single(f => f.Key == "LocalEvasionRatingIncreasePercent");

        crafted.Source.Should().Be("crafted");
        crafted.IsCrafted.Should().BeTrue();
        crafted.IsDesecrated.Should().BeFalse();
        crafted.SourceTag.Should().Be("Crafted");
        crafted.SourceCssClass.Should().Be("source-crafted");
        crafted.DisplayText.Should().Contain("Evasion");

        normal.Source.Should().BeEmpty();
        normal.IsCrafted.Should().BeFalse();
        normal.SourceTag.Should().BeNull();
    }

    [Fact]
    public void CraftedStatCrafts_CompileOnEveryMappedClass()
    {
        var ids = new[]
        {
            "EnergyShieldPercentOffSlot",
            "StrIntMasterCraftedEvasionRating",
            "StrIntMasterCraftedArmourRating",
        };
        foreach (var id in ids)
        {
            var mod = Engine.FindMod(id)!;
            foreach (var classId in mod.Classes)
            {
                var compiled = Engine.CompilePicks(
                    [new WantedMod(mod.Id)], [], matchAll: false, itemClass: classId);
                compiled.Query.Should().NotBeNullOrWhiteSpace($"{mod.Id} on {classId}");
                compiled.Exceeded.Should().BeFalse();
                var classLine = $"Item Class: {Engine.Classes.Single(c => c.Id == classId).Name}";
                var line = AdvancedStatLine.For(mod.StatLines[0]);
                ItemMatcher.Match(compiled.Query, [classLine, line]).Match
                    .Should().BeTrue($"{compiled.Query} should match '{line}' on {classId}");
            }
        }
    }
}

using PoeRegex.Web.Core;

namespace PoeRegex.Web.Tests;

/// <summary>
/// The PoE2 raw export also carries PoE1 master-craft records in the
/// `crafted` domain. They have no positive PoE2 base mapping and must not
/// enter the catalog. PoE2's actual crafted mods are jewel-only
/// (`CraftedJewel*`, domain `misc`), handled with jewel support.
/// </summary>
public class CraftedLegacyTests
{
    private static readonly EngineService Engine = new();

    [Fact]
    public void Catalog_ExcludesLegacyPoe1CraftedMods()
    {
        var legacyIds = new[]
        {
            "EnergyShieldPercentOffSlot",
            "StrIntMasterCraftedEvasionRating",
            "StrIntMasterCraftedArmourRating",
            "StrMasterItemGenerationCannotChangePrefixes",
            "DexMasterItemGenerationCannotChangeSuffixes",
            "IntMasterItemGenerationCannotRollAttackAffixes",
            "StrDexMasterItemGenerationCannotRollCasterAffixes",
            "StrIntMasterItemGenerationCanHaveMultipleCraftedMods",
            "CraftedTravelSkillsCannotBeExerted",
        };

        Engine.PlayableMods().Should().NotContain(m => m.Source == "crafted");
        foreach (var id in legacyIds)
            Engine.FindMod(id).Should().BeNull($"{id} is a PoE1 legacy record");
    }
}

using PoeRegex.Data.Build;
using PoeRegex.Data.Build.RePoE;

namespace PoeRegex.Data.Build.Tests;

/// <summary>
/// Crafted records carry no positive spawn weights. Defence crafts expose the
/// defence family in implicit_tags, so their classes come from released base
/// tags; generic meta-crafts have no tag and apply to every normal equipment
/// class. This is deliberately class-level, matching the catalog's scope.
/// </summary>
public class CraftedMappingTests
{
    private static RePoESchema.BaseItem Base(
        string itemClass, string name, params string[] tags) =>
        new()
        {
            Domain = "item",
            ReleaseState = "released",
            ItemClass = itemClass,
            Name = name,
            Tags = [.. tags],
        };

    private static RePoESchema.Mod Mod(string generation, params string[] implicitTags) =>
        new()
        {
            Domain = "crafted",
            GenerationType = generation,
            Stats = [],
            ImplicitTags = [.. implicitTags],
        };

    [Fact]
    public void MapsDefenceCraftsByBaseTagsAndMetaCraftsToAllEquipment()
    {
        var bases = new Dictionary<string, RePoESchema.BaseItem>(StringComparer.Ordinal)
        {
            ["Metadata/Items/Armours/TestBody"] =
                Base("Body Armour", "Test Body",
                    "str_armour", "dex_armour", "int_armour", "body_armour", "armour"),
            ["Metadata/Items/Armours/TestHelmet"] =
                Base("Helmet", "Test Helmet", "int_armour", "helmet", "armour"),
            ["Metadata/Items/Offhands/TestBuckler"] =
                Base("Buckler", "Test Buckler", "dex_armour", "buckler", "armour"),
            ["Metadata/Items/Rings/TestRing"] =
                Base("Ring", "Test Ring", "ring", "default"),
        };
        var equipment = new HashSet<string>(
            ["Body Armour", "Helmet", "Buckler", "Ring"], StringComparer.Ordinal);
        var mods = new Dictionary<string, RePoESchema.Mod>(StringComparer.Ordinal)
        {
            ["ArmourCraft"] = Mod("suffix", "defences", "armour"),
            ["EvasionCraft"] = Mod("suffix", "defences", "evasion"),
            ["EnergyShieldCraft"] = Mod("suffix", "defences", "energy_shield"),
            ["MetaCraft"] = Mod("suffix"),
            ["NormalMod"] = new RePoESchema.Mod
            {
                Domain = "item",
                GenerationType = "prefix",
                Stats = [],
            },
        };

        var result = CatalogBuilder.MapCraftedModsToClasses(mods, bases, equipment);

        result.Keys.Should().BeEquivalentTo(
            ["ArmourCraft", "EvasionCraft", "EnergyShieldCraft", "MetaCraft"]);
        result["ArmourCraft"].Should().BeEquivalentTo(["Body Armour"]);
        result["EvasionCraft"].Should().BeEquivalentTo(["Body Armour", "Buckler"]);
        result["EnergyShieldCraft"].Should().BeEquivalentTo(["Body Armour", "Helmet"]);
        result["MetaCraft"].Should().BeEquivalentTo(equipment);
    }
}

using PoeRegex.Data.Build;
using PoeRegex.Data.Build.RePoE;

namespace PoeRegex.Data.Build.Tests;

/// <summary>
/// Desecrated mods are not in mods_by_base, so their classes come from
/// positive spawn-weight tags intersected with released, non-unique base
/// items. Zero-weight exclusion tags (for example one_hand_weapon/two_hand_weapon)
/// must subtract base-level eligibility, and map/tablet-only desecrated mods
/// must stay out.
/// </summary>
public class DesecratedMappingTests
{
    private static RePoESchema.ItemClass ItemClass(string id, string name) =>
        new() { Id = id, Name = name, Category = "[Equipment]" };

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

    private static RePoESchema.Mod Mod(
        string domain, string generation, params (string Tag, int Weight)[] spawn) =>
        new()
        {
            Domain = domain,
            GenerationType = generation,
            Stats = [],
            SpawnWeights = spawn
                .Select(x => new RePoESchema.SpawnWeight { Tag = x.Tag, Weight = x.Weight })
                .ToList(),
        };

    [Fact]
    public void MapsOnlyItemEligibleDesecratedMods()
    {
        var classes = new Dictionary<string, RePoESchema.ItemClass>(StringComparer.Ordinal)
        {
            ["Helmet"] = ItemClass("Helmet", "Helmets"),
            ["Ring"] = ItemClass("Ring", "Rings"),
            ["Amulet"] = ItemClass("Amulet", "Amulets"),
            ["One Hand Mace"] = ItemClass("One Hand Mace", "One Hand Maces"),
            ["Two Hand Mace"] = ItemClass("Two Hand Mace", "Two Hand Maces"),
        };
        var bases = new Dictionary<string, RePoESchema.BaseItem>(StringComparer.Ordinal)
        {
            ["Metadata/Items/Armours/TestHelmet"] =
                Base("Helmet", "Test Helmet", "helmet", "armour", "default"),
            ["Metadata/Items/Rings/TestRing"] =
                Base("Ring", "Test Ring", "ring", "default"),
            ["Metadata/Items/Amulets/TestAmulet"] =
                Base("Amulet", "Test Amulet", "amulet", "default"),
            ["Metadata/Items/Weapons/TestOneHandMace"] =
                Base("One Hand Mace", "Test One Hand Mace",
                    "mace", "one_hand_weapon", "default"),
            ["Metadata/Items/Weapons/TestTwoHandMace"] =
                Base("Two Hand Mace", "Test Two Hand Mace",
                    "mace", "two_hand_weapon", "default"),
            // Unique-only bases must not widen class coverage.
            ["Metadata/Items/Armours/TestHelmetUnique"] =
                Base("Helmet", "Unique Helmet", "helmet", "armour", "default"),
        };
        var mods = new Dictionary<string, RePoESchema.Mod>(StringComparer.Ordinal)
        {
            ["HelmetMod"] = Mod("desecrated", "prefix",
                ("helmet", 1), ("default", 0)),
            ["JewelleryMod"] = Mod("desecrated", "suffix",
                ("ring", 1), ("amulet", 1), ("default", 0)),
            ["OneHandMaceMod"] = Mod("desecrated", "suffix",
                ("mace", 1), ("two_hand_weapon", 0), ("default", 0)),
            ["TwoHandMaceMod"] = Mod("desecrated", "suffix",
                ("mace", 1), ("one_hand_weapon", 0), ("default", 0)),
            ["MapMod"] = Mod("desecrated", "prefix",
                ("map", 1), ("default", 0)),
            ["NormalMod"] = Mod("item", "prefix",
                ("helmet", 1), ("default", 0)),
        };

        var result = CatalogBuilder.MapDesecratedModsToClasses(mods, bases, classes);

        result.Keys.Should().BeEquivalentTo(
            ["HelmetMod", "JewelleryMod", "OneHandMaceMod", "TwoHandMaceMod"]);
        result["HelmetMod"].Should().BeEquivalentTo(["Helmet"]);
        result["JewelleryMod"].Should().BeEquivalentTo(["Ring", "Amulet"]);
        result["OneHandMaceMod"].Should().BeEquivalentTo(["One Hand Mace"]);
        result["TwoHandMaceMod"].Should().BeEquivalentTo(["Two Hand Mace"]);
    }
}

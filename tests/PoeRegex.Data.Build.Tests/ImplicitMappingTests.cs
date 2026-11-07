using PoeRegex.Data.Build;
using PoeRegex.Data.Build.RePoE;

namespace PoeRegex.Data.Build.Tests;

/// <summary>
/// Base implicits are mapped from released, non-unique bases in the normal
/// equipment classes. The catalog adds only implicit-only patterns; this
/// mapping is the positive PoE2 reference used to choose their classes.
/// </summary>
public class ImplicitMappingTests
{
    private static RePoESchema.BaseItem Base(
        string itemClass, string name, params string[] implicits) =>
        new()
        {
            Domain = "item",
            ReleaseState = "released",
            ItemClass = itemClass,
            Name = name,
            Implicits = [.. implicits],
        };

    private static RePoESchema.Mod Mod() =>
        new()
        {
            Domain = "item",
            GenerationType = "unique",
            Stats = [],
        };

    [Fact]
    public void MapsImplicitsFromReleasedNonUniqueEquipment()
    {
        var mods = new Dictionary<string, RePoESchema.Mod>(StringComparer.Ordinal)
        {
            ["SharedImplicit"] = Mod(),
            ["HelmetOnly"] = Mod(),
        };
        var bases = new Dictionary<string, RePoESchema.BaseItem>(StringComparer.Ordinal)
        {
            ["Metadata/Items/Armours/TestHelmet"] =
                Base("Helmet", "Test Helmet", "SharedImplicit", "HelmetOnly"),
            ["Metadata/Items/Armours/TestBody"] =
                Base("Body Armour", "Test Body", "SharedImplicit"),
            ["Metadata/Items/Armours/TestHelmetUnique"] =
                Base("Helmet", "Unique Helmet", "HelmetOnly"),
            ["Metadata/Items/Rings/TestRing"] =
                Base("Ring", "Test Ring", "SharedImplicit"),
        };
        var equipment = new HashSet<string>(
            ["Helmet", "Body Armour", "Ring"], StringComparer.Ordinal);

        var result = CatalogBuilder.MapImplicitModsToClasses(mods, bases, equipment);

        result.Keys.Should().BeEquivalentTo(["SharedImplicit", "HelmetOnly"]);
        result["SharedImplicit"].Should().BeEquivalentTo(["Helmet", "Body Armour", "Ring"]);
        result["HelmetOnly"].Should().BeEquivalentTo(["Helmet"]);
    }

    [Fact]
    public void IncludesReleasedRuneforgedAndAscendancyBases()
    {
        var mods = new Dictionary<string, RePoESchema.Mod>(StringComparer.Ordinal)
        {
            ["RuneforgedImplicit"] = Mod(),
            ["AscendancyImplicit"] = Mod(),
        };
        var bases = new Dictionary<string, RePoESchema.BaseItem>(StringComparer.Ordinal)
        {
            ["Metadata/Items/Weapons/TestRuneforged"] = new()
            {
                Domain = "item",
                ReleaseState = "released",
                ItemClass = "One Hand Mace",
                Name = "Runeforged Test Mace",
                Tags = ["runeforged"],
                Implicits = ["RuneforgedImplicit"],
            },
            ["Metadata/Items/Armours/TestAscendancy"] = new()
            {
                Domain = "item",
                ReleaseState = "released",
                ItemClass = "Gloves",
                Name = "Fists of Stone",
                Implicits = ["AscendancyImplicit"],
            },
        };
        var equipment = new HashSet<string>(
            ["One Hand Mace", "Gloves"], StringComparer.Ordinal);

        var result = CatalogBuilder.MapImplicitModsToClasses(mods, bases, equipment);

        result["RuneforgedImplicit"].Should().BeEquivalentTo(["One Hand Mace"]);
        result["AscendancyImplicit"].Should().BeEquivalentTo(["Gloves"]);
    }

    [Fact]
    public void SelectsMissingScopesPerClassInsteadOfGlobalPattern()
    {
        var patterns = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["A"] = ["P1"],
            ["B"] = ["P2"],
            ["C"] = ["P1", "P3"],
            ["D"] = ["P4"],
            ["E"] = ["P5"],
            ["F"] = ["P5"],
        };
        var classes = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            ["A"] = ["Ring", "Belt"],
            ["B"] = ["Ring", "Belt"],
            ["C"] = ["Ring"],
            ["D"] = ["Ring", "Belt"],
            ["E"] = ["Ring", "Belt"],
            ["F"] = ["Ring", "Belt"],
        };
        var covered = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            ["Ring"] = ["P1", "P2", "P4"],
        };

        var selected = CatalogBuilder.SelectMissingScopes(patterns, classes, covered);

        // Each mod is kept only for the class where its line is still missing.
        selected["A"].Should().BeEquivalentTo(["Belt"]);
        selected["B"].Should().BeEquivalentTo(["Belt"]);
        selected["C"].Should().BeEquivalentTo(["Ring"],
            "a multi-line mod is kept when any line is new on the class");
        selected["D"].Should().BeEquivalentTo(["Belt"]);
        // Implicit tiers sharing a pattern stay in the catalog; the family
        // grouping merges them and preserves their ranges.
        selected["E"].Should().BeEquivalentTo(["Ring", "Belt"]);
        selected["F"].Should().BeEquivalentTo(["Ring", "Belt"]);
    }
}

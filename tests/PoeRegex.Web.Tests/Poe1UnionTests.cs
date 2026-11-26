using System.Text.Json;
using PoeRegex.Core.Compilation;
using PoeRegex.Core.Data;
using PoeRegex.Web.Core;

namespace PoeRegex.Web.Tests;

/// <summary>
/// The same any-of union path runs for PoE1 normal affixes. These fixtures
/// come from a real PoE1 picker state; the MatchAll Boots and narrowed Ring
/// variants are derived from it.
/// </summary>
public class Poe1UnionTests
{
    private static readonly string Root =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));

    private static EngineService Engine()
    {
        var dir = Path.Combine(Root, "data", "catalogs");
        var catalog = new FileCatalogSource(Path.Combine(dir, "poe1.catalog.json")).Load();
        var table = JsonSerializer.Deserialize(
            File.ReadAllText(Path.Combine(dir, "poe1.fragments.json")),
            CatalogJsonContext.Default.FragmentTable)!;
        var vendor = JsonSerializer.Deserialize(
            File.ReadAllText(Path.Combine(dir, "poe1.vendor-corpus.json")),
            CatalogJsonContext.Default.VendorCorpusFile)!;
        return new EngineService(new GameData(catalog, table, vendor));
    }

    private static (EngineService Engine, MultiPasteResult Result) Compose(string fixture)
    {
        var engine = Engine();
        var snap = JsonSerializer.Deserialize(
            File.ReadAllText(Path.Combine(
                Root, "tests", "PoeRegex.Web.Tests", "Fixtures", "StringBudget", fixture)),
            WebJsonContext.Default.WorkbenchSnapshot)!;
        var state = new WorkbenchState();
        WorkbenchSnapshot.Restore(state, snap);
        var slots = state.ActiveSlots()
            .Select(slot =>
            {
                var crit = state.CriteriaFor(slot);
                return (slot, crit.ClassId, (IEnumerable<WantedMod>)crit.Included, crit.MatchAll);
            })
            .ToList();
        var excludes = state.ActiveSlots()
            .SelectMany(slot => state.CriteriaFor(slot).Excluded)
            .Concat(state.Global.Excluded)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return (engine, engine.Combine(slots, excludes, allowOverflow: true));
    }

    [Fact]
    public void PoE1BudgetState_UnionsRingAndGlovesDamage()
    {
        var (engine, result) = Compose("poe1-budget.json");

        result.Length.Should().Be(211);
        result.Length.Should().BeLessThanOrEqualTo(250);
        result.Groups.Should().HaveCount(4);
        result.Groups.Should().Contain(group => group.TargetClasses.Contains("Gloves") &&
                                                group.TargetClasses.Contains("Ring"));
        Match(engine, result, "Rings", "Adds 1 to 2 Cold Damage to Attacks").Should().BeTrue();
        Match(engine, result, "Rings", "Adds 17 to 40 Cold Damage to Attacks").Should().BeTrue(
            "the Ring keeps every cold tier even though Gloves only has the first six");
        Match(engine, result, "Gloves", "Adds 1 to 2 Fire Damage to Attacks").Should().BeTrue();
        Match(engine, result, "Body Armours", "+180 to maximum Life").Should().BeTrue(
            "Body Armour keeps its 175–189 tier when the other life slots stop at 144");
    }

    [Fact]
    public void PoE1NarrowedRingRange_DoesNotJoinTheSharedDamageUnion()
    {
        var (engine, result) = Compose("poe1-narrow-ring.json");

        result.Length.Should().Be(238,
            "the narrowed Ring stays exact while the other groups still shorten");
        result.Length.Should().BeLessThanOrEqualTo(250);
        Match(engine, result, "Rings", "Adds 1 to 2 Cold Damage to Attacks").Should().BeTrue();
        Match(engine, result, "Rings", "Adds 3 to 8 Cold Damage to Attacks").Should().BeFalse(
            "the Ring was narrowed to the first cold tier; a shared union must not widen it");
        Match(engine, result, "Gloves", "Adds 3 to 8 Cold Damage to Attacks").Should().BeTrue();
        Match(engine, result, "Rings", "Adds 1 to 2 Fire Damage to Attacks").Should().BeTrue();
        Match(engine, result, "Gloves", "Adds 1 to 2 Fire Damage to Attacks").Should().BeTrue();
    }

    [Fact]
    public void PoE1MatchAllBoots_DoesNotBlockOtherUnions()
    {
        var (engine, result) = Compose("poe1-matchall-boots.json");

        result.Length.Should().Be(234);
        result.Length.Should().BeLessThanOrEqualTo(250);
        result.Groups.Should().HaveCount(5);
        result.Groups.Should().Contain(group =>
            group.TargetClasses.Count == 1 && group.TargetClasses[0] == "Boots");
        ItemMatcher.Match(
            result.Query,
            [
                "Item Class: Boots",
                "25% increased Movement Speed",
                "+120 to maximum Life",
            ],
            engine.Syntax).Match.Should().BeTrue();
        ItemMatcher.Match(
            result.Query,
            ["Item Class: Boots", "25% increased Movement Speed"],
            engine.Syntax).Match.Should().BeFalse();
        ItemMatcher.Match(
            result.Query,
            ["Item Class: Boots", "+120 to maximum Life"],
            engine.Syntax).Match.Should().BeFalse();
    }

    private static bool Match(
        EngineService engine, MultiPasteResult result, string itemClass, string line) =>
        ItemMatcher.Match(
            result.Query, [$"Item Class: {itemClass}", line], engine.Syntax).Match;
}

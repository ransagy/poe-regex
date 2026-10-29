using PoeRegex.Core;
using PoeRegex.Core.Compilation;
using PoeRegex.Core.Data;
using PoeRegex.Web.Core;

namespace PoeRegex.Web.Tests;

/// <summary>
/// Web data-service tests: workbench state semantics (filter,
/// available, union, playable classes, meter) plus engine parity through
/// <see cref="EngineService"/> (determinism, exact-query match with the CLI,
/// explain recall, validate).
/// </summary>
public class WebDataServiceTests
{
    private static readonly EngineService Engine = new();

    [Fact]
    public void PlayableClasses_AllHaveExplicitMods()
    {
        var playable = Engine.PlayableClasses().ToList();
        playable.Should().NotBeEmpty();
        playable.Should().Contain(c => c.Id == "Ring");
        foreach (var cls in playable)
            Engine.ModsForClass(cls.Id).Should().NotBeEmpty($"class {cls.Id} is advertised as playable");
    }

    [Fact]
    public void InGameClasses_ExcludesUnreleasedAndDataminedBases()
    {
        var ids = Engine.InGameClasses().Select(c => c.Id).ToList();
        ids.Should().NotContain([
            "Dagger", "Claw", "Flail",
            "One Hand Sword", "One Hand Axe", "Two Hand Sword", "Two Hand Axe",
            "FishingRod", "TrapTool"]);
        ids.Should().Contain(["Ring", "Crossbow", "Bow", "Staff", "Spear", "Wand",
            "Sceptre", "Shield", "Focus", "Buckler", "Quiver", "Warstaff", "Talisman"]);
        ids.Should().HaveCount(Engine.PlayableClasses().Count() - 9);
    }

    [Fact]
    public void FilterMods_SearchesNameAndLine_CaseInsensitively()
    {
        var ring = Engine.ModsForClass("Ring");
        var byName = ModSearch.FilterMods(ring, "brute");
        var byUpper = ModSearch.FilterMods(ring, "BRUTE");
        byName.Should().NotBeEmpty();
        byUpper.Select(m => m.Id).Should().BeEquivalentTo(byName.Select(m => m.Id));

        var byLine = ModSearch.FilterMods(ring, "maximum Life");
        byLine.Should().Contain(m => m.Id == "IncreasedLife6");
        ModSearch.FilterMods(ring, "IncreasedLife6").Should().BeEmpty(
            "internal mod IDs are not user-facing search text");

        ModSearch.FilterMods(ring, "   ").Should().HaveCount(ring.Count);
        ModSearch.FilterMods(ring, "zzz-no-such-mod").Should().BeEmpty();
    }

    [Fact]
    public void DisplayLine_PrefersRange_FallsBackToLine()
    {
        var mod = Engine.FindMod("IncreasedLife6")!;
        var sl = mod.StatLines[0];
        sl.RangeLine.Should().NotBeEmpty();
        ModSearch.DisplayLine(sl).Should().Be(sl.RangeLine);
        var legacy = new StatLineEntry
        {
            Line = sl.Line,
            Pattern = sl.Pattern,
            MinValue = sl.MinValue,
            MaxValue = sl.MaxValue,
            Fingerprints = sl.Fingerprints,
        };
        ModSearch.DisplayLine(legacy).Should().Be(sl.Line);
    }

    [Fact]
    public void FilterMods_MatchesRangeText()
    {
        var ring = Engine.ModsForClass("Ring");
        var sl = Engine.FindMod("IncreasedLife6")!.StatLines[0];
        // A distinctive slice of the range rendering still resolves the mod.
        var probe = sl.RangeLine.Replace("–", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(w => w.Any(char.IsDigit));
        probe.Should().NotBeNull();
        ModSearch.FilterMods(ring, probe!).Should().Contain(m => m.Id == "IncreasedLife6");
    }

    [Fact]
    public void AvailableMods_HidesTakenIds()
    {
        var ring = Engine.ModsForClass("Ring");
        var avail = ModSearch.AvailableMods(ring, ["IncreasedLife6"]);
        avail.Should().NotContain(m => m.Id == "IncreasedLife6");
        avail.Should().HaveCount(ring.Count - 1);
    }

    [Fact]
    public void MergeMods_UnionsAndDedupes_FirstWins()
    {
        var ring = Engine.ModsForClass("Ring").Take(3).ToList();
        var amulet = Engine.ModsForClass("Amulet").Take(3).ToList();
        var merged = ModSearch.MergeMods([ring, amulet]);
        merged.Select(m => m.Id).Should().OnlyHaveUniqueItems();
        merged.Count.Should().BeLessThanOrEqualTo(6);
        merged.Take(3).Select(m => m.Id).Should()
            .BeEquivalentTo(ring.Select(m => m.Id), "first list wins order");
    }

    [Fact]
    public void Meter_ColorThresholds()
    {
        ModSearch.MeterColor(10, 250).Should().Be("green");
        ModSearch.MeterColor(150, 250).Should().Be("orange");
        ModSearch.MeterColor(225, 250).Should().Be("red");
        ModSearch.MeterRatio(300, 250).Should().Be(1);
    }

    [Fact]
    public void Compile_SampleSpec_IsDeterministic_AndMatchesCli()
    {
        var includes = new[] { new ModifierRequirement("IncreasedLife6", 80) };
        var excludes = new[] { new ModifierRequirement("reflect") };
        var first = Engine.Compile(includes, excludes, itemClass: "Ring");
        var second = Engine.Compile(includes, excludes, itemClass: "Ring");
        second.Query.Should().Be(first.Query);
        // Byte parity with `poe-regex generate --class Ring
        // --min IncreasedLife6:80 --exclude reflect`.
        first.Query.Should().Be("\": Ri\" \"\\+(\\d{3,}|[9-9]\\d|8[0-9])\\(\\d+-\\d+\\) to maximum Life\" !\"reflect\"");
        first.Limit.Should().Be(250);
        first.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void Explain_CompiledQuery_RecallsSourceMod()
    {
        var result = Engine.Compile(
            [new ModifierRequirement("IncreasedLife6", 80)],
            [new ModifierRequirement("reflect")],
            itemClass: "Ring");
        var explained = Engine.Explain(result.Query, "Ring");
        explained.Mods.Should().Contain(m => m.Id == "IncreasedLife6");
    }

    [Fact]
    public async Task Explain_IsCachedByQueryAndClass()
    {
        var result = Engine.Compile(
            [new ModifierRequirement("IncreasedLife6", 80)],
            [], itemClass: "Ring");

        var first = Engine.Explain(result.Query, "Ring");
        var second = Engine.Explain(result.Query, "Ring");
        var asyncSecond = await Engine.ExplainAsync(result.Query, "Ring");

        second.Should().BeSameAs(first);
        asyncSecond.Should().BeSameAs(first);
        Engine.Explain(result.Query, "Amulet").Should().NotBeSameAs(first);
    }

    [Fact]
    public void QueryImport_ReconstructsAdvancedModsAndClass()
    {
        var result = Engine.Compile(
            [new ModifierRequirement("IncreasedLife6")], [],
            itemClass: "Ring");
        var imported = new QueryImportService(Engine).Import(result.Query);

        imported.Mode.Should().Be("advanced");
        imported.Classes.Should().Contain("Ring");
        imported.Includes.Should().Contain(x => x.Value.StartsWith("IncreasedLife"));
        imported.Excludes.Should().BeEmpty();
        imported.Keywords.Should().BeEmpty();
    }

    [Fact]
    public void QueryImport_ResolvesAClassAnchor()
    {
        var imported = new QueryImportService(Engine).Import("\": Ri\"");

        imported.Mode.Should().Be("advanced");
        imported.Classes.Should().Contain("Ring");
        imported.Includes.Should().BeEmpty();
    }

    [Fact]
    public void QueryImport_PreservesUnknownTokens()
    {
        var imported = new QueryImportService(Engine).Import("\"life\" \"zzzz\"");

        imported.Mode.Should().Be("simple");
        imported.Keywords.Should().Contain("zzzz");
        imported.Tokens.Should().Contain(x => x.Kind == "keyword" && x.Phrase == "zzzz");
    }

    [Fact]
    public void Validate_AcceptsGeneratedQuery()
    {
        var result = Engine.Compile(
            [new ModifierRequirement("IncreasedLife6", 80)], [],
            itemClass: "Ring");
        Engine.Validate(result.Query).Should().BeNull();
    }

    [Fact]
    public void DataStatus_ReportsAdvancedTargetModel()
    {
        Engine.DataReady.Should().BeTrue();
        Engine.TargetModel.Should().Be(FragmentTable.AdvancedRanges);
        Engine.FragmentDataVersion.Should().Be(Engine.DataVersion);
    }

    [Fact]
    public void QueryImport_HandlesAlternationAndThreshold()
    {
        var result = Engine.Compile(
            [new ModifierRequirement("IncreasedLife6", 80), new ModifierRequirement("Strength1")],
            [], itemClass: "Ring");
        var imported = new QueryImportService(Engine).Import(result.Query);

        imported.Keywords.Should().BeEmpty();
        imported.Includes.Should().Contain(x => x.Value.StartsWith("IncreasedLife"));
        imported.Includes.Should().Contain(x => x.Value == "Strength1");
    }

    [Fact]
    public void QueryImport_RecoversThresholdOnly()
    {
        var result = Engine.Compile(
            [new ModifierRequirement("IncreasedLife6", 80)], [],
            itemClass: "Ring");
        var imported = new QueryImportService(Engine).Import(result.Query);

        imported.Keywords.Should().BeEmpty();
        imported.Includes.Should().Contain(x => x.Value.StartsWith("IncreasedLife"));
    }
}

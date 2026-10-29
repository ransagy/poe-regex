using AwesomeAssertions;
using PoeRegex.Core.Compilation;
using PoeRegex.Core.Data;

namespace PoeRegex.Core.Tests;

/// <summary>
/// The shipped exact table is proven against the advanced item-search text
/// (ranged stats show `roll(min-max)`). These tests keep that basis explicit
/// and prevent a table generated under a different display model from being
/// accepted silently.
/// </summary>
public class TargetModelTests
{
    [Fact]
    public void ShippedTable_DeclaresAdvancedRanges()
    {
        var corpus = RealCatalog.Corpus();
        corpus.HasPrecomputedFragments.Should().BeTrue();
        corpus.PrecomputedTargetModel.Should().Be(FragmentTable.AdvancedRanges);
    }

    [Fact]
    public void Engine_RejectsUnknownTargetModel()
    {
        var good = TestCatalog.Fragments();
        var bad = new FragmentTable
        {
            Game = good.Game,
            DataVersion = good.DataVersion,
            TargetModel = "plain-v1",
            Global = good.Global,
            ByClass = good.ByClass,
            ClassAnchors = good.ClassAnchors,
        };

        var corpus = new EngineCorpus(TestCatalog.Build(), bad);
        corpus.HasPrecomputedFragments.Should().BeFalse(
            "a table from another display basis must fail closed");
        corpus.TryPrecomputedFragment("+\u0001 to maximum Life", "Ring", out _).Should().BeFalse();
    }

    [Fact]
    public void RangedTargets_UseAdvancedRangeShapes()
    {
        var corpus = RealCatalog.Corpus();
        const string pattern = "\u0001% increased Armour, Evasion and Energy Shield";
        var targets = corpus.RealLinesForClass("Body Armour")
            .Where(x => x.Pattern == pattern)
            .Select(x => x.Real)
            .ToList();

        targets.Should().NotBeEmpty();
        targets.Should().Contain(
            line => line.Contains('(', StringComparison.Ordinal),
            "the search basis is the advanced item text, not the plain display line");
    }
}

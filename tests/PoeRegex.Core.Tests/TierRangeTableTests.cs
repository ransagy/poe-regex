using PoeRegex.Core.Compilation;

namespace PoeRegex.Core.Tests;

public class TierRangeTableTests
{
    [Fact]
    public void MovementSpeedTierUnion_IsComposedWithoutProof()
    {
        var corpus = RealCatalog.Corpus();
        const string pattern = "\u0001% increased Movement Speed";
        corpus.TryComposeTierRange(pattern, [(15d, 15d), (25d, 25d)], "Boots", out var pair)
            .Should().BeTrue();
        pair.Should().Be("[12]5.*eed$");
        corpus.TryComposeTierRange(
                pattern, [(15d, 15d), (20d, 20d), (25d, 25d)], "Boots", out var triple)
            .Should().BeTrue();
        triple.Should().Be("(15|2[05]).*eed$");
    }

    [Fact]
    public void MovementSpeedTierUnion_CompilesToTheComposedQuery()
    {
        var corpus = RealCatalog.Corpus();
        var compiler = new PoeRegexCompiler(corpus);
        var result = compiler.Compile(new QuerySpecification
        {
            ItemClass = "Boots",
            IncludedModifiers =
            [
                new ModifierRequirement(
                    "MovementVelocity2", MinValue: 15, MaxValue: 15,
                    RangeModIds: ["MovementVelocity2"]),
                new ModifierRequirement(
                    "MovementVelocity4", MinValue: 25, MaxValue: 25,
                    RangeModIds: ["MovementVelocity4"]),
            ],
        });
        result.Query.Should().Be("\": Boo\" \"[12]5.*eed$\"");
    }

    [Theory]
    [InlineData(15d, 15d, "Boots", "15.*eed$")]
    [InlineData(20d, 20d, "Boots", "20.*eed$")]
    [InlineData(15d, 15d, null, "15% increased Movement Speed")]
    public void MovementSpeedTierRange_IsPrecomputed(
        double value, double same, string? itemClass, string expected)
    {
        var corpus = RealCatalog.Corpus();
        var key = CompactionEngine.RangePlanKey(
            "\u0001% increased Movement Speed",
            [(value, same)], 0, itemClass, ["\u0001% increased Movement Speed"]);
        corpus.TryPrecomputedRangeFragment(key, out var fragment).Should().BeTrue();
        fragment.Should().Be(expected);
    }
}

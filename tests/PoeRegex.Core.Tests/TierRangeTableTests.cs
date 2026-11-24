using PoeRegex.Core.Compilation;

namespace PoeRegex.Core.Tests;

public class TierRangeTableTests
{
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

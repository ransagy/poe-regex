using System.Text.RegularExpressions;
using PoeRegex.Core.Compilation;

namespace PoeRegex.Core.Tests;

public class DigitRangeRegexTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(12)]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(99)]
    [InlineData(100)]
    [InlineData(256)]
    [InlineData(500)]
    public void MatchesExactlyValuesAboveOrEqualMin(int minValue)
    {
        var pattern = DigitRangeRegex.Build(minValue);
        pattern.Should().NotBeNull();
        for (var v = 1; v <= 2000; v++)
        {
            var expected = v >= minValue;
            var actual = Regex.IsMatch(v.ToString(), "^" + pattern + "$");
            actual.Should().Be(expected, $"value {v} vs min {minValue} pattern {pattern}");
        }
    }

    [Fact]
    public void NonPositiveMin_IsAnyDigitRun()
    {
        DigitRangeRegex.Build(0).Should().Be(@"\d+");
        DigitRangeRegex.Build(-5).Should().BeNull();
    }
}

public class CommonPrefixFactorTests
{
    [Fact]
    public void FactorsSharedPrefix()
    {
        var result = CompactionEngine.FactorCommonPrefixes(["to strength", "to dexterity"]);
        result.Single().Should().Be("to (dexterity|strength)");
    }

    [Fact]
    public void Deterministic()
    {
        var a = CompactionEngine.FactorCommonPrefixes(["to strength", "to dexterity", "to intelligence"]);
        var b = CompactionEngine.FactorCommonPrefixes(["to strength", "to dexterity", "to intelligence"]);
        a.Should().Equal(b);
    }

    [Fact]
    public void WithSources_ReportsWhichFragmentsWereMerged()
    {
        var result = CompactionEngine.FactorWithSources(["to strength", "to dexterity", "of fire"]);
        result.Should().HaveCount(2);
        var factored = result.Single(a => a.Text.StartsWith("to ", StringComparison.Ordinal));
        factored.SourceIndices.Should().Equal([0, 1]);
        var plain = result.Single(a => a.Text == "of fire");
        plain.SourceIndices.Should().Equal([2]);
    }
}

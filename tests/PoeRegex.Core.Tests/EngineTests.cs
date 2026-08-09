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
}

public class GameTargetTests
{
    [Fact]
    public void Parse_AcceptsBothGames()
    {
        GameTargetExtensions.Parse("poe1").Should().Be(GameTarget.PoE1);
        GameTargetExtensions.Parse("POE2").Should().Be(GameTarget.PoE2);
    }

    [Fact]
    public void Parse_RejectsUnknown()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GameTargetExtensions.Parse("diablo4"));
    }

    [Fact]
    public void RuleKeys()
    {
        GameTarget.PoE1.ToRuleKey().Should().Be("poe1");
        GameTarget.PoE2.ToRuleKey().Should().Be("poe2");
    }
}

public class GameRulesTests
{
    [Fact]
    public void Defaults()
    {
        GameRules.For(GameTarget.PoE1).MaxCharacterLimit.Should().Be(250);
        GameRules.For(GameTarget.PoE2).SupportsLookahead.Should().BeTrue();
        GameRules.For(GameTarget.PoE1).SupportsLookahead.Should().BeFalse();
    }

    [Fact]
    public void MissingOverrideFile_FallsBackToDefaults()
    {
        GameRules.Load(GameTarget.PoE1, "/nonexistent/override.json").MaxCharacterLimit.Should().Be(250);
    }
}

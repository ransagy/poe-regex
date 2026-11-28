using PoeRegex.Core.Compilation;

namespace PoeRegex.Core.Tests;

public class FactoringTests
{
    [Fact]
    public void CharacterClasses_FactorAtSafeBoundary()
    {
        var fragments = new[]
        {
            @"\+1[234]\d.*fe$",
            @"\+1[567]\d.*fe$",
        };

        var factored = CompactionEngine.FactorCommonPrefixes(fragments);

        factored.Should().ContainSingle()
            .Which.Should().Be(@"(\+1([234]|[567]))\d.*fe$");
        QueryShape.Validate("\"" + string.Join("|", factored) + "\"")
            .Should().BeEmpty("factoring must never cut inside a character class");
    }

    [Fact]
    public void StructuredPrefix_FactorsAtTokenBoundary()
    {
        var fragments = new[]
        {
            @"\+(3[1-9]|4[0-5]).*o Cold R",
            @"\+(3[1-9]|4[0-5]).*o Fire R",
            @"\+(3[1-9]|4[0-5]).*o l.+ce$",
        };

        var factored = CompactionEngine.FactorCommonPrefixes(fragments);

        factored.Should().ContainSingle()
            .Which.Should().Be(@"\+(3[1-9]|4[0-5]).*o (Cold R|Fire R|l.+ce$)");
        QueryShape.Validate("\"" + string.Join("|", factored) + "\"")
            .Should().BeEmpty("the factored form must remain valid query shape");
    }

    [Fact]
    public void CommonLiteralBeforeStructuredGroup_BacksOffSafely()
    {
        var fragments = new[]
        {
            @"Adds ([2-9]|[1-3]\d).*Ph.*ks",
            @"Adds ([5-9]|[1-3]\d).*ld.*ks",
            @"Adds ([6-9]|[1-4]\d).*ir.*ks$",
        };

        var factored = CompactionEngine.FactorCommonPrefixes(fragments);

        factored.Should().ContainSingle()
            .Which.Should().Be(
                @"Adds (([2-9]|[1-3]\d).*Ph.*ks|([5-9]|[1-3]\d).*ld.*ks|([6-9]|[1-4]\d).*ir.*ks$)");
        QueryShape.Validate("\"" + string.Join("|", factored) + "\"")
            .Should().BeEmpty("backing off to `Adds ` must produce a valid query");
    }

    [Fact]
    public void LeadingAnchor_IsFactored()
    {
        var fragments = new[] { @"^\d.+ed ca.*d$", @"^\d.+ll d.+g.$" };

        var factored = CompactionEngine.FactorCommonPrefixes(fragments);

        factored.Should().ContainSingle()
            .Which.Should().Be(@"^\d.+(ed ca.*d$|ll d.+g.$)");
    }

    [Fact]
    public void CommonSuffix_FactorsValueFragments()
    {
        var fragments = new[]
        {
            "1 Physical Damage",
            "2 Cold Damage",
            "3 Fire Damage",
        };

        var factored = CompactionEngine.FactorCommonPrefixes(fragments);

        factored.Should().ContainSingle()
            .Which.Should().Be("(1 Physical|2 Cold|3 Fire) Damage");
        QueryShape.Validate("\"" + string.Join("|", factored) + "\"")
            .Should().BeEmpty("the suffix form must remain a valid query shape");
    }

    [Fact]
    public void AnchoredSuffix_IsNotMixedWithUnanchoredText()
    {
        var fragments = new[]
        {
            "1 Xks",
            "2 Yks$",
        };

        CompactionEngine.FactorCommonPrefixes(fragments)
            .Should().BeEquivalentTo(fragments);
    }

    [Fact]
    public void RepetitionBraces_AreNotSplit()
    {
        var fragments = new[]
        {
            "a{1,2}X",
            "a{1,3}Y",
        };

        CompactionEngine.FactorCommonPrefixes(fragments)
            .Should().BeEquivalentTo(fragments);
    }

    [Fact]
    public void TopLevelAlternation_IsNotRescopedByPrefixFactoring()
    {
        var fragments = new[]
        {
            "a b|c",
            "a d|c",
        };

        CompactionEngine.FactorCommonPrefixes(fragments)
            .Should().BeEquivalentTo(fragments);
    }

    [Fact]
    public void LiteralPrefix_StillFactors()
    {
        var factored = CompactionEngine.FactorCommonPrefixes(
            ["maximum Life", "maximum Mana"]);

        factored.Should().ContainSingle()
            .Which.Should().Be("maximum (Life|Mana)");
    }
}

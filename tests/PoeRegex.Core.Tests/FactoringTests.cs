using PoeRegex.Core.Compilation;

namespace PoeRegex.Core.Tests;

public class FactoringTests
{
    [Fact]
    public void CharacterClasses_AreNotSplit()
    {
        var fragments = new[]
        {
            @"\+1[234]\d.*fe$",
            @"\+1[567]\d.*fe$",
        };

        var factored = CompactionEngine.FactorCommonPrefixes(fragments);

        factored.Should().BeEquivalentTo(fragments);
        QueryShape.Validate("\"" + string.Join("|", factored) + "\"")
            .Should().BeEmpty("factoring must never cut inside a character class");
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

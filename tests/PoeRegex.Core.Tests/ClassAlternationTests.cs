using PoeRegex.Core.Compilation;
using PoeRegex.Core.Data;

namespace PoeRegex.Core.Tests;

/// <summary>
/// Paste class-group alternation: shared suffixes fold into `.*suffix` only
/// when the merged term matches exactly the same classes and no universal or
/// mod line, and only when the alternation gets shorter.
/// </summary>
public class ClassAlternationTests
{
    private static ICompilerStrategy Strategy => PoeRegexCompiler.StrategyFor(GameTarget.PoE2);

    [Fact]
    public void Merges_SharedSuffix_IntoWildcardTerm()
    {
        var merged = ClassAlternation.Build(RealCatalog.Corpus(), Strategy, ["One Hand Mace", "Two Hand Mace"]);
        merged.Should().Be(".*Hand M");
        merged.Length.Should().BeLessThan("One Hand M|Two Hand M".Length);
    }

    [Fact]
    public void SingleClass_KeepsItsPrefix()
    {
        ClassAlternation.Build(RealCatalog.Corpus(), Strategy, ["One Hand Mace"]).Should().Be("One Hand M");
    }

    [Fact]
    public void NoWildcard_WhenTermsShareNothing()
    {
        var alt = ClassAlternation.Build(RealCatalog.Corpus(), Strategy, ["One Hand Mace", "Shield"]);
        alt.Should().NotContain(".*");
        alt.Should().Contain("One Hand M");
        alt.Should().Contain("|");
    }

    [Fact]
    public void ClassTermExact_RequiresTheWholeSet()
    {
        var corpus = RealCatalog.Corpus();
        corpus.ClassTermExact(".*Hand M", ["One Hand Mace", "Two Hand Mace"]).Should().BeTrue();
        corpus.ClassTermExact(".*Hand M", ["One Hand Mace"]).Should().BeFalse("it also matches Two Hand Mace");
        corpus.ClassTermExact(".*Hand M", ["Belt"]).Should().BeFalse();
        corpus.ClassTermQuiet(".*Hand M").Should().BeTrue();
    }
}

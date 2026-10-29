using PoeRegex.Core.Compilation;
using PoeRegex.Core.Data;

namespace PoeRegex.Core.Tests;

/// <summary>
/// Paste class-group alternation: precomputed exact multi-class terms are
/// combined with optimized single-class anchors by an exact cover, so the
/// shortest verified form wins and the runtime never rescans the corpus.
/// </summary>
public class ClassAlternationTests
{
    [Fact]
    public void Merges_SharedSubstring_IntoWildcardTerm()
    {
        var merged = ClassAlternation.Build(RealCatalog.Corpus(), ["One Hand Mace", "Two Hand Mace"]);
        merged.Should().Be(".*Mac");
        merged.Length.Should().BeLessThan("On.+M|Tw.+M".Length);
        merged.Length.Should().BeLessThan(".*Hand M".Length);
    }

    [Fact]
    public void SingleClass_UsesItsShortAnchor()
    {
        ClassAlternation.Build(RealCatalog.Corpus(), ["One Hand Mace"]).Should().Be("On.+M");
    }

    [Fact]
    public void UnrelatedClasses_UseTheShorterAnchoredJoin()
    {
        var alt = ClassAlternation.Build(RealCatalog.Corpus(), ["One Hand Mace", "Shield"]);
        alt.Should().Be("On.+M|Shi.+s");
        alt.Length.Should().BeLessThan("One Hand M|Shields".Length);
    }

    [Theory]
    [InlineData(new[] { "Bow", "Crossbow" }, ".*Bow")]
    [InlineData(new[] { "Staff", "Warstaff" }, ".*tav")]
    [InlineData(new[] { "Gloves", "Staff", "Warstaff" }, ".*ves")]
    public void PrecomputedGroup_IsUsedWhenItCoversTheSet(string[] classes, string expected)
    {
        ClassAlternation.Build(RealCatalog.Corpus(), classes).Should().Be(expected);
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

    [Fact]
    public void AnchoredAlternation_IsExactAndQuiet()
    {
        var corpus = RealCatalog.Corpus();
        var classes = new[] { "One Hand Mace", "Shield", "Belt" };
        var alt = "(" + ClassAlternation.Build(corpus, classes) + ")";

        corpus.ClassTermExact(alt, classes).Should().BeTrue();
        corpus.ClassTermQuiet(alt).Should().BeTrue();
    }
}

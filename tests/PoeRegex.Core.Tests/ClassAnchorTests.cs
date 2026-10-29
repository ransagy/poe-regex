using AwesomeAssertions;
using PoeRegex.Core.Compilation;

namespace PoeRegex.Core.Tests;

public class ClassAnchorTests
{
    [Fact]
    public void ShippedAnchors_AreExactQuietAndRecomputed()
    {
        var corpus = RealCatalog.Corpus();
        foreach (var itemClass in corpus.InGameClasses)
        {
            var anchor = corpus.ClassNameAnchor(itemClass.Id);
            anchor.Should().NotBeNullOrEmpty();
            corpus.ClassTermExact(anchor, [itemClass.Id]).Should().BeTrue($"{itemClass.Id}: {anchor}");
            corpus.ClassTermQuiet(anchor).Should().BeTrue($"{itemClass.Id}: {anchor}");
            corpus.ComputeClassNameAnchor(itemClass.Id).Should().Be(anchor,
                $"{itemClass.Id}: shipped anchor must equal the data-time optimizer");
        }
    }

    [Fact]
    public void Anchors_AreNoLongerThanTheLiteralPrefix()
    {
        var corpus = RealCatalog.Corpus();
        foreach (var itemClass in corpus.InGameClasses)
        {
            var anchor = corpus.ClassNameAnchor(itemClass.Id);
            anchor.Length.Should().BeLessThanOrEqualTo(corpus.ClassNamePrefix(itemClass.Id).Length,
                $"{itemClass.Id}: '{anchor}'");
        }
    }

    [Theory]
    [InlineData("One Hand Mace", "On.+M")]
    [InlineData("Two Hand Mace", "Tw.+M")]
    [InlineData("Shield", "Shi.+s")]
    public void ShortWildcardAnchors_AreExactAndQuiet(string classId, string anchor)
    {
        var corpus = RealCatalog.Corpus();
        corpus.ClassTermExact(anchor, [classId]).Should().BeTrue();
        corpus.ClassTermQuiet(anchor).Should().BeTrue();
    }

    [Fact]
    public void Compiler_EmitsWildcardAnchorAsRegex()
    {
        var corpus = RealCatalog.Corpus();
        var result = new PoeRegexCompiler(corpus).Compile(new QuerySpecification
        {
            ItemClass = "One Hand Mace",
        });

        result.Query.Should().Be("\": On.+M\"");
        result.Query.Should().NotContain(@"\.");
        result.Query.Should().NotContain(@"\+");
    }
}

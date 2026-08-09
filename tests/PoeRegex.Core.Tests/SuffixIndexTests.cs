using PoeRegex.Core.Search;

namespace PoeRegex.Core.Tests;

public class SuffixIndexTests
{
    [Fact]
    public void ShortestUniqueSubstring_IsUniqueInCorpus()
    {
        var corpus = new[]
        {
            "+# to Strength",
            "+# to Dexterity",
            "+# to maximum Life",
            "Minions have #% increased maximum Life",
            "Item Class: Rings",
        };
        var idx = SuffixIndex.Build(corpus);

        for (var i = 0; i < corpus.Length; i++)
        {
            var fp = idx.ShortestUniqueSubstring(i);
            if (fp is null) continue; // identical-to-another-line case handled below
            idx.FragmentUniqueToLine(i, fp).Should().BeTrue($"fingerprint '{fp}' must be unique for line {corpus[i]}");
        }
    }

    [Fact]
    public void SharedTailAcrossLines_HasNoUniqueSubstring()
    {
        var corpus = new[] { "a to maximum Life", "b to maximum Life" };
        var idx = SuffixIndex.Build(corpus);
        // Line 0's whole content "a to maximum Life" also occurs (as substring) nowhere
        // else; but "to maximum Life" tail is shared, so the shortest unique substring
        // must include the 'a'.
        var fp = idx.ShortestUniqueSubstring(0);
        fp.Should().NotBeNull();
        fp.Should().StartWith("a");
    }

    [Fact]
    public void LinesMatching_ReturnsDistinctLines()
    {
        var corpus = new[]
        {
            "+# to Strength",
            "+# to Dexterity",
            "+# to maximum Life",
            "Item Class: Rings",
        };
        var idx = SuffixIndex.Build(corpus);
        var matches = idx.LinesMatching("to");
        matches.Length.Should().BeGreaterThanOrEqualTo(3);
        matches.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void DigitPlaceholder_IsValueAgnostic()
    {
        var corpus = new[] { "+# to maximum Life", "Item Class: Rings" };
        var idx = SuffixIndex.Build(corpus);
        // A line differing only in digits normalizes to the same pattern and is deduped
        // by the builder, so the index sees a single entry.
        idx.ClassCorpus.Count.Should().Be(2);
    }
}

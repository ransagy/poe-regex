using PoeRegex.Core.Compilation;
using PoeRegex.Core.Data;
using PoeRegex.Core.Explain;
using PoeRegex.Core.Search;

namespace PoeRegex.Core.Tests;

public class ExplainerTests
{
    private static (EngineCorpus Corpus, Explainer Explainer) Create()
    {
        var corpus = new EngineCorpus(TestCatalog.Build());
        return (corpus, new Explainer(corpus));
    }

    [Fact]
    public void Tokenize_SplitsQuotedBlocksAndExclusions()
    {
        var tokens = Explainer.Tokenize("\"a b\" !\"c\" \"d\"");

        tokens.Should().HaveCount(3);
        tokens[0].Should().Be(("a b", false));
        tokens[1].Should().Be(("c", true));
        tokens[2].Should().Be(("d", false));
    }

    [Fact]
    public void ClassAnchor_IsDetectedWhenItMatchesNoModLines()
    {
        var (_, explainer) = Create();
        var result = explainer.Explain("\" m\" \":\"", "Ring");

        result.Tokens.Should().Contain(t => t.Kind == "class" && t.Phrase == ":");
        result.Tokens.Should().Contain(t => t.Kind == "included" && t.Phrase == " m");
        result.Mods.Select(m => m.Id).Should().Contain("IncreasedLife1");
    }

    [Fact]
    public void PhraseMatchingModLines_IsNotTreatedAsClassAnchor()
    {
        // "in" appears in the class line ("Item Class: Rings") AND in mod lines
        // ("increased Rarity of Items found"), so it must be a mod match, not a class.
        var (_, explainer) = Create();
        var result = explainer.Explain("\"in\"", "Ring");

        var token = result.Tokens.Should().ContainSingle(t => t.Phrase == "in").Subject;
        token.Kind.Should().Be("included");
        result.Mods.Select(m => m.Id).Should().Contain("ItemFoundRarityIncrease1");
    }

    [Fact]
    public void IncludedPhrase_MapsToMods()
    {
        var (_, explainer) = Create();
        var result = explainer.Explain("\" m\"", "Ring");

        var token = result.Tokens.Should().ContainSingle().Subject;
        token.Kind.Should().Be("included");
        token.Matches.Should().Contain(m => m.ModIds.Contains("IncreasedLife1"));
    }

    [Fact]
    public void Exclusion_DropsMatchedMods()
    {
        var (_, explainer) = Create();
        var result = explainer.Explain("\" m\" !\"tr\"", "Ring");

        // "tr" matches the Strength line, so Strength1 must be excluded.
        result.Mods.Select(m => m.Id).Should().Contain("IncreasedLife1");
        result.Mods.Select(m => m.Id).Should().NotContain("Strength1");
    }

    [Fact]
    public void Keyword_WithNoMatches_IsNotAnInclude()
    {
        var (_, explainer) = Create();
        var result = explainer.Explain("\"ilvl:60\"", "Ring");

        var token = result.Tokens.Should().ContainSingle().Subject;
        token.Kind.Should().Be("keyword");
        result.Mods.Should().BeEmpty();
    }

    [Fact]
    public void ExplicitClass_ScopesCandidates()
    {
        var (_, explainer) = Create();
        var result = explainer.Explain("\" m\"", "Ring");

        result.Classes.Select(c => c.Id).Should().Contain("Ring");
        result.Mods.All(m => m.Classes.Contains("Ring")).Should().BeTrue();
    }

    [Fact]
    public void Output_IsDeterministic()
    {
        var (_, explainer) = Create();
        var query = "\" m\" !\"tr\" \":\"";

        var a = explainer.Explain(query, "Ring");
        var b = explainer.Explain(query, "Ring");

        a.Mods.Select(m => m.Id).Should().Equal(b.Mods.Select(m => m.Id));
        a.Tokens.Select(t => (t.Kind, t.Phrase, t.Excluded)).Should().Equal(b.Tokens.Select(t => (t.Kind, t.Phrase, t.Excluded)));
    }

    [Fact]
    public void PhraseToRegex_ExpandsDigitPlaceholder()
    {
        var p = new string(SuffixIndex.DigitPlaceholder, 1);
        Explainer.PhraseToRegex($"{p} P").Should().Be(@"\d+ P");
        Explainer.PhraseToRegex("maximum Life").Should().Be("maximum Life");
    }
}

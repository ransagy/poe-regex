using PoeRegex.Core.Compilation;
using PoeRegex.Core.Data;
using PoeRegex.Core.Explain;

namespace PoeRegex.Core.Tests;

public class QueryShapeTests
{
    [Fact]
    public void QuotedQuery_Passes()
    {
        QueryShape.Validate("\"maximum Life\" !\"reflect\" \": Ri\"")
            .Should().BeEmpty();
    }

    [Theory]
    [InlineData("maximum Life")]
    [InlineData("!reflect")]
    [InlineData("\"maximum Life")]
    [InlineData("\"quo\"te\"")]
    public void BrokenQueryShapes_Fail(string query)
    {
        QueryShape.Validate(query).Should().NotBeEmpty();
    }

    [Theory]
    [InlineData(".*")]
    [InlineData(@"\d+")]
    [InlineData("^$")]
    [InlineData(@"\+")]
    [InlineData("|")]
    public void ReservedOnlyFragments_Fail(string fragment)
    {
        QueryShape.ValidateFragment(fragment).Should().NotBeEmpty(
            $"'{fragment}' is regex syntax, not a stat selector");
    }

    [Theory]
    [InlineData("fe$")]
    [InlineData(@"\+.*fe$")]
    [InlineData("o .+fe")]
    [InlineData("k Speed$")]
    [InlineData("to maximum Life")]
    [InlineData(" additional bolts")]
    public void SemanticFragments_Pass(string fragment)
    {
        QueryShape.ValidateFragment(fragment).Should().BeEmpty();
    }

    [Fact]
    public void Compiler_SingleMod_IsOneQuotedBlockPlusAnchor()
    {
        var corpus = new EngineCorpus(TestCatalog.Build());
        var compiler = new PoeRegexCompiler(
            corpus, PoeRegexCompiler.StrategyFor(GameTarget.PoE1));
        var result = compiler.Compile(QuerySpecification.For(GameTarget.PoE1) with
        {
            ItemClass = "Ring",
            IncludedModifiers = [new ModifierRequirement("IncreasedLife1")],
        });

        QueryShape.Validate(result.Query).Should().BeEmpty();
        var tokens = Explainer.Tokenize(result.Query);
        tokens.Where(t => !t.Excluded).Should().HaveCount(2,
            "one included block plus one class anchor");
        result.Diagnostics.Should().NotContain(d => d.Code == "query-shape");
    }

    [Fact]
    public void Compiler_MultipleMods_StayInsideOneQuotedBlock()
    {
        var corpus = new EngineCorpus(TestCatalog.Build());
        var compiler = new PoeRegexCompiler(
            corpus, PoeRegexCompiler.StrategyFor(GameTarget.PoE1));
        var result = compiler.Compile(QuerySpecification.For(GameTarget.PoE1) with
        {
            ItemClass = "Ring",
            IncludedModifiers =
            [
                new ModifierRequirement("IncreasedLife1"),
                new ModifierRequirement("Strength1"),
            ],
        });

        QueryShape.Validate(result.Query).Should().BeEmpty();
        var tokens = Explainer.Tokenize(result.Query);
        tokens.Where(t => !t.Excluded).Should().HaveCount(2,
            "alternatives are OR-ed inside one block, not split into AND blocks");
        tokens.Where(t => !t.Excluded).Any(t => t.Phrase.Contains('|'))
            .Should().BeTrue();
        result.Diagnostics.Should().NotContain(d => d.Code == "query-shape");
    }

    [Fact]
    public void PasteComposer_QuotesEachGroup()
    {
        var corpus = RealCatalog.Corpus();
        var composer = new PasteComposer(
            corpus, PoeRegexCompiler.StrategyFor(GameTarget.PoE2));
        var result = composer.Compose(
        [
            new PasteSlotCriterion("Ring", [new ModifierRequirement("IncreasedLife6")], "ring"),
            new PasteSlotCriterion("Amulet", [new ModifierRequirement("Strength1")], "amulet"),
        ], []);

        QueryShape.Validate(result.Query).Should().BeEmpty();
        var tokens = Explainer.Tokenize(result.Query);
        tokens.Where(t => !t.Excluded).Should().HaveCount(3,
            "one anchor plus one group per modded slot");
        result.Diagnostics.Should().NotContain(d => d.Code == "query-shape");
    }
}

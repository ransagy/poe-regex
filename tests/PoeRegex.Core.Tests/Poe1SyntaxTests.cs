using PoeRegex.Core.Compilation;
using PoeRegex.Core.Data;
using PoeRegex.Core.Explain;

namespace PoeRegex.Core.Tests;

/// <summary>PoE1 query dialect: exclusions live inside the quoted term.</summary>
public class Poe1SyntaxTests
{
    private static EngineCorpus Corpus()
    {
        var source = TestCatalog.Build();
        var catalog = new Catalog
        {
            Game = "poe1",
            DataVersion = source.DataVersion,
            ItemClasses = source.ItemClasses,
            Mods = source.Mods,
        };
        return new EngineCorpus(catalog, TestCatalog.Fragments());
    }

    [Fact]
    public void ExclusionShape_DiffersPerGame()
    {
        QuerySyntax.Poe1.FormatExclusion("reflect").Should().Be("\"!reflect\"");
        QuerySyntax.Poe2.FormatExclusion("reflect").Should().Be("!\"reflect\"");
        QuerySyntax.ForGame("poe1").Game.Should().Be("poe1");
        QuerySyntax.ForGame("poe2").Game.Should().Be("poe2");
        QuerySyntax.ForGame(null).Should().BeSameAs(QuerySyntax.Poe2);
    }

    [Fact]
    public void Tokenize_ReadsPoE1ExclusionInsideQuotes()
    {
        var tokens = QuerySyntax.Poe1.Tokenize("\": Ri\" \"!reflect\" \"maximum L\"");

        tokens.Should().HaveCount(3);
        tokens[0].Should().Be((": Ri", false));
        tokens[1].Should().Be(("reflect", true));
        tokens[2].Should().Be(("maximum L", false));
    }

    [Fact]
    public void Tokenize_PoE2KeepsBangOutsideQuotes()
    {
        var tokens = QuerySyntax.Poe2.Tokenize("\"a\" !\"b\" \"!c\"");

        tokens.Should().Equal(("a", false), ("b", true), ("!c", false));
    }

    [Fact]
    public void Compiler_UsesTheCorpusDialect()
    {
        var result = new PoeRegexCompiler(Corpus()).Compile(new QuerySpecification
        {
            ItemClass = "Ring",
            IncludedModifiers = [new ModifierRequirement("IncreasedLife1")],
            ExcludedModifiers = [new ModifierRequirement("reflect")],
        });

        result.Query.Should().Contain("\"!reflect\"");
        result.Query.Should().NotContain("!\"reflect\"");
        result.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void PasteComposer_UsesTheCorpusDialect()
    {
        var result = new PasteComposer(Corpus()).Compose(
            [new PasteSlotCriterion("Ring", [new ModifierRequirement("IncreasedLife1")], "Ring")],
            [new ModifierRequirement("reflect")]);

        result.Query.Should().Contain("\"!reflect\"");
        result.Query.Should().NotContain("!\"reflect\"");
    }

    [Fact]
    public void Explainer_TreatsPoE1ExclusionAsExcluded()
    {
        var corpus = Corpus();
        var explained = new Explainer(corpus).Explain("\": Ri\" \"maximum L\" \"!reflect\"", "Ring");

        explained.Tokens.Should().Contain(t => t.Phrase == "reflect" && t.Excluded);
        explained.Tokens.Where(t => t.Excluded).Should().ContainSingle();
        explained.Mods.Select(m => m.Id).Should().Contain("IncreasedLife1");
    }
}

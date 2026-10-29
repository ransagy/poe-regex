using PoeRegex.Core.Compilation;
using PoeRegex.Core.Data;
using PoeRegex.Core.Explain;

namespace PoeRegex.Core.Tests;

public class CompilerTests
{
    private static PoeRegexCompiler CreateCompiler()
    {
        var corpus = TestCatalog.Corpus();
        return new PoeRegexCompiler(corpus);
    }

    [Fact]
    public void SingleMod_ProducesQuotedBlockAndClassAnchor()
    {
        var compiler = CreateCompiler();
        var result = compiler.Compile(new QuerySpecification
        {
            ItemClass = "Ring",
            IncludedModifiers = [new ModifierRequirement("IncreasedLife1")],
        });

        result.Query.Should().Contain("\"");
        result.Query.Should().Contain("\": Ri\""); // class anchor block (distinctive, never bare ":")
        result.Exceeded.Should().BeFalse();
        result.Length.Should().BeLessThan(result.Limit);

        // The generated query must reverse back to the source mod (reversibility).
        var explainer = new Explainer(TestCatalog.Corpus());
        var explained = explainer.Explain(result.Query, "Ring");
        explained.Mods.Select(m => m.Id).Should().Contain("IncreasedLife1");
    }

    [Fact]
    public void ClassAnchor_IsTheFirstQuotedBlock()
    {
        var compiler = CreateCompiler();
        var result = compiler.Compile(new QuerySpecification
        {
            ItemClass = "Ring",
            IncludedModifiers = [new ModifierRequirement("IncreasedLife1")],
            ExcludedModifiers = [new ModifierRequirement("reflect")],
        });

        result.Query.Should().StartWith("\": Ri\" ");
        result.Query.IndexOf("\": Ri\"", StringComparison.Ordinal).Should()
            .BeLessThan(result.Query.IndexOf("!\"reflect\"", StringComparison.Ordinal));
    }

    [Fact]
    public void MultipleMods_UseAlternation()
    {
        var compiler = CreateCompiler();
        var result = compiler.Compile(new QuerySpecification
        {
            ItemClass = "Ring",
            IncludedModifiers =
            [
                new ModifierRequirement("IncreasedLife1"),
                new ModifierRequirement("Strength1"),
            ],
        });

        result.Query.Should().Contain("|");
        result.Query.Should().Contain("\": Ri\""); // class anchor block (distinctive, never bare ":")
        result.Query.Should().Contain("\"");

        var explainer = new Explainer(TestCatalog.Corpus());
        var explained = explainer.Explain(result.Query, "Ring");
        var ids = explained.Mods.Select(m => m.Id).ToList();
        ids.Should().Contain("IncreasedLife1");
        ids.Should().Contain("Strength1");
    }

    [Fact]
    public void Exclusions_BecomeNegatedQuotedBlocks()
    {
        var compiler = CreateCompiler();
        var result = compiler.Compile(new QuerySpecification
        {
            ItemClass = "Ring",
            IncludedModifiers = [new ModifierRequirement("IncreasedLife1")],
            ExcludedModifiers = [new ModifierRequirement("reflect")],
        });

        result.Query.Should().Contain("!\"reflect\"");
    }

    [Fact]
    public void Keywords_AreQuoted()
    {
        var compiler = CreateCompiler();
        var result = compiler.Compile(new QuerySpecification
        {
            ItemClass = "Ring",
            IncludedModifiers = [new ModifierRequirement("IncreasedLife1")],
            Keywords = ["ilvl:60"],
        });

        result.Query.Should().Contain("\"ilvl:60\"");
    }

    [Fact]
    public void FreeText_IsEscapedForLiteralMatch()
    {
        var compiler = CreateCompiler();
        var result = compiler.Compile(new QuerySpecification
        {
            ItemClass = "Ring",
            IncludedModifiers = [new ModifierRequirement("to +maximum Life")],
        });

        // The '+' must be escaped so it matches literally.
        result.Query.Should().Contain(@"\+");
    }

    [Fact]
    public void Threshold_EmitsDigitRangePattern()
    {
        var compiler = CreateCompiler();
        var result = compiler.Compile(new QuerySpecification
        {
            ItemClass = "Ring",
            IncludedModifiers = [new ModifierRequirement("IncreasedLife1", 20)],
        });

        result.Query.Should().Contain("2[0-9]");
        result.Query.Should().Contain("maximum Life");
    }

    [Fact]
    public void Overflow_ThrowsUnlessAllowed()
    {
        var compiler = CreateCompiler();
        var spec = new QuerySpecification
        {
            ItemClass = "Ring",
            IncludedModifiers = [new ModifierRequirement("IncreasedLife1")],
            Keywords = Enumerable.Range(0, 100).Select(i => $"ilvl:{i}").ToList(),
        };

        Assert.Throws<RegexLengthOverflowException>(() => compiler.Compile(spec));

        var allowed = compiler.Compile(spec with { AllowOverflow = true });
        allowed.Exceeded.Should().BeTrue();
        allowed.Length.Should().BeGreaterThan(allowed.Limit);
    }

    [Fact]
    public void DeterministicOutput_ForSameInput()
    {
        var compiler = CreateCompiler();
        var spec = new QuerySpecification
        {
            ItemClass = "Ring",
            IncludedModifiers = [new ModifierRequirement("IncreasedLife1"), new ModifierRequirement("Strength1")],
        };

        var a = compiler.Compile(spec).Query;
        var b = compiler.Compile(spec).Query;
        a.Should().Be(b);
    }

    [Fact]
    public void ModWithNoStatLines_ProducesWarning()
    {
        var compiler = CreateCompiler();
        var result = compiler.Compile(new QuerySpecification
        {
            ItemClass = "Ring",
            IncludedModifiers = [new ModifierRequirement("EmptyStatMod1")],
        });

        result.Diagnostics.Should().Contain(d => d.Code == "unresolved-modifier");
    }

    [Fact]
    public void MissingTable_FailsClosed()
    {
        // The synthetic catalog's dataVersion does not match the embedded
        // PoE2 table, so the corpus deliberately has no usable fragments.
        var compiler = new PoeRegexCompiler(new EngineCorpus(TestCatalog.Build()));
        var result = compiler.Compile(new QuerySpecification
        {
            ItemClass = "Ring",
            IncludedModifiers = [new ModifierRequirement("IncreasedLife1")],
        });

        result.Diagnostics.Should().Contain(d => d.Code == "unresolved-modifier");
        result.Query.Should().NotContain("maximum");
    }

    [Fact]
    public void WordBoundary_IsWarned()
    {
        Poe2Syntax.ValidateFragment(@"\bfoo").Should().NotBeNull();
    }

}

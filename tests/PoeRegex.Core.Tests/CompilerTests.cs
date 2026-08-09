using PoeRegex.Core.Compilation;
using PoeRegex.Core.Data;

namespace PoeRegex.Core.Tests;

public class CompilerTests
{
    private static PoeRegexCompiler CreateCompiler()
    {
        var corpus = new EngineCorpus(TestCatalog.Build());
        return new PoeRegexCompiler(corpus, PoeRegexCompiler.StrategyFor(GameTarget.PoE1));
    }

    [Fact]
    public void SingleMod_ProducesQuotedBlockAndClassAnchor()
    {
        var compiler = CreateCompiler();
        var result = compiler.Compile(QuerySpecification.For(GameTarget.PoE1) with
        {
            ItemClass = "Ring",
            IncludedModifiers = [new ModifierRequirement("IncreasedLife1")],
        });

        result.Query.Should().Contain("maximum L");
        result.Query.Should().Contain("\"");
        result.Query.Should().Contain("Ring");
        result.Exceeded.Should().BeFalse();
        result.Length.Should().BeLessThan(result.Limit);
    }

    [Fact]
    public void MultipleMods_UseAlternation()
    {
        var compiler = CreateCompiler();
        var result = compiler.Compile(QuerySpecification.For(GameTarget.PoE1) with
        {
            ItemClass = "Ring",
            IncludedModifiers =
            [
                new ModifierRequirement("IncreasedLife1"),
                new ModifierRequirement("Strength1"),
            ],
        });

        result.Query.Should().Contain("|");
        result.Query.Should().Contain("St");
        result.Query.Should().Contain("maximum L");
    }

    [Fact]
    public void Exclusions_BecomeNegatedQuotedBlocks()
    {
        var compiler = CreateCompiler();
        var result = compiler.Compile(QuerySpecification.For(GameTarget.PoE1) with
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
        var result = compiler.Compile(QuerySpecification.For(GameTarget.PoE1) with
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
        var result = compiler.Compile(QuerySpecification.For(GameTarget.PoE1) with
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
        var result = compiler.Compile(QuerySpecification.For(GameTarget.PoE1) with
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
        var spec = QuerySpecification.For(GameTarget.PoE1) with
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
        var spec = QuerySpecification.For(GameTarget.PoE1) with
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
        var result = compiler.Compile(QuerySpecification.For(GameTarget.PoE1) with
        {
            ItemClass = "Ring",
            IncludedModifiers = [new ModifierRequirement("EmptyStatMod1")],
        });

        result.Diagnostics.Should().Contain(d => d.Code == "unresolved-modifier");
    }
}

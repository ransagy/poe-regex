using PoeRegex.Core.Compilation;

namespace PoeRegex.Core.Tests;

/// <summary>
/// The canonical constraint layer: exact tiers, “or better”, and family picks
/// are the same semantic set, and the compiler result is order-independent.
/// </summary>
public class CanonicalConstraintTests
{
    private static PoeRegexCompiler Compiler() => new(RealCatalog.Corpus());

    private static string Compile(
        PoeRegexCompiler compiler, string itemClass,
        params ModifierRequirement[] requirements) =>
        compiler.Compile(new QuerySpecification
        {
            ItemClass = itemClass,
            IncludedModifiers = requirements,
        }).Query;

    private static ModifierRequirement Exact(int tier) =>
        new($"MovementVelocity{tier}", MinValue: Value(tier), MaxValue: Value(tier),
            RangeModIds: [$"MovementVelocity{tier}"]);

    private static ModifierRequirement Up(int from, int to) =>
        new($"MovementVelocity{from}", MinValue: Value(from), MaxValue: Value(to),
            RangeModIds: Enumerable.Range(from, to - from + 1)
                .Select(t => $"MovementVelocity{t}").ToList());

    private static int Value(int tier) => tier switch
    {
        1 => 10, 2 => 15, 3 => 20, 4 => 25, 5 => 30, 6 => 35,
        _ => throw new ArgumentOutOfRangeException(nameof(tier)),
    };

    [Fact]
    public void FourExactTiers_EqualStallionOrBetter()
    {
        var compiler = Compiler();
        var exact = Compile(compiler, "Boots", Exact(3), Exact(4), Exact(5), Exact(6));
        var up = Compile(compiler, "Boots", Up(3, 6));

        exact.Should().Be(up);
        exact.Should().NotContain("Stallion");
    }

    [Fact]
    public void ExactTierOrder_DoesNotChangeTheQuery()
    {
        var compiler = Compiler();
        var a = Compile(compiler, "Boots", Exact(3), Exact(4), Exact(5), Exact(6));
        var b = Compile(compiler, "Boots", Exact(6), Exact(3), Exact(5), Exact(4));

        a.Should().Be(b);
    }

    [Fact]
    public void FirstTierOrBetter_EqualsTheFamilyPick()
    {
        var compiler = Compiler();
        var up = Compile(compiler, "Boots", Up(1, 6));
        var family = Compile(compiler, "Boots",
            Enumerable.Range(1, 6)
                .Select(t => new ModifierRequirement($"MovementVelocity{t}"))
                .ToArray());

        up.Should().Be(family);
    }
}

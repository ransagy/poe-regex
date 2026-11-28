using System.Text.Json;
using PoeRegex.Core;
using PoeRegex.Core.Compilation;
using PoeRegex.Web.Core;

namespace PoeRegex.Web.Tests;

/// <summary>
/// A representative four-family added-damage-to-attacks state. The shared
/// "Adds " prefix is the only safe text factor; the remaining per-family value
/// classes and tails stay distinct.
/// </summary>
public class AddedDamageFactoringTests
{
    private static readonly string Root =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));

    [Fact]
    public void FourFamilyAddedDamage_FactorsTheSharedAddsPrefix()
    {
        var engine = new EngineService();
        var snapshot = JsonSerializer.Deserialize(
            File.ReadAllText(Path.Combine(
                Root, "tests", "PoeRegex.Web.Tests", "Fixtures", "StringBudget",
                "poe2-added-damage.json")),
            WebJsonContext.Default.WorkbenchSnapshot)!;
        var state = new WorkbenchState();
        WorkbenchSnapshot.Restore(state, snapshot);

        var result = engine.CompilePicks(
            state.Global.Included,
            state.Global.Excluded,
            state.Global.MatchAll,
            itemClass: null,
            state.ExternalIncludes);

        result.Length.Should().Be(88);
        result.Query.Should().Be(
            "\"Adds (([2-9]|[1-3]\\d).*Ph.*ks|([5-9]|[1-3]\\d).*ld.*ks|([6-9]|[1-4]\\d).*ir.*ks$)|tn.+ks\"");
        result.Query.Split("Adds ", StringSplitOptions.None).Length.Should().Be(2,
            "the shared Adds prefix should appear once");

        Match(engine, result, "Adds 7 to 20 Physical damage to Attacks").Should().BeTrue();
        Match(engine, result, "Adds 9 to 17 Cold damage to Attacks").Should().BeTrue();
        Match(engine, result, "Adds 13 to 32 Fire damage to Attacks").Should().BeTrue();
        Match(engine, result, "Adds 1 to 22 Lightning damage to Attacks").Should().BeTrue();
    }

    private static bool Match(EngineService engine, CompileResult result, string line) =>
        ItemMatcher.Match(result.Query, [line], engine.Syntax).Match;
}

using System.Text.Json;
using PoeRegex.Core;
using PoeRegex.Core.Compilation;
using PoeRegex.Web.Core;

namespace PoeRegex.Web.Tests;

/// <summary>
/// A real PoE2 resistance state: three 31–45% resistance picks in simple
/// any-of mode. The shared range+tail prefix must collapse without widening
/// the 31–45 span or importing another resistance family.
/// </summary>
public class ResistanceFactoringTests
{
    private static readonly string Root =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));

    [Fact]
    public void SharedResistancePrefix_FactorsWithoutBroadening()
    {
        var engine = new EngineService();
        var snapshot = JsonSerializer.Deserialize(
            File.ReadAllText(Path.Combine(
                Root, "tests", "PoeRegex.Web.Tests", "Fixtures", "StringBudget",
                "poe2-resistance.json")),
            WebJsonContext.Default.WorkbenchSnapshot)!;
        var state = new WorkbenchState();
        WorkbenchSnapshot.Restore(state, snapshot);

        var result = engine.CompilePicks(
            state.Global.Included,
            state.Global.Excluded,
            state.Global.MatchAll,
            itemClass: null,
            state.ExternalIncludes);

        result.Query.Should().Be(
            "\"\\+(3[1-9]|4[0-5]).*o (Cold R|Fire R|l.+ce$)\"");
        result.Length.Should().Be(45);

        Match(engine, result, "+31% to Cold Resistance").Should().BeTrue();
        Match(engine, result, "+45% to Fire Resistance").Should().BeTrue();
        Match(engine, result, "+31% to Lightning Resistance").Should().BeTrue();
        Match(engine, result, "+30% to Cold Resistance").Should().BeFalse(
            "the selected range starts at 31");
        Match(engine, result, "+46% to Cold Resistance").Should().BeFalse(
            "the selected range ends at 45");
        Match(engine, result, "+31% to all Elemental Resistances").Should().BeFalse(
            "the pick selects Cold, Fire, and Lightning, not all-elemental");
    }

    private static bool Match(EngineService engine, CompileResult result, string line) =>
        ItemMatcher.Match(result.Query, [line], engine.Syntax).Match;
}

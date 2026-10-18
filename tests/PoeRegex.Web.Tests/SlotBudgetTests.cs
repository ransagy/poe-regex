using PoeRegex.Web.Core;

namespace PoeRegex.Web.Tests;

/// <summary>
/// Bulk-add budget previews: compiling with prospective picks must never
/// throw and must report Exceeded so the picker can gate "add all matches".
/// </summary>
public class SlotBudgetTests
{
    private static readonly EngineService Engine = new();

    [Fact]
    public void Preview_SinglePick_Fits()
    {
        var crit = new SlotCriteria { ClassId = "Ring" };
        var preview = SlotBudget.Preview(Engine, crit, "Ring", [new WantedMod("Strength4")]);
        preview.Exceeded.Should().BeFalse();
        preview.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Preview_EveryPlayableMod_ExceedsWithoutThrowing()
    {
        // Global scope (no class anchor) with the whole playable pool: the
        // fragments are diverse enough that compaction can't fold them.
        var crit = new SlotCriteria();
        var picks = Engine.PlayableMods().Select(m => new WantedMod(m.Id)).ToList();
        picks.Should().HaveCountGreaterThan(100, "the full pool is broad enough to bust the limit");
        var preview = SlotBudget.Preview(Engine, crit, null, picks);
        preview.Exceeded.Should().BeTrue($"length={preview.Length} limit={preview.Limit} picks={picks.Count}");
        preview.Length.Should().BeGreaterThan(preview.Limit);
    }

    [Fact]
    public void Preview_IgnoresPicksAlreadyInTheCriteria_AndDoesNotMutateThem()
    {
        var crit = new SlotCriteria { ClassId = "Ring" };
        crit.Included.Add(new WantedMod("IncreasedLife2"));
        var withDuplicate = SlotBudget.Preview(Engine, crit, "Ring", [new WantedMod("IncreasedLife2")]);
        var alone = SlotBudget.Preview(Engine, crit, "Ring", []);
        withDuplicate.Length.Should().Be(alone.Length);
        crit.Included.Should().HaveCount(1, "preview must not mutate the criteria");
    }

    [Fact]
    public void Preview_BudgetsExclusionsAndClassAnchor()
    {
        var crit = new SlotCriteria { ClassId = "Ring" };
        crit.Excluded.Add("reflect");
        var withExclusion = SlotBudget.Preview(Engine, crit, "Ring", []);
        var without = SlotBudget.Preview(Engine, new SlotCriteria { ClassId = "Ring" }, "Ring", []);
        withExclusion.Length.Should().BeGreaterThan(without.Length, "exclusions are part of the budget");
    }
}

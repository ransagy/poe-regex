using System.Text.Json;
using PoeRegex.Core.Compilation;
using PoeRegex.Core.Data;
using PoeRegex.Web.Core;

namespace PoeRegex.Web.Tests;

/// <summary>
/// Over-budget multi-slot pastes may union compatible any-of groups. The
/// union must keep every target class's valid match set, not merely produce a
/// shorter string.
/// </summary>
public class AnyOfUnionTests
{
    private static readonly string Root =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
    private static readonly EngineService Engine = new();

    private static MultiPasteResult Compose(string fixture)
    {
        var snap = JsonSerializer.Deserialize(
            File.ReadAllText(Path.Combine(
                Root, "tests", "PoeRegex.Web.Tests", "Fixtures", "StringBudget", fixture)),
            WebJsonContext.Default.WorkbenchSnapshot)!;
        var state = new WorkbenchState();
        WorkbenchSnapshot.Restore(
            state, snap,
            id => Engine.FindMod(id) is not null,
            Engine.IsKnownExclusion);
        var slots = state.ActiveSlots()
            .Select(slot =>
            {
                var crit = state.CriteriaFor(slot);
                return (slot, crit.ClassId, (IEnumerable<WantedMod>)crit.Included, crit.MatchAll);
            })
            .ToList();
        var excludes = state.ActiveSlots()
            .SelectMany(slot => state.CriteriaFor(slot).Excluded)
            .Concat(state.Global.Excluded)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return Engine.Combine(slots, excludes, allowOverflow: true);
    }

    [Theory]
    [InlineData("boots-gloves.json", 183)]
    [InlineData("ring-damage.json", 186)]
    public void OverBudgetPastes_UseCompatibleAnyOfUnion(string fixture, int expectedLength)
    {
        var result = Compose(fixture);

        result.Length.Should().Be(expectedLength);
        result.Length.Should().BeLessThanOrEqualTo(250);
        result.Groups.Should().HaveCount(2);
        result.Groups.Should().OnlyContain(group => !group.Label.Contains('('));
        if (fixture == "ring-damage.json")
            result.Groups.Select(group => group.Label).Should().Contain("Gloves, Ring");

        Match(result, "Boots", "25% increased Movement Speed").Should().BeTrue();
        Match(result, "Boots", "+120 to maximum Life").Should().BeTrue();
        Match(result, "Gloves", "Adds 1 to 3 Fire damage to Attacks").Should().BeTrue();
        Match(result, "Gloves", "+120 to maximum Life").Should().BeTrue();
        Match(result, "Belt", "+120 to maximum Life").Should().BeTrue();
        Match(result, "Two Hand Maces", "20% increased Physical Damage").Should().BeTrue();
    }

    [Fact]
    public void MatchAllSlot_DoesNotBlockNormalAnyOfUnion()
    {
        var result = Compose("matchall-mixed.json");

        result.Length.Should().Be(246);
        result.Groups.Should().HaveCount(4);
        result.Groups.Select(group => group.Label)
            .Should().Contain("Gloves, Ring")
            .And.Contain("Belt, Body Armour, Helmet, Two Hand Mace");
        Matches(result, "Boots",
            "25% increased Movement Speed", "+120 to maximum Life").Should().BeTrue();
        Match(result, "Body Armours", "+180 to maximum Life").Should().BeTrue();
        Match(result, "Gloves", "Adds 1 to 3 Fire damage to Attacks").Should().BeTrue();
        Match(result, "Rings", "Adds 1 to 3 Cold damage to Attacks").Should().BeTrue();
        Match(result, "Two Hand Maces", "20% increased Physical Damage").Should().BeTrue();
    }

    [Fact]
    public void Union_DoesNotBroadenANarrowedClassRange()
    {
        var result = Compose("body-narrow-ring-damage.json");

        result.Length.Should().BeLessThan(385, "safe groups should still be unioned");
        Match(result, "Body Armours", "+110 to maximum Life").Should().BeTrue();
        Match(result, "Body Armours", "+130 to maximum Life").Should().BeFalse(
            "Body Armour was narrowed to 100–119; a 120–149 union must not leak in");
        Match(result, "Ring", "Adds 1 to 3 Cold damage to Attacks").Should().BeTrue();
        Match(result, "Gloves", "Adds 1 to 3 Cold damage to Attacks").Should().BeTrue();
    }

    private static bool Match(MultiPasteResult result, string itemClass, string line) =>
        Matches(result, itemClass, line);

    private static bool Matches(
        MultiPasteResult result, string itemClass, params string[] lines) =>
        ItemMatcher.Match(
            result.Query,
            [$"Item Class: {itemClass}", .. lines],
            Engine.Syntax).Match;
}

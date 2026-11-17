using PoeRegex.Core.Explain;
using PoeRegex.Web.Core;

namespace PoeRegex.Web.Tests;

/// <summary>
/// PoE2 tier range prototype: one combined family+range fragment, shortest
/// safe representation, and min/max only when the stat really rolls.
/// </summary>
public class TierRangeTests
{
    private static readonly EngineService Engine = new();

    private static ModFamily Family(string classId, string key) =>
        ModFamilies.Group(Engine.ModsForClass(classId))
            .First(f => f.Key == key);

    [Fact]
    public void BootsMovementSpeed_UpTier_IsOneRangeFragment()
    {
        var fam = Family("Boots", "MovementVelocity");
        var tier = fam.Tiers.Single(t => t.Name == "Stallion's");

        var pick = Engine.TierRangePick(fam, tier, up: true);
        pick.Should().NotBeNull();
        pick!.RangeModIds.Should().HaveCount(4);
        pick.MinValue.Should().Be(20);
        pick.MaxValue.Should().Be(35);
        Engine.TierRangeLabel(pick, tier, "Boots")
            .Should().Be("20–35% increased Movement Speed");

        var fragment = Engine.PickFragment(pick, "Boots");
        fragment.Should().NotBeNullOrEmpty();
        fragment.Should().Contain("eed$");
        var query = Engine.CompilePicks([pick], [], matchAll: false, itemClass: "Boots").Query;
        ItemMatcher.Match(query, ["Item Class: Boots", "20% increased Movement Speed"])
            .Match.Should().BeTrue();
        ItemMatcher.Match(query, ["Item Class: Boots", "35% increased Movement Speed"])
            .Match.Should().BeTrue();
        ItemMatcher.Match(query, ["Item Class: Boots", "15% increased Movement Speed"])
            .Match.Should().BeFalse();
        Engine.Explain(query, "Boots").Mods.Select(m => m.Id)
            .Should().Contain("MovementVelocity3",
                "the range fragment still recalls the selected tier from the catalog");
    }

    [Fact]
    public void BootsMovementSpeed_ExactTier_PrefersTheRangeOverTheName()
    {
        var fam = Family("Boots", "MovementVelocity");
        var tier = fam.Tiers.Single(t => t.Name == "Stallion's");

        var pick = Engine.BestTierPick(fam, tier, up: false, cls: "Boots");

        pick.Should().NotBeNull();
        pick!.IsTierRange.Should().BeTrue("20.*eed$ is shorter than \"Stallion's\"");
        Engine.TierRangeLabel(pick, tier, "Boots")
            .Should().Be("20% increased Movement Speed");
    }

    [Fact]
    public void BodyLife_ExactTier_PrefersTheName_UpTier_PrefersTheRange()
    {
        var fam = Family("Body Armour", "IncreasedLife");
        var tier = fam.Tiers.Single(t => t.Name == "Virile");

        var exact = Engine.BestTierPick(fam, tier, up: false, cls: "Body Armour");
        exact.Should().NotBeNull();
        exact!.IsNamePick.Should().BeTrue("\"Virile\" is shorter than the exact range fragment");

        var up = Engine.BestTierPick(fam, tier, up: true, cls: "Body Armour");
        up.Should().NotBeNull();
        up!.IsTierRange.Should().BeTrue("the six-name set is longer than one 100–214 fragment");
        up.MinValue.Should().Be(100);
        up.MaxValue.Should().Be(214);
        Engine.TierRangeLabel(up, tier, "Body Armour")
            .Should().Be("+100–214 to maximum Life");
        Engine.TierRangeLabel(up with { MinValue = 110, MaxValue = 119 }, tier, "Body Armour")
            .Should().Be("+110–119 to maximum Life");
    }

    [Fact]
    public void BodyLifeRegen_TierRange_MatchesRolledPrecision()
    {
        var family = ModFamilies.Group(Engine.ModsForClass("Body Armour"))
            .First(f => f.Members.Any(m => m.Id == "BodyArmourImplicitLifeRegenerationPercent1"));
        var tier = family.Tiers.First(t =>
            t.MemberIds.Contains("BodyArmourImplicitLifeRegenerationPercent1"));
        var pick = Engine.TierRangePick(family, tier, up: false);
        pick.Should().NotBeNull();

        var query = Engine.CompilePicks([pick!], [], matchAll: false, itemClass: "Body Armour").Query;
        ItemMatcher.Match(query,
            ["Item Class: Body Armours", "Regenerate 2.03(1.5-2.5)% of maximum Life per second"])
            .Match.Should().BeTrue("the roll can carry more decimals than the range endpoints");
        ItemMatcher.Match(query,
            ["Item Class: Body Armours", "Regenerate 1.5(1.5-2.5)% of maximum Life per second"])
            .Match.Should().BeTrue();
        ItemMatcher.Match(query,
            ["Item Class: Body Armours", "Regenerate 2.5(1.5-2.5)% of maximum Life per second"])
            .Match.Should().BeTrue();
        ItemMatcher.Match(query,
            ["Item Class: Body Armours", "Regenerate 0.5(1.5-2.5)% of maximum Life per second"])
            .Match.Should().BeFalse("the range endpoint must not stand in for a worse roll");
    }

    [Fact]
    public void MovementSpeedNameUp_HasCompactEquivalentRange()
    {
        var boots = Family("Boots", "MovementVelocity");
        var tier = boots.Tiers.Single(t => t.Name == "Stallion's");
        Engine.TierCoveredRangeLabel(boots, tier)
            .Should().Be("20–35% increased Movement Speed");
    }

    [Fact]
    public void MovementSpeedFamily_MinMax_FiltersFixedTiers()
    {
        var boots = Family("Boots", "MovementVelocity");
        var pick = boots.Pick() with { MinValue = 20, MaxValue = 30 };
        var query = Engine.CompilePicks([pick], [], matchAll: false, itemClass: "Boots").Query;

        foreach (var value in new[] { 20, 25, 30 })
        {
            ItemMatcher.Match(query,
                ["Item Class: Boots", $"{value}% increased Movement Speed"]).Match
                .Should().BeTrue($"{value}% is inside the 20–30 filter");
        }
        foreach (var value in new[] { 10, 15, 35 })
        {
            ItemMatcher.Match(query,
                ["Item Class: Boots", $"{value}% increased Movement Speed"]).Match
                .Should().BeFalse($"{value}% is outside the 20–30 filter");
        }
    }

    [Fact]
    public void NumericRange_UsesThePicksValueSpan()
    {
        // A single exact tier or a genuinely fixed mod has nothing to narrow.
        Engine.NumericRange(new WantedMod("MovementVelocity3"), "Boots").Should().BeNull();
        Engine.NumericRange(new WantedMod("GlobalSpellGemsLevel1"), "Focus").Should().BeNull();

        // A fixed-tier family still covers several values.
        var boots = Family("Boots", "MovementVelocity");
        Engine.NumericRange(boots.Pick(), "Boots")
            .Should().Be(new EngineService.NumericSpan(10, 35, 0));

        Engine.NumericRange(new WantedMod("IncreasedLife8"), "Body Armour")
            .Should().Be(new EngineService.NumericSpan(100, 119, 0));
        Engine.NumericRange(new WantedMod("IncreasedLife8", ["Virile"]), "Body Armour")
            .Should().BeNull();
    }

    [Fact]
    public void SetRange_KeepsIdentityAndRoundTrips()
    {
        var state = new WorkbenchState();
        state.AddInclude("body", new WantedMod("IncreasedLife8"));

        state.SetRange("body", new WantedMod("IncreasedLife8"), 110, 119).Should().BeTrue();
        var pick = state.CriteriaFor("body").Included[0];
        pick.MinValue.Should().Be(110);
        pick.MaxValue.Should().Be(119);
        pick.SamePick(new WantedMod("IncreasedLife8")).Should().BeTrue();

        var json = System.Text.Json.JsonSerializer.Serialize(
            WorkbenchSnapshot.Capture(state), WebJsonContext.Default.WorkbenchSnapshot);
        var back = System.Text.Json.JsonSerializer.Deserialize(
            json, WebJsonContext.Default.WorkbenchSnapshot)!;
        var restored = new WorkbenchState();
        WorkbenchSnapshot.Restore(restored, back);

        var restoredPick = restored.CriteriaFor("body").Included[0];
        restoredPick.MinValue.Should().Be(110);
        restoredPick.MaxValue.Should().Be(119);
    }
}

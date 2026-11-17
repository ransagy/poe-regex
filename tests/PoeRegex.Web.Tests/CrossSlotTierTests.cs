using PoeRegex.Core.Compilation;
using PoeRegex.Core.Data;
using PoeRegex.Web.Core;

namespace PoeRegex.Web.Tests;

/// <summary>
/// Multi-slot tier ranges: identical ranges share one group across classes;
/// different ranges stay separate and do not cross-match.
/// </summary>
public class CrossSlotTierTests
{
    private static readonly EngineService Engine = new();

    private static WantedMod RangePick(string classId, string name, bool up = false)
    {
        var family = ModFamilies.Group(Engine.ModsForClass(classId))
            .First(f => f.Key == "IncreasedLife");
        var tier = family.Tiers.Single(t => t.Name == name);
        return Engine.TierRangePick(family, tier, up)
            ?? throw new InvalidOperationException($"{classId}/{name} has no range pick");
    }

    private static string Line(string modId) =>
        AdvancedStatLine.For(Engine.FindMod(modId)!.StatLines[0]);

    [Fact]
    public void SameExactTierName_AcrossClasses_AppearsOnce()
    {
        var helmet = new WantedMod("IncreasedLife9", ["Athlete's"]);
        var body = new WantedMod("IncreasedLife9", ["Athlete's"]);
        var paste = Engine.Combine(
        [
            ("helmet (Helmet)", "Helmet", new[] { helmet }),
            ("body (Body Armour)", "Body Armour", new[] { body }),
        ], []);

        paste.Diagnostics.Should().BeEmpty();
        paste.Groups.Should().ContainSingle("the same affix name is one criterion");
        paste.Query.Split("Athlete's", StringSplitOptions.None).Should().HaveCount(2,
            "the shared name literal appears once");
        paste.Query.Should().Be("\": (Bod|Hel)\" \"Athlete's\"");
    }

    [Fact]
    public void DifferentExactTierNames_AcrossClasses_AppearOnceEach()
    {
        var helmet = new WantedMod("IncreasedLife9", ["Athlete's"]);
        var body = new WantedMod("IncreasedLife10", ["Fecund"]);
        var paste = Engine.Combine(
        [
            ("helmet (Helmet)", "Helmet", new[] { helmet }),
            ("body (Body Armour)", "Body Armour", new[] { body }),
        ], []);

        paste.Diagnostics.Should().BeEmpty();
        paste.Groups.Should().HaveCount(2);
        paste.Query.Split("Athlete's", StringSplitOptions.None).Should().HaveCount(2);
        paste.Query.Split("Fecund", StringSplitOptions.None).Should().HaveCount(2);
    }

    [Fact]
    public void GlovesTwoTiersAndOthersExact_StaySeparateButValid()
    {
        var gloves = new[]
        {
            RangePick("Gloves", "Athlete's"),
            new WantedMod("IncreasedLife10", MinValue: 150, MaxValue: 174,
                RangeModIds: ["IncreasedLife10"]),
        };
        var paste = Engine.Combine(
        [
            ("belt (Belt)", "Belt", new[] { RangePick("Belt", "Athlete's") }),
            ("body (Body Armour)", "Body Armour", new[] { RangePick("Body Armour", "Athlete's") }),
            ("gloves (Gloves)", "Gloves", gloves),
            ("helmet (Helmet)", "Helmet", new[] { RangePick("Helmet", "Athlete's") }),
        ], []);

        paste.Diagnostics.Should().BeEmpty();
        QueryShape.Validate(paste.Query).Should().BeEmpty(
            "no malformed class split or line break");
        paste.Groups.Should().HaveCount(2,
            "Belt/Body/Helmet exact Athlete's cannot union with Gloves 120–174 without over-matching Fecund");

        // Gloves has no Fecund tier, so its wider range is safe there.
        ItemMatcher.Match(paste.Query,
            ["Item Class: Gloves", Line("IncreasedLife10")]).Match.Should().BeTrue();
        // Belt/Body/Helmet asked for exact Athlete's; Fecund must not match.
        ItemMatcher.Match(paste.Query,
            ["Item Class: Body Armours", Line("IncreasedLife10")]).Match.Should().BeFalse();
        ItemMatcher.Match(paste.Query,
            ["Item Class: Body Armours", Line("IncreasedLife9")]).Match.Should().BeTrue();
    }

    [Fact]
    public void MultipleExactTiersInOneSlot_CoalesceToValidRange()
    {
        var gloves = new[]
        {
            RangePick("Gloves", "Athlete's"),
            new WantedMod("IncreasedLife10", MinValue: 150, MaxValue: 174,
                RangeModIds: ["IncreasedLife10"]),
        };
        var paste = Engine.Combine(
        [
            ("gloves (Gloves)", "Gloves", gloves),
            ("helmet (Helmet)", "Helmet", new[] { RangePick("Helmet", "Athlete's") }),
        ], []);

        paste.Diagnostics.Should().BeEmpty();
        QueryShape.Validate(paste.Query).Should().BeEmpty(
            "coalesced range fragments must never produce a malformed class split");
        paste.Query.Should().NotContain("Athlete's");
        ItemMatcher.Match(paste.Query,
            ["Item Class: Gloves", Line("IncreasedLife9")]).Match.Should().BeTrue();
        ItemMatcher.Match(paste.Query,
            ["Item Class: Gloves", Line("IncreasedLife10")]).Match.Should().BeTrue();
        ItemMatcher.Match(paste.Query,
            ["Item Class: Gloves", Line("IncreasedLife8")]).Match.Should().BeFalse(
            "the Gloves group is 120–174, not the whole life family");
    }

    [Fact]
    public void ExactAndUpAthletes_AcrossClasses_UnionToOneGroup()
    {
        var paste = Engine.Combine(
        [
            ("helmet (Helmet)", "Helmet",
                new[] { RangePick("Helmet", "Athlete's", up: true) }),
            ("body (Body Armour)", "Body Armour",
                new[] { RangePick("Body Armour", "Athlete's", up: true) }),
            ("gloves (Gloves)", "Gloves",
                new[] { RangePick("Gloves", "Athlete's", up: false) }),
        ], []);

        paste.Diagnostics.Should().BeEmpty();
        paste.Groups.Should().ContainSingle(
            "Gloves exact Athlete's is inside the Body/Helmet union and has no Fecund tier");
        paste.Groups[0].TargetClasses.Should().Equal(["Body Armour", "Gloves", "Helmet"]);
        paste.Query.Should().NotContain("Athlete's");

        ItemMatcher.Match(paste.Query,
            ["Item Class: Gloves", Line("IncreasedLife9")]).Match.Should().BeTrue();
        ItemMatcher.Match(paste.Query,
            ["Item Class: Helmets", Line("IncreasedLife10")]).Match.Should().BeTrue();
        ItemMatcher.Match(paste.Query,
            ["Item Class: Body Armours", Line("IncreasedLife13")]).Match.Should().BeTrue();
        ItemMatcher.Match(paste.Query,
            ["Item Class: Gloves", Line("IncreasedLife8")]).Match.Should().BeFalse(
            "the union starts at Athlete's 120");
    }

    [Fact]
    public void SameExactTierRange_AcrossClasses_OneSharedGroup()
    {
        var paste = Engine.Combine(
        [
            ("helmet (Helmet)", "Helmet", new[] { RangePick("Helmet", "Athlete's") }),
            ("body (Body Armour)", "Body Armour", new[] { RangePick("Body Armour", "Athlete's") }),
        ], []);

        paste.Diagnostics.Should().BeEmpty();
        paste.Groups.Should().ContainSingle("the same 120–149 range is one criterion");
        paste.Groups[0].TargetClasses.Should().Equal(["Body Armour", "Helmet"]);
        paste.Query.Should().NotContain("Athlete's",
            "tier ranges should not fall back to affix-name literals");

        var athlete = Line("IncreasedLife9");
        foreach (var cls in new[] { "Helmets", "Body Armours" })
        {
            ItemMatcher.Match(paste.Query, [$"Item Class: {cls}", athlete]).Match
                .Should().BeTrue($"{cls} with +120–149 life satisfies the shared range");
            ItemMatcher.Match(paste.Query, [$"Item Class: {cls}", Line("IncreasedLife8")]).Match
                .Should().BeFalse($"{cls} with +100–119 life is outside the shared range");
        }
    }

    [Fact]
    public void DifferentExactTierRanges_AcrossClasses_StaySeparate()
    {
        var paste = Engine.Combine(
        [
            ("helmet (Helmet)", "Helmet", new[] { RangePick("Helmet", "Athlete's") }),
            ("body (Body Armour)", "Body Armour", new[] { RangePick("Body Armour", "Fecund") }),
        ], []);

        paste.Diagnostics.Should().BeEmpty();
        paste.Groups.Should().HaveCount(2, "different tier ranges are different criteria");
        paste.Query.Should().NotContain("Athlete's").And.NotContain("Fecund");

        ItemMatcher.Match(paste.Query,
            ["Item Class: Helmets", Line("IncreasedLife9")]).Match.Should().BeTrue();
        ItemMatcher.Match(paste.Query,
            ["Item Class: Body Armours", Line("IncreasedLife10")]).Match.Should().BeTrue();
        ItemMatcher.Match(paste.Query,
            ["Item Class: Helmets", Line("IncreasedLife10")]).Match.Should().BeFalse(
            "a helmet with Fecund's range does not satisfy the helmet Athlete's slot");
        ItemMatcher.Match(paste.Query,
            ["Item Class: Body Armours", Line("IncreasedLife9")]).Match.Should().BeFalse(
            "a body with Athlete's range does not satisfy the body Fecund slot");
    }

    [Fact]
    public void SameExactTierRange_SameClassTwice_OneMergedGroup()
    {
        var paste = Engine.Combine(
        [
            ("ring1 (Ring)", "Ring", new[] { RangePick("Ring", "Virile") }),
            ("ring2 (Ring)", "Ring", new[] { RangePick("Ring", "Virile") }),
        ], []);

        paste.Diagnostics.Should().BeEmpty();
        paste.Groups.Should().ContainSingle("same-class slots merge before grouping");
        paste.Groups[0].TargetClasses.Should().Equal(["Ring"]);
        ItemMatcher.Match(paste.Query,
            ["Item Class: Rings", Line("IncreasedLife8")]).Match.Should().BeTrue();
    }

    [Fact]
    public void UpTierRanges_WithDifferentCoveredSets_DoNotOverMatch()
    {
        var paste = Engine.Combine(
        [
            ("helmet (Helmet)", "Helmet", new[] { RangePick("Helmet", "Athlete's", up: true) }),
            ("body (Body Armour)", "Body Armour", new[] { RangePick("Body Armour", "Athlete's", up: true) }),
        ], []);

        paste.Diagnostics.Should().BeEmpty();
        paste.Query.Should().NotContain("Athlete's");

        ItemMatcher.Match(paste.Query,
            ["Item Class: Helmets", Line("IncreasedLife9")]).Match.Should().BeTrue();
        ItemMatcher.Match(paste.Query,
            ["Item Class: Helmets", Line("IncreasedLife10")]).Match.Should().BeTrue();
        ItemMatcher.Match(paste.Query,
            ["Item Class: Body Armours", Line("IncreasedLife13")]).Match.Should().BeTrue();
        ItemMatcher.Match(paste.Query,
            ["Item Class: Helmets", Line("IncreasedLife8")]).Match.Should().BeFalse(
            "the helmet Athlete's-or-better range starts at 120");
    }

    [Fact]
    public void UpTierRanges_NarrowedToSameSpan_ShareOneGroup()
    {
        var helmet = RangePick("Helmet", "Athlete's", up: true) with { MaxValue = 174 };
        var body = RangePick("Body Armour", "Athlete's", up: true) with { MaxValue = 174 };

        var paste = Engine.Combine(
        [
            ("helmet (Helmet)", "Helmet", new[] { helmet }),
            ("body (Body Armour)", "Body Armour", new[] { body }),
        ], []);

        paste.Diagnostics.Should().BeEmpty();
        paste.Groups.Should().ContainSingle(
            "the narrowed 120–174 span is the same criterion on both classes");
        paste.Groups[0].TargetClasses.Should().Equal(["Body Armour", "Helmet"]);
        paste.Query.Should().NotContain("Athlete's");
    }
}

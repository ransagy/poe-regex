using PoeRegex.Core.Compilation;
using PoeRegex.Core.Data;
using PoeRegex.Web.Core;

namespace PoeRegex.Web.Tests;

/// <summary>
/// Combined-paste integration: structure, per-group recall cleanliness, and
/// lab-matcher agreement on item-level verdicts (ring+life bright, plain
/// ring dim, plain amulet bright).
/// </summary>
public class CombineServiceTests
{
    private static readonly EngineService Engine = new();

    [Fact]
    public void Combine_RingLife_AmuletAny_MatcherAgrees()
    {
        var paste = Engine.Combine(
        [
            ("weapon (Ring)", "Ring", new[] { new WantedMod("IncreasedLife6") }),
            ("amulet (Amulet)", "Amulet", []),
        ], []);
        paste.Diagnostics.Should().BeEmpty();
        paste.Groups.Should().HaveCount(1, "only the modded ring gets an explicit group");
        paste.Groups[0].TargetClasses.Should().Equal(["Ring"]);

        var lifeLine = AdvancedStatLine.For(Engine.FindMod("IncreasedLife6")!.StatLines[0]);
        ItemMatcher.Match(paste.Query, [lifeLine, "Item Class: Rings"])
            .Match.Should().BeTrue("ring with life satisfies anchor + ring group");
        ItemMatcher.Match(paste.Query, ["Item Class: Rings"])
            .Match.Should().BeFalse("plain ring fails the ring group");
        ItemMatcher.Match(paste.Query, ["Item Class: Amulets"])
            .Match.Should().BeTrue("unmodded amulet slot matches everything");
    }

    [Fact]
    public void Combine_SameClassAllOf_ReportsMergedToAnyClass()
    {
        var picks = new[] { new WantedMod("IncreasedLife6"), new WantedMod("Strength1") };
        var paste = Engine.Combine(
        [
            ("ring1 (Ring)", "Ring", picks, true),
            ("ring2 (Ring)", "Ring", picks, true),
        ], []);

        paste.MergedToAnyClasses.Should().Equal(["Ring"]);
        paste.Diagnostics.Should().Contain(d => d.Code == "paste-merge-any");
    }

    [Fact]
    public void Combine_Deterministic()
    {
        MultiPasteResult Build() => Engine.Combine(
        [
            ("weapon (Ring)", "Ring", new[] { new WantedMod("IncreasedLife6") }),
            ("amulet (Amulet)", "Amulet", []),
        ], []);
        Build().Query.Should().Be(Build().Query);
    }

    /// <summary>
    /// The reported bug: a boots slot with Movement Speed plus a crossbow
    /// slot with Projectile Skills; the boot assembled in the item builder
    /// (stat lines live only in Mapped, parsed mods empty) must match the
    /// combined paste — and so must an imported crossbow with its line.
    /// </summary>
    [Fact]
    public void Combine_CrossbowProjectile_BootsMovement_BuilderBootMatches()
    {
        const string projId = "GlobalProjectileSkillGemLevelTwoHandWeapon3";
        const string moveId = "MovementVelocity5";
        var paste = Engine.Combine(
        [
            ("weapon (Crossbow)", "Crossbow", new[] { new WantedMod(projId) }),
            ("boots (Boots)", "Boots", new[] { new WantedMod(moveId) }),
        ], []);
        paste.Diagnostics.Should().BeEmpty();

        static TestItem Item(string parsedClass, string classId, string name)
        {
            var parsed = new ParsedItem(parsedClass, "Magic", name, [], null, null, null, null, false, []);
            return new TestItem { Name = name, ClassId = classId, Parsed = parsed };
        }

        var moveLine = AdvancedStatLine.For(Engine.FindMod(moveId)!.StatLines[0]);
        var builtBoot = Item("Boots", "Boots", "Built Boots");
        builtBoot.Mapped.Add(new MappedLine(moveLine, moveLine, [], [moveId], moveId));
        ItemMatcher.Match(paste.Query, builtBoot.MatchLines()).Match
            .Should().BeTrue("boots with movement speed satisfy the boots slot group");

        var plainBoot = Item("Boots", "Boots", "Built Boots");
        ItemMatcher.Match(paste.Query, plainBoot.MatchLines()).Match
            .Should().BeFalse("boots without the wanted line are filtered out");

        // Imported items carry parsed lines; the mapped copy must dedupe.
        var parsedMod = new ParsedModBlock("prefix", null, null, [], [new ParsedValueLine(moveLine, [])]);
        var imported = new TestItem
        {
            Name = "i",
            ClassId = "Boots",
            Parsed = new ParsedItem("Boots", "Magic", "i", [], null, null, null, null, false, [parsedMod]),
        };
        imported.Mapped.Add(new MappedLine(moveLine, moveLine, [], [moveId], moveId));
        imported.MatchLines().Count(l => l == moveLine).Should().Be(1, "parsed and mapped copies dedupe");

        var projLine = AdvancedStatLine.For(Engine.FindMod(projId)!.StatLines[0]);
        var builtXbow = Item("Crossbows", "Crossbow", "Built Crossbow");
        builtXbow.Mapped.Add(new MappedLine(projLine, projLine, [], [projId], projId));
        ItemMatcher.Match(paste.Query, builtXbow.MatchLines()).Match
            .Should().BeTrue("crossbow with projectile skills satisfy the weapon slot group");
    }
}

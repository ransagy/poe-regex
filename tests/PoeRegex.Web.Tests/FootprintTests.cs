using PoeRegex.Web.Core;

namespace PoeRegex.Web.Tests;

public class FootprintTests
{
    [Theory]
    [InlineData("Belt", 2, 1)]
    [InlineData("Buckler", 2, 2)]
    [InlineData("Shield", 2, 4)]
    [InlineData("Focus", 2, 3)]
    [InlineData("One Hand Mace", 2, 3)]
    [InlineData("Two Hand Mace", 2, 4)]
    [InlineData("Wand", 1, 3)]
    [InlineData("Staff", 1, 4)]
    [InlineData("Spear", 1, 4)]
    [InlineData("Bow", 2, 4)]
    [InlineData("Crossbow", 2, 4)]
    [InlineData("Sceptre", 2, 3)]
    [InlineData("Quiver", 2, 3)]
    [InlineData("Talisman", 2, 2)]
    [InlineData("Helmet", 2, 2)]
    [InlineData("Gloves", 2, 2)]
    [InlineData("Boots", 2, 2)]
    [InlineData("Body Armour", 2, 3)]
    [InlineData("Amulet", 1, 1)]
    [InlineData("Ring", 1, 1)]
    public void ConfirmedFootprints(string classId, int cols, int rows)
    {
        var fp = ItemFootprints.For(classId);
        fp.Cols.Should().Be(cols);
        fp.Rows.Should().Be(rows);
        fp.Confidence.Should().Be(FootprintConfidence.Confirmed);
    }

    [Fact]
    public void EveryPlayableClass_ResolvesPositiveDims()
    {
        var engine = new EngineService();
        foreach (var c in engine.PlayableClasses())
        {
            var fp = ItemFootprints.For(c.Id);
            fp.Cols.Should().BeGreaterThan(0);
            fp.Rows.Should().BeGreaterThan(0);
        }
    }

    [Fact]
    public void SlotClasses_ArmorAccessoryAreOneToOne_WeaponExcludesThem()
    {
        var engine = new EngineService();
        var playable = engine.PlayableClasses().Select(c => c.Id);
        SlotClasses.AllowedFor("ring1", playable).Should().Equal(["Ring"]);
        SlotClasses.AllowedFor("ring2", playable).Should().Equal(["Ring"]);
        SlotClasses.AllowedFor("amulet", playable).Should().Equal(["Amulet"]);
        SlotClasses.AllowedFor("body", playable).Should().Equal(["Body Armour"]);
        var weapon = SlotClasses.AllowedFor("weapon", playable);
        weapon.Should().Contain(["Crossbow", "Bow", "Shield", "Quiver"]);
        weapon.Should().NotContain(["Ring", "Amulet", "Belt", "Helmet", "Body Armour",
            "Gloves", "Boots", "Talisman"]);
        foreach (var slot in new[] { "weapon", "helmet", "body", "gloves", "boots",
                     "belt", "amulet", "ring1", "ring2" })
        {
            var state = new WorkbenchState();
            SlotClasses.AllowedFor(slot, playable)
                .Should().Contain(state.SlotClassOrDefault(slot), "default class must fit its slot");
        }
    }

    [Fact]
    public void Placement_MultiCell_NoOverlap_InBounds()
    {
        var state = new WorkbenchState();
        ParsedItem Body(string n) => new(null, "Rare", n, [], null, null, null, null, false, []);
        state.AddTestItem(new TestItem { Name = "a", ClassId = "Body Armour", Parsed = Body("a") });
        state.AddTestItem(new TestItem { Name = "b", ClassId = "Body Armour", Parsed = Body("b") });
        state.AddTestItem(new TestItem { Name = "c", ClassId = "Staff", Parsed = Body("c") });
        var cells = new HashSet<(int, int)>();
        foreach (var t in state.TestItems)
        {
            t.Cols.Should().BeGreaterThan(0);
            t.Rows.Should().BeGreaterThan(0);
            for (var dr = 0; dr < t.Rows; dr++)
                for (var dc = 0; dc < t.Cols; dc++)
                {
                    var cell = (t.Row + dr, t.Col + dc);
                    cell.Item1.Should().BeLessThan(GridPlacement.Rows);
                    cell.Item2.Should().BeLessThan(GridPlacement.Cols);
                    cells.Add(cell).Should().BeTrue("no overlap");
                }
        }
        var staff = state.TestItems[2];
        (staff.Cols, staff.Rows).Should().Be((1, 4));
    }
}

using PoeRegex.Web.Core;

namespace PoeRegex.Web.Tests;

public class WorkbenchStateTests
{
    [Fact]
    public void Defaults_SingleMode_WeaponSelected()
    {
        var state = new WorkbenchState();
        state.Mode.Should().Be("single");
        state.ActiveSlots().Should().Equal(["weapon"]);
    }

    [Fact]
    public void SetMode_Global_ClearsSlot_GlobalYieldsNoSlots()
    {
        var state = new WorkbenchState();
        state.SetMode("global");
        state.ActiveSlots().Should().BeEmpty();
        state.SelectSlot("helmet"); // ignored in global, like the TUI
        state.ActiveSlots().Should().BeEmpty();
    }

    [Fact]
    public void SetMode_BackToSingle_RestoresWeapon_WhenNoneSelected()
    {
        var state = new WorkbenchState();
        state.SetMode("global");
        state.SetMode("single");
        state.ActiveSlots().Should().Equal(["weapon"]);
    }

    [Fact]
    public void SelectSlot_Single_Replaces()
    {
        var state = new WorkbenchState();
        state.SelectSlot("body");
        state.ActiveSlots().Should().Equal(["body"]);
    }

    [Fact]
    public void SelectSlot_Multi_Toggles()
    {
        var state = new WorkbenchState();
        state.SetMode("multi");
        state.ActiveSlots().Should().BeEmpty();
        state.SelectSlot("body");
        state.SelectSlot("helmet");
        state.ActiveSlots().Should().Equal(["body", "helmet"]);
        state.SelectSlot("body");
        state.ActiveSlots().Should().Equal(["helmet"]);
    }

    [Fact]
    public void SetMode_RejectsUnknown()
    {
        var state = new WorkbenchState();
        Assert.Throws<ArgumentOutOfRangeException>(() => state.SetMode("bogus"));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.SelectSlot("bogus"));
    }

    [Fact]
    public void GearSlots_NineSlots_AllUniqueIds()
    {
        GearSlot.All.Should().HaveCount(9);
        GearSlot.All.Select(s => s.Id).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Snapshot_RoundTrips_State()
    {
        var state = new WorkbenchState();
        state.SetMode("multi");
        state.SelectSlot("body");
        state.SelectSlot("helmet");
        state.SetSlotClass("body", "Helmet");
        state.AddInclude("IncreasedLife6", 80);
        state.AddExclude("reflect");
        var parsed = ItemTextParser.Parse(FixtureTexts.Talisman);
        state.AddTestItem(new TestItem { Name = "t", ClassId = "Talisman", Parsed = parsed });

        var json = System.Text.Json.JsonSerializer.Serialize(
            WorkbenchSnapshot.Capture(state), WebJsonContext.Default.WorkbenchSnapshot);
        var back = System.Text.Json.JsonSerializer.Deserialize(
            json, WebJsonContext.Default.WorkbenchSnapshot)!;

        var restored = new WorkbenchState();
        WorkbenchSnapshot.Restore(restored, back);
        restored.Mode.Should().Be("multi");
        restored.ActiveSlots().Should().Equal(["body", "helmet"]);
        restored.SlotClassOrDefault("body").Should().Be("Helmet");
        restored.Included.Should().Equal([new WantedMod("IncreasedLife6", 80)]);
        restored.Excluded.Should().Equal(["reflect"]);
        restored.TestItems.Should().HaveCount(1);
        restored.TestItems[0].Name.Should().Be("t");
        restored.TestItems[0].Parsed.Mods.Should().HaveCount(2);
    }

    [Fact]
    public void Snapshot_Restore_DropsUnknowns()
    {
        var state = new WorkbenchState();
        var snap = new WorkbenchSnapshot(1, "bogus", "bogus",
            ["bogus", "body"], new Dictionary<string, string> { ["bogus"] = "X" },
            [new WantedMod("")], [""], []);
        WorkbenchSnapshot.Restore(state, snap);
        state.Mode.Should().Be("single");
        state.MultiSlots.Should().Equal(["body"]);
        state.ActiveSlots().Should().Equal(["weapon"]);
    }

    [Fact]
    public void Chips_AddRemoveClearBehave()
    {
        var state = new WorkbenchState();
        state.AddInclude("IncreasedLife6", 80);
        state.AddInclude("IncreasedLife6"); // dup ignored
        state.AddExclude("reflect");
        state.AddExclude("reflect"); // dup ignored
        state.Included.Should().HaveCount(1);
        state.Included[0].Should().Be(new WantedMod("IncreasedLife6", 80));
        state.Excluded.Should().Equal(["reflect"]);

        state.RemoveInclude("IncreasedLife6").Should().BeTrue();
        state.RemoveInclude("nope").Should().BeFalse();
        state.RemoveExclude("reflect").Should().BeTrue();
        state.Included.Should().BeEmpty();
        state.Excluded.Should().BeEmpty();

        state.AddInclude("A");
        state.AddExclude("B");
        state.ClearChips();
        state.Included.Should().BeEmpty();
        state.Excluded.Should().BeEmpty();
    }

    [Fact]
    public void SlotClass_DefaultsAndOverride()
    {
        var state = new WorkbenchState();
        state.SlotClassOrDefault("body").Should().Be("Body Armour");
        state.SlotClassOrDefault("ring2").Should().Be("Ring");
        state.SetSlotClass("body", "Helmet");
        state.SlotClassOrDefault("body").Should().Be("Helmet");
        Assert.Throws<ArgumentOutOfRangeException>(() => state.SetSlotClass("bogus", "Ring"));
    }
}

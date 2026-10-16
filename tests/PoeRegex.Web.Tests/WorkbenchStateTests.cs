using PoeRegex.Web.Core;

namespace PoeRegex.Web.Tests;

public class WorkbenchStateTests
{
    [Fact]
    public void Defaults_GlobalMode_NoSlots()
    {
        var state = new WorkbenchState();
        state.Mode.Should().Be("global");
        state.ActiveSlots().Should().BeEmpty();
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
        state.SetMode("single");
        state.SelectSlot("body");
        state.ActiveSlots().Should().Equal(["body"]);
    }

    [Fact]
    public void SelectSlot_Multi_MovesFocus_WithoutEnrolling()
    {
        var state = new WorkbenchState();
        state.SetMode("multi");
        state.MultiFocus.Should().Be("weapon", "multi defaults the editing focus");
        state.SelectSlot("body");
        state.SelectSlot("helmet");
        state.MultiFocus.Should().Be("helmet");
        state.ActiveSlots().Should().BeEmpty("enrollment is derived from chips");
    }

    [Fact]
    public void Multi_Enrollment_FollowsChips_InPanelOrder()
    {
        var state = new WorkbenchState();
        state.SetMode("multi");
        state.AddInclude("body", "IncreasedLife6");
        state.AddInclude("helmet", "IncreasedLife6", 80);
        state.AddExclude("ring2", "reflect"); // excluded-only still enrolls
        state.ActiveSlots().Should().Equal(["helmet", "body", "ring2"]);
        state.ClearChips("body");
        state.ActiveSlots().Should().Equal(["helmet", "ring2"]);
    }

    [Fact]
    public void SetMode_RejectsUnknown()
    {
        var state = new WorkbenchState();
        Assert.Throws<ArgumentOutOfRangeException>(() => state.SetMode("bogus"));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.SelectSlot("bogus"));
    }

    [Fact]
    public void GearSlots_TenSlots_AllUniqueIds()
    {
        GearSlot.All.Should().HaveCount(10);
        GearSlot.All.Select(s => s.Id).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Snapshot_RoundTrips_State()
    {
        var state = new WorkbenchState();
        state.SetMode("multi");
        state.SelectSlot("helmet"); // editing focus
        state.SetSlotClass("body", "Helmet");
        state.AddInclude("body", "IncreasedLife6", 80);
        state.AddInclude("helmet", "IncreasedLife6");
        state.AddExclude(null, "reflect");
        var parsed = ItemTextParser.Parse(FixtureTexts.Talisman);
        state.AddTestItem(new TestItem { Name = "t", ClassId = "Talisman", Parsed = parsed });

        var json = System.Text.Json.JsonSerializer.Serialize(
            WorkbenchSnapshot.Capture(state), WebJsonContext.Default.WorkbenchSnapshot);
        var back = System.Text.Json.JsonSerializer.Deserialize(
            json, WebJsonContext.Default.WorkbenchSnapshot)!;

        var restored = new WorkbenchState();
        WorkbenchSnapshot.Restore(restored, back);
        restored.Mode.Should().Be("multi");
        restored.MultiFocus.Should().Be("helmet");
        restored.ActiveSlots().Should().Equal(["helmet", "body"]);
        restored.SlotClassOrDefault("body").Should().Be("Helmet");
        restored.CriteriaFor("body").Included.Should().Equal([new WantedMod("IncreasedLife6", 80)]);
        restored.CriteriaFor(null).Excluded.Should().Equal(["reflect"]);
        restored.TestItems.Should().HaveCount(1);
        restored.TestItems[0].Name.Should().Be("t");
        restored.TestItems[0].Parsed.Mods.Should().HaveCount(2);
    }

    [Fact]
    public void Snapshot_Restore_DropsUnknowns()
    {
        var state = new WorkbenchState();
        var snap = new WorkbenchSnapshot(1, "bogus", "bogus", "bogus",
            new Dictionary<string, SlotCriteria>
            {
                ["bogus"] = new SlotCriteria { ClassId = "X" },
                ["body"] = new SlotCriteria { ClassId = "Helmet" },
            },
            new SlotCriteria(),
            []);
        WorkbenchSnapshot.Restore(state, snap);
        state.Mode.Should().Be("global");
        state.MultiFocus.Should().BeNull("unknown focus slot is dropped");
        state.SlotClassOrDefault("body").Should().Be("Helmet");
        state.ActiveSlots().Should().BeEmpty();
    }

    [Fact]
    public void Chips_PerSlotAndGlobalScopes()
    {
        var state = new WorkbenchState();
        state.AddInclude("body", "IncreasedLife6", 80);
        state.AddInclude("body", "IncreasedLife6"); // dup ignored
        state.AddInclude(null, "IncreasedLife6"); // global is a separate scope
        state.AddExclude("body", "reflect");
        state.CriteriaFor("body").Included.Should().HaveCount(1);
        state.CriteriaFor(null).Included.Should().HaveCount(1);
        state.CriteriaFor("helmet").Included.Should().BeEmpty("untouched slot stays empty");

        state.RemoveInclude("body", "IncreasedLife6").Should().BeTrue();
        state.RemoveInclude("body", "nope").Should().BeFalse();
        state.RemoveExclude("body", "reflect").Should().BeTrue();
        state.CriteriaFor(null).Included.Should().HaveCount(1, "global untouched");

        state.AddInclude("body", "A");
        state.ClearChips("body");
        state.CriteriaFor("body").Included.Should().BeEmpty();
    }

    [Fact]
    public void SlotClass_DefaultsAndOverride()
    {
        var state = new WorkbenchState();
        state.SlotClassOrDefault("body").Should().Be("Body Armour");
        state.SlotClassOrDefault("ring2").Should().Be("Ring");
        state.SetSlotClass("body", "Helmet");
        state.SlotClassOrDefault("body").Should().Be("Helmet");
        state.CriteriaFor("body").ClassId.Should().Be("Helmet");
        Assert.Throws<KeyNotFoundException>(() => state.CriteriaFor("bogus"));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.SetSlotClass("bogus", "Ring"));
    }
}

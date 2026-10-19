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
        state.SelectSlot("helmet"); // ignored in global mode
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
        state.AddInclude("helmet", "IncreasedLife6");
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
        state.AddInclude("body", "IncreasedLife6");
        state.AddInclude("helmet", "IncreasedLife6");
        state.AddInclude("helmet", new WantedMod("ColdDamagePrefixOnWeapon4", ["Snowy", "Hailing"]));
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
        restored.CriteriaFor("body").Included.Should().Equal([new WantedMod("IncreasedLife6")]);
        restored.CriteriaFor("helmet").Included.Should().Equal(
            [new WantedMod("IncreasedLife6"), new WantedMod("ColdDamagePrefixOnWeapon4", ["Snowy", "Hailing"])]);
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
    public void Snapshot_Restore_DropsRetiredMinValues()
    {
        // Old snapshots carry a `minValue` on wanted mods from the retired
        // min-roll box; loading must not resurrect a floor the UI can no
        // longer show or edit.
        var json = """
            {"Version":2,"Mode":"global","SelectedSlot":null,"MultiFocus":null,
             "Slots":{"body":{"ClassId":"Helmet","Included":[{"ModId":"IncreasedLife6","MinValue":80}],"Excluded":[]}},
             "Global":{"ClassId":"","Included":[],"Excluded":[]},"TestItems":[]}
            """;
        var back = System.Text.Json.JsonSerializer.Deserialize(
            json, WebJsonContext.Default.WorkbenchSnapshot)!;
        var restored = new WorkbenchState();
        WorkbenchSnapshot.Restore(restored, back);
        restored.CriteriaFor("body").Included.Should().Equal([new WantedMod("IncreasedLife6")]);
    }

    [Fact]
    public void Snapshot_Restore_HealsStaleFootprints()
    {
        var state = new WorkbenchState();
        var belt = new TestItem { Name = "b", ClassId = "Belt", Parsed = ItemTextParser.Parse(FixtureTexts.Belt) };
        var talisman = new TestItem { Name = "t", ClassId = "Talisman", Parsed = ItemTextParser.Parse(FixtureTexts.Talisman) };
        var snap = new WorkbenchSnapshot(1, "global", null, null,
            new Dictionary<string, SlotCriteria>(), new SlotCriteria(), [belt, talisman]);
        WorkbenchSnapshot.Restore(state, snap);
        belt.Cols.Should().Be(2);
        belt.Rows.Should().Be(1);
        talisman.Cols.Should().Be(2);
        talisman.Rows.Should().Be(2);
        (belt.Row, belt.Col).Should().Be((0, 0));
        (talisman.Row, talisman.Col).Should().Be((0, 2), "healed items re-place instead of stacking");
    }

    [Fact]
    public void Snapshot_Restore_NotifiesOnce_AfterStateIsWhole()
    {
        var state = new WorkbenchState();
        var belt = new TestItem { Name = "b", ClassId = "Belt", Parsed = ItemTextParser.Parse(FixtureTexts.Belt) };
        var snap = new WorkbenchSnapshot(1, "multi", "helmet", null,
            new Dictionary<string, SlotCriteria> { ["helmet"] = new SlotCriteria { ClassId = "Helmet" } },
            new SlotCriteria(), [belt]);

        // Mirrors the app's persistence listener: every change captures a save.
        var notifications = 0;
        WorkbenchSnapshot? saved = null;
        state.Changed += () =>
        {
            notifications++;
            saved = WorkbenchSnapshot.Capture(state);
        };

        WorkbenchSnapshot.Restore(state, snap);

        notifications.Should().Be(1, "restore must not leak half-applied states to observers");
        saved!.Mode.Should().Be("multi");
        saved.TestItems.Should().ContainSingle(i => i.Name == "b",
            "the save triggered by restore must already see the restored backpack");
    }

    [Fact]
    public void SelectTestItem_Remove_ClearsSelection()
    {
        var state = new WorkbenchState();
        var item = new TestItem { Name = "x", ClassId = "Belt", Parsed = ItemTextParser.Parse(FixtureTexts.Belt) };
        state.AddTestItem(item);
        state.SelectTestItem(item.Id);
        state.SelectedItemId.Should().Be(item.Id);
        state.RemoveTestItem(item.Id).Should().BeTrue();
        state.SelectedItemId.Should().BeNull();
        state.TestItems.Should().BeEmpty();
    }

    [Fact]
    public void AddTestItem_GridFull_Rejects_WithoutTouchingExistingItems()
    {
        static ParsedItem Parsed(string n) => new(null, "Rare", n, [], null, null, null, null, false, []);
        var state = new WorkbenchState();
        for (var i = 0; i < GridPlacement.Cols * GridPlacement.Rows; i++)
            state.AddTestItem(new TestItem { Name = $"a{i}", ClassId = "Amulet", Parsed = Parsed($"a{i}") })
                .Should().BeTrue();
        state.TestItems.Should().HaveCount(60);
        state.AddTestItem(new TestItem { Name = "late", ClassId = "Amulet", Parsed = Parsed("late") })
            .Should().BeFalse("a full grid rejects new items");
        state.TestItems.Should().HaveCount(60);
    }

    [Fact]
    public void Snapshot_Restore_OverCapacity_KeepsItems_RemoveReplacesUnplaced()
    {
        static TestItem Amulet(string n) => new()
        {
            Name = n,
            ClassId = "Amulet",
            Parsed = new ParsedItem(null, "Rare", n, [], null, null, null, null, false, []),
        };
        var state = new WorkbenchState();
        var items = Enumerable.Range(0, 61).Select(i => Amulet($"a{i}")).ToList();
        var snap = new WorkbenchSnapshot(1, "global", null, null,
            new Dictionary<string, SlotCriteria>(), new SlotCriteria(), items);
        WorkbenchSnapshot.Restore(state, snap);
        state.TestItems.Should().HaveCount(61, "restore never drops items");
        state.TestItems.Count(t => t.Row < 0).Should().Be(1, "the overflow item waits unplaced");
        var unplaced = state.TestItems.Single(t => t.Row < 0);
        state.RemoveTestItem(state.TestItems[0].Id).Should().BeTrue();
        unplaced.Row.Should().BeGreaterThanOrEqualTo(0, "freeing a cell places waiting items");
    }

    [Fact]
    public void Chips_PerSlotAndGlobalScopes()
    {
        var state = new WorkbenchState();
        state.AddInclude("body", "IncreasedLife6");
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
    public void Chips_NamePicks_AreDistinctFromPlainPicksOnTheSameAnchor()
    {
        var state = new WorkbenchState();
        const string anchor = "ColdDamagePrefixOnWeapon4";
        state.AddInclude("weapon", anchor);                       // any tier
        state.AddInclude("weapon", new WantedMod(anchor, ["Snowy"])); // exact tier
        state.AddInclude("weapon", new WantedMod(anchor, ["Snowy"])); // dup ignored
        state.AddInclude("weapon", new WantedMod(anchor, ["Snowy", "Hailing"])); // tier or better
        state.CriteriaFor("weapon").Included.Should().HaveCount(3,
            "plain, exact-name and name-set picks coexist on one anchor");

        state.RemoveInclude("weapon", new WantedMod(anchor, ["Snowy"])).Should().BeTrue();
        state.RemoveInclude("weapon", new WantedMod(anchor, ["Snowy"])).Should().BeFalse("already removed");
        state.CriteriaFor("weapon").Included.Should().Equal(
            [new WantedMod(anchor), new WantedMod(anchor, ["Snowy", "Hailing"])]);
    }

    [Fact]
    public void AddIncludes_BatchesOneNotification_AndIgnoresDuplicates()
    {
        var state = new WorkbenchState();
        var notified = 0;
        state.Changed += () => notified++;

        var added = state.AddIncludes("body",
            [new WantedMod("IncreasedLife6"), new WantedMod("IncreasedLife6"), new WantedMod("Strength4")]);

        added.Should().Be(2, "the duplicate pick is ignored");
        notified.Should().Be(1, "bulk adds notify once, not per pick");
        state.CriteriaFor("body").Included.Should().HaveCount(2);

        state.AddIncludes("body", [new WantedMod("Strength4")]).Should().Be(0);
        notified.Should().Be(1, "a no-op bulk add does not notify");
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

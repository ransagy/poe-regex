namespace PoeRegex.Web.Core;

/// <summary>
/// The ten editable gear slots (weapon + offhand + armour + accessories).
/// </summary>
public sealed record GearSlot(string Id, string Label, int Cols, int Rows)
{
    public static readonly IReadOnlyList<GearSlot> All =
    [
        new("weapon", "Weapon", 2, 4),
        new("offhand", "Offhand", 2, 4),
        new("helmet", "Helmet", 2, 2),
        new("body", "Body Armour", 2, 3),
        new("gloves", "Gloves", 2, 2),
        new("boots", "Boots", 2, 2),
        new("belt", "Belt", 2, 1),
        new("amulet", "Amulet", 1, 1),
        new("ring1", "Ring", 1, 1),
        new("ring2", "Ring", 1, 1),
    ];

    public static GearSlot? ById(string id) =>
        All.FirstOrDefault(s => s.Id == id);
}

/// <summary>One wanted (+) or unwanted (-) modifier: a catalog mod id with an optional value floor.</summary>
public sealed record WantedMod(string ModId, double? MinValue = null);

/// <summary>Search criteria for one slot (or the global scope): base class + wanted/unwanted mods.</summary>
public sealed class SlotCriteria
{
    public string ClassId { get; set; } = "";
    public List<WantedMod> Included { get; set; } = [];
    public List<string> Excluded { get; set; } = [];
}

/// <summary>
/// UI workbench state. Mode semantics mirror the TUI's
/// `demos/opentui-tui/src/app.mjs`: single edits one slot, global selects
/// none. Multi has no manual roster: a slot joins the multi group exactly
/// when it has any wanted/unwanted chips, and clicking a slot only moves the
/// editing focus. Every slot owns its own criteria so multi-slot compiles
/// differ per slot (plus a separate global criteria set).
/// </summary>
public sealed class WorkbenchState
{
    private static readonly Dictionary<string, string> DefaultClasses = new(StringComparer.Ordinal)
    {
        ["weapon"] = "Crossbow",
        ["offhand"] = "Shield",
        ["helmet"] = "Helmet",
        ["body"] = "Body Armour",
        ["gloves"] = "Gloves",
        ["boots"] = "Boots",
        ["belt"] = "Belt",
        ["amulet"] = "Amulet",
        ["ring1"] = "Ring",
        ["ring2"] = "Ring",
    };

    /// <summary>Global is the first and default mode (one string for chosen mods).</summary>
    public string Mode { get; private set; } = "global";

    /// <summary>Single mode: the active slot id (default: weapon).</summary>
    public string? SelectedSlot { get; private set; } = null;

    /// <summary>Multi mode: the slot whose editor is showing (membership is derived from chips).</summary>
    public string? MultiFocus { get; private set; } = null;

    /// <summary>Per-slot criteria (class + wanted/unwanted mods).</summary>
    public Dictionary<string, SlotCriteria> Slots { get; } = new(StringComparer.Ordinal);

    /// <summary>Classless global criteria.</summary>
    public SlotCriteria Global { get; } = new();

    /// <summary>Backpack lab test items (imported or freeform).</summary>
    public List<TestItem> TestItems { get; } = [];

    public event Action? Changed;

    public WorkbenchState()
    {
        foreach (var (slot, cls) in DefaultClasses)
            Slots[slot] = new SlotCriteria { ClassId = cls };
    }

    /// <summary>Criteria for a slot, or the global set when slot is null.</summary>
    public SlotCriteria CriteriaFor(string? slot) =>
        slot is null ? Global : Slots[slot];

    public void SetMode(string mode)
    {
        if (mode is not ("single" or "global" or "multi"))
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "expected single|global|multi");
        Mode = mode;
        if (mode == "global")
            SelectedSlot = null;
        else if (mode == "single" && SelectedSlot is null)
            SelectedSlot = "weapon";
        else if (mode == "multi")
            MultiFocus ??= SelectedSlot ?? "weapon";
        Notify();
    }

    public void SelectSlot(string slotId)
    {
        if (GearSlot.ById(slotId) is null)
            throw new ArgumentOutOfRangeException(nameof(slotId), slotId, "unknown gear slot");
        if (Mode == "global")
            return;
        if (Mode == "single")
            SelectedSlot = slotId;
        else
            MultiFocus = slotId;
        Notify();
    }

    /// <summary>Set the multi editing focus directly (snapshot restore).</summary>
    public void FocusSlot(string slotId)
    {
        if (GearSlot.ById(slotId) is null)
            throw new ArgumentOutOfRangeException(nameof(slotId), slotId, "unknown gear slot");
        MultiFocus = slotId;
        Notify();
    }

    /// <summary>True when a slot has any wanted/unwanted chips (multi enrollment).</summary>
    public bool Enrolled(string slotId) =>
        Slots.TryGetValue(slotId, out var c) && (c.Included.Count > 0 || c.Excluded.Count > 0);

    /// <summary>Slots the query engine should compile for (global: none).</summary>
    public IReadOnlyList<string> ActiveSlots() => Mode switch
    {
        "global" => [],
        "single" => SelectedSlot is null ? [] : [SelectedSlot],
        _ => GearSlot.All.Where(s => Enrolled(s.Id)).Select(s => s.Id).ToList(),
    };

    public void SetSlotClass(string slotId, string classId)
    {
        if (GearSlot.ById(slotId) is null)
            throw new ArgumentOutOfRangeException(nameof(slotId), slotId, "unknown gear slot");
        CriteriaFor(slotId).ClassId = classId;
        Notify();
    }

    public string SlotClassOrDefault(string slotId) =>
        Slots.TryGetValue(slotId, out var c) && c.ClassId.Length > 0 ? c.ClassId : "Ring";

    public void AddInclude(string? slot, string modId, double? minValue = null)
    {
        var list = CriteriaFor(slot).Included;
        if (!list.Any(m => m.ModId == modId))
            list.Add(new WantedMod(modId, minValue));
        Notify();
    }

    public bool RemoveInclude(string? slot, string modId)
    {
        var list = CriteriaFor(slot).Included;
        var i = list.FindIndex(m => m.ModId == modId);
        if (i < 0) return false;
        list.RemoveAt(i);
        Notify();
        return true;
    }

    public void AddExclude(string? slot, string modId)
    {
        var list = CriteriaFor(slot).Excluded;
        if (!list.Contains(modId, StringComparer.Ordinal))
            list.Add(modId);
        Notify();
    }

    public bool RemoveExclude(string? slot, string modId)
    {
        var removed = CriteriaFor(slot).Excluded.Remove(modId);
        if (removed) Notify();
        return removed;
    }

    /// <summary>Clear wanted/unwanted mods for one scope (slot or global).</summary>
    public void ClearChips(string? slot)
    {
        var c = CriteriaFor(slot);
        c.Included.Clear();
        c.Excluded.Clear();
        Notify();
    }

    public void AddTestItem(TestItem item)
    {
        var fp = ItemFootprints.For(item.ClassId);
        var w = item.Cols > 0 ? item.Cols : fp.Cols;
        var h = item.Rows > 0 ? item.Rows : fp.Rows;
        var taken = new HashSet<(int, int)>();
        foreach (var t in TestItems)
            for (var dr = 0; dr < Math.Max(1, t.Rows); dr++)
                for (var dc = 0; dc < Math.Max(1, t.Cols); dc++)
                    taken.Add((t.Row + dr, t.Col + dc));
        for (var r = 0; r + h <= GridPlacement.Rows; r++)
            for (var c = 0; c + w <= GridPlacement.Cols; c++)
            {
                var free = true;
                for (var dr = 0; dr < h && free; dr++)
                    for (var dc = 0; dc < w && free; dc++)
                        if (taken.Contains((r + dr, c + dc)))
                            free = false;
                if (free)
                {
                    item.Row = r;
                    item.Col = c;
                    item.Cols = w;
                    item.Rows = h;
                    TestItems.Add(item);
                    Notify();
                    return;
                }
            }
        TestItems.Add(item); // grid full: list-only
        Notify();
    }

    public bool RemoveTestItem(string id)
    {
        var i = TestItems.FindIndex(t => t.Id == id);
        if (i < 0) return false;
        TestItems.RemoveAt(i);
        Notify();
        return true;
    }

    /// <summary>Restore factory defaults (mode, slots, classes, criteria, items).</summary>
    public void Reset()
    {
        Mode = "global";
        SelectedSlot = null;
        MultiFocus = null;
        Slots.Clear();
        foreach (var (slot, cls) in DefaultClasses)
            Slots[slot] = new SlotCriteria { ClassId = cls };
        Global.ClassId = "";
        Global.Included.Clear();
        Global.Excluded.Clear();
        TestItems.Clear();
        Notify();
    }

    private void Notify() => Changed?.Invoke();
}

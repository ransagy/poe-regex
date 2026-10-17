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

/// <summary>One wanted (+) or unwanted (-) modifier: a catalog mod id.</summary>
public sealed record WantedMod(string ModId);

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

    /// <summary>
    /// Backstop for bulk mutations: notifications are dropped while nested
    /// scopes are active and fire once when the outermost scope ends.
    /// </summary>
    private int _suppressNotify;

    public WorkbenchState()
    {
        foreach (var (slot, cls) in DefaultClasses)
            Slots[slot] = new SlotCriteria { ClassId = cls };
    }

    /// <summary>
    /// Applies a batch of mutations (snapshot restore) without firing
    /// <see cref="Changed"/> per step; one notification fires at the end.
    /// Observers therefore never see — and the persistence listener never
    /// saves — a half-applied state (restoring the mode used to trigger a
    /// save while the backpack was still empty, silently dropping items on
    /// the next load).
    /// </summary>
    public void WithSuppressedNotifications(Action mutate)
    {
        _suppressNotify++;
        try { mutate(); }
        finally
        {
            _suppressNotify--;
            if (_suppressNotify == 0) Notify();
        }
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

    public void AddInclude(string? slot, string modId)
    {
        var list = CriteriaFor(slot).Included;
        if (!list.Any(m => m.ModId == modId))
            list.Add(new WantedMod(modId));
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

    /// <summary>
    /// Add a backpack item, auto-placing it when there is room. When the grid
    /// is full the item is rejected (returns false) — callers surface the
    /// error; nothing is ever dropped or implicitly replaced.
    /// </summary>
    public bool AddTestItem(TestItem item)
    {
        var fp = ItemFootprints.For(item.ClassId);
        item.Cols = item.Cols > 0 ? item.Cols : fp.Cols;
        item.Rows = item.Rows > 0 ? item.Rows : fp.Rows;
        var (row, col) = FirstFreeSpot(TestItems, item.Cols, item.Rows);
        if (row < 0) return false;
        item.Row = row;
        item.Col = col;
        TestItems.Add(item);
        Notify();
        return true;
    }

    /// <summary>First free top-left cell for a w x h item, or (-1,-1) when the grid is full.</summary>
    public static (int Row, int Col) FirstFreeSpot(IReadOnlyList<TestItem> items, int w, int h)
    {
        var taken = new HashSet<(int, int)>();
        foreach (var t in items)
        {
            if (t.Row < 0 || t.Col < 0) continue; // unplaced items occupy nothing
            for (var dr = 0; dr < Math.Max(1, t.Rows); dr++)
                for (var dc = 0; dc < Math.Max(1, t.Cols); dc++)
                    taken.Add((t.Row + dr, t.Col + dc));
        }
        for (var r = 0; r + h <= GridPlacement.Rows; r++)
            for (var c = 0; c + w <= GridPlacement.Cols; c++)
            {
                var free = true;
                for (var dr = 0; dr < h && free; dr++)
                    for (var dc = 0; dc < w && free; dc++)
                        if (taken.Contains((r + dr, c + dc)))
                            free = false;
                if (free)
                    return (r, c);
            }
        return (-1, -1);
    }

    public bool RemoveTestItem(string id)
    {
        var i = TestItems.FindIndex(t => t.Id == id);
        if (i < 0) return false;
        TestItems.RemoveAt(i);
        if (SelectedItemId == id)
            SelectedItemId = null;
        ReplaceUnplaced();
        Notify();
        return true;
    }

    /// <summary>
    /// Place items that arrived unplaced (legacy over-capacity snapshots) —
    /// called whenever space frees up. Item order decides who gets the spot.
    /// </summary>
    private void ReplaceUnplaced()
    {
        foreach (var t in TestItems)
        {
            if (t.Row >= 0 && t.Col >= 0) continue;
            var (row, col) = FirstFreeSpot(TestItems, Math.Max(1, t.Cols), Math.Max(1, t.Rows));
            if (row < 0) break;
            t.Row = row;
            t.Col = col;
        }
    }

    /// <summary>Backpack item whose detail box is open (view-only; not persisted).</summary>
    public string? SelectedItemId { get; private set; }

    public void SelectTestItem(string? id)
    {
        SelectedItemId = id;
        Notify();
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
        SelectedItemId = null;
        Notify();
    }

    private void Notify()
    {
        if (_suppressNotify > 0) return;
        Changed?.Invoke();
    }
}

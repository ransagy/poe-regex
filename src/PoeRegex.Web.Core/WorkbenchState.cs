namespace PoeRegex.Web.Core;

/// <summary>
/// The nine editable gear slots. Cell footprints are the author's reading of
/// the PoE2 paper-doll and are marked TO-CONFIRM until the maintainer signs
/// off the shell screenshot (Phase 3 exit).
/// </summary>
public sealed record GearSlot(string Id, string Label, int Cols, int Rows)
{
    public static readonly IReadOnlyList<GearSlot> All =
    [
        new("weapon", "Weapon", 2, 4), // TO-CONFIRM footprint
        new("helmet", "Helmet", 2, 2), // TO-CONFIRM footprint
        new("body", "Body Armour", 2, 3), // TO-CONFIRM footprint
        new("gloves", "Gloves", 2, 2), // TO-CONFIRM footprint
        new("boots", "Boots", 2, 2), // TO-CONFIRM footprint
        new("belt", "Belt", 2, 1), // TO-CONFIRM footprint
        new("amulet", "Amulet", 1, 1),
        new("ring1", "Ring", 1, 1),
        new("ring2", "Ring", 1, 1),
    ];

    public static GearSlot? ById(string id) =>
        All.FirstOrDefault(s => s.Id == id);
}

/// <summary>One wanted (+) or unwanted (-) modifier: a catalog mod id with an optional value floor.</summary>
public sealed record WantedMod(string ModId, double? MinValue = null);

/// <summary>
/// UI workbench state (mode + slot selection). Mirrors the TUI's
/// `demos/opentui-tui/src/app.mjs` mode semantics: single keeps one slot,
/// global selects none, multi toggles many. Per-slot class/mod configuration
/// arrives in Phase 4.
/// </summary>
public sealed class WorkbenchState
{
    public string Mode { get; private set; } = "single";

    /// <summary>Single mode: the active slot id (default: weapon).</summary>
    public string? SelectedSlot { get; private set; } = "weapon";

    /// <summary>Multi mode: toggled slot ids.</summary>
    public HashSet<string> MultiSlots { get; } = new(StringComparer.Ordinal);

    /// <summary>Base-equipment class per gear slot (defaults use real poe2 class ids).</summary>
    public Dictionary<string, string> SlotClass { get; } = new(StringComparer.Ordinal)
    {
        ["weapon"] = "Crossbow",
        ["helmet"] = "Helmet",
        ["body"] = "Body Armour",
        ["gloves"] = "Gloves",
        ["boots"] = "Boots",
        ["belt"] = "Belt",
        ["amulet"] = "Amulet",
        ["ring1"] = "Ring",
        ["ring2"] = "Ring",
    };

    /// <summary>Shared wanted mods (TUI parity: one set applied to every active slot).</summary>
    public List<WantedMod> Included { get; } = [];

    /// <summary>Shared unwanted mod ids.</summary>
    public List<string> Excluded { get; } = [];

    /// <summary>Backpack lab test items (imported or freeform).</summary>
    public List<TestItem> TestItems { get; } = [];

    public event Action? Changed;

    public void AddTestItem(TestItem item)
    {
        // First free 1x1 cell, row-major (real footprints need the dimension table).
        var taken = TestItems.Select(t => (t.Row, t.Col)).ToHashSet();
        for (var r = 0; r < 5; r++)
            for (var c = 0; c < 12; c++)
                if (taken.Add((r, c)))
                {
                    item.Row = r;
                    item.Col = c;
                    TestItems.Add(item);
                    Notify();
                    return;
                }
        TestItems.Add(item); // grid full (60): list-only
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

    public void SetMode(string mode)
    {
        if (mode is not ("single" or "global" or "multi"))
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "expected single|global|multi");
        Mode = mode;
        if (mode == "global")
            SelectedSlot = null;
        else if (mode == "single" && SelectedSlot is null)
            SelectedSlot = "weapon";
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
        {
            if (!MultiSlots.Add(slotId))
                MultiSlots.Remove(slotId);
        }
        Notify();
    }

    /// <summary>Slots the query engine should compile for (global: none).</summary>
    public IReadOnlyList<string> ActiveSlots() => Mode switch
    {
        "global" => [],
        "single" => SelectedSlot is null ? [] : [SelectedSlot],
        _ => MultiSlots.OrderBy(s => s, StringComparer.Ordinal).ToList(),
    };

    public void SetSlotClass(string slotId, string classId)
    {
        if (GearSlot.ById(slotId) is null)
            throw new ArgumentOutOfRangeException(nameof(slotId), slotId, "unknown gear slot");
        SlotClass[slotId] = classId;
        Notify();
    }

    public string SlotClassOrDefault(string slotId) =>
        SlotClass.TryGetValue(slotId, out var c) ? c : "Ring";

    public void AddInclude(string modId, double? minValue = null)
    {
        if (!Included.Any(m => m.ModId == modId))
            Included.Add(new WantedMod(modId, minValue));
        Notify();
    }

    public bool RemoveInclude(string modId)
    {
        var i = Included.FindIndex(m => m.ModId == modId);
        if (i < 0) return false;
        Included.RemoveAt(i);
        Notify();
        return true;
    }

    public void AddExclude(string modId)
    {
        if (!Excluded.Contains(modId, StringComparer.Ordinal))
            Excluded.Add(modId);
        Notify();
    }

    public bool RemoveExclude(string modId)
    {
        var removed = Excluded.Remove(modId);
        if (removed) Notify();
        return removed;
    }

    /// <summary>Clear wanted/unwanted mods (keeps game/mode/slots/classes — TUI `r` parity).</summary>
    public void ClearChips()
    {
        Included.Clear();
        Excluded.Clear();
        Notify();
    }

    private void Notify() => Changed?.Invoke();
}

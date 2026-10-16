using System.Text.Json.Serialization;

namespace PoeRegex.Web.Core;

/// <summary>
/// Persisted workbench shape (localStorage now; URL-share later). Versioned
/// for forward-compatible loads. Domain objects are records/init-only and
/// round-trip through the source-generated <see cref="WebJsonContext"/>.
/// </summary>
public sealed record WorkbenchSnapshot(
    int Version,
    string Mode,
    string? SelectedSlot,
    string? MultiFocus,
    Dictionary<string, SlotCriteria> Slots,
    SlotCriteria Global,
    List<TestItem> TestItems)
{
    public const int CurrentVersion = 1;

    public static WorkbenchSnapshot Capture(WorkbenchState state) => new(
        CurrentVersion,
        state.Mode,
        state.SelectedSlot,
        state.MultiFocus,
        state.Slots.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal),
        state.Global,
        state.TestItems.ToList());

    /// <summary>Restore with light validation (unknown modes/slots/classes are dropped).</summary>
    public static void Restore(WorkbenchState state, WorkbenchSnapshot snap) =>
        // One batch: a single Changed fires when the state is whole again, so
        // the persistence listener saves the rebuilt snapshot — never a
        // half-applied one with the backpack still empty.
        state.WithSuppressedNotifications(() => RestoreCore(state, snap));

    private static void RestoreCore(WorkbenchState state, WorkbenchSnapshot snap)
    {
        try { state.SetMode(snap.Mode); } catch (ArgumentOutOfRangeException) { /* keep default */ }
        if (snap.SelectedSlot is not null && GearSlot.ById(snap.SelectedSlot) is not null)
        {
            try
            {
                if (state.Mode == "global") state.SetMode("single");
                state.SelectSlot(snap.SelectedSlot);
            }
            catch (ArgumentOutOfRangeException) { /* keep default */ }
        }
        if (snap.MultiFocus is not null && GearSlot.ById(snap.MultiFocus) is not null)
            state.FocusSlot(snap.MultiFocus);
        foreach (var (slot, crit) in snap.Slots)
        {
            if (GearSlot.ById(slot) is null) continue;
            var mine = state.CriteriaFor(slot);
            if (crit.ClassId.Length > 0) mine.ClassId = crit.ClassId;
            mine.Included.Clear();
            mine.Included.AddRange(crit.Included.Where(m => m.ModId.Length > 0));
            mine.Excluded.Clear();
            mine.Excluded.AddRange(crit.Excluded.Where(e => e.Length > 0));
        }
        state.Global.Included.Clear();
        state.Global.Included.AddRange(snap.Global.Included.Where(m => m.ModId.Length > 0));
        state.Global.Excluded.Clear();
        state.Global.Excluded.AddRange(snap.Global.Excluded.Where(e => e.Length > 0));
        state.TestItems.Clear();
        var placed = new List<TestItem>(snap.TestItems.Count);
        foreach (var item in snap.TestItems)
        {
            // Heal stale pre-footprint items: derive the class footprint.
            if (item.Cols <= 0 || item.Rows <= 0)
            {
                var fp = ItemFootprints.For(item.ClassId);
                item.Cols = fp.Cols;
                item.Rows = fp.Rows;
            }
            // Keep valid placements stable; re-place anything out of bounds or
            // overlapping (legacy grid-full adds stacked at 0,0). When even the
            // re-placement fails the item stays in the data as unplaced
            // (row/col -1) and renders again once space frees up.
            var fits = item.Row >= 0 && item.Col >= 0
                && item.Row + item.Rows <= GridPlacement.Rows
                && item.Col + item.Cols <= GridPlacement.Cols
                && !Overlaps(placed, item);
            if (!fits)
            {
                var (row, col) = WorkbenchState.FirstFreeSpot(placed, item.Cols, item.Rows);
                item.Row = row;
                item.Col = col;
            }
            placed.Add(item);
        }
        state.TestItems.AddRange(placed);
    }

    /// <summary>True when any already-placed item shares a cell with this one.</summary>
    private static bool Overlaps(IReadOnlyList<TestItem> placed, TestItem item)
    {
        foreach (var p in placed)
        {
            if (p.Row < 0 || p.Col < 0) continue;
            if (item.Col < p.Col + Math.Max(1, p.Cols) && p.Col < item.Col + Math.Max(1, item.Cols) &&
                item.Row < p.Row + Math.Max(1, p.Rows) && p.Row < item.Row + Math.Max(1, item.Rows))
                return true;
        }
        return false;
    }
}

/// <summary>Source-generated JSON (AOT-safe) for workbench persistence.</summary>
[JsonSerializable(typeof(WorkbenchSnapshot))]
[JsonSerializable(typeof(SlotCriteria))]
[JsonSerializable(typeof(WantedMod))]
[JsonSerializable(typeof(TestItem))]
[JsonSerializable(typeof(MappedLine))]
[JsonSerializable(typeof(ParsedItem))]
[JsonSerializable(typeof(ParsedModBlock))]
[JsonSerializable(typeof(ParsedValueLine))]
public sealed partial class WebJsonContext : JsonSerializerContext;

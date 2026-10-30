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
    public const int CurrentVersion = 4;

    public static WorkbenchSnapshot Capture(WorkbenchState state) => new(
        CurrentVersion,
        state.Mode,
        null, // single mode retired; kept for loading v2 snapshots
        state.FocusedSlot,
        state.Slots.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal),
        state.Global,
        state.TestItems.ToList());

    /// <summary>Restore with light validation (unknown modes/slots/classes are dropped).</summary>
    public static void Restore(WorkbenchState state, WorkbenchSnapshot snap) =>
        // One batch: a single Changed fires when the state is whole again, so
        // the persistence listener saves the rebuilt snapshot — never a
        // half-applied one with the backpack still empty.
        state.WithSuppressedNotifications(() => RestoreCore(state, snap));

    /// <summary>Restores modes, classes and picks only; the backpack stays as it is.</summary>
    public static void RestoreCriteria(WorkbenchState state, WorkbenchSnapshot snap) =>
        state.WithSuppressedNotifications(() =>
        {
            foreach (var crit in state.Slots.Values)
            {
                crit.Included.Clear();
                crit.Excluded.Clear();
                crit.MatchAll = false;
            }
            RestoreCore(state, snap, items: false);
        });

    /// <summary>Maps retired mode names onto the two-mode model.</summary>
    private static (string Mode, string? Focus) ModeOf(WorkbenchSnapshot snap) => snap.Mode switch
    {
        "advanced" => ("advanced", snap.MultiFocus),
        "multi" => ("advanced", snap.MultiFocus),
        // v2 single mode: its slot becomes the advanced editing focus.
        "single" => ("advanced", snap.SelectedSlot ?? snap.MultiFocus),
        _ => ("simple", null),
    };

    private static void RestoreCore(WorkbenchState state, WorkbenchSnapshot snap, bool items = true)
    {
        var (mode, focus) = ModeOf(snap);
        try { state.SetMode(mode); } catch (ArgumentOutOfRangeException) { /* keep default */ }
        if (mode == "advanced" && focus is not null && GearSlot.ById(focus) is not null)
        {
            try { state.FocusSlot(focus); }
            catch (ArgumentOutOfRangeException) { /* keep default */ }
        }
        foreach (var (slot, crit) in snap.Slots)
        {
            if (GearSlot.ById(slot) is null) continue;
            var mine = state.CriteriaFor(slot);
            if (crit.ClassId.Length > 0) mine.ClassId = crit.ClassId;
            mine.Included.Clear();
            mine.Included.AddRange(Picks(snap, crit.Included));
            mine.Excluded.Clear();
            mine.Excluded.AddRange(crit.Excluded.Where(e => e.Length > 0));
            mine.MatchAll = crit.MatchAll;
        }
        state.Global.Included.Clear();
        state.Global.Included.AddRange(Picks(snap, snap.Global.Included));
        state.Global.Excluded.Clear();
        state.Global.Excluded.AddRange(snap.Global.Excluded.Where(e => e.Length > 0));
        state.Global.MatchAll = snap.Global.MatchAll;
        if (!items) return;
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

    /// <summary>Snapshots before v4 carry floors from the retired min-roll box that nobody set on purpose; drop them.</summary>
    private static IEnumerable<WantedMod> Picks(WorkbenchSnapshot snap, IEnumerable<WantedMod> picks) =>
        picks.Where(m => m.ModId.Length > 0)
            .Select(m => snap.Version < 4 && m.MinValue is not null ? m with { MinValue = null } : m);

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
[JsonSerializable(typeof(List<SavedRecipe>))]
public sealed partial class WebJsonContext : JsonSerializerContext;

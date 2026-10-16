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
    public static void Restore(WorkbenchState state, WorkbenchSnapshot snap)
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
        foreach (var item in snap.TestItems)
        {
            // Heal stale pre-footprint items: derive the class footprint and
            // re-place so old 1x1 renders (and stacks) resolve correctly.
            if (item.Cols <= 0 || item.Rows <= 0)
            {
                var fp = ItemFootprints.For(item.ClassId);
                item.Cols = fp.Cols;
                item.Rows = fp.Rows;
                var (row, col) = WorkbenchState.FirstFreeSpot(state.TestItems, item.Cols, item.Rows);
                if (row >= 0)
                {
                    item.Row = row;
                    item.Col = col;
                }
            }
            state.TestItems.Add(item);
        }
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

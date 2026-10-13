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
    List<string> MultiSlots,
    Dictionary<string, SlotCriteria> Slots,
    SlotCriteria Global,
    List<TestItem> TestItems)
{
    public const int CurrentVersion = 1;

    public static WorkbenchSnapshot Capture(WorkbenchState state) => new(
        CurrentVersion,
        state.Mode,
        state.SelectedSlot,
        state.MultiSlots.OrderBy(s => s, StringComparer.Ordinal).ToList(),
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
        state.MultiSlots.Clear();
        foreach (var s in snap.MultiSlots)
            if (GearSlot.ById(s) is not null)
                state.MultiSlots.Add(s);
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
        state.TestItems.AddRange(snap.TestItems);
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

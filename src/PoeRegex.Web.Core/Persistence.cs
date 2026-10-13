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
    Dictionary<string, string> SlotClass,
    List<WantedMod> Included,
    List<string> Excluded,
    List<TestItem> TestItems)
{
    public const int CurrentVersion = 1;

    public static WorkbenchSnapshot Capture(WorkbenchState state) => new(
        CurrentVersion,
        state.Mode,
        state.SelectedSlot,
        state.MultiSlots.OrderBy(s => s, StringComparer.Ordinal).ToList(),
        new Dictionary<string, string>(state.SlotClass, StringComparer.Ordinal),
        state.Included.ToList(),
        state.Excluded.ToList(),
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
        foreach (var (slot, cls) in snap.SlotClass)
            if (GearSlot.ById(slot) is not null && cls.Length > 0)
                state.SlotClass[slot] = cls;
        state.Included.Clear();
        state.Included.AddRange(snap.Included.Where(m => m.ModId.Length > 0));
        state.Excluded.Clear();
        state.Excluded.AddRange(snap.Excluded.Where(e => e.Length > 0));
        state.TestItems.Clear();
        state.TestItems.AddRange(snap.TestItems);
    }
}

/// <summary>Source-generated JSON (AOT-safe) for workbench persistence.</summary>
[JsonSerializable(typeof(WorkbenchSnapshot))]
[JsonSerializable(typeof(WantedMod))]
[JsonSerializable(typeof(TestItem))]
[JsonSerializable(typeof(MappedLine))]
[JsonSerializable(typeof(ParsedItem))]
[JsonSerializable(typeof(ParsedModBlock))]
[JsonSerializable(typeof(ParsedValueLine))]
public sealed partial class WebJsonContext : JsonSerializerContext;

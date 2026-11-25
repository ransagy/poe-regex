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
    List<TestItem> TestItems,
    List<string>? ExternalIncludes = null,
    string Game = "poe2")
{
    public const int CurrentVersion = 6;

    public static WorkbenchSnapshot Capture(WorkbenchState state, string game = "poe2") => new(
        CurrentVersion,
        state.Mode,
        null, // single mode retired; kept for loading v2 snapshots
        state.FocusedSlot,
        state.Slots.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal),
        state.Global,
        state.TestItems.ToList(),
        state.ExternalIncludes.ToList(),
        game);

    /// <summary>
    /// Restore with light validation (unknown modes/slots/classes are dropped).
    /// When a catalog lookup is supplied, picks and exclusions whose mod ids
    /// no longer exist are dropped and returned so the caller can explain.
    /// </summary>
    public static IReadOnlyList<string> Restore(
        WorkbenchState state,
        WorkbenchSnapshot snap,
        Func<string, bool>? knownMod = null,
        Func<string, bool>? knownExclusion = null)
    {
        var removed = new List<string>();
        // One batch: a single Changed fires when the state is whole again, so
        // the persistence listener saves the rebuilt snapshot — never a
        // half-applied one with the backpack still empty.
        state.WithSuppressedNotifications(() =>
            RestoreCore(state, snap, removed, knownMod, knownExclusion));
        return Removed(removed);
    }

    /// <summary>Restores modes, classes and picks only; the backpack stays as it is.</summary>
    public static IReadOnlyList<string> RestoreCriteria(
        WorkbenchState state,
        WorkbenchSnapshot snap,
        Func<string, bool>? knownMod = null,
        Func<string, bool>? knownExclusion = null)
    {
        var removed = new List<string>();
        state.WithSuppressedNotifications(() =>
        {
            foreach (var crit in state.Slots.Values)
            {
                crit.Included.Clear();
                crit.Excluded.Clear();
                crit.MatchAll = false;
            }
            RestoreCore(state, snap, removed, knownMod, knownExclusion, items: false);
        });
        return Removed(removed);
    }

    private static IReadOnlyList<string> Removed(List<string> removed) =>
        removed.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();

    /// <summary>Maps retired mode names onto the two-mode model.</summary>
    private static (string Mode, string? Focus) ModeOf(WorkbenchSnapshot snap) => snap.Mode switch
    {
        "advanced" => ("advanced", snap.MultiFocus),
        "multi" => ("advanced", snap.MultiFocus),
        // v2 single mode: its slot becomes the advanced editing focus.
        "single" => ("advanced", snap.SelectedSlot ?? snap.MultiFocus),
        _ => ("simple", null),
    };

    private static void RestoreCore(
        WorkbenchState state,
        WorkbenchSnapshot snap,
        List<string>? removed = null,
        Func<string, bool>? knownMod = null,
        Func<string, bool>? knownExclusion = null,
        bool items = true)
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
            mine.Included.AddRange(Picks(snap, crit.Included, knownMod, removed));
            mine.Excluded.Clear();
            mine.Excluded.AddRange(
                KnownExclusions(crit.Excluded, knownExclusion ?? knownMod, removed));
            mine.MatchAll = crit.MatchAll;
            mine.NormalizeMatchAll();
        }
        state.Global.Included.Clear();
        state.Global.Included.AddRange(Picks(snap, snap.Global.Included, knownMod, removed));
        state.Global.Excluded.Clear();
        state.Global.Excluded.AddRange(
            KnownExclusions(snap.Global.Excluded, knownExclusion ?? knownMod, removed));
        state.Global.MatchAll = snap.Global.MatchAll;
        state.Global.NormalizeMatchAll();
        state.ExternalIncludes.Clear();
        state.ExternalIncludes.AddRange(
            (snap.ExternalIncludes ?? []).Where(e => e.Length > 0).Distinct(StringComparer.Ordinal));
        if (!items) return;
        state.TestItems.Clear();
        if (snap.TestItems is null) return;
        var placed = new List<TestItem>(snap.TestItems.Count);
        foreach (var item in snap.TestItems)
        {
            if (item is null || item.Parsed is null) continue;
            item.Mapped ??= [];
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

    /// <summary>
    /// Snapshots before v4 carry floors from the retired min-roll box that
    /// nobody set on purpose; drop them, and any floor/ceiling out of range.
    /// Picks with ids that no longer exist are dropped instead of being
    /// compiled as raw literals.
    /// </summary>
    private static IEnumerable<WantedMod> Picks(
        WorkbenchSnapshot snap,
        IEnumerable<WantedMod> picks,
        Func<string, bool>? knownMod,
        List<string>? removed) =>
        picks.Where(m => m.ModId.Length > 0)
            .Where(m =>
            {
                if (knownMod is null) return true;
                if (m.IsRawText) return true;
                var known =
                    knownMod(m.ModId) &&
                    (m.VariantModIds is not { Count: > 0 } variants ||
                     variants.All(knownMod)) &&
                    (m.RangeModIds is not { Count: > 0 } rangeIds ||
                     rangeIds.All(knownMod));
                if (!known) removed?.Add(m.ModId);
                return known;
            })
            .Select(m =>
            {
                var min = m.MinValue;
                var max = m.MaxValue;
                if (min is { } lo && (snap.Version < 4 || !WantedMod.IsValidMinValue(lo))) min = null;
                if (max is { } hi && !WantedMod.IsValidMaxValue(hi)) max = null;
                if (min is { } a && max is { } b && a > b)
                    min = max = null;
                return m with { MinValue = min, MaxValue = max };
            });

    private static IEnumerable<string> KnownExclusions(
        IEnumerable<string> exclusions,
        Func<string, bool>? knownMod,
        List<string>? removed)
    {
        foreach (var exclusion in exclusions)
        {
            if (exclusion.Length == 0) continue;
            if (knownMod is not null && !RawExclusion.IsRaw(exclusion) && !knownMod(exclusion))
            {
                removed?.Add(exclusion);
                continue;
            }
            yield return exclusion;
        }
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
[JsonSerializable(typeof(List<SavedPick>))]
public sealed partial class WebJsonContext : JsonSerializerContext;

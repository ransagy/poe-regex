using PoeRegex.Core.Data;

namespace PoeRegex.Web.Core;

/// <summary>
/// Pure client-side mod-list helpers, ported from the TUI's
/// `demos/opentui-tui/src/app.mjs` (filterMods, availableMods, mergeMods).
/// Kept dependency-free so they are unit-testable without a browser.
/// </summary>
public static class ModSearch
{
    /// <summary>
    /// Friendly display text for a stat line: the per-number range rendering,
    /// falling back to the representative line when ranges are absent.
    /// </summary>
    public static string DisplayLine(StatLineEntry sl) =>
        string.IsNullOrEmpty(sl.RangeLine) ? sl.Line : sl.RangeLine;

    /// <summary>Case-insensitive substring filter over name, id, and every stat line.</summary>
    public static IReadOnlyList<ModEntry> FilterMods(IEnumerable<ModEntry> mods, string? text)
    {
        var t = (text ?? "").Trim();
        if (t.Length == 0) return mods.ToList();
        return mods.Where(m =>
            (m.Name?.Contains(t, StringComparison.OrdinalIgnoreCase) ?? false) ||
            m.Id.Contains(t, StringComparison.OrdinalIgnoreCase) ||
            m.StatLines.Any(s => s.Line.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                                 s.RangeLine.Contains(t, StringComparison.OrdinalIgnoreCase))).ToList();
    }

    /// <summary>Mods not already chosen (by id).</summary>
    public static IReadOnlyList<ModEntry> AvailableMods(
        IEnumerable<ModEntry> mods, IEnumerable<string> takenIds)
    {
        var taken = new HashSet<string>(takenIds, StringComparer.Ordinal);
        return mods.Where(m => !taken.Contains(m.Id)).ToList();
    }

    /// <summary>Union of per-slot mod lists, deduped by id (first wins).</summary>
    public static IReadOnlyList<ModEntry> MergeMods(IEnumerable<IEnumerable<ModEntry>> lists)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var out_ = new List<ModEntry>();
        foreach (var list in lists)
            foreach (var m in list)
                if (seen.Add(m.Id))
                    out_.Add(m);
        return out_;
    }

    public static double MeterRatio(int length, int limit) =>
        limit <= 0 ? 0 : Math.Min(1, (double)length / limit);

    public static string MeterColor(int length, int limit)
    {
        var ratio = MeterRatio(length, limit);
        return ratio >= 0.9 ? "red" : ratio >= 0.6 ? "orange" : "green";
    }
}

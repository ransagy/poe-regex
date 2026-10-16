namespace PoeRegex.Web.Core;

/// <summary>One pasted line with its catalog mapping outcome.</summary>
public sealed record MappedLine(
    string Text,
    string CanonicalText,
    IReadOnlyList<double> Rolls,
    IReadOnlyList<string> Candidates,
    string? SelectedModId);

/// <summary>
/// A test item in the backpack lab: parsed text plus per-line catalog mapping.
/// Grid footprint defaults to 1x1; real per-class footprints need the item
/// dimension table (Phase 5 QA question) and arrive as data later.
/// </summary>
public sealed class TestItem
{
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public required string Name { get; set; }
    public string? ClassId { get; set; }
    public required ParsedItem Parsed { get; set; }
    public List<MappedLine> Mapped { get; set; } = [];
    /// <summary>True when the item was assembled in the ItemBuilder (so its
    /// mapped lines are synthetic and must be re-rendered from the catalog).</summary>
    public bool Built { get; set; }
    public int Row { get; set; }
    public int Col { get; set; }
    /// <summary>Backpack footprint in cells (0 = unset, treated as 1x1).</summary>
    public int Cols { get; set; }
    public int Rows { get; set; }

    /// <summary>
    /// Matchable lines for query verdicts: the game's `Item Class:` line
    /// first (slot and combined-paste queries carry a class anchor phrased
    /// against it), then base lines, name, mod lines and mapped lines.
    /// Mapped lines matter for builder-assembled items, whose stat lines
    /// live only there (parsed mods stay empty); for imported items they
    /// duplicate the parsed lines and are deduplicated.
    /// </summary>
    public IReadOnlyList<string> MatchLines()
    {
        var lines = new List<string>(Parsed.AllLines());
        foreach (var ml in Mapped)
            if (!lines.Contains(ml.Text, StringComparer.Ordinal))
                lines.Add(ml.Text);
        if (!string.IsNullOrEmpty(Parsed.ItemClass))
            lines.Insert(0, $"Item Class: {Parsed.ItemClass}");
        return lines;
    }

    /// <summary>Re-renders this item's mapped lines from the current catalog when
    /// it was builder-assembled (or merely looks like one: no parsed mod
    /// blocks but mapped mod ids are present). Returns true when the displayed
    /// text changed (so callers can persist). Lines whose mod id is no longer
    /// in the catalog keep their stored text as a fallback.</summary>
    public bool Reheal(EngineService engine)
    {
        if (engine is null) return false;
        if (!Built && !(Parsed.Mods.Count == 0 && Mapped.Count > 0)) return false;
        if (Mapped.Count == 0) { Built = true; return false; }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var candidate = new List<MappedLine>(Mapped.Count);
        foreach (var ml in Mapped)
        {
            if (ml.SelectedModId is not { Length: > 0 } mid || engine.FindMod(mid) is not { } mod)
            {
                candidate.Add(ml);
                continue;
            }
            if (!seen.Add(mid))
            {
                candidate.Add(ml);
                continue;
            }
            foreach (var sl in mod.StatLines)
                candidate.Add(new MappedLine(sl.Line, sl.Line, ml.Rolls, ml.Candidates, mid));
        }

        var differ = candidate.Count != Mapped.Count;
        if (!differ)
        {
            for (var i = 0; i < candidate.Count; i++)
            {
                if (!string.Equals(candidate[i].Text, Mapped[i].Text, StringComparison.Ordinal)
                    || !string.Equals(candidate[i].CanonicalText, Mapped[i].CanonicalText, StringComparison.Ordinal))
                {
                    differ = true;
                    break;
                }
            }
        }

        var wasBuilt = Built;
        Built = true;
        if (differ)
        {
            Mapped = candidate;
            return true;
        }
        return !wasBuilt;
    }
}

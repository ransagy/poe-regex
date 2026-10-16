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
}

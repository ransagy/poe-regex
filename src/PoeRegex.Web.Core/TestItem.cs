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

    public IReadOnlyList<string> MatchLines() => Parsed.AllLines();
}

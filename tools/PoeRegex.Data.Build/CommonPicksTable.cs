using System.Text;
using PoeRegex.Web.Core;

namespace PoeRegex.Data.Build;

/// <summary>
/// Render the contributor-facing common-picks table from
/// <c>data/common-picks.json</c> and keep it in sync with
/// <c>docs/COMMON-PICKS.md</c>. The table is generated so labels, scopes, and
/// resolved family keys cannot drift from the data file.
/// </summary>
internal static class CommonPicksTable
{
    public const string StartMarker = "<!-- common-picks:start -->";
    public const string EndMarker = "<!-- common-picks:end -->";
    public const string RelativePath = "docs/COMMON-PICKS.md";

    public static string Render(EngineService engine)
    {
        var lines = new List<string>
        {
            StartMarker,
            "_Generated from [`data/common-picks.json`](../data/common-picks.json) by " +
            "`common-picks --table`. Do not edit by hand._",
            "",
            "| Pick | Data file | Scope | Resolved family |",
            "| --- | --- | --- | --- |",
        };

        foreach (var entry in CommonPicks.Entries)
        {
            var data = !string.IsNullOrWhiteSpace(entry.Family)
                ? $"`{Cell(entry.Family.Trim())}`"
                : $"match: \"{Cell(entry.Match?.Trim() ?? "")}\"";
            var scope = entry.Classes.Count == 0
                ? "All gear"
                : Cell(string.Join(", ", entry.Classes));
            lines.Add(
                $"| {Cell(entry.Label.Trim())} | {data} | {scope} | {Resolved(engine, entry)} |");
        }

        lines.Add(EndMarker);
        return string.Join("\n", lines);
    }

    /// <summary>Replace the marked block, preserving the document's newlines.</summary>
    public static string ReplaceBlock(string document, string block)
    {
        var (start, end) = Bounds(document);
        var newline = document.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var normalized = newline == "\n" ? block : block.Replace("\n", newline);
        return string.Concat(document.AsSpan(0, start), normalized, document.AsSpan(end));
    }

    /// <summary>Return the marked block normalized to LF, or null when absent.</summary>
    public static string? ExtractBlock(string document)
    {
        try
        {
            var (start, end) = Bounds(document);
            return document[start..end].Replace("\r\n", "\n");
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static (int Start, int End) Bounds(string document)
    {
        var start = document.IndexOf(StartMarker, StringComparison.Ordinal);
        var end = document.IndexOf(EndMarker, StringComparison.Ordinal);
        if (start < 0 || end < 0 || end < start)
        {
            throw new InvalidOperationException(
                $"{RelativePath} needs {StartMarker} and {EndMarker}.");
        }
        return (start, end + EndMarker.Length);
    }

    private static string Resolved(EngineService engine, CommonPick entry)
    {
        var scopes = entry.Classes.Count == 0
            ? new string?[] { null }
            : entry.Classes.Cast<string?>().ToArray();
        var parts = new List<string>();
        foreach (var cls in scopes)
        {
            var keys = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var family in CommonPicksAudit.Resolve(engine, entry, cls))
                keys.Add(family.Key);
            var text = keys.Count == 0
                ? "—"
                : string.Join(", ", keys.Select(k => $"`{Cell(k)}`"));
            parts.Add(scopes.Length == 1 ? text : $"{cls}: {text}");
        }
        return string.Join("; ", parts);
    }

    private static string Cell(string value)
    {
        var text = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (ch is '|' or '\n' or '\r') text.Append(ch == '|' ? "\\|" : ' ');
            else text.Append(ch);
        }
        return text.ToString();
    }
}

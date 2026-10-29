namespace PoeRegex.Core.Compilation;

/// <summary>
/// Finds the shortest class-line selector for one catalog class. The search
/// is deliberately data-time only: runtime class anchors come from the shipped
/// table. Candidates are literal substrings of the class name joined by `.`
/// (one skipped character) or `.+` (one or more), with at most three
/// wildcards and no leading/trailing wildcard. Those are the only shapes that
/// can beat a literal prefix without adding a wildcard that matches nothing on
/// the target line. Each shorter candidate is checked with
/// <see cref="EngineCorpus.ClassTermExact"/> and
/// <see cref="EngineCorpus.ClassTermQuiet"/> before it can win.
/// </summary>
internal static class ClassAnchorOptimizer
{
    private const int MaxWildcards = 3;

    private sealed record Candidate(
        string Pattern,
        int Length,
        int FirstLiteral,
        int LiteralCharacters);

    /// <summary>
    /// Shortest legal anchor for <paramref name="classId"/>. Falls back to the
    /// escaped literal name prefix when no shorter candidate is exact and
    /// quiet.
    /// </summary>
    public static string Shortest(EngineCorpus corpus, string classId)
    {
        var entry = corpus.FindItemClass(classId);
        var baseline = Poe2Syntax.EscapeLiteral(corpus.ComputeClassNamePrefix(classId));
        if (entry is null || string.IsNullOrEmpty(entry.Name) || baseline.Length <= 1)
            return baseline;

        foreach (var candidate in Candidates(entry.Name, baseline.Length - 1))
        {
            if (candidate.Length >= baseline.Length) continue;
            if (!corpus.ClassTermExact(candidate.Pattern, [classId])) continue;
            if (!corpus.ClassTermQuiet(candidate.Pattern)) continue;
            return candidate.Pattern;
        }
        return baseline;
    }

    /// <summary>
    /// Deterministic candidate set, best first: shortest, then the longest
    /// leading literal (readability), then the most literal characters, then
    /// ordinal text.
    /// </summary>
    private static IReadOnlyList<Candidate> Candidates(string name, int maxLength)
    {
        var best = new Dictionary<string, Candidate>(StringComparer.Ordinal);
        Walk(name, 0, 0, 0, 0, 0, 0, [], maxLength, best);

        return best.Values
            .OrderBy(c => c.Length)
            .ThenByDescending(c => c.FirstLiteral)
            .ThenByDescending(c => c.LiteralCharacters)
            .ThenBy(c => c.Pattern, StringComparer.Ordinal)
            .ToList();
    }

    private static void Walk(
        string name,
        int start,
        int blocks,
        int length,
        int previousEnd,
        int firstLiteral,
        int literalCharacters,
        List<string> pieces,
        int maxLength,
        Dictionary<string, Candidate> best)
    {
        if (blocks > 0)
        {
            var pattern = string.Concat(pieces);
            var candidate = new Candidate(pattern, pattern.Length, firstLiteral, literalCharacters);
            if (!best.TryGetValue(pattern, out var current) ||
                candidate.FirstLiteral > current.FirstLiteral ||
                (candidate.FirstLiteral == current.FirstLiteral &&
                 candidate.LiteralCharacters > current.LiteralCharacters))
                best[pattern] = candidate;
        }

        if (blocks == MaxWildcards + 1) return;

        for (var i = start; i < name.Length; i++)
        {
            for (var j = i + 1; j <= name.Length; j++)
            {
                var literal = Poe2Syntax.EscapeLiteral(name[i..j]);
                var separator = blocks == 0 ? "" : i - previousEnd == 1 ? "." : ".+";
                var nextLength = length + separator.Length + literal.Length;
                if (nextLength > maxLength) break;

                pieces.Add(separator);
                pieces.Add(literal);
                Walk(
                    name, j + 1, blocks + 1, nextLength, j,
                    blocks == 0 ? literal.Length : firstLiteral,
                    literalCharacters + literal.Length,
                    pieces, maxLength, best);
                pieces.RemoveRange(pieces.Count - 2, 2);
            }
        }
    }
}

using System.Text.RegularExpressions;
using PoeRegex.Core.Compilation;

namespace PoeRegex.Data.Build;

/// <summary>
/// Indexed evaluator for the matrix artifact. It produces exactly the same
/// option counts and flags as the exhaustive <see cref="CandidateReport.Generate"/>
/// path, but resolves candidate matches through a per-scope trigram index and
/// shares literal lookups across every pattern in that scope.
///
/// The index is a necessary-condition filter: a line can only contain a
/// candidate if it contains every trigram of the candidate's literal body, so
/// scanning the rarest trigram's postings and verifying with IndexOf/regex is
/// exact, never approximate.
/// </summary>
internal sealed class ScopeCorpus
{
    private readonly Dictionary<string, int[]> _byPattern;
    private const int LiteralCacheLimit = 1_000;
    private const int FragmentCacheLimit = 2_000;
    private const int RegexCacheLimit = 50;
    private const int CacheableMatchLimit = 1_000;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, int[]> _literalCache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, int[]> _fragmentCache =
        new(StringComparer.Ordinal);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Regex?> _regexCache =
        new(StringComparer.Ordinal);
    private readonly TrigramIndex _index;

    public ScopeCorpus(EngineCorpus corpus, string? scope, IReadOnlyList<CandidateReport.SourceLine> lines)
    {
        Corpus = corpus;
        Scope = scope;
        Lines = lines;
        _index = new TrigramIndex(lines.Select(x => x.Real).ToList());
        _byPattern = lines
            .Select((line, index) => (line.Pattern, index))
            .GroupBy(x => x.Pattern, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => x.index).ToArray(),
                StringComparer.Ordinal);
    }

    public EngineCorpus Corpus { get; }
    public string? Scope { get; }
    public IReadOnlyList<CandidateReport.SourceLine> Lines { get; }

    public static ScopeCorpus Build(EngineCorpus corpus, string? scope) =>
        new(corpus, scope, CandidateReport.ScopeLines(corpus, scope));

    public bool HasPattern(string pattern) => _byPattern.ContainsKey(pattern);

    public IReadOnlyList<string> Targets(string pattern) =>
        _byPattern.TryGetValue(pattern, out var indexes)
            ? indexes.Select(i => Lines[i].Real)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(x => x.Length)
                .ThenBy(x => x, StringComparer.Ordinal)
                .ToList()
            : [];

    /// <summary>
    /// Line indexes in this scope that the candidate matches. Literal bodies
    /// are cached independently of anchors. Wildcards use their longest
    /// literal run as the index hint and verify with the real regex.
    /// </summary>
    public int[] MatchLines(CandidateReport.Candidate candidate, Regex? compiled = null)
    {
        if (!candidate.Wildcard)
        {
            var (body, starts, ends) = SplitAnchors(candidate.Fragment);
            if (body.Length == 0) return [];
            var hits = LiteralHits(body);
            if (!starts && !ends) return hits;

            var filtered = new List<int>(hits.Length);
            foreach (var index in hits)
            {
                var text = Lines[index].Real;
                if (starts && !text.StartsWith(body, StringComparison.OrdinalIgnoreCase)) continue;
                if (ends && !text.EndsWith(body, StringComparison.OrdinalIgnoreCase)) continue;
                filtered.Add(index);
            }
            return filtered.ToArray();
        }

        if (_fragmentCache.TryGetValue(candidate.Fragment, out var cached)) return cached;

        var pieces = LiteralPieces(candidate.Fragment);
        var hint = pieces
            .Where(x => x.Length >= 3)
            .OrderBy(x => _index.PostingCount(x))
            .ThenByDescending(x => x.Length)
            .FirstOrDefault();
        IEnumerable<int> seed = hint is not null
            ? LiteralHits(hint)
            : Enumerable.Range(0, Lines.Count);

        var (startAnchor, endAnchor) = Anchors(candidate.Fragment);
        var regex = compiled ?? RegexFor(candidate.Fragment);
        var matches = new List<int>();
        if (regex is not null)
        {
            foreach (var index in seed)
            {
                var text = Lines[index].Real;
                if (pieces.Count > 0)
                {
                    if (startAnchor && !text.StartsWith(pieces[0], StringComparison.OrdinalIgnoreCase)) continue;
                    if (endAnchor && !text.EndsWith(pieces[^1], StringComparison.OrdinalIgnoreCase)) continue;
                    if (!PiecesInOrder(text, pieces)) continue;
                }
                try
                {
                    if (regex.IsMatch(text)) matches.Add(index);
                }
                catch (RegexMatchTimeoutException)
                {
                    // A runaway candidate is not proven to match; skip it.
                }
            }
        }

        var result = matches.ToArray();
        if (result.Length <= CacheableMatchLimit)
        {
            if (_fragmentCache.Count > FragmentCacheLimit) _fragmentCache.Clear();
            _fragmentCache[candidate.Fragment] = result;
        }
        return result;
    }

    private int[] LiteralHits(string body)
    {
        if (_literalCache.TryGetValue(body, out var cached)) return cached;
        var result = _index.Lookup(body);
        if (result.Length <= CacheableMatchLimit)
        {
            if (_literalCache.Count > LiteralCacheLimit) _literalCache.Clear();
            _literalCache[body] = result;
        }
        return result;
    }

    internal Regex? RegexFor(string fragment)
    {
        if (_regexCache.TryGetValue(fragment, out var cached)) return cached;
        Regex? regex;
        try
        {
            var options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
            try
            {
                regex = new Regex(
                    fragment,
                    options | RegexOptions.NonBacktracking,
                    TimeSpan.FromMilliseconds(250));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
            {
                regex = new Regex(fragment, options, TimeSpan.FromMilliseconds(250));
            }
        }
        catch (ArgumentException)
        {
            regex = null;
        }
        if (_regexCache.Count > RegexCacheLimit) _regexCache.Clear();
        _regexCache[fragment] = regex;
        return regex;
    }

    internal static (bool Starts, bool Ends) Anchors(string fragment) =>
        (fragment.StartsWith('^'), fragment.EndsWith('$'));

    internal static (string Body, bool Starts, bool Ends) SplitAnchors(string fragment)
    {
        var starts = fragment.StartsWith('^');
        var ends = fragment.EndsWith('$');
        var body = fragment;
        if (starts) body = body[1..];
        if (ends && body.Length > 0) body = body[..^1];
        return (body, starts, ends);
    }

    /// <summary>
    /// Literal pieces of a wildcard fragment, split on anchors and wildcard
    /// tokens. Used to choose the rarest index posting and to reject a line
    /// cheaply before running the regex.
    /// </summary>
    internal static List<string> LiteralPieces(string fragment)
    {
        var pieces = new List<string>();
        var current = new System.Text.StringBuilder();
        void Flush()
        {
            if (current.Length > 0)
            {
                pieces.Add(current.ToString());
                current.Clear();
            }
        }
        for (var i = 0; i < fragment.Length; i++)
        {
            var c = fragment[i];
            if (c == '\\' && i + 1 < fragment.Length)
            {
                current.Append(fragment[i + 1]);
                i++;
                continue;
            }
            if (c is '^' or '$' or '.' or '*' or '+' or '(' or ')' or '[' or ']' or '{' or '}' or '|' or '?')
            {
                Flush();
                continue;
            }
            current.Append(c);
        }
        Flush();
        return pieces;
    }

    internal static bool PiecesInOrder(string line, IReadOnlyList<string> pieces)
    {
        var position = 0;
        foreach (var piece in pieces)
        {
            var index = line.IndexOf(piece, position, StringComparison.OrdinalIgnoreCase);
            if (index < 0) return false;
            position = index + piece.Length;
        }
        return true;
    }

    internal static bool MatchesLiteral(string fragment, string line)
    {
        var (body, starts, ends) = SplitAnchors(fragment);
        if (body.Length == 0) return false;
        if (starts && !line.StartsWith(body, StringComparison.OrdinalIgnoreCase)) return false;
        if (ends && !line.EndsWith(body, StringComparison.OrdinalIgnoreCase)) return false;
        return line.IndexOf(body, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}

/// <summary>
/// Exact 3-gram inverted index over one scope's real lines.
/// </summary>
internal sealed class TrigramIndex
{
    private readonly IReadOnlyList<string> _lines;
    private readonly Dictionary<long, int[]> _postings;

    public TrigramIndex(IReadOnlyList<string> lines)
    {
        _lines = lines;
        var postings = new Dictionary<long, List<int>>();
        var seen = new HashSet<long>();
        for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            var text = lines[lineIndex];
            seen.Clear();
            for (var i = 0; i + 2 < text.Length; i++)
            {
                var key = Key(text[i], text[i + 1], text[i + 2]);
                if (!seen.Add(key)) continue;
                if (!postings.TryGetValue(key, out var list))
                {
                    list = [];
                    postings[key] = list;
                }
                list.Add(lineIndex);
            }
        }
        _postings = postings.ToDictionary(x => x.Key, x => x.Value.ToArray());
    }

    /// <summary>Size of the rarest trigram posting list, or 0 when absent.</summary>
    public int PostingCount(string literal)
    {
        if (literal.Length < 3) return _lines.Count;
        var best = int.MaxValue;
        for (var i = 0; i + 2 < literal.Length; i++)
        {
            var key = Key(literal[i], literal[i + 1], literal[i + 2]);
            if (!_postings.TryGetValue(key, out var list)) return 0;
            if (list.Length < best) best = list.Length;
        }
        return best == int.MaxValue ? 0 : best;
    }

    /// <summary>
    /// Distinct line indexes containing the literal. Picks the rarest trigram
    /// as the posting list to scan, then verifies every candidate line.
    /// </summary>
    public int[] Lookup(string literal)
    {
        if (literal.Length < 3)
        {
            var all = new List<int>();
            for (var i = 0; i < _lines.Count; i++)
            {
                if (_lines[i].Contains(literal, StringComparison.OrdinalIgnoreCase))
                    all.Add(i);
            }
            return all.ToArray();
        }

        int[]? best = null;
        for (var i = 0; i + 2 < literal.Length; i++)
        {
            var key = Key(literal[i], literal[i + 1], literal[i + 2]);
            if (!_postings.TryGetValue(key, out var list)) return [];
            if (best is null || list.Length < best.Length) best = list;
        }
        if (best is null) return [];

        var matches = new List<int>(best.Length);
        foreach (var lineIndex in best)
        {
            if (_lines[lineIndex].Contains(literal, StringComparison.OrdinalIgnoreCase))
                matches.Add(lineIndex);
        }
        matches.Sort();
        return matches.ToArray();
    }

    private static long Key(char a, char b, char c) =>
        ((long)char.ToLowerInvariant(a) << 32) |
        ((long)char.ToLowerInvariant(b) << 16) |
        char.ToLowerInvariant(c);
}

/// <summary>
/// Top-N result for one pattern in one scope. <see cref="Floor"/> is the
/// shortest target-matching candidate regardless of collisions; it is
/// diagnostic only and never a shipping proposal.
/// </summary>
internal sealed record TopOptions(
    CandidateReport.Option? Floor,
    IReadOnlyList<CandidateReport.Option> Strict,
    IReadOnlyList<CandidateReport.Option> Minion,
    IReadOnlyList<CandidateReport.Option> Line,
    IReadOnlyList<CandidateReport.Option> Relaxed);

internal static class CandidateEvaluator
{
    /// <summary>
    /// Exact top-N evaluation. Candidates are visited in ascending length, so
    /// once every profile has N entries and the next candidate is longer than
    /// every profile's Nth entry, no remaining candidate can change the
    /// result. Profiles that never fill force a full scan, which keeps an
    /// empty list an honest "none".
    /// </summary>
    public static TopOptions EvaluateTop(
        ScopeCorpus scope, string pattern, string? current, int maxLength, int top = 3)
    {
        var targets = scope.Targets(pattern);
        var candidates = CandidateReport.EnumerateCandidates(targets, maxLength, current);

        var strict = new List<CandidateReport.Option>();
        var minion = new List<CandidateReport.Option>();
        var line = new List<CandidateReport.Option>();
        var relaxed = new List<CandidateReport.Option>();
        CandidateReport.Option? floor = null;
        var evaluated = new HashSet<string>(StringComparer.Ordinal);

        void Consider(CandidateReport.Candidate candidate)
        {
            if (!evaluated.Add(candidate.Fragment)) return;
            if (!candidate.Plausible) return;

            var option = BuildOption(scope, pattern, targets, candidate);
            if (option is null) return;

            if (floor is null || Compare(option, floor) < 0) floor = option;

            var canReachProfiles = option.ForeignLines == 0 || option.ForeignOtherLines == 0;
            if (option.HasWildcard && canReachProfiles)
                CandidateReport.EvaluateCross(scope.Corpus, scope.Scope, option);

            if (option.ForeignLines == 0)
            {
                AddTop(line, option, top);
                if (!option.HasCross) AddTop(strict, option, top);
            }
            if (option.ForeignOtherLines == 0)
            {
                AddTop(relaxed, option, top);
                if (!option.HasCross) AddTop(minion, option, top);
            }
        }

        // The shipped fragment is evaluated first so a long but valid current
        // entry can never disappear behind the length cap.
        var currentCandidate = candidates.FirstOrDefault(x =>
            string.Equals(x.Fragment, current, StringComparison.Ordinal));
        if (currentCandidate is not null) Consider(currentCandidate);

        foreach (var candidate in candidates)
        {
            if (AllFull(strict, minion, line, relaxed, top) &&
                candidate.Fragment.Length > MaxThird(strict, minion, line, relaxed))
                break;
            Consider(candidate);
        }

        return new TopOptions(floor, strict, minion, line, relaxed);
    }

    private static CandidateReport.Option? BuildOption(
        ScopeCorpus scope,
        string pattern,
        IReadOnlyList<string> targets,
        CandidateReport.Candidate candidate)
    {
        if (targets.Count == 0) return null;

        Regex? regex = null;
        if (candidate.Wildcard)
        {
            regex = scope.RegexFor(candidate.Fragment);
            if (regex is null) return null;
            foreach (var target in targets)
            {
                try
                {
                    if (!regex.IsMatch(target)) return null;
                }
                catch (RegexMatchTimeoutException)
                {
                    return null;
                }
            }
        }
        else
        {
            foreach (var target in targets)
                if (!ScopeCorpus.MatchesLiteral(candidate.Fragment, target)) return null;
        }

        var matched = scope.MatchLines(candidate, regex);
        var realSet = new HashSet<string>(StringComparer.Ordinal);
        var patternSet = new HashSet<string>(StringComparer.Ordinal);
        var examples = new List<string>(2);
        var foreignLines = 0;
        var foreignMinion = 0;

        foreach (var index in matched)
        {
            var source = scope.Lines[index];
            if (string.Equals(source.Pattern, pattern, StringComparison.Ordinal)) continue;
            foreignLines++;
            if (CandidateReport.IsMinionAlliesLine(source.Real)) foreignMinion++;
            patternSet.Add(source.Pattern);
            if (realSet.Add(source.Real) && examples.Count < 2) examples.Add(source.Real);
        }

        return new CandidateReport.Option
        {
            Fragment = candidate.Fragment,
            Length = candidate.Fragment.Length,
            HasWildcard = candidate.Wildcard,
            ForeignPatterns = patternSet.Count,
            ForeignLines = realSet.Count,
            ForeignMinionLines = foreignMinion,
            ForeignOtherLines = foreignLines - foreignMinion,
            ForeignExamples = examples,
            Plausible = true,
        };
    }

    private static void AddTop(
        List<CandidateReport.Option> list, CandidateReport.Option option, int top)
    {
        if (list.Count == top && Compare(option, list[^1]) >= 0) return;
        var index = list.FindIndex(x => Compare(option, x) < 0);
        if (index < 0) list.Add(option);
        else list.Insert(index, option);
        if (list.Count > top) list.RemoveAt(top);
    }

    private static bool AllFull(
        List<CandidateReport.Option> strict,
        List<CandidateReport.Option> minion,
        List<CandidateReport.Option> line,
        List<CandidateReport.Option> relaxed,
        int top) =>
        strict.Count >= top && minion.Count >= top &&
        line.Count >= top && relaxed.Count >= top;

    private static int MaxThird(
        List<CandidateReport.Option> strict,
        List<CandidateReport.Option> minion,
        List<CandidateReport.Option> line,
        List<CandidateReport.Option> relaxed) =>
        Math.Max(
            Math.Max(strict[^1].Length, minion[^1].Length),
            Math.Max(line[^1].Length, relaxed[^1].Length));

    private static int Compare(CandidateReport.Option a, CandidateReport.Option b)
    {
        var byLength = a.Length.CompareTo(b.Length);
        return byLength != 0
            ? byLength
            : StringComparer.Ordinal.Compare(a.Fragment, b.Fragment);
    }
}

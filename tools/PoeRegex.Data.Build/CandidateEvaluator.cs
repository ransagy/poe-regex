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
        if (!NeedsRegex(candidate.Fragment))
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
    internal static bool NeedsRegex(string fragment) =>
        fragment.Any(c => c is '\\' or '.' or '*' or '+' or '?' or '(' or ')' or
            '[' or ']' or '{' or '}' or '^' or '$' or '|');

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
                var escaped = fragment[i + 1];
                if (escaped is 'd' or 'D' or 'w' or 'W' or 's' or 'S')
                {
                    // A character-class token, not a literal letter. Split the
                    // run and let the regex verification decide the match.
                    Flush();
                    i++;
                    continue;
                }
                current.Append(escaped);
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
    IReadOnlyList<CandidateReport.Option> Minion);

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
        ScopeCorpus scope, string pattern, string? current, int maxLength, int top = 3,
        bool includeTwoWildcards = true)
    {
        var targets = scope.Targets(pattern);
        var candidates = CandidateReport.EnumerateCandidates(
            targets, maxLength, current, includeTwoWildcards);

        var strict = new List<CandidateReport.Option>();
        var minion = new List<CandidateReport.Option>();
        CandidateReport.Option? floor = null;
        var evaluated = new HashSet<string>(StringComparer.Ordinal);

        void Consider(CandidateReport.Candidate candidate)
        {
            if (!evaluated.Add(candidate.Fragment)) return;
            if (!candidate.Plausible) return;

            var option = BuildOption(scope, pattern, targets, candidate);
            if (option is null) return;

            if (floor is null || Compare(option, floor) < 0) floor = option;

            // Quoted terms are line-local: the same-line foreign count is the
            // shipping safety criterion. There is no cross-line dot phase.
            // Keep one representative per policy variant (anchored, quoted-only,
            // digit guard, wildcard) so the review can compare rulesets.
            if (option.ForeignLines == 0) AddVariant(strict, option);
            if (option.ForeignOtherLines == 0) AddVariant(minion, option);
        }

        foreach (var candidate in candidates)
        {
            if (strict.Count >= top && minion.Count >= top &&
                candidate.Fragment.Length > Math.Max(
                    ThirdLength(strict, top), ThirdLength(minion, top)))
                break;
            Consider(candidate);
        }

        return new TopOptions(
            floor,
            Selected(strict, top),
            Selected(minion, top));
    }

    /// <summary>Evaluate one known/reference fragment without adding it to a profile.</summary>
    internal static CandidateReport.Option? EvaluateFragment(
        ScopeCorpus scope, string pattern, string fragment)
    {
        if (string.IsNullOrWhiteSpace(fragment)) return null;
        var targets = scope.Targets(pattern);
        if (targets.Count == 0) return null;
        var candidate = new CandidateReport.Candidate(
            fragment, fragment.Contains('.'), "", true);
        return BuildOption(scope, pattern, targets, candidate);
    }

    private static List<CandidateReport.Option> Selected(
        List<CandidateReport.Option> list, int top) =>
        list.OrderBy(x => x.Length)
            .ThenBy(x => x.Fragment, StringComparer.Ordinal)
            .Take(top)
            .ToList();

    // Only the anchor policy is a genuine ruleset fork. Quoted-only and
    // wildcard variants are not separate policies; the shorter/stronger one
    // within a group wins, and the known reference is displayed separately.
    private static string VariantKey(CandidateReport.Option option) =>
        (option.Fragment.StartsWith('^') || option.Fragment.EndsWith('$')).ToString();

    private static void AddVariant(
        List<CandidateReport.Option> list, CandidateReport.Option option)
    {
        if (option.Fragment.Contains(@"\d", StringComparison.Ordinal) &&
            option.GuardLetters < 3)
            return;
        var key = VariantKey(option);
        var index = list.FindIndex(x => VariantKey(x) == key);
        if (index < 0)
        {
            list.Add(option);
            return;
        }
        if (Better(option, list[index])) list[index] = option;
    }

    private static bool Better(
        CandidateReport.Option a, CandidateReport.Option b)
    {
        if (a.Length != b.Length) return a.Length < b.Length;
        if (a.ForeignOtherLines != b.ForeignOtherLines)
            return a.ForeignOtherLines < b.ForeignOtherLines;
        if (a.ForeignMinionLines != b.ForeignMinionLines)
            return a.ForeignMinionLines < b.ForeignMinionLines;
        // Quoted-only is a restriction, not a benefit: prefer the no-space
        // form at equal length before comparing guard strength.
        if (a.TermSplits != b.TermSplits) return !a.TermSplits;
        if (a.GuardLetters != b.GuardLetters) return a.GuardLetters > b.GuardLetters;
        return StringComparer.Ordinal.Compare(a.Fragment, b.Fragment) < 0;
    }

    private static CandidateReport.Option? BuildOption(
        ScopeCorpus scope,
        string pattern,
        IReadOnlyList<string> targets,
        CandidateReport.Candidate candidate)
    {
        if (targets.Count == 0) return null;

        Regex? regex = null;
        if (ScopeCorpus.NeedsRegex(candidate.Fragment))
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
        var foreignKeys = new List<string>();
        var foreignLines = 0;
        var foreignMinion = 0;

        foreach (var index in matched)
        {
            var source = scope.Lines[index];
            if (string.Equals(source.Pattern, pattern, StringComparison.Ordinal)) continue;
            foreignLines++;
            if (CandidateReport.IsMinionAlliesLine(source.Real)) foreignMinion++;
            patternSet.Add(source.Pattern);
            foreignKeys.Add(source.Pattern + "\0" + source.Real);
            if (realSet.Add(source.Real) && examples.Count < 2) examples.Add(source.Real);
        }

        return new CandidateReport.Option
        {
            Fragment = candidate.Fragment,
            Length = candidate.Fragment.Length,
            HasWildcard = candidate.Fragment.Contains('.'),
            ForeignPatterns = patternSet.Count,
            ForeignLines = realSet.Count,
            ForeignMinionLines = foreignMinion,
            ForeignOtherLines = foreignLines - foreignMinion,
            ForeignExamples = examples,
            MatchSetHash = CandidateReport.StableHash(foreignKeys),
            Plausible = true,
        };
    }

    private static int ThirdLength(
        List<CandidateReport.Option> list, int top) =>
        list.OrderBy(x => x.Length)
            .ThenBy(x => x.Fragment, StringComparer.Ordinal)
            .ElementAt(Math.Min(top - 1, list.Count - 1))
            .Length;

    private static int Compare(CandidateReport.Option a, CandidateReport.Option b)
    {
        var byLength = a.Length.CompareTo(b.Length);
        return byLength != 0
            ? byLength
            : StringComparer.Ordinal.Compare(a.Fragment, b.Fragment);
    }
}

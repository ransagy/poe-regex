using PoeRegex.Core.Search;

namespace PoeRegex.Core.Compilation;

/// <summary>
/// Finds the shortest fragment that selects one stat text and nothing else in
/// a scope, using regex selectors when they genuinely shorten the string.
///
/// Candidate space (per normalized line pattern):
///  - every contiguous substring (the shortest unique one comes from the
///    suffix index; prefixes/suffixes are tried with the per-line anchors),
///  - two literal pieces bridged by one wildcard, with optional <c>^</c>/<c>$</c>,
///  - placeholders rendered as <c>\d</c>, <c>\d+</c> or <c>.</c>.
///
/// There is no readability floor: the shortest candidate that is unique and
/// safe on the real copy text of the scope wins. The oracle, not the shape,
/// is the guard; every candidate is checked for recall (it matches every real
/// line of the stat) and safety (it matches no line of another stat). Ties
/// prefer more anchors, then a stricter wildcard, then more letters.
/// </summary>
/// <summary>
/// Safety constraints for a shippable fragment. The default is the guaranteed
/// policy: the fragment must be line-anchored and keep either a digit
/// selector or a line-start 4+ letter stat word, so it cannot be an
/// arbitrary letter shape that matches item names, headers, or flavor text.
/// The unconditional whole-line fallback is exempt (some lines start with a
/// short word, e.g. "Bow Attacks fire an additional Arrow"). Wildcards are
/// allowed (up to two bridges) because the full line corpus and fixtures
/// validate the actual candidates.
/// </summary>
public sealed record FragmentPolicy(
    bool RequireGuard = true,
    bool RequireAnchor = true,
    int MaxBridges = 2)
{
    public static readonly FragmentPolicy Guaranteed = new();

    /// <summary>Diagnostic-only policy: no shape constraints (not shipped).</summary>
    public static readonly FragmentPolicy Absolute = new(false, false, 2);
}

public sealed class FragmentSelector
{
    private const int MinWildcardLength = 3;
    private const int MaxSearchLength = 48;
    private static readonly string[] TwoWildcards = [".+", ".*"];
    private static readonly string[] ThreeWildcards = [".+", ".*", "."];

    private readonly EngineCorpus _corpus;
    private readonly ICompilerStrategy _strategy;
    private readonly bool _usePrecomputed;
    private readonly FragmentPolicy _policy;
    private readonly Dictionary<(string Scope, string Pattern), string?> _cache = new();
    private Func<string, bool>? _extraSafety;
    private readonly Dictionary<string, ScopeData> _scopes = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Scope, string Target, string Hint), List<string>> _foreignCache = new();

    public FragmentSelector(
        EngineCorpus corpus, ICompilerStrategy strategy, bool usePrecomputed = true,
        FragmentPolicy? policy = null)
    {
        _corpus = corpus;
        _strategy = strategy;
        _usePrecomputed = usePrecomputed;
        _policy = policy ?? FragmentPolicy.Guaranteed;
    }

    private sealed class ScopeData
    {
        public required Dictionary<string, List<string>> ByPattern { get; init; }
        public required List<(string Pattern, string Real)> All { get; init; }
    }

    /// <summary>
    /// The shortest safe fragment for <paramref name="pattern"/> in the scope
    /// (<paramref name="itemClass"/> null means the union of every class).
    /// Returns null only when the pattern has no real line in the scope.
    /// </summary>
    public string? Select(string pattern, string? itemClass, Func<string, bool>? extraSafety = null)
    {
        var key = (itemClass ?? "\0", pattern);
        if (_cache.TryGetValue(key, out var cached)) return cached;
        if (_usePrecomputed && _corpus.TryPrecomputedFragment(pattern, itemClass, out var precomputed))
        {
            _cache[key] = precomputed;
            return precomputed;
        }
        // Keep the extra check for the whole nested search (the class scope
        // recurses into the global scope); the next top-level call resets it.
        _extraSafety = extraSafety;
        var result = SelectCore(pattern, itemClass);
        _cache[key] = result;
        return result;
    }

    private string? SelectCore(string pattern, string? itemClass)
    {
        var scope = GetScope(itemClass);
        if (!scope.ByPattern.TryGetValue(pattern, out var targetList) || targetList.Count == 0) return null;
        var others = scope.All
            .Where(x => !string.Equals(x.Pattern, pattern, StringComparison.Ordinal))
            .Select(x => x.Real)
            .ToList();

        // Most stats have a very short plain anchor; checking that band first
        // avoids walking every long prefix/suffix for the few that do not.
        const int ShortSearch = 11;
        var plain = BestPlain(pattern, itemClass, targetList, others, maxLength: ShortSearch);
        if (plain is not null && plain.Length <= MinWildcardLength) return plain;

        // A global-safe fragment is safe on every class and matches every
        // class's copy of the pattern; use it as a fallback for class scopes.
        string? best = plain;
        if (itemClass is not null)
        {
            var global = Select(pattern, null, _extraSafety);
            if (global is not null && (best is null || global.Length < best.Length)) best = global;
        }
        if (best is not null && best.Length <= MinWildcardLength) return best;

        var bound = best?.Length ?? ShortSearch;
        var wildcard = FindWildcard(pattern, itemClass, targetList, others, bound);
        if (wildcard is not null && (best is null || wildcard.Length < best.Length)) best = wildcard;
        if (_policy.MaxBridges >= 2)
        {
            var two = FindWildcard2(pattern, itemClass, targetList, others, best?.Length ?? ShortSearch);
            if (two is not null && (best is null || two.Length < best.Length)) best = two;
        }
        // Every search covered all candidates below ShortSearch.
        if (best is not null && best.Length < ShortSearch) return best;

        // No short answer: repeat the plain search without a length cap, then
        // let the wildcard search use that new upper bound.
        var fullPlain = BestPlain(pattern, itemClass, targetList, others);
        if (fullPlain is not null && (best is null || fullPlain.Length < best.Length)) best = fullPlain;
        if (best is not null && best.Length <= MinWildcardLength) return best;

        var fullBound = best?.Length ?? MaxSearchLength + 1;
        var fullWildcard = FindWildcard(pattern, itemClass, targetList, others, Math.Min(fullBound, MaxSearchLength + 1));
        if (fullWildcard is not null && (best is null || fullWildcard.Length < best.Length)) best = fullWildcard;
        if (_policy.MaxBridges >= 2)
        {
            var two = FindWildcard2(pattern, itemClass, targetList, others,
                Math.Min(best?.Length ?? MaxSearchLength + 1, MaxSearchLength + 1));
            if (two is not null && (best is null || two.Length < best.Length)) best = two;
        }

        if (best is not null) return best;

        // The whole line with per-line anchors is always safe and recalls every
        // real line; it is the unconditional fallback (never expected in data).
        var whole = WholeLine(pattern);
        if (whole is not null && Validate(whole, itemClass, pattern, targetList, others)) return whole;
        return null;
    }

    private ScopeData GetScope(string? itemClass)
    {
        var key = itemClass ?? "\0";
        if (_scopes.TryGetValue(key, out var cached)) return cached;
        var all = itemClass is null
            ? _corpus.RealLinesGlobal().ToList()
            : _corpus.RealLinesForClass(itemClass).ToList();
        var byPattern = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (pattern, real) in all)
        {
            if (!byPattern.TryGetValue(pattern, out var list))
            {
                list = [];
                byPattern[pattern] = list;
            }
            list.Add(real);
        }
        var data = new ScopeData { ByPattern = byPattern, All = all };
        _scopes[key] = data;
        return data;
    }

    /// <summary>Shortest safe contiguous fragment (substring, prefix or suffix).</summary>
    private string? BestPlain(string pattern, string? itemClass, List<string> targets, List<string> others, int maxLength = int.MaxValue)
    {
        var candidates = new HashSet<string>(StringComparer.Ordinal);
        var unique = _corpus.ShortestUniqueSubstringForPattern(pattern, itemClass, avoidPlaceholder: false);
        if (unique is not null)
        {
            var at = pattern.IndexOf(unique, StringComparison.Ordinal);
            if (at >= 0) AddRenderings(candidates, pattern, at, at + unique.Length);
        }
        var uniqueNoPlaceholder = _corpus.ShortestUniqueSubstringForPattern(pattern, itemClass, avoidPlaceholder: true);
        if (uniqueNoPlaceholder is not null)
            candidates.Add(_strategy.EscapeLiteral(uniqueNoPlaceholder));

        // Anchored prefixes and suffixes; no shape floor — the oracle decides.
        for (var end = 1; end <= pattern.Length; end++)
            AddRenderings(candidates, pattern, 0, end, prefixAnchor: true);
        for (var start = 0; start < pattern.Length; start++)
            AddRenderings(candidates, pattern, start, pattern.Length, suffixAnchor: true);

        // The whole line is the long-but-always-safe fallback.
        AddRenderings(candidates, pattern, 0, pattern.Length, prefixAnchor: true);
        AddRenderings(candidates, pattern, 0, pattern.Length, suffixAnchor: true);
        AddRenderings(candidates, pattern, 0, pattern.Length, prefixAnchor: true, suffixAnchor: true);

        foreach (var candidate in candidates
                     .Where(c => c.Length < maxLength && PassesPolicy(c))
                     .OrderBy(c => c.Length)
                     .ThenBy(c => AnchorCount(c), Comparer<int>.Create((a, b) => b.CompareTo(a)))
                     .ThenBy(c => c, StringComparer.Ordinal))
        {
            if (Validate(candidate, itemClass, pattern, targets, others)) return candidate;
        }
        return null;
    }

    /// <summary>
    /// Adds the placeholder renderings of one literal range to the set, with
    /// the requested per-line anchors. The range must be a contiguous piece of
    /// the normalized pattern.
    /// </summary>
    private void AddRenderings(
        HashSet<string> into, string pattern, int start, int end,
        bool prefixAnchor = false, bool suffixAnchor = false)
    {
        if (start < 0 || end <= start || end > pattern.Length) return;
        foreach (var rendered in Renderings(pattern, start, end))
        {
            into.Add((prefixAnchor ? "^" : "") + rendered + (suffixAnchor ? "$" : ""));
        }
    }

    /// <summary>All placeholder renderings of a range, shortest first.</summary>
    private List<string> Renderings(string pattern, int start, int end)
    {
        var parts = new List<string>();
        var literalStart = start;
        for (var i = start; i < end; i++)
        {
            if (pattern[i] != SuffixIndex.DigitPlaceholder) continue;
            if (i > literalStart) parts.Add(_strategy.EscapeLiteral(pattern[literalStart..i]));
            parts.Add("\0"); // placeholder marker
            literalStart = i + 1;
        }
        if (literalStart < end) parts.Add(_strategy.EscapeLiteral(pattern[literalStart..end]));

        var result = new List<string> { "" };
        var placeholders = parts.Count(p => p == "\0");
        if (placeholders > 4)
        {
            // Pathological line: keep only the uniform renderings.
            result.Clear();
            result.Add(string.Concat(parts.Select(p => p == "\0" ? "." : p)));
            result.Add(string.Concat(parts.Select(p => p == "\0" ? @"\d" : p)));
            result.Add(string.Concat(parts.Select(p => p == "\0" ? @"\d+" : p)));
            return result.Distinct().OrderBy(s => s.Length).ThenBy(s => s, StringComparer.Ordinal).ToList();
        }

        foreach (var part in parts)
        {
            if (part != "\0")
            {
                for (var i = 0; i < result.Count; i++) result[i] += part;
                continue;
            }
            var next = new List<string>(result.Count * 3);
            foreach (var prefix in result)
            {
                next.Add(prefix + ".");
                next.Add(prefix + @"\d");
                next.Add(prefix + @"\d+");
            }
            result = next;
        }
        return result
            .Distinct(StringComparer.Ordinal)
            .OrderBy(s => s.Length)
            .ThenBy(s => s, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Searches one-wildcard fragments by increasing rendered length. Only
    /// lengths below <paramref name="bound"/> are considered; the first safe
    /// candidate at the smallest length wins.
    /// </summary>
    private string? FindWildcard(string pattern, string? itemClass, List<string> targets, List<string> others, int bound)
    {
        bound = Math.Min(bound, MaxSearchLength + 1);
        if (bound <= MinWildcardLength) return null;
        // Generate/validate in growing caps: most stats find their minimum in
        // a handful of characters, and the no-floor candidate space is large.
        var cap = Math.Min(bound, 10);
        while (true)
        {
            var found = FindWildcardUpTo(pattern, itemClass, targets, others, cap);
            if (found is not null) return found;
            if (cap >= bound) return null;
            cap = Math.Min(bound, cap + 6);
        }
    }

    private string? FindWildcardUpTo(string pattern, string? itemClass, List<string> targets, List<string> others, int bound)
    {
        var n = pattern.Length;
        bound = Math.Min(bound, MaxSearchLength + 1);
        if (bound <= MinWildcardLength) return null;

        var buckets = new HashSet<string>?[bound];
        var renderCache = new List<string>?[n + 1, n + 1];

        List<string> Piece(int a, int b)
        {
            if (renderCache[a, b] is { } cached) return cached;
            var rendered = Renderings(pattern, a, b);
            renderCache[a, b] = rendered;
            return rendered;
        }

        void Add(string candidate)
        {
            if (candidate.Length >= bound || !PassesPolicy(candidate)) return;
            var bucket = buckets[candidate.Length] ??= new HashSet<string>(StringComparer.Ordinal);
            bucket.Add(candidate);
        }

        for (var i = 0; i < n - 1; i++)
            for (var k = i + 1; k < n; k++)
            {
                var aRenderings = Piece(i, k);
                var startVariants = i == 0 ? 2 : 1;
                for (var l = k + 1; l < n; l++)
                {
                    var gap = l - k;
                    var wildcards = TwoWildcards;
                    var wildcardCount = 2;
                    if (gap == 1 && (pattern[k] == SuffixIndex.DigitPlaceholder || _strategy.EscapeLiteral(pattern[k].ToString()).Length > 1))
                    {
                        wildcards = ThreeWildcards;
                        wildcardCount = 3;
                    }
                    for (var j = l + 1; j <= n; j++)
                    {
                        var minPieceLength = (k - i) + (j - l);
                        // Wildcard is at least one char; anchors are optional.
                        if (minPieceLength + 1 >= bound) continue;
                        var endVariants = j == n ? 2 : 1;
                        var bRenderings = Piece(l, j);
                        foreach (var aText in aRenderings)
                            foreach (var bText in bRenderings)
                            {
                                var baseLength = aText.Length + bText.Length;
                                for (var wi = 0; wi < wildcardCount; wi++)
                                {
                                    var wildcard = wildcards[wi];
                                    var length = baseLength + wildcard.Length;
                                    for (var sv = 0; sv < startVariants; sv++)
                                        for (var ev = 0; ev < endVariants; ev++)
                                        {
                                            var total = length + sv + ev;
                                            if (total >= bound) continue;
                                            Add((sv == 1 ? "^" : "") + aText + wildcard + bText + (ev == 1 ? "$" : ""));
                                        }
                                }
                            }
                    }
                }
            }

        for (var length = MinWildcardLength; length < bound; length++)
        {
            var bucket = buckets[length];
            if (bucket is null || bucket.Count == 0) continue;
            foreach (var candidate in bucket
                         .OrderByDescending(AnchorCount)
                         .ThenBy(WildcardRank)
                         .ThenByDescending(LetterCount)
                         .ThenBy(c => c, StringComparer.Ordinal))
            {
                if (Validate(candidate, itemClass, pattern, targets, others)) return candidate;
            }
        }
        return null;
    }

    private string? FindWildcard2(string pattern, string? itemClass, List<string> targets, List<string> others, int bound)
    {
        bound = Math.Min(bound, MaxSearchLength + 1);
        if (bound <= MinWildcardLength + 3) return null;
        var cap = Math.Min(bound, 12);
        while (true)
        {
            var found = FindWildcard2UpTo(pattern, itemClass, targets, others, cap);
            if (found is not null) return found;
            if (cap >= bound) return null;
            cap = Math.Min(bound, cap + 8);
        }
    }

    private string? FindWildcard2UpTo(string pattern, string? itemClass, List<string> targets, List<string> others, int bound)
    {
        var n = pattern.Length;
        bound = Math.Min(bound, MaxSearchLength + 1);
        if (bound <= MinWildcardLength + 3) return null;
        var buckets = new HashSet<string>?[bound];
        var renderCache = new List<string>?[n + 1, n + 1];
        List<string> Piece(int a, int b)
        {
            if (renderCache[a, b] is { } cached) return cached;
            var rendered = Renderings(pattern, a, b);
            renderCache[a, b] = rendered;
            return rendered;
        }
        void Add(string candidate)
        {
            if (candidate.Length >= bound || !PassesPolicy(candidate)) return;
            var bucket = buckets[candidate.Length] ??= new HashSet<string>(StringComparer.Ordinal);
            bucket.Add(candidate);
        }

        for (var i = 0; i < n - 5; i++)
            for (var k = i + 1; k < n - 4; k++)
            {
                var aRenderings = Piece(i, k);
                for (var l = k + 1; l < n - 3; l++)
                    for (var m = l + 1; m < n - 2; m++)
                    {
                        var bRenderings = Piece(l, m);
                        for (var r = m + 1; r < n - 1; r++)
                            for (var j = r + 1; j <= n; j++)
                            {
                                if ((k - i) + (m - l) + (j - r) + 2 >= bound) continue;
                                var cRenderings = Piece(r, j);
                                var startCount = i == 0 ? 2 : 1;
                                var endCount = j == n ? 2 : 1;
                                var w1s = TwoWildcards;
                                var w2s = TwoWildcards;
                                var gap1 = l - k;
                                var gap2 = j - r;
                                if (gap1 == 1 && (pattern[k] == SuffixIndex.DigitPlaceholder || _strategy.EscapeLiteral(pattern[k].ToString()).Length > 1))
                                    w1s = ThreeWildcards;
                                if (gap2 == 1 && (pattern[r] == SuffixIndex.DigitPlaceholder || _strategy.EscapeLiteral(pattern[r].ToString()).Length > 1))
                                    w2s = ThreeWildcards;
                                foreach (var aText in aRenderings)
                                    foreach (var bText in bRenderings)
                                        foreach (var cText in cRenderings)
                                            foreach (var w1 in w1s)
                                                foreach (var w2 in w2s)
                                                    for (var sv = 0; sv < startCount; sv++)
                                                        for (var ev = 0; ev < endCount; ev++)
                                                        {
                                                            var total = aText.Length + bText.Length + cText.Length
                                                                + w1.Length + w2.Length + sv + ev;
                                                            if (total >= bound) continue;
                                                            Add((sv == 1 ? "^" : "") + aText + w1 + bText + w2 + cText + (ev == 1 ? "$" : ""));
                                                        }
                            }
                    }
            }

        for (var length = MinWildcardLength + 3; length < bound; length++)
        {
            var bucket = buckets[length];
            if (bucket is null || bucket.Count == 0) continue;
            foreach (var candidate in bucket
                         .OrderByDescending(AnchorCount)
                         .ThenBy(WildcardRank)
                         .ThenByDescending(LetterCount)
                         .ThenBy(c => c, StringComparer.Ordinal))
            {
                if (Validate(candidate, itemClass, pattern, targets, others)) return candidate;
            }
        }
        return null;
    }

    /// <summary>
    /// True when a candidate satisfies the shipped safety policy: at least one
    /// line anchor, and a digit selector or a line-start 4+ letter stat word.
    /// </summary>
    private static string FirstLetters(string text, int start)
    {
        var i = start;
        while (i < text.Length && char.IsAsciiLetter(text[i])) i++;
        return text[start..i];
    }

    private bool PassesPolicy(string candidate)
    {
        if (!_policy.RequireGuard && !_policy.RequireAnchor) return true;
        var anchor = candidate.StartsWith('^') || candidate.EndsWith('$');
        if (_policy.RequireAnchor && !anchor) return false;
        if (!_policy.RequireGuard) return true;
        if (candidate.Contains(@"\d", StringComparison.Ordinal)) return true;

        // Word-only guards are line-start anchored and must begin with a full
        // 4+ letter stat word (e.g. ^Allies…). A mid-word 4-letter run such as
        // "acks"/"nces" is not enough: it is exactly how letter-shape
        // fragments leaked into names and flavor text before the fixtures.
        if (!candidate.StartsWith('^')) return false;
        var run = 0;
        for (var i = 1; i < candidate.Length; i++)
        {
            var c = candidate[i];
            if (char.IsAsciiLetter(c))
            {
                run++;
                if (run >= 4) return true;
                continue;
            }
            return false;
        }
        return false;
    }

    /// <summary>Whole line with per-line anchors; guaranteed recall when the scope has the pattern.</summary>
    private string WholeLine(string pattern)
    {
        var sb = new System.Text.StringBuilder(pattern.Length + 8);
        sb.Append('^');
        var literalStart = 0;
        for (var i = 0; i < pattern.Length; i++)
        {
            if (pattern[i] != SuffixIndex.DigitPlaceholder) continue;
            if (i > literalStart) sb.Append(_strategy.EscapeLiteral(pattern[literalStart..i]));
            sb.Append(@"\d+");
            literalStart = i + 1;
        }
        if (literalStart < pattern.Length) sb.Append(_strategy.EscapeLiteral(pattern[literalStart..]));
        sb.Append('$');
        return sb.ToString();
    }

    private bool Validate(string candidate, string? itemClass, string pattern, List<string> targets, List<string> others)
    {
        // Word-only guards must use the complete first stat word of the
        // target line (^Allies…), not a truncated 4-letter prefix (^Alli…).
        var wholeLine = string.Equals(candidate, WholeLine(pattern), StringComparison.Ordinal);
        if (!wholeLine && !candidate.Contains(@"\d") && candidate.StartsWith('^'))
        {
            var prefix = FirstLetters(candidate, 1);
            if (prefix.Length < 4) return false;
            var complete = targets.Any(line =>
                string.Equals(FirstLetters(line, 0), prefix, StringComparison.OrdinalIgnoreCase));
            if (!complete) return false;
        }

        var matcher = FragmentMatcher.Parse(candidate);
        // Recall first: candidates that miss one of the stat's own lines are
        // rejected after a handful of matches instead of scanning the scope.
        foreach (var line in targets)
            if (!matcher.IsMatch(line)) return false;

        // A required literal in the candidate can only match patterns that
        // contain it; scan that subset instead of every foreign line.
        var hint = LiteralHint(candidate);
        var foreign = hint is null ? others : ForeignLines(itemClass, pattern, hint, others);
        foreach (var line in foreign)
            if (matcher.IsMatch(line)) return false;
        return _extraSafety is null || _extraSafety(candidate);
    }

    /// <summary>
    /// Foreign real lines that can possibly match a candidate containing the
    /// given literal (case-insensitive). Falls back to the full list when the
    /// subset is not smaller.
    /// </summary>
    private List<string> ForeignLines(string? itemClass, string targetPattern, string hint, List<string> allOthers)
    {
        var key = (itemClass ?? "\0", targetPattern, hint);
        if (_foreignCache.TryGetValue(key, out var cached)) return cached;
        var patterns = _corpus.PatternsMatchingLiteral(hint, itemClass);
        if (patterns.Count == 0) return allOthers;
        var scope = GetScope(itemClass);
        var lines = new List<string>();
        foreach (var p in patterns)
        {
            if (string.Equals(p, targetPattern, StringComparison.Ordinal)) continue;
            if (scope.ByPattern.TryGetValue(p, out var real)) lines.AddRange(real);
        }
        var result = lines.Count < allOthers.Count ? lines : allOthers;
        _foreignCache[key] = result;
        return result;
    }

    /// <summary>
    /// Longest literal run in a fragment (3+ characters, at least two
    /// alphanumeric). Used as a safe pre-filter for the false-positive scan.
    /// </summary>
    private static string? LiteralHint(string fragment)
    {
        var best = "";
        var current = new System.Text.StringBuilder();
        void Flush()
        {
            if (current.Length > best.Length && current.Length >= 3 &&
                current.ToString().Count(char.IsLetterOrDigit) >= 2)
                best = current.ToString();
            current.Clear();
        }
        for (var i = 0; i < fragment.Length; i++)
        {
            var c = fragment[i];
            if (c == '\\' && i + 1 < fragment.Length)
            {
                var n = fragment[i + 1];
                if (n == 'd')
                {
                    Flush();
                    i++;
                    if (i + 1 < fragment.Length && fragment[i + 1] == '+') i++;
                }
                else
                {
                    current.Append(n);
                    i++;
                }
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
        return best.Length >= 3 ? best : null;
    }

    private static int AnchorCount(string candidate) =>
        (candidate.StartsWith('^') ? 1 : 0) + (candidate.EndsWith('$') ? 1 : 0);

    private static int WildcardRank(string candidate) =>
        candidate.Contains(".*", StringComparison.Ordinal) ? 2
        : candidate.Contains(".+", StringComparison.Ordinal) ? 1
        : 0;

    private static int LetterCount(string candidate)
    {
        var count = 0;
        for (var i = 0; i < candidate.Length; i++)
        {
            if (candidate[i] == '\\' && i + 1 < candidate.Length)
            {
                if (candidate[i + 1] == 'd')
                {
                    i++;
                    if (i + 1 < candidate.Length && candidate[i + 1] == '+') i++;
                    continue;
                }
                i++;
                continue;
            }
            if (char.IsAsciiLetter(candidate[i])) count++;
        }
        return count;
    }
}

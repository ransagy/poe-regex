using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;
using PoeRegex.Core.Data;
using PoeRegex.Core.Search;

namespace PoeRegex.Core.Compilation;

/// <summary>
/// Turns modifier requirements into the shortest safe regex fragments,
/// then factors common prefixes across all included fragments.
/// </summary>
public sealed class CompactionEngine
{
    private readonly EngineCorpus _corpus;
    private readonly ConcurrentDictionary<string, Lazy<string?>> _rangeCache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Lazy<IReadOnlyList<string>>> _planCache =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Lazy<IReadOnlyList<StatConstraint>>> _constraintCache =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, IReadOnlyList<(string Pattern, string Real)>> _catalogLines =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<(string Fragment, string? Class, string? Literal), Lazy<ProofMatches>> _proofMatches = new();
    private readonly ConcurrentDictionary<(string Fragment, string? Class, string Allowed), IReadOnlyList<double>> _rollHits = new();
    private readonly ConcurrentDictionary<string, (bool Unsafe, bool Matched)> _rollResults = new(StringComparer.Ordinal);

    public CompactionEngine(EngineCorpus corpus)
    {
        _corpus = corpus;
    }

    /// <summary>
    /// Selects the shortest safe regex fragment for a single modifier
    /// requirement in the selected scope. Returns null when the modifier
    /// cannot be represented.
    /// </summary>
    public string? SelectFragment(ModifierRequirement requirement, string? itemClass)
    {
        if (requirement.RangeModIds is { Count: > 0 })
            return BuildTierRangeFragment(requirement, itemClass);

        var mod = _corpus.FindMod(requirement.Value);
        if (mod is null) return null;
        if (mod.StatLines.Count == 0) return null;

        // Hybrid mods have more than one line. The table is already proven
        // per scope; keep the shortest table form across the lines.
        string? best = null;
        StatLineEntry? bestLine = null;
        foreach (var line in mod.StatLines)
        {
            var fragment = Lookup(line.Pattern, itemClass);
            // The class anchor is already present in a class-scoped query, so
            // the global fragment is free to use when it is shorter.
            if (itemClass is not null)
            {
                var global = Lookup(line.Pattern, null);
                if (global is not null && (fragment is null || global.Length < fragment.Length))
                    fragment = global;
            }
            if (fragment is null) continue;
            if (best is null || fragment.Length < best.Length)
            {
                best = fragment;
                bestLine = line;
            }
        }
        if (best is null || bestLine is null) return null;

        if (requirement.MaxValue is not null)
            return BuildBoundedFragment(bestLine, requirement.MinValue, requirement.MaxValue, itemClass, best);
        if (requirement.MinValue is not null)
            return BuildThresholdFragment(bestLine, requirement.MinValue.Value, itemClass, best);
        return best;
    }

    /// <summary>
    /// One combined family+range fragment for a tier pick. Every covered tier
    /// must share one stat pattern; the catalog proof then checks both the
    /// pattern and the actual roll value, so a value class that also matches a
    /// worse tier is rejected.
    /// </summary>
    private string? BuildTierRangeFragment(ModifierRequirement requirement, string? itemClass)
    {
        var lines = new List<StatLineEntry>();
        string? pattern = null;
        foreach (var id in requirement.RangeModIds!)
        {
            var mod = _corpus.FindMod(id);
            if (mod is null || mod.StatLines.Count != 1) return null;
            var line = mod.StatLines[0];
            if (pattern is null) pattern = line.Pattern;
            else if (!string.Equals(pattern, line.Pattern, StringComparison.Ordinal)) return null;
            lines.Add(line);
        }
        if (pattern is null || lines.Count == 0) return null;

        var intervals = lines
            .Select(line => (Lo: line.MinValue, Hi: line.MaxValue))
            .Where(x => x.Hi >= x.Lo)
            .ToList();
        intervals = Clamp(intervals, requirement.MinValue, requirement.MaxValue);
        if (intervals.Count == 0) return null;

        var precision = lines.Max(line => Math.Max(
            RangeRegex.PrecisionOf(line.MinValue), RangeRegex.PrecisionOf(line.MaxValue)));
        return CachedRangeFragment(
            pattern, intervals, precision, itemClass, [pattern]);
    }

    /// <summary>Max-only or min+max filter on one catalog line.</summary>
    private string? BuildBoundedFragment(
        StatLineEntry line, double? minValue, double? maxValue, string? itemClass, string fallbackFp)
    {
        if (line.Pattern.IndexOf(SuffixIndex.DigitPlaceholder) < 0) return fallbackFp;
        if (minValue is null && maxValue is null) return fallbackFp;

        // A min without a max keeps the shipped "this or higher" behavior:
        // every better tier stays matchable instead of clipping at this
        // line's own maximum.
        if (minValue is not null && maxValue is null)
            return BuildThresholdFragment(line, minValue.Value, itemClass, fallbackFp);

        var lo = Math.Max(line.MinValue, minValue ?? line.MinValue);
        var hi = Math.Min(line.MaxValue, maxValue ?? line.MaxValue);
        if (hi < lo) return null;

        var precision = Math.Max(
            RangeRegex.PrecisionOf(line.MinValue), RangeRegex.PrecisionOf(line.MaxValue));
        return CachedRangeFragment(
            line.Pattern, [(lo, hi)], precision, itemClass, [line.Pattern]) ?? fallbackFp;
    }

    /// <summary>
    /// Stable key for one range-plan input. The data build writes exact
    /// tier fragments under this key so the browser can skip the first proof.
    /// </summary>
    public static string RangePlanKey(
        string pattern,
        IReadOnlyList<(double Lo, double Hi)> intervals,
        int precision,
        string? itemClass,
        IReadOnlyCollection<string> allowedPatterns) =>
        string.Join(
            "\u0001",
            pattern,
            itemClass ?? "",
            precision.ToString(CultureInfo.InvariantCulture),
            string.Join(
                ",",
                intervals
                    .OrderBy(x => x.Lo)
                    .ThenBy(x => x.Hi)
                    .Select(x => x.Lo.ToString("R", CultureInfo.InvariantCulture) + "-" +
                                 x.Hi.ToString("R", CultureInfo.InvariantCulture))),
            string.Join(",", allowedPatterns.OrderBy(x => x, StringComparer.Ordinal)));

    /// <summary>
    /// Data-build proof for a composed tier-union fragment. The per-tier
    /// value classes are already proven; this catches a bad template or split.
    /// </summary>
    public bool ProveRangeFragment(
        string fragment,
        IReadOnlyCollection<string> allowedPatterns,
        IReadOnlyList<(double Lo, double Hi)> intervals,
        string? itemClass,
        bool exact = false) =>
        CatalogRangeSafe(fragment, allowedPatterns, intervals, itemClass, exact);

    private string? CachedRangeFragment(
        string pattern,
        IReadOnlyList<(double Lo, double Hi)> intervals,
        int precision,
        string? itemClass,
        IReadOnlyCollection<string> allowedPatterns)
    {
        var key = RangePlanKey(pattern, intervals, precision, itemClass, allowedPatterns);
        if (_corpus.TryPrecomputedRangeFragment(key, out var precomputed))
            return precomputed;
        return _rangeCache.GetOrAdd(
            key,
            _ => new Lazy<string?>(() =>
                BuildRangeFragment(pattern, intervals, precision, itemClass, allowedPatterns))).Value;
    }

    /// <summary>
    /// Builds one fragment that replaces the first roll placeholder with the
    /// shortest candidate value class and proves it against the catalog: every
    /// matched line must be an allowed pattern and its first roll must be
    /// inside the requested intervals.
    /// </summary>
    private string? BuildRangeFragment(
        string pattern,
        IReadOnlyList<(double Lo, double Hi)> intervals,
        int precision,
        string? itemClass,
        IReadOnlyCollection<string> allowedPatterns)
    {
        var candidates = RangeRegex.Candidates(intervals, precision).ToList();
        if (candidates.Count == 0) return null;

        // A finite set of catalog rolls can be compiled exactly from a digit
        // trie. This is the universal path for fixed-tier families: 15–35
        // becomes 15|[23][05], while the loose [1-3][05] would include 10 and
        // is rejected by the proof.
        HashSet<string>? exactCandidates = null;
        if (precision == 0)
        {
            var allowedValues = AllowedIntegerValues(allowedPatterns, intervals, itemClass);
            if (allowedValues.Count > 0)
            {
                var exact = RangeRegex.ExactIntegerCandidates(allowedValues);
                if (exact.Count > 0)
                {
                    exactCandidates = new HashSet<string>(exact, StringComparer.Ordinal);
                    foreach (var candidate in exact)
                    {
                        if (!candidates.Contains(candidate))
                            candidates.Add(candidate);
                    }
                    candidates = candidates
                        .Distinct(StringComparer.Ordinal)
                        .OrderBy(candidate => candidate.Length)
                        .ThenBy(candidate => candidate, StringComparer.Ordinal)
                        .ToList();
                }
            }
        }

        var ranged = intervals.Any(x => x.Hi > x.Lo);
        var shapes = _corpus.DisplayPatternsFor(pattern, itemClass)
            .Where(shape => shape.Contains(SuffixIndex.DigitPlaceholder))
            .Where(shape => ranged
                ? shape.Contains('(', StringComparison.Ordinal)
                : !shape.Contains('(', StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        // A ranged stat's advanced copy text carries the roll as
        // `2.03(1.5-2.5)`. A plain-shape fragment would miss every advanced
        // item, so only range-aware shapes are eligible there. A fixed tier
        // must use the plain shape even when another mod shares its pattern
        // and has a ranged variant. Inside one catalog line the plain shape is
        // also worth trying: its literal separators keep a one-digit exact
        // range (`Adds 1 to ...`) from swallowing a longer roll such as 12,
        // while multi-tier unions keep the advanced shape.
        if (shapes.Count == 0 || IsWithinOneLine(intervals, allowedPatterns, itemClass))
        {
            shapes.Add(pattern);
            shapes = shapes.Distinct(StringComparer.Ordinal).ToList();
        }

        var anyNumber = precision > 0 ? @"\d+(?:\.\d+)?" : @"\d+";
        var familyFragment = Lookup(pattern, itemClass);
        if (itemClass is not null)
        {
            var global = Lookup(pattern, null);
            if (global is not null &&
                (familyFragment is null || global.Length < familyFragment.Length))
                familyFragment = global;
        }
        string? best = null;
        foreach (var shape in shapes)
        {
            foreach (var candidate in candidates)
            {
                if (exactCandidates?.Contains(candidate) != true &&
                    !RangeRegex.MatchesIntervals(candidate, intervals, precision)) continue;
                var forms = BuildRangeForms(shape, candidate, anyNumber, familyFragment)
                    .OrderBy(form => form.Text.Length)
                    .ThenBy(form => form.Text, StringComparer.Ordinal)
                    .ToList();
                var safe = false;
                foreach (var form in forms)
                {
                    if (!CatalogRangeSafe(
                            form.Text, allowedPatterns, intervals, itemClass,
                            exactCandidates?.Contains(candidate) == true,
                            form.Literal))
                        continue;
                    if (best is null || form.Text.Length < best.Length) best = form.Text;
                    safe = true;
                    break;
                }
                // Candidates are shortest-first; once this shape has a safe
                // form, a longer value class cannot produce a shorter result.
                if (safe) break;
            }
        }
        return best;
    }

    /// <summary>A range form and a literal every matching line must contain.</summary>
    private readonly record struct RangeForm(string Text, string? Literal);

    /// <summary>
    /// Forms tried for one shape and value class:
    /// the full display shape (anchored and not), a prefix-anchored
    /// <c>range.*tail</c> form, and the same with the shipped family
    /// fragment as the tail. The prefix keeps the range on the first roll,
    /// so an advanced range endpoint (1.5 in <c>2.03(1.5-2.5)</c>) cannot
    /// satisfy it.
    /// </summary>
    private static IReadOnlyList<RangeForm> BuildRangeForms(
        string shape, string valueRegex, string anyNumber, string? familyFragment)
    {
        var idx = shape.IndexOf(SuffixIndex.DigitPlaceholder);
        if (idx < 0) return [];
        var prefix = EscapeShape(shape[..idx]);
        var suffix = EscapeShape(shape[(idx + 1)..], anyNumber);
        var tail = TailAfterRangeGroup(shape, idx, anyNumber);
        // Parentheses are only needed when the value regex alternates. A
        // pure concatenation such as `1[2-7]\d` can sit directly in the
        // prefix/suffix, which keeps the compiled query shorter.
        var value = valueRegex.Contains('|', StringComparison.Ordinal)
            ? "(" + valueRegex + ")"
            : valueRegex;

        var forms = new List<RangeForm>(4)
        {
            new(prefix + value + suffix + "$", CleanLiteral(suffix)),
            new(prefix + value + suffix, CleanLiteral(suffix)),
            new(prefix + value + ".*" + tail, CleanLiteral(tail)),
        };
        if (!string.IsNullOrEmpty(familyFragment) &&
            !familyFragment.StartsWith(@"\.", StringComparison.Ordinal))
        {
            forms.Add(new(prefix + value + ".*" + familyFragment, null));
        }
        // The shipped fragment can carry the line prefix and a wildcard
        // (`\+.*fe`), so it cannot always follow a range. Its literal tail
        // can: `\+(range).*fe` is the shape the in-game checks validated.
        if (LiteralTail(familyFragment) is { } literalTail)
            forms.Add(new(prefix + value + ".*" + literalTail, CleanLiteral(literalTail)));
        return forms
            .GroupBy(form => form.Text, StringComparer.Ordinal)
            .Select(group => new RangeForm(
                group.Key,
                group.Select(form => form.Literal)
                    .FirstOrDefault(literal => literal is not null)))
            .ToList();
    }

    /// <summary>
    /// A literal run that every match of a form must contain, or null when
    /// the text has regex metacharacters. Used to narrow the proof scan.
    /// </summary>
    private static string? CleanLiteral(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var trimmed = text.TrimEnd('$');
        if (trimmed.Length < 3) return null;
        foreach (var c in trimmed)
        {
            if (!char.IsLetterOrDigit(c) &&
                c is not (' ' or '%' or '\'' or '/' or ',' or '-' or ':' or '_'))
                return null;
        }
        return trimmed;
    }

    /// <summary>Trailing literal run of a fragment, with an optional anchor.</summary>
    private static string? LiteralTail(string? fragment)
    {
        if (string.IsNullOrEmpty(fragment) || fragment.Contains('|')) return null;
        var anchored = fragment.EndsWith('$');
        var end = anchored ? fragment.Length - 1 : fragment.Length;
        var start = end;
        while (start > 0 &&
               (char.IsAsciiLetterOrDigit(fragment[start - 1]) ||
                fragment[start - 1] is ' ' or '%' or '\''))
        {
            start--;
        }
        if (end - start < 2) return null;
        return fragment[start..end] + (anchored ? "$" : "");
    }

    /// <summary>The literal stat tail after the first advanced (low-high) group.</summary>
    private static string TailAfterRangeGroup(string shape, int firstPlaceholder, string anyNumber)
    {
        var start = firstPlaceholder + 1;
        if (start < shape.Length && shape[start] == '(')
        {
            var depth = 0;
            for (var i = start; i < shape.Length; i++)
            {
                if (shape[i] == '(') depth++;
                else if (shape[i] == ')')
                {
                    depth--;
                    if (depth == 0)
                    {
                        start = i + 1;
                        break;
                    }
                }
            }
        }
        return EscapeShape(shape[start..], anyNumber);
    }

    private static string EscapeShape(string text, string? anyNumber = null)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var c in text)
        {
            if (c == SuffixIndex.DigitPlaceholder)
            {
                sb.Append(anyNumber ?? @"\d+");
            }
            else
            {
                if (Regex.IsMatch(c.ToString(), @"[\\.+*?()\[\]{}$|]"))
                    sb.Append('\\');
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    private static readonly Regex FirstNumber = new(
        @"[-+]?\d+(?:\.\d+)?", RegexOptions.CultureInvariant);

    private sealed record ProofMatches(
        bool Ok, IReadOnlyList<(string Pattern, double Value)> Lines);

    private bool CatalogRangeSafe(
        string fragment,
        IReadOnlyCollection<string> allowedPatterns,
        IReadOnlyList<(double Lo, double Hi)> intervals,
        string? itemClass,
        bool exact = false,
        string? literal = null)
    {
        // The representative catalog line shows the high roll. A value class
        // that also matches lower rolls of the next tier would pass the
        // line scan, so reject any allowed span whose out-of-range rolls the
        // fragment matches. The same walk proves the fragment matches at
        // least one in-range roll, which keeps a narrowed range valid even
        // when no representative line sits inside it.
        var matchedAny = false;
        if (exact)
        {
            // A trie candidate only matches values that were collected from
            // the allowed spans, so it cannot overmatch. Check one in-range
            // roll per span for shape completeness without walking every
            // value.
            var regex = new Regex(
                fragment, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!MatchesAnyAllowedRoll(regex, allowedPatterns, intervals, itemClass))
                return false;
            matchedAny = true;
        }
        else
        {
            var (unsafeRoll, matchedRoll) =
                CachedCheckCatalogRolls(fragment, allowedPatterns, intervals, itemClass);
            if (unsafeRoll) return false;
            matchedAny = matchedRoll;
        }
        // No allowed roll matched: the fragment cannot represent the range,
        // and any disallowed proof-line hit would reject it anyway.
        if (!matchedAny) return false;
        var proof = _proofMatches.GetOrAdd(
            (fragment, itemClass, literal),
            key => new Lazy<ProofMatches>(() => BuildProofMatches(
                key.Fragment, key.Class, key.Literal))).Value;
        if (!proof.Ok) return false;
        matchedAny |= proof.Lines.Count > 0;
        foreach (var (pattern, value) in proof.Lines)
        {
            if (!allowedPatterns.Contains(pattern)) return false;
            if (!InIntervals(value, intervals)) return false;
        }
        return matchedAny;
    }

    /// <summary>
    /// Catalog lines a fragment matches, with their first roll. The match
    /// set depends only on the fragment and scope, so interval and filter
    /// variants reuse it instead of rescanning the proof corpus.
    /// </summary>
    private ProofMatches BuildProofMatches(
        string fragment, string? itemClass, string? literal)
    {
        var regex = new Regex(
            fragment, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var proofLines = literal is null
            ? CatalogLines(itemClass)
            : _corpus.ProofLinesWithLiteral(itemClass, literal);
        var lines = new List<(string Pattern, double Value)>();
        foreach (var (pattern, real) in proofLines)
        {
            if (!regex.IsMatch(real)) continue;
            if (!TryFirstNumber(real, out var value))
                return new ProofMatches(false, []);
            lines.Add((pattern, value));
        }
        return new ProofMatches(true, lines);
    }

    /// <summary>
    /// True when the request is one narrow span inside a single catalog line.
    /// A multi-tier union must keep the advanced display shape, because its
    /// copy text carries `roll(low-high)` even though each tier fits a line.
    /// </summary>
    private bool IsWithinOneLine(
        IReadOnlyList<(double Lo, double Hi)> intervals,
        IReadOnlyCollection<string> allowedPatterns,
        string? itemClass)
    {
        if (intervals.Count != 1) return false;
        var (lo, hi) = intervals[0];
        foreach (var pattern in allowedPatterns)
        foreach (var mod in _corpus.ModsWithPattern(pattern))
        {
            if (itemClass is not null &&
                !mod.Classes.Contains(itemClass, StringComparer.Ordinal))
                continue;
            foreach (var line in mod.StatLines)
            {
                if (!string.Equals(line.Pattern, pattern, StringComparison.Ordinal))
                    continue;
                if (lo >= line.MinValue - 1e-9 && hi <= line.MaxValue + 1e-9)
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Integer rolls the allowed patterns can actually display inside the
    /// requested intervals. Used to synthesize an exact value regex; the
    /// catalog proof still validates the final fragment.
    /// </summary>
    private IReadOnlyList<long> AllowedIntegerValues(
        IReadOnlyCollection<string> allowedPatterns,
        IReadOnlyList<(double Lo, double Hi)> intervals,
        string? itemClass)
    {
        var values = new SortedSet<long>();
        foreach (var pattern in allowedPatterns)
        {
            foreach (var mod in _corpus.ModsWithPattern(pattern))
            {
                if (itemClass is not null &&
                    !mod.Classes.Contains(itemClass, StringComparer.Ordinal))
                    continue;
                foreach (var line in mod.StatLines)
                {
                    if (!string.Equals(line.Pattern, pattern, StringComparison.Ordinal))
                        continue;
                    if (line.MaxValue < line.MinValue ||
                        (line.MinValue == 0 && line.MaxValue == 0))
                        continue;
                    // The fragment replaces the first placeholder. On a
                    // single-roll line that placeholder spans min..max, but on
                    // a multi-roll line inside one catalog span it only shows
                    // the first value: walking 1..2 for `Adds 1 to 2` would
                    // treat 2 as a first roll and make a narrow exact range
                    // impossible to prove. Multi-tier thresholds keep the
                    // full span walk.
                    var first = (long)Math.Ceiling(line.MinValue - 1e-9);
                    var last = (long)Math.Floor(line.MaxValue + 1e-9);
                    if (line.Pattern.Count(ch => ch == SuffixIndex.DigitPlaceholder) > 1 &&
                        intervals.All(interval =>
                            interval.Lo >= line.MinValue - 1e-9 &&
                            interval.Hi <= line.MaxValue + 1e-9))
                        last = first;
                    if (last - first > 5000) continue;
                    for (var value = first; value <= last; value++)
                    {
                        if (InIntervals(value, intervals)) values.Add(value);
                        if (values.Count > 5000) return [];
                    }
                }
            }
        }
        return values.ToList();
    }

    private static bool OverlapsIntervals(
        IReadOnlyList<(double Lo, double Hi)> intervals, double lo, double hi) =>
        intervals.Any(interval =>
            lo <= interval.Hi + 1e-9 && interval.Lo <= hi + 1e-9);

    /// <summary>
    /// True when the fragment matches at least one in-range roll in an allowed
    /// span. Samples the first in-range value per span, which is enough to
    /// prove the shape is real without walking every value.
    /// </summary>
    private bool MatchesAnyAllowedRoll(
        Regex regex,
        IReadOnlyCollection<string> allowedPatterns,
        IReadOnlyList<(double Lo, double Hi)> intervals,
        string? itemClass)
    {
        foreach (var pattern in allowedPatterns)
        {
            foreach (var mod in _corpus.ModsWithPattern(pattern))
            {
                if (itemClass is not null &&
                    !mod.Classes.Contains(itemClass, StringComparer.Ordinal))
                    continue;
                foreach (var line in mod.StatLines)
                {
                    if (!string.Equals(line.Pattern, pattern, StringComparison.Ordinal))
                        continue;
                    if (line.MaxValue < line.MinValue ||
                        (line.MinValue == 0 && line.MaxValue == 0))
                        continue;
                    if (!OverlapsIntervals(intervals, line.MinValue, line.MaxValue))
                        continue;
                    var precision = Math.Max(
                        RangeRegex.PrecisionOf(line.MinValue),
                        RangeRegex.PrecisionOf(line.MaxValue));
                    var advanced = AdvancedStatLine.For(line);
                    foreach (var value in SpanSamples(line.MinValue, line.MaxValue, precision))
                    {
                        if (!InIntervals(value, intervals)) continue;
                        if (regex.IsMatch(ReplaceFirstNumber(line.Line, value, precision)))
                            return true;
                        if (!string.Equals(advanced, line.Line, StringComparison.Ordinal) &&
                            regex.IsMatch(ReplaceFirstNumber(advanced, value, precision)))
                            return true;
                        break;
                    }
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Cached roll proof by fragment, scope, allowed patterns, and intervals.
    /// A safe form's hit values are reused for later interval unions; an
    /// unsafe form keeps the early exit and is only cached for the exact
    /// interval set that rejected it.
    /// </summary>
    private (bool Unsafe, bool Matched) CachedCheckCatalogRolls(
        string fragment,
        IReadOnlyCollection<string> allowedPatterns,
        IReadOnlyList<(double Lo, double Hi)> intervals,
        string? itemClass)
    {
        var allowedKey = string.Join(
            ",", allowedPatterns.OrderBy(x => x, StringComparer.Ordinal));
        var resultKey = string.Join(
            "\u0001",
            fragment,
            itemClass ?? "",
            allowedKey,
            string.Join(
                ",",
                intervals
                    .OrderBy(x => x.Lo)
                    .ThenBy(x => x.Hi)
                    .Select(x => x.Lo.ToString("R", CultureInfo.InvariantCulture) + "-" +
                                 x.Hi.ToString("R", CultureInfo.InvariantCulture))));
        if (_rollResults.TryGetValue(resultKey, out var cached)) return cached;

        if (!_rollHits.TryGetValue((fragment, itemClass, allowedKey), out var hits))
        {
            var (unsafeRoll, matchedRoll, foundHits) = CheckCatalogRolls(
                new Regex(fragment, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                allowedPatterns, intervals, itemClass);
            if (unsafeRoll)
            {
                var unsafeResult = (true, matchedRoll);
                _rollResults[resultKey] = unsafeResult;
                return unsafeResult;
            }
            hits = foundHits!;
            _rollHits[(fragment, itemClass, allowedKey)] = hits;
        }

        var matched = false;
        foreach (var value in hits)
        {
            if (!InIntervals(value, intervals))
            {
                var unsafeResult = (true, matched);
                _rollResults[resultKey] = unsafeResult;
                return unsafeResult;
            }
            matched = true;
        }
        var result = (false, matched);
        _rollResults[resultKey] = result;
        return result;
    }

    /// <summary>
    /// Walks every displayed roll in the allowed catalog spans. Returns
    /// unsafe when the fragment matches an out-of-range roll, and matched
    /// when it matches at least one in-range roll. The representative line
    /// is the high roll, so a narrowed range needs the synthesized values.
    /// </summary>
    private (bool Unsafe, bool Matched, List<double>? Hits) CheckCatalogRolls(
        Regex regex,
        IReadOnlyCollection<string> allowedPatterns,
        IReadOnlyList<(double Lo, double Hi)> intervals,
        string? itemClass)
    {
        var matched = false;
        var hits = new List<double>();
        foreach (var pattern in allowedPatterns)
        {
            foreach (var mod in _corpus.ModsWithPattern(pattern))
            {
                if (itemClass is not null &&
                    !mod.Classes.Contains(itemClass, StringComparer.Ordinal))
                    continue;
                foreach (var line in mod.StatLines)
                {
                    if (!string.Equals(line.Pattern, pattern, StringComparison.Ordinal))
                        continue;
                    if (line.MaxValue < line.MinValue ||
                        (line.MinValue == 0 && line.MaxValue == 0))
                        continue;
                    var precision = Math.Max(
                        RangeRegex.PrecisionOf(line.MinValue),
                        RangeRegex.PrecisionOf(line.MaxValue));
                    var advanced = AdvancedStatLine.For(line);
                    foreach (var value in SpanSamples(line.MinValue, line.MaxValue, precision))
                    {
                        var plain = ReplaceFirstNumber(line.Line, value, precision);
                        var ranged = ReplaceFirstNumber(advanced, value, precision);
                        var hit = regex.IsMatch(plain) ||
                                  (!string.Equals(advanced, line.Line, StringComparison.Ordinal) &&
                                   regex.IsMatch(ranged));
                        if (!hit) continue;
                        if (!InIntervals(value, intervals)) return (true, matched, null);
                        matched = true;
                        hits.Add(value);
                    }
                }
            }
        }
        return (false, matched, hits);
    }

    private static IEnumerable<double> SpanSamples(double lo, double hi, int precision)
    {
        if (hi < lo) yield break;
        var step = precision <= 0 ? 1 : Math.Pow(10, -precision);
        var steps = (hi - lo) / step;
        if (steps > 2000)
        {
            yield return lo;
            yield return hi;
            yield return Math.Round((lo + hi) / 2, precision);
            yield break;
        }
        var count = (int)Math.Round(steps);
        for (var i = 0; i <= count; i++)
            yield return Math.Round(lo + i * step, precision);
    }

    private static string ReplaceFirstNumber(string text, double value, int precision)
    {
        var match = FirstNumber.Match(text);
        if (!match.Success) return text;
        var formatted = precision <= 0
            ? Math.Round(value).ToString("0", CultureInfo.InvariantCulture)
            : value.ToString("0." + new string('#', precision), CultureInfo.InvariantCulture);
        if (match.Value.StartsWith('+') && !formatted.StartsWith('+'))
            formatted = "+" + formatted;
        return text[..match.Index] + formatted + text[(match.Index + match.Length)..];
    }

    private IReadOnlyList<(string Pattern, string Real)> CatalogLines(string? itemClass) =>
        _catalogLines.GetOrAdd(
            itemClass is null ? "\0" : "\u0001" + itemClass,
            _ => _corpus.ProofLines(itemClass));

    private static bool TryFirstNumber(string text, out double value)
    {
        value = 0;
        var match = Regex.Match(text, @"[-+]?\d+(?:\.\d+)?", RegexOptions.CultureInvariant);
        return match.Success && double.TryParse(
            match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static bool InIntervals(double value, IReadOnlyList<(double Lo, double Hi)> intervals)
    {
        foreach (var (lo, hi) in intervals)
            if (value >= lo - 1e-9 && value <= hi + 1e-9) return true;
        return false;
    }

    private static List<(double Lo, double Hi)> Clamp(
        IReadOnlyList<(double Lo, double Hi)> intervals, double? min, double? max)
    {
        var result = new List<(double Lo, double Hi)>();
        foreach (var (lo, hi) in intervals)
        {
            var a = min is { } mn ? Math.Max(lo, mn) : lo;
            var b = max is { } mx ? Math.Min(hi, mx) : hi;
            if (b >= a) result.Add((a, b));
        }
        return result;
    }

    private string? Lookup(string pattern, string? itemClass) =>
        _corpus.TryPrecomputedFragment(pattern, itemClass, out var fragment)
            ? fragment
            : null;

    /// <summary>One chosen representation and the capability that produced it.</summary>
    public sealed record PlannedFragment(string Text, string Kind);

    /// <summary>
    /// Canonicalize a pick list and plan each stat family once. This is the
    /// single entry point for the compiler: four exact tiers, an “or better”
    /// pick, and the family pick become the same canonical constraint.
    /// </summary>
    public IReadOnlyList<string> PlanMany(
        IReadOnlyList<ModifierRequirement> requirements, string? itemClass)
    {
        var key = PlanKey(requirements, itemClass);
        return _planCache.GetOrAdd(
            key,
            _ => new Lazy<IReadOnlyList<string>>(
                () => ComputePlanMany(requirements, itemClass))).Value;
    }

    /// <summary>Canonicalize with a cache; the paste composer calls this per slot.</summary>
    public IReadOnlyList<StatConstraint> Canonicalize(
        IReadOnlyList<ModifierRequirement> requirements, string? itemClass)
    {
        var key = PlanKey(requirements, itemClass);
        return _constraintCache.GetOrAdd(
            key,
            _ => new Lazy<IReadOnlyList<StatConstraint>>(
                () => ConstraintCanonicalizer.Canonicalize(
                    requirements, itemClass, _corpus))).Value;
    }

    private (IReadOnlyList<StatConstraint> Constraints, IReadOnlyList<ModifierRequirement> Raw)
        SplitCached(IReadOnlyList<ModifierRequirement> requirements, string? itemClass)
    {
        var raw = requirements
            .Where(requirement => requirement.IsRegex ||
                                  _corpus.FindMod(requirement.Value) is null)
            .ToList();
        return (Canonicalize(requirements, itemClass), raw);
    }

    private IReadOnlyList<string> ComputePlanMany(
        IReadOnlyList<ModifierRequirement> requirements, string? itemClass)
    {
        var (constraints, raw) = SplitCached(requirements, itemClass);
        var result = new List<string>();
        foreach (var constraint in constraints)
        {
            var planned = PlanConstraint(constraint, itemClass);
            if (planned is not null)
            {
                result.Add(planned.Text);
                continue;
            }
            // Hybrid/multi-pattern constraints keep the old per-member
            // fallback so a family pick still ORs every variant's proven
            // table fragment, not just the anchor's line.
            var fallbackFragments = constraint.MemberIds
                .Select(id => SelectFragment(new ModifierRequirement(id), itemClass))
                .Where(IsCleanFragment)
                .Select(fragment => fragment!)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            result.AddRange(fallbackFragments);
        }
        foreach (var requirement in raw)
            result.Add(requirement.IsRegex
                ? requirement.Value
                : _corpus.Syntax.EscapeLiteral(requirement.Value));
        return result;
    }

    private static string PlanKey(
        IReadOnlyList<ModifierRequirement> requirements, string? itemClass)
    {
        var parts = requirements.Select(requirement =>
        {
            var min = requirement.MinValue?.ToString("R", CultureInfo.InvariantCulture) ?? "";
            var max = requirement.MaxValue?.ToString("R", CultureInfo.InvariantCulture) ?? "";
            var ids = requirement.RangeModIds is { Count: > 0 } rangeIds
                ? string.Join(",", rangeIds.OrderBy(id => id, StringComparer.Ordinal))
                : "";
            return (requirement.IsRegex ? "r" : "m") + (requirement.FullFamily ? "f" : "") +
                   "\u0002" + requirement.Value + "\u0002" + min + "\u0002" + max + "\u0002" + ids;
        });
        // Keep input order in the key: the canonicalizer already sorts the
        // planned constraints, but raw regex terms keep their order in the
        // emitted block. Sorting here would let reversed raw inputs share a
        // cached result.
        return (itemClass is null ? "\0" : "\u0001" + itemClass) + "\u0001" +
               string.Join("\u0001", parts);
    }

    /// <summary>
    /// Plan one canonical constraint. Full-family coverage uses the exact
    /// table fragment; a multi-value tier/filter prefers the range form; a
    /// single-value family uses the shortest proven form.
    /// </summary>
    public PlannedFragment? PlanConstraint(StatConstraint constraint, string? itemClass)
    {
        if (constraint.Patterns.Count != 1) return null;
        var pattern = constraint.Patterns[0];

        var familyFragment = Lookup(pattern, itemClass);
        if (itemClass is not null)
        {
            var global = Lookup(pattern, null);
            if (global is not null &&
                (familyFragment is null || global.Length < familyFragment.Length))
                familyFragment = global;
        }
        if (!IsCleanFragment(familyFragment)) familyFragment = null;
        if (constraint.Intervals.Count == 0)
            return familyFragment is null
                ? null
                : new PlannedFragment(familyFragment, "family");

        // A full-family multi-tier pick uses the table fragment without
        // building a range. A single-tier family range still needs the
        // precise range form.
        var needsRangeForFullFamily = constraint.IsFullFamily &&
            constraint.PreferRange &&
            constraint.FamilyHasMultipleValues &&
            constraint.MemberIds.Count == 1;
        if (constraint.IsFullFamily && familyFragment is not null &&
            !needsRangeForFullFamily)
            return new PlannedFragment(familyFragment, "family");
        // A plain pick with a table fragment keeps the exact-table baseline;
        // only filters and tier selections pay for range generation. A plain
        // pick with no table entry fails closed instead of silently inventing
        // a live range.
        if (!constraint.PreferRange)
            return familyFragment is null
                ? null
                : new PlannedFragment(familyFragment, "family");

        var precision = constraint.Intervals
            .SelectMany(interval => new[]
            {
                RangeRegex.PrecisionOf(interval.Lo),
                RangeRegex.PrecisionOf(interval.Hi)
            })
            .DefaultIfEmpty(0)
            .Max();
        var intervals = constraint.Intervals.Select(i => (i.Lo, i.Hi)).ToList();
        var range = _corpus.TryComposeTierRange(
                pattern, intervals, itemClass, out var composed) &&
            IsCleanFragment(composed)
            ? composed
            : CachedRangeFragment(pattern, intervals, precision, itemClass, [pattern]);
        if (!IsCleanFragment(range)) range = null;

        if (constraint.IsFullFamily)
        {
            if (needsRangeForFullFamily && range is not null)
                return new PlannedFragment(range, "range");
            if (familyFragment is not null)
                return new PlannedFragment(familyFragment, "family");
            if (range is not null)
                return new PlannedFragment(range, "range");
        }

        if (constraint.PreferRange && constraint.FamilyHasMultipleValues && range is not null)
            return new PlannedFragment(range, "range");

        var candidates = new List<PlannedFragment>();
        if (range is not null) candidates.Add(new PlannedFragment(range, "range"));
        if (familyFragment is not null) candidates.Add(new PlannedFragment(familyFragment, "family"));
        return candidates
            .Where(candidate => IsCleanFragment(candidate.Text))
            .OrderBy(candidate => candidate.Text.Length)
            .ThenBy(candidate => candidate.Text, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static bool IsCleanFragment(string? text) =>
        text is not null && text.IndexOf('\n') < 0 && text.IndexOf('\r') < 0;

    private bool IsFragmentSafe(string? itemClass, string fragment, string pattern)
    {
        var allowed = new HashSet<string> { pattern };
        // Any line the threshold regex can match must contain the pattern's
        // longest literal run; skipping the rest keeps the oracle from
        // scanning the whole catalog on every minimum change.
        var literal = EngineCorpus.LiteralProbe(pattern);
        return itemClass is not null
            ? _corpus.RegexSafeOnClass(itemClass, fragment, allowed, literal)
            : _corpus.GlobalRegexSafe(fragment, allowed, literal);
    }

    /// <summary>
    /// Builds a fragment matching values >= minValue. The upper bound is the
    /// pattern's own maximum, so the range builder can anchor the family tail
    /// and prove the actual roll values; a plain "digit range + rest of line"
    /// fragment also matches longer lines such as "25% increased Movement
    /// Speed while Sprinting" and was silently falling back.
    /// </summary>
    private string? BuildThresholdFragment(StatLineEntry line, double minValue, string? itemClass, string fallbackFp)
    {
        var pattern = line.Pattern;
        if (pattern.IndexOf(SuffixIndex.DigitPlaceholder) < 0) return fallbackFp;

        var maxValue = MaxPatternValue(pattern, itemClass);
        if (maxValue <= 0) maxValue = line.MaxValue;
        if (maxValue < minValue) return null;

        var precision = Math.Max(
            RangeRegex.PrecisionOf(line.MinValue), RangeRegex.PrecisionOf(line.MaxValue));
        var fragment = CachedRangeFragment(
            pattern, [(minValue, maxValue)], precision, itemClass, [pattern]);
        return fragment ?? fallbackFp;
    }

    /// <summary>Largest catalog roll for a pattern in the selected scope.</summary>
    private double MaxPatternValue(string pattern, string? itemClass)
    {
        var values = _corpus.ModsWithPattern(pattern)
            .Where(mod => itemClass is null ||
                          mod.Classes.Contains(itemClass, StringComparer.Ordinal))
            .SelectMany(mod => mod.StatLines)
            .Where(stat => string.Equals(stat.Pattern, pattern, StringComparison.Ordinal))
            .Select(stat => stat.MaxValue)
            .Where(value => value > 0)
            .ToList();
        return values.Count == 0 ? 0 : values.Max();
    }

    /// <summary>
    /// Factors a common prefix across fragments using a trie, e.g.
    /// "to maximum" common → "to (maxima|strength)" style alternation.
    /// Deterministic and order-independent.
    /// </summary>
    public static IReadOnlyList<string> FactorCommonPrefixes(IReadOnlyList<string> fragments) =>
        FactorWithSources(fragments).Select(a => a.Text).ToList();

    /// <summary>One emitted alternative and the input fragment indexes it covers.</summary>
    public readonly record struct FactoredAlternative(string Text, IReadOnlyList<int> SourceIndices);

    /// <summary>
    /// As <see cref="FactorCommonPrefixes"/>, but keeps track of which input
    /// fragments produced each emitted alternative. The picker ruler uses this
    /// to split a single any-of block into per-pick segments: a factored
    /// alternative that covers several picks becomes one shared segment.
    /// </summary>
    public static IReadOnlyList<FactoredAlternative> FactorWithSources(IReadOnlyList<string> fragments)
    {
        if (fragments.Count < 2)
            return fragments.Select((f, i) => new FactoredAlternative(f, [i])).ToList();

        var sources = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        for (var i = 0; i < fragments.Count; i++)
        {
            if (!sources.TryGetValue(fragments[i], out var list))
                sources[fragments[i]] = list = [];
            list.Add(i);
        }
        var distinct = fragments.Distinct().OrderBy(s => s, StringComparer.Ordinal).ToList();

        // Group by the first character; within a group find the longest common prefix
        // of all members. If shared prefix is >= 2 chars and alternation is shorter,
        // factor it.
        var result = new List<FactoredAlternative>();
        var groups = distinct.GroupBy(s => s.Length > 0 ? s[0] : '\0').OrderBy(g => g.Key);
        foreach (var group in groups)
        {
            var items = group.ToList();
            if (items.Count < 2)
            {
                result.Add(new FactoredAlternative(items[0], sources[items[0]]));
                continue;
            }
            // Character classes and groups are multi-character tokens. A
            // character-based prefix split can cut inside them and produce
            // invalid regex such as `1[(234]\d...`. Leave those alternatives
            // unfactored rather than risk a malformed query.
            if (items.Any(ContainsStructuredRegex))
            {
                result.AddRange(items.Select(t => new FactoredAlternative(t, sources[t])));
                continue;
            }
            var prefix = LongestCommonPrefix(items);
            var suffixes = prefix.Length >= 2 && items.All(s => s.Length > prefix.Length)
                ? items.Select(s => s.Substring(prefix.Length)).ToList()
                : null;
            // Do not split an escape or a wildcard token, and do not put a
            // quantifier at the start of an alternative (`\d` + `+...`).
            var splittable = suffixes is not null
                && prefix.Length >= 2
                && !EndsWithOddBackslash(prefix)
                && !prefix.EndsWith('.')
                && suffixes.All(s => s.Length > 0 && s[0] is not ('*' or '+' or '?' or '{' or '|' or ')'));
            if (splittable)
            {
                var factored = prefix + "(" + string.Join("|", suffixes!) + ")";
                // Only use if actually shorter than the union.
                var union = string.Join("|", items);
                if (factored.Length < union.Length)
                {
                    result.Add(new FactoredAlternative(factored,
                        items.SelectMany(t => sources[t]).Distinct().OrderBy(x => x).ToList()));
                }
                else
                {
                    result.AddRange(items.Select(t => new FactoredAlternative(t, sources[t])));
                }
            }
            else
            {
                result.AddRange(items.Select(t => new FactoredAlternative(t, sources[t])));
            }
        }
        return result;
    }

    private static bool ContainsStructuredRegex(string fragment) =>
        fragment.IndexOfAny(['[', ']', '(', ')', '{', '}', '|']) >= 0;

    private static bool EndsWithOddBackslash(string s)
    {
        var count = 0;
        for (var i = s.Length - 1; i >= 0 && s[i] == '\\'; i--) count++;
        return count % 2 == 1;
    }

    private static string LongestCommonPrefix(IReadOnlyList<string> strings)
    {
        var first = strings[0];
        var max = first.Length;
        for (var i = 1; i < strings.Count && max > 0; i++)
        {
            var other = strings[i];
            var n = 0;
            while (n < max && n < other.Length && first[n] == other[n]) n++;
            max = n;
        }
        return first.Substring(0, max);
    }
}

/// <summary>Builds a regex that matches decimal integers &gt;= a minimum value.</summary>
public static class DigitRangeRegex
{
    public static string? Build(int minValue)
    {
        if (minValue < 0) return null; // negative thresholds unsupported for now
        if (minValue <= 0) return @"\d+";
        var digits = minValue.ToString();
        var parts = new List<string> { $@"\d{{{digits.Length + 1},}}" };
        AppendSameLength(parts, digits, "");
        return parts.Count == 1 ? $@"\d{{{digits.Length + 1},}}" : string.Join("|", parts);
    }

    /// <summary>
    /// Appends patterns for numbers with exactly <paramref name="digits.Length"/> digits
    /// that are &gt;= <paramref name="digits"/>, recursing on the shared leading prefix.
    /// </summary>
    private static void AppendSameLength(List<string> parts, string digits, string prefix)
    {
        var n = digits.Length;
        var d = digits[0] - '0';
        var tail = digitClass(n - 1);
        if (n == 1)
        {
            // Single digit: numbers d..9.
            if (d <= 9) parts.Add($"{prefix}[{d}-9]");
            return;
        }
        if (d < 9)
        {
            // Leading digit strictly greater than the minimum's: tail is free.
            parts.Add($"{prefix}[{d + 1}-9]{tail}");
        }
        // Leading digit equal: recurse on the remaining digits.
        AppendSameLength(parts, digits.Substring(1), prefix + d);
    }

    private static string digitClass(int count)
    {
        if (count <= 0) return "";
        return count == 1 ? @"\d" : $@"\d{{{count}}}";
    }
}

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
    private readonly ConcurrentDictionary<string, IReadOnlyList<(string Pattern, string Real)>> _catalogLines =
        new(StringComparer.Ordinal);

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

    private string? CachedRangeFragment(
        string pattern,
        IReadOnlyList<(double Lo, double Hi)> intervals,
        int precision,
        string? itemClass,
        IReadOnlyCollection<string> allowedPatterns)
    {
        var key = string.Join(
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
        var candidates = RangeRegex.Candidates(intervals, precision);
        if (candidates.Count == 0) return null;

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
        // and has a ranged variant.
        if (shapes.Count == 0) shapes.Add(pattern);

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
                if (!RangeRegex.MatchesIntervals(candidate, intervals, precision)) continue;
                var forms = BuildRangeForms(shape, candidate, anyNumber, familyFragment)
                    .OrderBy(fragment => fragment.Length)
                    .ThenBy(fragment => fragment, StringComparer.Ordinal)
                    .ToList();
                var safe = false;
                foreach (var fragment in forms)
                {
                    if (!CatalogRangeSafe(fragment, allowedPatterns, intervals, itemClass)) continue;
                    if (best is null || fragment.Length < best.Length) best = fragment;
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

    /// <summary>
    /// Forms tried for one shape and value class:
    /// the full display shape (anchored and not), a prefix-anchored
    /// <c>range.*tail</c> form, and the same with the shipped family
    /// fragment as the tail. The prefix keeps the range on the first roll,
    /// so an advanced range endpoint (1.5 in <c>2.03(1.5-2.5)</c>) cannot
    /// satisfy it.
    /// </summary>
    private static IReadOnlyList<string> BuildRangeForms(
        string shape, string valueRegex, string anyNumber, string? familyFragment)
    {
        var idx = shape.IndexOf(SuffixIndex.DigitPlaceholder);
        if (idx < 0) return [];
        var prefix = EscapeShape(shape[..idx]);
        var suffix = EscapeShape(shape[(idx + 1)..], anyNumber);
        var tail = TailAfterRangeGroup(shape, idx, anyNumber);
        var value = "(" + valueRegex + ")";

        var forms = new List<string>(4)
        {
            prefix + value + suffix + "$",
            prefix + value + suffix,
            prefix + value + ".*" + tail,
        };
        if (!string.IsNullOrEmpty(familyFragment) &&
            !familyFragment.StartsWith(@"\.", StringComparison.Ordinal))
        {
            forms.Add(prefix + value + ".*" + familyFragment);
        }
        // The shipped fragment can carry the line prefix and a wildcard
        // (`\+.*fe`), so it cannot always follow a range. Its literal tail
        // can: `\+(range).*fe` is the shape the in-game checks validated.
        if (LiteralTail(familyFragment) is { } literalTail)
            forms.Add(prefix + value + ".*" + literalTail);
        return forms.Distinct(StringComparer.Ordinal).ToList();
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

    private bool CatalogRangeSafe(
        string fragment,
        IReadOnlyCollection<string> allowedPatterns,
        IReadOnlyList<(double Lo, double Hi)> intervals,
        string? itemClass)
    {
        var regex = new Regex(
            fragment, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var matchedAny = false;
        foreach (var (pattern, real) in CatalogLines(itemClass))
        {
            if (!regex.IsMatch(real)) continue;
            matchedAny = true;
            if (!allowedPatterns.Contains(pattern)) return false;
            if (!TryFirstNumber(real, out var value)) return false;
            if (!InIntervals(value, intervals)) return false;
        }
        return matchedAny;
    }

    private IReadOnlyList<(string Pattern, string Real)> CatalogLines(string? itemClass) =>
        _catalogLines.GetOrAdd(
            itemClass ?? "\0", _ => _corpus.ExplainLines(itemClass));

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
    /// Builds a fragment matching values >= minValue by replacing the value token
    /// in the line's pattern with a digit-range alternation, then re-validating
    /// uniqueness on the class corpus against real copy text.
    /// </summary>
    private string? BuildThresholdFragment(StatLineEntry line, double minValue, string? itemClass, string fallbackFp)
    {
        var pattern = line.Pattern;
        if (pattern.IndexOf(SuffixIndex.DigitPlaceholder) < 0) return fallbackFp;

        var digitRange = DigitRangeRegex.Build((int)Math.Ceiling(minValue));
        if (digitRange is null) return fallbackFp;

        var shapes = _corpus.DisplayPatternsFor(pattern, itemClass)
            .Where(x => x.Contains('(', StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        // Prefer a range-aware shape when one exists: the compact catalog
        // pattern would silently fail on every advanced ranged item.
        string? best = null;
        foreach (var shape in shapes)
        {
            var fragment = BuildThresholdFromPattern(shape, digitRange);
            if (fragment is null) continue;
            if (!IsFragmentSafe(itemClass, fragment, pattern)) continue;
            if (best is null || fragment.Length < best.Length) best = fragment;
        }
        if (best is not null) return best;

        var plain = BuildThresholdFromPattern(pattern, digitRange);
        if (plain is not null && IsFragmentSafe(itemClass, plain, pattern)) return plain;
        return fallbackFp;
    }

    private static string? BuildThresholdFromPattern(string pattern, string digitRange)
    {
        var idx = pattern.IndexOf(SuffixIndex.DigitPlaceholder);
        if (idx < 0) return null;
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < pattern.Length; i++)
        {
            if (i == idx)
            {
                sb.Append("(").Append(digitRange).Append(')');
            }
            else if (pattern[i] == SuffixIndex.DigitPlaceholder)
            {
                // Other rolls and tier-span endpoints can be multi-digit.
                sb.Append(@"\d+");
            }
            else
            {
                var c = pattern[i];
                if (System.Text.RegularExpressions.Regex.IsMatch(c.ToString(), @"[\\.+*?()\[\]{}$|]"))
                    sb.Append('\\');
                sb.Append(c);
            }
        }
        return sb.ToString();
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

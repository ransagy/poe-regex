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
    private readonly ICompilerStrategy _strategy;
    private readonly FragmentSelector _selector;

    public CompactionEngine(EngineCorpus corpus, ICompilerStrategy strategy)
    {
        _corpus = corpus;
        _strategy = strategy;
        _selector = new FragmentSelector(corpus, strategy, policy: FragmentPolicy.Guaranteed);
    }

    /// <summary>
    /// Selects the shortest safe regex fragment for a single modifier
    /// requirement in the selected scope. Returns null when the modifier
    /// cannot be represented.
    /// </summary>
    public string? SelectFragment(ModifierRequirement requirement, string? itemClass)
    {
        var mod = _corpus.FindMod(requirement.Value);
        if (mod is null) return null;
        if (mod.StatLines.Count == 0) return null;

        // Hybrid mods have more than one line. The selector is scope-aware and
        // validates each line against the real copy text; keep the shortest.
        string? best = null;
        StatLineEntry? bestLine = null;
        foreach (var line in mod.StatLines)
        {
            var fragment = _selector.Select(line.Pattern, itemClass);
            if (fragment is null) continue;
            if (best is null || fragment.Length < best.Length)
            {
                best = fragment;
                bestLine = line;
            }
        }
        if (best is null || bestLine is null) return null;

        if (requirement.MinValue is not null)
            return BuildThresholdFragment(bestLine, requirement.MinValue.Value, itemClass, best);
        return best;
    }

    private bool IsFragmentSafe(string? itemClass, string fragment, string pattern)
    {
        var allowed = new HashSet<string> { pattern };
        return itemClass is not null
            ? _corpus.RegexSafeOnClass(itemClass, fragment, allowed)
            : _corpus.GlobalRegexSafe(fragment, allowed);
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
    public static IReadOnlyList<string> FactorCommonPrefixes(IReadOnlyList<string> fragments)
    {
        if (fragments.Count < 2) return fragments;
        var distinct = fragments.Distinct().OrderBy(s => s, StringComparer.Ordinal).ToList();

        // Group by the first character; within a group find the longest common prefix
        // of all members. If shared prefix is >= 2 chars and alternation is shorter,
        // factor it.
        var result = new List<string>();
        var groups = distinct.GroupBy(s => s.Length > 0 ? s[0] : '\0').OrderBy(g => g.Key);
        foreach (var group in groups)
        {
            var items = group.ToList();
            if (items.Count < 2)
            {
                result.AddRange(items);
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
                if (factored.Length < union.Length) result.Add(factored);
                else result.AddRange(items);
            }
            else
            {
                result.AddRange(items);
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

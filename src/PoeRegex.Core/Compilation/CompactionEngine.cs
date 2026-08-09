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

    public CompactionEngine(EngineCorpus corpus, ICompilerStrategy strategy)
    {
        _corpus = corpus;
        _strategy = strategy;
    }

    /// <summary>
    /// Selects the regex fragment for a single modifier requirement on the
    /// selected class. Returns null when the modifier cannot be represented.
    /// </summary>
    public string? SelectFragment(ModifierRequirement requirement, string? itemClass)
    {
        var mod = _corpus.FindMod(requirement.Value);
        if (mod is null) return null;

        // Choose the mod's single best stat line: prefer one present on the class.
        var lines = itemClass is not null
            ? mod.StatLines.Where(sl => sl.Fingerprints.ContainsKey(itemClass)).ToList()
            : mod.StatLines.ToList();
        if (lines.Count == 0) lines = mod.StatLines.ToList();
        if (lines.Count == 0) return null;

        // Prefer the shortest placeholder-free fingerprint so the emitted fragment
        // stays a plain literal substring that trade search matches directly.
        string? best = null;
        StatLineEntry? bestLine = null;
        foreach (var line in lines)
        {
            var fp = GetFingerprint(mod.Id, line.Pattern, itemClass, avoidPlaceholder: true);
            if (fp is null) continue;
            if (best is null || fp.Length < best.Length)
            {
                best = fp;
                bestLine = line;
            }
        }

        // Escape any literal regex metacharacters so a plain fingerprint matches
        // literally rather than being interpreted as regex syntax. This must happen
        // before the fallback path, which replaces best with an already-escaped
        // regex fragment (its \d+ quantifier must not be escaped again).
        if (best is not null && bestLine is not null)
        {
            best = _strategy.EscapeLiteral(best);
        }

        // No line has a placeholder-free unique substring: fall back to the shortest
        // fingerprint, expanding digit placeholders into a digit-run matcher and
        // re-validating uniqueness against real copy text.
        if (best is null)
        {
            foreach (var line in lines)
            {
                var fp = GetFingerprint(mod.Id, line.Pattern, itemClass, avoidPlaceholder: false);
                if (fp is null || !ContainsPlaceholder(fp)) continue;
                var fragment = ExpandPlaceholders(fp);
                if (fragment is null) continue;
                if (!IsFragmentSafe(itemClass, fragment, line.Pattern)) continue;
                best = fragment;
                bestLine = line;
                break;
            }
            if (best is null) return null;
        }

        if (requirement.MinValue is not null && best is not null && bestLine is not null)
        {
            return BuildThresholdFragment(bestLine, requirement.MinValue.Value, itemClass, best);
        }
        return best;
    }

    private string? GetFingerprint(string modId, string linePattern, string? itemClass, bool avoidPlaceholder) =>
        itemClass is not null
            ? _corpus.Fingerprint(modId, linePattern, itemClass, avoidPlaceholder, caseInsensitive: true)
            : _corpus.GlobalFingerprint(modId, linePattern, avoidPlaceholder, caseInsensitive: true);

    private bool IsFragmentSafe(string? itemClass, string fragment, string pattern)
    {
        var allowed = new HashSet<string> { pattern };
        return itemClass is not null
            ? _corpus.RegexSafeOnClass(itemClass, fragment, allowed)
            : _corpus.GlobalRegexSafe(fragment, allowed);
    }

    private static bool ContainsPlaceholder(string s) => s.IndexOf(SuffixIndex.DigitPlaceholder) >= 0;

    /// <summary>
    /// Replaces every digit placeholder in a fragment with a digit-run matcher and
    /// escapes regex metacharacters, producing a literal-matching regex fragment.
    /// </summary>
    private string? ExpandPlaceholders(string fragment)
    {
        var sb = new System.Text.StringBuilder(fragment.Length + 8);
        var literalStart = 0;
        for (var i = 0; i < fragment.Length; i++)
        {
            if (fragment[i] != SuffixIndex.DigitPlaceholder) continue;
            if (i > literalStart) sb.Append(_strategy.EscapeLiteral(fragment.Substring(literalStart, i - literalStart)));
            sb.Append(@"\d+");
            literalStart = i + 1;
        }
        if (literalStart < fragment.Length) sb.Append(_strategy.EscapeLiteral(fragment.Substring(literalStart)));
        return sb.ToString();
    }

    /// <summary>
    /// Builds a fragment matching values >= minValue by replacing the value token
    /// in the line's pattern with a digit-range alternation, then re-validating
    /// uniqueness on the class corpus against real copy text.
    /// </summary>
    private string? BuildThresholdFragment(StatLineEntry line, double minValue, string? itemClass, string fallbackFp)
    {
        var pattern = line.Pattern;
        var idx = pattern.IndexOf(SuffixIndex.DigitPlaceholder);
        if (idx < 0) return fallbackFp;

        var digitRange = DigitRangeRegex.Build((int)Math.Ceiling(minValue));
        if (digitRange is null) return fallbackFp;

        // Escape literal characters; the value token becomes the digit-range alternation.
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < pattern.Length; i++)
        {
            if (i == idx)
            {
                sb.Append("(").Append(digitRange).Append(')');
            }
            else
            {
                var c = pattern[i];
                if (c == SuffixIndex.DigitPlaceholder)
                {
                    sb.Append("(\\d)");
                }
                else
                {
                    if (System.Text.RegularExpressions.Regex.IsMatch(c.ToString(), @"[\\.+*?()\[\]{}$|]"))
                        sb.Append('\\');
                    sb.Append(c);
                }
            }
        }
        var fragment = sb.ToString();

        // Re-validate against real copy text: must match at least one line of this
        // pattern and no line of any other pattern in the class corpus.
        if (IsFragmentSafe(itemClass, fragment, pattern))
        {
            return fragment;
        }
        return fallbackFp;
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
            var suffix = prefix.Length >= 2 && items.All(s => s.Length > prefix.Length)
                ? string.Join("|", items.Select(s => s.Substring(prefix.Length)))
                : null;
            if (suffix is not null)
            {
                var factored = prefix + "(" + suffix + ")";
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

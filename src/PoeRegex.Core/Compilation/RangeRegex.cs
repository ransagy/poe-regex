using System.Globalization;
using System.Text;

namespace PoeRegex.Core.Compilation;

/// <summary>
/// Builds short regex candidates for a numeric value range. Candidates are
/// deliberately allowed to over-match (for example <c>[12]\d\d</c> for
/// 100–214); the caller proves each one against the catalog before using it.
/// Integer candidates use character classes and ranges; decimal candidates
/// preserve the displayed precision and allow one extra fractional digit so
/// a game roll such as 2.03 still matches a 1.5–2.5 stat.
/// </summary>
public static class RangeRegex
{
    private const int MaxIterate = 10_000;
    private const int MaxCandidates = 128;

    /// <summary>Shortest-first candidate value regexes for the given inclusive intervals.</summary>
    public static IReadOnlyList<string> Candidates(
        IReadOnlyList<(double Lo, double Hi)> intervals, int precision)
    {
        var normalized = Normalize(intervals);
        if (normalized.Count == 0) return [];

        var candidates = new HashSet<string>(StringComparer.Ordinal);
        if (precision <= 0)
        {
            foreach (var candidate in IntegerCandidates(normalized))
                candidates.Add(candidate);
        }
        else
        {
            foreach (var candidate in DecimalCandidates(normalized, Math.Clamp(precision, 1, 3)))
                candidates.Add(candidate);
        }

        return candidates
            .Where(c => c.Length > 0)
            .OrderBy(c => c.Length)
            .ThenBy(c => c, StringComparer.Ordinal)
            .Take(MaxCandidates)
            .ToList();
    }

    /// <summary>True when the candidate matches every representative value in the intervals.</summary>
    public static bool MatchesIntervals(
        string candidate, IReadOnlyList<(double Lo, double Hi)> intervals, int precision)
    {
        var regex = new System.Text.RegularExpressions.Regex(
            "^(?:" + candidate + ")$",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        foreach (var (lo, hi) in Normalize(intervals))
        {
            foreach (var value in RepresentativeValues(lo, hi, precision))
            {
                var text = FormatValue(value, precision);
                if (!regex.IsMatch(text)) return false;
            }
            if (precision > 0 && hi > lo)
            {
                // The game can show more fractional digits than the range
                // endpoints (a 1.5–2.5 stat rolled 2.03). A candidate that
                // only accepts one decimal must not pass.
                var extra = Math.Round(lo + (hi - lo) * 0.37, Math.Min(precision + 1, 3));
                if (extra > lo && extra < hi)
                {
                    var text = extra.ToString(
                        "0." + new string('#', Math.Min(precision + 1, 3)),
                        CultureInfo.InvariantCulture);
                    if (!regex.IsMatch(text)) return false;
                }
            }
        }
        return true;
    }

    /// <summary>Decimal places in a rendered value, capped for regex generation.</summary>
    public static int PrecisionOf(double value)
    {
        var text = value.ToString("0.############", CultureInfo.InvariantCulture);
        var dot = text.IndexOf('.');
        return dot < 0 ? 0 : text.Length - dot - 1;
    }

    private static List<(double Lo, double Hi)> Normalize(
        IReadOnlyList<(double Lo, double Hi)> intervals)
    {
        var ordered = intervals
            .Where(x => !double.IsNaN(x.Lo) && !double.IsNaN(x.Hi) && x.Hi >= x.Lo)
            .OrderBy(x => x.Lo)
            .ThenBy(x => x.Hi)
            .ToList();
        var merged = new List<(double Lo, double Hi)>(ordered.Count);
        foreach (var current in ordered)
        {
            if (merged.Count == 0)
            {
                merged.Add(current);
                continue;
            }
            var last = merged[^1];
            if (current.Lo <= last.Hi + 1e-9)
                merged[^1] = (last.Lo, Math.Max(last.Hi, current.Hi));
            else
                merged.Add(current);
        }
        return merged;
    }

    private static IEnumerable<double> RepresentativeValues(double lo, double hi, int precision)
    {
        yield return lo;
        if (hi > lo)
        {
            yield return hi;
            yield return Math.Round((lo + hi) / 2, Math.Min(precision, 2));
        }
    }

    private static string FormatValue(double value, int precision) =>
        precision <= 0
            ? Math.Round(value).ToString("0", CultureInfo.InvariantCulture)
            : value.ToString("0." + new string('#', Math.Max(1, precision)), CultureInfo.InvariantCulture);

    private static IEnumerable<string> IntegerCandidates(List<(double Lo, double Hi)> intervals)
    {
        var values = new List<long>();
        var allPoints = true;
        foreach (var (lo, hi) in intervals)
        {
            var a = (long)Math.Ceiling(lo - 1e-9);
            var b = (long)Math.Floor(hi + 1e-9);
            if (b < a) continue;
            if (a != b) allPoints = false;
            if (b - a <= MaxIterate)
            {
                for (var v = a; v <= b; v++) values.Add(v);
            }
        }

        if (allPoints && values.Count > 0)
        {
            var distinct = values.Distinct().OrderBy(x => x).ToList();
            yield return string.Join("|", distinct);
            foreach (var candidate in SetClassCandidates(distinct))
                yield return candidate;
        }

        var perInterval = intervals
            .Select(interval => IntervalAlternatives(
                (long)Math.Ceiling(interval.Lo - 1e-9),
                (long)Math.Floor(interval.Hi + 1e-9)))
            .Where(list => list.Count > 0)
            .ToList();
        if (perInterval.Count == 0) yield break;

        // Combine the same kind of alternative across intervals, so a
        // multi-tier range gets one value regex instead of one per interval.
        var maxCount = perInterval.Max(list => list.Count);
        for (var i = 0; i < maxCount; i++)
        {
            var parts = perInterval
                .Select(list => i < list.Count ? list[i] : list[^1])
                .ToList();
            yield return string.Join("|", parts);
        }

        // Width-specific alternatives for a single interval, and the
        // individual interval forms as fallbacks.
        foreach (var alternative in perInterval.SelectMany(list => list))
            yield return alternative;

        // A single loose span can be shorter than an alternation of exact
        // intervals; the caller's catalog proof rejects it when a gap value
        // really exists.
        var spanLo = (long)Math.Ceiling(intervals[0].Lo - 1e-9);
        var spanHi = (long)Math.Floor(intervals[^1].Hi + 1e-9);
        foreach (var candidate in IntervalAlternatives(spanLo, spanHi))
            yield return candidate;
        if (intervals.Count > 1 && spanHi - spanLo <= MaxIterate)
        {
            var span = new List<long>();
            for (var v = spanLo; v <= spanHi; v++) span.Add(v);
            yield return string.Join("|", span);
            foreach (var candidate in SetClassCandidates(span))
                yield return candidate;
        }
    }

    /// <summary>
    /// Complete value regexes for one interval. The first three combine all
    /// digit widths (loose, full, tight), so 9–12 yields <c>9|1[0-2]</c>.
    /// </summary>
    private static List<string> IntervalAlternatives(long lo, long hi)
    {
        if (hi < lo) return [];
        var widths = SplitByWidth(lo, hi).ToList();
        if (widths.Count == 0) return [];

        var loose = widths.Select(w => LooseClass(w.Lo, w.Hi, w.Width)).ToList();
        var full = widths.Select(w => RepeatDigit(w.Width)).ToList();
        var tight = widths.SelectMany(w => TightRange(w.Lo, w.Hi, w.Width)).ToList();

        var result = new List<string>
        {
            string.Join("|", loose),
            string.Join("|", full),
            string.Join("|", tight),
        };
        result.AddRange(loose);
        result.AddRange(full);
        result.AddRange(tight);
        return result.Distinct(StringComparer.Ordinal).ToList();
    }

    private static IEnumerable<string> SetClassCandidates(IReadOnlyList<long> values)
    {
        foreach (var group in values.GroupBy(v => v.ToString(CultureInfo.InvariantCulture).Length))
        {
            var width = group.Key;
            var exact = DigitsClass(group.Select(v => v.ToString(CultureInfo.InvariantCulture)), exactDigits: true);
            var loose = DigitsClass(group.Select(v => v.ToString(CultureInfo.InvariantCulture)), exactDigits: false);
            if (exact is not null) yield return exact;
            if (loose is not null && loose != exact) yield return loose;
            yield return RepeatDigit(width);
        }
    }

    /// <summary>Per-position class over the numbers actually present, or their min/max span.</summary>
    private static string? DigitsClass(IEnumerable<string> numbers, bool exactDigits)
    {
        var list = numbers.ToList();
        if (list.Count == 0) return null;
        var width = list[0].Length;
        if (list.Any(n => n.Length != width)) return null;
        var sb = new StringBuilder();
        for (var i = 0; i < width; i++)
        {
            var digits = list.Select(n => n[i] - '0').Distinct().OrderBy(d => d).ToList();
            if (!exactDigits)
            {
                var min = digits[0];
                var max = digits[^1];
                digits = Enumerable.Range(min, max - min + 1).ToList();
            }
            if (digits.Count == 10)
                sb.Append(@"\d");
            else if (digits.Count == 1)
                sb.Append((char)('0' + digits[0]));
            else
                sb.Append('[').Append(string.Concat(digits.Select(d => (char)('0' + d)))).Append(']');
        }
        return sb.ToString();
    }

    /// <summary>Loose per-position class over every number in the interval.</summary>
    private static string LooseClass(long lo, long hi, int width)
    {
        var size = hi - lo;
        if (size <= MaxIterate)
        {
            var numbers = new List<string>((int)size + 1);
            for (var v = lo; v <= hi; v++)
                numbers.Add(v.ToString(CultureInfo.InvariantCulture).PadLeft(width, '0'));
            return DigitsClass(numbers, exactDigits: false) ?? RepeatDigit(width);
        }
        // Large ranges: first digit range, then any digit. The catalog proof
        // rejects it when it is too broad.
        var loStr = lo.ToString(CultureInfo.InvariantCulture).PadLeft(width, '0');
        var hiStr = hi.ToString(CultureInfo.InvariantCulture).PadLeft(width, '0');
        var first = loStr[0] == hiStr[0]
            ? loStr[0].ToString()
            : $"[{loStr[0]}-{hiStr[0]}]";
        return first + RepeatDigit(width - 1);
    }

    private static IEnumerable<(int Width, long Lo, long Hi)> SplitByWidth(long lo, long hi)
    {
        for (var width = Digits(lo); width <= Digits(hi); width++)
        {
            var min = width == 1 ? 0 : Pow10(width - 1);
            var max = Pow10(width) - 1;
            var a = Math.Max(lo, min);
            var b = Math.Min(hi, max);
            if (a <= b) yield return (width, a, b);
        }
    }

    private static List<string> TightRange(long lo, long hi, int width)
    {
        if (width <= 0) return [""];
        if (lo == 0 && hi == Pow10(width) - 1)
            return [RepeatDigit(width)];
        var loStr = lo.ToString(CultureInfo.InvariantCulture).PadLeft(width, '0');
        var hiStr = hi.ToString(CultureInfo.InvariantCulture).PadLeft(width, '0');
        if (loStr[0] == hiStr[0])
        {
            var restLo = lo - (loStr[0] - '0') * Pow10(width - 1);
            var restHi = hi - (hiStr[0] - '0') * Pow10(width - 1);
            return TightRange(restLo, restHi, width - 1)
                .Select(rest => loStr[0] + rest)
                .ToList();
        }

        var result = new List<string>();
        var loDigit = loStr[0] - '0';
        var hiDigit = hiStr[0] - '0';
        var loRest = lo - loDigit * Pow10(width - 1);
        var hiRest = hi - hiDigit * Pow10(width - 1);

        result.AddRange(TightRange(loRest, Pow10(width - 1) - 1, width - 1)
            .Select(rest => loStr[0] + rest));
        if (loDigit + 1 <= hiDigit - 1)
        {
            var middle = loDigit + 1 == hiDigit - 1
                ? ((char)('0' + loDigit + 1)).ToString()
                : $"[{loDigit + 1}-{hiDigit - 1}]";
            result.Add(middle + RepeatDigit(width - 1));
        }
        result.AddRange(TightRange(0, hiRest, width - 1)
            .Select(rest => hiStr[0] + rest));
        return result;
    }

    private static IEnumerable<string> DecimalCandidates(
        List<(double Lo, double Hi)> intervals, int precision)
    {
        var maxPrecision = Math.Max(
            precision,
            intervals.Max(x => Math.Max(PrecisionOf(x.Lo), PrecisionOf(x.Hi))));

        for (var p = 1; p <= Math.Min(3, maxPrecision); p++)
        {
            var scale = Pow10(p);
            foreach (var (lo, hi) in intervals)
            {
                var a = (long)Math.Ceiling(lo * scale - 1e-9);
                var b = (long)Math.Floor(hi * scale + 1e-9);
                if (b < a) continue;
                var width = Math.Max(
                    Math.Max(a.ToString(CultureInfo.InvariantCulture).Length,
                             b.ToString(CultureInfo.InvariantCulture).Length),
                    p + 1);

                foreach (var integer in DecimalIntegerAlternatives(a, b, width))
                {
                    var transformed = InsertDecimal(integer, width, p);
                    if (transformed is null) continue;
                    yield return transformed;
                    yield return MakeLastDigitOptional(transformed);
                }
            }

            // A loose fractional tail: the integer part is bounded, the
            // fractional digits are free. This is often the shortest form and
            // still matches a game roll with more precision than the range
            // endpoints (2.03 in a 1.5–2.5 stat).
            foreach (var (lo, hi) in intervals)
            {
                var intLo = (long)Math.Floor(lo + 1e-9);
                var intHi = (long)Math.Floor(hi + 1e-9);
                if (intHi < intLo) continue;
                var intWidth = Math.Max(
                    intLo.ToString(CultureInfo.InvariantCulture).Length,
                    intHi.ToString(CultureInfo.InvariantCulture).Length);
                var intPart = LooseClass(intLo, intHi, intWidth);
                yield return intPart + @"\.\d+";

                // An integer roll can render without a decimal point (2.0
                // shows as "2"), while a fractional roll can carry more
                // digits than the endpoints. Offer both shapes.
                var integers = new List<string>();
                for (var n = Math.Max(intLo, (long)Math.Ceiling(lo - 1e-9));
                     n <= Math.Min(intHi, (long)Math.Floor(hi + 1e-9));
                     n++)
                {
                    integers.Add(n.ToString(CultureInfo.InvariantCulture));
                }
                if (integers.Count > 0)
                {
                    yield return string.Join("|", integers) + "|" + intPart + @"\.\d+";
                    yield return intPart + @"(\.\d+)?";
                }
            }
        }
    }

    private static IEnumerable<string> DecimalIntegerAlternatives(long lo, long hi, int width)
    {
        yield return LooseClass(lo, hi, width);
        yield return RepeatDigit(width);
        foreach (var tight in TightRange(lo, hi, width))
            yield return tight;
    }

    private static string? InsertDecimal(string alternative, int width, int precision)
    {
        var tokens = Tokenize(alternative);
        if (tokens.Count != width || precision <= 0 || precision >= width) return null;
        var split = width - precision;
        var sb = new StringBuilder();
        for (var i = 0; i < tokens.Count; i++)
        {
            if (i == split) sb.Append(@"\.");
            sb.Append(tokens[i]);
        }
        return sb.ToString();
    }

    private static string MakeLastDigitOptional(string alternative)
    {
        var tokens = Tokenize(alternative);
        if (tokens.Count == 0) return alternative;
        var last = tokens[^1];
        if (last == @"\.") return alternative;
        tokens[^1] = last + "?";
        return string.Concat(tokens);
    }

    private static List<string> Tokenize(string alternative)
    {
        var tokens = new List<string>();
        for (var i = 0; i < alternative.Length; i++)
        {
            var c = alternative[i];
            if (c == '\\' && i + 1 < alternative.Length)
            {
                tokens.Add(alternative.Substring(i, 2));
                i++;
            }
            else if (c == '[')
            {
                var end = alternative.IndexOf(']', i + 1);
                if (end < 0)
                {
                    tokens.Add(alternative[i..]);
                    break;
                }
                tokens.Add(alternative.Substring(i, end - i + 1));
                i = end;
            }
            else
            {
                tokens.Add(c.ToString());
            }
        }
        return tokens;
    }

    private static string RepeatDigit(int count) =>
        count <= 0 ? "" : string.Concat(Enumerable.Repeat(@"\d", count));

    private static int Digits(long value)
    {
        if (value <= 0) return 1;
        var digits = 0;
        while (value > 0)
        {
            digits++;
            value /= 10;
        }
        return digits;
    }

    private static long Pow10(int exponent)
    {
        long result = 1;
        for (var i = 0; i < exponent; i++) result *= 10;
        return result;
    }
}

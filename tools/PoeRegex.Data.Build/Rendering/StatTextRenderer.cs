using PoeRegex.Data.Build.RePoE;

namespace PoeRegex.Data.Build.Rendering;

/// <summary>
/// Renders RePoE mod stats into human-readable copy-text lines, mirroring the
/// in-game advanced mod description (English client).
///
/// Algorithm ported from PyPoE's TranslationString.format_string:
/// 1. pick the highest-scoring translation variant whose condition ranges match the values,
/// 2. apply index_handlers (multiplier → divisor → addend, then precision),
/// 3. substitute formatted values into the template.
/// </summary>
public sealed class StatTextRenderer
{
    private readonly Dictionary<string, RePoESchema.StatValueHandler> _handlers;
    private readonly Dictionary<string, List<RePoESchema.StatTranslationEntry>> _translationsByStat;

    public StatTextRenderer(
        Dictionary<string, RePoESchema.StatValueHandler> handlers,
        IEnumerable<RePoESchema.StatTranslationEntry> translations)
    {
        _handlers = handlers;
        _translationsByStat = new Dictionary<string, List<RePoESchema.StatTranslationEntry>>();
        foreach (var entry in translations)
        {
            foreach (var id in entry.Ids)
            {
                if (!_translationsByStat.TryGetValue(id, out var list))
                {
                    list = new List<RePoESchema.StatTranslationEntry>();
                    _translationsByStat[id] = list;
                }
                list.Add(entry);
            }
        }
    }

    public sealed record RenderedLine(string Line, string Pattern, double MinValue, double MaxValue);

    /// <summary>
    /// Renders a mod's stats. Multi-stat mods may produce a single line (when a
    /// translation covers several stat indices) or several lines. Returns the
    /// distinct rendered lines.
    /// </summary>
    public List<RenderedLine> RenderMod(IReadOnlyList<RePoESchema.ModStat> stats)
    {
        var lines = new List<RenderedLine>();
        var used = new bool[stats.Count];

        // Group stats that belong to the same translation entry together.
        for (var i = 0; i < stats.Count; i++)
        {
            if (used[i]) continue;
            if (!_translationsByStat.TryGetValue(stats[i].Id, out var candidates))
            {
                continue; // no translation available for this stat id
            }

            foreach (var entry in candidates)
            {
                if (!TryRenderEntry(entry, stats, i, used, out var line)) continue;
                if (line is not null) lines.Add(line);
                break;
            }
        }

        return lines;
    }

    /// <summary>
    /// Attempts to render the stats covered by <paramref name="entry"/> starting at
    /// <paramref name="startIndex"/>. Marks consumed indices in <paramref name="used"/>.
    /// </summary>
    private bool TryRenderEntry(
        RePoESchema.StatTranslationEntry entry,
        IReadOnlyList<RePoESchema.ModStat> stats,
        int startIndex,
        bool[] used,
        out RenderedLine? line)
    {
        line = null;
        var n = entry.Ids.Count;
        var indices = new List<int>(n);
        var values = new List<(double Min, double Max)>(n);
        var testValues = new List<double>(n);

        var cursor = startIndex;
        foreach (var expectedId in entry.Ids)
        {
            // Find the next unused stat matching the expected id.
            var found = -1;
            for (var j = cursor; j < stats.Count; j++)
            {
                if (used[j]) continue;
                if (stats[j].Id == expectedId) { found = j; break; }
            }
            if (found < 0) return false;

            indices.Add(found);
            var stat = stats[found];
            values.Add((stat.Min, stat.Max));
            testValues.Add(stat.Max != 0 ? stat.Max : stat.Min);
            used[found] = true;
            cursor = found + 1;
        }

        var variant = SelectVariant(entry.English, testValues);
        if (variant is null)
        {
            // No variant matches; release the indices.
            foreach (var idx in indices) used[idx] = false;
            return false;
        }

        line = FormatVariant(variant, values);
        return true;
    }

    private RePoESchema.TranslationVariant? SelectVariant(
        IReadOnlyList<RePoESchema.TranslationVariant> variants,
        IReadOnlyList<double> testValues)
    {
        RePoESchema.TranslationVariant? best = null;
        var bestRating = 0;
        foreach (var v in variants)
        {
            var rating = MatchRange(v.Condition, testValues);
            if (rating > bestRating)
            {
                bestRating = rating;
                best = v;
            }
        }
        return bestRating > 0 ? best : null;
    }

    private static int MatchRange(IReadOnlyList<RePoESchema.Condition> ranges, IReadOnlyList<double> values)
    {
        var rating = 0;
        for (var i = 0; i < values.Count; i++)
        {
            var r = i < ranges.Count ? ranges[i] : new RePoESchema.Condition();
            rating += InRange(r, values[i]);
        }
        return rating;
    }

    private static int InRange(RePoESchema.Condition r, double value)
    {
        if (r.Min is null && r.Max is null) return 1;
        if (r.Negated == true)
        {
            var ok = (r.Min is null || value > r.Min.Value) || (r.Max is null || value > r.Max.Value);
            return ok ? 2 : -10000;
        }
        if (r.Min is null) return value <= r.Max ? 2 : -10000;
        if (r.Max is null) return value >= r.Min ? 2 : -10000;
        return value >= r.Min.Value && value <= r.Max.Value ? 3 : -10000;
    }

    private RenderedLine? FormatVariant(
        RePoESchema.TranslationVariant variant,
        IReadOnlyList<(double Min, double Max)> values)
    {
        // Apply index_handlers to each value.
        var handledMin = new double?[values.Count];
        var handledMax = new double?[values.Count];
        var specs = new DisplaySpec[values.Count];
        for (var i = 0; i < values.Count; i++)
        {
            var handlers = i < variant.IndexHandlers.Count ? variant.IndexHandlers[i] : [];
            var format = i < variant.Format.Count ? variant.Format[i] : "#";
            if (format == "ignore")
            {
                continue;
            }
            var (minV, minSpec) = ApplyHandlers(values[i].Min, handlers);
            var (maxV, maxSpec) = ApplyHandlers(values[i].Max, handlers);
            handledMin[i] = minV;
            handledMax[i] = maxV;
            specs[i] = maxSpec.Precision >= minSpec.Precision ? maxSpec : minSpec;
        }

        var template = variant.String;
        var text = new List<char>(template.Length + 16);
        for (var i = 0; i < template.Length; i++)
        {
            var c = template[i];
            if (c != '{')
            {
                text.Add(c);
                continue;
            }
            var close = template.IndexOf('}', i);
            if (close < 0) { text.Add(c); continue; }
            var token = template.Substring(i + 1, close - i - 1);
            if (int.TryParse(token, out var idx) && idx < values.Count)
            {
                var format = idx < variant.Format.Count ? variant.Format[idx] : "#";
                if (format != "ignore" && handledMax[idx] is not null)
                {
                    // Items display a single rolled value; use the max roll.
                    text.AddRange(FormatNumber(handledMax[idx]!.Value, format, specs[idx]));
                }
                i = close;
            }
            else
            {
                text.Add(c);
            }
        }
        var rendered = new string(text.ToArray());
        if (rendered.Length == 0) return null;

        // PoE2 markup: [X] attribute markers and [X|Y] language alternatives
        // both render as plain text in-game; keep the first alternative.
        rendered = StripBracketMarkup(rendered);

        // Value-agnostic pattern: replace every digit-run with the placeholder.
        var pattern = NormalizeDigits(rendered);

        // Representative value span for threshold patterns (after handlers).
        var globalMin = double.MaxValue;
        var globalMax = double.MinValue;
        for (var i = 0; i < values.Count; i++)
        {
            var handlers = i < variant.IndexHandlers.Count ? variant.IndexHandlers[i] : [];
            var format = i < variant.Format.Count ? variant.Format[i] : "#";
            if (format == "ignore" || handledMin[i] is null) continue;
            globalMin = Math.Min(globalMin, handledMin[i]!.Value);
            globalMax = Math.Max(globalMax, handledMax[i]!.Value);
        }
        if (globalMax < globalMin) { globalMin = 0; globalMax = 0; }

        return new RenderedLine(rendered, pattern, globalMin, globalMax);
    }

    private (double Value, DisplaySpec Spec) ApplyHandlers(double value, IReadOnlyList<string> handlerNames)
    {
        var spec = DisplaySpec.Default;
        foreach (var name in handlerNames)
        {
            if (!_handlers.TryGetValue(name, out var h)) continue;
            switch (h.Type)
            {
                case "int":
                    if (h.Multiplier is { } m) value *= m;
                    if (h.Divisor is { } d) value /= d;
                    if (h.Addend is { } a) value += a;
                    if (h.Precision is { } p)
                    {
                        value = Math.Round(value, p);
                        spec = new DisplaySpec(p, h.Fixed == true);
                    }
                    break;
                case "relational":
                case "noop":
                case "string":
                    break;
            }
        }
        return (value, spec);
    }

    private static string FormatNumber(double value, string format, DisplaySpec spec)
    {
        var v = spec.Apply(value);
        var s = v;
        if (format.Contains('+') && value > 0) s = "+" + s;
        if (format.Contains('-') && value < 0) s = "-" + s;
        if (format.Contains('%')) s += "%";
        return s;
    }

    private readonly record struct DisplaySpec(int Precision, bool Fixed)
    {
        public static DisplaySpec Default => new(0, false);

        public string Apply(double v)
        {
            if (Fixed)
            {
                return v.ToString("F" + Precision, Culture);
            }
            var rounded = Math.Round(v, Precision);
            if (Math.Abs(rounded - Math.Truncate(rounded)) < 1e-9)
            {
                return ((long)rounded).ToString(Culture);
            }
            return rounded.ToString("0." + new string('#', Precision), Culture);
        }
    }

    private static readonly System.Globalization.CultureInfo Culture = System.Globalization.CultureInfo.InvariantCulture;

    /// <summary>
    /// Strips PoE2 bracket markup: "[X]" attribute markers and "[X|Y]" language
    /// alternatives both render as plain text in-game; the first alternative is kept.
    /// </summary>
    public static string StripBracketMarkup(string s)
    {
        if (!s.Contains('[')) return s;
        var sb = new System.Text.StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] == '[')
            {
                var close = s.IndexOf(']', i);
                if (close < 0)
                {
                    sb.Append(s[i]);
                    continue;
                }
                var inner = s.Substring(i + 1, close - i - 1);
                var pipe = inner.IndexOf('|');
                var keep = pipe >= 0 ? inner.Substring(0, pipe) : inner;
                sb.Append(keep);
                i = close;
            }
            else
            {
                sb.Append(s[i]);
            }
        }
        return sb.ToString();
    }

    /// <summary>Replaces every digit-run (incl. decimals) with a single placeholder char.</summary>
    public static string NormalizeDigits(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        var inDigit = false;
        foreach (var c in s)
        {
            if (char.IsAsciiDigit(c))
            {
                if (!inDigit)
                {
                    sb.Append(SuffixIndexPlaceholder);
                    inDigit = true;
                }
            }
            else
            {
                if (inDigit) inDigit = false;
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    /// <summary>Placeholder char used in value-agnostic patterns. Must match Core's SuffixIndex.</summary>
    public const char SuffixIndexPlaceholder = '\x01';
}

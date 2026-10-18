// Stat-text rendering for the catalog build tool.
//
// Input is RePoE's stat_translations / stat_descriptions export, whose shape is
// documented by the RePoE project (RePoE/docs/stat_translations.md): an entry
// bundles one or more stat ids with a list of English variants, and each
// variant carries one condition per id, a value format per id, the
// index_handlers to run, and a template string with {i} placeholders.
//
// This is an independent implementation of that documented format, written for
// this project and pinned by the shipped catalogs plus the renderer tests.
// PyPoE's TranslationString (GPL-3.0) is the best-known prior renderer for the
// same data; it informed the expected behaviour only and no PyPoE code is
// incorporated here. See NOTICE and docs/PROVENANCE.md.

using System.Globalization;
using System.Text;
using PoeRegex.Data.Build.RePoE;

namespace PoeRegex.Data.Build.Rendering;

/// <summary>
/// Turns a mod's raw stat values into the English lines shown on an item,
/// mirroring the in-game advanced mod description.
/// </summary>
public sealed class StatTextRenderer
{
    private const string DefaultFormat = "#";

    private readonly IReadOnlyDictionary<string, RePoESchema.StatValueHandler> _valueHandlers;
    private readonly Dictionary<string, List<RePoESchema.StatTranslationEntry>> _entriesByStatId;

    public StatTextRenderer(
        Dictionary<string, RePoESchema.StatValueHandler> handlers,
        IEnumerable<RePoESchema.StatTranslationEntry> translations)
    {
        _valueHandlers = handlers;
        _entriesByStatId = new Dictionary<string, List<RePoESchema.StatTranslationEntry>>();
        foreach (var entry in translations)
        {
            foreach (var id in entry.Ids)
            {
                if (!_entriesByStatId.TryGetValue(id, out var bucket))
                {
                    bucket = [];
                    _entriesByStatId[id] = bucket;
                }
                bucket.Add(entry);
            }
        }
    }

    public sealed record RenderedLine(string Line, string RangeLine, string Pattern, double MinValue, double MaxValue);

    /// <summary>
    /// Renders every stat a mod carries. One translation entry can cover
    /// several stat ids and produce a single line; stats without an entry are
    /// skipped. The plain line shows the max roll (the item a mod spawns
    /// always rolls inside its tier), while <see cref="RenderedLine.RangeLine"/>
    /// keeps the tier's min–max span.
    /// </summary>
    public List<RenderedLine> RenderMod(IReadOnlyList<RePoESchema.ModStat> stats)
    {
        var rendered = new List<RenderedLine>();
        var consumed = new bool[stats.Count];

        for (var start = 0; start < stats.Count; start++)
        {
            if (consumed[start]) continue;
            if (!_entriesByStatId.TryGetValue(stats[start].Id, out var entries)) continue;

            foreach (var entry in entries)
            {
                var match = MatchEntry(entry, stats, start, consumed);
                if (match is null) continue;

                if (match.Line is not null) rendered.Add(match.Line);
                foreach (var index in match.Consumed) consumed[index] = true;
                break;
            }
        }

        return rendered;
    }

    /// <summary>
    /// Pairs an entry's ids with the mod's stats in order and builds its line.
    /// Returns null when the entry cannot be satisfied, in which case no stat
    /// is consumed (the caller may try the next candidate entry).
    /// </summary>
    private EntryMatch? MatchEntry(
        RePoESchema.StatTranslationEntry entry,
        IReadOnlyList<RePoESchema.ModStat> stats,
        int start,
        bool[] consumed)
    {
        var count = entry.Ids.Count;
        var picked = new bool[stats.Count];
        var claimed = new List<int>(count);
        var values = new (double Min, double Max)[count];
        var probes = new double[count];
        var missing = new bool[count];

        var next = start;
        for (var slot = 0; slot < count; slot++)
        {
            var wanted = entry.Ids[slot];
            var found = -1;
            for (var i = next; i < stats.Count; i++)
            {
                if (consumed[i] || picked[i]) continue;
                if (stats[i].Id == wanted) { found = i; break; }
            }

            if (found < 0)
            {
                if (!HasStat(stats, wanted))
                {
                    // Entries can pair ids that only some mods carry (for
                    // example the "no physical damage" flag alongside
                    // local_physical_damage_+%). An id the mod does not carry
                    // takes part as zero and renders nothing.
                    values[slot] = (0, 0);
                    probes[slot] = 0;
                    missing[slot] = true;
                    continue;
                }
                return null;
            }

            var stat = stats[found];
            picked[found] = true;
            claimed.Add(found);
            values[slot] = (stat.Min, stat.Max);
            probes[slot] = stat.Max != 0 ? stat.Max : stat.Min;
            next = found + 1;
        }

        var variant = ChooseVariant(entry.English, probes);
        if (variant is null) return null;

        // A variant that would print a value for an id the mod does not carry
        // would invent a number; the variants keyed on such ids mark them
        // "ignore" in their format list.
        for (var slot = 0; slot < count; slot++)
        {
            if (!missing[slot]) continue;
            if (slot >= variant.Format.Count || variant.Format[slot] != "ignore") return null;
        }

        return new EntryMatch(BuildLine(variant, values), claimed);
    }

    private static bool HasStat(IReadOnlyList<RePoESchema.ModStat> stats, string id)
    {
        foreach (var stat in stats)
        {
            if (stat.Id == id) return true;
        }
        return false;
    }

    /// <summary>
    /// Picks the variant that describes the values most precisely. A condition
    /// with both bounds is tighter than one with a single bound, which is
    /// tighter than an unconstrained one; the scores add up over the ids, and
    /// ties go to the variant listed first (the order the game data uses).
    /// Variants with any failing condition cannot be chosen.
    /// </summary>
    private static RePoESchema.TranslationVariant? ChooseVariant(
        IReadOnlyList<RePoESchema.TranslationVariant> variants,
        IReadOnlyList<double> probes)
    {
        RePoESchema.TranslationVariant? chosen = null;
        var best = -1;

        foreach (var variant in variants)
        {
            var score = ScoreConditions(variant.Condition, probes);
            if (score is null || score <= best) continue;
            best = score.Value;
            chosen = variant;
        }

        return best > 0 ? chosen : null;
    }

    private static int? ScoreConditions(
        IReadOnlyList<RePoESchema.Condition> conditions,
        IReadOnlyList<double> probes)
    {
        var score = 0;
        for (var i = 0; i < probes.Count; i++)
        {
            var condition = i < conditions.Count ? conditions[i] : new RePoESchema.Condition();
            var weight = ConditionWeight(condition, probes[i]);
            if (weight is null) return null;
            score += weight.Value;
        }
        return score;
    }

    private static int? ConditionWeight(RePoESchema.Condition condition, double value)
    {
        if (condition.Min is null && condition.Max is null) return 1;

        if (condition.Negated == true)
        {
            var outside = (condition.Min is null || value > condition.Min.Value)
                          || (condition.Max is null || value > condition.Max.Value);
            return outside ? 2 : null;
        }

        if (condition.Min is null) return value <= condition.Max!.Value ? 2 : null;
        if (condition.Max is null) return value >= condition.Min.Value ? 2 : null;
        return value >= condition.Min.Value && value <= condition.Max.Value ? 3 : null;
    }

    private RenderedLine? BuildLine(
        RePoESchema.TranslationVariant variant,
        IReadOnlyList<(double Min, double Max)> values)
    {
        var displays = new ValueDisplay[values.Count];
        for (var i = 0; i < values.Count; i++)
        {
            var format = i < variant.Format.Count ? variant.Format[i] : DefaultFormat;
            if (format == "ignore") continue;

            var handlers = i < variant.IndexHandlers.Count ? variant.IndexHandlers[i] : [];
            var low = ApplyHandlers(values[i].Min, handlers);
            var high = ApplyHandlers(values[i].Max, handlers);
            var style = high.Style.Precision >= low.Style.Precision ? high.Style : low.Style;
            displays[i] = new ValueDisplay(true, low.Value, high.Value, style);
        }

        var plain = new StringBuilder(variant.String.Length + 16);
        var ranged = new StringBuilder(variant.String.Length + 24);
        var template = variant.String;
        var cursor = 0;

        while (cursor < template.Length)
        {
            var open = template.IndexOf('{', cursor);
            if (open < 0)
            {
                plain.Append(template, cursor, template.Length - cursor);
                ranged.Append(template, cursor, template.Length - cursor);
                break;
            }

            plain.Append(template, cursor, open - cursor);
            ranged.Append(template, cursor, open - cursor);

            var close = template.IndexOf('}', open + 1);
            if (close < 0)
            {
                // Unterminated placeholder: copy the brace and keep scanning.
                plain.Append('{');
                ranged.Append('{');
                cursor = open + 1;
                continue;
            }

            var token = template.AsSpan(open + 1, close - open - 1);
            if (!int.TryParse(token, out var slot) || slot < 0 || slot >= values.Count)
            {
                plain.Append(template, open, close - open + 1);
                ranged.Append(template, open, close - open + 1);
                cursor = close + 1;
                continue;
            }

            var format = slot < variant.Format.Count ? variant.Format[slot] : DefaultFormat;
            var display = displays[slot];
            if (!display.Visible || format == "ignore")
            {
                cursor = close + 1;
                continue;
            }

            plain.Append(Decorate(display.Max, format, display.Style));

            if (display.Min != display.Max)
            {
                // The friendly range form repeats the value slot with its tier
                // span. Decorations appear once: '+' leads the range, '%'
                // trails it.
                var low = display.Style.Render(display.Min);
                var highFormat = format;
                if (format.Contains('+') && display.Min > 0)
                {
                    low = "+" + low;
                    highFormat = format.Replace("+", "");
                }
                ranged.Append(low).Append('–').Append(Decorate(display.Max, highFormat, display.Style));
            }
            else
            {
                ranged.Append(Decorate(display.Max, format, display.Style));
            }

            cursor = close + 1;
        }

        var line = plain.ToString();
        if (line.Length == 0) return null;

        // PoE2 markup: [X] keyword markers and [Key|Display] language
        // alternatives both render as plain text in-game; keep the display
        // alternative.
        line = StripBracketMarkup(line);
        var rangeLine = StripBracketMarkup(ranged.ToString());

        // Value-agnostic pattern: replace every digit-run with the placeholder.
        var pattern = NormalizeDigits(line);

        // Representative value span for threshold patterns (after handlers).
        var lowest = double.MaxValue;
        var highest = double.MinValue;
        foreach (var display in displays)
        {
            if (!display.Visible) continue;
            lowest = Math.Min(lowest, display.Min);
            highest = Math.Max(highest, display.Max);
        }
        if (highest < lowest) { lowest = 0; highest = 0; }

        return new RenderedLine(line, rangeLine, pattern, lowest, highest);
    }

    /// <summary>
    /// Applies the RePoE value handlers for one stat id: multiply, divide and
    /// add in that order; a precision handler also rounds and fixes the number
    /// of decimals. Relational, noop and string handlers carry no numeric
    /// effect for item text.
    /// </summary>
    private (double Value, NumberStyle Style) ApplyHandlers(double value, IReadOnlyList<string> handlerNames)
    {
        var style = NumberStyle.Default;
        foreach (var name in handlerNames)
        {
            if (!_valueHandlers.TryGetValue(name, out var handler)) continue;
            if (handler.Type != "int") continue;

            if (handler.Multiplier is { } multiplier) value *= multiplier;
            if (handler.Divisor is { } divisor) value /= divisor;
            if (handler.Addend is { } addend) value += addend;
            if (handler.Precision is { } precision)
            {
                value = Math.Round(value, precision);
                style = new NumberStyle(precision, handler.Fixed == true);
            }
        }
        return (value, style);
    }

    private static string Decorate(double value, string format, NumberStyle style)
    {
        var text = style.Render(value);
        if (format.Contains('+') && value > 0) text = "+" + text;
        if (format.Contains('-') && value < 0) text = "-" + text;
        if (format.Contains('%')) text += "%";
        return text;
    }

    /// <summary>One value slot of a variant: whether it prints and how.</summary>
    private readonly record struct ValueDisplay(bool Visible, double Min, double Max, NumberStyle Style);

    /// <summary>Decimal places and fixed-width flag for one rendered number.</summary>
    private readonly record struct NumberStyle(int Precision, bool Fixed)
    {
        public static NumberStyle Default => new(0, false);

        public string Render(double value)
        {
            if (Fixed) return value.ToString("F" + Precision, Culture);

            var rounded = Math.Round(value, Precision);
            return Math.Abs(rounded - Math.Truncate(rounded)) < 1e-9
                ? ((long)rounded).ToString(Culture)
                : rounded.ToString("0." + new string('#', Precision), Culture);
        }
    }

    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    /// <summary>
    /// Strips PoE2 bracket markup: "[X]" keyword markers and "[Key|Display]"
    /// language alternatives both render as plain text in-game; the display
    /// alternative (after the pipe, e.g. [EnergyShield|Energy Shield]) is kept.
    /// </summary>
    public static string StripBracketMarkup(string s)
    {
        if (!s.Contains('[')) return s;
        var sb = new StringBuilder(s.Length);
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
                var keep = pipe >= 0 ? inner.Substring(pipe + 1) : inner;
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
        var sb = new StringBuilder(s.Length);
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

    private sealed record EntryMatch(RenderedLine? Line, List<int> Consumed);
}

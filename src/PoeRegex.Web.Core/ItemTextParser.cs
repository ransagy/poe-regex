using System.Text.RegularExpressions;

namespace PoeRegex.Web.Core;

/// <summary>
/// One value line inside a mod block, e.g. `Adds 26(19-29) to 47(33-49) Physical Damage`.
/// Rolls are the displayed numbers; ranges come from the parenthesized groups.
/// </summary>
public sealed record ParsedValueLine(string Text, IReadOnlyList<double> Rolls)
{
    /// <summary>Canonical text with parenthesized groups (ranges, tags, markers) stripped.</summary>
    public string CanonicalText { get; } = Regex.Replace(Text, @"\([^()]*\)", "").Trim();

    /// <summary>
    /// Numeric `(lo-hi)` groups in display order: `26(19-29) … 47(33-49)`
    /// yields `[(19,29), (33,49)]`. Non-numeric groups (`(augmented)`,
    /// `(unmet)`) yield nothing. Hyphen and both dash variants are accepted.
    /// </summary>
    public IReadOnlyList<(double Lo, double Hi)> Ranges { get; } = ParseRanges(Text);

    private static IReadOnlyList<(double Lo, double Hi)> ParseRanges(string text)
    {
        var ranges = new List<(double, double)>();
        foreach (Match m in Regex.Matches(text,
                     @"\(\s*([+-]?\d+(?:\.\d+)?)\s*[-\u2013\u2014]\s*([+-]?\d+(?:\.\d+)?)\s*\)"))
        {
            if (double.TryParse(m.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var lo) &&
                double.TryParse(m.Groups[2].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var hi))
                ranges.Add((lo, hi));
        }
        return ranges;
    }
}

/// <summary>One `{ ... Modifier ... }` block with its value lines.</summary>
public sealed record ParsedModBlock(
    string Kind, // implicit | prefix | suffix
    string? Name,
    int? Tier,
    IReadOnlyList<string> Tags,
    IReadOnlyList<ParsedValueLine> Lines);

/// <summary>Structured form of a pasted advanced item description (English).</summary>
public sealed record ParsedItem(
    string? ItemClass,
    string? Rarity,
    string? Name,
    IReadOnlyList<string> BaseLines,
    string? Requires,
    int? ItemLevel,
    string? Sockets,
    string? GrantsSkill,
    bool Corrupted,
    IReadOnlyList<ParsedModBlock> Mods)
{
    /// <summary>Every matchable line: base lines + name + all mod value lines.</summary>
    public IReadOnlyList<string> AllLines() =>
        BaseLines.Concat(Name is null ? [] : [Name]).Concat(Mods.SelectMany(m => m.Lines.Select(l => l.Text))).ToList();
}

/// <summary>
/// Parser for the game's copyable advanced item description (English only).
/// Pure and browser-safe. Handles the five locked fixtures: normal, magic,
/// rare, socketed/staff, corrupted, including tiered mod headers, value
/// ranges, `(augmented)`/`(unmet)` markers, `Sockets:`, `Grants Skill:`.
/// </summary>
public static partial class ItemTextParser
{
    [GeneratedRegex(@"^Item Class:\s*(?<v>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex ItemClassRe();
    [GeneratedRegex(@"^Rarity:\s*(?<v>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex RarityRe();
    [GeneratedRegex(@"^Requires:\s*(?<v>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex RequiresRe();
    [GeneratedRegex(@"^Item Level:\s*(?<v>\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex ItemLevelRe();
    [GeneratedRegex(@"^Sockets:\s*(?<v>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex SocketsRe();
    [GeneratedRegex(@"^Grants Skill:\s*(?<v>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex GrantsSkillRe();
    [GeneratedRegex(@"^\{\s*(?<kind>Implicit|Prefix|Suffix)\b(?<rest>.*)\}$", RegexOptions.IgnoreCase)]
    private static partial Regex ModHeaderRe();
    [GeneratedRegex(@"""(?<name>[^""]+)""")]
    private static partial Regex QuotedNameRe();
    [GeneratedRegex(@"\(Tier:\s*(?<tier>\d+)\)", RegexOptions.IgnoreCase)]
    private static partial Regex TierRe();
    [GeneratedRegex(@"—\s*(?<tags>.+)$")]
    private static partial Regex TagsRe();

    /// <summary>
    /// Split a paste that may hold several advanced descriptions into one text
    /// per item: a new item starts at every `Item Class:` line. In-game pastes
    /// can open with a stray line before that header (the socketed staff's
    /// `sockets`); inside a multi-item paste those pre-header lines sit after
    /// the previous item's trailing blank line, so anything after the last
    /// blank line moves to the new item instead of corrupting the previous
    /// one. Text without any `Item Class:` line is a single item (plain
    /// single-item behavior).
    /// </summary>
    public static IReadOnlyList<string> SplitItems(string text)
    {
        var blocks = new List<string>();
        var current = new List<string>();
        var currentHasHeader = false;
        void Flush()
        {
            blocks.Add(string.Join('\n', current).Trim());
            current.Clear();
        }
        foreach (var raw in text.Split(['\r', '\n']))
        {
            var isHeader = ItemClassRe().Match(raw.Trim()).Success;
            if (isHeader && currentHasHeader)
            {
                var lastBlank = current.FindLastIndex(l => l.Trim().Length == 0);
                if (lastBlank >= 0 && lastBlank < current.Count - 1)
                {
                    var prefix = current.Skip(lastBlank + 1).ToList();
                    current.RemoveRange(lastBlank + 1, current.Count - lastBlank - 1);
                    Flush();
                    current.AddRange(prefix);
                }
                else
                {
                    Flush();
                }
            }
            currentHasHeader |= isHeader;
            current.Add(raw);
        }
        if (current.Count > 0)
            Flush();
        return blocks;
    }

    public static ParsedItem Parse(string text)
    {
        string? itemClass = null;
        string? rarity = null;
        string? name = null;
        var baseLines = new List<string>();
        string? requires = null;
        int? itemLevel = null;
        string? sockets = null;
        string? grantsSkill = null;
        var corrupted = false;
        var mods = new List<ParsedModBlock>();

        ParsedModBlock? current = null;
        List<ParsedValueLine>? currentLines = null;
        var seenSeparator = false;

        void FlushBlock()
        {
            if (current is not null && currentLines is not null)
                mods.Add(current with { Lines = currentLines.ToList() });
            current = null;
            currentLines = null;
        }

        foreach (var raw in text.Split(['\r', '\n']))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (line == "--------") { seenSeparator = true; FlushBlock(); continue; }

            var m = ItemClassRe().Match(line);
            if (m.Success) { itemClass = m.Groups["v"].Value.Trim(); continue; }
            m = RarityRe().Match(line);
            if (m.Success) { rarity = m.Groups["v"].Value.Trim(); continue; }
            if (rarity is not null && !seenSeparator && !line.StartsWith('{'))
            {
                // Name line(s) sit between Rarity: and the first separator.
                name = name is null ? line : name + " " + line;
                continue;
            }
            m = RequiresRe().Match(line);
            if (m.Success) { requires = m.Groups["v"].Value.Trim(); continue; }
            m = ItemLevelRe().Match(line);
            if (m.Success) { itemLevel = int.Parse(m.Groups["v"].Value); continue; }
            m = SocketsRe().Match(line);
            if (m.Success) { sockets = m.Groups["v"].Value.Trim(); continue; }
            m = GrantsSkillRe().Match(line);
            if (m.Success) { grantsSkill = m.Groups["v"].Value.Trim(); continue; }
            if (line.Equals("Corrupted", StringComparison.OrdinalIgnoreCase)) { corrupted = true; continue; }

            m = ModHeaderRe().Match(line);
            if (m.Success)
            {
                FlushBlock();
                var kind = m.Groups["kind"].Value.ToLowerInvariant();
                var rest = m.Groups["rest"].Value;
                var qn = QuotedNameRe().Match(rest);
                var tr = TierRe().Match(rest);
                var tg = TagsRe().Match(rest);
                current = new ParsedModBlock(
                    kind,
                    qn.Success ? qn.Groups["name"].Value : null,
                    tr.Success ? int.Parse(tr.Groups["tier"].Value) : null,
                    tg.Success
                        ? tg.Groups["tags"].Value.Split(',').Select(t => t.Trim())
                            .Where(t => t.Length > 0).ToList()
                        : [],
                    []);
                currentLines = [];
                continue;
            }

            if (currentLines is not null)
            {
                currentLines.Add(new ParsedValueLine(line, ParseRolls(line)));
                continue;
            }

            // Base-stat lines (damage, crit, APS, reload...) and anything else.
            baseLines.Add(line);
        }
        FlushBlock();

        return new ParsedItem(itemClass, rarity, name, baseLines, requires,
            itemLevel, sockets, grantsSkill, corrupted, mods);
    }

    private static IReadOnlyList<double> ParseRolls(string line)
    {
        // Displayed rolls: numbers immediately followed by a range group or
        // standing alone (e.g. `26(19-29)`, `5.1(5-5.9)%`, `Has 1(1-3)`).
        var rolls = new List<double>();
        foreach (Match m in Regex.Matches(line, @"([+-]?\d+(?:\.\d+)?)\s*\("))
        {
            if (double.TryParse(m.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var v))
                rolls.Add(v);
        }
        return rolls;
    }
}

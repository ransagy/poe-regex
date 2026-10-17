using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using PoeRegex.Core.Data;

namespace PoeRegex.Web.Core;

/// <summary>
/// One affix-name group inside a family: every catalog mod in the current
/// scope that carries the same affix name (one-hand/two-hand and other scope
/// variants merge — the in-game search keys on the name, which they share).
/// </summary>
public sealed record ModTier(string Name, IReadOnlyList<ModEntry> Members, int RequiredLevel)
{
    /// <summary>Representative member: chip identity and name-set anchor.</summary>
    public ModEntry Anchor => Members[0];

    /// <summary>Range lines to show: every line of a single member, else the
    /// first line of each member (merged variants differ by value, not text).</summary>
    public IReadOnlyList<string> DisplayLines => Members.Count == 1
        ? Members[0].StatLines.Select(ModSearch.DisplayLine).ToList()
        : Members.Select(m => ModSearch.DisplayLine(m.StatLines[0])).Distinct().ToList();
}

/// <summary>
/// A mod family (raw RePoE `type`): all tiers of one stat in the current
/// scope. Picking the family compiles the plain stat-text fragment (matches
/// every tier); picking a tier compiles its affix name instead.
/// </summary>
public sealed record ModFamily(string Key, bool IsPrefix, IReadOnlyList<ModTier> Tiers)
{
    public IEnumerable<ModEntry> Members => Tiers.SelectMany(t => t.Members);

    /// <summary>Representative member for whole-family picks (base tier).</summary>
    public ModEntry Anchor => Tiers[0].Anchor;

    /// <summary>Family text with each number's overall span, e.g.
    /// "+10–214 to maximum Life". Falls back to the first member's line when
    /// members' text shapes differ.</summary>
    public required string DisplayText { get; init; }

    /// <summary>This tier's affix name and every better tier's name, by
    /// required level (the honest "tier or better" — never game tier numbers).</summary>
    public IReadOnlyList<string> NamesFrom(ModTier tier) => Tiers
        .Where(t => t.Name.Length > 0 && t.RequiredLevel >= tier.RequiredLevel)
        .OrderBy(t => t.RequiredLevel)
        .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
        .Select(t => t.Name)
        .Distinct(StringComparer.Ordinal)
        .ToList();
}

/// <summary>
/// Groups a scope's mods into families/tiers for the pickers. Pure and
/// browser-safe; ordering is deterministic (prefixes first, then display text).
/// </summary>
public static class ModFamilies
{
    /// <summary>Awaiting name for the group key of a mod outside any family.</summary>
    public static string KeyOf(ModEntry mod) =>
        mod.Family.Length > 0 ? mod.Family : "\u0001" + mod.Id;

    public static IReadOnlyList<ModFamily> Group(IEnumerable<ModEntry> mods)
    {
        var families = new List<ModFamily>();
        foreach (var famGroup in mods
                     .GroupBy(m => (Key: KeyOf(m), Gen: m.GenerationType.ToLowerInvariant()))
                     .OrderBy(g => g.Key.Gen == "prefix" ? 0 : 1)
                     .ThenBy(g => FamilyDisplay(g.ToList()), StringComparer.OrdinalIgnoreCase)
                     .ThenBy(g => g.Key.Key, StringComparer.Ordinal))
        {
            var members = famGroup.ToList();
            var tiers = members
                .GroupBy(m => m.Name ?? "")
                .Select(g => new ModTier(
                    g.Key,
                    g.OrderBy(m => m.RequiredLevel).ThenBy(m => m.Id, StringComparer.Ordinal).ToList(),
                    g.Min(m => m.RequiredLevel)))
                .OrderBy(t => t.RequiredLevel)
                .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(t => t.Anchor.Id, StringComparer.Ordinal)
                .ToList();
            families.Add(new ModFamily(famGroup.Key.Key, famGroup.Key.Gen == "prefix", tiers)
            {
                DisplayText = FamilyDisplay(members),
            });
        }
        return families;
    }

    /// <summary>Family-level text: all lines unioned by member, when the
    /// members share a line shape; otherwise the first member's text.</summary>
    public static string FamilyDisplay(IReadOnlyList<ModEntry> members) =>
        string.Join(" · ", FamilyLines(members));

    private static IReadOnlyList<string> FamilyLines(IReadOnlyList<ModEntry> members)
    {
        var anchor = members.OrderBy(m => m.RequiredLevel).ThenBy(m => m.Id, StringComparer.Ordinal).First();
        if (members.Count == 1 || members.Any(m => m.StatLines.Count != anchor.StatLines.Count))
            return anchor.StatLines.Select(ModSearch.DisplayLine).ToList();
        var lines = new List<string>(anchor.StatLines.Count);
        for (var i = 0; i < anchor.StatLines.Count; i++)
            lines.Add(UnionLine(members.Select(m => m.StatLines[i]).ToList()));
        return lines;
    }

    private static readonly Regex PairRe = new(
        @"(\d+(?:\.\d+)?)(?:\s*[\u2013\u2014-]\s*(\d+(?:\.\d+)?))?", RegexOptions.Compiled);

    /// <summary>One unioned stat line: every number (or `lo–hi` span) replaced
    /// by the overall min–max across members. A flat number in one tier
    /// (`Adds 1 to 7–10 …`) is a degenerate span, so mixed spans still union.
    /// Falls back to the first entry when the shapes or counts differ.</summary>
    internal static string UnionLine(IReadOnlyList<StatLineEntry> lines)
    {
        var parsed = lines.Select(l => Split(l.RangeLine)).ToList();
        var count = parsed[0].Pairs.Count;
        if (count == 0 || parsed.Any(p => p.Pairs.Count != count)
                       || parsed.Select(p => p.Skeleton).Distinct(StringComparer.Ordinal).Count() != 1)
            return ModSearch.DisplayLine(lines[0]);
        var sb = new StringBuilder();
        var parts = parsed[0].Skeleton.Split('\u0001');
        for (var i = 0; i < parts.Length; i++)
        {
            sb.Append(parts[i]);
            if (i >= count) break;
            var lo = parsed.Min(p => p.Pairs[i].Lo);
            var hi = parsed.Max(p => p.Pairs[i].Hi);
            sb.Append(FormatNumber(lo));
            if (lo != hi) sb.Append('–').Append(FormatNumber(hi));
        }
        var text = sb.ToString();
        // The skeleton deliberately drops the original pairs; when nothing was
        // actually substituted (single values), the plain line is friendlier.
        return text.Length > 0 ? text : ModSearch.DisplayLine(lines[0]);
    }

    private static (string Skeleton, List<(double Lo, double Hi)> Pairs) Split(string rangeLine)
    {
        if (rangeLine.Length == 0) return ("", []);
        var pairs = new List<(double, double)>();
        var skeleton = PairRe.Replace(rangeLine, m =>
        {
            var hiText = m.Groups[2].Success ? m.Groups[2].Value : m.Groups[1].Value;
            if (double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var lo) &&
                double.TryParse(hiText, NumberStyles.Float, CultureInfo.InvariantCulture, out var hi))
            {
                pairs.Add((lo, hi));
                return "\u0001";
            }
            return m.Value;
        });
        return (skeleton, pairs);
    }

    private static string FormatNumber(double v) =>
        Math.Abs(v - Math.Truncate(v)) < 1e-9
            ? ((long)Math.Truncate(v)).ToString(CultureInfo.InvariantCulture)
            : v.ToString("0.##", CultureInfo.InvariantCulture);
}

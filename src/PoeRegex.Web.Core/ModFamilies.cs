using System.Globalization;
using System.Text.RegularExpressions;
using PoeRegex.Core.Data;
using PoeRegex.Core.Search;

namespace PoeRegex.Web.Core;

/// <summary>
/// One affix-name group inside a family: every catalog mod in the current
/// scope that carries the same affix name (one-hand/two-hand and other scope
/// variants merge — the in-game search keys on the name, which they share).
/// </summary>
public sealed record ModTier(string Name, IReadOnlyList<ModEntry> Members, int RequiredLevel)
{
    /// <summary>Cached case-insensitive search text: name plus every rendered
    /// line/range form. Built once per grouping instead of filtering members
    /// on every keystroke.</summary>
    public string SearchText { get; init; } = "";

    /// <summary>Representative member: chip identity and name-set anchor.</summary>
    public ModEntry Anchor => Members[0];

    /// <summary>One mod's lines: the anchor. A tier can merge variants from
    /// several classes; showing every member here made the tier look like a
    /// single hybrid mod. Use <see cref="UnionLines"/> for the merged view.</summary>
    public IReadOnlyList<string> DisplayLines =>
        Members[0].StatLines.Select(ModSearch.DisplayLine).ToList();

    /// <summary>One line per stat shape for the merged family view.</summary>
    public IReadOnlyList<string> ShapeLines => Members
        .SelectMany(m => m.StatLines)
        .Select(ModFamilies.ShapeOf)
        .Distinct(StringComparer.Ordinal)
        .ToList();

    /// <summary>
    /// Roll alternatives for an All-gear tier: one concrete line when all
    /// members agree, otherwise a compact `+1 / +2 ...` line. True multi-line
    /// mods keep their real lines instead of being joined.
    /// </summary>
    public IReadOnlyList<string> UnionLines
    {
        get
        {
            if (Members.Count == 1 || Members.Any(m => m.StatLines.Count != 1))
                return DisplayLines;
            var lines = Members
                .Select(m => ModSearch.DisplayLine(m.StatLines[0]))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(ModFamilies.FirstNumber)
                .ThenBy(x => x, StringComparer.Ordinal)
                .ToList();
            if (lines.Count <= 1) return lines;
            var compact = ModFamilies.CompactAlternatives(lines);
            return [compact ?? string.Join(" / ", lines)];
        }
    }

    /// <summary>Concrete member variants for the tier tooltip.</summary>
    public IReadOnlyList<string> VariantLines => Members
        .Select(m => string.Join(" / ", m.StatLines.Select(ModSearch.DisplayLine)))
        .Distinct(StringComparer.Ordinal)
        .ToList();

    /// <summary>Stable tier merge key inside a family.</summary>
    public string MergeKey { get; init; } = "";

    /// <summary>Empty for normal explicit affixes; "desecrated" for Abyss
    /// desecrated mods.</summary>
    public string Source { get; init; } = "";

    /// <summary>User-facing tier label. Normal tiers use their affix name;
    /// desecrated tiers use their numeric range because faction names repeat
    /// across every mod of a faction.</summary>
    public string DisplayLabel { get; init; } = "";
}

/// <summary>
/// A mod family (raw RePoE `type`): all tiers of one stat in the current
/// scope. Picking the family compiles the plain stat-text fragment (matches
/// every tier); picking a tier compiles its affix name instead.
/// </summary>
public sealed record ModFamily(string Key, bool IsPrefix, IReadOnlyList<ModTier> Tiers)
{
    /// <summary>Empty for normal explicit affixes; "desecrated" for Abyss
    /// desecrated mods. The family key carries the source prefix, so a
    /// desecrated family with the same stat text as a normal family remains
    /// a distinct row.</summary>
    public string Source { get; init; } = "";

    /// <summary>True when this family was merged for the All-gear view.</summary>
    public bool IsUnion { get; init; }

    public bool IsDesecrated => Source == "desecrated";

    public IEnumerable<ModEntry> Members => Tiers.SelectMany(t => t.Members);

    /// <summary>Distinct normalized faction names (Amanamu, Kurgal, Ulaman)
    /// for the desecrated mods in this family.</summary>
    public IReadOnlyList<string> Factions => Members
        .Select(m => FactionName(m.Name))
        .Where(x => x.Length > 0)
        .Distinct(StringComparer.Ordinal)
        .OrderBy(x => x, StringComparer.Ordinal)
        .ToList();

    /// <summary>Green faction tag for a desecrated row; null when no faction
    /// name is available.</summary>
    public string? SourceTag => !IsDesecrated || Factions.Count == 0
        ? null
        : string.Join(" / ", Factions);

    /// <summary>Normalize `of Amanamu` / `Amanamu's` to `Amanamu`.</summary>
    public static string FactionName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        var value = name.Trim();
        if (value.StartsWith("of ", StringComparison.OrdinalIgnoreCase))
            value = value[3..];
        if (value.EndsWith("'s", StringComparison.OrdinalIgnoreCase))
            value = value[..^2];
        return value.Trim();
    }

    /// <summary>Representative member for whole-family picks (base tier).</summary>
    public ModEntry Anchor => Tiers[0].Anchor;

    /// <summary>Family text lines with the numbers removed, e.g.
    /// "+ to maximum Life" — one entry per stat line (hybrid families have
    /// two); names the stat without implying one tier's range (tier rows
    /// carry the real ranges).</summary>
    public required IReadOnlyList<string> DisplayLines { get; init; }

    /// <summary>Single-line form for chips: lines joined with `/`.</summary>
    public string DisplayText => string.Join(" / ", DisplayLines);

    /// <summary>
    /// The family pick: the anchor plus every tier variant. Compiling all
    /// variant mod ids ORs their fragments, so a family whose tiers differ in
    /// wording (singular/plural, an explicit "2") still matches every tier.
    /// </summary>
    public WantedMod Pick() => new(
        Anchor.Id,
        null,
        Tiers.SelectMany(t => t.Members)
            .Where(m => m.StatLines.Count > 0)
            .Select(m => m.Id)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList());

    /// <summary>This tier's affix name and every better tier's name, by
    /// required level (the honest "tier or better" — never game tier numbers).</summary>
    public IReadOnlyList<string> NamesFrom(ModTier tier) =>
        Source == "desecrated"
            ? []
            : Tiers
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
    /// <summary>
    /// Family identity for grouping and lookups. The source is part of the
    /// key so a desecrated family never merges with a normal family carrying
    /// the same stat text or raw RePoE type.
    /// </summary>
    public static string KeyOf(ModEntry mod)
    {
        var key = mod.Family.Length > 0 ? mod.Family : "\u0001" + mod.Id;
        return mod.Source == "desecrated" ? "desecrated:" + key : key;
    }

    /// <summary>
    /// Families offered before the user types: the usual goals across gear
    /// (defence, resistances, attributes, speed, damage). Scope-dependent —
    /// keys missing from the current scope are simply skipped.
    /// </summary>
    public static readonly IReadOnlySet<string> CommonKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        // prefixes
        "IncreasedLife", "IncreasedMana", "LocalPhysicalDamagePercent",
        "LocalFireDamage", "LocalColdDamage", "LocalLightningDamage",
        "LocalEnergyShield", "LocalPhysicalDamageReductionRating", "LocalEvasionRating",
        "BaseSpirit",
        // suffixes
        "FireResistance", "ColdResistance", "LightningResistance", "ChaosResistance",
        "Strength", "Dexterity", "Intelligence",
        "MovementVelocity", "LocalIncreasedAttackSpeed", "LocalBaseCriticalStrikeChance",
        "LocalAccuracyRating", "ManaRegeneration",
    };

    /// <summary>True when the family is in the pre-typing shortlist.</summary>
    public static bool IsCommon(ModFamily family) =>
        CommonKeys.Contains(family.Key) ||
        family.Members.Any(m => CommonKeys.Contains(KeyOf(m)));

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, IReadOnlyList<ModFamily>> GroupCache = new();
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, IReadOnlyList<ModFamily>> UnionGroupCache = new();

    /// <summary>
    /// Shared grouping cache keyed by the stable mod-list instance from
    /// <see cref="EngineService"/>. Mode and slot switches reuse the same
    /// grouping instead of rebuilding every tier's search text.
    /// </summary>
    public static IReadOnlyList<ModFamily> CachedGroup(object mods, bool union = false) =>
        (union ? UnionGroupCache : GroupCache)
        .GetValue(mods, key => Group((IEnumerable<ModEntry>)key, union));

    public static IReadOnlyList<ModFamily> Group(IEnumerable<ModEntry> mods, bool union = false)
    {
        var families = new List<ModFamily>();
        foreach (var famGroup in mods
                     .GroupBy(m => (Key: KeyOf(m), Gen: m.GenerationType.ToLowerInvariant()))
                     .OrderBy(g => g.Key.Gen == "prefix" ? 0 : 1)
                     .ThenBy(g => string.Join(" · ", FamilyLines(g.ToList())), StringComparer.OrdinalIgnoreCase)
                     .ThenBy(g => g.Key.Key, StringComparer.Ordinal))
        {
            var members = famGroup.ToList();
            var tiers = members
                .GroupBy(TierKey)
                .Select(g =>
                {
                    var tierMembers = g
                        .OrderBy(m => m.RequiredLevel)
                        .ThenBy(m => m.Id, StringComparer.Ordinal)
                        .ToList();
                    var anchor = tierMembers[0];
                    return new ModTier(anchor.Name ?? "", tierMembers, g.Min(m => m.RequiredLevel))
                    {
                        Source = anchor.Source,
                        DisplayLabel = DisplayLabelFor(tierMembers),
                        MergeKey = TierKey(anchor),
                        SearchText = SearchTextOf(tierMembers),
                    };
                })
                .OrderBy(t => t.RequiredLevel)
                .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(t => t.Anchor.Id, StringComparer.Ordinal)
                .ToList();
            families.Add(new ModFamily(famGroup.Key.Key, famGroup.Key.Gen == "prefix", tiers)
            {
                Source = members[0].Source,
                DisplayLines = FamilyLines(members),
            });
        }
        return union ? UnionFamilies(families) : families;
    }

    private static List<ModFamily> UnionFamilies(IReadOnlyList<ModFamily> families)
    {
        var result = new List<ModFamily>();
        foreach (var group in families
                     .GroupBy(UnionKeyOf)
                     .OrderBy(g => g.First().IsPrefix ? 0 : 1)
                     .ThenBy(g => g.First().DisplayText, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(g => g.Key, StringComparer.Ordinal))
        {
            var members = group.SelectMany(f => f.Members).ToList();
            var tiers = members
                .GroupBy(TierKey)
                .Select(g =>
                {
                    var tierMembers = g
                        .OrderBy(m => m.RequiredLevel)
                        .ThenBy(m => m.Id, StringComparer.Ordinal)
                        .ToList();
                    var anchor = tierMembers[0];
                    return new ModTier(anchor.Name ?? "", tierMembers, g.Min(m => m.RequiredLevel))
                    {
                        Source = anchor.Source,
                        DisplayLabel = DisplayLabelFor(tierMembers),
                        MergeKey = TierKey(anchor),
                        SearchText = SearchTextOf(tierMembers),
                    };
                })
                .OrderBy(t => t.RequiredLevel)
                .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(t => t.Anchor.Id, StringComparer.Ordinal)
                .ToList();
            result.Add(new ModFamily(group.Key, group.First().IsPrefix, tiers)
            {
                Source = group.First().Source,
                DisplayLines = FamilyLines(members),
                IsUnion = true,
            });
        }
        return result;
    }

    private static string UnionKeyOf(ModFamily family)
    {
        var patterns = family.Members
            .SelectMany(m => m.StatLines)
            .Select(sl => sl.Pattern)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal);
        return "union:" + family.Source + ":" + (family.IsPrefix ? "prefix" : "suffix") +
               ":" + string.Join("\u0001", patterns);
    }

    internal static string? CompactAlternatives(IReadOnlyList<string> lines)
    {
        if (lines.Count < 2) return null;
        var prefix = lines[0];
        foreach (var line in lines.Skip(1))
            prefix = CommonPrefix(prefix, line);
        if (prefix.Length == 0) return null;
        var suffix = lines[0];
        foreach (var line in lines.Skip(1))
            suffix = CommonSuffix(suffix, line);
        if (suffix.Length == 0) return null;
        if (lines.Any(line => prefix.Length + suffix.Length >= line.Length)) return null;
        var middles = lines
            .Select(line => line[prefix.Length..(line.Length - suffix.Length)])
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (middles.Count < 2 || middles.Any(m => m.Length == 0)) return null;
        // Keep the prefix on every short alternative (`+1 / +2`), but only
        // once for a long textual prefix (`Break Armour equal to 2–4 / 4–7`).
        var body = prefix.Length <= 2
            ? string.Join(" / ", middles.Select(m => prefix + m))
            : prefix + string.Join(" / ", middles);
        return body + suffix;
    }

    private static string CommonPrefix(string a, string b)
    {
        var i = 0;
        while (i < a.Length && i < b.Length && a[i] == b[i]) i++;
        return a[..i];
    }

    private static string CommonSuffix(string a, string b)
    {
        var i = 0;
        while (i < a.Length && i < b.Length && a[^(i + 1)] == b[^(i + 1)]) i++;
        return a[^i..];
    }

    internal static double FirstNumber(string text)
    {
        var match = NumberRe.Match(text);
        return match.Success &&
               double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
    }

    private static string TierKey(ModEntry mod) =>
        mod.Source == "desecrated"
            ? string.Join("\u0001", mod.StatLines
                .Select(sl => sl.Pattern)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(x => x, StringComparer.Ordinal))
            : mod.Name ?? "";

    /// <summary>
    /// Tier label: the affix name for normal tiers, or every distinct range
    /// label for a desecrated tier merged across ranges.
    /// </summary>
    private static string DisplayLabelFor(IReadOnlyList<ModEntry> members)
    {
        var anchor = members[0];
        if (anchor.Source != "desecrated") return anchor.Name ?? "";
        return string.Join(" / ", members
            .Select(RangeLabel)
            .Where(label => label.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(FirstNumber)
            .ThenBy(label => label, StringComparer.Ordinal));
    }

    private static string RangeLabel(ModEntry mod)
    {
        var parts = mod.StatLines
            .Select(sl =>
            {
                var range = RangeRe.Match(sl.RangeLine);
                if (range.Success)
                {
                    var percent = range.Groups[3].Success || sl.RangeLine.Contains('%')
                        ? "%"
                        : "";
                    return $"{range.Groups[1].Value}–{range.Groups[2].Value}{percent}";
                }
                if (sl.MinValue == 0 && sl.MaxValue == 0) return "";
                var min = sl.MinValue.ToString("0.##", CultureInfo.InvariantCulture);
                var max = sl.MaxValue.ToString("0.##", CultureInfo.InvariantCulture);
                var pct = sl.RangeLine.Contains('%') ? "%" : "";
                return min == max ? $"{min}{pct}" : $"{min}–{max}{pct}";
            })
            .Where(part => part.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return string.Join(" / ", parts);
    }

    private static string SearchTextOf(IReadOnlyList<ModEntry> members)
    {
        var text = new System.Text.StringBuilder();
        foreach (var member in members)
        {
            if (!string.IsNullOrEmpty(member.Name)) text.Append(member.Name).Append('\n');
            foreach (var line in member.StatLines)
                text.Append(line.Line).Append('\n')
                    .Append(line.RangeLine).Append('\n')
                    .Append(AdvancedStatLine.For(line)).Append('\n');
        }
        return text.ToString();
    }

    /// <summary>Family-level text lines: the anchor member's stat shape with
    /// the numbers dropped (`+ to maximum Life`, `% increased Physical
    /// Damage`), one entry per stat line. Tier rows carry the real ranges.</summary>
    public static IReadOnlyList<string> FamilyLines(IReadOnlyList<ModEntry> members)
    {
        var anchor = members.OrderBy(m => m.RequiredLevel).ThenBy(m => m.Id, StringComparer.Ordinal).First();
        return anchor.StatLines.Select(ShapeOf).ToList();
    }

    /// <summary>Stat text with every digit placeholder dropped and spaces
    /// collapsed; comparison/searching still run on the real rendered lines.</summary>
    internal static string ShapeOf(StatLineEntry line) =>
        WhitespaceRe.Replace(line.Pattern.Replace(SuffixIndex.DigitPlaceholder, ' '), " ").Trim();

    private static readonly Regex WhitespaceRe = new(@"\s{2,}", RegexOptions.Compiled);
    private static readonly Regex RangeRe = new(
        @"(\d+(?:\.\d+)?)\s*[–—-]\s*(\d+(?:\.\d+)?)(\s*%)?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex NumberRe = new(
        @"[+-]?\d+(?:\.\d+)?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
}

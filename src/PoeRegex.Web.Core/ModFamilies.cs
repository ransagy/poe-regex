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

    /// <summary>Range lines to show: every line of a single member, else the
    /// first line of each member (merged variants differ by value, not text).</summary>
    public IReadOnlyList<string> DisplayLines => Members.Count == 1
        ? Members[0].StatLines.Select(ModSearch.DisplayLine).ToList()
        : Members.Select(m => ModSearch.DisplayLine(m.StatLines[0])).Distinct().ToList();

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

    /// <summary>Green source tag for the row; includes faction when known.</summary>
    public string? SourceTag => !IsDesecrated
        ? null
        : Factions.Count == 0
            ? "Desecrated"
            : "Desecrated · " + string.Join(" / ", Factions);

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
    public static bool IsCommon(ModFamily family) => CommonKeys.Contains(family.Key);

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, IReadOnlyList<ModFamily>> GroupCache = new();

    /// <summary>
    /// Shared grouping cache keyed by the stable mod-list instance from
    /// <see cref="EngineService"/>. Mode and slot switches reuse the same
    /// grouping instead of rebuilding every tier's search text.
    /// </summary>
    public static IReadOnlyList<ModFamily> CachedGroup(object mods) =>
        GroupCache.GetValue(mods, key => Group((IEnumerable<ModEntry>)key));

    public static IReadOnlyList<ModFamily> Group(IEnumerable<ModEntry> mods)
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
                        DisplayLabel = anchor.Source == "desecrated"
                            ? RangeLabel(anchor)
                            : anchor.Name ?? "",
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
        return families;
    }

    private static string TierKey(ModEntry mod) =>
        mod.Source == "desecrated"
            ? string.Join("\u0001", mod.StatLines.Select(sl =>
                $"{sl.Pattern}\u0002{sl.RangeLine}"))
            : mod.Name ?? "";

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
}

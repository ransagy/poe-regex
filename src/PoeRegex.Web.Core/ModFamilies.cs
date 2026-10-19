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
                DisplayLines = FamilyLines(members),
            });
        }
        return families;
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
}

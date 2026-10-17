using PoeRegex.Core.Data;

namespace PoeRegex.Web.Core;

/// <summary>One rule verdict: error blocks saving, warning does not.</summary>
public sealed record RuleViolation(string Level, string Message);

/// <summary>One affix line for rule checking: header/mod kind + selected catalog mod (if mapped).</summary>
public sealed record RuleLine(string Kind, string? ModId);

/// <summary>
/// Item construction rules (caps confirmed by maintainer: Normal 0/0,
/// Magic 1 prefix + 1 suffix, Rare 3 + 3; implicits exempt).
/// Pure and browser-safe; needs an <see cref="EngineService"/> only to read
/// mod generation types and groups for mapped lines.
/// </summary>
public static class ItemRules
{
    /// <summary>Affix caps for a rarity; (-1, -1) for unknown (unchecked).</summary>
    public static (int Prefix, int Suffix) CapsFor(string? rarity) =>
        rarity?.Trim().ToLowerInvariant() switch
        {
            "normal" => (0, 0),
            "magic" => (1, 1),
            "rare" => (3, 3),
            _ => (-1, -1),
        };

    /// <summary>
    /// Whether the assemble dialog may add this catalog mod: rarity must
    /// allow affixes, the generation must be under its cap, and the mod must
    /// not share a family or a catalog group with anything already chosen
    /// (no second tier of one stat, no group clash). Mirrors
    /// <see cref="Validate"/>; keeping the picker's "no" states and the
    /// validator's verdicts in one place.
    /// </summary>
    public static bool CanAdd(
        EngineService engine, ModEntry candidate, IEnumerable<string> addedIds, string? rarity)
    {
        var (maxPrefix, maxSuffix) = CapsFor(rarity);
        var kind = candidate.GenerationType.ToLowerInvariant();
        if (kind != "prefix" && kind != "suffix") return false;
        if ((kind == "prefix" ? maxPrefix : maxSuffix) <= 0) return false;

        var prefixes = 0;
        var suffixes = 0;
        var usedGroups = new HashSet<string>(StringComparer.Ordinal);
        var usedFamilies = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in addedIds)
        {
            if (string.Equals(id, candidate.Id, StringComparison.Ordinal)) return false;
            var mod = engine.FindMod(id);
            if (mod is null) continue;
            switch (mod.GenerationType.ToLowerInvariant())
            {
                case "prefix": prefixes++; break;
                case "suffix": suffixes++; break;
            }
            foreach (var g in mod.Groups) usedGroups.Add(g);
            if (mod.Family.Length > 0) usedFamilies.Add(mod.Family);
        }

        if (kind == "prefix" ? prefixes >= maxPrefix : suffixes >= maxSuffix) return false;
        if (candidate.Family.Length > 0 && usedFamilies.Contains(candidate.Family)) return false;
        if (candidate.Groups.Any(usedGroups.Contains)) return false;
        return true;
    }

    public static IReadOnlyList<RuleViolation> Validate(
        EngineService engine, IEnumerable<RuleLine> lines, string? rarity)
    {
        var list = lines.ToList();
        var violations = new List<RuleViolation>();

        var (maxPrefix, maxSuffix) = CapsFor(rarity);
        if (maxPrefix < 0)
        {
            violations.Add(new RuleViolation("warning",
                $"unknown rarity '{rarity}': affix counts unchecked"));
        }

        var prefixes = 0;
        var suffixes = 0;
        var seenGroups = new Dictionary<string, string>(StringComparer.Ordinal);
        var seenMods = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in list)
        {
            if (line.Kind == "implicit") continue;
            var mod = line.ModId is null ? null : engine.FindMod(line.ModId);
            var kind = mod?.GenerationType.ToLowerInvariant() ?? line.Kind;
            if (kind != "prefix" && kind != "suffix")
            {
                violations.Add(new RuleViolation("warning",
                    $"'{line.ModId ?? line.Kind}' is not classifiable (kind '{kind}'): counts unchecked"));
                continue;
            }
            if (mod is not null && mod.GenerationType.ToLowerInvariant() != line.Kind && line.Kind is "prefix" or "suffix")
            {
                violations.Add(new RuleViolation("warning",
                    $"header says {line.Kind} but catalog says {mod.GenerationType} for {mod.Id}"));
            }
            if (kind == "prefix") prefixes++;
            else suffixes++;
            if (mod is not null)
            {
                foreach (var g in mod.Groups)
                {
                    if (seenGroups.TryGetValue(g, out var other) && other != mod.Id)
                    {
                        violations.Add(new RuleViolation("error",
                            $"group clash '{g}': {other} and {mod.Id} cannot coexist"));
                    }
                    else
                    {
                        seenGroups[g] = mod.Id;
                    }
                }
                if (!seenMods.Add(mod.Id))
                {
                    violations.Add(new RuleViolation("error",
                        $"duplicate mod '{mod.Id}': only one tier of a mod can be on an item"));
                }
            }
        }

        if (maxPrefix >= 0 && prefixes > maxPrefix)
            violations.Add(new RuleViolation("error",
                $"too many prefixes for {rarity}: {prefixes} > {maxPrefix}"));
        if (maxSuffix >= 0 && suffixes > maxSuffix)
            violations.Add(new RuleViolation("error",
                $"too many suffixes for {rarity}: {suffixes} > {maxSuffix}"));
        return violations;
    }
}

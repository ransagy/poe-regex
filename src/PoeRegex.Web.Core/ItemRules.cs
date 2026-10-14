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
    public static IReadOnlyList<RuleViolation> Validate(
        EngineService engine, IEnumerable<RuleLine> lines, string? rarity)
    {
        var list = lines.ToList();
        var violations = new List<RuleViolation>();

        var (maxPrefix, maxSuffix) = rarity?.Trim().ToLowerInvariant() switch
        {
            "normal" => (0, 0),
            "magic" => (1, 1),
            "rare" => (3, 3),
            _ => (-1, -1),
        };
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

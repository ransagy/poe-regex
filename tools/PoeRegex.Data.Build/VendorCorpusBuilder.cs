using System.Globalization;
using System.Text.Json;
using PoeRegex.Core.Compilation;
using PoeRegex.Core.Data;
using PoeRegex.Data.Build.RePoE;
using PoeRegex.Data.Build.Rendering;

namespace PoeRegex.Data.Build;

/// <summary>
/// Builds the vendor/stash searchable-text corpus. The selectable catalog is
/// only explicit prefixes/suffixes; this corpus also covers base items,
/// implicits, corrupted/essence/crafted mods, affix captions, base properties,
/// and regular/base names. Unique-only mods, unique names, flavour, and rare
/// names are deliberately excluded.
/// </summary>
public static class VendorCorpusBuilder
{
    private static readonly HashSet<string> IncludedGenerations = new(StringComparer.Ordinal)
    {
        "prefix", "suffix", "corrupted", "essence", "crafted", "implicit",
        "talisman", "blight", "synthesis_a", "synthesis_globals", "synthesis_bonus",
    };

    public static VendorCorpusFile Build(string game, string rawDir)
    {
        var mods = RePoEJson.LoadMods(Path.Combine(rawDir, "mods.min.json"));
        var itemClasses = RePoEJson.LoadItemClasses(Path.Combine(rawDir, "item_classes.min.json"));
        var baseItems = RePoEJson.LoadBaseItems(Path.Combine(rawDir, "base_items.min.json"));
        var handlers = RePoEJson.LoadValueHandlers(Path.Combine(rawDir, "stat_value_handlers.min.json"));
        var translations = CatalogBuilder.LoadAllTranslations(game, rawDir);
        var renderer = new StatTextRenderer(handlers, translations);
        var classByMod = CatalogBuilder.MapModsToClasses(rawDir, itemClasses);

        var classById = itemClasses.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        var classByName = itemClasses.Values
            .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        string? ResolveClass(string? itemClass)
        {
            if (string.IsNullOrWhiteSpace(itemClass)) return null;
            if (classById.ContainsKey(itemClass)) return itemClass;
            return classByName.TryGetValue(itemClass, out var cls) ? (cls.Id ?? itemClass) : null;
        }

        var lines = new HashSet<(string? ClassId, string Line, string Kind)>();

        void Add(string? classId, string? line, string kind)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            var clean = line.Trim();
            if (clean.Length == 0) return;
            lines.Add((classId, clean, kind));
        }

        void AddModLines(string modId, RePoESchema.Mod mod, IEnumerable<string>? forcedClasses = null)
        {
            var rendered = renderer.RenderMod(mod.Stats);
            if (rendered.Count == 0) return;
            var classes = forcedClasses?.ToList() ?? (classByMod.TryGetValue(modId, out var set) ? set.ToList() : []);
            foreach (var line in rendered)
            {
                if (classes.Count == 0) Add(null, line.Line, "mod");
                else foreach (var cls in classes) Add(cls, line.Line, "mod");
            }
        }

        // All item-domain non-unique mod lines, including corrupted/essence/
        // crafted and the extra regular tiers that the selectable catalog omits.
        foreach (var (modId, mod) in mods)
        {
            if (mod.Domain != "item") continue;
            if (!IncludedGenerations.Contains(mod.GenerationType)) continue;
            AddModLines(modId, mod);
        }

        // Base items: names, properties, requirements, granted skills, and
        // implicit mod lines. Unique-only/legacy/unreleased bases are out of
        // the normal/craftable vendor/stash scope.
        foreach (var baseItem in baseItems.Values)
        {
            if (baseItem.Domain != "item") continue;
            if (!string.Equals(baseItem.ReleaseState, "released", StringComparison.OrdinalIgnoreCase)) continue;
            if (string.IsNullOrWhiteSpace(baseItem.Name)) continue;
            var cls = ResolveClass(baseItem.ItemClass);
            Add(cls, baseItem.Name, "base-name");

            foreach (var implicitId in baseItem.Implicits ?? [])
            {
                if (!mods.TryGetValue(implicitId, out var implicitMod)) continue;
                AddModLines(implicitId, implicitMod, cls is null ? null : [cls]);
            }

            RenderBaseProperties(baseItem, cls, Add);

            if (baseItem.Requirements is { Level: > 0 } req)
            {
                var parts = new List<string> { $"Level {req.Level}" };
                if (req.Strength > 0) parts.Add($"{req.Strength} Str");
                if (req.Dexterity > 0) parts.Add($"{req.Dexterity} Dex");
                if (req.Intelligence > 0) parts.Add($"{req.Intelligence} Int");
                Add(cls, "Requires: " + string.Join(", ", parts), "base-property");
            }

            foreach (var skill in baseItem.SkillsGranted ?? [])
            {
                var name = HumanizeSkill(skill);
                if (name.Length == 0) continue;
                Add(cls, $"Grants Skill: {name}", "base-property");
                Add(cls, $"Grants Skill: Level 1 {name}", "base-property");
            }
        }

        foreach (var (modId, mod) in mods)
        {
            if (mod.Domain != "item") continue;
            if (!IncludedGenerations.Contains(mod.GenerationType)) continue;
            if (string.IsNullOrWhiteSpace(mod.Name)) continue;
            var modClasses = classByMod.TryGetValue(modId, out var set) ? set.ToList() : [];
            var kind = mod.GenerationType switch
            {
                "prefix" => "Prefix",
                "suffix" => "Suffix",
                "corrupted" => "Corrupted",
                "essence" => "Essence",
                "crafted" => "Crafted",
                _ => "Modifier",
            };
            var tags = DisplayTags(mod.ImplicitTags);
            var tagText = tags.Count == 0 ? "" : " — " + string.Join(", ", tags);
            foreach (var tier in new[] { 1, 5, 10 })
            {
                var caption = $"{{ {kind} Modifier \"{mod.Name}\" (Tier: {tier}){tagText} }}";
                if (modClasses.Count == 0) Add(null, caption, "caption");
                else foreach (var cls in modClasses) Add(cls, caption, "caption");
            }
            if (modClasses.Count == 0) Add(null, mod.Name, "affix-name");
            else foreach (var cls in modClasses) Add(cls, mod.Name, "affix-name");
        }

        // Jewel usage is item text, not unique flavour.
        Add("Jewel", "Place into an allocated Jewel Socket on the Passive Skill Tree. Right click to remove from the Socket.", "usage");

        var dataVersion = CatalogBuilder.ReadVersionFile(rawDir);
        return new VendorCorpusFile
        {
            Game = game,
            DataVersion = dataVersion,
            Lines = lines
                .OrderBy(x => x.ClassId ?? "", StringComparer.Ordinal)
                .ThenBy(x => x.Line, StringComparer.Ordinal)
                .ThenBy(x => x.Kind, StringComparer.Ordinal)
                .Select(x => new VendorLine(x.ClassId, x.Line, x.Kind))
                .ToList(),
        };
    }

    private static void RenderBaseProperties(
        RePoESchema.BaseItem item,
        string? cls,
        Action<string?, string?, string> add)
    {
        var props = item.Properties;
        if (props is null) return;

        AddRange("Armour", "armour");
        AddRange("Evasion Rating", "evasion");
        AddRange("Energy Shield", "energy_shield");
        AddRange("Ward", "ward");

        if (TryNumber(props, "block", out var block))
            add(cls, $"Block chance: {Format(block, 1)}%", "base-property");
        if (TryNumber(props, "movement_speed", out var move))
            add(cls, $"Movement Speed: {Format(move / 1000, 1)}%", "base-property");
        if (TryNumber(props, "physical_damage_min", out var pmin) &&
            TryNumber(props, "physical_damage_max", out var pmax))
            add(cls, $"Physical Damage: {Format(pmin, 0)}-{Format(pmax, 0)}", "base-property");
        if (TryNumber(props, "critical_strike_chance", out var crit))
            add(cls, $"Critical Hit Chance: {Format(crit / 100, 2)}%", "base-property");
        if (TryNumber(props, "attack_time", out var attack) && attack > 0)
            add(cls, $"Attacks per Second: {Format(1000 / attack, 2)}", "base-property");
        if (TryNumber(props, "reload_time", out var reload))
            add(cls, $"Reload Time: {Format(reload / 1000, 2)}", "base-property");
        if (TryNumber(props, "range", out var range))
            add(cls, $"Range: {Format(range, 0)}", "base-property");

        void AddRange(string label, string key)
        {
            if (!TryRange(props, key, out var min, out var max)) return;
            var text = Math.Abs(min - max) < 0.0001
                ? $"{label}: {Format(min, 0)}"
                : $"{label}: {Format(min, 0)}-{Format(max, 0)}";
            add(cls, text, "base-property");
        }
    }

    private static bool TryRange(
        Dictionary<string, JsonElement> props, string key, out double min, out double max)
    {
        min = max = 0;
        if (!props.TryGetValue(key, out var el)) return false;
        if (el.ValueKind == JsonValueKind.Number)
        {
            min = max = el.GetDouble();
            return true;
        }
        if (el.ValueKind != JsonValueKind.Object) return false;
        var hasMin = el.TryGetProperty("min", out var minEl) && minEl.ValueKind == JsonValueKind.Number;
        var hasMax = el.TryGetProperty("max", out var maxEl) && maxEl.ValueKind == JsonValueKind.Number;
        if (!hasMin && !hasMax) return false;
        min = hasMin ? minEl.GetDouble() : 0;
        max = hasMax ? maxEl.GetDouble() : min;
        return true;
    }

    private static bool TryNumber(Dictionary<string, JsonElement> props, string key, out double value)
    {
        value = 0;
        if (!props.TryGetValue(key, out var el)) return false;
        if (el.ValueKind != JsonValueKind.Number) return false;
        value = el.GetDouble();
        return true;
    }

    private static string Format(double value, int decimals) =>
        value.ToString("0." + new string('0', decimals), CultureInfo.InvariantCulture)
            .TrimEnd('0').TrimEnd('.');

    private static List<string> DisplayTags(List<string>? tags)
    {
        if (tags is null) return [];
        var result = new List<string>();
        foreach (var tag in tags)
        {
            if (string.IsNullOrWhiteSpace(tag)) continue;
            var text = string.Join(' ', tag.Split('_', StringSplitOptions.RemoveEmptyEntries)
                .Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
            if (!result.Contains(text, StringComparer.OrdinalIgnoreCase)) result.Add(text);
        }
        return result;
    }

    private static string HumanizeSkill(string metadata)
    {
        var name = metadata.Split('/').LastOrDefault() ?? "";
        if (name.StartsWith("SkillGem", StringComparison.OrdinalIgnoreCase)) name = name["SkillGem".Length..];
        var chars = new List<char>();
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (c == '_') { chars.Add(' '); continue; }
            if (i > 0 && char.IsUpper(c) && char.IsLower(name[i - 1])) chars.Add(' ');
            chars.Add(c);
        }
        return new string(chars.ToArray()).Trim();
    }
}

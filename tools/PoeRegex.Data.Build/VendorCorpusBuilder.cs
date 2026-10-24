using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using PoeRegex.Core.Compilation;
using PoeRegex.Core.Data;
using PoeRegex.Data.Build.RePoE;
using PoeRegex.Data.Build.Rendering;

namespace PoeRegex.Data.Build;

/// <summary>
/// Builds the vendor/stash searchable-text corpus from raw RePoE data.
///
/// The selectable catalog is only explicit prefixes/suffixes. This corpus
/// additionally covers base items, implicits, corrupted/essence/crafted mods,
/// affix captions, base properties, requirements, sockets, and regular/base
/// names. Unique-only mods, unique names, flavour, and random rare names are
/// deliberately excluded and remain unmodeled.
///
/// Every stat line is emitted in the forms the game actually displays:
///  - the representative max roll without a range,
///  - the min roll without a range,
///  - the advanced-description range form, e.g. <c>+8(5-8) to Strength</c>
///    or <c>6(6-13)% increased Armour and Evasion</c>, for both min and max
///    rolls.
/// All variants carry the catalog's value-agnostic pattern so the separator
/// oracle treats them as one stat, not as foreign lines.
/// </summary>
public static class VendorCorpusBuilder
{
    private static readonly HashSet<string> IncludedGenerations = new(StringComparer.Ordinal)
    {
        "prefix", "suffix", "corrupted", "essence", "crafted", "implicit",
        "talisman", "blight", "synthesis_a", "synthesis_globals", "synthesis_bonus",
    };

    private static readonly Regex ZeroAddedDamage = new(
        @"^Adds 0(?:\.0+)? to 0(?:\.0+)?\s",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex RangePair = new(
        @"([+-]?\d+(?:\.\d+)?)\s*[-\u2013\u2014]\s*([+-]?\d+(?:\.\d+)?)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

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

        var lines = new HashSet<(string? ClassId, string Line, string Kind, string? Pattern)>();

        void Add(string? classId, string? line, string kind, string? pattern = null)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            var clean = line.Trim();
            if (clean.Length == 0) return;
            // Zero-to-zero added-damage entries are base-item placeholders,
            // not a searchable roll; keeping them would force recall of a line
            // that never appears on a real item.
            if (ZeroAddedDamage.IsMatch(clean)) return;
            lines.Add((classId, clean, kind, pattern));
        }

        void AddForClasses(IEnumerable<string> classes, string? line, string kind, string? pattern = null)
        {
            var list = classes.Distinct(StringComparer.Ordinal).ToList();
            if (list.Count == 0) Add(null, line, kind, pattern);
            else foreach (var cls in list) Add(cls, line, kind, pattern);
        }

        // Adds the rendered stat line in every display form that can appear in
        // an advanced description. The pattern is always the catalog pattern
        // (the max-roll, range-free form), so the selector's recall check sees
        // all real forms of one stat rather than treating a range as a
        // different stat.
        void AddRenderedVariants(IEnumerable<string> classes, StatTextRenderer.RenderedLine rendered, string kind)
        {
            // The catalog's pattern key is the max-roll, range-free shape, but
            // an advanced item text does not show that line for a stat with a
            // roll range: it shows e.g. "6(6-13)% increased Armour and
            // Evasion". Emit the actual display form(s) and do not require the
            // catalog representative as a separate recall target. A stat with
            // min == max has no parenthetical range and uses the plain line.
            var ranged = rendered.RangeLine;
            if (ranged.Length == 0 || string.Equals(ranged, rendered.Line, StringComparison.Ordinal))
            {
                AddForClasses(classes, rendered.Line, kind, rendered.Pattern);
                return;
            }

            var minDisplay = ReplaceRanges(ranged, useMax: false, withRange: true);
            var maxDisplay = ReplaceRanges(ranged, useMax: true, withRange: true);
            // Some translation formats put the '+' only on the low end of the
            // range line. The advanced description decorates the roll, so keep
            // the representative line's leading sign on both variants.
            if (rendered.Line.StartsWith('+'))
            {
                if (!minDisplay.StartsWith('+')) minDisplay = "+" + minDisplay;
                if (!maxDisplay.StartsWith('+')) maxDisplay = "+" + maxDisplay;
            }
            AddForClasses(classes, minDisplay, kind, rendered.Pattern);
            AddForClasses(classes, maxDisplay, kind, rendered.Pattern);
        }

        void AddModLines(string modId, RePoESchema.Mod mod, string kind, IEnumerable<string>? forcedClasses = null)
        {
            var rendered = renderer.RenderMod(mod.Stats);
            if (rendered.Count == 0) return;
            var classes = forcedClasses?.ToList()
                          ?? (classByMod.TryGetValue(modId, out var set) ? set.ToList() : []);
            foreach (var line in rendered) AddRenderedVariants(classes, line, kind);
        }

        void AddModCaption(RePoESchema.Mod mod, string kind, IEnumerable<string>? forcedClasses = null)
        {
            var classes = forcedClasses?.ToList() ?? [];
            var tags = DisplayTags(mod.ImplicitTags);
            var tagText = tags.Count == 0 ? "" : " — " + string.Join(", ", tags);
            var label = kind switch
            {
                "prefix" => "Prefix",
                "suffix" => "Suffix",
                "implicit" => "Implicit",
                "corrupted" => "Corrupted",
                "essence" => "Essence",
                "crafted" => "Crafted",
                _ => "Modifier",
            };
            var tiers = kind is "prefix" or "suffix" or "corrupted" or "essence" or "crafted"
                ? Enumerable.Range(1, 12)
                : new[] { 0 };
            foreach (var tier in tiers)
            {
                var tierText = tier == 0 ? "" : $" (Tier: {tier})";
                var nameText = string.IsNullOrWhiteSpace(mod.Name) ? "" : $" \"{mod.Name}\"";
                AddForClasses(classes,
                    $"{{ {label} Modifier{nameText}{tierText}{tagText} }}",
                    kind == "implicit" ? "implicit-caption" : "caption");
            }
        }

        // All item-domain non-unique stat lines, including corrupted/essence/
        // crafted and the extra regular tiers the selectable catalog omits.
        foreach (var (modId, mod) in mods)
        {
            if (mod.Domain != "item") continue;
            if (!IncludedGenerations.Contains(mod.GenerationType)) continue;
            AddModLines(modId, mod, KindForGeneration(mod.GenerationType));
        }

        var prefixNamesByClass = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var suffixNamesByClass = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var baseNamesByClass = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        void AddName(string cls, string name, string kind)
        {
            Add(cls, name, kind);
            if (!baseNamesByClass.TryGetValue(cls, out var list))
            {
                list = [];
                baseNamesByClass[cls] = list;
            }
            if (!list.Contains(name, StringComparer.Ordinal)) list.Add(name);
        }

        // Base items: names, properties, requirements, granted skills, and
        // implicit mod headers/stat lines. Unique-only/legacy/unreleased bases
        // are out of the normal/craftable vendor/stash scope.
        foreach (var baseItem in baseItems.Values)
        {
            if (baseItem.Domain != "item") continue;
            if (!string.Equals(baseItem.ReleaseState, "released", StringComparison.OrdinalIgnoreCase)) continue;
            if (string.IsNullOrWhiteSpace(baseItem.Name)) continue;
            var cls = ResolveClass(baseItem.ItemClass);
            Add(cls, baseItem.Name, "base-name");
            if (cls is not null) AddName(cls, baseItem.Name, "base-name");

            foreach (var implicitId in baseItem.Implicits ?? [])
            {
                if (!mods.TryGetValue(implicitId, out var implicitMod)) continue;
                AddModLines(implicitId, implicitMod, "implicit", cls is null ? null : [cls]);
                AddModCaption(implicitMod, "implicit", cls is null ? null : [cls]);
            }

            RenderBaseProperties(baseItem, cls, Add);

            if (baseItem.Requirements is { Level: > 0 } req)
            {
                var parts = new List<string> { $"Level {req.Level}" };
                if (req.Strength > 0) parts.Add($"{req.Strength} Str");
                if (req.Dexterity > 0) parts.Add($"{req.Dexterity} Dex");
                if (req.Intelligence > 0) parts.Add($"{req.Intelligence} Int");
                Add(cls, "Requires: " + string.Join(", ", parts), "base-property");
                for (var i = 1; i < parts.Count; i++)
                {
                    var unmet = parts.ToList();
                    unmet[i] = parts[i] + " (unmet)";
                    Add(cls, "Requires: " + string.Join(", ", unmet), "base-property");
                }
            }

            foreach (var skill in baseItem.SkillsGranted ?? [])
            {
                var name = HumanizeSkill(skill);
                if (name.Length == 0) continue;
                Add(cls, $"Grants Skill: {name}", "base-property");
                Add(cls, $"Grants Skill: Level 1 {name}", "base-property");
                Add(cls, $"Grants Skill: Level 10 {name}", "base-property");
                Add(cls, $"Grants Skill: Level 18 {name}", "base-property");
            }
        }

        // Regular affix names, captions, and tier/tag headers. The game's own
        // tier numbers are not derivable from RePoE, so cover the full realistic
        // 1..12 range; this is intentionally conservative.
        foreach (var (modId, mod) in mods)
        {
            if (mod.Domain != "item") continue;
            if (!IncludedGenerations.Contains(mod.GenerationType)) continue;
            var modClasses = classByMod.TryGetValue(modId, out var set) ? set.ToList() : [];
            var kind = KindForGeneration(mod.GenerationType);
            AddModCaption(mod, kind, modClasses);
            if (string.IsNullOrWhiteSpace(mod.Name)) continue;
            AddForClasses(modClasses, mod.Name, "affix-name");
            if (mod.GenerationType is "prefix") AddNames(prefixNamesByClass, modClasses, mod.Name);
            if (mod.GenerationType is "suffix") AddNames(suffixNamesByClass, modClasses, mod.Name);
        }

        // Magic names are prefix + base + suffix; model the common one- and
        // two-affix shapes without exploding into every pair on every base.
        foreach (var (cls, bases) in baseNamesByClass)
        {
            var prefixes = prefixNamesByClass.GetValueOrDefault(cls) ?? [];
            var suffixes = suffixNamesByClass.GetValueOrDefault(cls) ?? [];
            foreach (var baseName in bases.Distinct(StringComparer.Ordinal))
            {
                if (prefixes.Count > 0) Add(cls, prefixes[0] + " " + baseName, "magic-name");
                if (suffixes.Count > 0) Add(cls, baseName + " " + suffixes[0], "magic-name");
                if (prefixes.Count > 0 && suffixes.Count > 0)
                {
                    Add(cls, prefixes[0] + " " + baseName + " " + suffixes[0], "magic-name");
                    if (prefixes.Count > 1)
                        Add(cls, prefixes[1] + " " + baseName + " " + suffixes[0], "magic-name");
                    if (suffixes.Count > 1)
                        Add(cls, prefixes[0] + " " + baseName + " " + suffixes[1], "magic-name");
                }
            }
        }

        // Socket counts are random, not part of the base record.
        foreach (var sockets in new[] { "S", "S S", "S S S", "S S S S", "S S S S S", "S S S S S S" })
            Add(null, "Sockets: " + sockets, "base-property");

        // Quality is item text too.
        Add(null, "Quality: +20%", "base-property");
        Add(null, "Quality: +10%", "base-property");

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
                .ThenBy(x => x.Pattern ?? "", StringComparer.Ordinal)
                .Select(x => new VendorLine(x.ClassId, x.Line, x.Kind, x.Pattern))
                .ToList(),
        };
    }

    private static string KindForGeneration(string generation) => generation switch
    {
        "prefix" => "prefix",
        "suffix" => "suffix",
        "implicit" => "implicit",
        "corrupted" => "corrupted",
        "essence" => "essence",
        "crafted" => "crafted",
        _ => "mod",
    };

    private static void AddNames(Dictionary<string, List<string>> map, IEnumerable<string> classes, string name)
    {
        foreach (var cls in classes)
        {
            if (!map.TryGetValue(cls, out var list))
            {
                list = [];
                map[cls] = list;
            }
            if (!list.Contains(name, StringComparer.Ordinal)) list.Add(name);
        }
    }

    /// <summary>Replaces each `low–high` range with a single value or the
    /// advanced-description `roll(low-high)` form.</summary>
    private static string ReplaceRanges(string text, bool useMax, bool withRange)
    {
        return RangePair.Replace(text, match =>
        {
            var low = match.Groups[1].Value;
            var high = match.Groups[2].Value;
            var hasPlus = low.StartsWith('+') || high.StartsWith('+');
            var lowValue = low.TrimStart('+');
            var highValue = high.TrimStart('+');
            var roll = hasPlus ? "+" + (useMax ? highValue : lowValue) : (useMax ? highValue : lowValue);
            return withRange ? $"{roll}({lowValue}-{highValue})" : roll;
        });
    }

    private static void RenderBaseProperties(
        RePoESchema.BaseItem item,
        string? cls,
        Action<string?, string?, string, string?> add)
    {
        var props = item.Properties;
        if (props is null) return;

        AddRange("Armour", "armour");
        AddRange("Evasion Rating", "evasion");
        AddRange("Energy Shield", "energy_shield");
        AddRange("Ward", "ward");

        if (TryNumber(props, "block", out var block))
            add(cls, $"Block chance: {Format(block, 1)}%", "base-property", null);
        if (TryNumber(props, "movement_speed", out var move))
            add(cls, $"Movement Speed: {Format(move / 1000, 1)}%", "base-property", null);
        if (TryNumber(props, "physical_damage_min", out var pmin) &&
            TryNumber(props, "physical_damage_max", out var pmax))
            add(cls, $"Physical Damage: {Format(pmin, 0)}-{Format(pmax, 0)}", "base-property", null);
        if (TryNumber(props, "critical_strike_chance", out var crit))
            add(cls, $"Critical Hit Chance: {Format(crit / 100, 2)}%", "base-property", null);
        if (TryNumber(props, "attack_time", out var attack) && attack > 0)
            add(cls, $"Attacks per Second: {Format(1000 / attack, 2)}", "base-property", null);
        if (TryNumber(props, "reload_time", out var reload))
            add(cls, $"Reload Time: {Format(reload / 1000, 2)}", "base-property", null);
        if (TryNumber(props, "range", out var range))
            add(cls, $"Range: {Format(range, 0)}", "base-property", null);

        void AddRange(string label, string key)
        {
            if (!TryRange(props, key, out var min, out var max)) return;
            var text = Math.Abs(min - max) < 0.0001
                ? $"{label}: {Format(min, 0)}"
                : $"{label}: {Format(min, 0)}-{Format(max, 0)}";
            add(cls, text, "base-property", null);

            // Quality and other local modifiers mark the property augmented;
            // the advanced description still exposes the number.
            var value = Math.Abs(min - max) < 0.0001 ? Format(min, 0) : $"{Format(min, 0)}-{Format(max, 0)}";
            add(cls, $"{label}: {value} (augmented)", "base-property", null);
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

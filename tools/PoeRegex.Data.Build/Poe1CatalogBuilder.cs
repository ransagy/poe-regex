using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using PoeRegex.Core.Compilation;
using PoeRegex.Core.Data;
using PoeRegex.Data.Build.RePoE;

namespace PoeRegex.Data.Build;

/// <summary>
/// Builds the PoE1 normal-equipment MVP catalog and the foreign-line corpus
/// used by the exact oracle. PoE1 mods already carry rendered English text, so
/// this path reads the raw RePoE <c>text</c> field instead of re-rendering
/// stats through the PoE2 translation tables.
/// </summary>
public static class Poe1CatalogBuilder
{
    public static readonly IReadOnlyList<string> Equipment =
    [
        "Helmet", "Body Armour", "Gloves", "Boots", "Belt", "Amulet", "Ring",
        "Shield", "Quiver", "Claw", "Dagger", "Wand", "One Hand Sword",
        "Thrusting One Hand Sword", "One Hand Axe", "One Hand Mace", "Sceptre",
        "Bow", "Staff", "Two Hand Sword", "Two Hand Axe", "Two Hand Mace",
        "Warstaff",
    ];

    private static readonly string[] InfluenceSuffixes =
    [
        "_shaper", "_elder", "_crusader", "_redeemer", "_hunter", "_warlord",
        "_eyrie", "_adjudicator", "_basilisk",
    ];

    private static readonly string[] LegacyBaseTags =
    [
        "talisman", "demigods", "trade_market_legacy_item",
    ];

    private static readonly string[] LegacyBasePathMarkers =
    [
        "Royale", "Descent", "Victor", "Demigods", "Wreath", "MirrorRing",
        "StormBlade", "TwoHandSwordDev",
    ];

    private static readonly string[] LegacyModIdMarkers =
    [
        "Royale", "Descent", "Demigod", "Victor", "Talisman", "StormBlade",
    ];

    /// <summary>
    /// True for base records that are not current normal equipment: old
    /// Talisman bases, race/reward bases (Demigod's, Descent, Victor,
    /// Royale), legacy trade-market base variants, and skill/test bases.
    /// These records still exist in RePoE but their implicits and mods are
    /// not obtainable on current normal items.
    /// </summary>
    public static bool IsLegacyBase(string path, RePoESchema.BaseItem baseItem)
    {
        var tags = baseItem.Tags ?? [];
        if (tags.Any(tag => LegacyBaseTags.Contains(tag, StringComparer.OrdinalIgnoreCase)))
            return true;
        return LegacyBasePathMarkers.Any(marker =>
            path.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>True for mod ids that belong to legacy/event/race data.</summary>
    public static bool IsLegacyModId(string modId) =>
        LegacyModIdMarkers.Any(marker =>
            modId.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static readonly Regex Range = new(
        @"\(([-+]?\d+(?:\.\d+)?)\s*-\s*([-+]?\d+(?:\.\d+)?)\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex Number = new(
        @"[-+]?\d+(?:\.\d+)?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private sealed record ParsedLine(
        string Line, string RangeLine, string Pattern, double MinValue, double MaxValue);

    public sealed record BuildResult(Catalog Catalog, VendorCorpusFile Vendor);

    public static BuildResult Build(string rawDir, string dataVersion)
    {
        var mods = RePoEJson.LoadMods(Path.Combine(rawDir, "mods.min.json"));
        var bases = RePoEJson.LoadBaseItems(Path.Combine(rawDir, "base_items.min.json"));
        var classes = RePoEJson.LoadItemClasses(Path.Combine(rawDir, "item_classes.min.json"));
        var classByMod = MapClasses(
            Path.Combine(rawDir, "mods_by_base.min.json"), classes, bases);

        var nameToId = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (id, cls) in classes)
            nameToId.TryAdd(cls.Name, id);

        var normal = classByMod
            .Where(kv => !IsLegacyModId(kv.Key) &&
                         mods.TryGetValue(kv.Key, out var mod) &&
                         mod.Domain == "item" &&
                         mod.GenerationType is "prefix" or "suffix" &&
                         !string.IsNullOrEmpty(mod.Text) &&
                         !mod.IsEssenceOnly)
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);

        var corrupted = classByMod
            .Where(kv => !IsLegacyModId(kv.Key) &&
                         mods.TryGetValue(kv.Key, out var mod) &&
                         mod.Domain == "item" &&
                         mod.GenerationType == "corrupted" &&
                         !string.IsNullOrEmpty(mod.Text))
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);

        var implicitByMod = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var (basePath, baseItem) in bases)
        {
            if (!string.Equals(baseItem.ReleaseState, "released", StringComparison.Ordinal))
                continue;
            if (!Equipment.Contains(baseItem.ItemClass, StringComparer.Ordinal)) continue;
            if (IsLegacyBase(basePath, baseItem)) continue;
            foreach (var modId in baseItem.Implicits ?? [])
            {
                if (IsLegacyModId(modId)) continue;
                if (!mods.TryGetValue(modId, out var mod) ||
                    string.IsNullOrEmpty(mod.Text)) continue;
                if (!implicitByMod.TryGetValue(modId, out var set))
                    implicitByMod[modId] = set = new HashSet<string>(StringComparer.Ordinal);
                set.Add(baseItem.ItemClass);
            }
        }

        var parsed = new Dictionary<string, List<ParsedLine>>(StringComparer.Ordinal);
        List<ParsedLine> LinesOf(string modId)
        {
            if (parsed.TryGetValue(modId, out var cached)) return cached;
            var result = new List<ParsedLine>();
            foreach (var raw in mods[modId].Text!.Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                result.Add(ParseLine(raw));
            }
            parsed[modId] = result;
            return result;
        }

        var normalPatterns = Equipment.ToDictionary(
            c => c, _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
        foreach (var (modId, classSet) in normal)
        foreach (var line in LinesOf(modId))
        foreach (var cls in classSet)
            normalPatterns[cls].Add(line.Pattern);

        // Keep only the classes where the implicit text is not already a
        // normal affix pattern. Otherwise a base implicit that shares wording
        // with an explicit mod would be offered twice for that class.
        var implicitSelected = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var (modId, classSet) in implicitByMod)
        {
            var classesForImplicit = classSet
                .Where(cls => LinesOf(modId)
                    .Any(line => !normalPatterns[cls].Contains(line.Pattern)))
                .ToHashSet(StringComparer.Ordinal);
            if (classesForImplicit.Count > 0)
                implicitSelected[modId] = classesForImplicit;
        }

        var entries = new List<ModEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var sources = new (string Source, Dictionary<string, HashSet<string>> Mods)[]
        {
            ("", normal),
            ("implicit", implicitSelected),
            ("corrupted", corrupted),
        };
        foreach (var (source, selected) in sources)
        {
            foreach (var (modId, classSet) in selected.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                if (!seen.Add(modId)) continue;
                var mod = mods[modId];
                var lines = LinesOf(modId);
                if (lines.Count == 0) continue;
                entries.Add(new ModEntry
                {
                    Id = modId,
                    Name = mod.Name,
                    GenerationType = source == "implicit" ? "implicit" : mod.GenerationType,
                    Source = source,
                    Family = mod.Type ?? "",
                    RequiredLevel = mod.RequiredLevel,
                    Classes = classSet.OrderBy(c => c, StringComparer.Ordinal).ToList(),
                    Groups = mod.Groups ?? [],
                    StatLines = lines.Select(line => new StatLineEntry
                    {
                        Line = line.Line,
                        RangeLine = line.RangeLine,
                        Pattern = line.Pattern,
                        MinValue = line.MinValue,
                        MaxValue = line.MaxValue,
                        Fingerprints = new Dictionary<string, string>(StringComparer.Ordinal),
                    }).ToList(),
                });
            }
        }

        var catalog = new Catalog
        {
            Game = "poe1",
            DataVersion = dataVersion,
            ItemClasses = Equipment.Select(id => new ItemClassEntry
            {
                Id = id,
                Name = classes[id].Name,
                BaseFingerprint = $"Item Class: {classes[id].Name}",
            }).ToList(),
            Mods = entries,
        };

        return new BuildResult(
            catalog, BuildVendor(catalog, bases, classes, mods, dataVersion));
    }

    private static Dictionary<string, HashSet<string>> MapClasses(
        string modsByBasePath,
        Dictionary<string, RePoESchema.ItemClass> classes,
        Dictionary<string, RePoESchema.BaseItem> bases)
    {
        var nameToId = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (id, cls) in classes)
            nameToId.TryAdd(cls.Name, id);

        var classByMod = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        using var doc = JsonDocument.Parse(File.ReadAllText(modsByBasePath));
        foreach (var classProp in doc.RootElement.EnumerateObject())
        {
            if (!nameToId.TryGetValue(classProp.Name, out var classId) ||
                !Equipment.Contains(classId, StringComparer.Ordinal))
                continue;

            foreach (var tagGroup in classProp.Value.EnumerateObject())
            {
                if (!tagGroup.Value.TryGetProperty("bases", out var basesNode) ||
                    basesNode.ValueKind != JsonValueKind.Array)
                    continue;
                var basePaths = basesNode.EnumerateArray()
                    .Select(x => x.GetString() ?? "")
                    .ToList();
                // Entries backed only by unique, legacy, event, or race bases
                // are outside the current normal-equipment scope. A tag group
                // that also has a current base keeps its mod.
                var currentPaths = basePaths
                    .Where(path => !path.Contains("Unique", StringComparison.Ordinal))
                    .Where(path => !bases.TryGetValue(path, out var item) ||
                                   !IsLegacyBase(path, item))
                    .ToList();
                if (currentPaths.Count == 0)
                    continue;

                if (!tagGroup.Value.TryGetProperty("mods", out var modsNode) ||
                    modsNode.ValueKind != JsonValueKind.Object)
                    continue;
                foreach (var generation in modsNode.EnumerateObject())
                {
                    if (generation.Name is not ("prefix" or "suffix" or "corrupted"))
                        continue;
                    foreach (var group in generation.Value.EnumerateObject())
                    {
                        // The export lists each spawn tag's mods as an object
                        // keyed by mod id (the values are weights).
                        if (group.Value.ValueKind == JsonValueKind.Object)
                        {
                            foreach (var mod in group.Value.EnumerateObject())
                                AddMod(mod.Name);
                        }
                        else if (group.Value.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var mod in group.Value.EnumerateArray())
                                AddMod(mod.GetString());
                        }

                        void AddMod(string? modId)
                        {
                            if (string.IsNullOrEmpty(modId)) return;
                            if (!classByMod.TryGetValue(modId, out var set))
                                classByMod[modId] = set = new HashSet<string>(StringComparer.Ordinal);
                            set.Add(classId);
                        }
                    }
                }
            }
        }
        return classByMod;
    }

    private static ParsedLine ParseLine(string raw)
    {
        var numbers = new List<double>();
        foreach (Match match in Range.Matches(raw))
        {
            numbers.Add(double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));
            numbers.Add(double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture));
        }

        var line = Range.Replace(raw, m =>
        {
            var low = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            var high = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            return Format(Math.Max(low, high));
        });
        numbers.AddRange(Number.Matches(line)
            .Select(m => double.Parse(m.Value, CultureInfo.InvariantCulture)));

        var rangeLine = Range.Replace(raw, m =>
        {
            var low = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            var high = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            return $"{Format(Math.Min(low, high))}\u2013{Format(Math.Max(low, high))}";
        });

        var pattern = Range.Replace(raw, "\u0001");
        pattern = Number.Replace(pattern, "\u0001");
        // The client treats a leading "+" as optional across mod tiers; the
        // same visible line can be copied with or without it. One canonical
        // pattern keeps the table from distinguishing identical text.
        if (pattern.StartsWith('+')) pattern = pattern[1..];

        return new ParsedLine(
            line,
            rangeLine,
            pattern,
            numbers.Count == 0 ? 0 : numbers.Min(),
            numbers.Count == 0 ? 0 : numbers.Max());
    }

    private static string Format(double value) =>
        value.ToString("G6", CultureInfo.InvariantCulture);

    private static VendorCorpusFile BuildVendor(
        Catalog catalog,
        Dictionary<string, RePoESchema.BaseItem> bases,
        Dictionary<string, RePoESchema.ItemClass> classes,
        Dictionary<string, RePoESchema.Mod> mods,
        string dataVersion)
    {
        var lines = new List<VendorLine>();
        var seen = new HashSet<(string?, string)>(new LineKeyComparer());

        void Add(string? classId, string? line, string? kind = null, string? pattern = null)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            var clean = line.Trim();
            if (clean.Length == 0) return;
            if (seen.Add((classId, clean)))
                lines.Add(new VendorLine(classId, clean, kind, pattern));
        }

        IEnumerable<string> CopyForms(string text)
        {
            var plain = Range.Replace(text, m =>
            {
                var low = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                var high = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                return Format(Math.Max(low, high));
            });
            yield return plain;
            var advanced = Range.Replace(text,
                m => $"{Format(Math.Max(
                    double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                    double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)))}({m.Groups[1].Value}-{m.Groups[2].Value})");
            if (advanced != plain) yield return advanced;
        }

        foreach (var mod in catalog.Mods)
        foreach (var statLine in mod.StatLines)
        {
            foreach (var cls in mod.Classes)
            foreach (var form in CopyForms(statLine.Line))
                Add(cls, form, "mod", statLine.Pattern);
            foreach (var form in CopyForms(statLine.Line))
                Add(null, form, "mod", statLine.Pattern);
        }

        foreach (var (basePath, baseItem) in bases
                     .OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (!string.Equals(baseItem.ReleaseState, "released", StringComparison.Ordinal))
                continue;
            if (!Equipment.Contains(baseItem.ItemClass, StringComparer.Ordinal)) continue;
            if (IsLegacyBase(basePath, baseItem)) continue;

            var cls = baseItem.ItemClass;
            Add(cls, baseItem.Name, "base");
            Add(null, baseItem.Name, "base");

            var props = baseItem.Properties ?? [];

            AddRange("Armour", "armour");
            AddRange("Evasion Rating", "evasion");
            AddRange("Energy Shield", "energy_shield");
            AddRange("Ward", "ward");

            if (props.TryGetValue("block", out var block) &&
                block.ValueKind == JsonValueKind.Number)
                Add(cls, $"Chance to Block: {Format(block.GetDouble())}%", "property");

            if (props.TryGetValue("movement_speed", out var move) &&
                move.ValueKind == JsonValueKind.Number)
                Add(cls, $"Movement Speed: {Format(move.GetDouble())}%", "property");

            if (props.TryGetValue("physical_damage_min", out var pmin) &&
                props.TryGetValue("physical_damage_max", out var pmax) &&
                pmin.ValueKind == JsonValueKind.Number &&
                pmax.ValueKind == JsonValueKind.Number)
                Add(cls,
                    $"Physical Damage: {Format(pmin.GetDouble())}-{Format(pmax.GetDouble())}",
                    "property");

            if (props.TryGetValue("critical_strike_chance", out var crit) &&
                crit.ValueKind == JsonValueKind.Number)
                Add(cls,
                    $"Critical Strike Chance: {crit.GetDouble() / 100:0.00}%",
                    "property");

            if (props.TryGetValue("attack_time", out var attack) &&
                attack.ValueKind == JsonValueKind.Number &&
                attack.GetDouble() > 0)
                Add(cls,
                    $"Attacks per Second: {1000 / attack.GetDouble():0.00}",
                    "property");

            if (props.TryGetValue("range", out var range) &&
                range.ValueKind == JsonValueKind.Number)
                Add(cls, $"Weapon Range: {Format(range.GetDouble())}", "property");

            void AddRange(string label, string key)
            {
                if (!props.TryGetValue(key, out var node)) return;
                if (node.ValueKind == JsonValueKind.Object &&
                    node.TryGetProperty("min", out var minNode) &&
                    node.TryGetProperty("max", out var maxNode) &&
                    minNode.ValueKind == JsonValueKind.Number &&
                    maxNode.ValueKind == JsonValueKind.Number)
                {
                    var min = minNode.GetDouble();
                    var max = maxNode.GetDouble();
                    var text = Math.Abs(min - max) < 0.0001
                        ? $"{label}: {Format(min)}"
                        : $"{label}: {Format(min)}-{Format(max)}";
                    Add(cls, text, "property");
                    Add(cls, $"{text} (augmented)", "property");
                }
                else if (node.ValueKind == JsonValueKind.Number)
                {
                    var value = Format(node.GetDouble());
                    Add(cls, $"{label}: {value}", "property");
                    Add(cls, $"{label}: {value} (augmented)", "property");
                }
            }

            if (baseItem.Requirements is { Level: > 0 } req)
            {
                var parts = new List<string> { $"Level {req.Level}" };
                Add(cls, $"Level: {req.Level}", "property");
                if (req.Strength > 0)
                {
                    parts.Add($"{req.Strength} Str");
                    Add(cls, $"Str: {req.Strength}", "property");
                }
                if (req.Dexterity > 0)
                {
                    parts.Add($"{req.Dexterity} Dex");
                    Add(cls, $"Dex: {req.Dexterity}", "property");
                }
                if (req.Intelligence > 0)
                {
                    parts.Add($"{req.Intelligence} Int");
                    Add(cls, $"Int: {req.Intelligence}", "property");
                }
                Add(cls, "Requires: " + string.Join(", ", parts), "property");
            }

            // The base implicit is item text even when the selectable catalog
            // omits it because the same wording is also an explicit affix.
            foreach (var implicitId in baseItem.Implicits ?? [])
            {
                if (IsLegacyModId(implicitId)) continue;
                if (!mods.TryGetValue(implicitId, out var implicitMod) ||
                    string.IsNullOrEmpty(implicitMod.Text)) continue;
                foreach (var raw in implicitMod.Text.Split('\n'))
                {
                    if (string.IsNullOrWhiteSpace(raw)) continue;
                    var parsed = ParseLine(raw);
                    foreach (var form in CopyForms(parsed.Line))
                        Add(cls, form, "implicit", parsed.Pattern);
                }
            }
        }

        // Affix names and advanced-description captions are item text too.
        // They are common on magic items and in imported advanced copies, so
        // the exact table must not pick fragments that match them.
        foreach (var mod in catalog.Mods)
        {
            if (string.IsNullOrWhiteSpace(mod.Name)) continue;
            foreach (var cls in mod.Classes)
                Add(cls, mod.Name, "affix-name");
            Add(null, mod.Name, "affix-name");
        }

        foreach (var mod in catalog.Mods)
        {
            var label = mod.Source switch
            {
                "implicit" => "Implicit",
                "corrupted" => "Corrupted",
                _ => mod.GenerationType == "prefix" ? "Prefix" : "Suffix",
            };
            var name = string.IsNullOrWhiteSpace(mod.Name) ? "" : $" \"{mod.Name}\"";
            var tiers = mod.Source == "implicit" ? [0] : Enumerable.Range(1, 12);
            foreach (var tier in tiers)
            {
                var tierText = tier == 0 ? "" : $" (Tier: {tier})";
                var line = $"{{ {label} Modifier{name}{tierText} }}";
                foreach (var cls in mod.Classes)
                    Add(cls, line, "caption");
                Add(null, line, "caption");
            }
        }

        var socketColors = new[] { "R", "G", "B", "W" };
        var socketLines = new List<string>();
        foreach (var first in socketColors)
        {
            socketLines.Add($"Sockets: {first}");
            foreach (var second in socketColors)
                socketLines.Add($"Sockets: {first}-{second}");
        }
        socketLines.AddRange(
        [
            "Sockets: R-G-B", "Sockets: R-R-G-G", "Sockets: G-G-G", "Sockets: B-B-B",
        ]);

        foreach (var line in new[]
                 {
                     "Rarity: Normal", "Rarity: Magic", "Rarity: Rare", "Rarity: Unique",
                     "Corrupted", "Fractured Item", "Synthesised Item", "Mirrored", "Split",
                 })
            Add(null, line, "property");

        foreach (var cls in Equipment)
        {
            Add(cls, $"Item Class: {classes[cls].Name}", "class");
            Add(null, $"Item Class: {classes[cls].Name}", "class");
            foreach (var line in new[]
                     {
                         "Requirements:", "Level: 80", "Str: 100", "Dex: 100", "Int: 100",
                         "Item Level: 80", "Quality: +20%", "Quality: +17%",
                     })
            {
                Add(cls, line, "property");
                Add(null, line, "property");
            }
            // Socket lines are item text the in-game search sees. They matter
            // for class anchors: a one-letter anchor like ": G" would match
            // "Sockets: G" and satisfy a class guard on the wrong item.
            foreach (var socket in socketLines)
            {
                Add(cls, socket, "property");
                Add(null, socket, "property");
            }
        }

        return new VendorCorpusFile
        {
            Game = "poe1",
            DataVersion = dataVersion,
            Lines = lines,
        };
    }

    private sealed class LineKeyComparer : IEqualityComparer<(string? ClassId, string Line)>
    {
        public bool Equals((string? ClassId, string Line) x, (string? ClassId, string Line) y) =>
            string.Equals(x.ClassId, y.ClassId, StringComparison.Ordinal) &&
            string.Equals(x.Line, y.Line, StringComparison.Ordinal);

        public int GetHashCode((string? ClassId, string Line) obj) =>
            HashCode.Combine(obj.ClassId, obj.Line);
    }
}

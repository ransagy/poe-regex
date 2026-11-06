using PoeRegex.Core;
using PoeRegex.Core.Data;
using PoeRegex.Core.Search;
using PoeRegex.Data.Build.RePoE;
using PoeRegex.Data.Build.Rendering;

namespace PoeRegex.Data.Build;

/// <summary>
/// Builds a derived catalog from raw RePoE exports: renders mod text,
/// constructs per-class corpora, computes fingerprints, and emits the catalog.
/// </summary>
public static class CatalogBuilder
{
    private static readonly string[] ItemGenerationTypes = ["explicit", "prefix", "suffix"];

    public static Catalog Build(string rawDir)
    {
        var mods = RePoEJson.LoadMods(Path.Combine(rawDir, "mods.min.json"));
        var itemClasses = RePoEJson.LoadItemClasses(Path.Combine(rawDir, "item_classes.min.json"));
        var baseItems = RePoEJson.LoadBaseItems(Path.Combine(rawDir, "base_items.min.json"));
        var handlers = RePoEJson.LoadValueHandlers(Path.Combine(rawDir, "stat_value_handlers.min.json"));

        var translations = LoadAllTranslations(rawDir);
        var renderer = new StatTextRenderer(handlers, translations);

        // mod -> item class ids it can spawn on. Normal affixes use
        // mods_by_base; desecrated mods are applied by the Abyss well rather
        // than natural rolls, so their spawn_weights tags are mapped through
        // the released item-base tags instead. Crafted mods are bench/guaranteed
        // crafts with no positive spawn tags at all: defence crafts are inferred
        // from their implicit_tags and the released base tags, and generic
        // meta-crafts apply to every normal equipment class.
        var classByMod = MapModsToClasses(rawDir, itemClasses);
        var equipmentClasses = classByMod
            .Where(kv => mods.TryGetValue(kv.Key, out var mod) &&
                         mod.Domain == "item" &&
                         ItemGenerationTypes.Contains(mod.GenerationType))
            .SelectMany(kv => kv.Value)
            .Where(GameItemClasses.IsInGame)
            .ToHashSet(StringComparer.Ordinal);
        var desecratedByMod = MapDesecratedModsToClasses(mods, baseItems, itemClasses);
        foreach (var (modId, classes) in desecratedByMod)
            classByMod[modId] = classes;
        var craftedByMod = MapCraftedModsToClasses(mods, baseItems, equipmentClasses);
        foreach (var (modId, classes) in craftedByMod)
            classByMod[modId] = classes;

        // 1. Render mod text lines.
        var renderedLines = new Dictionary<string, List<StatTextRenderer.RenderedLine>>();
        var classLines = new Dictionary<string, HashSet<string>>();
        foreach (var (modId, mod) in mods)
        {
            if (!IsSelectableAffix(mod)) continue;
            if (mod.Domain == "desecrated" && !desecratedByMod.ContainsKey(modId)) continue;

            var lines = renderer.RenderMod(mod.Stats);
            if (lines.Count == 0) continue;
            renderedLines[modId] = lines;

            if (!classByMod.TryGetValue(modId, out var classes)) continue;
            foreach (var cls in classes)
            {
                if (!classLines.TryGetValue(cls, out var set))
                {
                    set = new HashSet<string>();
                    classLines[cls] = set;
                }
                foreach (var line in lines) set.Add(line.Pattern);
            }
        }

        // A source-specific line that maps to an item class but renders nothing
        // would silently disappear from the picker and the proof. Fail loudly
        // instead of shipping a partial category.
        var unrendered = desecratedByMod.Keys
            .Where(id => !renderedLines.ContainsKey(id))
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
        if (unrendered.Count > 0)
        {
            throw new InvalidDataException(
                $"desecrated item mods rendered no lines: {string.Join(", ", unrendered)}");
        }
        var craftedUnrendered = craftedByMod.Keys
            .Where(id => !renderedLines.ContainsKey(id))
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
        if (craftedUnrendered.Count > 0)
        {
            throw new InvalidDataException(
                $"crafted item mods rendered no lines: {string.Join(", ", craftedUnrendered)}");
        }
        var craftedUnmapped = craftedByMod
            .Where(kv => kv.Value.Count == 0)
            .Select(kv => kv.Key)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
        if (craftedUnmapped.Count > 0)
        {
            throw new InvalidDataException(
                $"crafted item mods mapped to no classes: {string.Join(", ", craftedUnmapped)}");
        }

        // 2. Per-class fingerprinting.
        //    Each class corpus = distinct normalized mod lines for the class
        //    plus the "Item Class: X" line. Build the suffix index once per class
        //    and fingerprint every line in the class in bulk.
        var baseFingerprint = new Dictionary<string, string>();
        var classIndexCache = new Dictionary<string, SuffixIndex>();

        foreach (var (classId, cls) in itemClasses)
        {
            var classLine = StatTextRenderer.NormalizeDigits($"Item Class: {cls.Name}");
            var corpus = BuildClassCorpus(classLines, classId, classLine);
            var index = SuffixIndex.Build(corpus);
            classIndexCache[classId] = index;

            var classFp = index.ShortestUniqueSubstring(corpus.Count - 1);
            baseFingerprint[classId] = classFp ?? classLine;
        }

        // fingerprints[(modId, pattern)][classId] = shortest unique fragment.
        var fingerprints = new Dictionary<(string ModId, string Pattern), Dictionary<string, string>>();
        foreach (var (modId, lines) in renderedLines)
        {
            if (!classByMod.TryGetValue(modId, out var classes)) continue;
            foreach (var line in lines)
            {
                var perClass = new Dictionary<string, string>();
                foreach (var cls in classes)
                {
                    var index = classIndexCache[cls];
                    var corpus = index.ClassCorpus;
                    var lineIdx = System.Array.FindIndex((string[])corpus, x => x == line.Pattern);
                    if (lineIdx < 0) continue;
                    var fp = index.ShortestUniqueSubstring(lineIdx);
                    if (fp is not null) perClass[cls] = fp;
                }
                fingerprints[(modId, line.Pattern)] = perClass;
            }
        }

        // 4. Emit catalog.
        var itemClassEntries = itemClasses.Select(kv => new ItemClassEntry
        {
            Id = kv.Key,
            Name = kv.Value.Name,
            BaseFingerprint = baseFingerprint.GetValueOrDefault(kv.Key) ?? $"Item Class: {kv.Value.Name}",
        }).ToList();

        var modEntries = new List<ModEntry>();
        foreach (var (modId, lines) in renderedLines)
        {
            if (!classByMod.TryGetValue(modId, out var classes)) continue;
            if (classes.Count == 0) continue;

            var statLines = new List<StatLineEntry>();
            foreach (var line in lines)
            {
                var fpMap = fingerprints.GetValueOrDefault((modId, line.Pattern)) ?? new Dictionary<string, string>();
                statLines.Add(new StatLineEntry
                {
                    Line = line.Line,
                    RangeLine = line.RangeLine,
                    Pattern = line.Pattern,
                    MinValue = line.MinValue,
                    MaxValue = line.MaxValue,
                    Fingerprints = fpMap,
                });
            }

            modEntries.Add(new ModEntry
            {
                Id = modId,
                Name = mods[modId].Name,
                GenerationType = mods[modId].GenerationType,
                Source = mods[modId].Domain switch
                {
                    "desecrated" => "desecrated",
                    "crafted" => "crafted",
                    _ => "",
                },
                Family = mods[modId].Type ?? "",
                RequiredLevel = mods[modId].RequiredLevel,
                Classes = classes.ToList(),
                Groups = mods[modId].Groups ?? [],
                StatLines = statLines,
            });
        }

        var dataVersion = ReadVersionFile(rawDir);

        return new Catalog
        {
            Game = "poe2",
            DataVersion = dataVersion,
            ItemClasses = itemClassEntries,
            Mods = modEntries,
        };
    }

    internal static List<RePoESchema.StatTranslationEntry> LoadAllTranslations(string rawDir)
    {
        var result = new List<RePoESchema.StatTranslationEntry>();
        // PoE 2: translations are a main stat_descriptions list plus split
        // *_stat_descriptions.json files in the stat_translations directory
        // (or alongside the raw files).
        var searchDir = Path.Combine(rawDir, "stat_translations");
        if (!Directory.Exists(searchDir)) searchDir = rawDir;
        foreach (var file in Directory.EnumerateFiles(searchDir, "*.json"))
        {
            var name = Path.GetFileName(file);
            if (name.Contains("_stat_descriptions") || name.Contains("stat_descriptions"))
            {
                result.AddRange(RePoEJson.LoadTranslations(file));
            }
        }
        return result;
    }

    /// <summary>
    /// The catalog's dataVersion is the fetch date of the raw exports: RePoE's
    /// hosted files carry no version field, so the mods file's write time (set
    /// when `fetch` downloaded it) is used.
    /// </summary>
    internal static string ReadVersionFile(string rawDir)
    {
        var modsPath = Path.Combine(rawDir, "mods.min.json");
        if (File.Exists(modsPath)) return File.GetLastWriteTimeUtc(modsPath).ToString("yyyy-MM-dd");
        return "unknown";
    }

    private static List<string> BuildClassCorpus(
        Dictionary<string, HashSet<string>> classLines,
        string classId,
        string classLine)
    {
        var corpus = new List<string>(classLines.TryGetValue(classId, out var set) ? set : new HashSet<string>());
        // Canonical order keeps equal-length tie-breaks stable across processes.
        corpus.Sort(StringComparer.Ordinal);
        // Same universal non-mod lines the runtime oracle sees, so embedded
        // fingerprints avoid them too. The class line must stay LAST: callers
        // fingerprint it via corpus.Count - 1.
        foreach (var u in PoeRegex.Core.Compilation.EngineCorpus.UniversalLines)
            corpus.Add(StatTextRenderer.NormalizeDigits(u));
        corpus.Add(classLine);
        return corpus;
    }

    /// <summary>
    /// Selectable affixes: normal explicit item prefixes/suffixes, Abyss
    /// desecrated prefixes/suffixes, and crafted-domain prefixes/suffixes.
    /// Map/tablet desecrated mods are filtered later because they have no
    /// item-class spawn tags.
    /// </summary>
    private static bool IsSelectableAffix(RePoESchema.Mod mod) =>
        (mod.Domain == "item" && ItemGenerationTypes.Contains(mod.GenerationType)) ||
        (mod.Domain == "desecrated" && mod.GenerationType is "prefix" or "suffix") ||
        (mod.Domain == "crafted" && mod.GenerationType is "prefix" or "suffix");

    /// <summary>
    /// Maps crafted item mods to normal equipment classes. Crafted records do
    /// not carry positive spawn weights (they are guaranteed by the bench or a
    /// crafting method), so the export cannot supply a class list directly.
    /// Defence crafts expose `implicit_tags` naming the defence family; those
    /// are intersected with released base tags. Generic meta-crafts have no
    /// tags and apply to every normal equipment class. This is deliberately
    /// class-level, matching what the rest of the catalog can express.
    /// </summary>
    internal static Dictionary<string, HashSet<string>> MapCraftedModsToClasses(
        IReadOnlyDictionary<string, RePoESchema.Mod> mods,
        IReadOnlyDictionary<string, RePoESchema.BaseItem> baseItems,
        IReadOnlySet<string> equipmentClasses)
    {
        // Released, non-unique bases per class. The defence tags are attribute
        // combinations (`str_armour`, `dex_int_armour`, ...), not just the
        // generic `armour` marker every armour base carries.
        var defenceTagsByClass = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var (path, baseItem) in baseItems)
        {
            if (baseItem.Domain != "item") continue;
            if (!string.Equals(baseItem.ReleaseState, "released", StringComparison.OrdinalIgnoreCase)) continue;
            if (string.IsNullOrWhiteSpace(baseItem.Name)) continue;
            if (path.Contains("Unique", StringComparison.OrdinalIgnoreCase)) continue;
            if (!equipmentClasses.Contains(baseItem.ItemClass)) continue;
            if (!defenceTagsByClass.TryGetValue(baseItem.ItemClass, out var tags))
            {
                tags = new HashSet<string>(StringComparer.Ordinal);
                defenceTagsByClass[baseItem.ItemClass] = tags;
            }
            foreach (var tag in baseItem.Tags ?? [])
                tags.Add(tag);
        }

        var armour = new[] { "str_armour", "str_dex_armour", "str_int_armour", "str_dex_int_armour" };
        var evasion = new[] { "dex_armour", "str_dex_armour", "dex_int_armour", "str_dex_int_armour" };
        var energyShield = new[] { "int_armour", "str_int_armour", "dex_int_armour", "str_dex_int_armour" };

        var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var (modId, mod) in mods)
        {
            if (mod.Domain != "crafted") continue;
            if (mod.GenerationType is not ("prefix" or "suffix")) continue;

            var implicitTags = (mod.ImplicitTags ?? []).ToHashSet(StringComparer.Ordinal);
            var defenceTags = implicitTags.Contains("armour") ? armour
                : implicitTags.Contains("evasion") ? evasion
                : implicitTags.Contains("energy_shield") ? energyShield
                : null;

            var classes = new HashSet<string>(StringComparer.Ordinal);
            if (defenceTags is null)
            {
                // Generic meta-craft: applies to every normal equipment class.
                classes.UnionWith(equipmentClasses);
            }
            else
            {
                foreach (var (classId, tags) in defenceTagsByClass)
                {
                    if (defenceTags.Any(tags.Contains)) classes.Add(classId);
                }
            }
            result[modId] = classes;
        }
        return result;
    }

    /// <summary>
    /// Maps desecrated item mods to classes from their positive spawn_weights
    /// tags and the released, non-unique base items. A base is eligible when
    /// it matches at least one positive tag and no zero-weight exclusion tag
    /// (the catch-all `default: 0` is ignored when a positive tag matched).
    /// Desecrated mods are not in mods_by_base (the Abyss well applies them
    /// rather than natural rolls), so this is the authoritative mapping
    /// available in the export.
    /// </summary>
    internal static Dictionary<string, HashSet<string>> MapDesecratedModsToClasses(
        IReadOnlyDictionary<string, RePoESchema.Mod> mods,
        IReadOnlyDictionary<string, RePoESchema.BaseItem> baseItems,
        IReadOnlyDictionary<string, RePoESchema.ItemClass> itemClasses)
    {
        var bases = new List<(string ClassId, HashSet<string> Tags)>();
        foreach (var (path, baseItem) in baseItems)
        {
            if (baseItem.Domain != "item") continue;
            if (!string.Equals(baseItem.ReleaseState, "released", StringComparison.OrdinalIgnoreCase)) continue;
            if (string.IsNullOrWhiteSpace(baseItem.Name)) continue;
            if (path.Contains("Unique", StringComparison.OrdinalIgnoreCase)) continue;
            if (!itemClasses.ContainsKey(baseItem.ItemClass)) continue;
            bases.Add((baseItem.ItemClass, new HashSet<string>(baseItem.Tags ?? [], StringComparer.Ordinal)));
        }

        var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var (modId, mod) in mods)
        {
            if (mod.Domain != "desecrated") continue;
            if (mod.GenerationType is not ("prefix" or "suffix")) continue;

            var positiveTags = (mod.SpawnWeights ?? [])
                .Where(w => w.Weight > 0 && !string.Equals(w.Tag, "default", StringComparison.Ordinal))
                .Select(w => w.Tag)
                .ToHashSet(StringComparer.Ordinal);
            if (positiveTags.Count == 0) continue;
            var excludedTags = (mod.SpawnWeights ?? [])
                .Where(w => w.Weight == 0 && !string.Equals(w.Tag, "default", StringComparison.Ordinal))
                .Select(w => w.Tag)
                .ToHashSet(StringComparer.Ordinal);

            var classes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (classId, tags) in bases)
            {
                if (!tags.Overlaps(positiveTags)) continue;
                if (tags.Overlaps(excludedTags)) continue;
                classes.Add(classId);
            }
            if (classes.Count > 0) result[modId] = classes;
        }
        return result;
    }

    /// <summary>
    /// Maps each mod to the item classes whose bases it can spawn on using
    /// mods_by_base.json (authoritative RePoE structure):
    /// class name → tag group → {bases, mods: {generation → group → {mod_id → weight}}}.
    /// </summary>
    internal static Dictionary<string, HashSet<string>> MapModsToClasses(
        string rawDir,
        Dictionary<string, RePoESchema.ItemClass> itemClasses)
    {
        var result = new Dictionary<string, HashSet<string>>();
        var path = Path.Combine(rawDir, "mods_by_base.min.json");
        if (!File.Exists(path)) return result;

        // item class human name → item class id
        var nameToId = new Dictionary<string, string>();
        foreach (var (id, cls) in itemClasses) nameToId[cls.Name] = id;

        var json = File.ReadAllText(path);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        foreach (var classProp in doc.RootElement.EnumerateObject())
        {
            if (!nameToId.TryGetValue(classProp.Name, out var classId)) continue;
            var classes = new HashSet<string> { classId };
            foreach (var tagGroup in classProp.Value.EnumerateObject())
            {
                if (!tagGroup.Value.TryGetProperty("mods", out var modsEl)) continue;
                if (!tagGroup.Value.TryGetProperty("bases", out var basesEl) ||
                    !HasRegularBase(basesEl)) continue;
                foreach (var genProp in modsEl.EnumerateObject())
                {
                    foreach (var groupProp in genProp.Value.EnumerateObject())
                    {
                        foreach (var modProp in groupProp.Value.EnumerateObject())
                        {
                            if (!result.TryGetValue(modProp.Name, out var set))
                            {
                                set = new HashSet<string>();
                                result[modProp.Name] = set;
                            }
                            set.Add(classId);
                        }
                    }
                }
            }
        }

        return result;
    }

    /// <summary>
    /// True when a mods_by_base tag group has at least one non-unique base.
    /// Unique bases are included in RePoE's mods_by_base export, but their
    /// affixes are fixed unique mods, not selectable random affixes. Mapping a
    /// regular prefix/suffix through a unique-only base creates false class
    /// coverage (e.g. reduced critical damage on Bucklers).
    /// </summary>
    private static bool HasRegularBase(System.Text.Json.JsonElement bases)
    {
        foreach (var baseItem in bases.EnumerateArray())
        {
            var path = baseItem.GetString();
            if (string.IsNullOrEmpty(path)) continue;
            if (!path.Contains("Unique", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}

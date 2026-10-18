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

    public static Catalog Build(string game, string rawDir)
    {
        var mods = RePoEJson.LoadMods(Path.Combine(rawDir, "mods.min.json"));
        var itemClasses = RePoEJson.LoadItemClasses(Path.Combine(rawDir, "item_classes.min.json"));
        var baseItems = RePoEJson.LoadBaseItems(Path.Combine(rawDir, "base_items.min.json"));
        var handlers = RePoEJson.LoadValueHandlers(Path.Combine(rawDir, "stat_value_handlers.min.json"));

        var translations = LoadAllTranslations(game, rawDir);
        var renderer = new StatTextRenderer(handlers, translations);

        // mod -> item class ids it can spawn on
        var classByMod = MapModsToClasses(rawDir, itemClasses);

        // 1. Render mod text lines.
        var renderedLines = new Dictionary<string, List<StatTextRenderer.RenderedLine>>();
        var classLines = new Dictionary<string, HashSet<string>>();
        foreach (var (modId, mod) in mods)
        {
            if (!ItemGenerationTypes.Contains(mod.GenerationType)) continue;
            if (mod.Domain != "item") continue;

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
                Family = mods[modId].Type ?? "",
                RequiredLevel = mods[modId].RequiredLevel,
                Classes = classes.ToList(),
                Groups = mods[modId].Groups ?? [],
                StatLines = statLines,
            });
        }

        var dataVersion = ReadVersionFile(game, rawDir);

        return new Catalog
        {
            Game = game,
            DataVersion = dataVersion,
            ItemClasses = itemClassEntries,
            Mods = modEntries,
        };
    }

    private static List<RePoESchema.StatTranslationEntry> LoadAllTranslations(string game, string rawDir)
    {
        var result = new List<RePoESchema.StatTranslationEntry>();
        if (game == "poe1")
        {
            var path = Path.Combine(rawDir, "stat_translations.min.json");
            if (File.Exists(path))
            {
                result.AddRange(RePoEJson.LoadTranslations(path));
            }
            return result;
        }

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

    private static string ReadVersionFile(string game, string rawDir)
    {
        // RePoE serves the version in the HTML index; fall back to the mods file mtime.
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
        // Same universal non-mod lines the runtime oracle sees, so embedded
        // fingerprints avoid them too. The class line must stay LAST: callers
        // fingerprint it via corpus.Count - 1.
        foreach (var u in PoeRegex.Core.Compilation.EngineCorpus.UniversalLines)
            corpus.Add(StatTextRenderer.NormalizeDigits(u));
        corpus.Add(classLine);
        return corpus;
    }

    /// <summary>
    /// Maps each mod to the item classes whose bases it can spawn on using
    /// mods_by_base.json (authoritative RePoE structure):
    /// class name → tag group → {bases, mods: {generation → group → {mod_id → weight}}}.
    /// </summary>
    private static Dictionary<string, HashSet<string>> MapModsToClasses(
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
}

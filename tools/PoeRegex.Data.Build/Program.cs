using PoeRegex.Core;
using PoeRegex.Core.Compilation;
using PoeRegex.Core.Data;
using PoeRegex.Core.Search;
using PoeRegex.Data.Build.RePoE;
using PoeRegex.Data.Build.Rendering;

namespace PoeRegex.Data.Build;

/// <summary>
/// Dev-time ingestion + fingerprint builder.
///
/// Usage:
///   dotnet run -- build --game poe1 --raw data/raw/poe1 --catalog data/catalogs/poe1.catalog.json
///   dotnet run -- corpus --game poe1 --raw data/raw/poe1 --out data/catalogs/poe1.vendor-corpus.json
///   dotnet run -- fragments --game poe1
///   dotnet run -- fetch --game poe1 --out data/raw/poe1
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0)
            {
                Console.Error.WriteLine("Usage: dotnet run -- <command> [options]");
                return 2;
            }

            return args[0].ToLowerInvariant() switch
            {
                "build" => Build(args[1..]),
                "fragments" => Fragments(args[1..]),
                "corpus" => Corpus(args[1..]),
                "probe" => Probe(args[1..]),
                "candidate-report" => CandidateReport.Run(args[1..]),
                "candidate-matrix" => CandidateMatrix.Run(args[1..]),
                "fetch" => Fetch(args[1..]),
                _ => Usage(args[0]),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static int Usage(string bad)
    {
        Console.Error.WriteLine($"Unknown command '{bad}'. Expected: build | fragments | corpus | probe | candidate-report | candidate-matrix | fetch");
        return 2;
    }

    private static int Build(string[] args)
    {
        var opts = ParseOptions(args);
        var game = GetValue(opts, "--game", "poe1");
        var rawDir = GetValue(opts, "--raw", $"data/raw/{game}");
        var catalogPath = GetValue(opts, "--catalog", $"data/catalogs/{game}.catalog.json");

        Console.WriteLine($"Building catalog for {game} from {rawDir}");
        var catalog = CatalogBuilder.Build(game, rawDir);
        Console.WriteLine($"  {catalog.ItemClasses.Count} item classes, {catalog.Mods.Count} mods");

        Directory.CreateDirectory(Path.GetDirectoryName(catalogPath)!);
        var json = System.Text.Json.JsonSerializer.Serialize(
            catalog,
            new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = false,
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            });
        File.WriteAllText(catalogPath, json);
        Console.WriteLine($"  wrote {catalogPath} ({new FileInfo(catalogPath).Length / 1024} KiB)");
        return 0;
    }

    private static int Corpus(string[] args)
    {
        var opts = ParseOptions(args);
        var game = GetValue(opts, "--game", "poe1");
        var rawDir = GetValue(opts, "--raw", $"data/raw/{game}");
        var outPath = GetValue(opts, "--out", $"data/catalogs/{game}.vendor-corpus.json");
        Console.WriteLine($"Building vendor/stash search corpus for {game} from {rawDir}");
        var corpus = VendorCorpusBuilder.Build(game, rawDir);
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        var json = System.Text.Json.JsonSerializer.Serialize(
            corpus, PoeRegex.Core.Data.CatalogJsonContext.Default.VendorCorpusFile);
        File.WriteAllText(outPath, json);
        var classes = corpus.Lines.Select(x => x.ClassId).Where(x => x is not null).Distinct().Count();
        Console.WriteLine($"  {corpus.Lines.Count} lines, {classes} classes");
        Console.WriteLine($"  wrote {outPath} ({new FileInfo(outPath).Length / 1024} KiB)");
        return 0;
    }

    private static int Fragments(string[] args)
    {
        var opts = ParseOptions(args);
        var gameName = GetValue(opts, "--game", "poe1");
        var game = gameName == "poe1" ? GameTarget.PoE1 : GameTarget.PoE2;
        var catalogPath = GetValue(opts, "--catalog", $"data/catalogs/{gameName}.catalog.json");
        var outPath = GetValue(opts, "--out", $"data/catalogs/{gameName}.fragments.json");

        Console.WriteLine($"Computing minimal fragments for {gameName} from {catalogPath}");
        var catalog = new FileCatalogSource(catalogPath).Load(game);
        var corpus = new EngineCorpus(catalog);
        var selector = new FragmentSelector(
            corpus, PoeRegexCompiler.StrategyFor(game), usePrecomputed: false,
            policy: opts.ContainsKey("--absolute") ? FragmentPolicy.Absolute :
                opts.ContainsKey("--guaranteed") ? FragmentPolicy.Guaranteed :
                FragmentPolicy.Shortest);

        var global = new Dictionary<string, string>(StringComparer.Ordinal);
        var globalUnresolved = new Dictionary<string, string>(StringComparer.Ordinal);
        var patternFilter = GetValue(opts, "--pattern", "")
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.Replace('#', '\u0001'))
            .ToHashSet(StringComparer.Ordinal);
        var patterns = catalog.Mods.SelectMany(m => m.StatLines).Select(l => l.Pattern)
            .Distinct(StringComparer.Ordinal)
            .Where(p => patternFilter.Count == 0 || patternFilter.Contains(p))
            .ToList();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        foreach (var pattern in patterns)
        {
            var fragment = selector.SelectWithMinionFallback(pattern, null);
            if (fragment is null)
            {
                Console.Error.WriteLine($"  unresolved global: {pattern.Replace('\u0001', '#')}");
                globalUnresolved[pattern] = UnresolvedReason(pattern);
            }
            if (fragment is not null) global[pattern] = fragment;
        }
        Console.WriteLine($"  global: {global.Count}/{patterns.Count} in {sw.ElapsedMilliseconds} ms");

        var byClass = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        var byClassUnresolved = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        var globalOnly = opts.ContainsKey("--global-only");
        foreach (var cls in catalog.ItemClasses.Where(c =>
                     !globalOnly && catalog.Mods.Any(m => m.Classes.Contains(c.Id))))
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            var unresolved = new Dictionary<string, string>(StringComparer.Ordinal);
            var classPatterns = catalog.Mods.Where(m => m.Classes.Contains(cls.Id))
                .SelectMany(m => m.StatLines).Select(l => l.Pattern)
                .Distinct(StringComparer.Ordinal)
                .Where(p => patternFilter.Count == 0 || patternFilter.Contains(p))
                .ToList();
            sw.Restart();
            foreach (var pattern in classPatterns)
            {
                var fragment = selector.SelectWithMinionFallback(pattern, cls.Id);
                if (fragment is not null) map[pattern] = fragment;
                else unresolved[pattern] = UnresolvedReason(pattern);
            }
            byClass[cls.Id] = map;
            if (unresolved.Count > 0) byClassUnresolved[cls.Id] = unresolved;
            Console.WriteLine($"  {cls.Id}: {map.Count}/{classPatterns.Count} in {sw.ElapsedMilliseconds} ms");
        }

        var classAnchors = catalog.ItemClasses
            .Where(c => catalog.Mods.Any(m => m.Classes.Contains(c.Id)))
            .ToDictionary(c => c.Id, c => corpus.ClassNamePrefix(c.Id), StringComparer.Ordinal);
        var table = new FragmentTable
        {
            Game = catalog.Game,
            DataVersion = catalog.DataVersion,
            Global = global,
            ByClass = byClass,
            GlobalUnresolved = globalUnresolved,
            ByClassUnresolved = byClassUnresolved,
            ClassAnchors = classAnchors,
        };
        // The selector now finds the shorter quoted forms directly; the old
        // hardcoded overlay would overwrite them with longer legacy entries.
        if (opts.ContainsKey("--representative-class"))
        {
            CandidateReport.ApplyRepresentativeClassCandidates(corpus, catalog, table);
            Console.WriteLine("  applied representative-class strict candidates");
        }
        if (opts.ContainsKey("--representative-global"))
        {
            CandidateReport.ApplyRepresentativeGlobalCandidates(corpus, catalog, table);
            Console.WriteLine("  applied representative-global strict candidates");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        File.WriteAllText(outPath, System.Text.Json.JsonSerializer.Serialize(
            table, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"  wrote {outPath} ({new FileInfo(outPath).Length / 1024} KiB)");
        return 0;
    }

    private static int Probe(string[] args)
    {
        var opts = ParseOptions(args);
        var gameName = GetValue(opts, "--game", "poe2");
        var game = gameName == "poe1" ? GameTarget.PoE1 : GameTarget.PoE2;
        var catalogPath = GetValue(opts, "--catalog", $"data/catalogs/{gameName}.catalog.json");
        var pattern = GetValue(opts, "--pattern", "").Replace('#', '\u0001');
        var scope = opts.TryGetValue("--scope", out var s) ? s : null;
        if (string.IsNullOrEmpty(pattern))
        {
            Console.Error.WriteLine("probe requires --pattern (use # for a digit placeholder)");
            return 2;
        }
        var catalog = new FileCatalogSource(catalogPath).Load(game);
        var corpus = new EngineCorpus(catalog);
        var selector = new FragmentSelector(
            corpus, PoeRegexCompiler.StrategyFor(game), usePrecomputed: false,
            policy: opts.ContainsKey("--guaranteed") ? FragmentPolicy.Guaranteed : FragmentPolicy.Absolute);
        if (opts.TryGetValue("--fragment", out var probeFragment))
        {
            var all = scope is null ? corpus.RealLinesGlobal() : corpus.RealLinesForClass(scope);
            var targets = all.Where(x => x.Pattern == pattern).Select(x => x.Real).Distinct().ToList();
            var others = all.Where(x => x.Pattern != pattern).Select(x => x.Real).Distinct().ToList();
            others.AddRange(corpus.SafetyLines(scope)
                .Where(x => x.Pattern != pattern).Select(x => x.Raw));
            others = others.Distinct().ToList();
            var regex = new System.Text.RegularExpressions.Regex(
                probeFragment, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var misses = targets.Where(t => !regex.IsMatch(t)).ToList();
            var hits = others.Where(t => regex.IsMatch(t)).ToList();
            Console.WriteLine($"targets={targets.Count} others={others.Count} misses={misses.Count} foreign={hits.Count} cross={!corpus.CrossLineSafe(scope, probeFragment)}");
            foreach (var m in misses.Take(5)) Console.WriteLine($"  MISS {m}");
            foreach (var h in hits.Take(5)) Console.WriteLine($"  HIT  {h}");
            if (opts.ContainsKey("--verify-only")) return 0;
        }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var fragment = selector.Select(pattern, scope);
        var encoded = fragment is null ? "" : Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(fragment));
        Console.WriteLine($"{scope ?? "global"} | {pattern.Replace('\u0001', '#')} => {fragment} (len={fragment?.Length}, b64={encoded}) ({sw.ElapsedMilliseconds} ms)");
        return 0;
    }

    private static int Fetch(string[] args)
    {
        var opts = ParseOptions(args);
        var game = GetValue(opts, "--game", "poe1");
        var outDir = GetValue(opts, "--out", $"data/raw/{game}");

        var baseUrl = game == "poe1"
            ? "https://repoe-fork.github.io"
            : "https://repoe-fork.github.io/poe2";

        var files = new List<(string Url, string File)>
        {
            ($"{baseUrl}/mods.min.json", "mods.min.json"),
            ($"{baseUrl}/base_items.min.json", "base_items.min.json"),
            ($"{baseUrl}/item_classes.min.json", "item_classes.min.json"),
            ($"{baseUrl}/mods_by_base.min.json", "mods_by_base.min.json"),
            ($"{baseUrl}/stat_value_handlers.min.json", "stat_value_handlers.min.json"),
        };

        if (game == "poe1")
        {
            files.Add(($"{baseUrl}/stat_translations.min.json", "stat_translations.min.json"));
            files.Add(($"{baseUrl}/stats.min.json", "stats.min.json"));
        }
        else
        {
            files.Add(($"{baseUrl}/stat_translations/stat_descriptions.min.json", "stat_descriptions.min.json"));
            files.Add(($"{baseUrl}/stat_translations/advanced_mod_stat_descriptions.min.json", "advanced_mod_stat_descriptions.min.json"));
            files.Add(($"{baseUrl}/stats_by_file.min.json", "stats_by_file.min.json"));
        }

        Directory.CreateDirectory(outDir);
        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PoeRegex.Data.Build/0.1");
        foreach (var (url, file) in files)
        {
            var dest = Path.Combine(outDir, file);
            if (File.Exists(dest))
            {
                Console.WriteLine($"  skip {file} (exists)");
                continue;
            }
            Console.Write($"  download {file} ... ");
            var resp = client.GetAsync(url).GetAwaiter().GetResult();
            if (!resp.IsSuccessStatusCode)
            {
                Console.WriteLine($"FAILED ({resp.StatusCode})");
                continue;
            }
            File.WriteAllBytes(dest, resp.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult());
            Console.WriteLine($"ok ({new FileInfo(dest).Length / 1024} KiB)");
        }

        Console.WriteLine("done");
        return 0;
    }

    private static string UnresolvedReason(string pattern)
    {
        var display = pattern.Replace('\u0001', '#');
        if (display.Contains("Movement Speed", StringComparison.OrdinalIgnoreCase))
            return "Fixed and ranged tiers share no line-local guard: fixed tiers need ^digit+%..., " +
                   "ranged tiers need the range parenthesis, and the common suffix also matches " +
                   "Minions have ...% increased Movement Speed. Use an item-class scope or test an " +
                   "explicit alternation in-game before adding one.";
        if (display.Contains("Life per second", StringComparison.OrdinalIgnoreCase))
            return "Life-regeneration tiers mix integer, decimal-percent, and 'Regeneration per second' " +
                   "forms; no single line-local guard covers every form without matching a different " +
                   "regeneration stat. Use an item-class scope or an explicit alternation.";
        if (display.Contains("Energy Shield per second", StringComparison.OrdinalIgnoreCase))
            return "Energy-shield regeneration mixes integer and decimal forms; the shared suffix also " +
                   "matches other regeneration stats. Use an item-class scope or an explicit alternation.";
        if (display.Contains("Attack Speed", StringComparison.OrdinalIgnoreCase) ||
            display.Contains("Cast Speed", StringComparison.OrdinalIgnoreCase))
            return "The speed suffix also matches minion and base speed lines; compact and range tiers " +
                   "need different guards. Use an item-class scope or an explicit alternation.";
        if (display.Contains("Critical Strike Chance", StringComparison.OrdinalIgnoreCase))
            return "The critical-chance suffix is shared with spell/attack critical lines and base " +
                   "properties; no line-local guard covers every form. Use an item-class scope or an " +
                   "explicit alternation.";
        if (display.Contains("Area of Effect", StringComparison.OrdinalIgnoreCase))
            return "The area-of-effect suffix is shared with skill and base area lines; no line-local " +
                   "guard covers every form. Use an item-class scope or an explicit alternation.";
        if (display.Contains("Elemental Damage", StringComparison.OrdinalIgnoreCase))
            return "The elemental-damage suffix is shared with weapon and minion elemental lines; no " +
                   "line-local guard covers every form. Use an item-class scope or an explicit alternation.";
        if (display.Contains("Gain ", StringComparison.OrdinalIgnoreCase) &&
            display.Contains("as Extra", StringComparison.OrdinalIgnoreCase))
            return "The gain-as-extra conversion suffix is shared with other element conversions; no " +
                   "line-local guard covers every form. Use an item-class scope or an explicit alternation.";
        if (display.Contains("maximum Life", StringComparison.OrdinalIgnoreCase))
            return "The common suffix 'maximum Life' also matches added-Life and 'Minions have ... " +
                   "maximum Life' lines; no line-local guard covers compact and range tiers. Use an " +
                   "item-class scope or an explicit alternation.";
        if (display.Contains("Accuracy Rating", StringComparison.OrdinalIgnoreCase))
            return "Compact and range tiers share 'to Accuracy Rating', which also matches added-Accuracy " +
                   "lines; no single line-local guard covers both display forms. Use an item-class scope " +
                   "or an explicit alternation.";
        if (display.Contains("Elemental Resistances", StringComparison.OrdinalIgnoreCase))
            return "The resistance suffix is shared with minion/allies and other resistance lines; " +
                   "compact and range tiers need different guards. Use an item-class scope or an explicit " +
                   "alternation.";
        if (display.Contains("Chance to Block", StringComparison.OrdinalIgnoreCase))
            return "The block suffix is shared by attack and spell block lines; compact and range tiers " +
                   "need different guards. Use an item-class scope or an explicit alternation.";
        if (display.Contains("Damage Penetrates", StringComparison.OrdinalIgnoreCase))
            return "The penetration prefix is shared with elemental-resistance lines; compact and range " +
                   "tiers need different guards. Use an item-class scope or an explicit alternation.";
        if (display.Contains("Effect of Curses", StringComparison.OrdinalIgnoreCase))
            return "The curse-effect suffix also matches the increased/reduced counterpart; no line-local " +
                   "guard separates the two. Use an item-class scope or an explicit alternation.";
        if (display.StartsWith("Adds ", StringComparison.OrdinalIgnoreCase) &&
            display.Contains("Damage", StringComparison.OrdinalIgnoreCase))
            return "Compact and range tiers share the added-damage suffix, which also matches other " +
                   "added-damage stats; no line-local guard covers both display forms. Use an item-class " +
                   "scope or an explicit alternation.";
        if (display.Contains("Physical Damage Reduction", StringComparison.OrdinalIgnoreCase))
            return "The shared physical-damage-reduction suffix also matches other mitigation lines; " +
                   "compact and range tiers need different guards. Use an item-class scope or an explicit " +
                   "alternation.";
        return "No line-local candidate recalls every display form without matching a foreign modeled " +
               "line. Use an item-class scope or an explicit alternation.";
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var dict = new Dictionary<string, string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--")) continue;
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
            {
                dict[args[i]] = args[i + 1];
                i++;
            }
            else
            {
                dict[args[i]] = "1";
            }
        }
        return dict;
    }

    private static string GetValue(Dictionary<string, string> opts, string key, string fallback) =>
        opts.TryGetValue(key, out var v) ? v : fallback;
}

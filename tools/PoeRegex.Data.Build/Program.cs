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
        Console.Error.WriteLine($"Unknown command '{bad}'. Expected: build | fragments | fetch");
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
        var selector = new FragmentSelector(corpus, PoeRegexCompiler.StrategyFor(game), usePrecomputed: false);

        var global = new Dictionary<string, string>(StringComparer.Ordinal);
        var patterns = catalog.Mods.SelectMany(m => m.StatLines).Select(l => l.Pattern)
            .Distinct(StringComparer.Ordinal).ToList();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        foreach (var pattern in patterns)
        {
            var fragment = selector.Select(pattern, null,
                candidate => SafetyItemValidator.IsSafe(corpus, pattern, candidate));
            if (fragment is not null) global[pattern] = fragment;
        }
        Console.WriteLine($"  global: {global.Count}/{patterns.Count} in {sw.ElapsedMilliseconds} ms");

        var byClass = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (var cls in catalog.ItemClasses.Where(c => catalog.Mods.Any(m => m.Classes.Contains(c.Id))))
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            var classPatterns = catalog.Mods.Where(m => m.Classes.Contains(cls.Id))
                .SelectMany(m => m.StatLines).Select(l => l.Pattern)
                .Distinct(StringComparer.Ordinal).ToList();
            sw.Restart();
            foreach (var pattern in classPatterns)
            {
                var fragment = selector.Select(pattern, cls.Id,
                    candidate => SafetyItemValidator.IsSafe(corpus, pattern, candidate));
                if (fragment is not null) map[pattern] = fragment;
            }
            byClass[cls.Id] = map;
            Console.WriteLine($"  {cls.Id}: {map.Count}/{classPatterns.Count} in {sw.ElapsedMilliseconds} ms");
        }

        var table = new FragmentTable
        {
            Game = catalog.Game,
            DataVersion = catalog.DataVersion,
            Global = global,
            ByClass = byClass,
        };
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        File.WriteAllText(outPath, System.Text.Json.JsonSerializer.Serialize(
            table, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"  wrote {outPath} ({new FileInfo(outPath).Length / 1024} KiB)");
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

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var dict = new Dictionary<string, string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("--") && i + 1 < args.Length)
            {
                dict[args[i]] = args[i + 1];
                i++;
            }
        }
        return dict;
    }

    private static string GetValue(Dictionary<string, string> opts, string key, string fallback) =>
        opts.TryGetValue(key, out var v) ? v : fallback;
}

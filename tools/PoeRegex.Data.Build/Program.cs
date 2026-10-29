using System.Text.Json;
using PoeRegex.Core.Compilation;
using PoeRegex.Core.Data;
using PoeRegex.Data.Build.RePoE;
using PoeRegex.Data.Build.Rendering;

namespace PoeRegex.Data.Build;

/// <summary>
/// Dev-time PoE2 data pipeline.
///
/// Usage:
///   dotnet run -- build
///   dotnet run -- corpus
///   dotnet run -- matrix-solve [--from-scratch]
///   dotnet run -- matrix-proof
///   dotnet run -- matrix-status [--check --check-shortest]
///   dotnet run -- solve-exact --pattern "#% increased maximum Life"
///   dotnet run -- probe --pattern "#% increased maximum Life" --fragment "fe$"
///   dotnet run -- fetch
/// </summary>
public static class Program
{
    private const string CatalogPath = "data/catalogs/poe2.catalog.json";
    private const string VendorPath = "data/catalogs/poe2.vendor-corpus.json";
    private const string TablePath = "data/catalogs/poe2.fragments.json";
    private const string MatrixPath = "docs/CANDIDATE-MATRIX-poe2.html";
    private const string ProofPath = "docs/CANDIDATE-PROOF-poe2.json";
    private const string StatusPath = "docs/CANDIDATE-STATUS-poe2.json";

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
                "corpus" => Corpus(args[1..]),
                "matrix-solve" => MatrixSolve(args[1..]),
                "matrix-table" => MatrixTable(args[1..]),
                "matrix-proof" => MatrixProof.Run(args[1..]),
                "matrix-status" => BuildStatus.Run(args[1..]),
                "solve-exact" => SolveExact(args[1..]),
                "probe" => Probe(args[1..]),
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
        Console.Error.WriteLine($"Unknown command '{bad}'. Expected: build | corpus | matrix-solve | matrix-table | matrix-proof | matrix-status | solve-exact | probe | fetch");
        return 2;
    }

    private static int Build(string[] args)
    {
        var opts = ParseOptions(args);
        var rawDir = GetValue(opts, "--raw", "data/raw/poe2");
        var catalogPath = GetValue(opts, "--catalog", CatalogPath);

        Console.WriteLine($"Building PoE2 catalog from {rawDir}");
        var catalog = CatalogBuilder.Build(rawDir);
        Console.WriteLine($"  {catalog.ItemClasses.Count} item classes, {catalog.Mods.Count} mods");

        Directory.CreateDirectory(Path.GetDirectoryName(catalogPath)!);
        var json = JsonSerializer.Serialize(
            catalog,
            new JsonSerializerOptions
            {
                WriteIndented = false,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            });
        File.WriteAllText(catalogPath, json);
        Console.WriteLine($"  wrote {catalogPath} ({new FileInfo(catalogPath).Length / 1024} KiB)");
        return 0;
    }

    private static int Corpus(string[] args)
    {
        var opts = ParseOptions(args);
        var rawDir = GetValue(opts, "--raw", "data/raw/poe2");
        var outPath = GetValue(opts, "--out", VendorPath);
        Console.WriteLine($"Building PoE2 vendor/stash corpus from {rawDir}");
        var corpus = VendorCorpusBuilder.Build(rawDir);
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        var json = JsonSerializer.Serialize(
            corpus, CatalogJsonContext.Default.VendorCorpusFile);
        File.WriteAllText(outPath, json);
        var classes = corpus.Lines.Select(x => x.ClassId).Where(x => x is not null).Distinct().Count();
        Console.WriteLine($"  {corpus.Lines.Count} lines, {classes} classes");
        Console.WriteLine($"  wrote {outPath} ({new FileInfo(outPath).Length / 1024} KiB)");
        return 0;
    }

    private static int MatrixSolve(string[] args)
    {
        var opts = ParseOptions(args);
        var jobs = GetValue(opts, "--jobs", Math.Max(1, Environment.ProcessorCount - 1).ToString());
        var catalog = GetValue(opts, "--catalog", CatalogPath);
        var matrix = GetValue(opts, "--matrix", MatrixPath);
        var table = GetValue(opts, "--table", TablePath);
        var proof = GetValue(opts, "--proof", ProofPath);
        var status = GetValue(opts, "--status", StatusPath);

        var common = new[] { "--catalog", catalog };
        var matrixArgs = common.Concat(new[] { "--jobs", jobs, "--out", matrix });
        if (opts.ContainsKey("--from-scratch"))
            matrixArgs = matrixArgs.Append("--from-scratch");
        var code = CandidateMatrix.Run(matrixArgs.ToArray());
        if (code != 0) return code;

        code = MatrixTable(common.Concat(new[] { "--matrix", matrix, "--out", table }).ToArray());
        if (code != 0) return code;

        code = MatrixProof.Run(common.Concat(new[]
        {
            "--matrix", matrix, "--jobs", jobs, "--out", proof,
        }).ToArray());
        if (code != 0) return code;

        return BuildStatus.Run(common.Concat(new[]
        {
            "--matrix", matrix, "--table", table, "--proof", proof, "--out", status,
        }).ToArray());
    }

    /// <summary>
    /// Applies the exact matrix choices to the shipped table and verifies every
    /// entry against its scope before writing.
    /// </summary>
    private static int MatrixTable(string[] args)
    {
        var opts = ParseOptions(args);
        var catalogPath = GetValue(opts, "--catalog", CatalogPath);
        var matrixPath = GetValue(opts, "--matrix", MatrixPath);
        var outPath = GetValue(opts, "--out", TablePath);

        var catalog = new FileCatalogSource(catalogPath).Load();
        var corpus = new EngineCorpus(catalog);
        var global = new Dictionary<string, string>(StringComparer.Ordinal);
        var byClass = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

        var html = File.ReadAllText(matrixPath);
        const string marker = "window.MATRIX = ";
        var start = html.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) throw new InvalidOperationException("matrix JSON not found");
        start += marker.Length;
        var end = html.IndexOf(";</script>", start, StringComparison.Ordinal);
        if (end < 0) throw new InvalidOperationException("matrix JSON terminator not found");
        using var document = JsonDocument.Parse(html[start..end]);

        var globalScope = ScopeCorpus.Build(corpus, null);
        var classScopes = new Dictionary<string, ScopeCorpus>(StringComparer.Ordinal);
        ScopeCorpus ScopeFor(string? scope)
        {
            if (scope is null) return globalScope;
            if (!classScopes.TryGetValue(scope, out var value))
            {
                value = ScopeCorpus.Build(corpus, scope);
                classScopes[scope] = value;
            }
            return value;
        }

        var applied = 0;
        var rejected = 0;
        foreach (var family in document.RootElement.GetProperty("families").EnumerateArray())
        foreach (var patternNode in family.GetProperty("patterns").EnumerateArray())
        {
            var raw = patternNode.GetProperty("pattern").GetString();
            if (string.IsNullOrEmpty(raw)) continue;
            var pattern = raw.Replace('#', '\u0001');
            foreach (var scope in patternNode.GetProperty("scopes").EnumerateArray())
            {
                if (!scope.TryGetProperty("chosen", out var chosen) ||
                    chosen.ValueKind != JsonValueKind.Object) continue;
                var fragment = chosen.GetProperty("fragment").GetString();
                if (string.IsNullOrEmpty(fragment)) continue;
                if (QueryShape.ValidateFragment(fragment).Count > 0)
                {
                    Console.Error.WriteLine($"  invalid shape: {fragment}");
                    rejected++;
                    continue;
                }

                var kind = scope.GetProperty("kind").GetString() ?? "";
                if (kind == "global")
                {
                    var option = ExactOracle.Check(globalScope, pattern, fragment);
                    if (option is null || option.ForeignLines > 0)
                    {
                        Console.Error.WriteLine($"  rejected global/{raw}: '{fragment}'");
                        rejected++;
                        continue;
                    }
                    global[pattern] = fragment;
                    applied++;
                }
                else
                {
                    foreach (var classNode in scope.GetProperty("classes").EnumerateArray())
                    {
                        var classId = classNode.GetString();
                        if (string.IsNullOrEmpty(classId) || !corpus.IsInGameClass(classId)) continue;
                        var option = ExactOracle.Check(ScopeFor(classId), pattern, fragment);
                        if (option is null || option.ForeignLines > 0)
                        {
                            Console.Error.WriteLine($"  rejected {classId}/{raw}: '{fragment}'");
                            rejected++;
                            continue;
                        }
                        if (!byClass.TryGetValue(classId, out var map))
                        {
                            map = new Dictionary<string, string>(StringComparer.Ordinal);
                            byClass[classId] = map;
                        }
                        map[pattern] = fragment;
                        applied++;
                    }
                }
            }
        }

        if (rejected > 0)
        {
            Console.Error.WriteLine($"matrix-table: {rejected} choice(s) failed verification");
            return 3;
        }

        var anchors = catalog.ItemClasses
            .Where(c => corpus.IsInGameClass(c.Id) &&
                        catalog.Mods.Any(m => m.Classes.Contains(c.Id)))
            .ToDictionary(
                c => c.Id,
                c => corpus.ClassNamePrefix(c.Id, usePrecomputed: false),
                StringComparer.Ordinal);

        var table = new FragmentTable
        {
            Game = catalog.Game,
            DataVersion = catalog.DataVersion,
            Global = global,
            ByClass = byClass,
            ClassAnchors = anchors,
        };
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        File.WriteAllText(outPath, JsonSerializer.Serialize(table, new JsonSerializerOptions
        {
            WriteIndented = true,
        }));
        Console.WriteLine(
            $"matrix-table: applied {applied} scope choices, {global.Count} global, " +
            $"{byClass.Count} classes -> {outPath}");
        return 0;
    }

    private static int SolveExact(string[] args)
    {
        var opts = ParseOptions(args);
        var catalogPath = GetValue(opts, "--catalog", CatalogPath);
        var pattern = GetValue(opts, "--pattern", "").Replace('#', '\u0001');
        var scope = opts.TryGetValue("--scope", out var value) ? value : null;
        var min = int.TryParse(GetValue(opts, "--min", "3"), out var mn) ? mn : 3;
        var max = int.TryParse(GetValue(opts, "--max", "48"), out var mx) ? mx : 48;
        if (string.IsNullOrEmpty(pattern))
        {
            Console.Error.WriteLine("solve-exact requires --pattern");
            return 2;
        }
        var catalog = new FileCatalogSource(catalogPath).Load();
        var corpus = new EngineCorpus(catalog);
        var result = ExactSearchSolver.Solve(
            ScopeCorpus.Build(corpus, scope), pattern, min, max);
        if (result is null)
        {
            Console.WriteLine("none");
            return 0;
        }
        Console.WriteLine($"{result.Fragment} (len={result.Length}, iterations={result.Iterations}, negatives={result.Negatives})");
        return 0;
    }

    private static int Probe(string[] args)
    {
        var opts = ParseOptions(args);
        var catalogPath = GetValue(opts, "--catalog", CatalogPath);
        var pattern = GetValue(opts, "--pattern", "").Replace('#', '\u0001');
        var scope = opts.TryGetValue("--scope", out var s) ? s : null;
        if (string.IsNullOrEmpty(pattern))
        {
            Console.Error.WriteLine("probe requires --pattern (use # for a digit placeholder)");
            return 2;
        }

        var catalog = new FileCatalogSource(catalogPath).Load();
        var corpus = new EngineCorpus(catalog);
        var scopeCorpus = ScopeCorpus.Build(corpus, scope);
        var fragment = opts.TryGetValue("--fragment", out var provided)
            ? provided
            : corpus.TryPrecomputedFragment(pattern, scope, out var stored)
                ? stored
                : null;
        if (fragment is null)
        {
            Console.WriteLine($"{scope ?? "global"} | {pattern.Replace('\u0001', '#')} => (unresolved)");
            return 0;
        }

        var option = ExactOracle.Check(scopeCorpus, pattern, fragment);
        var targets = scopeCorpus.Targets(pattern);
        var foreign = scopeCorpus.Lines
            .Where(x => x.Pattern != pattern)
            .Select(x => x.Real)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var regex = new System.Text.RegularExpressions.Regex(
            fragment,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase |
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        var misses = targets.Where(t => !regex.IsMatch(t)).ToList();
        var hits = foreign.Where(t => regex.IsMatch(t)).ToList();
        Console.WriteLine(
            $"targets={targets.Count} others={foreign.Count} misses={misses.Count} " +
            $"foreign={hits.Count} strict={(option is { ForeignLines: 0 })}");
        foreach (var m in misses.Take(5)) Console.WriteLine($"  MISS {m}");
        foreach (var h in hits.Take(5)) Console.WriteLine($"  HIT  {h}");
        Console.WriteLine($"{scope ?? "global"} | {pattern.Replace('\u0001', '#')} => {fragment} (len={fragment.Length})");
        return 0;
    }

    private static int Fetch(string[] args)
    {
        var opts = ParseOptions(args);
        var outDir = GetValue(opts, "--out", "data/raw/poe2");
        const string baseUrl = "https://repoe-fork.github.io/poe2";
        var files = new List<(string Url, string File)>
        {
            ($"{baseUrl}/mods.min.json", "mods.min.json"),
            ($"{baseUrl}/base_items.min.json", "base_items.min.json"),
            ($"{baseUrl}/item_classes.min.json", "item_classes.min.json"),
            ($"{baseUrl}/mods_by_base.min.json", "mods_by_base.min.json"),
            ($"{baseUrl}/stat_value_handlers.min.json", "stat_value_handlers.min.json"),
            ($"{baseUrl}/stat_translations/stat_descriptions.min.json", "stat_descriptions.min.json"),
            ($"{baseUrl}/stat_translations/advanced_mod_stat_descriptions.min.json", "advanced_mod_stat_descriptions.min.json"),
            ($"{baseUrl}/stats_by_file.min.json", "stats_by_file.min.json"),
        };

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
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal)) continue;
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
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
        opts.TryGetValue(key, out var value) ? value : fallback;
}

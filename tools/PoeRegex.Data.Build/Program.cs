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
///   dotnet run -- fetch [--refresh]
///   dotnet run -- data-audit [--check] [--out report.md]
///   dotnet run -- common-picks [--check] [--table]
///   dotnet run -- poe1-fetch [--refresh]
///   dotnet run -- poe1-build [--raw data/raw/poe1]
///   dotnet run -- poe1-matrix [--from-scratch]
///   dotnet run -- poe1-status [--check]
/// </summary>
public static class Program
{
    private const string CatalogPath = "data/catalogs/poe2.catalog.json";
    private const string VendorPath = "data/catalogs/poe2.vendor-corpus.json";
    private const string TablePath = "data/catalogs/poe2.fragments.json";
    private const string MatrixPath = "docs/CANDIDATE-MATRIX-poe2.html";
    private const string ProofPath = "docs/CANDIDATE-PROOF-poe2.json";
    private const string StatusPath = "docs/CANDIDATE-STATUS-poe2.json";

    private const string Poe1CatalogPath = "data/catalogs/poe1.catalog.json";
    private const string Poe1VendorPath = "data/catalogs/poe1.vendor-corpus.json";
    private const string Poe1TablePath = "data/catalogs/poe1.fragments.json";
    private const string Poe1MatrixPath = "docs/CANDIDATE-MATRIX-poe1.html";
    private const string Poe1ProofPath = "docs/CANDIDATE-PROOF-poe1.json";
    private const string Poe1StatusPath = "docs/CANDIDATE-STATUS-poe1.json";

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
                "poe1-fetch" => Poe1Fetch(args[1..]),
                "data-audit" => DataAudit.Run(args[1..]),
                "common-picks" => CommonPicksAudit.Run(args[1..]),
                "poe1-build" => Poe1Build(args[1..]),
                "poe1-matrix" => Poe1Matrix(args[1..]),
                "poe1-status" => Poe1Status(args[1..]),
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
        Console.Error.WriteLine($"Unknown command '{bad}'. Expected: build | corpus | matrix-solve | matrix-table | matrix-proof | matrix-status | solve-exact | probe | fetch | poe1-fetch | data-audit | common-picks | poe1-build | poe1-matrix | poe1-status");
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

    private static int Poe1Build(string[] args)
    {
        var opts = ParseOptions(args);
        var rawDir = GetValue(opts, "--raw", "data/raw/poe1");
        var catalogPath = GetValue(opts, "--catalog", Poe1CatalogPath);
        var vendorPath = GetValue(opts, "--vendor", Poe1VendorPath);
        var snapshotPath = GetValue(opts, "--snapshot", "data/repoe-poe1.snapshot.json");
        var dataVersion = ReadDataVersion(snapshotPath);
        if (dataVersion is null)
        {
            Console.Error.WriteLine(
                $"warning: {snapshotPath} is missing; using today's date as dataVersion");
            dataVersion = DateTime.UtcNow.ToString("yyyy-MM-dd");
        }

        Console.WriteLine($"Building PoE1 normal-equipment catalog from {rawDir}");
        var result = Poe1CatalogBuilder.Build(rawDir, dataVersion);
        Directory.CreateDirectory(Path.GetDirectoryName(catalogPath)!);
        // Emit exactly the derived-catalog contract; source-generated
        // serialization would also write default-valued augment fields.
        var catalogJson = JsonSerializer.Serialize(
            new
            {
                game = result.Catalog.Game,
                dataVersion = result.Catalog.DataVersion,
                itemClasses = result.Catalog.ItemClasses.Select(c => new
                {
                    id = c.Id,
                    name = c.Name,
                    baseFingerprint = c.BaseFingerprint,
                }),
                mods = result.Catalog.Mods.Select(m => new
                {
                    id = m.Id,
                    name = m.Name,
                    generationType = m.GenerationType,
                    source = m.Source,
                    family = m.Family,
                    requiredLevel = m.RequiredLevel,
                    classes = m.Classes,
                    groups = m.Groups,
                    statLines = m.StatLines.Select(l => new
                    {
                        line = l.Line,
                        rangeLine = l.RangeLine,
                        pattern = l.Pattern,
                        minValue = l.MinValue,
                        maxValue = l.MaxValue,
                        fingerprints = l.Fingerprints,
                    }),
                }),
            },
            new JsonSerializerOptions
            {
                WriteIndented = false,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            });
        File.WriteAllText(catalogPath, catalogJson + "\n");

        Directory.CreateDirectory(Path.GetDirectoryName(vendorPath)!);
        var vendorJson = JsonSerializer.Serialize(
            result.Vendor, CatalogJsonContext.Default.VendorCorpusFile);
        File.WriteAllText(vendorPath, vendorJson + "\n");

        Console.WriteLine(
            $"  {result.Catalog.ItemClasses.Count} item classes, " +
            $"{result.Catalog.Mods.Count} mods, {result.Vendor.Lines.Count} foreign lines");
        Console.WriteLine($"  wrote {catalogPath}");
        Console.WriteLine($"  wrote {vendorPath}");
        return 0;
    }

    private static int Poe1Matrix(string[] args)
    {
        var opts = ParseOptions(args);
        var catalog = GetValue(opts, "--catalog", Poe1CatalogPath);
        var vendor = GetValue(opts, "--vendor", Poe1VendorPath);
        var matrix = GetValue(opts, "--matrix", Poe1MatrixPath);
        var table = GetValue(opts, "--table", Poe1TablePath);
        var proof = GetValue(opts, "--proof", Poe1ProofPath);
        var status = GetValue(opts, "--status", Poe1StatusPath);
        var jobs = GetValue(opts, "--jobs", Math.Max(1, Environment.ProcessorCount - 1).ToString());
        var timeout = GetValue(opts, "--timeout", "120");
        var prefer = opts.TryGetValue("--prefer", out var preferPath) ? preferPath : "";

        var common = new List<string>
        {
            "--catalog", catalog, "--vendor", vendor,
        };
        var matrixArgs = common.Concat(new[]
        {
            "--jobs", jobs, "--timeout", timeout, "--out", matrix,
        });
        if (opts.ContainsKey("--from-scratch"))
            matrixArgs = matrixArgs.Append("--from-scratch");
        var code = CandidateMatrix.Run(matrixArgs.ToArray());
        if (code != 0) return code;

        var tableArgs = common.Concat(new[] { "--matrix", matrix, "--out", table });
        if (prefer.Length > 0)
            tableArgs = tableArgs.Concat(new[] { "--prefer", prefer });
        code = MatrixTable(tableArgs.ToArray());
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

    private static int Poe1Status(string[] args)
    {
        var opts = ParseOptions(args);
        var catalog = GetValue(opts, "--catalog", Poe1CatalogPath);
        var matrix = GetValue(opts, "--matrix", Poe1MatrixPath);
        var table = GetValue(opts, "--table", Poe1TablePath);
        var proof = GetValue(opts, "--proof", Poe1ProofPath);
        var status = GetValue(opts, "--status", Poe1StatusPath);
        var statusArgs = new List<string>
        {
            "--catalog", catalog, "--matrix", matrix, "--table", table,
            "--proof", proof, "--out", status,
        };
        if (opts.ContainsKey("--check")) statusArgs.Add("--check");
        if (opts.ContainsKey("--check-shortest")) statusArgs.Add("--check-shortest");
        return BuildStatus.Run(statusArgs.ToArray());
    }

    private static string? ReadDataVersion(string snapshotPath)
    {
        if (!File.Exists(snapshotPath)) return null;
        using var doc = JsonDocument.Parse(File.ReadAllText(snapshotPath));
        return doc.RootElement.TryGetProperty("dataVersion", out var version)
            ? version.GetString()
            : null;
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
        var prefer = opts.TryGetValue("--prefer", out var preferPath) ? preferPath : "";
        var timeout = GetValue(opts, "--timeout", "120");
        var vendor = GetValue(opts, "--vendor", "");

        var common = new List<string> { "--catalog", catalog };
        if (vendor.Length > 0) common.AddRange(["--vendor", vendor]);
        var matrixArgs = common.Concat(new[] { "--jobs", jobs, "--timeout", timeout, "--out", matrix });
        if (opts.ContainsKey("--from-scratch"))
            matrixArgs = matrixArgs.Append("--from-scratch");
        var code = CandidateMatrix.Run(matrixArgs.ToArray());
        if (code != 0) return code;

        var tableArgs = common.Concat(new[] { "--matrix", matrix, "--out", table });
        if (prefer.Length > 0)
            tableArgs = tableArgs.Concat(new[] { "--prefer", prefer });
        code = MatrixTable(tableArgs.ToArray());
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
        var preferPath = GetValue(opts, "--prefer", "");
        var vendorPath = GetValue(opts, "--vendor", "");

        var corpus = CorpusLoader.Load(catalogPath, vendorPath);
        var catalog = corpus.Catalog;
        FragmentTable? prefer = null;
        if (preferPath.Length > 0 && File.Exists(preferPath))
        {
            prefer = JsonSerializer.Deserialize<FragmentTable>(
                File.ReadAllText(preferPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
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

        string Prefer(Dictionary<string, string>? oldMap, string pattern,
            string chosen, ScopeCorpus scope)
        {
            if (oldMap is null || !oldMap.TryGetValue(pattern, out var old)) return chosen;
            if (old.Length != chosen.Length) return chosen;
            var option = ExactOracle.Check(scope, pattern, old);
            return option is not null && option.ForeignLines == 0 ? old : chosen;
        }

        var applied = 0;
        var stabilized = 0;
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
                    var preferred = Prefer(prefer?.Global, pattern, fragment, globalScope);
                    if (!string.Equals(preferred, fragment, StringComparison.Ordinal)) stabilized++;
                    global[pattern] = preferred;
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
                        var preferred = Prefer(
                            prefer?.ByClass.GetValueOrDefault(classId), pattern,
                            fragment, ScopeFor(classId));
                        if (!string.Equals(preferred, fragment, StringComparison.Ordinal)) stabilized++;
                        map[pattern] = preferred;
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

        var inGameWithMods = catalog.ItemClasses
            .Where(c => corpus.IsInGameClass(c.Id) &&
                        catalog.Mods.Any(m => m.Classes.Contains(c.Id)))
            .ToList();
        var anchors = inGameWithMods.ToDictionary(
            c => c.Id,
            c => corpus.ComputeClassNameAnchor(c.Id),
            StringComparer.Ordinal);
        var prefixes = inGameWithMods.ToDictionary(
            c => c.Id,
            c => corpus.ComputeClassNamePrefix(c.Id),
            StringComparer.Ordinal);
        var groups = ClassGroupOptimizer.Build(
            corpus, inGameWithMods.Select(c => c.Id).ToList());

        var compiler = new PoeRegexCompiler(corpus);
        var table = new FragmentTable
        {
            Game = catalog.Game,
            DataVersion = catalog.DataVersion,
            TargetModel = FragmentTable.AdvancedRanges,
            Global = global,
            ByClass = byClass,
            ClassAnchors = anchors,
            ClassPrefixes = prefixes,
            ClassGroups = groups,
            MinCapable = MinCapabilityBuilder.Build(catalog, corpus, compiler),
        };
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        File.WriteAllText(outPath, JsonSerializer.Serialize(table, new JsonSerializerOptions
        {
            WriteIndented = true,
        }));
        Console.WriteLine(
            $"matrix-table: applied {applied} scope choices, {stabilized} stabilized, " +
            $"{global.Count} global, {byClass.Count} classes -> {outPath}");
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
        var timeoutSeconds = int.TryParse(GetValue(opts, "--timeout", "60"), out var ts) && ts > 0 ? ts : 60;
        if (string.IsNullOrEmpty(pattern))
        {
            Console.Error.WriteLine("solve-exact requires --pattern");
            return 2;
        }
        var catalog = new FileCatalogSource(catalogPath).Load();
        var corpus = new EngineCorpus(catalog);
        var result = ExactSearchSolver.Solve(
            ScopeCorpus.Build(corpus, scope), pattern, min, max,
            timeout: TimeSpan.FromSeconds(timeoutSeconds));
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

    private static int Fetch(string[] args) =>
        FetchGame("poe2", "data/raw/poe2", "data/repoe.snapshot.json", args);

    private static int Poe1Fetch(string[] args) =>
        FetchGame("poe1", "data/raw/poe1", "data/repoe-poe1.snapshot.json", args);

    private static int FetchGame(
        string game, string defaultOutDir, string defaultSnapshotPath, string[] args)
    {
        var opts = ParseOptions(args);
        var outDir = GetValue(opts, "--out", defaultOutDir);
        var manifestPath = GetValue(opts, "--manifest", "data/repoe.manifest.json");
        var snapshotPath = GetValue(opts, "--snapshot", defaultSnapshotPath);
        var refresh = opts.ContainsKey("--refresh");

        if (!File.Exists(manifestPath))
        {
            Console.Error.WriteLine($"manifest not found: {manifestPath}");
            return 1;
        }

        using var manifestDoc = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var export = manifestDoc.RootElement.GetProperty("exports")
            .EnumerateArray()
            .FirstOrDefault(e => e.GetProperty("id").GetString() == game);
        if (export.ValueKind == JsonValueKind.Undefined)
        {
            Console.Error.WriteLine($"manifest has no {game} export");
            return 1;
        }

        var baseUrl = export.GetProperty("baseUrl").GetString()!.TrimEnd('/');
        var files = export.GetProperty("entries")
            .EnumerateArray()
            .Where(e => e.GetProperty("status").GetString() == "used")
            .Select(e => e.GetProperty("path").GetString()!)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        Directory.CreateDirectory(outDir);
        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PoeRegex.Data.Build/0.1");
        var snapshotFiles = new List<(string Path, long Bytes, string Sha256)>();
        var failures = 0;
        foreach (var file in files)
        {
            var dest = Path.Combine(outDir, file);
            if (File.Exists(dest) && !refresh)
            {
                Console.WriteLine($"  skip {file} (exists)");
            }
            else
            {
                Console.Write($"  download {file} ... ");
                var url = $"{baseUrl}/{file}";
                using var resp = client.GetAsync(url).GetAwaiter().GetResult();
                if (!resp.IsSuccessStatusCode)
                {
                    Console.WriteLine($"FAILED ({resp.StatusCode})");
                    failures++;
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                var bytes = resp.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                File.WriteAllBytes(dest, bytes);
                Console.WriteLine($"ok ({bytes.Length / 1024} KiB)");
            }

            var data = File.ReadAllBytes(dest);
            snapshotFiles.Add((file, data.Length, Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(data)).ToLowerInvariant()));
        }

        var downloadedAt = DateTime.UtcNow;
        var snapshot = new
        {
            game,
            export = game,
            downloadedAt = downloadedAt.ToString("O"),
            dataVersion = downloadedAt.ToString("yyyy-MM-dd"),
            source = baseUrl,
            files = snapshotFiles.Select(f => new
            {
                path = f.Path,
                bytes = f.Bytes,
                sha256 = f.Sha256,
            }).ToList(),
        };
        Directory.CreateDirectory(Path.GetDirectoryName(snapshotPath)!);
        File.WriteAllText(snapshotPath,
            JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }) + "\n");
        Console.WriteLine($"  snapshot {snapshotPath} ({snapshotFiles.Count} files)");
        Console.WriteLine(failures == 0 ? "done" : $"done with {failures} failure(s)");
        return failures == 0 ? 0 : 1;
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

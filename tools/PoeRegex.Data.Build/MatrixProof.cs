using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using PoeRegex.Core;
using PoeRegex.Core.Compilation;
using PoeRegex.Core.Data;

namespace PoeRegex.Data.Build;

/// <summary>
/// Re-checks every chosen matrix scope against the current candidate grammar
/// and proves that no shorter Strict/Minion-only candidate exists in that
/// grammar. This is the build-side proof step; it never writes the shipped
/// table.
/// </summary>
public static class MatrixProof
{
    private sealed record Work(
        string Family, string Pattern, string? ClassId, string ScopeLabel, string Chosen);

    private sealed record Failure(
        string Family, string Pattern, string Scope, string Chosen, string Shorter,
        int Length, string Profile, int ForeignMinion, int ForeignOther);

    public static int Run(string[] args)
    {
        var opts = ParseOptions(args);
        var gameName = GetValue(opts, "--game", "poe2");
        var game = gameName == "poe1" ? GameTarget.PoE1 : GameTarget.PoE2;
        var catalogPath = GetValue(opts, "--catalog", $"data/catalogs/{gameName}.catalog.json");
        var matrixPath = GetValue(opts, "--matrix", $"docs/CANDIDATE-MATRIX-{gameName}.html");
        var outPath = GetValue(opts, "--out", "");
        var familyFilter = GetValue(opts, "--family", "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);
        var jobs = int.TryParse(GetValue(opts, "--jobs", "0"), out var requestedJobs) &&
                   requestedJobs > 0
            ? requestedJobs
            : Environment.ProcessorCount;
        var shard = ParseShard(GetValue(opts, "--shard", ""));
        var minGlobal = int.TryParse(GetValue(opts, "--min-global", "3"), out var mg) ? mg : 3;
        var minClass = int.TryParse(GetValue(opts, "--min-class", "2"), out var mc) ? mc : 2;
        var exact = opts.ContainsKey("--exact");
        var mergeFiles = GetValue(opts, "--merge", "");
        if (mergeFiles.Length > 0)
            return MergeProofs(mergeFiles, matrixPath, outPath, opts.ContainsKey("--check"));

        var catalog = new FileCatalogSource(catalogPath).Load(game);
        var corpus = new EngineCorpus(catalog);
        var html = File.ReadAllText(matrixPath);
        const string marker = "window.MATRIX = ";
        var start = html.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) throw new InvalidOperationException("matrix JSON not found");
        start += marker.Length;
        var end = html.IndexOf(";</script>", start, StringComparison.Ordinal);
        if (end < 0) throw new InvalidOperationException("matrix JSON terminator not found");
        using var matrix = JsonDocument.Parse(html[start..end]);

        var work = new List<Work>();
        foreach (var family in matrix.RootElement.GetProperty("families").EnumerateArray())
        {
            var key = family.GetProperty("key").GetString() ?? "";
            if (familyFilter.Count > 0 && !familyFilter.Contains(key)) continue;
            foreach (var patternNode in family.GetProperty("patterns").EnumerateArray())
            {
                var raw = patternNode.GetProperty("pattern").GetString() ?? "";
                var pattern = raw.Replace('#', '\u0001');
                foreach (var scope in patternNode.GetProperty("scopes").EnumerateArray())
                {
                    if (!scope.TryGetProperty("chosen", out var chosenNode) ||
                        chosenNode.ValueKind != JsonValueKind.Object) continue;
                    var chosen = chosenNode.GetProperty("fragment").GetString() ?? "";
                    if (chosen.Length == 0) continue;
                    var kind = scope.GetProperty("kind").GetString() ?? "";
                    var classes = scope.GetProperty("classes").EnumerateArray()
                        .Select(x => x.GetString() ?? "")
                        .Where(x => x.Length > 0)
                        .ToList();
                    if (kind == "global")
                    {
                        work.Add(new Work(key, pattern, null, "global", chosen));
                    }
                    else
                    {
                        foreach (var classId in classes)
                            work.Add(new Work(key, pattern, classId, classId, chosen));
                    }
                }
            }
        }

        if (shard is { } shardInfo)
            work = work.Where((_, index) => index % shardInfo.Count == shardInfo.Index).ToList();

        var failures = new ConcurrentBag<Failure>();
        var checkedCount = 0;
        var missing = 0;
        var policyExcluded = 0;
        var completed = 0;
        Parallel.ForEach(work, new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, jobs),
        }, item =>
        {
            Interlocked.Increment(ref checkedCount);
            var progress = Interlocked.Increment(ref completed);
            if (progress % 50 == 0)
                Console.Error.WriteLine($"  proof progress {progress}/{work.Count}");
            var scopeCorpus = ScopeCorpus.Build(corpus, item.ClassId);
            var chosenOption = CandidateEvaluator.EvaluateFragment(
                scopeCorpus, item.Pattern, item.Chosen);
            if (chosenOption is null ||
                (exact ? chosenOption.ForeignLines > 0 : chosenOption.ForeignOtherLines > 0))
            {
                Interlocked.Increment(ref missing);
                failures.Add(new Failure(
                    item.Family, item.Pattern, item.ScopeLabel, item.Chosen,
                    "chosen fragment is not clean", item.Chosen.Length,
                    "none", 0, 0));
                return;
            }

            CandidateReport.Option? legal;
            if (exact)
            {
                legal = CandidateEvaluator.SolveExact(
                    scopeCorpus, item.Pattern,
                    item.ClassId is null ? minGlobal : minClass,
                    Math.Max(0, item.Chosen.Length - 1));
            }
            else
            {
                var shorter = CandidateEvaluator.EvaluateTop(
                    scopeCorpus, item.Pattern, current: null,
                    maxLength: Math.Max(0, item.Chosen.Length - 1), top: 1,
                    includeTwoWildcards: true, includeDeepPhrases: true);
                legal = shorter.Strict.FirstOrDefault() ?? shorter.Minion.FirstOrDefault();
            }
            if (legal is not null)
            {
                var minimum = item.ClassId is null ? minGlobal : minClass;
                if (legal.Length < minimum)
                {
                    Interlocked.Increment(ref policyExcluded);
                    return;
                }
                failures.Add(new Failure(
                    item.Family, item.Pattern, item.ScopeLabel, item.Chosen,
                    legal.Fragment, legal.Length,
                    "strict",
                    legal.ForeignMinionLines, legal.ForeignOtherLines));
            }
        });

        var orderedFailures = failures
            .OrderBy(x => x.Family, StringComparer.Ordinal)
            .ThenBy(x => x.Pattern, StringComparer.Ordinal)
            .ThenBy(x => x.Scope, StringComparer.Ordinal)
            .ToList();
        var report = new
        {
            game = catalog.Game,
            dataVersion = catalog.DataVersion,
            grammarVersion = "quoted-shortest-v1",
            generatedAt = DateTimeOffset.UtcNow.ToString("u"),
            matrix = Path.GetFileName(matrixPath),
            matrixSha256 = Hash(matrixPath),
            checkedScopes = checkedCount,
            workScopes = work.Count,
            coveredKeys = work
                .Select(w => $"{w.Family}\u0001{w.Pattern}\u0001{w.ClassId ?? ""}")
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray(),
            proven = checkedCount - orderedFailures.Count,
            failed = orderedFailures.Count,
            missingChosen = missing,
            policyExcluded,
            failures = orderedFailures,
            status = orderedFailures.Count == 0 ? "proven-in-grammar" : "shorter-candidate-found",
        };
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
        if (outPath.Length > 0)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
            File.WriteAllText(outPath, json);
            Console.WriteLine(
                $"matrix-proof: checked {checkedCount}/{work.Count}, " +
                $"{orderedFailures.Count} shorter candidate(s) -> {outPath}");
        }
        else
        {
            Console.WriteLine(json);
        }

        if (opts.ContainsKey("--check") && orderedFailures.Count > 0)
            return 1;
        return 0;
    }

    private static int MergeProofs(
        string files, string matrixPath, string outPath, bool check)
    {
        var paths = files.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var documents = new List<JsonDocument>();
        try
        {
            var expectedHash = Hash(matrixPath);
            var grammar = "quoted-shortest-v1";
            var dataVersion = "";
            var game = "poe2";
            var checkedScopes = 0;
            var workScopes = 0;
            var failed = 0;
            var policyExcluded = 0;
            var failures = new List<JsonElement>();
            var shards = 0;
            foreach (var path in paths)
            {
                if (!File.Exists(path))
                    throw new FileNotFoundException("proof shard not found", path);
                var doc = JsonDocument.Parse(File.ReadAllText(path));
                documents.Add(doc);
                var root = doc.RootElement;
                shards++;
                var hash = root.GetProperty("matrixSha256").GetString();
                if (!string.Equals(hash, expectedHash, StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        $"proof shard '{path}' was built from a different matrix");
                var shardGrammar = root.GetProperty("grammarVersion").GetString();
                if (!string.Equals(grammar, shardGrammar, StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        $"proof shard '{path}' uses grammar '{shardGrammar}', expected '{grammar}'");
                dataVersion = root.GetProperty("dataVersion").GetString() ?? dataVersion;
                game = root.GetProperty("game").GetString() ?? game;
                checkedScopes += root.GetProperty("checkedScopes").GetInt32();
                workScopes += root.GetProperty("workScopes").GetInt32();
                failed += root.GetProperty("failed").GetInt32();
                if (root.TryGetProperty("policyExcluded", out var excludedNode))
                    policyExcluded += excludedNode.GetInt32();
                foreach (var failure in root.GetProperty("failures").EnumerateArray())
                    failures.Add(failure.Clone());
            }

            var proven = failed == 0 && checkedScopes == workScopes && workScopes > 0;
            var report = new
            {
                game,
                dataVersion,
                grammarVersion = grammar,
                generatedAt = DateTimeOffset.UtcNow.ToString("u"),
                matrix = Path.GetFileName(matrixPath),
                matrixSha256 = expectedHash,
                shards,
                checkedScopes,
                workScopes,
                proven = checkedScopes - failed,
                failed,
                policyExcluded,
                failures,
                status = proven ? "proven-in-grammar" : "shorter-candidate-found",
            };
            var json = JsonSerializer.Serialize(report, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            });
            if (outPath.Length > 0)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
                File.WriteAllText(outPath, json);
                Console.WriteLine(
                    $"matrix-proof: merged {shards} shards, {checkedScopes}/{workScopes} checked, " +
                    $"{failed} failure(s) -> {outPath}");
            }
            else
            {
                Console.WriteLine(json);
            }
            if (check && !proven) return 1;
            return 0;
        }
        finally
        {
            foreach (var document in documents) document.Dispose();
        }
    }

    private static string Hash(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private static (int Index, int Count)? ParseShard(string value)
    {
        if (value.Length == 0) return null;
        var parts = value.Split('/');
        if (parts.Length != 2 ||
            !int.TryParse(parts[0], out var index) ||
            !int.TryParse(parts[1], out var count) ||
            count <= 0 || index < 0 || index >= count)
            throw new ArgumentException("--shard must be i/n with 0 <= i < n");
        return (index, count);
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

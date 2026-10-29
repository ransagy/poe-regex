using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using PoeRegex.Core.Compilation;
using PoeRegex.Core.Data;

namespace PoeRegex.Data.Build;

/// <summary>
/// Re-checks every chosen matrix scope against the exact G1 solver and proves
/// that no shorter legal form exists. This step never writes the shipped table.
/// </summary>
public static class MatrixProof
{
    private sealed record Work(
        string Family, string Pattern, string? ClassId, string ScopeLabel, string Chosen);

    private sealed record Failure(
        string Family, string Pattern, string Scope, string Chosen, string Shorter, int Length);

    public static int Run(string[] args)
    {
        var opts = ParseOptions(args);
        var catalogPath = GetValue(opts, "--catalog", "data/catalogs/poe2.catalog.json");
        var matrixPath = GetValue(opts, "--matrix", "docs/CANDIDATE-MATRIX-poe2.html");
        var outPath = GetValue(opts, "--out", "");
        var familyFilter = GetValue(opts, "--family", "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);
        var jobs = int.TryParse(GetValue(opts, "--jobs", "0"), out var requestedJobs) &&
                   requestedJobs > 0
            ? requestedJobs
            : Environment.ProcessorCount;
        var minGlobal = int.TryParse(GetValue(opts, "--min-global", "3"), out var mg) ? mg : 3;
        var minClass = int.TryParse(GetValue(opts, "--min-class", "2"), out var mc) ? mc : 2;

        var catalog = new FileCatalogSource(catalogPath).Load();
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

        var failures = new ConcurrentBag<Failure>();
        var checkedCount = 0;
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
            var chosenOption = ExactOracle.Check(scopeCorpus, item.Pattern, item.Chosen);
            if (chosenOption is null || chosenOption.ForeignLines > 0)
            {
                failures.Add(new Failure(
                    item.Family, item.Pattern, item.ScopeLabel, item.Chosen,
                    "chosen fragment is not strict", item.Chosen.Length));
                return;
            }

            var minimum = item.ClassId is null ? minGlobal : minClass;
            var legal = ExactOracle.Solve(
                scopeCorpus, item.Pattern, minimum,
                Math.Max(0, item.Chosen.Length - 1));
            if (legal is not null)
            {
                failures.Add(new Failure(
                    item.Family, item.Pattern, item.ScopeLabel, item.Chosen,
                    legal.Fragment, legal.Length));
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

    private static string Hash(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

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

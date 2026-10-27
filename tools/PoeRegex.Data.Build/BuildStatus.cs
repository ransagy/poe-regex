using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using PoeRegex.Core;
using PoeRegex.Core.Compilation;
using PoeRegex.Core.Data;

namespace PoeRegex.Data.Build;

/// <summary>
/// Read-only status report for the reviewed matrix artifact and the shipped
/// table. It makes build provenance and bound-limited scopes visible without
/// changing either artifact.
/// </summary>
public static class BuildStatus
{
    public static int Run(string[] args)
    {
        var opts = ParseOptions(args);
        var gameName = GetValue(opts, "--game", "poe2");
        var game = gameName == "poe1" ? GameTarget.PoE1 : GameTarget.PoE2;
        var catalogPath = GetValue(opts, "--catalog", $"data/catalogs/{gameName}.catalog.json");
        var matrixPath = GetValue(opts, "--matrix", $"docs/CANDIDATE-MATRIX-{gameName}.html");
        var tablePath = GetValue(opts, "--table", $"data/catalogs/{gameName}.fragments.json");
        var outPath = GetValue(opts, "--out", "");
        var proofPath = GetValue(opts, "--proof", $"docs/CANDIDATE-PROOF-{gameName}.json");
        var check = opts.ContainsKey("--check");

        var catalog = new FileCatalogSource(catalogPath).Load(game);
        var table = JsonSerializer.Deserialize<FragmentTable>(
            File.ReadAllText(tablePath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException($"could not read table {tablePath}");
        var html = File.ReadAllText(matrixPath);
        const string marker = "window.MATRIX = ";
        var start = html.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) throw new InvalidOperationException("matrix JSON not found");
        start += marker.Length;
        var end = html.IndexOf(";</script>", start, StringComparison.Ordinal);
        if (end < 0) throw new InvalidOperationException("matrix JSON terminator not found");
        using var matrix = JsonDocument.Parse(html[start..end]);

        var families = 0;
        var patterns = 0;
        var scopes = 0;
        var coverage = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var bounds = new SortedDictionary<int, int>();
        var longCurrentOnly = new List<object>();
        var boundCompleteCurrentOnly = 0;
        foreach (var family in matrix.RootElement.GetProperty("families").EnumerateArray())
        {
            families++;
            var familyKey = family.GetProperty("key").GetString() ?? "";
            foreach (var patternNode in family.GetProperty("patterns").EnumerateArray())
            {
                patterns++;
                var pattern = patternNode.GetProperty("pattern").GetString() ?? "";
                foreach (var scope in patternNode.GetProperty("scopes").EnumerateArray())
                {
                    scopes++;
                    var kind = scope.GetProperty("kind").GetString() ?? "";
                    var cov = scope.GetProperty("coverage").GetString() ?? "";
                    var key = $"{kind}/{cov}";
                    coverage[key] = coverage.GetValueOrDefault(key) + 1;
                    var bound = scope.TryGetProperty("bound", out var boundNode)
                        ? boundNode.GetInt32()
                        : 10;
                    bounds[bound] = bounds.GetValueOrDefault(bound) + 1;
                    if (!string.Equals(cov, "current-only", StringComparison.Ordinal)) continue;

                    var chosen = scope.GetProperty("chosen");
                    var length = chosen.GetProperty("length").GetInt32();
                    if (length <= bound)
                    {
                        boundCompleteCurrentOnly++;
                        continue;
                    }
                    longCurrentOnly.Add(new
                    {
                        family = familyKey,
                        pattern = pattern.Replace('\u0001', '#'),
                        kind,
                        classes = scope.GetProperty("classes")
                            .EnumerateArray().Select(x => x.GetString()).ToArray(),
                        bound,
                        length,
                        fragment = chosen.GetProperty("fragment").GetString(),
                    });
                }
            }
        }

        object? proof = null;
        var proven = false;
        if (File.Exists(proofPath))
        {
            using var proofDoc = JsonDocument.Parse(File.ReadAllText(proofPath));
            var root = proofDoc.RootElement;
            var checkedScopes = root.TryGetProperty("checkedScopes", out var checkedNode)
                ? checkedNode.GetInt32()
                : 0;
            var workScopes = root.TryGetProperty("workScopes", out var workNode)
                ? workNode.GetInt32()
                : 0;
            var failed = root.TryGetProperty("failed", out var failedNode)
                ? failedNode.GetInt32()
                : 0;
            var proofGrammar = root.TryGetProperty("grammarVersion", out var proofGrammarNode)
                ? proofGrammarNode.GetString()
                : null;
            proven = failed == 0 && checkedScopes == workScopes && workScopes > 0 &&
                      string.Equals(proofGrammar, "quoted-shortest-v1", StringComparison.Ordinal);
            proof = new
            {
                path = proofPath,
                grammarVersion = proofGrammar,
                checkedScopes,
                workScopes,
                failed,
                proven,
            };
        }

        var staleReasons = new List<string>();
        if (!string.Equals(table.DataVersion, catalog.DataVersion, StringComparison.Ordinal))
            staleReasons.Add(
                $"table dataVersion '{table.DataVersion}' != catalog '{catalog.DataVersion}'");
        if (table.ClassAnchors.Count == 0)
            staleReasons.Add("table has no class anchors");
        if (longCurrentOnly.Count > 0)
            staleReasons.Add(
                $"{longCurrentOnly.Count} current-only scope(s) are longer than their search bound");

        var report = new
        {
            game = catalog.Game,
            dataVersion = catalog.DataVersion,
            grammarVersion = "quoted-shortest-v1",
            oracleVersion = "indexed-term-v1",
            generatedAt = DateTimeOffset.UtcNow.ToString("u"),
            matrixGenerated = matrix.RootElement.TryGetProperty("generated", out var generated)
                ? generated.GetString()
                : null,
            files = new
            {
                catalog = FileInfo(catalogPath),
                matrix = FileInfo(matrixPath),
                table = FileInfo(tablePath),
            },
            matrix = new
            {
                families,
                patterns,
                scopes,
                coverage,
                bounds,
                boundCompleteCurrentOnly,
                longCurrentOnlyCount = longCurrentOnly.Count,
                longCurrentOnly,
            },
            table = new
            {
                global = table.Global.Count,
                classes = table.ByClass.Count,
                classFragments = table.ByClass.Values.Sum(x => x.Count),
                unresolvedGlobal = table.GlobalUnresolved.Count,
                unresolvedClass = table.ByClassUnresolved.Values.Sum(x => x.Count),
                classAnchors = table.ClassAnchors.Count,
            },
            status = new
            {
                stale = staleReasons.Count > 0,
                staleReasons,
                anchorsInMatrix = false,
                shortestProven = proven,
                boundLimited = longCurrentOnly.Count,
                proof,
            },
        };

        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
        if (outPath.Length > 0)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
            File.WriteAllText(outPath, json);
            Console.WriteLine($"matrix-status: {families} families, {scopes} scopes, " +
                $"{longCurrentOnly.Count} bound-limited current-only -> {outPath}");
        }
        else
        {
            Console.WriteLine(json);
        }

        if (!check) return 0;
        if (staleReasons.Any(x => x.StartsWith("table dataVersion", StringComparison.Ordinal)))
        {
            Console.Error.WriteLine("matrix-status: stale table data version");
            return 1;
        }
        if (opts.ContainsKey("--check-shortest") && longCurrentOnly.Count > 0)
        {
            Console.Error.WriteLine(
                $"matrix-status: {longCurrentOnly.Count} bound-limited current-only scope(s)");
            return 1;
        }
        return 0;
    }

    private static object FileInfo(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return new
        {
            path,
            bytes = bytes.Length,
            sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
        };
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

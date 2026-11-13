using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PoeRegex.Data.Build;

/// <summary>
/// Compares the committed RePoE/PoB inventory with what the public export
/// currently serves. The point is to notice a newly added table before a
/// feature depends on data that we never fetched.
/// </summary>
public static class DataAudit
{
    private const string DefaultManifest = "data/repoe.manifest.json";

    public static int Run(string[] args) =>
        RunAsync(args).GetAwaiter().GetResult();

    private static async Task<int> RunAsync(string[] args)
    {
        var opts = ParseOptions(args);
        var manifestPath = GetValue(opts, "--manifest", DefaultManifest);
        var outPath = GetValue(opts, "--out", "");
        var check = !opts.ContainsKey("--no-check");

        if (!File.Exists(manifestPath))
        {
            Console.Error.WriteLine($"manifest not found: {manifestPath}");
            return 1;
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var lines = new List<string>
        {
            $"# RePoE data audit — {DateTime.UtcNow:yyyy-MM-dd}",
            "",
            $"Manifest: `{manifestPath}`",
            "",
        };
        var failed = false;

        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("PoeRegex.Data.Build/0.1");

        foreach (var export in doc.RootElement.GetProperty("exports").EnumerateArray())
        {
            var id = export.GetProperty("id").GetString() ?? "?";
            var baseUrl = export.GetProperty("baseUrl").GetString() ?? "";
            // Some exports (PoE1) publish a generated README index instead of a
            // browsable directory; point the audit at the real listing.
            var indexUrl = export.TryGetProperty("indexUrl", out var iu)
                ? iu.GetString() ?? baseUrl + "/"
                : baseUrl + "/";
            var entries = export.GetProperty("entries").EnumerateArray().ToList();
            var mapped = entries
                .Select(e => e.GetProperty("path").GetString() ?? "")
                .Where(p => p.Length > 0)
                .ToHashSet(StringComparer.Ordinal);

            lines.Add($"## {id}");
            lines.Add("");
            lines.Add($"Source: {baseUrl}");

            var rootHtml = await Fetch(http, indexUrl);
            var remoteRoot = ParseIndex(rootHtml, isMainExport: id == "poe2")
                .Where(p => !p.StartsWith("stat_translations/", StringComparison.Ordinal))
                .ToHashSet(StringComparer.Ordinal);
            var mappedRoot = mapped
                .Where(p => !p.StartsWith("stat_translations/", StringComparison.Ordinal))
                .Where(p => p.TrimEnd('/').IndexOf('/') < 0)
                .ToHashSet(StringComparer.Ordinal);

            var rootUnmapped = remoteRoot.Except(mappedRoot, StringComparer.Ordinal)
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();
            var rootMissing = mappedRoot.Except(remoteRoot, StringComparer.Ordinal)
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();

            lines.Add($"Root entries: {mappedRoot.Count} mapped, {remoteRoot.Count} remote");
            AddList(lines, "Unmapped root entries", rootUnmapped);
            AddList(lines, "Mapped root entries no longer served", rootMissing);
            if (rootUnmapped.Count > 0) failed = true;

            if (id == "poe2")
            {
                var transUrl = baseUrl.TrimEnd('/') + "/stat_translations/";
                var transHtml = await Fetch(http, transUrl);
                // The directory index lists paths relative to stat_translations/;
                // normalize them back to export-root paths for comparison.
                var remoteTrans = ParseIndex(transHtml, isMainExport: true)
                    .Select(p => "stat_translations/" + p)
                    .ToHashSet(StringComparer.Ordinal);
                var mappedTrans = mapped
                    .Where(p => p.StartsWith("stat_translations/", StringComparison.Ordinal))
                    .Where(p => p.Length > "stat_translations/".Length)
                    .ToHashSet(StringComparer.Ordinal);
                var transUnmapped = remoteTrans.Except(mappedTrans, StringComparer.Ordinal)
                    .OrderBy(p => p, StringComparer.Ordinal)
                    .ToList();
                var transMissing = mappedTrans.Except(remoteTrans, StringComparer.Ordinal)
                    .OrderBy(p => p, StringComparer.Ordinal)
                    .ToList();
                lines.Add(
                    $"Stat-translation files: {mappedTrans.Count} mapped, {remoteTrans.Count} remote");
                AddList(lines, "Unmapped stat-translation files", transUnmapped);
                AddList(lines, "Mapped stat-translation files no longer served", transMissing);
                if (transUnmapped.Count > 0) failed = true;
            }

            lines.Add("");
        }

        lines.Add(failed ? "**RESULT: FAIL — new or unmapped export entries.**" : "**RESULT: PASS**");
        var report = string.Join('\n', lines) + "\n";
        Console.Write(report);
        if (outPath.Length > 0)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
            File.WriteAllText(outPath, report);
            Console.WriteLine($"wrote {outPath}");
        }

        return check && failed ? 1 : 0;
    }

    /// <summary>
    /// Pulls the hrefs out of the hosted directory index and normalizes them
    /// into paths relative to the export root. The tree generator prefixes
    /// hrefs with `.data/` and `./`; normalize both.
    /// </summary>
    internal static IReadOnlyList<string> ParseIndex(string html, bool isMainExport)
    {
        var result = new List<string>();
        foreach (Match match in Regex.Matches(
                     html,
                     "href=\"([^\"]+)\"[^>]*>(.*?)</a>",
                     RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            var href = System.Net.WebUtility.HtmlDecode(match.Groups[1].Value);
            if (href.StartsWith("http", StringComparison.OrdinalIgnoreCase)) continue;
            if (href is "../" or "./" or "." or "..") continue;
            if (href.StartsWith("../", StringComparison.Ordinal)) continue;

            var path = href;
            if (isMainExport)
            {
                if (path.StartsWith(".data/", StringComparison.Ordinal))
                    path = path[".data/".Length..];
                else if (path.StartsWith("./", StringComparison.Ordinal))
                    path = path[2..];
            }
            else if (path.StartsWith("./", StringComparison.Ordinal))
            {
                path = path[2..];
            }

            path = path.Trim('/');
            if (path.Length == 0 || path is "." or "data") continue;
            result.Add(path + (href.EndsWith('/') ? "/" : ""));
        }
        return result.Distinct(StringComparer.Ordinal).ToList();
    }

    private static async Task<string> Fetch(HttpClient http, string url)
    {
        using var response = await http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    private static void AddList(List<string> lines, string title, IReadOnlyList<string> items)
    {
        if (items.Count == 0) return;
        lines.Add("");
        lines.Add($"{title} ({items.Count}):");
        foreach (var item in items) lines.Add($"- `{item}`");
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal)) continue;
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                result[args[i]] = args[++i];
            }
            else
            {
                result[args[i]] = "";
            }
        }
        return result;
    }

    private static string GetValue(
        IReadOnlyDictionary<string, string> opts, string key, string fallback) =>
        opts.TryGetValue(key, out var value) && value.Length > 0 ? value : fallback;
}

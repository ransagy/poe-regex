using System.Text;
using System.Text.Json;
using PoeRegex.Core;
using PoeRegex.Core.Compilation;
using PoeRegex.Core.Data;

namespace PoeRegex.Data.Build;

/// <summary>
/// Generates an in-game test sheet from the shipped table. Class queries use
/// the actual anchor first, so the sheet cannot drift into colloquial class
/// names or stale fragments.
/// </summary>
public static class MatrixTests
{
    public static int Run(string[] args)
    {
        var opts = ParseOptions(args);
        var gameName = GetValue(opts, "--game", "poe2");
        var game = gameName == "poe1" ? GameTarget.PoE1 : GameTarget.PoE2;
        var tablePath = GetValue(opts, "--table", $"data/catalogs/{gameName}.fragments.json");
        var catalogPath = GetValue(opts, "--catalog", $"data/catalogs/{gameName}.catalog.json");
        var outPath = GetValue(opts, "--out", "");
        var familyFilter = GetValue(opts, "--family", "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);

        var catalog = new FileCatalogSource(catalogPath).Load(game);
        var table = JsonSerializer.Deserialize<FragmentTable>(
            File.ReadAllText(tablePath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException($"could not read table {tablePath}");

        var rows = new List<(string Family, string Pattern, string Scope, string Query)>();
        foreach (var group in catalog.Mods
                     .GroupBy(m => string.IsNullOrEmpty(m.Family) ? m.Id : m.Family,
                         StringComparer.Ordinal)
                     .OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            if (familyFilter.Count > 0 && !familyFilter.Contains(group.Key)) continue;
            if (!group.Any(m => m.Classes.Count == 0 ||
                                m.Classes.Any(c => GameItemClasses.IsInGame(game, c)))) continue;
            foreach (var pattern in group
                         .SelectMany(m => m.StatLines.Select(sl => sl.Pattern))
                         .Distinct(StringComparer.Ordinal)
                         .OrderBy(x => x, StringComparer.Ordinal))
            {
                if (table.Global.TryGetValue(pattern, out var global))
                    rows.Add((group.Key, pattern, "global", Quote(global)));
                foreach (var (cls, map) in table.ByClass.OrderBy(x => x.Key, StringComparer.Ordinal))
                {
                    if (!GameItemClasses.IsInGame(game, cls)) continue;
                    if (!map.TryGetValue(pattern, out var fragment)) continue;
                    if (!table.ClassAnchors.TryGetValue(cls, out var anchor) ||
                        string.IsNullOrEmpty(anchor)) continue;
                    rows.Add((group.Key, pattern, cls, Quote(": " + anchor) + " " + Quote(fragment)));
                }
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine("# PoE2 in-game test sheet");
        sb.AppendLine();
        sb.AppendLine("Fill the Result column with `correct`, a failure, or a short note.");
        sb.AppendLine();
        sb.AppendLine("| # | Family | Pattern | Scope | Query | Expected | Result |");
        sb.AppendLine("|---:|---|---|---|---|---|---|");
        var index = 0;
        foreach (var row in rows)
        {
            index++;
            sb.Append("| ").Append(index)
                .Append(" | ").Append(Escape(row.Family))
                .Append(" | `").Append(Escape(row.Pattern.Replace('\u0001', '#'))).Append('`')
                .Append(" | ").Append(Escape(row.Scope))
                .Append(" | `").Append(Escape(row.Query)).Append('`')
                .Append(" | target line only | |")
                .AppendLine();
        }

        var text = sb.ToString();
        if (outPath.Length > 0)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
            File.WriteAllText(outPath, text);
            Console.WriteLine($"matrix-tests: {rows.Count} rows -> {outPath}");
        }
        else
        {
            Console.Write(text);
        }
        return 0;
    }

    private static string Quote(string value) => "\"" + value + "\"";

    private static string Escape(string value) => value.Replace("|", "\\|");

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

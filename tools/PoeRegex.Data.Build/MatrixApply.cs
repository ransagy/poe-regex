using System.Text.Json;
using System.Text.Json.Nodes;
using PoeRegex.Core;
using PoeRegex.Core.Compilation;
using PoeRegex.Core.Data;

namespace PoeRegex.Data.Build;

/// <summary>
/// Applies proof candidates to the reviewed matrix in one pass. It is the
/// batch counterpart to matrix-proof: global scopes require at least three
/// characters, class scopes at least two, and every candidate is re-evaluated
/// against the current safety corpus before it is accepted.
/// </summary>
public static class MatrixApply
{
    private sealed record Failure(
        string Family, string Pattern, string Scope, string Chosen, string Shorter, int Length);

    public static int Run(string[] args)
    {
        var opts = ParseOptions(args);
        var gameName = GetValue(opts, "--game", "poe2");
        var game = gameName == "poe1" ? GameTarget.PoE1 : GameTarget.PoE2;
        var catalogPath = GetValue(opts, "--catalog", $"data/catalogs/{gameName}.catalog.json");
        var matrixPath = GetValue(opts, "--matrix", $"docs/CANDIDATE-MATRIX-{gameName}.html");
        var proofPath = GetValue(opts, "--proof", $"docs/CANDIDATE-PROOF-{gameName}.json");
        var outPath = GetValue(opts, "--out", "");
        var dryRun = opts.ContainsKey("--dry-run");
        var minGlobal = int.TryParse(GetValue(opts, "--min-global", "3"), out var mg) ? mg : 3;
        var minClass = int.TryParse(GetValue(opts, "--min-class", "2"), out var mc) ? mc : 2;

        var catalog = new FileCatalogSource(catalogPath).Load(game);
        var corpus = new EngineCorpus(catalog);
        var html = File.ReadAllText(matrixPath);
        const string marker = "window.MATRIX = ";
        var start = html.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) throw new InvalidOperationException("matrix JSON not found");
        start += marker.Length;
        var end = html.IndexOf(";</script>", start, StringComparison.Ordinal);
        if (end < 0) throw new InvalidOperationException("matrix JSON terminator not found");
        var root = JsonNode.Parse(html[start..end]) as JsonObject
            ?? throw new InvalidOperationException("matrix JSON root is not an object");
        if (root["families"] is not JsonArray families)
            throw new InvalidOperationException("matrix families array not found");

        using var proofDocument = JsonDocument.Parse(File.ReadAllText(proofPath));
        var failures = proofDocument.RootElement.GetProperty("failures").EnumerateArray()
            .Select(x => new Failure(
                x.GetProperty("family").GetString() ?? "",
                x.GetProperty("pattern").GetString() ?? "",
                x.GetProperty("scope").GetString() ?? "global",
                x.GetProperty("chosen").GetString() ?? "",
                x.GetProperty("shorter").GetString() ?? "",
                x.GetProperty("length").GetInt32()))
            .ToList();

        var scopes = new Dictionary<string, ScopeCorpus>(StringComparer.Ordinal)
        {
            [""] = ScopeCorpus.Build(corpus, null),
        };
        var applied = 0;
        var skipped = 0;
        var missing = 0;
        foreach (var failure in failures)
        {
            var isGlobal = failure.Scope == "global";
            var minimum = isGlobal ? minGlobal : minClass;
            if (failure.Shorter.Length < minimum)
            {
                skipped++;
                continue;
            }
            var scopeKey = isGlobal ? "" : failure.Scope;
            if (!scopes.TryGetValue(scopeKey, out var scopeCorpus))
            {
                scopeCorpus = ScopeCorpus.Build(corpus, isGlobal ? null : failure.Scope);
                scopes[scopeKey] = scopeCorpus;
            }
            var option = CandidateEvaluator.EvaluateFragment(
                scopeCorpus, failure.Pattern, failure.Shorter);
            if (option is null || option.ForeignOtherLines > 0)
            {
                skipped++;
                continue;
            }

            var family = families.OfType<JsonObject>().FirstOrDefault(
                x => string.Equals(x["key"]?.GetValue<string>(), failure.Family, StringComparison.Ordinal));
            var pattern = family?["patterns"] is JsonArray patternArray
                ? patternArray.OfType<JsonObject>().FirstOrDefault(x =>
                    string.Equals(
                        ((x["pattern"]?.GetValue<string>() ?? "").Replace('#', '\u0001')),
                        failure.Pattern.Replace('#', '\u0001'),
                        StringComparison.Ordinal))
                : null;
            if (pattern?["scopes"] is not JsonArray scopeArray)
            {
                missing++;
                continue;
            }

            JsonObject? target = null;
            if (isGlobal)
            {
                target = scopeArray.OfType<JsonObject>().FirstOrDefault(
                    x => string.Equals(x["kind"]?.GetValue<string>(), "global", StringComparison.Ordinal));
            }
            else
            {
                var group = scopeArray.OfType<JsonObject>().FirstOrDefault(x =>
                    x["kind"]?.GetValue<string>() is "class" or "per-class" &&
                    x["classes"] is JsonArray classes &&
                    classes.Any(c => string.Equals(c?.GetValue<string>(), failure.Scope, StringComparison.Ordinal)));
                if (group?["classes"] is JsonArray groupClasses && groupClasses.Count > 1)
                {
                    var index = scopeArray.IndexOf(group);
                    var split = new List<JsonObject>();
                    foreach (var cls in groupClasses.Select(c => c?.GetValue<string>() ?? "").Where(c => c.Length > 0))
                    {
                        var clone = (JsonObject)group.DeepClone();
                        clone["classes"] = new JsonArray(cls);
                        clone["kind"] = "class";
                        split.Add(clone);
                    }
                    scopeArray.RemoveAt(index);
                    foreach (var clone in split) scopeArray.Add(clone);
                    target = split.FirstOrDefault(x =>
                        x["classes"] is JsonArray c &&
                        c.Any(y => string.Equals(y?.GetValue<string>(), failure.Scope, StringComparison.Ordinal)));
                }
                else
                {
                    target = group;
                }
            }

            if (target is null)
            {
                missing++;
                continue;
            }
            var current = target["chosen"] as JsonObject;
            var currentFragment = current?["fragment"]?.GetValue<string>() ?? "";
            if (currentFragment.Length > 0 && failure.Shorter.Length >= currentFragment.Length)
            {
                skipped++;
                continue;
            }

            var optionJson = OptionJson(option);
            var currentMatrix = target["currentOption"] as JsonObject;
            var currentQuoted = currentMatrix?["quotedLength"]?.GetValue<int>()
                ?? (currentFragment.Length + 2);
            target["chosen"] = optionJson;
            target["coverage"] = option.ForeignLines == 0 ? "strict" : "minion";
            target["save"] = Math.Max(0, currentQuoted - (option.Length + 2));
            var listName = option.ForeignLines == 0 ? "strict" : "minion";
            if (target[listName] is JsonArray options &&
                !options.OfType<JsonObject>().Any(x =>
                    string.Equals(x["fragment"]?.GetValue<string>(), option.Fragment, StringComparison.Ordinal)))
                options.Add(optionJson.DeepClone());
            applied++;
        }

        root["generated"] = DateTimeOffset.UtcNow.ToString("u");
        var json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = false })
            .Replace("</script", "<\\/script", StringComparison.OrdinalIgnoreCase);
        if (!dryRun)
        {
            File.WriteAllText(outPath, CandidateMatrix.Html(
                json, families.Count, families.Count, 14));
        }
        Console.WriteLine(
            $"matrix-apply: {applied} applied, {skipped} policy/safety skipped, " +
            $"{missing} missing, {failures.Count} proof candidates" +
            (dryRun ? " (dry run)" : $" -> {outPath}"));
        return missing > 0 ? 1 : 0;
    }

    private static JsonObject OptionJson(CandidateReport.Option option) => new()
    {
        ["fragment"] = option.Fragment,
        ["length"] = option.Length,
        ["foreignMinion"] = option.ForeignMinionLines,
        ["foreignOther"] = option.ForeignOtherLines,
        ["termSplits"] = option.TermSplits,
        ["wildcard"] = option.HasWildcard,
        ["anchored"] = option.Fragment.StartsWith('^') || option.Fragment.EndsWith('$'),
        ["guardLetters"] = option.GuardLetters,
        ["quoted"] = "\"" + option.Fragment + "\"",
        ["quotedLength"] = option.Length + 2,
    };

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

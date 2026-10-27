using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PoeRegex.Core;
using PoeRegex.Core.Compilation;
using PoeRegex.Core.Data;

namespace PoeRegex.Data.Build;

/// <summary>
/// Generates a self-contained, searchable HTML artifact for comparing
/// candidate fragments under several explicit safety/scope profiles. It is a
/// data-build artifact only; the web runtime does not consume it.
///
/// Scope policy is uniform: every family gets the same grammar, and every
/// applicable item class is evaluated. Identical class results collapse into
/// one "per-class" row; only genuinely different results get class rows.
/// </summary>
public static class CandidateMatrix
{
    /// <summary>
    /// In-game verified reference forms. These are shown even when a shorter
    /// dominated variant exists, so the review can compare the known answer
    /// with what the generator currently prefers.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string[]> KnownReferences =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["+\u0001 to maximum Life"] = [@"\+\d.+m life"],
        };

    private sealed record MatrixOption(
        string Fragment, int Length, int ForeignMinion, int ForeignOther,
        bool TermSplits, bool Wildcard, bool Anchored, int GuardLetters,
        string Quoted, int QuotedLength);

    private sealed record MatrixScope(
        string Kind, IReadOnlyList<string> Classes, string Current,
        MatrixOption? CurrentOption,
        IReadOnlyList<MatrixOption> Known,
        int Targets, int TargetMax, int Bound,
        IReadOnlyList<MatrixOption> Strict,
        IReadOnlyList<MatrixOption> Minion,
        MatrixOption? Floor,
        IReadOnlyList<MatrixOption> Floors,
        MatrixOption? Chosen,
        string Coverage,
        int Save);

    private sealed record MatrixPattern(
        string Pattern, string Display, IReadOnlyList<MatrixScope> Scopes);

    private sealed record MatrixFamily(
        string Key, string Prefix, string Display, int Tiers,
        IReadOnlyList<string> Classes, IReadOnlyList<MatrixPattern> Patterns);

    private sealed record WorkItem(
        string? Scope, string Family, string Pattern, MatrixScope Result);

    public static int Run(string[] args)
    {
        var opts = ParseOptions(args);
        var gameName = GetValue(opts, "--game", "poe2");
        var game = gameName == "poe1" ? GameTarget.PoE1 : GameTarget.PoE2;
        var catalogPath = GetValue(opts, "--catalog", $"data/catalogs/{gameName}.catalog.json");
        var outPath = GetValue(opts, "--out", $"docs/CANDIDATE-MATRIX-{gameName}.html");
        var only = GetValue(opts, "--family", "");
        // The matrix is a shortening review: 14 captures every candidate
        // that can beat the common shipped fragments. Pass --max-length 24
        // for the exhaustive grammar when needed.
        var maxLength = int.TryParse(GetValue(opts, "--max-length", "14"), out var ml) ? ml : 14;
        var mergeFiles = GetValue(opts, "--merge", "");
        if (mergeFiles.Length > 0)
            return Merge(mergeFiles, outPath, maxLength);
        var jobs = int.TryParse(GetValue(opts, "--jobs", "0"), out var requestedJobs) &&
                   requestedJobs > 0
            ? requestedJobs
            : Environment.ProcessorCount;

        var catalog = new FileCatalogSource(catalogPath).Load(game);
        var corpus = new EngineCorpus(catalog);
        var table = LoadTable($"data/catalogs/{gameName}.fragments.json");
        var familyKeys = only.Length > 0
            ? only.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : CandidateReport.AllFamilies(catalog).ToArray();
        var chunk = GetValue(opts, "--chunk", "");
        if (only.Length == 0 && chunk.Length > 0)
        {
            var parts = chunk.Split('/');
            if (parts.Length == 2 &&
                int.TryParse(parts[0], out var chunkIndex) &&
                int.TryParse(parts[1], out var chunkCount) &&
                chunkCount > 0 && chunkIndex > 0 && chunkIndex <= chunkCount)
            {
                var offset = chunkIndex - 1;
                familyKeys = familyKeys
                    .Where((_, index) => index % chunkCount == offset)
                    .ToArray();
            }
        }

        var modsByFamily = catalog.Mods
            .GroupBy(m => FamilyKey(m), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var applicableClasses = familyKeys
            .Where(modsByFamily.ContainsKey)
            .SelectMany(key => modsByFamily[key])
            .SelectMany(m => m.Classes)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        var scopes = new List<string?> { null };
        scopes.AddRange(applicableClasses);

        // Materialize the shared corpus/indexes once, then partition the work
        // by scope. Each worker owns its ScopeCorpus (trigram index + caches);
        // the corpus itself is read-only after this point.
        _ = corpus.RealLinesGlobal();
        _ = corpus.SafetyItems;
        _ = corpus.SafetyLines(null).ToList();
        foreach (var itemClass in applicableClasses)
        {
            _ = corpus.RealLinesForClass(itemClass);
            _ = corpus.SearchSectionsForClass(itemClass);
        }

        var scopeCorpora = scopes
            .Select(scope => (Scope: scope, Corpus: ScopeCorpus.Build(corpus, scope)))
            .ToList();

        // Balance the work across (family, scope) tasks rather than pinning
        // the whole global union to one worker. ScopeCorpus caches are
        // concurrent; the line lists and trigram postings are immutable.
        var tasks = new List<(string? Scope, ScopeCorpus Corpus, string Family)>();
        foreach (var (scope, scopeCorpus) in scopeCorpora)
        {
            foreach (var key in familyKeys)
            {
                if (!modsByFamily.TryGetValue(key, out var mods)) continue;
                if (scope is not null &&
                    !mods.Any(m => m.Classes.Contains(scope, StringComparer.Ordinal))) continue;
                tasks.Add((scope, scopeCorpus, key));
            }
        }

        var results = new ConcurrentBag<WorkItem>();
        Parallel.ForEach(tasks, new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, jobs),
        }, task =>
        {
            var (scope, scopeCorpus, key) = task;
            var mods = modsByFamily[key];
            var patterns = mods
                .Where(m => scope is null || m.Classes.Contains(scope, StringComparer.Ordinal))
                .SelectMany(m => m.StatLines.Select(sl => sl.Pattern))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(x => x, StringComparer.Ordinal);

            foreach (var pattern in patterns)
            {
                if (!scopeCorpus.HasPattern(pattern)) continue;
                var current = CurrentFragment(table, scope, pattern);
                var targets = scopeCorpus.Targets(pattern);
                if (targets.Count == 0) continue;
                var top = CandidateEvaluator.EvaluateTop(
                    scopeCorpus, pattern, current, maxLength);
                var currentOption = CandidateEvaluator.EvaluateFragment(
                    scopeCorpus, pattern, current);
                var known = scope is null &&
                            KnownReferences.TryGetValue(pattern, out var knownForms)
                    ? knownForms
                        .Select(form => CandidateEvaluator.EvaluateFragment(
                            scopeCorpus, pattern, form))
                        .Where(option => option is not null)
                        .Select(option => option!)
                        .ToList()
                    : [];
                results.Add(new WorkItem(scope, key, pattern, ToScope(
                    scope is null ? "global" : "class",
                    scope is null ? [] : [scope],
                    current,
                    currentOption,
                    known,
                    targets.Count,
                    maxLength,
                    top)));
            }
        });

        var grouped = results
            .GroupBy(x => x.Family, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var families = new List<MatrixFamily>();
        foreach (var key in familyKeys)
        {
            if (!grouped.TryGetValue(key, out var items) || items.Count == 0) continue;
            var mods = modsByFamily[key];
            var patterns = new List<MatrixPattern>();
            foreach (var patternGroup in items
                         .GroupBy(x => x.Pattern, StringComparer.Ordinal)
                         .OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                var scopesOut = new List<MatrixScope>();
                var global = patternGroup.FirstOrDefault(x => x.Scope is null);
                if (global is not null) scopesOut.Add(global.Result);

                var classRows = patternGroup
                    .Where(x => x.Scope is not null)
                    .GroupBy(x => Signature(x.Result), StringComparer.Ordinal)
                    .Select(g => new
                    {
                        Classes = g.Select(x => x.Scope!)
                            .Distinct(StringComparer.Ordinal)
                            .OrderBy(x => x, StringComparer.Ordinal)
                            .ToList(),
                        Result = g.First().Result with
                        {
                            Targets = g.Min(x => x.Result.Targets),
                            TargetMax = g.Max(x => x.Result.TargetMax),
                            Floors = g.SelectMany(x => x.Result.Floors)
                                .DistinctBy(Option, StringComparer.Ordinal)
                                .ToList(),
                        },
                    })
                    .OrderBy(x => x.Classes[0], StringComparer.Ordinal)
                    .ToList();

                if (classRows.Count == 1)
                {
                    scopesOut.Add(WithGroup(classRows[0].Result, "per-class", classRows[0].Classes));
                }
                else
                {
                    foreach (var row in classRows)
                        scopesOut.Add(WithGroup(row.Result, "class", row.Classes));
                }

                patterns.Add(new MatrixPattern(
                    patternGroup.Key.Replace('\u0001', '#'),
                    PatternDisplay(patternGroup.Key),
                    scopesOut));
            }

            families.Add(new MatrixFamily(
                key,
                mods.Select(m => m.GenerationType).FirstOrDefault() ?? "",
                PatternDisplay(patterns[0].Pattern.Replace('#', '\u0001')),
                mods.Select(m => m.Name ?? "").Distinct(StringComparer.Ordinal).Count(),
                mods.SelectMany(m => m.Classes)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(x => x, StringComparer.Ordinal)
                    .ToList(),
                patterns));
        }

        var data = new
        {
            Game = gameName,
            Generated = DateTimeOffset.UtcNow.ToString("u"),
            Families = families,
        };

        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        }).Replace("</script", "<\\/script", StringComparison.OrdinalIgnoreCase);

        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        File.WriteAllText(outPath, Html(json, families.Count, familyKeys.Length, maxLength));
        Console.WriteLine($"wrote {outPath} ({new FileInfo(outPath).Length / 1024} KiB, {families.Count} families)");
        return 0;
    }

    private static MatrixScope ToScope(
        string kind,
        IReadOnlyList<string> classes,
        string current,
        CandidateReport.Option? currentOption,
        IReadOnlyList<CandidateReport.Option> known,
        int targets,
        int bound,
        TopOptions top)
    {
        MatrixOption ToOption(CandidateReport.Option o) => new(
            o.Fragment, o.Length, o.ForeignMinionLines, o.ForeignOtherLines,
            o.TermSplits, o.HasWildcard,
            o.Fragment.StartsWith('^') || o.Fragment.EndsWith('$'),
            o.GuardLetters,
            "\"" + o.Fragment + "\"",
            o.Fragment.Length + 2);

        var floor = top.Floor is null ? null : ToOption(top.Floor);
        var chosenOption = top.Strict
            .Concat(top.Minion)
            .OrderBy(o => o.Length)
            .ThenByDescending(o => o.GuardLetters)
            .ThenBy(o => o.Fragment, StringComparer.Ordinal)
            .FirstOrDefault();
        var chosen = chosenOption is null ? null : ToOption(chosenOption);
        var currentMatrix = currentOption is null ? null : ToOption(currentOption);
        var currentOnly = chosenOption is not null && currentOption is not null &&
                          string.Equals(chosenOption.Fragment, currentOption.Fragment, StringComparison.Ordinal);
        var coverage = chosenOption is null
            ? "none"
            : currentOnly ? "current-only"
            : top.Strict.Contains(chosenOption) ? "strict" : "minion";
        var save = currentMatrix is null || chosen is null
            ? 0
            : currentMatrix.QuotedLength - chosen.QuotedLength;
        return new MatrixScope(
            kind,
            classes,
            current,
            currentMatrix,
            known.Select(ToOption).ToList(),
            targets,
            targets,
            bound,
            top.Strict.Select(ToOption).ToList(),
            top.Minion.Select(ToOption).ToList(),
            floor,
            floor is null ? [] : [floor],
            chosen,
            coverage,
            save);
    }

    private static MatrixScope WithGroup(
        MatrixScope scope, string kind, IReadOnlyList<string> classes)
    {
        var floors = scope.Floors
            .DistinctBy(Option, StringComparer.Ordinal)
            .ToList();
        return scope with
        {
            Kind = kind,
            Classes = classes,
            Floor = floors.Count == 1 ? floors[0] : null,
            Floors = floors,
            Known = scope.Known
                .DistinctBy(Option, StringComparer.Ordinal)
                .ToList(),
        };
    }

    private static string CurrentFragment(FragmentTable? table, string? scope, string pattern)
    {
        if (table is null) return "";
        if (scope is null) return table.Global.GetValueOrDefault(pattern) ?? "";
        return table.ByClass.GetValueOrDefault(scope)?.GetValueOrDefault(pattern) ?? "";
    }

    private static string FamilyKey(ModEntry mod) =>
        mod.Family.Length > 0 ? mod.Family : mod.Id;

    private static string PatternDisplay(string pattern) =>
        pattern.Replace('\u0001', ' ').Trim();

    private static string Signature(MatrixScope scope) =>
        string.Join("|",
            scope.Current,
            Option(scope.CurrentOption),
            Options(scope.Known),
            Options(scope.Strict),
            Options(scope.Minion),
            Option(scope.Chosen),
            scope.Coverage,
            scope.Save);

    private static string Options(IReadOnlyList<MatrixOption> options) =>
        string.Join(",", options.Select(Option));

    private static string Option(MatrixOption? option) =>
        option is null
            ? "-"
            : $"{option.Fragment}:{option.Length}:{option.ForeignMinion}:{option.ForeignOther}:" +
              $"{option.TermSplits}:{option.Wildcard}:{option.Anchored}:{option.GuardLetters}:" +
              $"{option.QuotedLength}";

    private static int Merge(string files, string outPath, int maxLength)
    {
        var paths = files.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var families = new List<JsonNode>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var game = "poe2";
        foreach (var path in paths)
        {
            if (!File.Exists(path)) continue;
            var fileBound = path.Contains("m14", StringComparison.OrdinalIgnoreCase) ? 14 : 10;
            var html = File.ReadAllText(path);
            const string marker = "window.MATRIX = ";
            var start = html.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0) continue;
            start += marker.Length;
            var end = html.IndexOf(";</script>", start, StringComparison.Ordinal);
            if (end < 0) continue;
            var node = JsonNode.Parse(html[start..end]) as JsonObject;
            if (node is null) continue;
            if (node["game"]?.GetValue<string>() is { Length: > 0 } g) game = g;
            if (node["families"] is not JsonArray array) continue;
            foreach (var item in array)
            {
                if (item is not JsonObject family) continue;
                var key = family["key"]?.GetValue<string>() ?? "";
                if (key.Length == 0 || !seen.Add(key)) continue;
                var clone = (JsonObject)family.DeepClone();
                if (clone["patterns"] is JsonArray patterns)
                {
                    foreach (var pattern in patterns.OfType<JsonObject>())
                    {
                        if (pattern["scopes"] is not JsonArray scopes) continue;
                        foreach (var scope in scopes.OfType<JsonObject>())
                        {
                            if (scope["bound"] is null) scope["bound"] = fileBound;
                            var chosen = scope["chosen"] as JsonObject;
                            var current = scope["currentOption"] as JsonObject;
                            if (chosen is null || current is null) continue;
                            var chosenFragment = chosen["fragment"]?.GetValue<string>();
                            var currentFragment = current["fragment"]?.GetValue<string>();
                            if (chosenFragment is not null &&
                                string.Equals(chosenFragment, currentFragment, StringComparison.Ordinal))
                                scope["coverage"] = "current-only";
                        }
                    }
                }
                families.Add(clone);
            }
        }

        var ordered = families
            .OrderBy(x => x["key"]?.GetValue<string>() ?? "", StringComparer.Ordinal)
            .ToList();
        var result = new JsonArray();
        foreach (var family in ordered) result.Add(family);
        var merged = new JsonObject
        {
            ["game"] = game,
            ["generated"] = DateTimeOffset.UtcNow.ToString("u"),
            ["families"] = result,
        };
        var json = merged.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = false,
        }).Replace("</script", "<\\/script", StringComparison.OrdinalIgnoreCase);
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        File.WriteAllText(outPath, Html(json, ordered.Count, ordered.Count, maxLength));
        Console.WriteLine($"merged {paths.Length} files, {ordered.Count} families -> {outPath}");
        return 0;
    }

    private static string Html(string json, int familyCount, int requestedCount, int maxLength)
    {
        const string css = """
            :root { --bg:#101418; --panel:#171d24; --line:#2d3742; --text:#d8dee8; --muted:#8995a5; --gold:#d9a441; --good:#6fcf97; --warn:#f2c94c; --bad:#eb5757; --accent:#8fb8ff; }
            * { box-sizing: border-box; }
            body { margin:0; background:var(--bg); color:var(--text); font:14px/1.45 system-ui,Segoe UI,sans-serif; }
            header { padding:24px 28px 16px; border-bottom:1px solid var(--line); background:#141a20; }
            h1 { margin:0 0 6px; color:var(--gold); font-size:22px; }
            .sub { color:var(--muted); max-width:1100px; }
            .defs { display:grid; grid-template-columns:repeat(auto-fit,minmax(220px,1fr)); gap:10px; margin:16px 0 0; }
            .def { border:1px solid var(--line); border-radius:6px; padding:10px 12px; background:var(--panel); }
            .def b { color:var(--accent); display:block; margin-bottom:3px; }
            .legend { margin-top:14px; border:1px solid var(--line); border-radius:6px; background:var(--panel); }
            .legend summary { cursor:pointer; padding:10px 12px; color:var(--gold); font-weight:600; }
            .legend .defs { margin:0; padding:0 12px 12px; }
            .toolbar { position:sticky; top:0; z-index:2; display:flex; gap:10px; flex-wrap:wrap; align-items:center; padding:12px 28px; background:#12181e; border-bottom:1px solid var(--line); }
            input, select { background:#0d1216; color:var(--text); border:1px solid var(--line); border-radius:4px; padding:8px 10px; font:inherit; }
            input { min-width:280px; flex:1; max-width:620px; }
            .count { color:var(--muted); margin-left:auto; }
            main { padding:18px 28px 80px; max-width:1500px; margin:0 auto; }
            details { border:1px solid var(--line); border-radius:6px; margin:8px 0; background:var(--panel); overflow:hidden; }
            summary { cursor:pointer; padding:10px 12px; font-weight:600; color:var(--gold); }
            summary span.meta { color:var(--muted); font-weight:400; margin-left:10px; }
            .patterns { padding:0 12px 12px; }
            .pattern { border-top:1px solid var(--line); padding:10px 0; }
            .pattern h4 { margin:0 0 8px; font-size:13px; color:var(--text); }
            .pattern h4 code { color:var(--accent); background:#0d1216; padding:2px 5px; border-radius:3px; }
            .scope { margin:8px 0 12px; }
            .scope h5 { margin:0 0 6px; color:var(--muted); font-size:12px; text-transform:uppercase; letter-spacing:.06em; }
            .scope h5 .targets { color:var(--muted); text-transform:none; letter-spacing:0; font-weight:400; }
            .current { margin-bottom:8px; color:var(--muted); }
            .current code { color:var(--good); background:#0d1216; padding:2px 5px; border-radius:3px; }
            .chosen { margin-bottom:8px; color:var(--good); }
            .chosen code { color:var(--good); background:#0d1216; padding:2px 5px; border-radius:3px; }
            .known { margin-bottom:8px; color:var(--muted); }
            .known code { color:var(--accent); background:#0d1216; padding:2px 5px; border-radius:3px; }
            .floor { margin-bottom:8px; color:var(--muted); }
            .floor code { color:var(--warn); background:#0d1216; padding:2px 5px; border-radius:3px; }
            .floor .diag { color:var(--warn); font-style:italic; }
            .profiles { display:grid; grid-template-columns:repeat(auto-fit,minmax(190px,1fr)); gap:8px; }
            .profile { border:1px solid var(--line); border-radius:5px; padding:8px; background:#12181e; }
            .profile h6 { margin:0 0 5px; color:var(--accent); font-size:11px; text-transform:uppercase; letter-spacing:.05em; }
            .opt { display:flex; gap:6px; align-items:baseline; flex-wrap:wrap; padding:3px 0; border-top:1px dotted #25303a; }
            .opt:first-of-type { border-top:0; }
            .opt code { color:var(--text); background:#0d1216; padding:1px 4px; border-radius:3px; }
            .badge { font-size:11px; color:var(--muted); }
            .badge.len { color:var(--gold); }
            .badge.cross { color:var(--warn); }
            .badge.foreign { color:var(--bad); }
            .badge.diag { color:var(--warn); text-transform:uppercase; letter-spacing:.05em; }
            .empty { color:var(--muted); font-style:italic; }
            @media (max-width:700px) { header,.toolbar,main { padding-left:14px; padding-right:14px; } .sub { font-size:13px; } }
            """;
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>Candidate matrix</title><style>").Append(css).Append("</style></head><body>");
        sb.Append("<header><h1>Candidate matrix</h1><div class=\"sub\">Compare fragment choices for every catalog family. ").Append(familyCount).Append(" families loaded from ").Append(requestedCount).Append(" requested. Search bounds vary by scope (10 or 14 characters); each scope header shows its bound. The current shipped fragment is always shown. This is a data-build artifact; it does not change the runtime table.</div>");
        sb.Append("<div class=\"defs\">");
        sb.Append("<div class=\"def\"><b>Strict</b>Zero same-line foreign lines. The compiler emits this candidate as a quoted, line-local term.</div>");
        sb.Append("<div class=\"def\"><b>Minion-only</b>Same-line foreign lines are only minion / allies / presence variants.</div>");
        sb.Append("</div>");
        sb.Append("<details class=\"legend\"><summary>Legend — what the numbers and labels mean</summary><div class=\"defs\">");
        sb.Append("<div class=\"def\"><b>len</b>Candidate length in characters, including anchors and wildcard dots.</div>");
        sb.Append("<div class=\"def\"><b>min</b>Foreign modeled lines that are minion, allies, or presence lines.</div>");
        sb.Append("<div class=\"def\"><b>other</b>All other foreign modeled lines matching the candidate.</div>");
        sb.Append("<div class=\"def\"><b>splits</b>The fragment contains whitespace. Quoted, it is one line-local term; unquoted, the game would split it into separate ANDed terms.</div>");
        sb.Append("<div class=\"def\"><b>current</b>The fragment currently shipped for this pattern and scope, with its evaluated same-line counts.</div>");
        sb.Append("<div class=\"def\"><b>known</b>An in-game verified reference form. It is shown even when a shorter dominated variant exists.</div>");
        sb.Append("<div class=\"def\"><b>chosen</b>The shortest candidate under the current hierarchy. The quoted form is what the compiler emits; the inner length excludes the two quotes.</div>");
        sb.Append("<div class=\"def\"><b>coverage</b>strict, minion, current-only, or none. current-only means no shorter form exists in the searched grammar; the shipped entry stays.</div>");
        sb.Append("<div class=\"def\"><b>save</b>Quoted characters saved against the current fragment.</div>");
        sb.Append("<div class=\"def\"><b>none</b>No candidate passes that profile after the whole grammar is evaluated. It does not mean the mod is unresolved.</div>");
        sb.Append("<div class=\"def\"><b>floor</b>The shortest raw candidate regardless of collisions. Diagnostic only — never a shipping proposal.</div>");
        sb.Append("<div class=\"def\"><b>per-class</b>Every applicable class produces the same result; the classes are listed. No class is singled out.</div>");
        sb.Append("<div class=\"def\"><b>class</b>Class results differ. Each row lists the classes that share that result.</div>");
        sb.Append("</div></details></header>");
        sb.Append("<div class=\"toolbar\"><input id=\"q\" placeholder=\"filter family, stat text, or class…\" autofocus><select id=\"scope\"><option value=\"\">all scopes</option><option value=\"global\">global</option><option value=\"per-class\">per-class</option><option value=\"class\">class-specific</option></select><span class=\"count\" id=\"count\"></span></div>");
        sb.Append("<main id=\"out\"></main><script>window.MATRIX = ").Append(json).Append(";</script>");
        sb.Append("""
            <script>
                const root = document.getElementById('out');
                const q = document.getElementById('q');
                const scopeSel = document.getElementById('scope');
                const count = document.getElementById('count');
                function esc(s) { return String(s).replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c])); }
                function opt(o) {
                  if (!o || !o.fragment) return '<span class="empty">none</span>';
                  const text = o.quoted || ('"' + o.fragment + '"');
                  const bits = [`<code>${esc(text)}</code>`, `<span class="badge len">${o.quotedLength ?? (o.length + 2)}</span>`, `<span class="badge">inner ${o.length}</span>`];
                  if (o.foreignMinion) bits.push(`<span class="badge">min ${o.foreignMinion}</span>`);
                  if (o.foreignOther) bits.push(`<span class="badge foreign">other ${o.foreignOther}</span>`);
                  if (o.termSplits) bits.push('<span class="badge cross">splits if unquoted</span>');
                  if (o.guardLetters) bits.push(`<span class="badge">guard ${o.guardLetters}</span>`);
                  if (o.anchored) bits.push('<span class="badge">anchored</span>');
                  return bits.join(' ');
                }
                function profile(label, list) {
                  const body = list && list.length
                    ? list.map(o => `<div class="opt">${opt(o)}</div>`).join('')
                    : '<span class="empty">none — no candidate passes</span>';
                  return `<div class="profile"><h6>${label}</h6>${body}</div>`;
                }
                function scopeTitle(s) {
                  if (s.kind === 'global') return 'global';
                  if (s.kind === 'per-class') return 'per-class · ' + s.classes.join(', ');
                  return 'class · ' + s.classes.join(', ');
                }
                function floorLine(s) {
                  if (!s.floor && s.floors && s.floors.length > 1) {
                    const groups = new Map();
                    for (const f of s.floors) {
                      const g = groups.get(f.fragment) || { fragment: f.fragment, length: f.length, minion: [], other: [] };
                      g.minion.push(f.foreignMinion);
                      g.other.push(f.foreignOther);
                      groups.set(f.fragment, g);
                    }
                    const text = [...groups.values()].slice(0, 4).map(g => {
                      const minion = Math.min(...g.minion);
                      const otherMin = Math.min(...g.other);
                      const otherMax = Math.max(...g.other);
                      let bits = `<code>${esc(g.fragment)}</code> <span class="badge len">${g.length}</span>`;
                      if (minion) bits += ` <span class="badge">min ${minion}</span>`;
                      if (otherMax) bits += ` <span class="badge foreign">other ${otherMin === otherMax ? otherMin : otherMin + '–' + otherMax}</span>`;
                      return bits;
                    }).join(' · ');
                    return `<div class="floor">floor varies by class: ${text} <span class="diag">diagnostic only</span></div>`;
                  }
                  if (!s.floor) return '<div class="floor">floor: <span class="empty">no target-matching candidate</span></div>';
                  const safe = s.strict[0] || s.minion[0];
                  const label = s.strict.length ? 'Strict' : s.minion.length ? 'Minion-only' : '';
                  const delta = safe ? `+${safe.length - s.floor.length}` : '';
                  const best = safe
                    ? ` · best safe <code>${esc(safe.quoted || safe.fragment)}</code> ${safe.quotedLength ?? (safe.length + 2)} via ${label} (${delta})`
                    : ' · <span class="empty">no safe candidate</span>';
                  return `<div class="floor">floor: ${opt(s.floor)}${best} <span class="diag">diagnostic only</span></div>`;
                }
                function chosenLine(s) {
                  if (!s.chosen) return '';
                  return `<div class="chosen">chosen: ${opt(s.chosen)} <span class="badge">${esc(s.coverage)}</span> <span class="badge">save ${s.save}</span></div>`;
                }
                function knownLine(s) {
                  if (!s.known || !s.known.length) return '';
                  return `<div class="known">known reference: ${s.known.map(o => opt(o) + ' <span class="badge">verified in-game</span>').join(' · ')}</div>`;
                }
                function render() {
                  const needle = q.value.trim().toLowerCase();
                  const scope = scopeSel.value;
                  let shown = 0;
                  const html = window.MATRIX.families.map(f => {
                    const familyText = [f.key, f.display, f.classes.join(' '), ...f.patterns.map(p => p.pattern)].join(' ').toLowerCase();
                    if (needle && !familyText.includes(needle)) return '';
                    const patterns = f.patterns.map(p => {
                      const scopes = p.scopes.filter(s => !scope || s.kind === scope);
                      if (!scopes.length) return '';
                      return `<div class="pattern"><h4>${esc(p.display)} <span style="color:var(--muted)">(${esc(p.pattern)})</span></h4>` +
                        scopes.map(s => `<div class="scope"><h5>${esc(scopeTitle(s))} <span class="targets">· ${s.targets === s.targetMax ? s.targets : s.targets + '–' + s.targetMax} target lines${s.kind === 'global' ? '' : ' · ' + s.classes.length + (s.classes.length === 1 ? ' class' : ' classes')} · coverage ${esc(s.coverage)} · bound ${s.bound ?? 10}</span></h5>` +
                          `<div class="current">current: ${s.currentOption ? opt(s.currentOption) : (s.current ? `<code>"${esc(s.current)}"</code>` : '<span class="empty">unresolved</span>')}</div>` +
                          knownLine(s) +
                          chosenLine(s) +
                          floorLine(s) +
                          `<div class="profiles">${profile('Strict', s.strict)}${profile('Minion-only', s.minion)}</div></div>`).join('') +
                        `</div>`;
                    }).join('');
                    if (!patterns) return '';
                    shown++;
                    return `<details class="family"><summary>${esc(f.display)} <span class="meta">${esc(f.key)} · ${esc(f.prefix)} · ${f.tiers} tiers · ${f.classes.length} classes</span></summary><div class="patterns">${patterns}</div></details>`;
                  }).join('');
                  root.innerHTML = html || '<p class="empty">no families match</p>';
                  count.textContent = `${shown} / ${window.MATRIX.families.length} families`;
                }
                q.addEventListener('input', render);
                scopeSel.addEventListener('change', render);
                render();
            </script>
            """);
        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static FragmentTable? LoadTable(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<FragmentTable>(
                File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException) { return null; }
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var dict = new Dictionary<string, string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--")) continue;
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--")) { dict[args[i]] = args[i + 1]; i++; }
            else dict[args[i]] = "1";
        }
        return dict;
    }

    private static string GetValue(Dictionary<string, string> opts, string key, string fallback) =>
        opts.TryGetValue(key, out var value) ? value : fallback;
}

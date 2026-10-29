using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using PoeRegex.Core.Compilation;
using PoeRegex.Core.Data;

namespace PoeRegex.Data.Build;

/// <summary>
/// Generates the reviewed matrix artifact from the exact solver. It is a
/// data-build artifact only; the runtime does not consume it.
/// </summary>
public static class CandidateMatrix
{
    private sealed record MatrixScope(
        string Kind,
        IReadOnlyList<string> Classes,
        string Current,
        MatrixOption? CurrentOption,
        MatrixOption? Chosen,
        string Coverage,
        int Targets,
        int Bound);

    private sealed record MatrixPattern(
        string Pattern,
        string Display,
        IReadOnlyList<MatrixScope> Scopes);

    private sealed record MatrixFamily(
        string Key,
        string Prefix,
        string Display,
        int Tiers,
        IReadOnlyList<string> Classes,
        IReadOnlyList<MatrixPattern> Patterns);

    private sealed record WorkItem(
        string? Scope,
        string Family,
        string Pattern,
        MatrixScope Result);

    public static int Run(string[] args)
    {
        var opts = ParseOptions(args);
        var catalogPath = GetValue(opts, "--catalog", "data/catalogs/poe2.catalog.json");
        var tablePath = GetValue(opts, "--table", "data/catalogs/poe2.fragments.json");
        var outPath = GetValue(opts, "--out", "docs/CANDIDATE-MATRIX-poe2.html");
        var only = GetValue(opts, "--family", "");
        var jobs = int.TryParse(GetValue(opts, "--jobs", "0"), out var requestedJobs) &&
                   requestedJobs > 0
            ? requestedJobs
            : Environment.ProcessorCount;

        var catalog = new FileCatalogSource(catalogPath).Load();
        var corpus = new EngineCorpus(catalog);
        var loadedTable = LoadTable(tablePath);
        var fromScratch = opts.ContainsKey("--from-scratch") ||
                          loadedTable is null ||
                          !string.Equals(loadedTable.DataVersion, catalog.DataVersion, StringComparison.Ordinal);
        var table = fromScratch ? new FragmentTable() : loadedTable!;

        var familyKeys = only.Length > 0
            ? only.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : MatrixModel.AllFamilies(catalog).ToArray();
        var modsByFamily = catalog.Mods
            .GroupBy(MatrixModel.FamilyKey, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        familyKeys = familyKeys
            .Where(key => modsByFamily.TryGetValue(key, out var mods) &&
                          mods.Any(m => m.Classes.Count == 0 ||
                                        m.Classes.Any(corpus.IsInGameClass)))
            .ToArray();
        var applicableClasses = familyKeys
            .SelectMany(key => modsByFamily[key])
            .SelectMany(m => m.Classes)
            .Where(corpus.IsInGameClass)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        var scopes = new List<string?> { null };
        scopes.AddRange(applicableClasses);
        var scopeCorpora = scopes
            .Select(scope => (Scope: scope, Corpus: ScopeCorpus.Build(corpus, scope)))
            .ToList();

        var tasks = new List<(string? Scope, ScopeCorpus Corpus, string Family)>();
        foreach (var (scope, scopeCorpus) in scopeCorpora)
        foreach (var key in familyKeys)
        {
            if (!modsByFamily.TryGetValue(key, out var mods)) continue;
            if (scope is not null &&
                !mods.Any(m => m.Classes.Contains(scope, StringComparer.Ordinal))) continue;
            tasks.Add((scope, scopeCorpus, key));
        }

        var results = new ConcurrentBag<WorkItem>();
        Parallel.ForEach(tasks, new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, jobs),
        }, task =>
        {
            var (scope, scopeCorpus, key) = task;
            var patterns = modsByFamily[key]
                .Where(m => scope is null || m.Classes.Contains(scope, StringComparer.Ordinal))
                .SelectMany(m => m.StatLines.Select(sl => sl.Pattern))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(x => x, StringComparer.Ordinal);

            foreach (var pattern in patterns)
            {
                if (!scopeCorpus.HasPattern(pattern)) continue;
                var targets = scopeCorpus.Targets(pattern);
                if (targets.Count == 0) continue;
                var current = MatrixModel.CurrentFragment(table, scope, pattern);
                var currentOption = string.IsNullOrEmpty(current)
                    ? null
                    : ExactOracle.Check(scopeCorpus, pattern, current);
                var minimum = scope is null ? 3 : 2;
                var maximum = currentOption is not null ? current.Length - 1 : 250;
                var solved = ExactOracle.Solve(scopeCorpus, pattern, minimum, maximum);
                var chosen = solved ?? currentOption;
                var coverage = chosen is null
                    ? "none"
                    : solved is null ? "current-only" : "strict";
                results.Add(new WorkItem(scope, key, pattern, new MatrixScope(
                    scope is null ? "global" : "class",
                    scope is null ? [] : [scope],
                    current,
                    currentOption,
                    chosen,
                    coverage,
                    targets.Count,
                    250)));
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
                            Classes = g.Select(x => x.Scope!)
                                .Distinct(StringComparer.Ordinal)
                                .OrderBy(x => x, StringComparer.Ordinal)
                                .ToList(),
                            Targets = g.Min(x => x.Result.Targets),
                        },
                    })
                    .OrderBy(x => x.Classes[0], StringComparer.Ordinal)
                    .ToList();

                if (classRows.Count == 1)
                {
                    scopesOut.Add(classRows[0].Result with { Kind = "per-class" });
                }
                else
                {
                    scopesOut.AddRange(classRows.Select(x => x.Result));
                }

                patterns.Add(new MatrixPattern(
                    patternGroup.Key.Replace('\u0001', '#'),
                    patternGroup.Key.Replace('\u0001', ' ').Trim(),
                    scopesOut));
            }

            families.Add(new MatrixFamily(
                key,
                mods.Select(m => m.GenerationType).FirstOrDefault() ?? "",
                patterns[0].Display,
                mods.Select(m => m.Name ?? "").Distinct(StringComparer.Ordinal).Count(),
                mods.SelectMany(m => m.Classes)
                    .Where(corpus.IsInGameClass)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(x => x, StringComparer.Ordinal)
                    .ToList(),
                patterns));
        }

        var data = new
        {
            Game = "poe2",
            Generated = DateTimeOffset.UtcNow.ToString("u"),
            Families = families,
        };
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        }).Replace("</script", "<\\/script", StringComparison.OrdinalIgnoreCase);

        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        File.WriteAllText(outPath, Html(json, families.Count, familyKeys.Length));
        Console.WriteLine($"wrote {outPath} ({new FileInfo(outPath).Length / 1024} KiB, {families.Count} families)");
        return 0;
    }

    private static string Signature(MatrixScope scope) =>
        string.Join("|",
            scope.Current,
            scope.CurrentOption?.Fragment,
            scope.Chosen?.Fragment,
            scope.Coverage);

    internal static string Html(string json, int familyCount, int requestedCount)
    {
        const string css = """
            :root{--bg:#101418;--panel:#171d24;--line:#2d3742;--text:#d8dee8;--muted:#8995a5;--gold:#d9a441;--good:#6fcf97;--accent:#8fb8ff}
            *{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--text);font:14px/1.45 system-ui,Segoe UI,sans-serif}
            header{padding:20px 24px;border-bottom:1px solid var(--line);background:#141a20}
            h1{margin:0 0 6px;color:var(--gold);font-size:21px}.sub{color:var(--muted);max-width:1100px}
            .toolbar{position:sticky;top:0;z-index:2;display:flex;gap:10px;padding:10px 24px;background:#12181e;border-bottom:1px solid var(--line)}
            input{background:#0d1216;color:var(--text);border:1px solid var(--line);border-radius:4px;padding:8px 10px;min-width:260px;flex:1;max-width:620px}
            .count{color:var(--muted);margin-left:auto;align-self:center}main{padding:16px 24px 70px;max-width:1300px;margin:0 auto}
            details{border:1px solid var(--line);border-radius:6px;margin:8px 0;background:var(--panel);overflow:hidden}
            summary{cursor:pointer;padding:10px 12px;font-weight:600;color:var(--gold);list-style:none}
            summary::-webkit-details-marker{display:none}.patterns{padding:0 12px 12px}
            .pattern{border-top:1px solid var(--line);padding:10px 0}.pattern h3{margin:0 0 6px;font-size:13px}
            code{color:var(--good);background:#0d1216;padding:2px 5px;border-radius:3px}
            .scope{margin:8px 0}.scope h4{margin:0 0 4px;color:var(--muted);font-size:12px;text-transform:uppercase;letter-spacing:.06em}
            .row{color:var(--muted)}.row b{color:var(--accent)}
            .badge{font-size:11px;color:var(--muted);margin-left:6px}
            .empty{color:var(--muted);font-style:italic}
            @media(max-width:700px){header,.toolbar,main{padding-left:12px;padding-right:12px}}
            """;
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>Exact candidate matrix</title><style>").Append(css).Append("</style></head><body>");
        sb.Append("<header><h1>Exact candidate matrix</h1><div class=\"sub\">")
          .Append(familyCount).Append(" families loaded from ").Append(requestedCount)
          .Append(" requested. Every scope is solved with the exact G1 solver; the chosen form is the shortest proven form. This is a data-build artifact.</div></header>");
        sb.Append("<div class=\"toolbar\"><input id=\"q\" placeholder=\"filter family, stat text, or class…\" autofocus><span class=\"count\" id=\"count\"></span></div>");
        sb.Append("<main id=\"out\"></main><script>window.MATRIX = ").Append(json).Append(";</script>");
        sb.Append("""
            <script>
            const root=document.getElementById('out'),q=document.getElementById('q'),count=document.getElementById('count');
            const esc=s=>String(s).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
            function opt(o){if(!o||!o.fragment)return '<span class="empty">none</span>';return `<code>${esc(o.quoted||('"'+o.fragment+'"'))}</code><span class="badge">${o.quotedLength??(o.length+2)} quoted · inner ${o.length}</span>`+(o.foreignLines?`<span class="badge">foreign ${o.foreignLines}</span>`:'');}
            function render(){const n=q.value.trim().toLowerCase();let shown=0;const html=window.MATRIX.families.map(f=>{const text=[f.key,f.display,f.classes.join(' '),...f.patterns.map(p=>p.pattern)].join(' ').toLowerCase();if(n&&!text.includes(n))return '';const patterns=f.patterns.map(p=>`<div class="pattern"><h3>${esc(p.display)} <span style="color:var(--muted)">(${esc(p.pattern)})</span></h3>`+p.scopes.map(s=>`<div class="scope"><h4>${esc(s.kind)}${s.classes.length?' · '+esc(s.classes.join(', ')):''} <span class="badge">${s.targets} targets · coverage ${esc(s.coverage)}</span></h4><div class="row"><b>current</b> ${s.currentOption?opt(s.currentOption):(s.current?`<code>${esc(s.current)}</code>`:'<span class="empty">unresolved</span>')}</div><div class="row"><b>chosen</b> ${opt(s.chosen)}</div></div>`).join('')+'</div>').join('');if(!patterns)return '';shown++;return `<details><summary>${esc(f.display)} <span style="color:var(--muted);font-weight:400">${esc(f.key)} · ${esc(f.prefix)} · ${f.tiers} tiers · ${f.classes.length} classes</span></summary><div class="patterns">${patterns}</div></details>`;}).join('');root.innerHTML=html||'<p class="empty">no families match</p>';count.textContent=`${shown} / ${window.MATRIX.families.length} families`;}
            q.addEventListener('input',render);render();
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

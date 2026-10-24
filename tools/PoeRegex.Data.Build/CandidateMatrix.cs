using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using PoeRegex.Core;
using PoeRegex.Core.Compilation;
using PoeRegex.Core.Data;

namespace PoeRegex.Data.Build;

/// <summary>
/// Generates a self-contained, searchable HTML artifact for comparing
/// candidate fragments under several explicit safety/scope profiles. It is a
/// data-build artifact only; the web runtime does not consume it.
/// </summary>
public static class CandidateMatrix
{
    private sealed record MatrixOption(
        string Fragment, int Length, int ForeignMinion, int ForeignOther,
        bool CrossSynthetic, bool CrossCaptured, bool Wildcard, bool Anchored);

    private sealed record MatrixScope(
        string Kind, string? ClassId, string Current,
        IReadOnlyList<MatrixOption> Strict,
        IReadOnlyList<MatrixOption> Minion,
        IReadOnlyList<MatrixOption> Line,
        IReadOnlyList<MatrixOption> Relaxed,
        IReadOnlyList<MatrixOption> Shortest);

    private sealed record MatrixPattern(
        string Pattern, string Display, IReadOnlyList<MatrixScope> Scopes);

    private sealed record MatrixFamily(
        string Key, string Prefix, string Display, int Tiers,
        IReadOnlyList<string> Classes, IReadOnlyList<MatrixPattern> Patterns);

    public static int Run(string[] args)
    {
        var opts = ParseOptions(args);
        var gameName = GetValue(opts, "--game", "poe2");
        var game = gameName == "poe1" ? GameTarget.PoE1 : GameTarget.PoE2;
        var catalogPath = GetValue(opts, "--catalog", $"data/catalogs/{gameName}.catalog.json");
        var outPath = GetValue(opts, "--out", $"docs/CANDIDATE-MATRIX-{gameName}.html");
        var only = GetValue(opts, "--family", "");
        var maxLength = int.TryParse(GetValue(opts, "--max-length", "24"), out var ml) ? ml : 24;

        var catalog = new FileCatalogSource(catalogPath).Load(game);
        var corpus = new EngineCorpus(catalog);
        var table = LoadTable($"data/catalogs/{gameName}.fragments.json");
        var familyKeys = only.Length > 0
            ? only.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : CandidateReport.AllFamilies(catalog).ToArray();

        // Materialize the shared corpus/indexes once, then let families run in
        // parallel. This keeps the artifact build bounded without changing the
        // runtime data path.
        _ = corpus.RealLinesGlobal();
        foreach (var itemClass in catalog.ItemClasses)
        {
            _ = corpus.RealLinesForClass(itemClass.Id);
            _ = corpus.SearchSectionsForClass(itemClass.Id);
        }
        _ = corpus.SafetyLines(null).ToList();

        var familiesBag = new ConcurrentBag<MatrixFamily>();
        Parallel.ForEach(familyKeys, new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1),
        }, key =>
        {
            var mods = catalog.Mods
                .Where(m => string.Equals(m.Family.Length > 0 ? m.Family : m.Id, key, StringComparison.Ordinal))
                .ToList();
            if (mods.Count == 0) return;
            var cls = CandidateReport.RepresentativeClasses.GetValueOrDefault(key,
                CandidateReport.FirstClassFor(catalog, key));
            if (cls.Length == 0) return;
            var patterns = CandidateReport.TargetPatterns(catalog, key, null)
                .OrderBy(x => x, StringComparer.Ordinal).ToList();
            if (patterns.Count == 0) return;

            var matrixPatterns = new List<MatrixPattern>();
            foreach (var pattern in patterns)
            {
                var scopes = new List<MatrixScope>();
                foreach (var (kind, scope) in new (string, string?)[]
                         { ("global", null), ("class", cls) })
                {
                    var all = CandidateReport.ScopeLines(corpus, scope);
                    var targets = all.Where(x => x.Pattern == pattern)
                        .Select(x => x.Real).Distinct(StringComparer.Ordinal).ToList();
                    if (targets.Count == 0) continue;
                    var foreign = all.Where(x => x.Pattern != pattern)
                        .Select(x => new CandidateReport.SourceLine(x.Pattern, x.Real)).ToList();
                    var current = table is null
                        ? ""
                        : (scope is null
                            ? table.Global.GetValueOrDefault(pattern) ?? ""
                            : table.ByClass.GetValueOrDefault(scope)?.GetValueOrDefault(pattern) ?? "");
                    var options = CandidateReport.Generate(corpus, targets, foreign, scope, maxLength,
                        current.Length > 0 ? current : null,
                        fast: !CandidateReport.CommonFamilies.Contains(key));
                    var plausible = options.Where(o => o.Plausible)
                        .OrderBy(o => o.Length).ThenBy(o => o.Fragment, StringComparer.Ordinal)
                        .Take(40).ToList();
                    foreach (var option in plausible) CandidateReport.EvaluateCross(corpus, scope, option);

                    MatrixOption ToOption(CandidateReport.Option o) => new(
                        o.Fragment, o.Length, o.ForeignMinionLines, o.ForeignOtherLines,
                        o.CrossSynthetic, o.CrossCaptured, o.HasWildcard,
                        o.Fragment.StartsWith('^') || o.Fragment.EndsWith('$'));

                    List<MatrixOption> Pick(Func<CandidateReport.Option, bool> predicate) =>
                        plausible.Where(predicate).Take(3).Select(ToOption).ToList();

                    scopes.Add(new MatrixScope(kind, scope, current,
                        Pick(o => o.ForeignLines == 0 && !o.CrossSynthetic && !o.CrossCaptured),
                        Pick(o => o.ForeignOtherLines == 0 && !o.CrossSynthetic && !o.CrossCaptured),
                        Pick(o => o.ForeignLines == 0),
                        Pick(o => o.ForeignOtherLines == 0),
                        Pick(_ => true)));
                }
                matrixPatterns.Add(new MatrixPattern(
                    pattern.Replace('\u0001', '#'),
                    pattern.Replace('\u0001', ' ').Trim(),
                    scopes));
            }
            if (matrixPatterns.Count == 0) return;
            familiesBag.Add(new MatrixFamily(
                key,
                mods.Select(m => m.GenerationType).FirstOrDefault() ?? "",
                patterns.Count > 0
                    ? patterns[0].Replace('\u0001', ' ').Trim()
                    : key,
                mods.Select(m => m.Name ?? "").Distinct(StringComparer.Ordinal).Count(),
                mods.SelectMany(m => m.Classes).Distinct(StringComparer.Ordinal).OrderBy(x => x).ToList(),
                matrixPatterns));
        });

        var families = familiesBag
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .ToList();

        var data = new
        {
            Game = gameName,
            Generated = DateTimeOffset.UtcNow.ToString("u"),
            Families = families,
            Profiles = new[]
            {
                new { Id = "strict", Label = "Strict", Description = "zero foreign lines; no cross-line matches" },
                new { Id = "minion", Label = "Minion-only", Description = "minion/allies/presence lines allowed; no cross-line" },
                new { Id = "line", Label = "Line-only", Description = "zero foreign lines; cross-line matches allowed" },
                new { Id = "relaxed", Label = "Relaxed", Description = "minion/allies allowed; cross-line allowed" },
                new { Id = "shortest", Label = "Shortest observed", Description = "no collision filter; diagnostic only" },
            },
        };

        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        }).Replace("</script", "<\\/script", StringComparison.OrdinalIgnoreCase);

        File.WriteAllText(outPath, Html(json, families.Count, familyKeys.Length));
        Console.WriteLine($"wrote {outPath} ({new FileInfo(outPath).Length / 1024} KiB, {families.Count} families)");
        return 0;
    }

    private static string Html(string json, int familyCount, int requestedCount)
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
            .current { margin-bottom:8px; color:var(--muted); }
            .current code { color:var(--good); background:#0d1216; padding:2px 5px; border-radius:3px; }
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
            .empty { color:var(--muted); font-style:italic; }
            @media (max-width:700px) { header,.toolbar,main { padding-left:14px; padding-right:14px; } .sub { font-size:13px; } }
            """;
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>Candidate matrix</title><style>").Append(css).Append("</style></head><body>");
        sb.Append("<header><h1>Candidate matrix</h1><div class=\"sub\">Compare fragment choices for every catalog family. ").Append(familyCount).Append(" families loaded from ").Append(requestedCount).Append(" requested. This is a data-build artifact; it does not change the runtime table.</div>");
        sb.Append("<div class=\"defs\">");
        sb.Append("<div class=\"def\"><b>Strict</b>Zero foreign lines; no cross-line wildcard match.</div>");
        sb.Append("<div class=\"def\"><b>Minion-only</b>Minion / allies / presence lines allowed; no cross-line.</div>");
        sb.Append("<div class=\"def\"><b>Line-only</b>Zero foreign lines; cross-line wildcard allowed.</div>");
        sb.Append("<div class=\"def\"><b>Relaxed</b>Minion/allies lines and cross-line allowed.</div>");
        sb.Append("<div class=\"def\"><b>Shortest</b>No collision filter; diagnostic only.</div>");
        sb.Append("</div></header>");
        sb.Append("<div class=\"toolbar\"><input id=\"q\" placeholder=\"filter family or stat text…\" autofocus><select id=\"scope\"><option value=\"\">all scopes</option><option value=\"global\">global</option><option value=\"class\">class</option></select><span class=\"count\" id=\"count\"></span></div>");
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
              const bits = [`<code>${esc(o.fragment)}</code>`, `<span class="badge len">${o.length}</span>`];
              if (o.foreignMinion) bits.push(`<span class="badge">min ${o.foreignMinion}</span>`);
              if (o.foreignOther) bits.push(`<span class="badge foreign">other ${o.foreignOther}</span>`);
              if (o.crossSynthetic) bits.push('<span class="badge cross">cross-syn</span>');
              if (o.crossCaptured) bits.push('<span class="badge cross">cross-cap</span>');
              return bits.join(' ');
            }
            function profile(id, label, list) {
              return `<div class="profile"><h6>${label}</h6>${(list && list.length ? list.map(opt).map(x=>`<div class="opt">${x}</div>`).join('') : '<span class="empty">none</span>')}</div>`;
            }
            function render() {
              const needle = q.value.trim().toLowerCase();
              const scope = scopeSel.value;
              let shown = 0;
              const html = window.MATRIX.families.map(f => {
                const familyText = [f.key, f.display, ...f.patterns.map(p => p.pattern)].join(' ').toLowerCase();
                if (needle && !familyText.includes(needle)) return '';
                const patterns = f.patterns.map(p => {
                  const scopes = p.scopes.filter(s => !scope || s.kind === scope);
                  if (!scopes.length) return '';
                  return `<div class="pattern"><h4>${esc(p.display)} <span style="color:var(--muted)">(${esc(p.pattern)})</span></h4>` +
                    scopes.map(s => `<div class="scope"><h5>${esc(s.kind)}${s.classId ? ' · '+esc(s.classId) : ''}</h5>` +
                      `<div class="current">current: ${s.current ? `<code>${esc(s.current)}</code>` : '<span class="empty">unresolved</span>'}</div>` +
                      `<div class="profiles">${profile('strict','Strict',s.strict)}${profile('minion','Minion-only',s.minion)}${profile('line','Line-only',s.line)}${profile('relaxed','Relaxed',s.relaxed)}${profile('shortest','Shortest',s.shortest)}</div></div>`).join('') +
                    `</div>`;
                }).join('');
                if (!patterns) return '';
                shown++;
                return `<details class="family"><summary>${esc(f.display)} <span class="meta">${esc(f.key)} · ${esc(f.prefix)} · ${f.tiers} tiers · ${esc(f.classes.join(', '))}</span></summary><div class="patterns">${patterns}</div></details>`;
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

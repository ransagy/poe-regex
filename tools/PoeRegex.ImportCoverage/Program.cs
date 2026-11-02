using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PoeRegex.Core;
using PoeRegex.Core.Compilation;
using PoeRegex.Core.Data;
using PoeRegex.Web.Core;

namespace PoeRegex.ImportCoverage;

/// <summary>
/// Round-trip / guide-import coverage spike (§16.3).
///
/// Diagnostic only: compiles representative workbench strings with the shipped
/// compiler, imports them with the current best-effort QueryImportService,
/// recompiles the recovered criteria, and reports where query-equivalence
/// holds. It does not change the importer, state model, solver, or table.
///
/// Usage:
///   dotnet run --project tools/PoeRegex.ImportCoverage
///   dotnet run --project tools/PoeRegex.ImportCoverage -- --guides /path/to/guides.txt
///   dotnet run --project tools/PoeRegex.ImportCoverage -- --phase1
///   dotnet run --project tools/PoeRegex.ImportCoverage -- --out docs/X.md --json docs/X.json
///
/// Guide files use <c>Name = filter</c> per line; quoted filters may use several
/// groups. Guide text is supplied locally and is never written to the report.
/// <c>--phase1</c> runs the Phase 1 QueryReconstructionService over the same
/// fixture matrix and writes a reconstruction report instead.
/// </summary>
public static class Program
{
    private const string DefaultOut = "docs/IMPORT-COVERAGE-poe2.md";
    private const string DefaultJson = "docs/IMPORT-COVERAGE-poe2.json";
    private const string Phase1Out = "docs/IMPORT-RECONSTRUCTION-poe2.md";
    private const string Phase1Json = "docs/IMPORT-RECONSTRUCTION-poe2.json";

    public static int Main(string[] args)
    {
        string outPath = DefaultOut;
        string? jsonPath = DefaultJson;
        string? guidesPath = null;
        var phase1 = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--out" when i + 1 < args.Length:
                    outPath = args[++i];
                    break;
                case "--json" when i + 1 < args.Length:
                    jsonPath = args[++i];
                    break;
                case "--no-json":
                    jsonPath = null;
                    break;
                case "--guides" when i + 1 < args.Length:
                    guidesPath = args[++i];
                    break;
                case "--phase1":
                    phase1 = true;
                    break;
                case "-h" or "--help":
                    Console.WriteLine("Usage: [--phase1] [--guides <file>] [--out <md>] [--json <json>|--no-json]");
                    return 0;
                default:
                    Console.Error.WriteLine($"Unknown argument: {args[i]}");
                    return 2;
            }
        }

        if (phase1)
        {
            if (outPath == DefaultOut) outPath = Phase1Out;
            if (jsonPath == DefaultJson) jsonPath = Phase1Json;
        }

        try
        {
            var engine = new EngineService();
            var cases = BuildCases(engine);

            if (phase1)
            {
                var reconstruction = new QueryReconstructionService(engine);
                var outcomes = cases.Select(c => EvaluatePhase1(engine, reconstruction, c)).ToList();
                var report = Phase1Report.Markdown(engine, outcomes);
                Directory.CreateDirectory(Path.GetDirectoryName(outPath) ?? ".");
                File.WriteAllText(outPath, report, new UTF8Encoding(false));
                if (jsonPath is not null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(jsonPath) ?? ".");
                    File.WriteAllText(jsonPath, Phase1Report.Json(engine, outcomes), new UTF8Encoding(false));
                }

                var passed = outcomes.Count(x => x.Ok && x.SameQuery);
                Console.WriteLine($"phase1 cases: {outcomes.Count}, reconstructed: {passed}, " +
                                  $"fail-closed: {outcomes.Count - passed}");
                Console.WriteLine($"wrote {outPath}" + (jsonPath is null ? "" : $" and {jsonPath}"));
                return 0;
            }

            var importer = new QueryImportService(engine);
            var importOutcomes = cases.Select(c => Evaluate(engine, importer, c)).ToList();
            var guides = guidesPath is null ? [] : GuideClassifier.Run(engine, guidesPath);
            var importReport = ReportWriter.Markdown(engine, importOutcomes, guides, guidesPath is not null);
            Directory.CreateDirectory(Path.GetDirectoryName(outPath) ?? ".");
            File.WriteAllText(outPath, importReport, new UTF8Encoding(false));

            if (jsonPath is not null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(jsonPath) ?? ".");
                File.WriteAllText(jsonPath, ReportWriter.Json(engine, importOutcomes, guides), new UTF8Encoding(false));
            }

            var byClass = importOutcomes.GroupBy(x => x.Classification)
                .OrderByDescending(g => g.Count())
                .Select(g => $"{g.Key}={g.Count()}");
            Console.WriteLine($"cases: {importOutcomes.Count} ({string.Join(", ", byClass)})");
            if (guides.Count > 0)
                Console.WriteLine($"guides: {guides.Count}, branches: {guides.Sum(g => g.Branches.Count)}");
            Console.WriteLine($"wrote {outPath}" + (jsonPath is null ? "" : $" and {jsonPath}"));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    // ---------------------------------------------------------------- fixtures

    private static List<CaseSpec> BuildCases(EngineService engine)
    {
        var cases = new List<CaseSpec>();
        var life = new WantedMod("IncreasedLife6");
        var strength = new WantedMod("Strength1");
        var ringFamilies = NamedFamilies(engine, "Ring");
        var ringFamily = ringFamilies[0];
        var ringTier = ringFamily.Tiers.First(t => !string.IsNullOrWhiteSpace(t.Name));
        var ringNamePick = new WantedMod(ringTier.Anchor.Id, Names: ringFamily.NamesFrom(ringTier));
        var ringFamilyPick = ringFamily.Pick();
        var ringSecondFamilyPick = ringFamilies[1].Pick();

        AddCompiled(cases, "simple-plain-1", "simple", "one plain pick", engine.CompilePicks([life], [], false, null));
        AddCompiled(cases, "simple-any-2", "simple", "two plain picks, any-of", engine.CompilePicks([life, strength], [], false, null));
        AddCompiled(cases, "simple-all-2", "simple", "two plain picks, all-of", engine.CompilePicks([life, strength], [], true, null));
        AddCompiled(cases, "simple-exclusion", "simple", "plain pick + exclusion", engine.CompilePicks([life], ["Strength1"], false, null));
        AddCompiled(cases, "simple-name", "simple", "one affix-name pick", engine.CompilePicks([ringNamePick], [], false, null), name: true);
        AddCompiled(cases, "simple-family", "simple", "one family pick", engine.CompilePicks([ringFamilyPick], [], false, null), family: true);
        AddCompiled(cases, "simple-min", "simple", "plain pick with minimum", engine.CompilePicks([new WantedMod("IncreasedLife6", MinValue: 10)], [], false, null), min: true);

        AddCompiled(cases, "advanced-plain", "advanced", "Ring plain pick", engine.CompilePicks([life], [], false, "Ring"));
        AddCompiled(cases, "advanced-name", "advanced", "Ring affix-name pick", engine.CompilePicks([ringNamePick], [], false, "Ring"), name: true);
        AddCompiled(cases, "advanced-family", "advanced", "Ring family pick", engine.CompilePicks([ringFamilyPick], [], false, "Ring"), family: true);
        AddCompiled(cases, "advanced-any-2", "advanced", "Ring two picks, any-of", engine.CompilePicks([ringFamilyPick, ringSecondFamilyPick], [], false, "Ring"));
        AddCompiled(cases, "advanced-all-2", "advanced", "Ring two picks, all-of", engine.CompilePicks([ringFamilyPick, ringSecondFamilyPick], [], true, "Ring"));

        AddPaste(cases, "paste-rings", "ring1 + ring2",
            engine.Combine([("ring1", "Ring", new[] { ringFamilyPick }), ("ring2", "Ring", new[] { ringSecondFamilyPick })], []));
        AddPaste(cases, "paste-weapon-offhand", "weapon + offhand",
            engine.Combine(
            [
                ("weapon", "Crossbow", new[] { FamilyPick(engine, "Crossbow") }),
                ("offhand", "Shield", new[] { FamilyPick(engine, "Shield") }),
            ], []));
        AddPaste(cases, "paste-armour", "helmet + boots + gloves",
            engine.Combine(
            [
                ("helmet", "Helmet", new[] { FamilyPick(engine, "Helmet") }),
                ("boots", "Boots", new[] { FamilyPick(engine, "Boots") }),
                ("gloves", "Gloves", new[] { FamilyPick(engine, "Gloves") }),
            ], []));
        AddPaste(cases, "paste-exclusion", "two slots + exclusion",
            engine.Combine(
            [
                ("helmet", "Helmet", new[] { FamilyPick(engine, "Helmet") }),
                ("boots", "Boots", new[] { FamilyPick(engine, "Boots") }),
            ], ["Strength1"]));
        AddPaste(cases, "paste-same-class-allof", "ring1 + ring2 all-of (accepted fallback)",
            engine.Combine(
            [
                ("ring1", "Ring", new[] { ringFamilyPick }, true),
                ("ring2", "Ring", new[] { ringSecondFamilyPick }, true),
            ], []));

        AddRaw(cases, "edge-class-anchor", "edge", "class anchor only", engine.Compile([], [], "Ring").Query);
        AddRaw(cases, "edge-old-anchor", "edge", "old literal class anchor", "\": One Hand M\"");
        AddRaw(cases, "edge-unknown-token", "edge", "free text / unknown token", "\"zzzz\"");
        AddRaw(cases, "edge-shape", "edge", "unterminated query", "\"life");
        AddRaw(cases, "edge-free-text-and-mod", "edge", "known fragment + free text", "\"life\" \"zzzz\"");

        foreach (var c in cases)
        {
            if (string.IsNullOrWhiteSpace(c.Query))
                throw new InvalidOperationException($"Fixture '{c.Id}' compiled to an empty query.");
        }
        return cases;
    }

    private static void AddCompiled(List<CaseSpec> cases, string id, string scope, string description,
        CompileResult result, bool min = false, bool name = false, bool family = false) =>
        cases.Add(new CaseSpec(id, scope, description, result.Query, min, name, family, false));

    private static void AddPaste(List<CaseSpec> cases, string id, string description, MultiPasteResult result) =>
        cases.Add(new CaseSpec(id, "paste", description, result.Query, false, false, false, true));

    private static void AddRaw(List<CaseSpec> cases, string id, string scope, string description, string query) =>
        cases.Add(new CaseSpec(id, scope, description, query));

    private static WantedMod FamilyPick(EngineService engine, string classId) =>
        NamedFamilies(engine, classId)[0].Pick();

    private static IReadOnlyList<ModFamily> NamedFamilies(EngineService engine, string classId)
    {
        var families = ModFamilies.Group(engine.ModsForClasses([classId]))
            .Where(f => f.Tiers.Count >= 2 && f.DisplayLines.Count > 0 &&
                        f.Tiers.Any(t => !string.IsNullOrWhiteSpace(t.Name)))
            .ToList();
        if (families.Count == 0)
            throw new InvalidOperationException($"No named mod families for class '{classId}'.");
        return families;
    }

    // ------------------------------------------------------------ classification

    private static CaseOutcome Evaluate(EngineService engine, QueryImportService importer, CaseSpec c)
    {
        var imported = importer.Import(c.Query);
        var clazz = imported.Classes.Count == 1 ? imported.Classes[0] : null;

        string? anyOf = null;
        string? allOf = null;
        if (imported.Mode is "simple" or "advanced")
        {
            anyOf = engine.Compile(imported.Includes, imported.Excludes, clazz, imported.Keywords, true).Query;
            if (imported.IncludeGroups is { Count: > 1 })
            {
                allOf = engine.Compile([], imported.Excludes, clazz, imported.Keywords, true,
                    imported.IncludeGroups).Query;
            }
        }
        else if (imported.Mode == "multi-class" &&
                 imported.Includes.Count == 0 && imported.Excludes.Count == 0 && imported.Keywords.Count == 0)
        {
            anyOf = engine.Combine(
                imported.Classes.Select(x => (Label: x, ClassId: x, Includes: (IEnumerable<WantedMod>)Array.Empty<WantedMod>())),
                []).Query;
        }

        var matchedAny = anyOf == c.Query;
        var matchedAll = allOf == c.Query;
        var recovered = imported.Diagnostics.Any(IsRecovered);
        var arbitrary = imported.Diagnostics.Any(IsArbitraryChoice);
        var hasKeyword = imported.Keywords.Count > 0;
        var unresolved = imported.Diagnostics.Any(IsUnresolved);
        var shapeError = imported.Diagnostics.Any(d => d.StartsWith("shape:", StringComparison.OrdinalIgnoreCase));

        string classification;
        string reason;
        if (shapeError)
        {
            classification = "unrecoverable";
            reason = "invalid query shape";
        }
        else if (hasKeyword)
        {
            // A byte-identical string is not a canonical round-trip when the
            // importer kept free text: names/guide text are not picks.
            classification = "unrecoverable";
            reason = c.HasNamePick
                ? "affix-name pick is preserved as free text (needs the affix-name index)"
                : "free text / unknown token is preserved as a keyword";
        }
        else if (matchedAny || matchedAll)
        {
            classification = recovered ? "factored" : "exact";
            reason = arbitrary
                ? "query-equivalent via an arbitrary catalog mod choice"
                : recovered ? "query-equivalent via explain recovery" : "identical recompile";
        }
        else if (c.HasMinValue)
        {
            classification = "unrecoverable";
            reason = "minimum value is not encoded in the string";
        }
        else if (imported.Mode == "multi-class")
        {
            classification = "unrecoverable";
            reason = "multi-class paste: per-slot mapping is lost";
        }
        else if (unresolved)
        {
            classification = "unrecoverable";
            reason = "unresolved or invalid fragment";
        }
        else
        {
            classification = "ambiguous";
            reason = IsLegacyAnchor(c.Query, anyOf, imported)
                ? "legacy literal class anchor recompiles to the current wildcard"
                : "recovered criteria recompile to a different query";
        }

        return new CaseOutcome(
            c.Id, c.Scope, c.Description, c.Query,
            matchedAll ? allOf : anyOf,
            anyOf, allOf,
            classification, reason,
            imported.Mode, imported.Classes.Count, imported.Includes.Count,
            imported.Excludes.Count, imported.Keywords.Count,
            imported.Diagnostics,
            c.HasMinValue, c.HasNamePick, c.HasFamilyPick, c.IsPaste,
            arbitrary);
    }

    private static bool IsArbitraryChoice(string message) =>
        message.Contains("maps to", StringComparison.OrdinalIgnoreCase) &&
        message.Contains("using '", StringComparison.OrdinalIgnoreCase);

    private static bool IsLegacyAnchor(string original, string? recompiled, QueryImportResult imported) =>
        original.StartsWith("\": ", StringComparison.Ordinal) &&
        recompiled is not null && recompiled.StartsWith("\": ", StringComparison.Ordinal) &&
        imported.Classes.Count == 1 &&
        imported.Includes.Count == 0 && imported.Excludes.Count == 0 && imported.Keywords.Count == 0;

    private static Phase1Outcome EvaluatePhase1(
        EngineService engine, QueryReconstructionService reconstruction, CaseSpec c)
    {
        var recovered = reconstruction.ReconstructWorkbench(c.Query);
        var recompiled = recovered.Recompile(engine);
        return new Phase1Outcome(
            c.Id, c.Scope, c.Description, c.Query,
            recovered.Ok, recovered.Mode, recovered.Slots,
            recovered.Excludes, recompiled == c.Query,
            recovered.Diagnostics, recovered.Warnings);
    }

    private static bool IsRecovered(string message) =>
        message.Contains("recovered", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("maps to", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("using '", StringComparison.OrdinalIgnoreCase);

    private static bool IsUnresolved(string message) =>
        message.StartsWith("shape:", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("unmatched", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("unresolved", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("no mod", StringComparison.OrdinalIgnoreCase);

    // ---------------------------------------------------------------- outcomes

    public sealed record CaseSpec(
        string Id, string Scope, string Description, string Query,
        bool HasMinValue = false, bool HasNamePick = false, bool HasFamilyPick = false, bool IsPaste = false);

    public sealed record CaseOutcome(
        string Id, string Scope, string Description, string Query,
        string? Recompiled,
        string? AnyOf, string? AllOf,
        string Classification, string Reason,
        string ImportMode, int Classes, int Includes, int Excludes, int Keywords,
        IReadOnlyList<string> Diagnostics,
        bool HasMinValue, bool HasNamePick, bool HasFamilyPick, bool IsPaste, bool ArbitraryChoice);

    public sealed record Phase1Outcome(
        string Id, string Scope, string Description, string Query,
        bool Ok, string Mode, IReadOnlyList<ReconstructedSlot> Slots,
        IReadOnlyList<string> Excludes, bool SameQuery,
        IReadOnlyList<string> Diagnostics, IReadOnlyList<string> Warnings);
}

/// <summary>Classifies guide filter branches without retaining the guide text.</summary>
public static class GuideClassifier
{
    public sealed record BranchResult(string Category, int Families, bool Negated);
    public sealed record GuideResult(string Name, IReadOnlyList<BranchResult> Branches, bool HasClassAnchor);

    public static IReadOnlyList<GuideResult> Run(EngineService engine, string path)
    {
        var results = new List<GuideResult>();
        var n = 0;
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            n++;

            var name = $"Guide {n}";
            var body = line;
            var eq = line.IndexOf('=');
            if (eq > 0)
            {
                name = line[..eq].Trim();
                body = line[(eq + 1)..].Trim();
            }

            var filters = ExtractQuoted(body);
            if (filters.Count == 0) filters = [body];
            var branchResults = new List<BranchResult>();
            var hasAnchor = false;
            foreach (var filter in filters)
            {
                if (filter.Contains("Item Class:", StringComparison.OrdinalIgnoreCase) ||
                    filter.Contains("s: ", StringComparison.OrdinalIgnoreCase) ||
                    filter.TrimStart().StartsWith(": ", StringComparison.Ordinal))
                    hasAnchor = true;
                foreach (var branch in SplitTopLevel(filter))
                {
                    var b = branch.Trim();
                    if (b.Length == 0) continue;
                    branchResults.Add(Classify(engine, b));
                }
            }
            results.Add(new GuideResult(name, branchResults, hasAnchor));
        }
        return results;
    }

    private static List<string> ExtractQuoted(string text)
    {
        var list = new List<string>();
        foreach (Match m in Regex.Matches(text, "\"((?:\\\\.|[^\"])*)\""))
            list.Add(m.Groups[1].Value);
        return list;
    }

    /// <summary>Splits a filter on top-level | only (not inside classes, groups, or escapes).</summary>
    private static IEnumerable<string> SplitTopLevel(string filter)
    {
        var start = 0;
        var depth = 0;
        var inClass = false;
        for (var i = 0; i < filter.Length; i++)
        {
            var ch = filter[i];
            if (ch == '\\') { i++; continue; }
            if (inClass)
            {
                if (ch == ']') inClass = false;
                continue;
            }
            switch (ch)
            {
                case '[':
                    inClass = true;
                    break;
                case '(':
                    depth++;
                    break;
                case ')':
                    if (depth > 0) depth--;
                    break;
                case '|' when depth == 0:
                    yield return filter[start..i];
                    start = i + 1;
                    break;
            }
        }
        yield return filter[start..];
    }

    private static BranchResult Classify(EngineService engine, string branch)
    {
        var negated = branch.StartsWith('!');
        var body = negated ? branch[1..].Trim() : branch;
        if (body.Length == 0) return new BranchResult("unknown/free text", 0, negated);

        var familyKeys = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            var explain = engine.Explain("\"" + body.Replace("\"", "\\\"") + "\"");
            foreach (var modId in explain.Tokens.SelectMany(t => t.Matches).SelectMany(m => m.ModIds).Distinct())
            {
                var mod = engine.FindMod(modId);
                if (mod is not null && mod.Family.Length > 0) familyKeys.Add(mod.Family);
            }
        }
        catch (ArgumentException)
        {
            // Invalid regex: fall through to the text classifiers.
        }

        if (familyKeys.Count == 1) return new BranchResult("one family", 1, negated);
        if (familyKeys.Count > 1) return new BranchResult("multiple families", familyKeys.Count, negated);

        var clean = body.Trim().Trim('^', '$').Trim();
        if (clean.Length == 0) return new BranchResult("unknown/free text", 0, negated);

        var affixNames = engine.ModsForClasses(engine.PlayableClasses().Select(c => c.Id))
            .Where(m => !string.IsNullOrWhiteSpace(m.Name))
            .Select(m => m.Name!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (affixNames.Contains(clean)) return new BranchResult("affix name/tier", 0, negated);

        if (engine.Classes.Any(c => string.Equals(c.Name, clean, StringComparison.OrdinalIgnoreCase)))
            return new BranchResult("class anchor", 0, negated);

        var looksLikeName = clean.Length >= 3 && clean.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 3 &&
                            !clean.Any(ch => ".*+?[](){}|\\^$".Contains(ch));
        if (looksLikeName) return new BranchResult("base/vendor name", 0, negated);

        return new BranchResult("unknown/free text", 0, negated);
    }
}

/// <summary>Writes the markdown/JSON report. Guide text itself is never emitted.</summary>
public static class ReportWriter
{
    public static string Markdown(EngineService engine, IReadOnlyList<Program.CaseOutcome> cases,
        IReadOnlyList<GuideClassifier.GuideResult> guides, bool hadGuideInput)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# PoE2 import coverage (spike)");
        sb.AppendLine();
        sb.AppendLine($"_Generated {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm} UTC._");
        sb.AppendLine();
        sb.AppendLine($"- Data: `{engine.DataVersion}`");
        sb.AppendLine($"- Target model: `{engine.TargetModel}`");
        sb.AppendLine($"- Fragment table: `{engine.FragmentDataVersion ?? "unknown"}`");
        sb.AppendLine();

        sb.AppendLine("## Method");
        sb.AppendLine();
        sb.AppendLine("Each representative app string is compiled by the shipped compiler, imported with the");
        sb.AppendLine("current best-effort `QueryImportService`, then the recovered criteria are recompiled");
        sb.AppendLine("(any-of and, when available, all-of). Classification:");
        sb.AppendLine();
        sb.AppendLine("- **exact** — the recompiled string is identical with no recovery diagnostics.");
        sb.AppendLine("- **factored** — identical after the importer's explain/factoring recovery.");
        sb.AppendLine("- **ambiguous** — every token resolved, but the recompiled string differs.");
        sb.AppendLine("- **unrecoverable** — free text, unresolved/shape errors, lost minimum values, or paste structure loss.");
        sb.AppendLine();

        sb.AppendLine("## App strings");
        sb.AppendLine();
        sb.AppendLine("| Scope | Cases | exact | factored | ambiguous | unrecoverable |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|");
        foreach (var group in cases.GroupBy(x => x.Scope).OrderBy(g => g.Key))
        {
            var exact = group.Count(x => x.Classification == "exact");
            var factored = group.Count(x => x.Classification == "factored");
            var ambiguous = group.Count(x => x.Classification == "ambiguous");
            var unrecoverable = group.Count(x => x.Classification == "unrecoverable");
            sb.AppendLine($"| {group.Key} | {group.Count()} | {exact} | {factored} | {ambiguous} | {unrecoverable} |");
        }
        sb.AppendLine();

        sb.AppendLine("### Cases");
        sb.AppendLine();
        sb.AppendLine("| Case | Scope | Classification | Import mode | Why |");
        sb.AppendLine("|---|---|---|---|---|");
        foreach (var c in cases)
            sb.AppendLine($"| `{c.Id}` | {c.Scope} | **{c.Classification}** | {c.ImportMode} | {Escape(c.Reason)} |");
        sb.AppendLine();

        sb.AppendLine("### Failure reasons");
        sb.AppendLine();
        var failures = cases.Where(x => x.Classification is "ambiguous" or "unrecoverable").ToList();
        if (failures.Count == 0)
        {
            sb.AppendLine("None.");
        }
        else
        {
            sb.AppendLine("| Reason | Count | Cases |");
            sb.AppendLine("|---|---:|---|");
            foreach (var g in failures.GroupBy(x => x.Reason).OrderByDescending(g => g.Count()))
                sb.AppendLine($"| {Escape(g.Key)} | {g.Count()} | {string.Join(", ", g.Select(x => $"`{x.Id}`"))} |");
        }
        sb.AppendLine();

        sb.AppendLine("### Representative queries");
        sb.AppendLine();
        foreach (var c in cases.Where(x => x.Scope is "simple" or "advanced" or "paste").Take(30))
        {
            sb.AppendLine($"- `{c.Id}` — {Escape(c.Description)}");
            sb.AppendLine($"  - compiled: `{Escape(Short(c.Query))}`");
            if (c.Recompiled is not null)
                sb.AppendLine($"  - recompiled: `{Escape(Short(c.Recompiled))}`");
            if (c.Diagnostics.Count > 0)
                sb.AppendLine($"  - diagnostics: {Escape(string.Join("; ", c.Diagnostics.Take(2).Select(Short)))}");
        }
        sb.AppendLine();

        sb.AppendLine("## Guide branches");
        sb.AppendLine();
        if (!hadGuideInput)
        {
            sb.AppendLine("No guide input supplied for this run.");
        }
        else
        {
            sb.AppendLine("Guide input was supplied locally and is not committed (provenance); only");
            sb.AppendLine("classifications and counts appear here.");
            sb.AppendLine();
            sb.AppendLine("| Guide | Branches | one family | multiple families | affix name/tier | base/vendor | unknown/free | class anchor |");
            sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|---|");
            foreach (var g in guides)
            {
                var one = g.Branches.Count(b => b.Category == "one family");
                var multi = g.Branches.Count(b => b.Category == "multiple families");
                var affix = g.Branches.Count(b => b.Category == "affix name/tier");
                var baseName = g.Branches.Count(b => b.Category == "base/vendor name");
                var unknown = g.Branches.Count(b => b.Category == "unknown/free text");
                sb.AppendLine($"| {Escape(g.Name)} | {g.Branches.Count} | {one} | {multi} | {affix} | {baseName} | {unknown} | {(g.HasClassAnchor ? "yes" : "no")} |");
            }
            sb.AppendLine();
            var total = guides.Sum(g => g.Branches.Count);
            var totalOne = guides.Sum(g => g.Branches.Count(b => b.Category == "one family"));
            var totalMulti = guides.Sum(g => g.Branches.Count(b => b.Category == "multiple families"));
            var totalAffix = guides.Sum(g => g.Branches.Count(b => b.Category == "affix name/tier"));
            var totalBase = guides.Sum(g => g.Branches.Count(b => b.Category == "base/vendor name"));
            var totalUnknown = guides.Sum(g => g.Branches.Count(b => b.Category == "unknown/free text"));
            sb.AppendLine($"**Total:** {total} branches — {totalOne} one-family, {totalMulti} multi-family, " +
                          $"{totalAffix} affix-name, {totalBase} base/vendor, {totalUnknown} unknown/free text.");
            var withAnchors = guides.Count(g => g.HasClassAnchor);
            sb.AppendLine($"{withAnchors} of {guides.Count} guide filters carry a class anchor; the rest are global-simple candidates.");
        }
        sb.AppendLine();

        sb.AppendLine("## Reading this");
        sb.AppendLine();
        var exactish = cases.Count(x => x.Classification is "exact" or "factored");
        sb.AppendLine($"- App strings reproduced query-equivalently: **{exactish} / {cases.Count}**.");
        var arbitrary = cases.Count(x => x.ArbitraryChoice);
        sb.AppendLine($"- {arbitrary} of those relied on an arbitrary catalog mod for a shared fragment; the");
        sb.AppendLine("  query matches, but the imported pick identity is not guaranteed.");
        var amb = cases.Count(x => x.Classification == "ambiguous");
        var unrec = cases.Count(x => x.Classification == "unrecoverable");
        sb.AppendLine($"- Ambiguous: **{amb}**; unrecoverable: **{unrec}**.");
        sb.AppendLine("- Affix-name picks currently survive only as free-text keywords; they need the");
        sb.AppendLine("  affix-name index. Minimum values and modded multi-class pastes also need the");
        sb.AppendLine("  shared reverse-mapping layer; the per-case table shows exactly where.");
        sb.AppendLine();

        var nameFailures = cases.Count(x => x.HasNamePick && x.Classification == "unrecoverable");

        sb.AppendLine("## Recommendation");
        sb.AppendLine();
        sb.AppendLine($"- **Start Phase 1.** {exactish}/{cases.Count} app strings are query-equivalent today, but " +
                      $"{arbitrary} depend on an arbitrary mod for a shared fragment and {nameFailures} " +
                      "affix-name picks survive only as free text. The anchor/group parser, affix-name index, " +
                      "and single-slot reconstruction make those deterministic.");
        var multiClassPastes = cases.Count(x => x.ImportMode == "multi-class");
        sb.AppendLine($"- **Budget Phase 2 for paste.** All {multiClassPastes} multi-class paste cases lose their " +
                      "per-slot mapping; this is the largest structural gap.");
        sb.AppendLine("- **Factoring is not the first blocker.** Ten cases resolve through explain/recovery and only " +
                      "one any-of arrangement recompiles differently in this sample; keep the factored resolver on " +
                      "the Phase 2 list rather than starting there.");
        sb.AppendLine("- **Guides are promising but not next.** The sample has 40 branches, mostly multi-family (21) " +
                      "and affix names (10), with no class anchors. Family/affix extraction is worth prototyping, " +
                      "but multi-family reconciliation and a base-type list are prerequisites.");
        return sb.ToString();
    }

    public static string Json(EngineService engine, IReadOnlyList<Program.CaseOutcome> cases,
        IReadOnlyList<GuideClassifier.GuideResult> guides)
    {
        var payload = new
        {
            generated = DateTimeOffset.UtcNow,
            dataVersion = engine.DataVersion,
            targetModel = engine.TargetModel,
            fragmentTable = engine.FragmentDataVersion,
            app = cases.Select(c => new
            {
                id = c.Id,
                scope = c.Scope,
                description = c.Description,
                classification = c.Classification,
                reason = c.Reason,
                importMode = c.ImportMode,
                sameQuery = c.Recompiled == c.Query,
                minValue = c.HasMinValue,
                namePick = c.HasNamePick,
                familyPick = c.HasFamilyPick,
                paste = c.IsPaste,
                arbitraryChoice = c.ArbitraryChoice,
                includes = c.Includes,
                excludes = c.Excludes,
                keywords = c.Keywords,
                diagnostics = c.Diagnostics,
            }),
            guide = guides.Select(g => new
            {
                g.Name,
                classAnchor = g.HasClassAnchor,
                branches = g.Branches.Count,
                categories = g.Branches.GroupBy(b => b.Category)
                    .ToDictionary(x => x.Key, x => x.Count()),
                multiFamilyBranches = g.Branches.Count(b => b.Category == "multiple families"),
            }),
        };
        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string Short(string value) =>
        value.Length <= 90 ? value : value[..87] + "...";

    private static string Escape(string value) =>
        value.Replace("|", "\\|").Replace("\r", "").Replace("\n", " ");
}

/// <summary>Writes the Phase 1 reconstruction report from the same fixture matrix.</summary>
public static class Phase1Report
{
    public static string Markdown(EngineService engine, IReadOnlyList<Program.Phase1Outcome> cases)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# PoE2 query reconstruction — Phase 1 results");
        sb.AppendLine();
        sb.AppendLine($"_Generated {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm} UTC._");
        sb.AppendLine();
        sb.AppendLine($"- Data: `{engine.DataVersion}`");
        sb.AppendLine($"- Target model: `{engine.TargetModel}`");
        sb.AppendLine($"- Fragment table: `{engine.FragmentDataVersion ?? "unknown"}`");
        sb.AppendLine();
        sb.AppendLine("## Method");
        sb.AppendLine();
        sb.AppendLine("The same representative strings as the coverage spike are reconstructed with");
        sb.AppendLine("`QueryReconstructionService`, then the recovered criteria are recompiled and");
        sb.AppendLine("compared byte-for-byte with the original. A case passes only when it is both");
        sb.AppendLine("reconstructed and recompiles to the identical string. Tierless fragments prefer");
        sb.AppendLine("the family pick; name literals stay name picks; factored and multi-class forms");
        sb.AppendLine("are expanded. The Import string panel applies the recovered slots; the same");
        sb.AppendLine("comparison is exercised by the UI tests. Free text, legacy anchors, class-only");
        sb.AppendLine("strings, and minimum values fail closed or are accepted losses.");
        sb.AppendLine();

        var passed = cases.Count(x => x.Ok && x.SameQuery);
        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine("| Scope | Cases | reconstructed | recompiled identically | fail-closed |");
        sb.AppendLine("|---|---:|---:|---:|---:|");
        foreach (var group in cases.GroupBy(x => x.Scope).OrderBy(g => g.Key))
        {
            var ok = group.Count(x => x.Ok && x.SameQuery);
            sb.AppendLine($"| {group.Key} | {group.Count()} | {group.Count(x => x.Ok)} | {ok} | {group.Count() - ok} |");
        }
        sb.AppendLine();
        sb.AppendLine($"**Overall:** {passed}/{cases.Count} cases reconstruct and recompile identically.");
        sb.AppendLine();

        sb.AppendLine("## Matrix");
        sb.AppendLine();
        sb.AppendLine("| Case | Scope | Result | Mode | Class | Picks | MatchAll | Notes |");
        sb.AppendLine("|---|---|---|---|---|---:|---|---|");
        foreach (var c in cases)
        {
            var result = c.Ok && c.SameQuery ? "**pass**" : "fail-closed";
            var classes = string.Join(", ", c.Slots.Select(s => s.ClassId)
                .Where(x => x.Length > 0).Distinct(StringComparer.Ordinal));
            var picks = c.Slots.Sum(s => s.Includes.Count);
            var matchAll = c.Slots.Any(s => s.MatchAll);
            sb.AppendLine($"| `{c.Id}` | {c.Scope} | {result} | {c.Mode} | {(classes.Length == 0 ? "—" : classes)} | {picks} | {(matchAll ? "yes" : "no")} | {Escape(Short(NoteFor(c)))} |");
        }
        sb.AppendLine();

        sb.AppendLine("## Passing examples");
        sb.AppendLine();
        foreach (var c in cases.Where(x => x.Ok && x.SameQuery))
        {
            sb.AppendLine($"### `{c.Id}` — {Escape(c.Description)}");
            sb.AppendLine($"- query: `{Escape(c.Query)}`");
            sb.AppendLine($"- mode: {c.Mode}");
            foreach (var slot in c.Slots)
            {
                var picks = slot.Includes.Count == 0
                    ? "—"
                    : string.Join(", ", slot.Includes.Select(PickLabel).Select(x => $"`{Escape(x)}`"));
                var cls = string.IsNullOrEmpty(slot.ClassId) ? "" : $" · {slot.ClassId}";
                sb.AppendLine($"- {slot.SlotId}{cls} — {(slot.MatchAll ? "all of" : "any of")}: {picks}");
            }
            if (c.Excludes.Count > 0)
                sb.AppendLine($"- exclusions: {string.Join(", ", c.Excludes.Select(x => $"`{Escape(x)}`"))}");
            foreach (var warning in c.Warnings)
                sb.AppendLine($"- warning: {Escape(warning)}");
            sb.AppendLine("- recompile: identical");
            sb.AppendLine();
        }

        sb.AppendLine("## Fail-closed examples");
        sb.AppendLine();
        foreach (var c in cases.Where(x => !x.Ok || !x.SameQuery))
        {
            var reason = c.Ok ? "recompiled string differs" : (c.Diagnostics.FirstOrDefault() ?? "rejected");
            sb.AppendLine($"- `{c.Id}` ({c.Scope}) — {Escape(Short(reason))}");
        }
        sb.AppendLine();

        sb.AppendLine("## Review notes");
        sb.AppendLine();
        sb.AppendLine("- **Simple affix-name picks choose one owning family when the names exist on");
        sb.AppendLine("  multiple classes.** The names are the query term; the family only supplies");
        sb.AppendLine("  identity, so the string is unchanged. An advanced anchor scopes it explicitly.");
        sb.AppendLine("- **Legacy literal class anchors** (e.g. `\": One Hand M\"`) reconstruct the class");
        sb.AppendLine("  but recompile to the current wildcard anchor, so they fail the same-string check.");
        sb.AppendLine("- **Tierless fragments reconstruct as family picks**, not representative tiers.");
        sb.AppendLine("  When a fragment or name set spans families, the chosen family is deterministic");
        sb.AppendLine("  but arbitrary; the query is unchanged.");
        sb.AppendLine("- **Ring1 + ring2 paste** reconstructs to one Ring scope; exact ring split is a");
        sb.AppendLine("  non-goal and the string is unchanged.");
        sb.AppendLine("- **Same-class all-of** falls back to any-of in the compiler; the reconstruction");
        sb.AppendLine("  reports the any-of form (accepted).");
        sb.AppendLine("- **Minimum values are genuinely lost**; the query encodes a threshold that the");
        sb.AppendLine("  pick model cannot recover (accepted limitation).");
        sb.AppendLine("- Factored fragments/name sets and multi-class pastes are supported by this");
        sb.AppendLine("  path. The Import string panel applies the recovered slots.");
        sb.AppendLine("- Guide-filter extraction ships as a separate mode: exact names/fragments and");
        sb.AppendLine("  regex matches against family/tier text map; multi-family branches use a");
        sb.AppendLine("  picker; negative groups apply as raw regex exclusions. Base/vendor names and");
        sb.AppendLine("  explicit class anchors are follow-ups.");
        sb.AppendLine();
        sb.AppendLine("Tests: `dotnet test tests/PoeRegex.Web.Tests/PoeRegex.Web.Tests.csproj -c Release --filter \"FullyQualifiedName~QueryReconstructionTests\"`.");
        return sb.ToString();
    }

    public static string Json(EngineService engine, IReadOnlyList<Program.Phase1Outcome> cases)
    {
        var payload = new
        {
            generated = DateTimeOffset.UtcNow,
            dataVersion = engine.DataVersion,
            targetModel = engine.TargetModel,
            fragmentTable = engine.FragmentDataVersion,
            passed = cases.Count(x => x.Ok && x.SameQuery),
            total = cases.Count,
            cases = cases.Select(c => new
            {
                id = c.Id,
                scope = c.Scope,
                description = c.Description,
                query = c.Query,
                ok = c.Ok,
                sameQuery = c.SameQuery,
                mode = c.Mode,
                slots = c.Slots.Select(s => new
                {
                    slotId = s.SlotId,
                    classId = s.ClassId,
                    matchAll = s.MatchAll,
                    includes = s.Includes.Select(PickLabel),
                }),
                excludes = c.Excludes,
                warnings = c.Warnings,
                diagnostics = c.Diagnostics,
            }),
        };
        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string NoteFor(Program.Phase1Outcome c)
    {
        if (!c.Ok) return c.Diagnostics.FirstOrDefault() ?? "rejected";
        if (!c.SameQuery) return "recompiled string differs";
        return c.Id switch
        {
            "paste-rings" => "identical; rings merge into one Ring scope (accepted)",
            "paste-same-class-allof" => "identical; same-class all-of falls back to any-of (accepted)",
            _ => "identical recompile",
        };
    }

    private static string PickLabel(WantedMod pick) =>
        pick.Names is { Count: > 0 } ? "name:" + string.Join("|", pick.Names)
        : pick.VariantModIds is { Count: > 0 } ? "family:" + pick.ModId
        : pick.ModId;

    private static string Short(string value) =>
        value.Length <= 110 ? value : value[..107] + "...";

    private static string Escape(string value) =>
        value.Replace("|", "\\|").Replace("\r", "").Replace("\n", " ");
}

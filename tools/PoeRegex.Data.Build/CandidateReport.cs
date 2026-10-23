using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PoeRegex.Core;
using PoeRegex.Core.Compilation;
using PoeRegex.Core.Data;

namespace PoeRegex.Data.Build;

/// <summary>
/// Data-only candidate experiment. It enumerates literal, partial-word,
/// anchored and middle-wildcard shapes against the real class corpora and
/// writes a Pareto-style report. It deliberately does not change the shipped
/// fragment table or the runtime selector.
/// </summary>
public static class CandidateReport
{
    internal static readonly string[] CommonFamilies =
    [
        "IncreasedLife", "IncreasedMana", "LocalPhysicalDamagePercent",
        "LocalFireDamage", "LocalColdDamage", "LocalLightningDamage",
        "LocalEnergyShield", "LocalPhysicalDamageReductionRating", "LocalEvasionRating",
        "BaseSpirit", "FireResistance", "ColdResistance", "LightningResistance",
        "ChaosResistance", "Strength", "Dexterity", "Intelligence",
        "MovementVelocity", "LocalIncreasedAttackSpeed", "LocalBaseCriticalStrikeChance",
        "LocalAccuracyRating", "ManaRegeneration",
    ];

    private static readonly Dictionary<string, string> RepresentativeClasses = new(StringComparer.Ordinal)
    {
        ["IncreasedLife"] = "Belt",
        ["IncreasedMana"] = "Ring",
        ["LocalPhysicalDamagePercent"] = "Bow",
        ["LocalFireDamage"] = "Bow",
        ["LocalColdDamage"] = "Bow",
        ["LocalLightningDamage"] = "Bow",
        ["LocalEnergyShield"] = "Body Armour",
        ["LocalPhysicalDamageReductionRating"] = "Body Armour",
        ["LocalEvasionRating"] = "Body Armour",
        ["BaseSpirit"] = "Amulet",
        ["FireResistance"] = "Amulet",
        ["ColdResistance"] = "Amulet",
        ["LightningResistance"] = "Amulet",
        ["ChaosResistance"] = "Amulet",
        ["Strength"] = "Amulet",
        ["Dexterity"] = "Amulet",
        ["Intelligence"] = "Amulet",
        ["MovementVelocity"] = "Boots",
        ["LocalIncreasedAttackSpeed"] = "Bow",
        ["LocalBaseCriticalStrikeChance"] = "Bow",
        ["LocalAccuracyRating"] = "Bow",
        ["ManaRegeneration"] = "Ring",
    };

    private sealed record SourceLine(string Pattern, string Real);
    private sealed record Chunk(string Text, bool StartsWord, bool EndsWord);
    private sealed class Option
    {
        public required string Fragment { get; init; }
        public required int Length { get; init; }
        public required bool HasWildcard { get; init; }
        public required int ForeignPatterns { get; init; }
        public required int ForeignLines { get; init; }
        public required IReadOnlyList<string> ForeignExamples { get; init; }
        public required bool Plausible { get; init; }
        public bool CrossSynthetic { get; set; }
        public bool CrossCaptured { get; set; }
        public bool HasCross => CrossSynthetic || CrossCaptured;
    }

    public static int Run(string[] args)
    {
        var opts = ParseOptions(args);
        var gameName = GetValue(opts, "--game", "poe2");
        var game = gameName == "poe1" ? GameTarget.PoE1 : GameTarget.PoE2;
        var catalogPath = GetValue(opts, "--catalog", $"data/catalogs/{gameName}.catalog.json");
        var outPath = GetValue(opts, "--out", $"docs/CANDIDATE-REPORT-{gameName}.md");
        var maxLength = int.TryParse(GetValue(opts, "--max-length", "24"), out var ml) ? ml : 24;
        var only = GetValue(opts, "--family", "");
        var includeGlobal = opts.ContainsKey("--global");

        var catalog = new FileCatalogSource(catalogPath).Load(game);
        var corpus = new EngineCorpus(catalog);
        var table = LoadTable($"data/catalogs/{gameName}.fragments.json");
        var familyList = only.Length > 0
            ? only.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : CommonFamilies;

        var sb = new StringBuilder();
        var details = new StringBuilder();
        var summary = new List<(string Family, string ClassId, string? Strict, int StrictLength, string? LineOnly, string? Current)>();
        sb.AppendLine($"# Candidate grammar report — {gameName}");
        sb.AppendLine();
        sb.AppendLine("Generated from the real catalog + vendor corpus + captured safety items.");
        sb.AppendLine("This report is diagnostic only; it does not change the shipped fragment table.");
        sb.AppendLine("Class scopes are the multi-slot scopes. Vendor/base-context filtering is not modeled in this pass; a class scope includes all of that class's vendor lines.");
        sb.AppendLine();
        sb.AppendLine("Grammar:");
        sb.AppendLine("- literal substrings, including partial words; no full-word requirement");
        sb.AppendLine("- word-start prefixes and word-end suffixes");
        sb.AppendLine("- one or two middle wildcards (`.`, `.*`, `.+`, fixed-length dot runs)");
        sb.AppendLine("- optional `^`/`$` anchors; no adjacency allowance");
        sb.AppendLine("- max candidate length " + maxLength);
        sb.AppendLine();
        sb.AppendLine("`Strict` = zero foreign lines and no cross-line match. `Line-only` = zero foreign lines but the wildcard may cross a line break. `Shortest observed` = shortest plausible candidates regardless of collisions.");
        sb.AppendLine("`Plausible` means the candidate starts at a word start or ends at a word end and contains at least three letters; arbitrary letter runs are excluded from the decision tables.");
        sb.AppendLine();

        foreach (var family in familyList)
        {
            var classId = GetValue(opts, "--class", RepresentativeClasses.GetValueOrDefault(family, ""));
            if (classId.Length == 0)
                classId = FirstClassFor(catalog, family);
            if (classId.Length == 0)
            {
                sb.AppendLine($"## {family}");
                sb.AppendLine();
                sb.AppendLine("No class with this family was found.");
                sb.AppendLine();
                continue;
            }
            var result = Analyze(corpus, catalog, table, family, classId, maxLength);
            if (result is null)
            {
                sb.AppendLine($"## {family} — {classId}");
                sb.AppendLine();
                sb.AppendLine("No target lines found.");
                sb.AppendLine();
                continue;
            }
            WriteScope(details, family, classId, result.Value, isGlobal: false);
            var strictBest = result.Value.Options
                .Where(o => o.Plausible && o.ForeignLines == 0 && !o.HasCross)
                .OrderBy(o => o.Length).ThenBy(o => o.Fragment, StringComparer.Ordinal)
                .FirstOrDefault();
            var lineOnlyBest = result.Value.Options
                .Where(o => o.Plausible && o.ForeignLines == 0)
                .OrderBy(o => o.Length).ThenBy(o => o.Fragment, StringComparer.Ordinal)
                .FirstOrDefault();
            summary.Add((family, classId, strictBest?.Fragment, strictBest?.Length ?? 0,
                lineOnlyBest?.Fragment, result.Value.Current));
            if (includeGlobal)
            {
                var global = Analyze(corpus, catalog, table, family, null, maxLength);
                if (global is not null) WriteScope(details, family, null, global.Value, isGlobal: true);
            }
        }

        sb.AppendLine("## Common-family strict summary");
        sb.AppendLine();
        sb.AppendLine("| family | class | strict candidate | len | line-only candidate | current |");
        sb.AppendLine("|---|---|---|---:|---|---|");
        foreach (var row in summary)
            sb.AppendLine($"| {row.Family} | {row.ClassId} | {Fmt(row.Strict ?? "(none)")} | {row.StrictLength} | {Fmt(row.LineOnly ?? "(none)")} | {Fmt(row.Current ?? "(unresolved)")} |");
        sb.AppendLine();
        var strictLengths = summary.Where(x => x.StrictLength > 0).Select(x => x.StrictLength).ToList();
        if (strictLengths.Count > 0)
            sb.AppendLine($"Strict class-scope average: **{strictLengths.Average():F1}** characters across {strictLengths.Count} families (max {strictLengths.Max()}).");
        sb.AppendLine(details.ToString());

        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        File.WriteAllText(outPath, sb.ToString());
        Console.WriteLine($"wrote {outPath} ({new FileInfo(outPath).Length / 1024} KiB)");
        return 0;
    }

    private static ScopeResult? Analyze(
        EngineCorpus corpus, Catalog catalog, FragmentTable? table,
        string family, string? scope, int maxLength)
    {
        var targetPatterns = TargetPatterns(catalog, family, scope);
        if (targetPatterns.Count == 0) return null;

        var all = new List<SourceLine>();
        if (scope is null)
            all.AddRange(corpus.RealLinesGlobal().Select(x => new SourceLine(x.Pattern, x.Real)));
        else
            all.AddRange(corpus.RealLinesForClass(scope).Select(x => new SourceLine(x.Pattern, x.Real)));
        foreach (var (pattern, raw) in corpus.SafetyLines(scope))
            all.Add(new SourceLine(pattern, raw));

        var targetLines = all
            .Where(x => targetPatterns.Contains(x.Pattern))
            .Select(x => x.Real)
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x.Length)
            .ThenBy(x => x, StringComparer.Ordinal)
            .ToList();
        if (targetLines.Count == 0) return null;

        var foreign = all
            .Where(x => !targetPatterns.Contains(x.Pattern))
            .GroupBy(x => x.Real, StringComparer.Ordinal)
            .Select(g => new SourceLine(g.First().Pattern, g.Key))
            .ToList();

        var current = CurrentFragment(table, family, scope, targetPatterns, catalog);
        var options = Generate(corpus, targetLines, foreign, scope, maxLength, current);
        return new ScopeResult(targetLines.Count, foreign.Count, current, options);
    }

    /// <summary>
    /// Use the strict candidate found for a common family's global scope as a
    /// seed for each of that family's global patterns. Each application is
    /// revalidated against the full global line set.
    /// </summary>
    public static void ApplyRepresentativeGlobalCandidates(
        EngineCorpus corpus, Catalog catalog, FragmentTable table, int maxLength = 24)
    {
        foreach (var family in CommonFamilies)
        {
            var result = Analyze(corpus, catalog, table, family, null, maxLength);
            if (result is null) continue;
            var candidate = result.Value.Options
                .Where(o => o.Plausible && o.ForeignLines == 0 && !o.HasCross)
                .OrderBy(o => o.Length).ThenBy(o => o.Fragment, StringComparer.Ordinal)
                .Select(o => o.Fragment).FirstOrDefault();
            if (candidate is null) continue;
            var patterns = TargetPatterns(catalog, family, null);
            var all = ScopeLines(corpus, null);
            foreach (var pattern in patterns)
            {
                var targets = all.Where(x => x.Pattern == pattern).Select(x => x.Real)
                    .Distinct(StringComparer.Ordinal).ToList();
                if (targets.Count == 0) continue;
                var foreign = all.Where(x => x.Pattern != pattern).Select(x => (x.Pattern, x.Real)).ToList();
                if (!CandidateMatches(corpus, null, candidate, targets, foreign)) continue;
                var current = table.Global.GetValueOrDefault(pattern);
                if (current is null || candidate.Length < current.Length)
                    table.Global[pattern] = candidate;
            }
        }
    }

    /// <summary>
    /// Use the strict candidate found for a representative class as a seed for
    /// every class in the same common family. Each application is revalidated
    /// against that class's target and foreign lines; unsafe applications are
    /// left at the current table value.
    /// </summary>
    public static void ApplyRepresentativeClassCandidates(
        EngineCorpus corpus, Catalog catalog, FragmentTable table, int maxLength = 24)
    {
        foreach (var family in CommonFamilies)
        {
            var rep = RepresentativeClasses.GetValueOrDefault(family, FirstClassFor(catalog, family));
            if (rep.Length == 0) continue;
            var result = Analyze(corpus, catalog, table, family, rep, maxLength);
            if (result is null) continue;
            var candidate = result.Value.Options
                .Where(o => o.Plausible && o.ForeignLines == 0 && !o.HasCross)
                .OrderBy(o => o.Length).ThenBy(o => o.Fragment, StringComparer.Ordinal)
                .Select(o => o.Fragment).FirstOrDefault();
            if (candidate is null) continue;

            foreach (var cls in catalog.ItemClasses)
            {
                var patterns = TargetPatterns(catalog, family, cls.Id);
                if (patterns.Count == 0) continue;
                var all = ScopeLines(corpus, cls.Id);
                if (!table.ByClass.TryGetValue(cls.Id, out var map))
                {
                    map = new Dictionary<string, string>(StringComparer.Ordinal);
                    table.ByClass[cls.Id] = map;
                }
                foreach (var pattern in patterns)
                {
                    var targets = all.Where(x => x.Pattern == pattern).Select(x => x.Real)
                        .Distinct(StringComparer.Ordinal).ToList();
                    if (targets.Count == 0) continue;
                    if (!CandidateMatches(corpus, cls.Id, candidate, targets,
                            all.Where(x => x.Pattern != pattern).Select(x => (x.Pattern, x.Real)).ToList()))
                        continue;
                    var current = map.GetValueOrDefault(pattern);
                    if (current is null || candidate.Length < current.Length)
                        map[pattern] = candidate;
                }
            }
        }
    }

    private static bool CandidateMatches(
        EngineCorpus corpus, string? scope, string candidate,
        IReadOnlyList<string> targets, IReadOnlyList<(string Pattern, string Real)> foreign)
    {
        Regex regex;
        try
        {
            regex = new Regex(candidate,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(100));
        }
        catch (ArgumentException)
        {
            return false;
        }
        if (!targets.All(regex.IsMatch)) return false;
        foreach (var (_, real) in foreign)
            if (regex.IsMatch(real)) return false;
        if (candidate.Contains('.'))
        {
            if (!corpus.CrossLineSafe(scope, candidate)) return false;
            if (CrossCaptured(corpus, scope, candidate)) return false;
        }
        return true;
    }

    private static List<SourceLine> ScopeLines(EngineCorpus corpus, string? scope)
    {
        var all = new List<SourceLine>();
        if (scope is null)
            all.AddRange(corpus.RealLinesGlobal().Select(x => new SourceLine(x.Pattern, x.Real)));
        else
            all.AddRange(corpus.RealLinesForClass(scope).Select(x => new SourceLine(x.Pattern, x.Real)));
        foreach (var (pattern, raw) in corpus.SafetyLines(scope))
            all.Add(new SourceLine(pattern, raw));
        return all;
    }

    private static void WriteScope(StringBuilder sb, string family, string? scope, ScopeResult result, bool isGlobal)
    {
        sb.AppendLine($"## {family}{(isGlobal ? " — global" : " — " + scope)}");
        sb.AppendLine();
        sb.AppendLine($"Targets: {result.TargetLines}; foreign lines: {result.ForeignLines}; current: {Fmt(result.Current ?? "(unresolved)")}");
        sb.AppendLine();

        var options = result.Options;
        if (options.Count == 0)
        {
            sb.AppendLine("No candidate matched every target line.");
            sb.AppendLine();
            return;
        }

        var strict = options
            .Where(o => o.Plausible && o.ForeignLines == 0 && !o.HasCross)
            .OrderBy(o => o.Length).ThenBy(o => o.Fragment, StringComparer.Ordinal)
            .Take(5).ToList();
        var lineOnly = options
            .Where(o => o.Plausible && o.ForeignLines == 0)
            .OrderBy(o => o.Length).ThenBy(o => o.Fragment, StringComparer.Ordinal)
            .Take(5).ToList();
        var shortest = options
            .Where(o => o.Plausible)
            .OrderBy(o => o.Length).ThenBy(o => o.ForeignLines).ThenBy(o => o.Fragment, StringComparer.Ordinal)
            .Take(8).ToList();

        WriteOptions(sb, "Strict (zero foreign, no cross-line)", strict);
        WriteOptions(sb, "Line-only (zero foreign; cross-line possible)", lineOnly);
        WriteOptions(sb, "Shortest observed (collisions included)", shortest);
    }

    private static void WriteOptions(StringBuilder sb, string title, IReadOnlyList<Option> options)
    {
        sb.AppendLine($"### {title}");
        sb.AppendLine();
        if (options.Count == 0)
        {
            sb.AppendLine("None.");
            sb.AppendLine();
            return;
        }
        sb.AppendLine("| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |");
        sb.AppendLine("|---:|---|---:|---:|---|---|---|");
        foreach (var o in options)
            sb.AppendLine($"| {o.Length} | {Fmt(o.Fragment)} | {o.ForeignPatterns} | {o.ForeignLines} | {(o.CrossSynthetic ? "yes" : "no")} | {(o.CrossCaptured ? "yes" : "no")} | {Fmt(string.Join(" · ", o.ForeignExamples))} |");
        sb.AppendLine();
    }

    private static string Fmt(string value) =>
        value.Length == 0 ? "" : "`" + value.Replace("|", "\\|") + "`";

    private static string? CurrentFragment(
        FragmentTable? table, string family, string? scope,
        IReadOnlySet<string> targetPatterns, Catalog catalog)
    {
        if (table is null) return null;
        var map = scope is null
            ? table.Global
            : table.ByClass.GetValueOrDefault(scope) ?? new Dictionary<string, string>();
        return targetPatterns
            .Select(p => map.GetValueOrDefault(p))
            .Where(x => !string.IsNullOrEmpty(x))
            .OrderBy(x => x!.Length)
            .FirstOrDefault();
    }

    internal static HashSet<string> TargetPatterns(Catalog catalog, string family, string? scope)
    {
        var patterns = new HashSet<string>(StringComparer.Ordinal);
        foreach (var mod in catalog.Mods)
        {
            var key = mod.Family.Length > 0 ? mod.Family : mod.Id;
            if (!string.Equals(key, family, StringComparison.Ordinal)) continue;
            if (scope is not null && !mod.Classes.Contains(scope, StringComparer.Ordinal)) continue;
            foreach (var line in mod.StatLines) patterns.Add(line.Pattern);
        }
        return patterns;
    }

    private static string FirstClassFor(Catalog catalog, string family) =>
        catalog.ItemClasses
            .FirstOrDefault(c => catalog.Mods.Any(m =>
                (m.Family.Length > 0 ? m.Family : m.Id) == family && m.Classes.Contains(c.Id)))?.Id ?? "";

    private static List<Option> Generate(
        EngineCorpus corpus, IReadOnlyList<string> targets, IReadOnlyList<SourceLine> foreign,
        string? scope, int maxLength, string? current)
    {
        var candidates = new Dictionary<string, (bool Wildcard, string Hint, bool Plausible)>(StringComparer.Ordinal);
        var allStart = false;
        var allEnd = false;

        void AddCandidate(string fragment, bool wildcard, string hint, bool plausible)
        {
            if (fragment.Length == 0 || fragment.Length > maxLength) return;
            if (fragment[0] == ' ' || fragment[^1] == ' ') return;
            if (fragment.Count(char.IsAsciiLetter) < 3) return;
            if (candidates.TryGetValue(fragment, out var existing))
            {
                if (!existing.Plausible && plausible)
                    candidates[fragment] = (existing.Wildcard, existing.Hint, true);
                return;
            }
            candidates[fragment] = (wildcard, hint, plausible);
        }

        void AddWithAnchors(string fragment, bool wildcard, string hint, bool plausible)
        {
            AddCandidate(fragment, wildcard, hint, plausible);
            if (allStart) AddCandidate("^" + fragment, wildcard, hint, plausible);
            if (allEnd) AddCandidate(fragment + "$", wildcard, hint, plausible);
            if (allStart && allEnd) AddCandidate("^" + fragment + "$", wildcard, hint, plausible);
        }

        // The current table fragment is a useful fallback and guarantees that
        // the experiment cannot report "none" where a shipped strict form
        // already exists.
        if (!string.IsNullOrWhiteSpace(current))
            AddCandidate(current, current.Contains('.'), "", true);

        // Use one representative line for every distinct display shape. This
        // matters for ranged stats: their compact and (roll(min-max)) forms
        // normalize differently and both must seed candidates.
        var seeds = new List<string>();
        var seenShapes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in targets.OrderBy(x => x.Length).ThenBy(x => x, StringComparer.Ordinal))
        {
            var shape = CatalogLineNormalizer.Normalize(line);
            if (seenShapes.Add(shape)) seeds.Add(line);
        }
        if (seeds.Count == 0) seeds.Add(targets[0]);

        var wildcards = new List<string> { ".", ".*", ".+" };
        for (var k = 2; k <= 5; k++) wildcards.Add(new string('.', k));

        foreach (var shortest in seeds)
        {
            allStart = targets.All(t => t.Length > 0 && t.StartsWith(shortest[0]));
            allEnd = targets.All(t => t.Length > 0 && t.EndsWith(shortest[^1]));

            // Literal substrings. This includes arbitrary partial words, not just full words.
            for (var start = 0; start < shortest.Length; start++)
            {
                if (shortest[start] == ' ') continue;
                for (var len = 1; len <= shortest.Length - start && len <= maxLength; len++)
                {
                    var raw = shortest.Substring(start, len);
                    if (raw.EndsWith(' ')) continue;
                    if (!IsLiteral(raw)) continue;
                    var startsWord = start == 0 || !char.IsAsciiLetter(shortest[start - 1]);
                    var endsWord = start + len == shortest.Length || !char.IsAsciiLetter(shortest[start + len]);
                    var letters = raw.Count(char.IsAsciiLetter);
                    if (letters < 3) continue;
                    if (!startsWord && !endsWord) continue;
                    AddWithAnchors(raw, false, raw, true);
                }
            }

            var chunks = WordChunks(shortest);
            var starts = chunks.Where(c => c.StartsWord).ToList();
            var ends = chunks.Where(c => c.EndsWord).ToList();
            var all = chunks.ToList();

            // One middle wildcard: two literal pieces.
            foreach (var a in starts)
            {
                foreach (var b in ends)
                {
                    foreach (var w in wildcards)
                    {
                        var frag = a.Text + w + b.Text;
                        if (frag.Length > maxLength) continue;
                        AddWithAnchors(frag, true, LongestHint(a.Text, b.Text), a.StartsWord && b.EndsWord);
                    }
                }
            }

            // Two middle wildcards: prefix, middle word, suffix. Keep the middle
            // piece to word-sized literals so the experiment stays bounded.
            var mids = all.Where(c => c.StartsWord && !c.Text.Contains(' ') &&
                                      c.Text.Length <= 10).ToList();
            var firsts = starts.Where(c => c.Text.Length <= 8).Take(24).ToList();
            var lasts = ends.Where(c => c.Text.Length <= 8).Take(24).ToList();
            foreach (var a in firsts)
            {
                foreach (var m in mids)
                {
                    foreach (var b in lasts)
                    {
                        foreach (var w1 in new[] { ".*", ".+" })
                        {
                            foreach (var w2 in new[] { ".*", ".+" })
                            {
                                var frag = a.Text + w1 + m.Text + w2 + b.Text;
                                if (frag.Length > maxLength) continue;
                                AddWithAnchors(frag, true, LongestHint(a.Text, m.Text, b.Text), a.StartsWord && b.EndsWord);
                            }
                        }
                    }
                }
            }
        }

        var options = new List<Option>();
        foreach (var (fragment, (wildcard, hint, plausible)) in candidates)
        {
            if (!plausible) continue;
            Regex regex;
            try
            {
                regex = new Regex(fragment,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                    TimeSpan.FromMilliseconds(100));
            }
            catch (ArgumentException)
            {
                continue;
            }

            var matchesTargets = true;
            foreach (var t in targets)
            {
                if (regex.IsMatch(t)) continue;
                matchesTargets = false;
                break;
            }
            if (!matchesTargets) continue;

            var foreignMatches = new List<SourceLine>();
            foreach (var line in foreign)
            {
                if (hint.Length > 0 && line.Real.IndexOf(hint, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (!regex.IsMatch(line.Real)) continue;
                foreignMatches.Add(line);
            }

            var foreignPatterns = foreignMatches.Select(x => x.Pattern).Distinct(StringComparer.Ordinal).Count();
            var examples = foreignMatches.Select(x => x.Real).Distinct(StringComparer.Ordinal).Take(2).ToList();
            options.Add(new Option
            {
                Fragment = fragment,
                Length = fragment.Length,
                HasWildcard = wildcard,
                ForeignPatterns = foreignPatterns,
                ForeignLines = foreignMatches.Select(x => x.Real).Distinct(StringComparer.Ordinal).Count(),
                ForeignExamples = examples,
                Plausible = plausible,
            });
        }

        // Cross-line is expensive. Evaluate it for the candidates that can be
        // strict/line-only or are in the shortest observed set.
        var zero = options.Where(o => o.ForeignLines == 0)
            .OrderBy(o => o.Length).ThenBy(o => o.Fragment, StringComparer.Ordinal)
            .Take(40).ToList();
        var shortestAny = options
            .OrderBy(o => o.Length).ThenBy(o => o.ForeignLines).ThenBy(o => o.Fragment, StringComparer.Ordinal)
            .Take(25).ToList();
        foreach (var option in zero.Concat(shortestAny).DistinctBy(o => o.Fragment))
        {
            if (!option.HasWildcard) continue;
            option.CrossSynthetic = !corpus.CrossLineSafe(scope, option.Fragment);
            option.CrossCaptured = CrossCaptured(corpus, scope, option.Fragment);
        }

        return options;
    }

    private static bool CrossCaptured(EngineCorpus corpus, string? scope, string fragment)
    {
        var className = scope is null ? null : corpus.FindItemClass(scope)?.Name;
        Regex regex;
        try
        {
            regex = new Regex(fragment,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline,
                TimeSpan.FromMilliseconds(200));
        }
        catch (ArgumentException)
        {
            return false;
        }
        foreach (var item in corpus.SafetyItems)
        {
            if (className is not null &&
                !string.Equals(item.ClassName, className, StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var section in item.Sections)
            {
                var text = string.Join("\n", section);
                try
                {
                    foreach (Match match in regex.Matches(text))
                        if (match.Value.Contains('\n')) return true;
                }
                catch (RegexMatchTimeoutException)
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static List<Chunk> WordChunks(string text)
    {
        var result = new List<Chunk>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var words = Regex.Matches(text, @"[A-Za-z]+(?:'[A-Za-z]+)?")
            .Select(m => (Start: m.Index, End: m.Index + m.Length))
            .ToList();
        void Add(string value, bool starts, bool ends)
        {
            if (value.Length == 0 || value.Length > 12 || !IsLiteral(value)) return;
            var key = value + "|" + starts + "|" + ends;
            if (seen.Add(key)) result.Add(new Chunk(value, starts, ends));
        }
        foreach (var (start, end) in words)
        {
            Add(text[start..end], true, true);
            for (var n = 1; n <= Math.Min(8, end - start); n++)
            {
                Add(text[start..(start + n)], true, start + n == end);
                Add(text[(end - n)..end], end - n == start, true);
            }
        }
        // Contiguous phrases of up to three words, preserving spaces.
        for (var i = 0; i < words.Count; i++)
        {
            for (var j = i; j < words.Count && j < i + 3; j++)
            {
                var value = text[words[i].Start..words[j].End];
                if (value.Length <= 12) Add(value, true, true);
            }
        }
        return result;
    }

    private static string LongestHint(params string[] parts) =>
        parts.OrderByDescending(x => x.Length).FirstOrDefault() ?? "";

    private static bool IsLiteral(string value)
    {
        if (value.Length == 0) return false;
        foreach (var c in value)
            if (!(char.IsAsciiLetter(c) || char.IsWhiteSpace(c) || c is '\'' or '-'))
                return false;
        return value.Trim().Length > 0;
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
        catch (JsonException)
        {
            return null;
        }
    }

    private readonly record struct ScopeResult(
        int TargetLines, int ForeignLines, string? Current, List<Option> Options);

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var dict = new Dictionary<string, string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--")) continue;
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
            {
                dict[args[i]] = args[i + 1];
                i++;
            }
            else dict[args[i]] = "1";
        }
        return dict;
    }

    private static string GetValue(Dictionary<string, string> opts, string key, string fallback) =>
        opts.TryGetValue(key, out var value) ? value : fallback;
}

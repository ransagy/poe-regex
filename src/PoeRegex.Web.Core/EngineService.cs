using System.Text.RegularExpressions;
using PoeRegex.Core;
using PoeRegex.Core.Compilation;
using PoeRegex.Core.Data;
using PoeRegex.Core.Explain;

namespace PoeRegex.Web.Core;

/// <summary>
/// In-process data + compile service for the PoE2 web app. Built once
/// (singleton) and shared: loads the embedded poe2 catalog, builds the
/// suffix-index corpus, and exposes classes, mod search, compile, explain,
/// and validate. No I/O, no network — WASM-safe.
/// </summary>
public sealed class EngineService
{
    private readonly EngineCorpus _corpus;
    private readonly PoeRegexCompiler _compiler;
    private readonly Explainer _explainer;
    private readonly ICompilerStrategy _strategy;
    private readonly Lazy<HashSet<string>> _playableClassIds;

    public EngineService()
    {
        var catalog = new EmbeddedCatalogSource().Load(GameTarget.PoE2);
        _corpus = new EngineCorpus(catalog);
        _strategy = PoeRegexCompiler.StrategyFor(GameTarget.PoE2);
        _compiler = new PoeRegexCompiler(_corpus, _strategy, allowFallback: false);
        _explainer = new Explainer(_corpus);
        _playableClassIds = new Lazy<HashSet<string>>(() =>
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var mod in _corpus.Mods)
                foreach (var cls in mod.Classes)
                    ids.Add(cls);
            return ids;
        });
    }

    public string DataVersion => _corpus.Catalog.DataVersion;

    /// <summary>Documented global unresolved reason for a catalog pattern.</summary>
    public string? GlobalUnresolvedReason(string pattern) =>
        _corpus.PrecomputedUnresolvedReason(pattern, null);

    public IReadOnlyList<ItemClassEntry> Classes => _corpus.Catalog.ItemClasses;

    /// <summary>Classes referenced by at least one mod (hides flasks, currency, gems...).</summary>
    public IEnumerable<ItemClassEntry> PlayableClasses() =>
        Classes.Where(c => _playableClassIds.Value.Contains(c.Id));

    /// <summary>
    /// Base types with no in-game presence yet (maintainer-confirmed), plus
    /// datamined leftovers — excluded from pickers, but the catalog still
    /// carries them. Not listed: `Warstaff`, whose poe2 export entry carries
    /// the Quarterstaves class (38 released quarterstaff bases).
    /// </summary>
    public static readonly HashSet<string> NotInGameClassIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "Dagger", "Claw", "Flail",
        "One Hand Sword", "One Hand Axe", "Two Hand Sword", "Two Hand Axe",
        "FishingRod", "TrapTool",
    };

    /// <summary>Playable classes actually in the game (picker scope).</summary>
    public IEnumerable<ItemClassEntry> InGameClasses() =>
        PlayableClasses().Where(c => !NotInGameClassIds.Contains(c.Id));

    /// <summary>Mods for one class, or the union across several (deduped by id, first wins).</summary>
    public IReadOnlyList<ModEntry> ModsForClasses(IEnumerable<string> classIds)
    {
        var lists = classIds.Select(ModsForClass).ToList();
        return lists.Count == 1 ? lists[0] : ModSearch.MergeMods(lists);
    }

    public IReadOnlyList<ModEntry> ModsForClass(string classId) =>
        _corpus.Mods.Where(m => m.Classes.Contains(classId, StringComparer.Ordinal)).ToList();

    /// <summary>Union of mods across every playable class (global scope).</summary>
    public IReadOnlyList<ModEntry> PlayableMods() =>
        ModSearch.MergeMods(PlayableClasses().Select(c => ModsForClass(c.Id)));

    /// <summary>
    /// Resolve a pasted `Item Class:` value to a catalog class id, matching
    /// the plural display name first (e.g. `Crossbows`), then the id.
    /// </summary>
    public string? ResolveClassId(string? itemClassText)
    {
        if (string.IsNullOrWhiteSpace(itemClassText)) return null;
        var t = itemClassText.Trim();
        return _corpus.Catalog.ItemClasses.FirstOrDefault(c =>
                c.Name.Equals(t, StringComparison.OrdinalIgnoreCase))?.Id
            ?? _corpus.Catalog.ItemClasses.FirstOrDefault(c =>
                c.Id.Equals(t, StringComparison.OrdinalIgnoreCase))?.Id;
    }

    /// <summary>A catalog mapping outcome: mod ids plus whether the match is approximate.</summary>
    public sealed record ModMapping(IReadOnlyList<string> ModIds, bool Approximate);

    /// <summary>
    /// Map a canonical pasted mod line to catalog mod ids: digit-normalize
    /// and look up the pattern, scope to the item class when known, then keep
    /// candidates whose value span covers every displayed roll and whose
    /// catalog range line repeats the paste's bracketed `(lo-hi)` pairs.
    /// Exact match first; when empty, a tight spaceless fallback covers
    /// internal/external spacing differences between game text and catalog
    /// lines (e.g. `Light Radius` vs `LightRadius`), flagged approximate.
    /// When still empty, a word-stem fallback covers wording differences from
    /// the same renderer behavior (e.g. `Leeches 5.1% ... as Mana` vs
    /// `ManaLeech 6% ...`), also flagged approximate.
    /// Lines from mod generations absent from the catalog (implicits) map nowhere.
    /// </summary>
    public ModMapping MapLineToMods(
        string? classId, string canonicalLine, IReadOnlyList<double> rolls,
        IReadOnlyList<(double Lo, double Hi)>? ranges = null)
    {
        var rs = ranges ?? [];
        var pattern = CatalogLineNormalizer.Normalize(canonicalLine);
        var exact = FilterMappingCandidates(classId, pattern, rolls, rs);
        if (exact.Count > 0) return new ModMapping(exact, Approximate: false);
        var compact = Compact(pattern);
        var spaceless = FilterCandidates(_corpus.Mods
            .Where(m => m.StatLines.Any(s => Compact(s.Pattern) == compact))
            .Where(m => classId is null || m.Classes.Contains(classId, StringComparer.Ordinal))
            .Select(m => m.Id)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal),
            rolls, rs, id => _corpus.FindMod(id)?.StatLines
                .FirstOrDefault(s => Compact(s.Pattern) == compact));
        if (spaceless.Count > 0) return new ModMapping(spaceless, Approximate: true);
        var stems = WordStemGroups(canonicalLine);
        var wordy = FilterCandidates(_corpus.Mods
            .Where(m => classId is null || m.Classes.Contains(classId, StringComparer.Ordinal))
            .Where(m => m.StatLines.Any(s => StemsMatch(stems, Compact(s.Pattern))))
            .Select(m => m.Id)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal),
            rolls, rs, id => _corpus.FindMod(id)?.StatLines
                .FirstOrDefault(s => StemsMatch(stems, Compact(s.Pattern))));
        return new ModMapping(wordy, Approximate: true);
    }

    private static string Compact(string pattern) =>
        pattern.Replace(" ", "").ToLowerInvariant();

    /// <summary>Lowercase letter-words of length ≥ 4, each with plural/verb-s alternatives.</summary>
    private static IReadOnlyList<string[]> WordStemGroups(string line)
    {
        var words = Regex.Matches(line.ToLowerInvariant(), @"[a-z]{4,}")
            .Select(m => m.Value).Distinct().ToList();
        var out_ = new List<string[]>();
        foreach (var w in words)
        {
            var alts = new List<string> { w };
            if (w.Length > 5 && w.EndsWith('s')) alts.Add(w[..^1]);
            if (w.Length > 6 && w.EndsWith("es")) alts.Add(w[..^2]);
            out_.Add(alts.ToArray());
        }
        return out_;
    }

    private static bool StemsMatch(IReadOnlyList<string[]> groups, string compactPattern) =>
        groups.Count > 0 && groups.All(g =>
            g.Any(a => compactPattern.Contains(a, StringComparison.Ordinal)));

    private IReadOnlyList<string> FilterMappingCandidates(
        string? classId, string pattern, IReadOnlyList<double> rolls,
        IReadOnlyList<(double Lo, double Hi)> ranges)
    {
        var candidates = _corpus.ModsWithPattern(pattern);
        if (classId is not null)
            candidates = candidates
                .Where(m => m.Classes.Contains(classId, StringComparer.Ordinal)).ToList();
        var ids = candidates.Select(m => m.Id)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal);
        return FilterCandidates(ids, rolls, ranges, id => _corpus.FindMod(id)?.StatLines
            .FirstOrDefault(s => s.Pattern == pattern));
    }

    /// <summary>
    /// Keep candidates whose value span covers every displayed roll, then —
    /// when the paste carries bracketed ranges — whose matched catalog line
    /// holds the same `lo-hi` pairs in order. Ranges only narrow: a candidate
    /// whose line has no comparable pairs counts as unrefuted, and when no
    /// candidate survives the pairs the roll-filtered list is returned
    /// unchanged (text matched across renderer quirks beats a hard rejection).
    /// </summary>
    private IReadOnlyList<string> FilterCandidates(
        IEnumerable<string> ids, IReadOnlyList<double> rolls,
        IReadOnlyList<(double Lo, double Hi)> ranges,
        Func<string, StatLineEntry?> matchingLine)
    {
        var list = ids.ToList();
        if (rolls.Count > 0)
            list = list.Where(id =>
            {
                var mod = _corpus.FindMod(id);
                if (mod is null) return false;
                var lo = mod.StatLines.Min(s => s.MinValue);
                var hi = mod.StatLines.Max(s => s.MaxValue);
                return rolls.All(r => r >= lo && r <= hi);
            }).ToList();
        if (ranges.Count == 0 || list.Count == 0) return list;
        var kept = new List<string>();
        foreach (var id in list)
        {
            var stat = matchingLine(id);
            if (stat is null) { kept.Add(id); continue; }
            var pairs = ParseRangePairs(stat.RangeLine);
            if (pairs.Count != ranges.Count) { kept.Add(id); continue; }
            if (PairsEqual(pairs, ranges)) kept.Add(id);
        }
        return kept.Count > 0 ? kept : list;
    }

    /// <summary>`lo–hi` pairs of a catalog range line, in order.</summary>
    private static IReadOnlyList<(double Lo, double Hi)> ParseRangePairs(string rangeLine)
    {
        var pairs = new List<(double Lo, double Hi)>();
        foreach (Match m in Regex.Matches(rangeLine,
                     @"(\d+(?:\.\d+)?)\s*[-\u2013\u2014]\s*(\d+(?:\.\d+)?)"))
        {
            if (double.TryParse(m.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var lo) &&
                double.TryParse(m.Groups[2].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var hi))
                pairs.Add((lo, hi));
        }
        return pairs;
    }

    private static bool PairsEqual(
        IReadOnlyList<(double Lo, double Hi)> a, IReadOnlyList<(double Lo, double Hi)> b) =>
        a.Count == b.Count && a.Zip(b).All(p => p.First.Lo == p.Second.Lo && p.First.Hi == p.Second.Hi);

    public ModEntry? FindMod(string id) => _corpus.FindMod(id);

    public CompileResult Compile(
        IEnumerable<ModifierRequirement> includes,
        IEnumerable<ModifierRequirement> excludes,
        string? itemClass = null,
        IEnumerable<string>? keywords = null,
        bool allowOverflow = false) =>
        _compiler.Compile(QuerySpecification.For(GameTarget.PoE2) with
        {
            ItemClass = itemClass,
            IncludedModifiers = includes.ToList(),
            ExcludedModifiers = excludes.ToList(),
            Keywords = keywords?.ToList() ?? [],
            AllowOverflow = allowOverflow,
        });

    /// <summary>
    /// Combine per-slot criteria into one stash paste (anchor + exclusion
    /// groups). Exclusions union globally; same-class slots merge.
    /// </summary>
    public MultiPasteResult Combine(
        IEnumerable<(string Label, string ClassId, IEnumerable<WantedMod> Includes)> slots,
        IEnumerable<string> excludes,
        bool allowOverflow = false) =>
        new PasteComposer(_corpus, _strategy, allowFallback: false, validateRecall: false).Compose(
            slots.Select(s => new PasteSlotCriterion(
                s.ClassId,
                s.Includes.SelectMany(m => m.Requirements()).ToList(),
                s.Label)).ToList(),
            excludes.Select(e => new ModifierRequirement(e)),
            allowOverflow);

    public ExplainResult Explain(string query, string? itemClass = null) =>
        _explainer.Explain(query, itemClass);

    /// <summary>Null when the query is grammatically valid for PoE2.</summary>
    public CompilerDiagnostic? Validate(string query) => _strategy.ValidateFragment(query);
}

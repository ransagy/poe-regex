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
        _compiler = new PoeRegexCompiler(_corpus, _strategy);
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

    public IReadOnlyList<ItemClassEntry> Classes => _corpus.Catalog.ItemClasses;

    /// <summary>Classes referenced by at least one mod (TUI parity: hides flasks, currency, gems...).</summary>
    public IEnumerable<ItemClassEntry> PlayableClasses() =>
        Classes.Where(c => _playableClassIds.Value.Contains(c.Id));

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
    /// candidates whose value span covers every displayed roll.
    /// Exact match first; when empty, a tight spaceless fallback covers
    /// internal/external spacing differences between game text and catalog
    /// lines (e.g. `Light Radius` vs `LightRadius`), flagged approximate.
    /// When still empty, a word-stem fallback covers wording differences from
    /// the same renderer behavior (e.g. `Leeches 5.1% ... as Mana` vs
    /// `ManaLeech 6% ...`), also flagged approximate.
    /// Lines from mod generations absent from the catalog (implicits) map nowhere.
    /// </summary>
    public ModMapping MapLineToMods(
        string? classId, string canonicalLine, IReadOnlyList<double> rolls)
    {
        var pattern = CatalogLineNormalizer.Normalize(canonicalLine);
        var exact = FilterMappingCandidates(classId, pattern, rolls);
        if (exact.Count > 0) return new ModMapping(exact, Approximate: false);
        var compact = Compact(pattern);
        var spaceless = FilterByRolls(_corpus.Mods
            .Where(m => m.StatLines.Any(s => Compact(s.Pattern) == compact))
            .Where(m => classId is null || m.Classes.Contains(classId, StringComparer.Ordinal))
            .Select(m => m.Id)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal), rolls);
        if (spaceless.Count > 0) return new ModMapping(spaceless, Approximate: true);
        var stems = WordStemGroups(canonicalLine);
        var wordy = FilterByRolls(_corpus.Mods
            .Where(m => classId is null || m.Classes.Contains(classId, StringComparer.Ordinal))
            .Where(m => m.StatLines.Any(s => StemsMatch(stems, Compact(s.Pattern))))
            .Select(m => m.Id)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal), rolls);
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
        string? classId, string pattern, IReadOnlyList<double> rolls)
    {
        var candidates = _corpus.ModsWithPattern(pattern);
        if (classId is not null)
            candidates = candidates
                .Where(m => m.Classes.Contains(classId, StringComparer.Ordinal)).ToList();
        var ids = candidates.Select(m => m.Id)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal).ToList();
        return FilterByRolls(ids, rolls);
    }

    private IReadOnlyList<string> FilterByRolls(IEnumerable<string> ids, IReadOnlyList<double> rolls)
    {
        var list = ids.ToList();
        if (rolls.Count == 0) return list;
        return list.Where(id =>
        {
            var mod = _corpus.FindMod(id);
            if (mod is null) return false;
            var lo = mod.StatLines.Min(s => s.MinValue);
            var hi = mod.StatLines.Max(s => s.MaxValue);
            return rolls.All(r => r >= lo && r <= hi);
        }).ToList();
    }

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

    public ExplainResult Explain(string query, string? itemClass = null) =>
        _explainer.Explain(query, itemClass);

    /// <summary>Null when the query is grammatically valid for PoE2.</summary>
    public CompilerDiagnostic? Validate(string query) => _strategy.ValidateFragment(query);
}

using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using PoeRegex.Core;
using PoeRegex.Core.Compilation;
using PoeRegex.Core.Data;
using PoeRegex.Core.Explain;
using PoeRegex.Core.Search;

namespace PoeRegex.Web.Core;

/// <summary>
/// In-process data + compile service for the web workbench (PoE2 and PoE1). Built once
/// (singleton) and shared: loads the embedded poe2 catalog, builds the
/// suffix-index corpus, and exposes classes, mod search, compile, explain,
/// and validate. No I/O, no network — WASM-safe.
/// </summary>
public sealed class EngineService
{
    private EngineCorpus _corpus = null!;
    private PoeRegexCompiler _compiler = null!;
    private CompactionEngine _compaction = null!;
    private Explainer _explainer = null!;
    private Lazy<HashSet<string>> _playableClassIds = null!;
    private readonly ConcurrentDictionary<(string Query, string? ItemClass), ExplainResult> _explainCache = new();
    private readonly ConcurrentDictionary<(string PickKey, string? ItemClass), bool> _minCapable = new();
    private readonly ConcurrentDictionary<(WantedMod Pick, string? Class), Lazy<IReadOnlyList<string>>> _pickTerms = new();
    private readonly ConcurrentDictionary<string, Lazy<string?>> _excludeFragments = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<(string Exclusion, string? Class), Lazy<string?>> _excludeClaims = new();
    private readonly ConcurrentDictionary<string, IReadOnlyList<ModEntry>> _modsByClass = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, IReadOnlyList<ModEntry>> _modsByClassSet = new(StringComparer.Ordinal);
    private ConcurrentDictionary<string, Lazy<CompileResult>> _compileCache = new(StringComparer.Ordinal);
    private ConcurrentDictionary<string, Lazy<MultiPasteResult>> _combineCache = new(StringComparer.Ordinal);
    private ConcurrentDictionary<(WantedMod Pick, string? Class), Lazy<IReadOnlyList<ValueInterval>>> _pickIntervals = new();
    private ConcurrentDictionary<(string ModId, string? Class), Lazy<ModFamily?>> _familyByMod = new();
    private PasteComposer _pasteComposer = null!;
    private volatile IReadOnlyList<ItemClassEntry>? _playableClasses;
    private volatile IReadOnlyList<ItemClassEntry>? _inGameClasses;
    private volatile IReadOnlyList<ModEntry>? _playableMods;

    public EngineService() : this(new GameData(new EmbeddedCatalogSource().Load()))
    {
    }

    public EngineService(GameData data)
    {
        LoadGame(data);
    }

    public string GameId => _corpus.Catalog.Game;

    /// <summary>The loaded game's query dialect.</summary>
    public QuerySyntax Syntax => _corpus.Syntax;

    /// <summary>Replace the loaded game in place; the web shell holds one service.</summary>
    public void LoadGame(GameData data)
    {
        _corpus = new EngineCorpus(data.Catalog, data.Fragments, data.Vendor, data.Safety);
        _compaction = new CompactionEngine(_corpus);
        _compiler = new PoeRegexCompiler(_corpus, _compaction);
        _explainer = new Explainer(_corpus);
        _pasteComposer = new PasteComposer(_corpus, _compaction, validateRecall: false);
        _compileCache = new(StringComparer.Ordinal);
        _combineCache = new(StringComparer.Ordinal);
        _pickIntervals = new();
        _familyByMod = new();
        _playableClassIds = new Lazy<HashSet<string>>(() =>
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var mod in _corpus.Mods)
                foreach (var cls in mod.Classes)
                    ids.Add(cls);
            return ids;
        });
        _explainCache.Clear();
        _minCapable.Clear();
        _pickTerms.Clear();
        _excludeFragments.Clear();
        _excludeClaims.Clear();
        _modsByClass.Clear();
        _modsByClassSet.Clear();
        _playableClasses = null;
        _inGameClasses = null;
        _playableMods = null;
        _warmedValueScopes.Clear();
        _valuePathReady = false;
        _vendorLoadTask = null;
    }

    public string DataVersion => _corpus.Catalog.DataVersion;

    public bool DataReady => _corpus.HasPrecomputedFragments;

    public string? FragmentDataVersion => _corpus.PrecomputedDataVersion;

    public string TargetModel => _corpus.PrecomputedTargetModel ?? FragmentTable.AdvancedRanges;

    /// <summary>Documented global unresolved reason for a catalog pattern.</summary>
    public string? GlobalUnresolvedReason(string pattern) =>
        _corpus.PrecomputedUnresolvedReason(pattern, null);

    public IReadOnlyList<ItemClassEntry> Classes => _corpus.Catalog.ItemClasses;

    public IReadOnlyList<ModEntry> ModsForPattern(string pattern) =>
        _corpus.ModsWithPattern(pattern);

    public string ClassNamePrefix(string classId) => _corpus.ClassNamePrefix(classId);

    /// <summary>Regex fragment for a single-class query anchor (may use wildcards).</summary>
    public string ClassNameAnchor(string classId) => _corpus.ClassNameAnchor(classId);

    public IEnumerable<(string Pattern, string Fragment, string? Class)> PrecomputedFragments() =>
        _corpus.PrecomputedFragments();

    /// <summary>Classes referenced by at least one mod (hides flasks, currency, gems...).</summary>
    public IReadOnlyList<ItemClassEntry> PlayableClasses() =>
        _playableClasses ??= Classes
            .Where(c => _playableClassIds.Value.Contains(c.Id))
            .ToList();

    /// <summary>
    /// Base types with no in-game presence yet (maintainer-confirmed), plus
    /// datamined leftovers — excluded from pickers, but the catalog still
    /// carries them. Not listed: `Warstaff`, whose poe2 export entry carries
    /// the Quarterstaves class (38 released quarterstaff bases).
    /// </summary>
    public static IReadOnlySet<string> NotInGameClassIds => GameItemClasses.NotInGame;

    /// <summary>Playable classes actually in the game (picker scope).</summary>
    public IReadOnlyList<ItemClassEntry> InGameClasses() =>
        _inGameClasses ??= PlayableClasses()
            .Where(c => _corpus.IsInGameClass(c.Id))
            .ToList();

    /// <summary>Mods for one class, or the union across several (deduped by id, first wins).</summary>
    public IReadOnlyList<ModEntry> ModsForClasses(IEnumerable<string> classIds)
    {
        var ids = classIds.Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();
        if (ids.Count == 0) return [];
        if (ids.Count == 1) return ModsForClass(ids[0]);
        return _modsByClassSet.GetOrAdd(
            string.Join('\u0001', ids),
            _ => ModSearch.MergeMods(ids.Select(ModsForClass).ToList()));
    }

    public IReadOnlyList<ModEntry> ModsForClass(string classId) =>
        _modsByClass.GetOrAdd(
            classId,
            id => _corpus.Mods
                .Where(m => m.Classes.Contains(id, StringComparer.Ordinal))
                .ToList());

    /// <summary>Union of mods across every in-game class (global scope).</summary>
    public IReadOnlyList<ModEntry> PlayableMods() =>
        _playableMods ??= ModSearch.MergeMods(
            InGameClasses().Select(c => ModsForClass(c.Id)));

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
        // Decimal stats use two placeholders in catalog patterns (0.17 ->
        // \x01.\x01), while the legacy normalizer collapses them to one.
        // Try the renderer's digit-run shape before falling back to fuzzy
        // matching so decimal lines stay exact.
        var digitPattern = CatalogLineNormalizer.NormalizeDigitRuns(canonicalLine);
        if (!string.Equals(digitPattern, pattern, StringComparison.Ordinal))
        {
            var exactDigits = FilterMappingCandidates(classId, digitPattern, rolls, rs);
            if (exactDigits.Count > 0) return new ModMapping(exactDigits, Approximate: false);
        }
        var compact = Compact(pattern);
        var spaceless = FilterCandidates(_corpus.Mods
            .Where(m => m.Source is not ("augment" or "bonded"))
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
            .Where(m => m.Source is not ("augment" or "bonded"))
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
        var candidates = _corpus.ModsWithPattern(pattern)
            .Where(m => m.Source is not ("augment" or "bonded"))
            .ToList();
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
    /// Keep candidates whose range pairs match the advanced display first,
    /// then use the displayed rolls only to narrow further. Ranges are the
    /// authoritative advanced-text evidence: they survive reversed rolls
    /// (`10(15-10)`) and renderer min/max quirks. The displayed roll is
    /// rounded, so when ranges are present allow half a display unit of
    /// tolerance (`8` against a `7.9` catalog max). When no candidate
    /// survives a pass, keep the previous list: text matched across renderer
    /// quirks beats a hard rejection.
    /// </summary>
    private IReadOnlyList<string> FilterCandidates(
        IEnumerable<string> ids, IReadOnlyList<double> rolls,
        IReadOnlyList<(double Lo, double Hi)> ranges,
        Func<string, StatLineEntry?> matchingLine)
    {
        var list = ids.ToList();
        if (list.Count == 0) return list;

        if (ranges.Count > 0)
        {
            var kept = new List<string>();
            foreach (var id in list)
            {
                var stat = matchingLine(id);
                if (stat is null) { kept.Add(id); continue; }
                var pairs = ParseRangePairs(stat.RangeLine);
                if (pairs.Count != ranges.Count) { kept.Add(id); continue; }
                if (PairsEqual(pairs, ranges)) kept.Add(id);
            }
            if (kept.Count > 0) list = kept;
        }

        if (rolls.Count > 0)
        {
            var tolerance = ranges.Count > 0 ? 0.5 : 0;
            var rolled = list.Where(id =>
            {
                var mod = _corpus.FindMod(id);
                if (mod is null) return false;
                var lo = mod.StatLines.Min(s => s.MinValue);
                var hi = mod.StatLines.Max(s => s.MaxValue);
                return rolls.All(r => r >= lo - tolerance && r <= hi + tolerance);
            }).ToList();
            if (rolled.Count > 0) list = rolled;
        }
        return list;
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
        bool allowOverflow = false,
        IEnumerable<IEnumerable<ModifierRequirement>>? requiredGroups = null)
    {
        var included = includes.ToList();
        var excluded = excludes.ToList();
        var keywordList = keywords?.ToList() ?? [];
        var groups = requiredGroups?
            .Select(g => (IReadOnlyList<ModifierRequirement>)g.ToList()).ToList() ?? [];
        var key = CompileKey(included, excluded, keywordList, groups, itemClass, allowOverflow);
        return _compileCache.GetOrAdd(
            key,
            _ => new Lazy<CompileResult>(() => _compiler.Compile(new QuerySpecification
            {
                ItemClass = itemClass,
                IncludedModifiers = included,
                RequiredGroups = groups,
                ExcludedModifiers = excluded,
                Keywords = keywordList,
                AllowOverflow = allowOverflow,
            }))).Value;
    }

    private static string CompileKey(
        IReadOnlyList<ModifierRequirement> includes,
        IReadOnlyList<ModifierRequirement> excludes,
        IReadOnlyList<string> keywords,
        IReadOnlyList<IReadOnlyList<ModifierRequirement>> requiredGroups,
        string? itemClass,
        bool allowOverflow) =>
        (itemClass is null ? "\0" : "\u0001" + itemClass) + "\u0001" +
        (allowOverflow ? "1" : "0") + "\u0001" +
        string.Join("\u0003", includes.Select(RequirementKey)) + "\u0004" +
        string.Join("\u0003", excludes.Select(RequirementKey)) + "\u0004" +
        string.Join("\u0003", keywords) + "\u0004" +
        string.Join("\u0005", requiredGroups.Select(
            group => string.Join("\u0003", group.Select(RequirementKey))));

    private static string RequirementKey(ModifierRequirement requirement)
    {
        var min = requirement.MinValue?.ToString("R", System.Globalization.CultureInfo.InvariantCulture) ?? "";
        var max = requirement.MaxValue?.ToString("R", System.Globalization.CultureInfo.InvariantCulture) ?? "";
        var ids = requirement.RangeModIds is { Count: > 0 } rangeIds
            ? string.Join(",", rangeIds)
            : "";
        return (requirement.IsRegex ? "r" : "m") + "\u0002" +
               requirement.Value + "\u0002" + min + "\u0002" + max + "\u0002" + ids;
    }

    private static string PickKey(WantedMod pick)
    {
        var min = pick.MinValue?.ToString("R", System.Globalization.CultureInfo.InvariantCulture) ?? "";
        var max = pick.MaxValue?.ToString("R", System.Globalization.CultureInfo.InvariantCulture) ?? "";
        return pick.ModId + "\u0002" +
               string.Join(",", pick.Names ?? []) + "\u0002" +
               string.Join(",", pick.VariantModIds ?? []) + "\u0002" +
               min + "\u0002" + max + "\u0002" +
               string.Join(",", pick.RangeModIds ?? []);
    }

    /// <summary>Compiles picks the way a slot asks for them: one any-of block, or one block per pick when every pick is required.</summary>
    public CompileResult CompilePicks(
        IReadOnlyList<WantedMod> picks, IEnumerable<string> excludes, bool matchAll, string? itemClass,
        IEnumerable<string>? externalIncludes = null) =>
        Compile(
            matchAll ? [] : picks.SelectMany(m => m.Requirements()),
            excludes.Select(ExclusionRequirement),
            itemClass: itemClass,
            keywords: externalIncludes,
            allowOverflow: true,
            requiredGroups: matchAll ? picks.Select(m => m.Requirements()) : null);

    /// <summary>
    /// Workbench exclusions are usually catalog mod ids, but a guide import can
    /// carry a raw regex group (for example `parry|bow|scept`). The marker
    /// keeps the value round-trippable through state and lets the compiler emit
    /// the pattern verbatim instead of escaping it as a literal.
    /// </summary>
    private static ModifierRequirement ExclusionRequirement(string exclusion) =>
        RawExclusion.IsRaw(exclusion)
            ? new ModifierRequirement(RawExclusion.Decode(exclusion), IsRegex: true)
            : new ModifierRequirement(exclusion);

    /// <summary>
    /// The compiled fragment for each requirement of a pick, in compile order.
    /// The picker ruler uses this to split one any-of block into per-pick
    /// segments without re-running the whole compile pipeline.
    /// </summary>
    public IReadOnlyList<string> RequirementFragments(WantedMod pick, string? itemClass)
    {
        var fragments = new List<string>();
        foreach (var req in pick.Requirements())
        {
            if (req.IsRegex)
            {
                fragments.Add(req.Value);
                continue;
            }
            var mod = _corpus.FindMod(req.Value);
            string? fragment;
            if (mod is not null)
                fragment = _compaction.SelectFragment(req, itemClass);
            else
                fragment = Syntax.ValidateFragment(req.Value) is null
                    ? Syntax.EscapeLiteral(req.Value)
                    : null;
            if (fragment is not null) fragments.Add(fragment);
        }
        return fragments;
    }

    /// <summary>
    /// True when setting a minimum on this pick changes its compiled fragment.
    /// Name picks never carry a minimum, and a pick must have a digit in a
    /// single stat line for a threshold to be meaningful. The result is probed
    /// once per pick shape + class and cached: the picker hides the min input
    /// when it would be silently ignored (for example a resistance family whose
    /// precomputed fragment already covers every roll).
    /// </summary>
    public bool CanApplyMin(WantedMod pick, string? itemClass)
    {
        if (!MayAcceptMin(pick)) return false;
        var key = FragmentTable.MinCapableKey(pick.ModId, pick.VariantModIds);
        if (_corpus.IsPrecomputedMinCapable(itemClass, key)) return true;
        return _minCapable.GetOrAdd(
            (key, itemClass),
            _ => MinCapability.Probe(_compiler, FindMod, pick, itemClass));
    }

    /// <summary>
    /// Exact capability from the shipped table, or null when the key is not
    /// present (stale/older table) so the caller can fall back to the probe.
    /// Name picks are known-false without consulting the table.
    /// </summary>
    public bool? PrecomputedMinCapability(WantedMod pick, string? itemClass)
    {
        if (!MayAcceptMin(pick)) return false;
        var key = FragmentTable.MinCapableKey(pick.ModId, pick.VariantModIds);
        return _corpus.IsPrecomputedMinCapable(itemClass, key) ? true : null;
    }

    /// <summary>
    /// Cheap catalog-only gate: plain pick, one stat line, a value to
    /// threshold, and every family variant sharing that shape. The cached
    /// fragment probe in <see cref="CanApplyMin"/> then decides whether the
    /// minimum actually changes the compiled text.
    /// </summary>
    public bool MayAcceptMin(WantedMod pick) =>
        MinCapability.MayAcceptMin(pick, FindMod);

    /// <summary>
    /// Warm the lazy real-line corpus used by value thresholds, so the first
    /// min entry does not pay for it. Called once after the first render (and
    /// when the editing scope changes), never at startup.
    /// </summary>
    public void PrewarmValuePath(string? itemClass)
    {
        if (itemClass is null) _corpus.RealLinesGlobal();
        else _corpus.RealLinesForClass(itemClass);
        _warmedValueScopes[itemClass ?? "global"] = 1;
        _valuePathReady = true;
    }

    private volatile bool _valuePathReady;
    private readonly ConcurrentDictionary<string, byte> _warmedValueScopes = new(StringComparer.Ordinal);
    private Task<VendorCorpusFile?>? _vendorLoadTask;

    /// <summary>True once the value corpus is loaded.</summary>
    public bool ValuePathReady => _valuePathReady;

    /// <summary>True when this exact scope's real lines are already built.</summary>
    public bool IsValueScopeReady(string? itemClass) =>
        _valuePathReady && _warmedValueScopes.ContainsKey(itemClass ?? "global");

    /// <summary>
    /// Browser warm-up: load the vendor corpus through the yielding stream,
    /// then build the requested scope's real lines. Safe to call repeatedly;
    /// the corpus load is shared and the line build is a cheap cached lookup.
    /// </summary>
    public async Task PrewarmValuePathAsync(string? itemClass)
    {
        // The embedded streaming vendor corpus is PoE2's. PoE1 already owns
        // its explicit corpus on the engine instance.
        if (GameId == "poe2")
        {
            _vendorLoadTask ??= VendorCorpus.ForCurrentGameAsync();
            await _vendorLoadTask.ConfigureAwait(false);
        }
        if (itemClass is null)
            await _corpus.PrewarmGlobalLinesAsync().ConfigureAwait(false);
        else
        {
            await _corpus.PrewarmVendorByClassAsync().ConfigureAwait(false);
            _corpus.RealLinesForClass(itemClass);
        }
        _warmedValueScopes[itemClass ?? "global"] = 1;
        _valuePathReady = true;
    }

    /// <summary>Fragment one pick compiles to on its own, cached across models.</summary>
    public string? PickFragment(WantedMod pick, string? cls) =>
        PickTerms(pick, cls).FirstOrDefault();

    private IReadOnlyList<string> PickTerms(WantedMod pick, string? cls) =>
        _pickTerms.GetOrAdd(
            (pick, cls),
            key => new Lazy<IReadOnlyList<string>>(() => ComputePickTerms(key.Pick, key.Class))).Value;

    private IReadOnlyList<string> ComputePickTerms(WantedMod pick, string? cls)
    {
        var result = Compile(pick.Requirements(), [], itemClass: cls, allowOverflow: true);
        var terms = Explainer.Tokenize(result.Query, Syntax)
            .Where(token => !token.Excluded)
            .Select(token => token.Phrase)
            .ToList();
        if (cls is not null && terms.Count > 0) terms.RemoveAt(0);
        return terms;
    }

    public string? ExcludeFragment(string exc) =>
        RawExclusion.IsRaw(exc)
            ? RawExclusion.Decode(exc)
            : _excludeFragments.GetOrAdd(
                exc,
                key => new Lazy<string?>(() => ComputeExcludeFragment(key))).Value;

    /// <summary>
    /// Canonical claim identity for an included pick in a scope: every
    /// positive quoted phrase, without the class anchor. Two mods/tiers with
    /// the same claim key compile to the same search and must share one
    /// interactive claim.
    /// </summary>
    public string? PickClaimKey(WantedMod pick, string? cls)
    {
        var terms = PickTerms(pick, cls);
        return terms.Count == 0 ? null : string.Join("\u0001", terms);
    }

    /// <summary>
    /// The discrete value spans a pick actually allows. This is the canonical
    /// interval view the picker and labels use; unlike Min/Max it keeps gaps
    /// between non-consecutive tiers.
    /// </summary>
    public IReadOnlyList<ValueInterval> PickIntervals(WantedMod pick, string? cls)
    {
        var cached = _pickIntervals.GetOrAdd(
            (pick, cls),
            key => new Lazy<IReadOnlyList<ValueInterval>>(() =>
            {
                if (key.Pick.IsNamePick || key.Pick.IsRawText) return [];
                var constraints = ConstraintCanonicalizer.Canonicalize(
                    key.Pick.Requirements().ToList(), key.Class, _corpus);
                return ConstraintCanonicalizer.MergeIntervals(
                    constraints.SelectMany(constraint => constraint.Intervals));
            }));
        return cached.Value;
    }

    /// <summary>The family containing this pick's anchor in the scope, if any.</summary>
    public ModFamily? FamilyFor(WantedMod pick, string? cls) =>
        cls is null
            ? null
            : _familyByMod.GetOrAdd((pick.ModId, cls), key =>
                new Lazy<ModFamily?>(() =>
                {
                    var mod = FindMod(key.ModId);
                    if (mod is null) return null;
                    return ModFamilies.CachedGroup(ModsForClass(key.Class!))
                        .FirstOrDefault(family => family.MemberIds.Contains(mod.Id));
                })).Value;

    /// <summary>Numeric span of a plain/family/tier-range pick, or null when it has no real range.</summary>
    public sealed record NumericSpan(double Min, double Max, int Precision)
    {
        public bool IsRanged => Max > Min;
    }

    /// <summary>
    /// The catalog roll span a pick can filter. A pick with a single value or
    /// no number returns null, so the workbench shows neither min nor max;
    /// a fixed-tier family that covers several values still gets a span.
    /// </summary>
    public NumericSpan? NumericRange(WantedMod pick, string? itemClass)
    {
        if (pick.IsNamePick || pick.IsRawText) return null;
        var ids = pick.RangeModIds is { Count: > 0 } rangeIds
            ? rangeIds
            : pick.VariantModIds is { Count: > 0 } variantIds
                ? variantIds
                : [pick.ModId];
        var mods = ids.Select(FindMod).Where(m => m is not null).ToList();
        if (mods.Count == 0) return null;
        var lines = mods.SelectMany(m => m!.StatLines).ToList();
        if (lines.Count == 0) return null;

        // The gate is the pick's value span, not whether each tier can roll
        // inside itself. A fixed-tier family such as movement speed still
        // covers 10/15/…/35, so it takes a min/max; a single fixed value
        // (+1 skills, one exact tier) has nothing to narrow.
        var values = lines
            .Where(line => line.MaxValue >= line.MinValue &&
                           (line.MinValue != 0 || line.MaxValue != 0))
            .SelectMany(line => new[] { line.MinValue, line.MaxValue })
            .Distinct()
            .ToList();
        if (values.Count <= 1) return null;
        var min = values.Min();
        var max = values.Max();
        if (max <= min) return null;
        var precision = lines.Max(line => Math.Max(
            PrecisionOf(line.MinValue), PrecisionOf(line.MaxValue)));
        return new NumericSpan(min, max, precision);
    }

    private static int PrecisionOf(double value)
    {
        var text = value.ToString("0.############", System.Globalization.CultureInfo.InvariantCulture);
        var dot = text.IndexOf('.');
        return dot < 0 ? 0 : text.Length - dot - 1;
    }

    /// <summary>
    /// Tier-up and multi-value exact tiers are range-first when a safe range
    /// exists; a single-value family keeps the shorter affix-name form.
    /// Returns null when the caller should use its fallback
    /// (desecrated/implicit/union tiers).
    /// </summary>
    public WantedMod? BestTierPick(ModFamily fam, ModTier tier, bool up, string? cls)
    {
        if (fam.Source is "desecrated" or "implicit" or "corrupted" or "augment" or "bonded")
            return null;
        if (fam.IsUnion || tier.Name.Length == 0) return null;
        var rangePick = TierRangePick(fam, tier, up);
        if (rangePick is null) return null;
        var rangeTerms = PickTerms(rangePick, cls);
        if (rangeTerms.Count == 0) return null;
        // Multi-value families are range-first for exact and up tiers: the
        // representation is predictable, min/max is available, and the
        // exact pick matches the "or better" form. A single-value family
        // (+1 Skills, one fixed tier) keeps the shorter name form.
        if (up || HasMultipleValues(fam)) return rangePick;
        var namePick = new WantedMod(tier.Anchor.Id, [tier.Name]);
        var nameTerms = PickTerms(namePick, cls);
        if (nameTerms.Count == 0) return rangePick;
        var rangeCost = string.Join("|", rangeTerms).Length;
        var nameCost = string.Join("|", nameTerms).Length;
        return rangeCost <= nameCost ? rangePick : namePick;
    }

    /// <summary>
    /// True when the family can produce more than one numeric value. Such a
    /// family uses the range form even for an exact tier, so individual
    /// movement-speed/life tiers match the "or better" representation.
    /// </summary>
    private static bool HasMultipleValues(ModFamily fam)
    {
        var values = new HashSet<double>();
        foreach (var tier in fam.Tiers)
        {
            if (tier.ValueRange is not { } range) continue;
            if (range.Max > range.Min) return true;
            if (range.Min != 0) values.Add(range.Min);
        }
        return values.Count > 1;
    }

    /// <summary>The one combined range pick for a tier action.</summary>
    public WantedMod? TierRangePick(ModFamily fam, ModTier tier, bool up)
    {
        var covered = up
            ? fam.Tiers.Where(t => t.RequiredLevel >= tier.RequiredLevel).ToList()
            : [tier];
        var ids = covered
            .SelectMany(t => t.Members)
            .Select(m => m.Id)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
        if (ids.Count == 0) return null;
        // A hybrid tier has more than one stat line; a single combined
        // family+range fragment cannot represent it, so keep the affix-name
        // form instead of claiming the whole family's search.
        if (covered.Any(t => t.Members.Any(m => m.StatLines.Count != 1)))
            return null;
        var lines = covered
            .SelectMany(t => t.Members)
            .SelectMany(m => m.StatLines)
            .ToList();
        if (lines.Count == 0 || lines.Any(line => line.MaxValue < line.MinValue)) return null;
        var min = lines.Min(line => line.MinValue);
        var max = lines.Max(line => line.MaxValue);
        return new WantedMod(
            tier.Anchor.Id,
            MinValue: min,
            MaxValue: max,
            RangeModIds: ids);
    }

    /// <summary>True when a tier action has a safe combined range fragment.</summary>
    public bool TierRangeAvailable(ModFamily fam, ModTier tier, bool up, string? cls)
    {
        var pick = TierRangePick(fam, tier, up);
        return pick is not null && PickFragment(pick, cls) is not null;
    }

    /// <summary>Explicit pick-row text for a tier range, e.g. "20–35% increased Movement Speed".</summary>
    public string? TierRangeLabel(
        WantedMod pick, ModTier tier, string? cls, ModFamily? family = null)
    {
        if (!pick.IsTierRange) return null;
        var span = NumericRange(pick, cls);
        var min = pick.MinValue ?? span?.Min ?? tier.ValueRange?.Min;
        var max = pick.MaxValue ?? span?.Max ?? tier.ValueRange?.Max;
        if (min is null || max is null) return null;
        var precision = span?.Precision ?? tier.Precision;
        var line = tier.DisplayLines.FirstOrDefault() ?? "";

        // A dash is only honest when the selected tiers are consecutive in
        // the family. A 15+25 pick keeps its gap and lists the values.
        family ??= FamilyFor(pick, cls);
        if (family is not null && pick.RangeModIds is { Count: > 0 } ids)
        {
            var indices = family.Tiers
                .Select((familyTier, index) => (familyTier, index))
                .Where(x => ids.Any(id => x.familyTier.MemberIds.Contains(id)))
                .Select(x => x.index)
                .OrderBy(index => index)
                .ToList();
            if (indices.Count > 1 &&
                indices[^1] - indices[0] + 1 == indices.Count)
                return ModFamilies.RangeLabel(line, min.Value, max.Value, precision);
        }

        var intervals = PickIntervals(pick, cls);
        if (intervals.Count > 1)
            return ModFamilies.IntervalLabel(line, intervals, precision);
        return ModFamilies.RangeLabel(line, min.Value, max.Value, precision);
    }

    /// <summary>
    /// The range covered by a tier-or-better name set, used as a compact
    /// equivalence hint in the pick row. Computed from tier metadata, so it
    /// never compiles a fragment.
    /// </summary>
    public string? TierCoveredRangeLabel(ModFamily fam, ModTier tier)
    {
        if (tier.Name.Length == 0) return null;
        if (tier.DisplayLines.Count != 1) return null;
        var line = tier.DisplayLines[0];
        // Only a single numeric span can be replaced unambiguously; a hybrid
        // "Adds X to Y" line would produce a misleading equivalence.
        if (System.Text.RegularExpressions.Regex.Matches(
                line, @"\d+(?:\.\d+)?(?:\s*[–-]\s*\d+(?:\.\d+)?)?").Count != 1)
            return null;
        var covered = fam.Tiers
            .Where(t => t.RequiredLevel >= tier.RequiredLevel)
            .ToList();
        if (covered.Count <= 1) return null;
        // Only hint when every covered tier has the same stat shape; a
        // singular/plural or hybrid variant would make one line misleading.
        var shape = tier.ShapeLines.FirstOrDefault();
        if (shape is null || covered.Any(t =>
                t.ShapeLines.Count != 1 ||
                !string.Equals(t.ShapeLines[0], shape, StringComparison.Ordinal)))
            return null;
        if (covered.Any(t => t.ValueRange is null)) return null;
        var min = covered.Min(t => t.ValueRange!.Value.Min);
        var max = covered.Max(t => t.ValueRange!.Value.Max);
        if (max <= min) return null;
        var precision = covered.Max(t => t.Precision);
        var indices = fam.Tiers
            .Select((familyTier, index) => (familyTier, index))
            .Where(x => covered.Contains(x.familyTier))
            .Select(x => x.index)
            .OrderBy(index => index)
            .ToList();
        if (indices.Count > 1 &&
            indices[^1] - indices[0] + 1 != indices.Count)
        {
            var intervals = covered
                .Where(t => t.ValueRange is not null)
                .Select(t => new ValueInterval(
                    t.ValueRange!.Value.Min, t.ValueRange.Value.Max))
                .ToList();
            return ModFamilies.IntervalLabel(line, intervals, precision);
        }
        return ModFamilies.RangeLabel(line, min, max, precision);
    }

    /// <summary>Canonical claim identity for an exclusion in a scope.</summary>
    public string? ExcludeClaimKey(string exc, string? cls) =>
        _excludeClaims.GetOrAdd(
            (exc, cls),
            key => new Lazy<string?>(() => ComputeExcludeClaimKey(key.Exclusion, key.Class))).Value;

    private string? ComputeExcludeClaimKey(string exc, string? cls)
    {
        var result = Compile(
            [], [ExclusionRequirement(exc)], itemClass: cls, allowOverflow: true);
        var terms = Explainer.Tokenize(result.Query, Syntax)
            .Where(token => token.Excluded)
            .Select(token => token.Phrase)
            .ToList();
        return terms.Count == 0 ? null : string.Join("\u0001", terms);
    }

    private string? ComputeExcludeFragment(string exc)
    {
        var result = Compile([], [new ModifierRequirement(exc)], allowOverflow: true);
        return Explainer.Tokenize(result.Query, Syntax)
            .Where(t => t.Excluded)
            .Select(t => t.Phrase)
            .FirstOrDefault();
    }

    /// <summary>
    /// Combine per-slot criteria into one stash paste (anchor + exclusion
    /// groups). Exclusions union globally; same-class slots merge.
    /// </summary>
    public MultiPasteResult Combine(
        IEnumerable<(string Label, string ClassId, IEnumerable<WantedMod> Includes)> slots,
        IEnumerable<string> excludes,
        bool allowOverflow = false,
        IEnumerable<string>? externalIncludes = null) =>
        Combine(slots.Select(s => (s.Label, s.ClassId, s.Includes, false)),
            excludes, allowOverflow, externalIncludes);

    /// <summary>As above; a MatchAll slot requires every pick, each as its own group.</summary>
    public MultiPasteResult Combine(
        IEnumerable<(string Label, string ClassId, IEnumerable<WantedMod> Includes, bool MatchAll)> slots,
        IEnumerable<string> excludes,
        bool allowOverflow = false,
        IEnumerable<string>? externalIncludes = null)
    {
        var slotList = slots.Select(s => (
            s.Label, s.ClassId, Includes: s.Includes.ToList(), s.MatchAll)).ToList();
        var excluded = excludes.ToList();
        var keywords = externalIncludes?.ToList() ?? [];
        var key = CombineKey(slotList, excluded, keywords, allowOverflow);
        return _combineCache.GetOrAdd(
            key,
            _ => new Lazy<MultiPasteResult>(() => _pasteComposer.Compose(
                slotList.Select(s => s.MatchAll && s.Includes.Count > 0
                    ? new PasteSlotCriterion(
                        s.ClassId, [], s.Label,
                        s.Includes.Select(m => (IReadOnlyList<ModifierRequirement>)m.Requirements().ToList()).ToList())
                    : new PasteSlotCriterion(
                        s.ClassId,
                        s.Includes.SelectMany(m => m.Requirements()).ToList(),
                        s.Label)).ToList(),
                excluded.Select(ExclusionRequirement),
                allowOverflow,
                keywords))).Value;
    }

    private static string CombineKey(
        IReadOnlyList<(string Label, string ClassId, List<WantedMod> Includes, bool MatchAll)> slots,
        IReadOnlyList<string> excludes,
        IReadOnlyList<string> keywords,
        bool allowOverflow) =>
        (allowOverflow ? "1" : "0") + "\u0001" +
        string.Join("\u0004", slots.Select(slot =>
            slot.Label + "\u0002" + slot.ClassId + "\u0002" + (slot.MatchAll ? "1" : "0") +
            "\u0002" + string.Join("\u0003", slot.Includes.Select(PickKey)))) +
        "\u0005" + string.Join("\u0003", excludes) +
        "\u0005" + string.Join("\u0003", keywords);

    /// <summary>
    /// Explain is the one intentionally expensive action. Results are cached
    /// by query + class so reopening a panel is instant, and <see cref="ExplainAsync"/>
    /// yields once so the loading state can paint before the corpus scan.
    /// </summary>
    public ExplainResult Explain(string query, string? itemClass = null) =>
        _explainCache.GetOrAdd((query, itemClass), key =>
            _explainer.Explain(key.Query, key.ItemClass));

    public async Task<ExplainResult> ExplainAsync(string query, string? itemClass = null)
    {
        await Task.Yield();
        return Explain(query, itemClass);
    }

    /// <summary>
    /// Warm the lazy per-class corpus indexes without producing an explanation.
    /// Intended for an explicit panel-open/idle path, not for app startup.
    /// </summary>
    public void PrewarmExplain(string? itemClass = null)
    {
        _corpus.ExplainLines(itemClass);
    }

    /// <summary>Null when the query is grammatically valid for the loaded game.</summary>
    public CompilerDiagnostic? Validate(string query) => Syntax.ValidateFragment(query);
}

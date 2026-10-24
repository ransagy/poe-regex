using System.Collections.Concurrent;
using PoeRegex.Core.Data;
using PoeRegex.Core.Search;

namespace PoeRegex.Core.Compilation;

/// <summary>
/// In-memory view of a catalog with per-class suffix indexes, used for
/// fingerprint lookup and the false-positive oracle.
/// </summary>
public sealed class EngineCorpus
{
    /// <summary>Prefix marking safety-item-only patterns (names/flavor/base).</summary>
    public const char SafetyPatternMarker = '\u0003';
    private readonly Catalog _catalog;
    private readonly Lazy<Dictionary<string, ClassIndex>> _byClass;
    private readonly Dictionary<string, ModEntry> _modsById;
    private readonly Lazy<IReadOnlyList<SafetyItem>> _safetyItems;
    private readonly Lazy<IReadOnlyList<VendorLine>> _vendorLines;
    private readonly Lazy<HashSet<string>> _vendorNormalized;
    private readonly Lazy<HashSet<string>> _vendorPatterns;
    private readonly Lazy<Dictionary<string, IReadOnlyList<SearchSection>>> _sectionsByClass;
    private readonly ConcurrentDictionary<(string Scope, string Fragment), bool> _crossLineCache = new();
    private readonly FragmentTable? _fragments;
    private HashSet<string>? _knownPatterns;
    private readonly object _knownLock = new();
    private UnionIndex? _union;
    private Dictionary<string, List<ModEntry>>? _modsByPattern;

    private sealed class ClassIndex
    {
        public required SuffixIndex Index { get; init; }
        /// <summary>Case-insensitive index: fingerprints are unique under case-folded matching.</summary>
        public required SuffixIndex FoldIndex { get; init; }
        /// <summary>Corpus line index of the "Item Class: X" line.</summary>
        public required int ClassLineIndex { get; init; }
        /// <summary>pattern -> corpus line index.</summary>
        public required Dictionary<string, int> PatternToIndex { get; init; }
        /// <summary>Patterns present only because of the captured-item safety lines.</summary>
        public required HashSet<string> SafetyOnlyPatterns { get; init; }
        /// <summary>
        /// All real (un-normalized) copy-text lines on this class, each tagged
        /// with its normalized pattern. Used by the regex-based oracle so value
        /// thresholds validate against every actual roll.
        /// </summary>
        public required List<(string Pattern, string Real)> AllRealLines { get; init; }
    }

    /// <summary>
    /// One search section of an item: base/name/property lines, implicit mods,
    /// or explicit mods. The game's search treats each section as a contiguous
    /// text; <c>.</c> may cross line breaks inside a section but not between
    /// sections (the observed `Charm.+Thorns` boundary).
    /// </summary>
    private sealed record SearchSection(string Kind, string Text, string ReverseText, IReadOnlySet<string> Patterns);

    /// <summary>Union corpus over every class's mod lines, for global (no-class) fingerprints.</summary>
    private sealed class UnionIndex
    {
        public required SuffixIndex Index { get; init; }
        public required SuffixIndex FoldIndex { get; init; }
        /// <summary>pattern -> corpus line index.</summary>
        public required Dictionary<string, int> PatternToIndex { get; init; }
        public required List<(string Pattern, string Real)> RealLines { get; init; }
    }

    /// <summary>
    /// Non-mod lines present on real items: rarity lines (Normal/Magic/Rare
    /// seen on game copies, Unique by identical shape), the requires shapes
    /// (level-only, level + one/two attributes, inline (unmet)/(augmented)
    /// markers), item level, corrupted, sockets, and base item stats
    /// (defensive lines on armour pieces, damage lines on weapons, granted
    /// skills) from maintainer captures. Fingerprints and class prefixes must
    /// avoid these exactly like mod lines (the false-positive rule covers
    /// unrelated lines; same-text cross-class matches stay legal).
    /// </summary>
    public static readonly IReadOnlyList<string> UniversalLines =
    [
        "Rarity: Normal",
        "Rarity: Magic",
        "Rarity: Rare",
        "Rarity: Unique",
        "Requires: Level 55",
        "Requires: Level 36, 46 Int",
        "Requires: Level 77, 163 (unmet) Str",
        "Requires: Level 70, 98 (unmet) Str, 72 Int",
        "Requires: Level 59, 44 Str, 44 Dex",
        "Requires: Level 62, 38 (augmented) Str, 38 (augmented) Int",
        "Item Level: 80",
        "Corrupted",
        "Sockets: S S",
        "Sockets: S",
        "Armour: 115",
        "Armour: 138 (augmented)",
        "Evasion Rating: 117",
        "Evasion Rating: 117 (augmented)",
        "Energy Shield: 49",
        "Energy Shield: 86 (augmented)",
        "Block chance: 32%",
        "Block chance: 32% (augmented)",
        "Physical Damage: 35-59",
        "Physical Damage: 93-159 (augmented)",
        "Elemental Damage: 35-59 (fire), 4-64 (lightning)",
        "Lightning Damage: 2-43 (lightning)",
        "Critical Hit Chance: 5.00%",
        "Attacks per Second: 1.45",
        "Reload Time: 1.10",
        "Grants Skill: Raise Shield",
        "Grants Skill: Level 18 Sigil of Power",
    ];

    /// <summary>Normalized patterns of <see cref="UniversalLines"/>.</summary>
    public static readonly HashSet<string> UniversalPatterns = UniversalLines
        .Select(CatalogLineNormalizer.Normalize).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Shortest class-name prefix unique across all catalog class names
    /// (case-insensitive), extended until its `: prefix` form matches only
    /// that class's `Item Class:` line and no mod or universal line in the
    /// corpus. Used for class anchors and paste class groups so they isolate
    /// the class instead of matching every colon line. Falls back to the full
    /// name (callers surface residual risk as diagnostics).
    /// </summary>
    public string ClassNamePrefix(string itemClass)
    {
        if (_fragments?.ClassAnchors is not null &&
            _fragments.ClassAnchors.TryGetValue(itemClass, out var precomputedAnchor) &&
            precomputedAnchor.Length > 0)
            return precomputedAnchor;
        var entry = FindItemClass(itemClass);
        var name = entry?.Name ?? itemClass;
        var allNames = _catalog.ItemClasses.Select(c => c.Name).ToList();
        var len = 1;
        for (; len < name.Length; len++)
        {
            var p = name[..len];
            if (allNames.Count(n => n.StartsWith(p, StringComparison.OrdinalIgnoreCase)) == 1)
                break;
        }
        len = Math.Min(len, name.Length);
        while (len < name.Length && !PrefixClean(name[..len], name))
            len++;
        return name[..len];
    }

    private bool PrefixClean(string prefix, string className)
    {
        var test = ": " + System.Text.RegularExpressions.Regex.Escape(prefix);
        foreach (var cls in _catalog.ItemClasses)
        {
            var line = $"Item Class: {cls.Name}";
            var hit = System.Text.RegularExpressions.Regex.IsMatch(line, test, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (cls.Name.Equals(className, StringComparison.OrdinalIgnoreCase))
            {
                if (!hit) return false;
            }
            else if (hit)
            {
                return false;
            }
        }
        // Class lines are covered by the loop above; here only mod and
        // universal lines may disqualify the prefix.
        return ClassTermQuiet(System.Text.RegularExpressions.Regex.Escape(prefix));
    }

    /// <summary>
    /// True when `: innerTerm` matches no universal or mod line (class lines
    /// are excluded — the class-set check is <see cref="ClassTermExact"/>).
    /// The acceptance test for merged alternation terms like `.*Hand M`.
    /// </summary>
    public bool ClassTermQuiet(string innerTerm)
    {
        var test = ": " + innerTerm;
        foreach (var (_, real) in EnsureUnionIndex().RealLines)
        {
            if (real.StartsWith("Item Class:", StringComparison.Ordinal)) continue;
            if (System.Text.RegularExpressions.Regex.IsMatch(real, test, System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return false;
        }
        return true;
    }

    /// <summary>
    /// True when `: innerTerm` matches the `Item Class:` line of every class
    /// in <paramref name="classIds"/> (by id) and of no other catalog class.
    /// </summary>
    public bool ClassTermExact(string innerTerm, IReadOnlyCollection<string> classIds)
    {
        var test = ": " + innerTerm;
        var wanted = new HashSet<string>(classIds, StringComparer.Ordinal);
        foreach (var cls in _catalog.ItemClasses)
        {
            var hit = System.Text.RegularExpressions.Regex.IsMatch(
                $"Item Class: {cls.Name}", test, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (wanted.Contains(cls.Id) != hit) return false;
        }
        return true;
    }

    public EngineCorpus(Catalog catalog)
    {
        _catalog = catalog;
        _modsById = catalog.Mods.ToDictionary(m => m.Id);
        // Startup must not parse the large vendor corpus or build the
        // per-class suffix indexes. The normal browser compile path uses the
        // precomputed table; the oracle is materialized only when a fallback,
        // import, or audit actually asks for it.
        _safetyItems = new(() => SafetyItemCorpus.For(catalog.Game));
        _vendorLines = new(() => VendorCorpus.For(catalog.Game)?.Lines ?? []);
        _vendorNormalized = new(() => _vendorLines.Value
            .Select(l => CatalogLineNormalizer.Normalize(l.Line))
            .ToHashSet(StringComparer.Ordinal));
        _vendorPatterns = new(() => _vendorLines.Value
            .Select(l => l.Pattern ?? CatalogLineNormalizer.Normalize(l.Line))
            .ToHashSet(StringComparer.Ordinal));
        _byClass = new(() => BuildClassIndexes(catalog, _vendorLines.Value));
        _sectionsByClass = new(() => BuildSearchSections(catalog, _vendorLines.Value));
        _fragments = FragmentTables.For(catalog.Game);
        if (_fragments is not null &&
            !string.Equals(_fragments.DataVersion, catalog.DataVersion, StringComparison.Ordinal))
            _fragments = null;
    }

    public Catalog Catalog => _catalog;

    /// <summary>Captured regression items (not all are in the modeled oracle).</summary>
    public IReadOnlyList<SafetyItem> SafetyItems => _safetyItems.Value;

    /// <summary>
    /// Captured raw lines with the catalog pattern they map to. The selector
    /// adds every line whose pattern differs from the target as foreign text,
    /// so a fragment short enough to pass the vendor corpus cannot leak into
    /// a captured unique-only or flavor line.
    /// </summary>
    public IEnumerable<(string Pattern, string Raw)> SafetyLines(string? classId)
    {
        foreach (var item in _safetyItems.Value)
        {
            if (classId is not null)
            {
                var cls = _catalog.ItemClasses.FirstOrDefault(c =>
                    c.Name.Equals(item.ClassName, StringComparison.OrdinalIgnoreCase) ||
                    c.Id.Equals(item.ClassName, StringComparison.OrdinalIgnoreCase));
                if (!string.Equals(cls?.Id, classId, StringComparison.Ordinal)) continue;
            }

            for (var i = 0; i < item.Lines.Count; i++)
            {
                var raw = i < item.RawLines.Count ? item.RawLines[i] : item.Lines[i];
                yield return (PatternForSafetyLine(item.Lines[i]), raw);
            }
        }
    }

    /// <summary>Derived vendor/stash searchable lines used by the oracle.</summary>
    public IReadOnlyList<VendorLine> VendorLines => _vendorLines.Value;

    /// <summary>
    /// True when a real line belongs to the modeled vendor corpus for a scope.
    /// Class-aware: a unique line whose text happens to match a regular mod on
    /// another class is not modeled for this item's class.
    /// </summary>
    public bool IsModeledLine(string line, string? classId = null)
    {
        var normalized = CatalogLineNormalizer.Normalize(line);
        if (classId is null)
            return _vendorNormalized.Value.Contains(normalized) ||
                   _vendorPatterns.Value.Contains(normalized) ||
                   KnownPatterns().Contains(normalized);
        if (!_byClass.Value.TryGetValue(classId, out var ci)) return false;
        return ci.AllRealLines.Any(x =>
            string.Equals(CatalogLineNormalizer.Normalize(x.Real), normalized, StringComparison.Ordinal) ||
            string.Equals(x.Pattern, normalized, StringComparison.Ordinal));
    }

    public ItemClassEntry? FindItemClass(string? id) =>
        id is null ? null : _catalog.ItemClasses.FirstOrDefault(c => c.Id == id);

    public ModEntry? FindMod(string? id) => id is null ? null : _modsById.GetValueOrDefault(id);

    /// <summary>True when a version-matched precomputed fragment table is loaded.</summary>
    public bool HasPrecomputedFragments => _fragments is not null;

    /// <summary>Data version of the loaded fragment table (for audit/diagnostics).</summary>
    public string? PrecomputedDataVersion => _fragments?.DataVersion;

    /// <summary>Global precomputed map (empty when the table is absent/stale).</summary>
    public IReadOnlyDictionary<string, string> PrecomputedGlobal =>
        _fragments?.Global ?? new Dictionary<string, string>();

    /// <summary>Class precomputed map (empty when absent).</summary>
    public IReadOnlyDictionary<string, string> PrecomputedForClass(string itemClass) =>
        _fragments is not null && _fragments.ByClass.TryGetValue(itemClass, out var map)
            ? map
            : new Dictionary<string, string>();

    /// <summary>
    /// Looks up a precomputed minimal fragment for a scope/pattern. Returns
    /// false when the table is absent, stale, or does not contain the entry;
    /// the selector then computes as usual.
    /// </summary>
    public bool TryPrecomputedFragment(string pattern, string? itemClass, out string? fragment)
    {
        if (_fragments is null)
        {
            fragment = null;
            return false;
        }
        return _fragments.TryGet(itemClass, pattern, out fragment);
    }

    /// <summary>
    /// Reason a precomputed scope deliberately has no fragment, or null when
    /// the pattern is present (or the table is absent/stale).
    /// </summary>
    public string? PrecomputedUnresolvedReason(string pattern, string? itemClass) =>
        _fragments?.UnresolvedReason(itemClass, pattern);

    public IReadOnlyList<ModEntry> Mods => _catalog.Mods;

    /// <summary>All item classes that can carry the given mod.</summary>
    public IReadOnlyList<string> ClassesForMod(string modId) =>
        _modsById.TryGetValue(modId, out var mod) ? mod.Classes : [];

    /// <summary>
    /// Real (un-normalized) copy-text lines on a class, with their patterns,
    /// including universal non-mod lines (used by the regex oracle).
    /// </summary>
    public IReadOnlyList<(string Pattern, string Real)> RealLinesForClass(string itemClass) =>
        _byClass.Value.TryGetValue(itemClass, out var ci) ? ci.AllRealLines : [];

    /// <summary>
    /// Normalized display shapes for a pattern in a scope. Ranged stats add
    /// the tier span as "(min-max)", so a threshold query must use this shape
    /// to see the actual roll on an advanced item.
    /// </summary>
    public IReadOnlyList<string> DisplayPatternsFor(string pattern, string? itemClass)
    {
        var lines = itemClass is null ? RealLinesGlobal() : RealLinesForClass(itemClass);
        return lines
            .Where(x => string.Equals(x.Pattern, pattern, StringComparison.Ordinal))
            .Select(x => CatalogLineNormalizer.Normalize(x.Real))
            .Where(x => !string.Equals(x, pattern, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Search sections for a class (base/name/properties, implicit mods, and
    /// explicit mods). A section is the unit within which the game's `.` may
    /// cross a line break; the observed implicit→explicit boundary does not.
    /// </summary>
    public IReadOnlyList<(string Kind, string Text)> SearchSectionsForClass(string itemClass) =>
        _sectionsByClass.Value.TryGetValue(itemClass, out var sections)
            ? sections.Select(s => (s.Kind, s.Text)).ToList()
            : [];

    /// <summary>
    /// True when a fragment has no match spanning a line break in any search
    /// section of the scope. Line-local matches are handled by the ordinary
    /// per-line oracle; this check rejects candidates that only work by
    /// bridging two lines of one item (e.g. a prefix caption and an unrelated
    /// resistance line). The check is conservative: a fragment must also not
    /// bridge two lines that happen to both be modeled.
    /// </summary>
    public bool CrossLineSafe(string? itemClass, string fragment)
    {
        // Only an unescaped `.` (including `.*`/`.+`) can consume a line
        // break. Anchors, digits, escaped dots, and literals cannot.
        if (!HasUnescapedDot(fragment)) return true;
        var key = (itemClass ?? "\0", fragment);
        if (_crossLineCache.TryGetValue(key, out var cached)) return cached;

        // A wildcard with no 3+ character literal guard is too broad to prove
        // cross-line safe; this rejects e.g. `Co.+ge$` before scanning the
        // whole item corpus.
        var hint = FragmentSelector.LiteralHint(fragment);
        if (hint is null)
        {
            _crossLineCache[key] = false;
            return false;
        }
        var result = CrossLineSafeCore(itemClass, fragment, hint);
        _crossLineCache[key] = result;
        return result;
    }

    private static bool HasUnescapedDot(string fragment)
    {
        for (var i = 0; i < fragment.Length; i++)
        {
            if (fragment[i] != '.') continue;
            if (i == 0 || fragment[i - 1] != '\\') return true;
        }
        return false;
    }

    private bool CrossLineSafeCore(string? itemClass, string fragment, string hint)
    {
        System.Text.RegularExpressions.Regex regex;
        try
        {
            regex = new System.Text.RegularExpressions.Regex(
                fragment,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase |
                System.Text.RegularExpressions.RegexOptions.CultureInvariant |
                System.Text.RegularExpressions.RegexOptions.Multiline |
                System.Text.RegularExpressions.RegexOptions.Singleline,
                TimeSpan.FromMilliseconds(250));
        }
        catch (ArgumentException)
        {
            return false;
        }

        IEnumerable<string> classes = itemClass is null
            ? _sectionsByClass.Value.Keys
            : new[] { itemClass };
        foreach (var cls in classes)
        {
            if (!_sectionsByClass.Value.TryGetValue(cls, out var sections)) continue;
            foreach (var section in sections)
            {
                // A match must contain the literal hint; skip sections that
                // cannot contain it. This turns most wildcard checks from a
                // full-section scan into a substring lookup.
                if (section.Text.IndexOf(hint, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (section.Text.IndexOf('\n') < 0) continue;
                if (HasCrossLineMatch(regex, section.Text) ||
                    HasCrossLineMatch(regex, section.ReverseText))
                    return false;
            }
        }
        return true;
    }

    private static bool HasCrossLineMatch(System.Text.RegularExpressions.Regex regex, string text)
    {
        try
        {
            foreach (System.Text.RegularExpressions.Match match in regex.Matches(text))
            {
                if (match.Value.IndexOf('\n') >= 0) return true;
            }
        }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
        {
            // A runaway candidate is not proven safe.
            return true;
        }
        return false;
    }

    /// <summary>
    /// Matchable copy-text lines on a class: real lines minus universal
    /// non-mod lines (used by explain/matching; the oracle still sees all).
    /// </summary>
    public IReadOnlyList<(string Pattern, string Real)> MatchableLinesForClass(string itemClass) =>
        _byClass.Value.TryGetValue(itemClass, out var ci)
            ? ci.AllRealLines.Where(x => !UniversalPatterns.Contains(x.Pattern)).ToList()
            : [];

    /// <summary>Real copy-text lines across every class (global scope), with their patterns.</summary>
    public IReadOnlyList<(string Pattern, string Real)> RealLinesGlobal() =>
        EnsureUnionIndex().RealLines;

    /// <summary>
    /// All distinct mods that have a stat line with the given (normalized) pattern.
    /// Used by the explainer to reverse a matched copy-text line into its mods.
    /// </summary>
    public IReadOnlyList<ModEntry> ModsWithPattern(string pattern)
    {
        if (_modsByPattern is null)
        {
            _modsByPattern = _catalog.Mods
                .SelectMany(m => m.StatLines.Select(sl => (Mod: m, Pattern: sl.Pattern)))
                .GroupBy(x => x.Pattern, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Select(x => x.Mod).Distinct().ToList(), StringComparer.Ordinal);
        }
        return _modsByPattern.TryGetValue(pattern, out var mods) ? mods : [];
    }

    /// <summary>
    /// Shortest fingerprint for a mod line on a given item class. Uses the
    /// catalog's precomputed fingerprint, falling back to the live index when
    /// absent. When <paramref name="avoidPlaceholder"/> is true, candidates that
    /// contain the digit placeholder are skipped (a longer plain-literal fragment
    /// may be returned). When <paramref name="caseInsensitive"/> is true, the
    /// precomputed (case-sensitive) fingerprint is bypassed and the live
    /// case-insensitive index is used, so the result is unique under the game's
    /// case-insensitive matching. Returns null when the line is not present on
    /// the class.
    /// </summary>
    public string? Fingerprint(string modId, string linePattern, string? itemClass, bool avoidPlaceholder = false, bool caseInsensitive = false)
    {
        if (itemClass is null) return null;
        if (!_modsById.TryGetValue(modId, out var mod)) return null;
        if (!caseInsensitive)
        {
            foreach (var sl in mod.StatLines)
            {
                if (sl.Pattern != linePattern) continue;
                if (sl.Fingerprints.TryGetValue(itemClass, out var fp) && !string.IsNullOrEmpty(fp))
                {
                    if (!avoidPlaceholder || fp.IndexOf(SuffixIndex.DigitPlaceholder) < 0) return fp;
                }
            }
        }
        if (!_byClass.Value.TryGetValue(itemClass, out var ci)) return null;
        if (!ci.PatternToIndex.TryGetValue(linePattern, out var idx)) return null;
        var index = caseInsensitive ? ci.FoldIndex : ci.Index;
        return index.ShortestUniqueSubstring(idx, avoidPlaceholder);
    }

    /// <summary>
    /// The class base fingerprint ("Item Class: X" line). Uses the class
    /// entry's precomputed fingerprint, or falls back to the shortest unique
    /// substring when unavailable. When <paramref name="caseInsensitive"/> is
    /// true, the precomputed fingerprint is bypassed and the case-insensitive
    /// index is used so the anchor stays unique under case-folded matching.
    /// </summary>
    public string? ClassFingerprint(string itemClass, bool caseInsensitive = false)
    {
        var entry = _catalog.ItemClasses.FirstOrDefault(c => c.Id == itemClass);
        if (!caseInsensitive && entry is not null && !string.IsNullOrEmpty(entry.BaseFingerprint)) return entry.BaseFingerprint;
        if (!_byClass.Value.TryGetValue(itemClass, out var ci)) return null;
        var index = caseInsensitive ? ci.FoldIndex : ci.Index;
        return index.ShortestUniqueSubstring(ci.ClassLineIndex);
    }

    /// <summary>
    /// True when the fragment occurs in every corpus line whose pattern is in
    /// <paramref name="allowedPatterns"/> and in no other line of the class
    /// corpus (false-positive oracle).
    /// </summary>
    public bool FragmentSafeOnClass(
        string itemClass, string fragment, IReadOnlyCollection<string> allowedPatterns,
        bool includeSafety = true, bool includeUniversal = true)
    {
        if (!_byClass.Value.TryGetValue(itemClass, out var ci)) return false;
        var matched = ci.Index.LinesMatching(fragment);
        var any = false;
        foreach (var lineIndex in matched)
        {
            var pattern = ci.Index.ClassCorpus[lineIndex];
            if (!includeSafety && ((pattern.Length > 0 && pattern[0] == SafetyPatternMarker) || ci.SafetyOnlyPatterns.Contains(pattern))) continue;
            if (!includeUniversal && UniversalPatterns.Contains(pattern)) continue;
            any = true;
            if (!allowedPatterns.Contains(pattern)) return false;
        }
        return any;
    }

    /// <summary>
    /// True when a regex fragment matches at least one real class-corpus line
    /// whose pattern is allowed, and no real line whose pattern is not allowed.
    /// Matching is case-insensitive to mirror the game. Used to validate
    /// value-threshold patterns against actual copy text.
    /// </summary>
    public bool RegexSafeOnClass(string itemClass, string regexFragment, IReadOnlyCollection<string> allowedPatterns)
    {
        if (!_byClass.Value.TryGetValue(itemClass, out var ci)) return false;
        var matchedAny = false;
        foreach (var (pattern, real) in ci.AllRealLines)
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(real, regexFragment, System.Text.RegularExpressions.RegexOptions.IgnoreCase)) continue;
            matchedAny = true;
            if (!allowedPatterns.Contains(pattern)) return false;
        }
        return matchedAny;
    }

    /// <summary>
    /// Shortest fingerprint for a mod line over the union of all class corpora
    /// (global / no-class mode). Uses the same placeholder-avoidance semantics as
    /// <see cref="Fingerprint"/>. When <paramref name="caseInsensitive"/> is true
    /// the case-insensitive union index is used. Returns null when the line has no
    /// unique substring in the union corpus.
    /// </summary>
    public string? GlobalFingerprint(string modId, string linePattern, bool avoidPlaceholder, bool caseInsensitive = false)
    {
        if (!_modsById.TryGetValue(modId, out _)) return null;
        return ShortestUniqueSubstringForPattern(linePattern, null, avoidPlaceholder, caseInsensitive);
    }

    /// <summary>
    /// Shortest substring of a normalized pattern that is unique in the scope
    /// (one class, or the union when <paramref name="itemClass"/> is null).
    /// </summary>
    public string? ShortestUniqueSubstringForPattern(
        string linePattern, string? itemClass, bool avoidPlaceholder, bool caseInsensitive = true)
    {
        if (itemClass is not null)
        {
            if (!_byClass.Value.TryGetValue(itemClass, out var ci)) return null;
            if (!ci.PatternToIndex.TryGetValue(linePattern, out var idx)) return null;
            var index = caseInsensitive ? ci.FoldIndex : ci.Index;
            return index.ShortestUniqueSubstring(idx, avoidPlaceholder);
        }
        var union = EnsureUnionIndex();
        if (!union.PatternToIndex.TryGetValue(linePattern, out var unionIdx)) return null;
        var unionIndex = caseInsensitive ? union.FoldIndex : union.Index;
        return unionIndex.ShortestUniqueSubstring(unionIdx, avoidPlaceholder);
    }

    /// <summary>
    /// Global analogue of <see cref="RegexSafeOnClass"/> over the union corpus:
    /// the fragment must match at least one real line whose pattern is allowed and
    /// no real line whose pattern is not allowed. Matching is case-insensitive.
    /// </summary>
    public bool GlobalRegexSafe(string regexFragment, IReadOnlyCollection<string> allowedPatterns)
    {
        var union = EnsureUnionIndex();
        var matchedAny = false;
        foreach (var (pattern, real) in union.RealLines)
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(real, regexFragment, System.Text.RegularExpressions.RegexOptions.IgnoreCase)) continue;
            matchedAny = true;
            if (!allowedPatterns.Contains(pattern)) return false;
        }
        return matchedAny;
    }

    /// <summary>
    /// Patterns in the scope whose text contains <paramref name="literal"/>
    /// (case-insensitive). A regex fragment that contains this literal can
    /// only match those patterns, so the selector's oracle can skip the rest
    /// of the corpus. The literal must not contain the digit placeholder.
    /// </summary>
    public IReadOnlyList<string> PatternsMatchingLiteral(string literal, string? itemClass)
    {
        if (literal.Length == 0) return [];
        if (itemClass is not null)
        {
            if (!_byClass.Value.TryGetValue(itemClass, out var ci)) return [];
            return ci.FoldIndex.LinesMatching(literal).Select(i => ci.Index.ClassCorpus[i]).ToList();
        }
        var union = EnsureUnionIndex();
        return union.FoldIndex.LinesMatching(literal).Select(i => union.Index.ClassCorpus[i]).ToList();
    }

    private UnionIndex EnsureUnionIndex()
    {
        if (_union is not null) return _union;

        var patterns = _catalog.Mods.SelectMany(m => m.StatLines).Select(sl => sl.Pattern)
            .Concat(UniversalLines.Select(CatalogLineNormalizer.Normalize))
            .Concat(_catalog.ItemClasses.Select(c => CatalogLineNormalizer.Normalize($"Item Class: {c.Name}")))
            .Concat(_vendorLines.Value.Select(l => l.Pattern ?? SafetyPattern(l.Line)))
            .Distinct().ToList();
        patterns.Sort(StringComparer.Ordinal);
        var patternToIndex = new Dictionary<string, int>();
        for (var i = 0; i < patterns.Count; i++) patternToIndex[patterns[i]] = i;

        var vendorPatterns = _vendorLines.Value
            .Where(l => !string.IsNullOrWhiteSpace(l.Pattern))
            .Select(l => l.Pattern!)
            .ToHashSet(StringComparer.Ordinal);
        var realLines = _catalog.Mods
            .SelectMany(m => m.StatLines)
            .Where(sl => !vendorPatterns.Contains(sl.Pattern))
            .Select(sl => (sl.Pattern, sl.Line))
            .Concat(UniversalLines.Select(l => (CatalogLineNormalizer.Normalize(l), l)))
            .Concat(_catalog.ItemClasses.Select(c => (CatalogLineNormalizer.Normalize($"Item Class: {c.Name}"), $"Item Class: {c.Name}")))
            .Concat(_vendorLines.Value.Select(l => (l.Pattern ?? SafetyPattern(l.Line), l.Line)))
            .Distinct()
            .ToList();

        _union = new UnionIndex
        {
            Index = SuffixIndex.Build(patterns),
            FoldIndex = SuffixIndex.Build(patterns, caseInsensitive: true),
            PatternToIndex = patternToIndex,
            RealLines = realLines,
        };
        return _union;
    }

    private static string SectionKindForGeneration(string generation) => generation switch
    {
        "implicit" => "implicit",
        _ => "explicit",
    };

    private static string SectionForVendorKind(string? kind) => kind switch
    {
        "implicit" or "implicit-caption" => "implicit",
        "base-name" or "base-property" or "magic-name" or "usage" or "universal" or "class" => "base",
        _ => "explicit",
    };

    private static Dictionary<string, IReadOnlyList<SearchSection>> BuildSearchSections(
        Catalog catalog, IReadOnlyList<VendorLine> vendorLines)
    {
        var linesByClass = new Dictionary<string, Dictionary<string, HashSet<string>>>(StringComparer.Ordinal);
        void Add(string cls, string section, string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            if (!linesByClass.TryGetValue(cls, out var sections))
            {
                sections = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
                linesByClass[cls] = sections;
            }
            if (!sections.TryGetValue(section, out var lines))
            {
                lines = new HashSet<string>(StringComparer.Ordinal);
                sections[section] = lines;
            }
            lines.Add(line.Trim());
        }

        foreach (var cls in catalog.ItemClasses)
        {
            foreach (var u in UniversalLines) Add(cls.Id, "base", u);
            Add(cls.Id, "base", $"Item Class: {cls.Name}");
        }
        var vendorPatternsByClass = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var line in vendorLines)
        {
            if (line.ClassId is null) continue;
            Add(line.ClassId, SectionForVendorKind(line.Kind), line.Line);
            if (!string.IsNullOrWhiteSpace(line.Pattern))
            {
                if (!vendorPatternsByClass.TryGetValue(line.ClassId, out var set))
                {
                    set = new HashSet<string>(StringComparer.Ordinal);
                    vendorPatternsByClass[line.ClassId] = set;
                }
                set.Add(line.Pattern);
            }
        }
        // Same fallback as the line corpus: only add the representative line
        // for a catalog pattern with no actual display form in the vendor data.
        foreach (var mod in catalog.Mods)
        {
            var section = SectionKindForGeneration(mod.GenerationType);
            foreach (var cls in mod.Classes)
            {
                var seen = vendorPatternsByClass.GetValueOrDefault(cls);
                foreach (var sl in mod.StatLines)
                    if (seen is null || !seen.Contains(sl.Pattern)) Add(cls, section, sl.Line);
            }
        }

        var result = new Dictionary<string, IReadOnlyList<SearchSection>>(StringComparer.Ordinal);
        foreach (var cls in catalog.ItemClasses)
        {
            if (!linesByClass.TryGetValue(cls.Id, out var sections)) continue;
            var list = new List<SearchSection>();
            foreach (var kind in new[] { "base", "implicit", "explicit" })
            {
                if (!sections.TryGetValue(kind, out var lines) || lines.Count == 0) continue;
                var ordered = lines.OrderBy(x => x, StringComparer.Ordinal).ToList();
                var text = string.Join("\n", ordered);
                var reverse = string.Join("\n", Enumerable.Reverse(ordered));
                list.Add(new SearchSection(kind, text, reverse,
                    lines.Select(CatalogLineNormalizer.Normalize).ToHashSet(StringComparer.Ordinal)));
            }
            result[cls.Id] = list;
        }
        return result;
    }

    /// <summary>Normalized pattern used for a real item line by the oracle.</summary>
    public string PatternForSafetyLine(string line) => VendorPattern(line, KnownPatterns());

    private HashSet<string> KnownPatterns()
    {
        lock (_knownLock)
            return _knownPatterns ??=
                _catalog.Mods.SelectMany(m => m.StatLines)
                    .Select(sl => sl.Pattern)
                    .Concat(UniversalPatterns)
                    .Concat(_catalog.ItemClasses.Select(c => CatalogLineNormalizer.Normalize($"Item Class: {c.Name}")))
                    .ToHashSet(StringComparer.Ordinal);
    }

    private static string VendorPattern(string line, HashSet<string> known)
    {
        var normalized = CatalogLineNormalizer.Normalize(line);
        return known.Contains(normalized) ? normalized : SafetyPatternMarker + normalized;
    }

    /// <summary>
    /// Foreign vendor text (base properties, captions, names, requirements)
    /// always gets a safety pattern, even when its normalized text happens to
    /// equal a catalog stat pattern. Otherwise a boot's "20% increased Movement
    /// Speed" base property would be treated as another copy of the explicit
    /// Movement Speed affix and would poison recall/target grouping.
    /// </summary>
    private static string SafetyPattern(string line) =>
        SafetyPatternMarker + CatalogLineNormalizer.Normalize(line);

    private static Dictionary<string, ClassIndex> BuildClassIndexes(Catalog catalog, IReadOnlyList<VendorLine> vendorLines)
    {
        // Per class: distinct patterns for all mods that can appear on the class,
        // plus the class line, tracking every real line for regex validation.
        var patternsByClass = new Dictionary<string, HashSet<string>>();
        var allRealByClass = new Dictionary<string, List<(string Pattern, string Real, string Kind)>>();
        void Add(string cls, string pattern, string real, string kind)
        {
            if (!patternsByClass.TryGetValue(cls, out var set))
            {
                set = new HashSet<string>();
                patternsByClass[cls] = set;
                allRealByClass[cls] = new List<(string, string, string)>();
            }
            set.Add(pattern);
            allRealByClass[cls].Add((pattern, real, kind));
        }

        foreach (var cls in catalog.ItemClasses)
        {
            foreach (var u in UniversalLines)
                Add(cls.Id, CatalogLineNormalizer.Normalize(u), u, "universal");
            var classLine = $"Item Class: {cls.Name}";
            Add(cls.Id, CatalogLineNormalizer.Normalize(classLine), classLine, "class");
        }
        foreach (var line in vendorLines)
        {
            if (line.ClassId is null) continue;
            if (!catalog.ItemClasses.Any(c => c.Id == line.ClassId)) continue;
            Add(line.ClassId, line.Pattern ?? SafetyPattern(line.Line), line.Line, line.Kind ?? "mod");
        }
        // Fallback for a catalog pattern that the vendor builder could not
        // render (e.g. a new translation shape): keep the representative line
        // so recall never silently disappears. Normally every catalog pattern
        // is already covered by an actual display form above.
        foreach (var mod in catalog.Mods)
        {
            var kind = SectionKindForGeneration(mod.GenerationType);
            foreach (var cls in mod.Classes)
            {
                if (!patternsByClass.TryGetValue(cls, out var present)) continue;
                foreach (var sl in mod.StatLines)
                    if (!present.Contains(sl.Pattern)) Add(cls, sl.Pattern, sl.Line, kind);
            }
        }

        var result = new Dictionary<string, ClassIndex>();
        foreach (var cls in catalog.ItemClasses)
        {
            var patterns = patternsByClass.GetValueOrDefault(cls.Id) ?? new HashSet<string>();
            var classLinePattern = CatalogLineNormalizer.Normalize($"Item Class: {cls.Name}");
            foreach (var u in UniversalLines) patterns.Add(CatalogLineNormalizer.Normalize(u));
            var corpus = new List<string>(patterns) { classLinePattern };
            corpus.Sort(StringComparer.Ordinal);

            var index = SuffixIndex.Build(corpus);
            var foldIndex = SuffixIndex.Build(corpus, caseInsensitive: true);
            var classLineIndex = corpus.IndexOf(classLinePattern);
            var patternToIndex = new Dictionary<string, int>();
            for (var i = 0; i < corpus.Count; i++) patternToIndex[corpus[i]] = i;
            var catalogPatterns = catalog.Mods.Where(m => m.Classes.Contains(cls.Id))
                .SelectMany(m => m.StatLines).Select(sl => sl.Pattern)
                .Concat(UniversalPatterns)
                .Append(classLinePattern)
                .ToHashSet(StringComparer.Ordinal);
            var safetyOnly = patterns.Where(p => !catalogPatterns.Contains(p)).ToHashSet(StringComparer.Ordinal);

            var allReal = allRealByClass.GetValueOrDefault(cls.Id) ?? new List<(string, string, string)>();
            var sortedReal = allReal
                .Select(x => (x.Pattern, x.Real))
                .Distinct()
                .OrderBy(x => x.Pattern, StringComparer.Ordinal)
                .ThenBy(x => x.Real, StringComparer.Ordinal)
                .ToList();
            result[cls.Id] = new ClassIndex
            {
                Index = index,
                FoldIndex = foldIndex,
                ClassLineIndex = classLineIndex,
                PatternToIndex = patternToIndex,
                SafetyOnlyPatterns = safetyOnly,
                AllRealLines = sortedReal,
            };
        }
        return result;
    }
}

/// <summary>Normalizes a copy-text line into a value-agnostic pattern.</summary>
public static class CatalogLineNormalizer
{
    public const char DigitPlaceholder = SuffixIndex.DigitPlaceholder;

    public static string Normalize(string line)
    {
        var sb = new System.Text.StringBuilder(line.Length);
        var inDigit = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (char.IsAsciiDigit(c))
            {
                if (!inDigit)
                {
                    sb.Append(DigitPlaceholder);
                    inDigit = true;
                }
            }
            else if (c == '.' && inDigit && i + 1 < line.Length && char.IsAsciiDigit(line[i + 1]))
            {
                // Decimal values are one roll placeholder: 4.4% and 4% both
                // normalize to \x01%. Keep inDigit so the fractional digits do
                // not emit a second placeholder.
            }
            else
            {
                if (inDigit) inDigit = false;
                sb.Append(c);
            }
        }
        return sb.ToString();
    }
}

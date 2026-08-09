using PoeRegex.Core.Data;
using PoeRegex.Core.Search;

namespace PoeRegex.Core.Compilation;

/// <summary>
/// In-memory view of a catalog with per-class suffix indexes, used for
/// fingerprint lookup and the false-positive oracle.
/// </summary>
public sealed class EngineCorpus
{
    private readonly Catalog _catalog;
    private readonly Dictionary<string, ClassIndex> _byClass;
    private readonly Dictionary<string, ModEntry> _modsById;
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
        /// <summary>
        /// All real (un-normalized) copy-text lines on this class, each tagged
        /// with its normalized pattern. Used by the regex-based oracle so value
        /// thresholds validate against every actual roll.
        /// </summary>
        public required List<(string Pattern, string Real)> AllRealLines { get; init; }
    }

    /// <summary>Union corpus over every class's mod lines, for global (no-class) fingerprints.</summary>
    private sealed class UnionIndex
    {
        public required SuffixIndex Index { get; init; }
        public required SuffixIndex FoldIndex { get; init; }
        /// <summary>pattern -> corpus line index.</summary>
        public required Dictionary<string, int> PatternToIndex { get; init; }
        public required List<(string Pattern, string Real)> RealLines { get; init; }
    }

    public EngineCorpus(Catalog catalog)
    {
        _catalog = catalog;
        _modsById = catalog.Mods.ToDictionary(m => m.Id);
        _byClass = BuildClassIndexes(catalog);
    }

    public Catalog Catalog => _catalog;

    public ItemClassEntry? FindItemClass(string? id) =>
        id is null ? null : _catalog.ItemClasses.FirstOrDefault(c => c.Id == id);

    public ModEntry? FindMod(string? id) => id is null ? null : _modsById.GetValueOrDefault(id);

    public IReadOnlyList<ModEntry> Mods => _catalog.Mods;

    /// <summary>All item classes that can carry the given mod.</summary>
    public IReadOnlyList<string> ClassesForMod(string modId) =>
        _modsById.TryGetValue(modId, out var mod) ? mod.Classes : [];

    /// <summary>Real (un-normalized) copy-text lines on a class, with their patterns.</summary>
    public IReadOnlyList<(string Pattern, string Real)> RealLinesForClass(string itemClass) =>
        _byClass.TryGetValue(itemClass, out var ci) ? ci.AllRealLines : [];

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
        if (!_byClass.TryGetValue(itemClass, out var ci)) return null;
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
        if (!_byClass.TryGetValue(itemClass, out var ci)) return null;
        var index = caseInsensitive ? ci.FoldIndex : ci.Index;
        return index.ShortestUniqueSubstring(ci.ClassLineIndex);
    }

    /// <summary>
    /// True when the fragment occurs in every corpus line whose pattern is in
    /// <paramref name="allowedPatterns"/> and in no other line of the class
    /// corpus (false-positive oracle).
    /// </summary>
    public bool FragmentSafeOnClass(string itemClass, string fragment, IReadOnlyCollection<string> allowedPatterns)
    {
        if (!_byClass.TryGetValue(itemClass, out var ci)) return false;
        var matched = ci.Index.LinesMatching(fragment);
        foreach (var lineIndex in matched)
        {
            var pattern = ci.Index.ClassCorpus[lineIndex];
            if (!allowedPatterns.Contains(pattern)) return false;
        }
        return matched.Length > 0;
    }

    /// <summary>
    /// True when a regex fragment matches at least one real class-corpus line
    /// whose pattern is allowed, and no real line whose pattern is not allowed.
    /// Matching is case-insensitive to mirror the game. Used to validate
    /// value-threshold patterns against actual copy text.
    /// </summary>
    public bool RegexSafeOnClass(string itemClass, string regexFragment, IReadOnlyCollection<string> allowedPatterns)
    {
        if (!_byClass.TryGetValue(itemClass, out var ci)) return false;
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
        var union = EnsureUnionIndex();
        if (!union.PatternToIndex.TryGetValue(linePattern, out var idx)) return null;
        var index = caseInsensitive ? union.FoldIndex : union.Index;
        return index.ShortestUniqueSubstring(idx, avoidPlaceholder);
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

    private UnionIndex EnsureUnionIndex()
    {
        if (_union is not null) return _union;

        var patterns = _catalog.Mods.SelectMany(m => m.StatLines).Select(sl => sl.Pattern).Distinct().ToList();
        patterns.Sort(StringComparer.Ordinal);
        var patternToIndex = new Dictionary<string, int>();
        for (var i = 0; i < patterns.Count; i++) patternToIndex[patterns[i]] = i;

        var realLines = _catalog.Mods
            .SelectMany(m => m.StatLines)
            .Select(sl => (sl.Pattern, sl.Line))
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

    private static Dictionary<string, ClassIndex> BuildClassIndexes(Catalog catalog)
    {
        // Per class: distinct patterns for all mods that can appear on the class,
        // plus the class line, tracking every real line for regex validation.
        var patternsByClass = new Dictionary<string, HashSet<string>>();
        var allRealByClass = new Dictionary<string, List<(string Pattern, string Real)>>();
        void Add(string cls, string pattern, string real)
        {
            if (!patternsByClass.TryGetValue(cls, out var set))
            {
                set = new HashSet<string>();
                patternsByClass[cls] = set;
                allRealByClass[cls] = new List<(string, string)>();
            }
            set.Add(pattern);
            allRealByClass[cls].Add((pattern, real));
        }

        foreach (var mod in catalog.Mods)
        {
            foreach (var cls in mod.Classes)
            {
                foreach (var sl in mod.StatLines) Add(cls, sl.Pattern, sl.Line);
            }
        }

        var result = new Dictionary<string, ClassIndex>();
        foreach (var cls in catalog.ItemClasses)
        {
            var patterns = patternsByClass.GetValueOrDefault(cls.Id) ?? new HashSet<string>();
            var classLinePattern = CatalogLineNormalizer.Normalize($"Item Class: {cls.Name}");
            var corpus = new List<string>(patterns) { classLinePattern };
            corpus.Sort(StringComparer.Ordinal);

            var index = SuffixIndex.Build(corpus);
            var foldIndex = SuffixIndex.Build(corpus, caseInsensitive: true);
            var classLineIndex = corpus.IndexOf(classLinePattern);
            var patternToIndex = new Dictionary<string, int>();
            for (var i = 0; i < corpus.Count; i++) patternToIndex[corpus[i]] = i;

            var allReal = allRealByClass.GetValueOrDefault(cls.Id) ?? new List<(string, string)>();
            var sortedReal = allReal.OrderBy(x => x.Pattern, StringComparer.Ordinal).ToList();
            result[cls.Id] = new ClassIndex
            {
                Index = index,
                FoldIndex = foldIndex,
                ClassLineIndex = classLineIndex,
                PatternToIndex = patternToIndex,
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
        foreach (var c in line)
        {
            if (char.IsAsciiDigit(c))
            {
                if (!inDigit)
                {
                    sb.Append(DigitPlaceholder);
                    inDigit = true;
                }
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

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

    private sealed class ClassIndex
    {
        public required SuffixIndex Index { get; init; }
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

    /// <summary>
    /// Shortest fingerprint for a mod line on a given item class. Uses the
    /// catalog's precomputed fingerprint, falling back to the live index when
    /// absent. Returns null when the line is not present on the class.
    /// </summary>
    public string? Fingerprint(string modId, string linePattern, string? itemClass)
    {
        if (itemClass is null) return null;
        if (!_modsById.TryGetValue(modId, out var mod)) return null;
        foreach (var sl in mod.StatLines)
        {
            if (sl.Pattern != linePattern) continue;
            if (sl.Fingerprints.TryGetValue(itemClass, out var fp) && !string.IsNullOrEmpty(fp)) return fp;
        }
        if (!_byClass.TryGetValue(itemClass, out var ci)) return null;
        if (!ci.PatternToIndex.TryGetValue(linePattern, out var idx)) return null;
        return ci.Index.ShortestUniqueSubstring(idx);
    }

    /// <summary>
    /// The class base fingerprint ("Item Class: X" line). Uses the class
    /// entry's precomputed fingerprint, or falls back to the shortest unique
    /// substring when unavailable.
    /// </summary>
    public string? ClassFingerprint(string itemClass)
    {
        var entry = _catalog.ItemClasses.FirstOrDefault(c => c.Id == itemClass);
        if (entry is not null && !string.IsNullOrEmpty(entry.BaseFingerprint)) return entry.BaseFingerprint;
        if (!_byClass.TryGetValue(itemClass, out var ci)) return null;
        return ci.Index.ShortestUniqueSubstring(ci.ClassLineIndex);
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
    /// Used to validate value-threshold patterns against actual copy text.
    /// </summary>
    public bool RegexSafeOnClass(string itemClass, string regexFragment, IReadOnlyCollection<string> allowedPatterns)
    {
        if (!_byClass.TryGetValue(itemClass, out var ci)) return false;
        var matchedAny = false;
        foreach (var (pattern, real) in ci.AllRealLines)
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(real, regexFragment)) continue;
            matchedAny = true;
            if (!allowedPatterns.Contains(pattern)) return false;
        }
        return matchedAny;
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
            var classLineIndex = corpus.IndexOf(classLinePattern);
            var patternToIndex = new Dictionary<string, int>();
            for (var i = 0; i < corpus.Count; i++) patternToIndex[corpus[i]] = i;

            var allReal = allRealByClass.GetValueOrDefault(cls.Id) ?? new List<(string, string)>();
            var sortedReal = allReal.OrderBy(x => x.Pattern, StringComparer.Ordinal).ToList();
            result[cls.Id] = new ClassIndex
            {
                Index = index,
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

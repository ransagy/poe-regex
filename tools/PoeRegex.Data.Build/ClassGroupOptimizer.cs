using PoeRegex.Core.Compilation;
using PoeRegex.Core.Data;

namespace PoeRegex.Data.Build;

/// <summary>
/// Data-time search for exact multi-class terms used by paste class groups.
/// Generates candidate literals from the in-game class names, keeps only terms
/// that match exactly one class set and no modeled non-class line, and stores
/// the shortest term per set. Runtime composition is lookup-only.
/// </summary>
internal static class ClassGroupOptimizer
{
    private const int MinLiteral = 3;

    public static List<ClassGroupEntry> Build(
        EngineCorpus corpus, IReadOnlyList<string> classIds)
    {
        var classes = classIds
            .Select(id => (Id: id, Name: corpus.FindItemClass(id)?.Name ?? id))
            .Where(c => !string.IsNullOrEmpty(c.Name))
            .ToList();
        var literals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (_, name) in classes)
        {
            for (var i = 0; i < name.Length; i++)
            {
                for (var j = i + MinLiteral; j <= name.Length; j++)
                {
                    var literal = name[i..j].Trim();
                    if (literal.Length >= MinLiteral) literals.Add(literal);
                }
            }
        }

        var bySet = new Dictionary<string, ClassGroupEntry>(StringComparer.Ordinal);
        foreach (var literal in literals.OrderBy(x => x, StringComparer.Ordinal))
        {
            var containing = classes
                .Where(c => c.Name.Contains(literal, StringComparison.OrdinalIgnoreCase))
                .Select(c => c.Id)
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToList();
            if (containing.Count >= 2)
                Add(corpus, bySet, ".*" + Poe2Syntax.EscapeLiteral(literal), containing);

            var prefixed = classes
                .Where(c => c.Name.StartsWith(literal, StringComparison.OrdinalIgnoreCase))
                .Select(c => c.Id)
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToList();
            if (prefixed.Count >= 2)
                Add(corpus, bySet, Poe2Syntax.EscapeLiteral(literal), prefixed);
        }

        return bySet.Values
            .OrderBy(g => string.Join("\u0001", g.Classes), StringComparer.Ordinal)
            .ToList();
    }

    private static void Add(
        EngineCorpus corpus,
        Dictionary<string, ClassGroupEntry> bySet,
        string term,
        IReadOnlyList<string> classes)
    {
        if (!corpus.ClassTermExact(term, classes)) return;
        if (!corpus.ClassTermQuiet(term)) return;

        var key = string.Join("\u0001", classes);
        if (bySet.TryGetValue(key, out var current) &&
            (current.Term.Length < term.Length ||
             (current.Term.Length == term.Length &&
              string.CompareOrdinal(current.Term, term) <= 0)))
            return;
        bySet[key] = new ClassGroupEntry
        {
            Term = term,
            Classes = classes.ToList(),
        };
    }
}

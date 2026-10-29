namespace PoeRegex.Core.Compilation;

/// <summary>
/// Builds the alternation inside a paste class group (`: (a|b|…)`). Starts
/// from each class's unique name prefix and greedily folds terms that share a
/// suffix into `.*<suffix>` — accepted only when the merged term matches
/// exactly the same classes (by `Item Class:` line) and no universal or mod
/// line in the corpus — so one-hand/two-hand variants stop repeating in every
/// group token. Deterministic; merges only when the alternation gets shorter.
/// </summary>
public static class ClassAlternation
{
    private const int MinSuffix = 3;
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<EngineCorpus, System.Collections.Concurrent.ConcurrentDictionary<string, string>> Cache = new();

    private sealed record Term(string Text, IReadOnlyList<string> Classes, bool Plain);

    public static string Build(EngineCorpus corpus, IReadOnlyList<string> classIds)
    {
        var key = string.Join("\u0001",
            classIds.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal));
        var cache = Cache.GetOrCreateValue(corpus);
        return cache.GetOrAdd(key, _ => BuildCore(corpus, classIds));
    }

    private static string BuildCore(EngineCorpus corpus, IReadOnlyList<string> classIds)
    {
        var terms = classIds
            .Distinct(StringComparer.Ordinal)
            .Select(id => new Term(corpus.ClassNamePrefix(id), [id], Plain: true))
            .ToList();
        while (terms.Count > 1)
        {
            var bestScore = 0;
            var bestI = -1;
            var bestJ = -1;
            string? bestTerm = null;
            for (var i = 0; i < terms.Count; i++)
                for (var j = i + 1; j < terms.Count; j++)
                {
                    if (!terms[i].Plain || !terms[j].Plain) continue;
                    var suffix = CommonSuffix(terms[i].Text, terms[j].Text).Trim();
                    if (suffix.Length < MinSuffix) continue;
                    var candidate = ".*" + Poe2Syntax.EscapeLiteral(suffix);
                    var score = terms[i].Text.Length + terms[j].Text.Length + 1 - candidate.Length;
                    if (score <= bestScore) continue;
                    var classes = terms[i].Classes.Concat(terms[j].Classes).ToList();
                    if (!corpus.ClassTermExact(candidate, classes)) continue;
                    if (!corpus.ClassTermQuiet(candidate)) continue;
                    bestScore = score;
                    bestI = i;
                    bestJ = j;
                    bestTerm = candidate;
                }
            if (bestI < 0 || bestTerm is null) break;
            terms[bestI] = new Term(bestTerm, terms[bestI].Classes.Concat(terms[bestJ].Classes).ToList(), Plain: false);
            terms.RemoveAt(bestJ);
        }
        return string.Join("|", terms.Select(t => t.Plain ? Poe2Syntax.EscapeLiteral(t.Text) : t.Text));
    }

    private static string CommonSuffix(string a, string b)
    {
        var n = 0;
        while (n < a.Length && n < b.Length && a[a.Length - 1 - n] == b[b.Length - 1 - n]) n++;
        return a.Substring(a.Length - n);
    }
}

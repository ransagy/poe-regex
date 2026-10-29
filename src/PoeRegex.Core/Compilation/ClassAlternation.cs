namespace PoeRegex.Core.Compilation;

/// <summary>
/// Builds the alternation inside a paste class group (`: (a|b|…)`).
///
/// The runtime path is table lookup only: single classes use the shortest
/// precomputed anchor, and multi-class groups use the precomputed
/// <see cref="FragmentTable.ClassGroups"/> exact terms. A small exact-cover
/// search (or a greedy cover for unusually large scopes) picks the shortest
/// combination of disjoint groups plus single-class anchors. No runtime code
/// rescans the corpus or re-validates a candidate.
/// </summary>
public static class ClassAlternation
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<EngineCorpus, System.Collections.Concurrent.ConcurrentDictionary<string, string>> Cache = new();

    private sealed record Part(string Term, int Mask, int MinIndex, int ClassCount);

    public static string Build(EngineCorpus corpus, IReadOnlyList<string> classIds)
    {
        var key = string.Join("\u0001",
            classIds.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal));
        var cache = Cache.GetOrCreateValue(corpus);
        return cache.GetOrAdd(key, _ => BuildCore(corpus, classIds));
    }

    private static string BuildCore(EngineCorpus corpus, IReadOnlyList<string> classIds)
    {
        var distinct = classIds
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();
        if (distinct.Count == 0) return "";

        var index = distinct
            .Select((id, i) => (id, i))
            .ToDictionary(x => x.id, x => x.i, StringComparer.Ordinal);
        var parts = BuildParts(corpus, distinct, index);
        var chosen = distinct.Count <= 12
            ? ExactCover(parts, distinct.Count)
            : GreedyCover(parts, distinct.Count);
        chosen.Sort((a, b) => a.MinIndex.CompareTo(b.MinIndex));
        return string.Join("|", chosen.Select(p => p.Term));
    }

    /// <summary>
    /// Singleton anchors plus every precomputed group whose class set is
    /// wholly inside the requested scope. Keeps the shortest term per mask.
    /// </summary>
    private static List<Part> BuildParts(
        EngineCorpus corpus,
        IReadOnlyList<string> distinct,
        IReadOnlyDictionary<string, int> index)
    {
        var byMask = new Dictionary<int, Part>();
        for (var i = 0; i < distinct.Count; i++)
            byMask[1 << i] = new Part(corpus.ClassNameAnchor(distinct[i]), 1 << i, i, 1);

        foreach (var group in corpus.PrecomputedClassGroups)
        {
            if (group.Classes.Count < 2 || string.IsNullOrEmpty(group.Term)) continue;
            var mask = 0;
            var minIndex = int.MaxValue;
            var complete = true;
            foreach (var classId in group.Classes)
            {
                if (!index.TryGetValue(classId, out var i))
                {
                    complete = false;
                    break;
                }
                mask |= 1 << i;
                minIndex = Math.Min(minIndex, i);
            }
            if (!complete || mask == 0) continue;
            var candidate = new Part(group.Term, mask, minIndex, group.Classes.Count);
            if (!byMask.TryGetValue(mask, out var current) ||
                candidate.Term.Length < current.Term.Length ||
                (candidate.Term.Length == current.Term.Length &&
                 string.CompareOrdinal(candidate.Term, current.Term) < 0))
                byMask[mask] = candidate;
        }

        return byMask.Values
            .OrderByDescending(p => p.ClassCount)
            .ThenByDescending(p => p.Term.Length)
            .ThenBy(p => p.Term, StringComparer.Ordinal)
            .ToList();
    }

    private static List<Part> ExactCover(List<Part> parts, int classCount)
    {
        var full = (1 << classCount) - 1;
        var cost = new int[full + 1];
        var count = new int[full + 1];
        var choice = new Part?[full + 1];
        Array.Fill(cost, int.MaxValue);
        cost[0] = 0;

        for (var mask = 1; mask <= full; mask++)
        {
            foreach (var part in parts)
            {
                if ((part.Mask & mask) != part.Mask) continue;
                var rest = mask ^ part.Mask;
                if (cost[rest] == int.MaxValue) continue;
                var nextCost = part.Term.Length + (rest == 0 ? 0 : 1 + cost[rest]);
                var nextCount = 1 + (rest == 0 ? 0 : count[rest]);
                if (nextCost < cost[mask] ||
                    (nextCost == cost[mask] && nextCount < count[mask]))
                {
                    cost[mask] = nextCost;
                    count[mask] = nextCount;
                    choice[mask] = part;
                }
            }
        }

        var chosen = new List<Part>();
        for (var mask = full; mask != 0;)
        {
            var part = choice[mask];
            if (part is null) break;
            chosen.Add(part);
            mask ^= part.Mask;
        }
        return chosen;
    }

    private static List<Part> GreedyCover(List<Part> parts, int classCount)
    {
        var remaining = (1 << classCount) - 1;
        var chosen = new List<Part>();
        var singletons = parts.Where(p => p.ClassCount == 1).ToDictionary(p => p.Mask);
        var groups = parts.Where(p => p.ClassCount > 1).ToList();

        while (remaining != 0)
        {
            Part? best = null;
            var bestSaving = 0;
            foreach (var group in groups)
            {
                if ((group.Mask & remaining) != group.Mask) continue;
                var singletonCost = 0;
                for (var i = 0; i < classCount; i++)
                    if ((group.Mask & (1 << i)) != 0)
                        singletonCost += singletons[1 << i].Term.Length;
                var saving = singletonCost + (group.ClassCount - 1) - group.Term.Length;
                if (saving > bestSaving ||
                    (saving == bestSaving && best is not null &&
                     string.CompareOrdinal(group.Term, best.Term) < 0))
                {
                    bestSaving = saving;
                    best = group;
                }
            }

            if (best is not null && bestSaving > 0)
            {
                chosen.Add(best);
                remaining ^= best.Mask;
                continue;
            }

            var lowest = System.Numerics.BitOperations.TrailingZeroCount(remaining);
            var singleton = singletons[1 << lowest];
            chosen.Add(singleton);
            remaining ^= singleton.Mask;
        }
        return chosen;
    }
}

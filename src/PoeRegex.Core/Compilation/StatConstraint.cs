using PoeRegex.Core.Data;

namespace PoeRegex.Core.Compilation;

/// <summary>One allowed numeric span for a stat. Discrete tiers stay separate.</summary>
public readonly record struct ValueInterval(double Lo, double Hi)
{
    public bool Contains(double value) => value >= Lo - 1e-9 && value <= Hi + 1e-9;

    public bool Contains(ValueInterval other) =>
        other.Lo >= Lo - 1e-9 && other.Hi <= Hi + 1e-9;

    public bool Overlaps(double lo, double hi) =>
        lo <= Hi + 1e-9 && Lo <= hi + 1e-9;
}

/// <summary>
/// The semantic unit every layer shares: a stat family in a scope, the exact
/// allowed value intervals, and the tier names/members that produced them.
/// Representation (family fragment, range, affix name) is chosen later by the
/// planner, never by the picker or composer independently.
/// </summary>
public sealed record StatConstraint(
    string FamilyKey,
    string AnchorModId,
    IReadOnlyList<string> Patterns,
    IReadOnlyList<ValueInterval> Intervals,
    IReadOnlyList<string> MemberIds,
    IReadOnlyList<string> Names,
    bool IsFullFamily,
    bool FamilyHasMultipleValues,
    bool PreferRange)
{
    public double Min => Intervals.Count == 0 ? 0 : Intervals.Min(i => i.Lo);
    public double Max => Intervals.Count == 0 ? 0 : Intervals.Max(i => i.Hi);

    /// <summary>True when the constraint allows more than one displayed value.</summary>
    public bool CoversMultipleValues =>
        Intervals.Any(i => i.Hi > i.Lo) || DistinctValues().Count > 1;

    public IReadOnlyList<double> DistinctValues() =>
        Intervals.SelectMany(i => i.Lo == i.Hi ? [i.Lo] : new[] { i.Lo, i.Hi })
            .Distinct()
            .OrderBy(v => v)
            .ToList();
}

/// <summary>
/// Turns picker/compiler requirements into canonical constraints. This is the
/// single place where exact tiers, “or better” unions, family picks, and
/// min/max filters become the same interval set.
/// </summary>
public static class ConstraintCanonicalizer
{
    public static IReadOnlyList<StatConstraint> Canonicalize(
        IReadOnlyList<ModifierRequirement> requirements, string? itemClass, EngineCorpus corpus)
    {
        var groups = new Dictionary<string, Builder>(StringComparer.Ordinal);
        foreach (var requirement in requirements)
        {
            if (requirement.IsRegex) continue;
            var mod = corpus.FindMod(requirement.Value);
            if (mod is null) continue;

            var covered = requirement.RangeModIds is { Count: > 0 } ids
                ? ids.Select(corpus.FindMod).Where(m => m is not null).Select(m => m!).ToList()
                : [mod];
            if (covered.Count == 0) continue;

            // A plain-mod floor/ceiling means "this stat value or higher/lower"
            // across the family, not just the named tier. Expand to every
            // family member whose span intersects the filter.
            if (requirement.RangeModIds is not { Count: > 0 } &&
                (requirement.MinValue is not null || requirement.MaxValue is not null) &&
                mod.StatLines.Count == 1)
            {
                var pattern = mod.StatLines[0].Pattern;
                var familyName = mod.Family;
                var familyMods = corpus.ModsWithPattern(pattern)
                    .Where(m => itemClass is null ||
                                m.Classes.Contains(itemClass, StringComparer.Ordinal))
                    .Where(m => string.IsNullOrEmpty(familyName) ||
                                string.Equals(m.Family, familyName, StringComparison.Ordinal))
                    .Where(m => m.StatLines.Count == 1)
                    .Where(m =>
                    {
                        var line = m.StatLines[0];
                        var lo = requirement.MinValue is { } min
                            ? Math.Max(line.MinValue, min)
                            : line.MinValue;
                        var hi = requirement.MaxValue is { } max
                            ? Math.Min(line.MaxValue, max)
                            : line.MaxValue;
                        return hi >= lo && (lo != 0 || hi != 0);
                    })
                    .ToList();
                if (familyMods.Count > 0) covered = familyMods;
            }

            var patterns = covered
                .SelectMany(m => m.StatLines.Select(line => line.Pattern))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(pattern => pattern, StringComparer.Ordinal)
                .ToList();
            if (patterns.Count == 0) continue;
            var family = covered[0].Family;
            var key = (family.Length > 0 ? family : covered[0].Id) + "\u0001" +
                      string.Join("\u0001", patterns);
            if (!groups.TryGetValue(key, out var builder))
                groups[key] = builder = new Builder(key, requirement.Value, family, patterns);
            if (requirement.RangeModIds is { Count: > 0 } ||
                requirement.MinValue is not null ||
                requirement.MaxValue is not null)
                builder.PreferRange = true;

            foreach (var member in covered)
            {
                builder.MemberIds.Add(member.Id);
                if (!string.IsNullOrWhiteSpace(member.Name)) builder.Names.Add(member.Name);
                foreach (var line in member.StatLines)
                {
                    if (!patterns.Contains(line.Pattern, StringComparer.Ordinal)) continue;
                    var lo = requirement.MinValue is { } min
                        ? Math.Max(line.MinValue, min)
                        : line.MinValue;
                    var hi = requirement.MaxValue is { } max
                        ? Math.Min(line.MaxValue, max)
                        : line.MaxValue;
                    if (hi >= lo && (lo != 0 || hi != 0))
                        builder.Intervals.Add(new ValueInterval(lo, hi));
                }
            }
        }

        var result = new List<StatConstraint>(groups.Count);
        foreach (var builder in groups.Values.OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var intervals = MergeIntervals(builder.Intervals);
            // Non-numeric/fixed-unknown lines (0/0) have no interval but still
            // need the table/family representation. Do not drop them.
            var fullFamily = intervals.Count == 0 ||
                             IsFullFamily(builder.Patterns, builder.Family, intervals, itemClass, corpus);
            var familyValues = FamilyValues(builder.Patterns, builder.Family, itemClass, corpus);
            result.Add(new StatConstraint(
                builder.Key,
                builder.AnchorModId,
                builder.Patterns,
                intervals,
                builder.MemberIds.OrderBy(id => id, StringComparer.Ordinal).ToList(),
                builder.Names.OrderBy(name => name, StringComparer.Ordinal).ToList(),
                fullFamily,
                familyValues.Count > 1,
                builder.PreferRange));
        }
        return result;
    }

    /// <summary>All canonical constraints plus the raw regex/free-text requirements.</summary>
    public static (IReadOnlyList<StatConstraint> Constraints, IReadOnlyList<ModifierRequirement> Raw)
        Split(IReadOnlyList<ModifierRequirement> requirements, string? itemClass, EngineCorpus corpus)
    {
        var raw = requirements
            .Where(r => r.IsRegex || corpus.FindMod(r.Value) is null)
            .ToList();
        return (Canonicalize(requirements, itemClass, corpus), raw);
    }

    public static IReadOnlyList<ValueInterval> MergeIntervals(
        IEnumerable<ValueInterval> intervals, double step = 0)
    {
        var ordered = intervals
            .Where(i => i.Hi >= i.Lo)
            .OrderBy(i => i.Lo)
            .ThenBy(i => i.Hi)
            .ToList();
        var result = new List<ValueInterval>(ordered.Count);
        foreach (var current in ordered)
        {
            if (result.Count == 0)
            {
                result.Add(current);
                continue;
            }
            var last = result[^1];
            // Only merge overlapping/adjacent spans. Discrete tiers such as
            // 20 and 25 stay separate, so the canonical set is lossless.
            if (current.Lo <= last.Hi + step + 1e-9)
                result[^1] = new ValueInterval(last.Lo, Math.Max(last.Hi, current.Hi));
            else
                result.Add(current);
        }
        return result;
    }

    private static bool IsFullFamily(
        IReadOnlyList<string> patterns,
        string family,
        IReadOnlyList<ValueInterval> intervals,
        string? itemClass,
        EngineCorpus corpus)
    {
        var lines = PatternLines(patterns, family, itemClass, corpus);
        return lines.Count > 0 &&
               lines.All(line => intervals.Any(interval =>
                   interval.Contains(new ValueInterval(line.MinValue, line.MaxValue))));
    }

    private static HashSet<double> FamilyValues(
        IReadOnlyList<string> patterns, string family, string? itemClass, EngineCorpus corpus)
    {
        var values = new HashSet<double>();
        foreach (var line in PatternLines(patterns, family, itemClass, corpus))
        {
            if (line.MaxValue > line.MinValue) return new HashSet<double> { line.MinValue, line.MaxValue };
            if (line.MinValue != 0) values.Add(line.MinValue);
        }
        return values;
    }

    private static List<StatLineEntry> PatternLines(
        IReadOnlyList<string> patterns, string family, string? itemClass, EngineCorpus corpus)
    {
        var result = new List<StatLineEntry>();
        foreach (var pattern in patterns)
        {
            foreach (var mod in corpus.ModsWithPattern(pattern))
            {
                if (itemClass is not null &&
                    !mod.Classes.Contains(itemClass, StringComparer.Ordinal))
                    continue;
                if (!string.IsNullOrEmpty(family) &&
                    !string.Equals(mod.Family, family, StringComparison.Ordinal))
                    continue;
                foreach (var line in mod.StatLines)
                {
                    if (string.Equals(line.Pattern, pattern, StringComparison.Ordinal))
                        result.Add(line);
                }
            }
        }
        return result;
    }

    private sealed class Builder(string key, string anchorModId, string family, IReadOnlyList<string> patterns)
    {
        public string Key { get; } = key;
        public string AnchorModId { get; } = anchorModId;
        public string Family { get; } = family;
        public IReadOnlyList<string> Patterns { get; } = patterns;
        public List<ValueInterval> Intervals { get; } = [];
        public HashSet<string> MemberIds { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Names { get; } = new(StringComparer.Ordinal);
        public bool PreferRange { get; set; }
    }
}

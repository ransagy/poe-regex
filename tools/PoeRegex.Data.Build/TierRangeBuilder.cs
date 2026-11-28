using System.Globalization;
using PoeRegex.Core;
using PoeRegex.Core.Compilation;
using PoeRegex.Core.Data;
using PoeRegex.Core.Search;
using PoeRegex.Web.Core;

namespace PoeRegex.Data.Build;

/// <summary>
/// Precomputes the exact tier-range fragments the picker uses for
/// range-first families. The browser otherwise pays for the first range
/// proof on the first exact tier pick.
/// </summary>
internal sealed record TierRangeData(
    Dictionary<string, string> Fragments,
    Dictionary<string, TierRangePlan> Plans);

internal static class TierRangeBuilder
{
    public static TierRangeData Build(EngineCorpus corpus)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var planEntries = new Dictionary<string, List<(double Lo, double Hi, string Template, string Value)>>(
            StringComparer.Ordinal);
        var planMeta = new Dictionary<string, (string Pattern, string? Class, int Precision)>(StringComparer.Ordinal);
        var engine = new CompactionEngine(corpus);
        var scopes = new List<string?> { null };
        scopes.AddRange(corpus.InGameClasses.Select(c => (string?)c.Id));
        foreach (var classId in scopes)
        {
            var mods = classId is null
                ? corpus.InGameMods
                : corpus.InGameMods
                    .Where(m => m.Classes.Contains(classId, StringComparer.Ordinal))
                    .ToList();
            if (mods.Count == 0) continue;
            foreach (var family in ModFamilies.Group(mods))
            {
                if (!HasMultipleValues(family)) continue;
                foreach (var tier in family.Tiers)
                {
                    if (tier.Members.Count == 0) continue;
                    if (tier.Members.Any(m => m.StatLines.Count != 1)) continue;
                    var lines = tier.Members.SelectMany(m => m.StatLines).ToList();
                    if (lines.Count == 0 || lines.Any(line => line.MaxValue < line.MinValue))
                        continue;
                    var pattern = lines[0].Pattern;
                    if (lines.Any(line => !string.Equals(
                            line.Pattern, pattern, StringComparison.Ordinal)))
                        continue;
                    var min = lines.Min(line => line.MinValue);
                    var max = lines.Max(line => line.MaxValue);
                    if (max < min) continue;
                    var ids = tier.Members
                        .Select(member => member.Id)
                        .Distinct(StringComparer.Ordinal)
                        .OrderBy(id => id, StringComparer.Ordinal)
                        .ToList();
                    var precision = lines.Max(line => Math.Max(
                        RangeRegex.PrecisionOf(line.MinValue),
                        RangeRegex.PrecisionOf(line.MaxValue)));
                    var intervals = lines
                        .Select(line => (Lo: line.MinValue, Hi: line.MaxValue))
                        .Where(interval => interval.Hi >= interval.Lo)
                        .ToList();
                    var requirement = new ModifierRequirement(
                        tier.Anchor.Id,
                        MinValue: min,
                        MaxValue: max,
                        RangeModIds: ids);
                    var fragment = engine.SelectFragment(requirement, classId);
                    if (string.IsNullOrEmpty(fragment)) continue;
                    var key = CompactionEngine.RangePlanKey(
                        pattern, intervals, precision, classId, [pattern]);
                    result.TryAdd(key, fragment);
                    var planKey = TierRangePlan.Key(pattern, classId);
                    planMeta[planKey] = (pattern, classId, precision);
                    if (!planEntries.TryGetValue(planKey, out var entries))
                        planEntries[planKey] = entries = [];
                    foreach (var split in DeriveValueSplits(
                                 fragment, intervals, precision, lines))
                    {
                        foreach (var interval in intervals)
                            entries.Add((interval.Lo, interval.Hi, split.Template, split.Value));
                    }
                }
            }
        }

        var plans = new Dictionary<string, TierRangePlan>(StringComparer.Ordinal);
        foreach (var (key, entries) in planEntries)
        {
            var chosen = entries
                .GroupBy(entry => entry.Template, StringComparer.Ordinal)
                .OrderByDescending(group => group.Select(entry => (entry.Lo, entry.Hi)).Distinct().Count())
                .ThenBy(group => group.Key, StringComparer.Ordinal)
                .FirstOrDefault();
            if (chosen is null) continue;
            var meta = planMeta[key];
            var chosenTiers = chosen
                .GroupBy(entry => (entry.Lo, entry.Hi))
                .Select(group => new TierRangeValue
                {
                    Lo = group.Key.Lo,
                    Hi = group.Key.Hi,
                    Value = group.First().Value,
                })
                .OrderBy(tier => tier.Lo)
                .ThenBy(tier => tier.Hi)
                .ToList();

            var filledTiers = new List<TierRangeValue>();
            foreach (var interval in entries
                         .Select(entry => (entry.Lo, entry.Hi))
                         .Distinct()
                         .OrderBy(item => item.Lo)
                         .ThenBy(item => item.Hi))
            {
                var value = FindPlanValue(
                    engine, chosen.Key, meta.Pattern, meta.Class, meta.Precision,
                    interval, entries);
                if (value is null)
                {
                    filledTiers = null;
                    break;
                }
                filledTiers.Add(new TierRangeValue
                {
                    Lo = interval.Lo,
                    Hi = interval.Hi,
                    Value = value,
                });
            }

            TierRangePlan? plan = null;
            if (filledTiers is { Count: >= 2 })
            {
                var filled = new TierRangePlan
                {
                    Template = chosen.Key,
                    Tiers = filledTiers,
                    UnionValues = BuildUnionValues(
                        engine, chosen.Key, meta.Pattern, meta.Class,
                        meta.Precision, filledTiers),
                };
                if (VerifyPlan(engine, filled, meta))
                    plan = filled;
            }
            if (plan is null && chosenTiers.Count >= 2)
            {
                var original = new TierRangePlan
                {
                    Template = chosen.Key,
                    Tiers = chosenTiers,
                };
                if (VerifyPlanLegacy(engine, original, meta))
                    plan = original;
            }
            if (plan is not null)
                plans[key] = plan;
        }
        return new TierRangeData(result, plans);
    }

    private static IEnumerable<string> ValueCandidates(
        IReadOnlyList<(double Lo, double Hi)> intervals, int precision)
    {
        var candidates = new List<string>();
        if (precision <= 0)
        {
            var values = new HashSet<long>();
            foreach (var (lo, hi) in intervals)
            {
                var first = (long)Math.Ceiling(lo - 1e-9);
                var last = (long)Math.Floor(hi + 1e-9);
                if (last - first > 5000) { values.Clear(); break; }
                for (var value = first; value <= last; value++) values.Add(value);
            }
            if (values.Count > 0)
                candidates.AddRange(RangeRegex.ExactIntegerCandidates(
                    values.OrderBy(value => value).ToList()));
        }
        candidates.AddRange(RangeRegex.Candidates(intervals, precision));
        return candidates.Distinct(StringComparer.Ordinal);
    }

    private static string? FindPlanValue(
        CompactionEngine engine, string template, string pattern, string? itemClass,
        int precision, (double Lo, double Hi) interval,
        IReadOnlyList<(double Lo, double Hi, string Template, string Value)> entries)
    {
        var candidates = entries
            .Where(entry => entry.Lo == interval.Lo && entry.Hi == interval.Hi)
            .Select(entry => entry.Value)
            .Concat(ValueCandidates([interval], precision))
            .Distinct(StringComparer.Ordinal)
            .Where(value => value.Length > 0 &&
                RangeRegex.MatchesIntervals(value, [interval], precision))
            .OrderBy(value => value.Length)
            .ThenBy(value => value, StringComparer.Ordinal);
        foreach (var value in candidates)
        {
            var valueText = value.Contains('|') ? "(" + value + ")" : value;
            var fragment = template.Replace("{0}", valueText, StringComparison.Ordinal);
            if (engine.ProveRangeFragment(
                    fragment, [pattern], [interval], itemClass,
                    exact: IsExact(interval.Lo, interval.Hi)))
                return value;
        }
        return null;
    }

    private static Dictionary<string, string> BuildUnionValues(
        CompactionEngine engine, string template, string pattern, string? itemClass,
        int precision, IReadOnlyList<TierRangeValue> tiers)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var start = 0; start < tiers.Count; start++)
        for (var end = start + 1; end < tiers.Count; end++)
        {
            var selected = tiers.Skip(start).Take(end - start + 1).ToList();
            if (selected.All(tier => IsExact(tier.Lo, tier.Hi))) continue;
            var intervals = selected.Select(tier => (tier.Lo, tier.Hi)).ToList();
            var exact = false;
            var candidate = ValueCandidates(intervals, precision)
                .Distinct(StringComparer.Ordinal)
                .Where(value => RangeRegex.MatchesIntervals(value, intervals, precision))
                .OrderBy(value => value.Length)
                .ThenBy(value => value, StringComparer.Ordinal)
                .FirstOrDefault(value =>
                {
                    var valueText = value.Contains('|') ? "(" + value + ")" : value;
                    var fragment = template.Replace(
                        "{0}", valueText, StringComparison.Ordinal);
                    return engine.ProveRangeFragment(
                        fragment, [pattern], intervals, itemClass, exact);
                });
            if (candidate is not null)
                result[TierRangePlan.UnionKey(intervals)] = candidate;
        }
        return result;
    }

    private static bool VerifyPlan(
        CompactionEngine engine, TierRangePlan plan,
        (string Pattern, string? Class, int Precision) meta)
    {
        var intervals = plan.Tiers.Select(tier => (tier.Lo, tier.Hi)).ToList();
        for (var start = 0; start < intervals.Count; start++)
        for (var end = start + 1; end < intervals.Count; end++)
        {
            var subset = intervals.GetRange(start, end - start + 1);
            var fragment = TierRangePlan.Compose(plan, subset);
            if (fragment is null ||
                !engine.ProveRangeFragment(
                    fragment, [meta.Pattern], subset, meta.Class,
                    exact: plan.Tiers
                        .Skip(start)
                        .Take(end - start + 1)
                        .All(tier => IsExact(tier.Lo, tier.Hi))))
                return false;
        }
        return true;
    }

    private static bool VerifyPlanLegacy(
        CompactionEngine engine, TierRangePlan plan,
        (string Pattern, string? Class, int Precision) meta)
    {
        var intervals = plan.Tiers.Select(tier => (tier.Lo, tier.Hi)).ToList();
        var full = TierRangePlan.Compose(plan, intervals);
        if (full is null ||
            !engine.ProveRangeFragment(full, [meta.Pattern], intervals, meta.Class,
                exact: plan.Tiers.All(tier => IsExact(tier.Lo, tier.Hi))))
            return false;
        if (intervals.Count < 2) return true;
        var pairIntervals = intervals.Take(2).ToList();
        var pair = TierRangePlan.Compose(plan, pairIntervals);
        return pair is not null &&
               engine.ProveRangeFragment(pair, [meta.Pattern], pairIntervals, meta.Class,
                   exact: plan.Tiers.Take(2).All(tier => IsExact(tier.Lo, tier.Hi)));
    }

    private static bool IsExact(double lo, double hi) =>
        Math.Abs(lo - hi) < 1e-9 &&
        Math.Abs(lo - Math.Round(lo)) < 1e-9;

    private static IEnumerable<(string Template, string Value)> DeriveValueSplits(
        string fragment,
        IReadOnlyList<(double Lo, double Hi)> intervals,
        int precision,
        IReadOnlyList<StatLineEntry> lines)
    {
        var candidates = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in RangeRegex.Candidates(intervals, precision))
            candidates.Add(candidate);
        if (precision == 0)
        {
            var values = lines
                .Select(line => (long)Math.Round(line.MinValue))
                .Concat(lines.Select(line => (long)Math.Round(line.MaxValue)))
                .Distinct()
                .OrderBy(value => value)
                .ToList();
            if (values.Count > 0)
            {
                foreach (var candidate in RangeRegex.ExactIntegerCandidates(values))
                    candidates.Add(candidate);
            }
        }
        if (intervals.Count == 1 &&
            Math.Abs(intervals[0].Lo - intervals[0].Hi) < 1e-9)
        {
            var value = intervals[0].Lo;
            candidates.Add(precision <= 0
                ? ((long)Math.Round(value)).ToString(CultureInfo.InvariantCulture)
                : value.ToString(
                    "0." + new string('#', precision), CultureInfo.InvariantCulture));
        }
        foreach (var candidate in candidates
                     .Where(candidate => candidate.Length > 0 &&
                         RangeRegex.MatchesIntervals(candidate, intervals, precision))
                     .OrderByDescending(candidate => candidate.Length)
                     .ThenBy(candidate => candidate, StringComparer.Ordinal))
        {
            var index = fragment.IndexOf(candidate, StringComparison.Ordinal);
            if (index < 0) continue;
            var prefix = fragment[..index];
            var suffix = fragment[(index + candidate.Length)..];
            if (prefix.EndsWith('(') && suffix.StartsWith(')'))
            {
                prefix = prefix[..^1];
                suffix = suffix[1..];
            }
            yield return (prefix + "{0}" + suffix, candidate);
        }
    }

    private static bool HasMultipleValues(ModFamily family)
    {
        var values = new HashSet<double>();
        foreach (var tier in family.Tiers)
        {
            if (tier.ValueRange is not { } range) continue;
            if (range.Max > range.Min) return true;
            if (range.Min != 0) values.Add(range.Min);
        }
        return values.Count > 1;
    }
}

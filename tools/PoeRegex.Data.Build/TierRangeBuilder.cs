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
        var planMeta = new Dictionary<string, (string Pattern, string? Class)>(StringComparer.Ordinal);
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
                    planMeta[planKey] = (pattern, classId);
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
            var tiers = chosen
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
            if (tiers.Count < 2) continue;
            var plan = new TierRangePlan
            {
                Template = chosen.Key,
                Tiers = tiers,
            };
            if (!VerifyPlan(engine, plan, planMeta[key]))
                continue;
            plans[key] = plan;
        }
        return new TierRangeData(result, plans);
    }

    private static bool VerifyPlan(
        CompactionEngine engine, TierRangePlan plan, (string Pattern, string? Class) meta)
    {
        var intervals = plan.Tiers.Select(tier => (tier.Lo, tier.Hi)).ToList();
        var full = TierRangePlan.Compose(plan, intervals);
        if (full is null ||
            !engine.ProveRangeFragment(full, [meta.Pattern], intervals, meta.Class,
                exact: plan.Tiers.All(tier =>
                    Math.Abs(tier.Lo - tier.Hi) < 1e-9 &&
                    Math.Abs(tier.Lo - Math.Round(tier.Lo)) < 1e-9)))
            return false;
        if (intervals.Count >= 2)
        {
            var pairIntervals = intervals.Take(2).ToList();
            var pair = TierRangePlan.Compose(plan, pairIntervals);
            if (pair is null ||
                !engine.ProveRangeFragment(pair, [meta.Pattern], pairIntervals, meta.Class,
                    exact: plan.Tiers.Take(2).All(tier =>
                        Math.Abs(tier.Lo - tier.Hi) < 1e-9 &&
                        Math.Abs(tier.Lo - Math.Round(tier.Lo)) < 1e-9)))
                return false;
        }
        return true;
    }

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
                     .Where(candidate => candidate.Length > 0 && !candidate.Contains('|'))
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

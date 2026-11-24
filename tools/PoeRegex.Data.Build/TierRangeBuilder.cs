using PoeRegex.Core;
using PoeRegex.Core.Compilation;
using PoeRegex.Core.Search;
using PoeRegex.Web.Core;

namespace PoeRegex.Data.Build;

/// <summary>
/// Precomputes the exact tier-range fragments the picker uses for
/// range-first families. The browser otherwise pays for the first range
/// proof on the first exact tier pick.
/// </summary>
internal static class TierRangeBuilder
{
    public static Dictionary<string, string> Build(EngineCorpus corpus)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
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
                }
            }
        }
        return result;
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

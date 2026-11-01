using PoeRegex.Core;
using PoeRegex.Core.Compilation;
using PoeRegex.Core.Data;
using PoeRegex.Core.Search;

namespace PoeRegex.Web.Core;

/// <summary>
/// Shared min-value capability rules. The data build uses the same probe to
/// precompute the table the browser reads.
/// </summary>
public static class MinCapability
{
    /// <summary>
    /// Cheap catalog-only gate: plain pick, one stat line, a value to
    /// threshold, and every family variant sharing that shape.
    /// </summary>
    public static bool MayAcceptMin(WantedMod pick, Func<string, ModEntry?> findMod)
    {
        if (pick.IsNamePick) return false;
        var ids = pick.VariantModIds is { Count: > 0 } ? pick.VariantModIds : [pick.ModId];
        foreach (var id in ids)
        {
            var mod = findMod(id);
            if (mod is not { StatLines.Count: 1 }) return false;
            if (!mod.StatLines[0].Pattern.Contains(SuffixIndex.DigitPlaceholder)) return false;
        }
        return ids.Count > 0;
    }

    /// <summary>
    /// The exact probe: a minimum changes the compiled fragment only when the
    /// base and min=1 queries differ.
    /// </summary>
    public static bool Probe(
        PoeRegexCompiler compiler, Func<string, ModEntry?> findMod,
        WantedMod pick, string? itemClass)
    {
        if (!MayAcceptMin(pick, findMod)) return false;
        var baseQuery = compiler.Compile(Spec(pick, itemClass, null)).Query;
        var minQuery = compiler.Compile(Spec(pick, itemClass, 1)).Query;
        return !string.Equals(baseQuery, minQuery, StringComparison.Ordinal);
    }

    private static QuerySpecification Spec(
        WantedMod pick, string? itemClass, double? minValue) =>
        new()
        {
            ItemClass = itemClass,
            IncludedModifiers = pick.Requirements()
                .Select(r => minValue is { } min ? r with { MinValue = min } : r)
                .ToList(),
            AllowOverflow = true,
        };
}

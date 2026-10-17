using PoeRegex.Core.Data;

namespace PoeRegex.Web.Core;

/// <summary>
/// Canonical item-display order: the game lists implicits, then all
/// prefixes, then all suffixes. Sorting is stable, so same-kind lines keep
/// their original (paste/add) order.
/// </summary>
public static class ItemDisplay
{
    public static int Rank(string? kind) => kind?.Trim().ToLowerInvariant() switch
    {
        "implicit" => 0,
        "prefix" => 1,
        "suffix" => 2,
        _ => 3,
    };

    /// <summary>Catalog mod ids in canonical order (unknown kinds last).</summary>
    public static IReadOnlyList<string> OrderModIds(EngineService engine, IEnumerable<string> ids) =>
        ids.OrderBy(id => Rank(engine.FindMod(id)?.GenerationType)).ToList();
}

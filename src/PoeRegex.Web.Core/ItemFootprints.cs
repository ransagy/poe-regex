namespace PoeRegex.Web.Core;

/// <summary>How a footprint value was sourced.</summary>
public enum FootprintConfidence
{
    /// <summary>Given by the maintainer.</summary>
    Confirmed,
    /// <summary>No data yet; fallback used.</summary>
    Unknown,
}

public sealed record ItemFootprint(int Cols, int Rows, FootprintConfidence Confidence);

/// <summary>
/// Backpack cell footprints per item class (maintainer-confirmed table;
/// see docs/WEB-APP-PLAN.md §8.2). Anything unlisted falls back to 1x3.
/// </summary>
public static class ItemFootprints
{
    private static readonly Dictionary<string, ItemFootprint> Table = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Helmet"] = new(2, 2, FootprintConfidence.Confirmed),
        ["Body Armour"] = new(2, 3, FootprintConfidence.Confirmed),
        ["Gloves"] = new(2, 2, FootprintConfidence.Confirmed),
        ["Boots"] = new(2, 2, FootprintConfidence.Confirmed),
        ["Belt"] = new(2, 1, FootprintConfidence.Confirmed),
        ["Amulet"] = new(1, 1, FootprintConfidence.Confirmed),
        ["Ring"] = new(1, 1, FootprintConfidence.Confirmed),
        ["Shield"] = new(2, 4, FootprintConfidence.Confirmed),
        ["Focus"] = new(2, 3, FootprintConfidence.Confirmed),
        ["Buckler"] = new(2, 2, FootprintConfidence.Confirmed),
        ["One Hand Mace"] = new(2, 3, FootprintConfidence.Confirmed),
        ["Two Hand Mace"] = new(2, 4, FootprintConfidence.Confirmed),
        ["Wand"] = new(1, 3, FootprintConfidence.Confirmed),
        ["Staff"] = new(1, 4, FootprintConfidence.Confirmed),
        ["Spear"] = new(1, 4, FootprintConfidence.Confirmed),
        ["Bow"] = new(2, 4, FootprintConfidence.Confirmed),
        ["Crossbow"] = new(2, 4, FootprintConfidence.Confirmed),
        ["Sceptre"] = new(2, 3, FootprintConfidence.Confirmed),
        ["Quiver"] = new(2, 3, FootprintConfidence.Confirmed),
        ["Talisman"] = new(2, 2, FootprintConfidence.Confirmed),
    };

    /// <summary>Fallback for classes with no data (currency, gems, unreleased bases).</summary>
    public static readonly ItemFootprint Fallback = new(1, 3, FootprintConfidence.Unknown);

    public static ItemFootprint For(string? classId)
    {
        if (classId is not null && Table.TryGetValue(classId, out var fp))
            return fp;
        return Fallback;
    }
}

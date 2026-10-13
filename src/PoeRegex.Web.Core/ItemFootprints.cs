namespace PoeRegex.Web.Core;

/// <summary>How a footprint value was sourced.</summary>
public enum FootprintConfidence
{
    /// <summary>Given by the maintainer.</summary>
    Confirmed,
    /// <summary>Author's reading of a related confirmation (needs verify).</summary>
    Inferred,
    /// <summary>No data yet; fallback used.</summary>
    Unknown,
}

public sealed record ItemFootprint(int Cols, int Rows, FootprintConfidence Confidence);

/// <summary>
/// Backpack cell footprints per item class. Values come from the maintainer
/// (Confirmed), are read off related confirmations (Inferred), or fall back
/// to 1x3 (Unknown). See the Phase 5 QA gap list in docs/WEB-APP-PLAN.md for
/// classes still needing real numbers.
/// </summary>
public static class ItemFootprints
{
    private static readonly Dictionary<string, ItemFootprint> Table = new(StringComparer.OrdinalIgnoreCase)
    {
        // Armour / accessories (confirmed).
        ["Helmet"] = new(2, 2, FootprintConfidence.Confirmed),
        ["Body Armour"] = new(2, 3, FootprintConfidence.Confirmed),
        ["Gloves"] = new(2, 2, FootprintConfidence.Confirmed),
        ["Boots"] = new(2, 2, FootprintConfidence.Confirmed),
        ["Belt"] = new(2, 1, FootprintConfidence.Confirmed),
        ["Amulet"] = new(1, 1, FootprintConfidence.Confirmed),
        ["Ring"] = new(1, 1, FootprintConfidence.Confirmed),
        // Shields: armour shields 2x4 (confirmed); "others" 2x3 read as Focus (inferred); bucklers 2x2 (confirmed).
        ["Shield"] = new(2, 4, FootprintConfidence.Confirmed),
        ["Focus"] = new(2, 3, FootprintConfidence.Inferred),
        ["Buckler"] = new(2, 2, FootprintConfidence.Confirmed),
        // Weapons with confirmed numbers.
        ["One Hand Mace"] = new(2, 3, FootprintConfidence.Confirmed),
        ["Wand"] = new(1, 3, FootprintConfidence.Confirmed),
        ["Staff"] = new(1, 4, FootprintConfidence.Confirmed),
        ["Warstaff"] = new(1, 4, FootprintConfidence.Inferred),
        ["Spear"] = new(1, 4, FootprintConfidence.Confirmed),
    };

    /// <summary>Fallback for classes with no data yet (flagged Unknown).</summary>
    public static readonly ItemFootprint Fallback = new(1, 3, FootprintConfidence.Unknown);

    public static ItemFootprint For(string? classId)
    {
        if (classId is not null && Table.TryGetValue(classId, out var fp))
            return fp;
        return Fallback;
    }
}

namespace PoeRegex.Web.Core;

/// <summary>
/// Which item classes fit each gear slot. Armour/accessory slots are 1:1;
/// the weapon and offhand slots take everything else — weapons and offhands
/// alike (dual wielding is legal, and pointing both slots at different base
/// types is the intended stand-in while weapon sets don't exist).
/// </summary>
public static class SlotClasses
{
    private static readonly HashSet<string> NonWeapon = new(StringComparer.OrdinalIgnoreCase)
    {
        "Helmet", "Body Armour", "Gloves", "Boots", "Belt", "Amulet", "Ring",
    };

    public static IReadOnlyList<string> AllowedFor(string slotId, IEnumerable<string> playableIds)
    {
        var playable = playableIds.ToList();
        return slotId switch
        {
            "helmet" => Filter(playable, "Helmet"),
            "body" => Filter(playable, "Body Armour"),
            "gloves" => Filter(playable, "Gloves"),
            "boots" => Filter(playable, "Boots"),
            "belt" => Filter(playable, "Belt"),
            "amulet" => Filter(playable, "Amulet"),
            "ring1" => Filter(playable, "Ring"),
            "ring2" => Filter(playable, "Ring"),
            "weapon" => playable.Where(c => !NonWeapon.Contains(c)).ToList(),
            "offhand" => playable.Where(c => !NonWeapon.Contains(c)).ToList(),
            _ => playable,
        };
    }

    private static IReadOnlyList<string> Filter(List<string> playable, string id) =>
        playable.Where(c => c.Equals(id, StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>
    /// Rings are the only intentional duplicate-class slots. Weapon and
    /// offhand may not both target the same non-armour class; two armour
    /// slots are 1:1 by definition.
    /// </summary>
    public static bool AllowsDuplicateClass(string classId, string slotA, string slotB) =>
        !string.Equals(slotA, slotB, StringComparison.Ordinal) &&
        string.Equals(classId, "Ring", StringComparison.OrdinalIgnoreCase) &&
        slotA is "ring1" or "ring2" &&
        slotB is "ring1" or "ring2";
}

namespace PoeRegex.Web.Core;

/// <summary>
/// Which item classes fit each gear slot. Armour/accessory slots are 1:1;
/// the weapon slot takes weapons and offhands (everything playable except
/// the slotted armour/accessory classes and talismans).
/// </summary>
public static class SlotClasses
{
    private static readonly HashSet<string> NonWeapon = new(StringComparer.OrdinalIgnoreCase)
    {
        "Helmet", "Body Armour", "Gloves", "Boots", "Belt", "Amulet", "Ring", "Talisman",
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
            _ => playable,
        };
    }

    private static IReadOnlyList<string> Filter(List<string> playable, string id) =>
        playable.Where(c => c.Equals(id, StringComparison.OrdinalIgnoreCase)).ToList();
}

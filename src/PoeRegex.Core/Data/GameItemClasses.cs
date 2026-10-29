namespace PoeRegex.Core.Data;

/// <summary>
/// Item classes with no released bases in PoE2. They stay in the catalog (the
/// raw export still carries them), but they must not create matrix scopes,
/// shipped table entries, pickers, or global safety lines.
/// </summary>
public static class GameItemClasses
{
    public static readonly HashSet<string> NotInGame = new(StringComparer.OrdinalIgnoreCase)
    {
        "Dagger", "Claw", "Flail",
        "One Hand Sword", "One Hand Axe", "Two Hand Sword", "Two Hand Axe",
        "FishingRod", "TrapTool",
    };

    public static bool IsInGame(string? classId) =>
        classId is not null && !NotInGame.Contains(classId);

    public static IReadOnlyList<ItemClassEntry> InGame(Catalog catalog) =>
        catalog.ItemClasses.Where(c => IsInGame(c.Id)).ToList();
}

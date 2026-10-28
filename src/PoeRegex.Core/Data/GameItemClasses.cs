namespace PoeRegex.Core.Data;

/// <summary>
/// Item classes with no released bases in the current game. They stay in the
/// catalog (the raw export still carries them), but they must not create
/// matrix scopes, shipped table entries, pickers, or global safety lines.
/// </summary>
public static class GameItemClasses
{
    public static readonly HashSet<string> NotInGame = new(StringComparer.OrdinalIgnoreCase)
    {
        "Dagger", "Claw", "Flail",
        "One Hand Sword", "One Hand Axe", "Two Hand Sword", "Two Hand Axe",
        "FishingRod", "TrapTool",
    };

    public static bool IsInGame(GameTarget game, string? classId) =>
        classId is not null &&
        (game != GameTarget.PoE2 || !NotInGame.Contains(classId));

    public static bool IsInGame(Catalog catalog, string? classId) =>
        IsInGame(Parse(catalog.Game), classId);

    public static IReadOnlyList<ItemClassEntry> InGame(Catalog catalog) =>
        catalog.ItemClasses
            .Where(c => IsInGame(catalog, c.Id))
            .ToList();

    private static GameTarget Parse(string game) =>
        string.Equals(game, "poe1", StringComparison.OrdinalIgnoreCase)
            ? GameTarget.PoE1
            : GameTarget.PoE2;
}

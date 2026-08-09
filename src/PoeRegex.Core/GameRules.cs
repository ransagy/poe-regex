namespace PoeRegex.Core;

/// <summary>
/// Per-game rules that constrain how a query is assembled and validated.
/// Rules come from an embedded JSON document so they can be updated without
/// recompiling; an optional on-disk override file may replace them at load.
/// </summary>
public sealed record GameRules
{
    public const int DefaultMaxCharacterLimit = 250;

    public string Game { get; init; } = "poe1";

    public int MaxCharacterLimit { get; init; } = DefaultMaxCharacterLimit;

    public bool SupportsLookahead { get; init; }

    public bool RequireQuotedBlocks { get; init; } = true;

    /// <summary>
    /// Feature flag: whether mod names may anchor lines. Off until in-game
    /// verified; the engine never emits mod-name anchors.
    /// </summary>
    public bool SupportsModNameAnchors { get; init; }

    /// <summary>Custom keyword prefixes supported by the game (e.g. "ilvl:").</summary>
    public IReadOnlyList<string> CustomKeywords { get; init; } = [];

    public static GameRules For(GameTarget target) =>
        target switch
        {
            GameTarget.PoE1 => new GameRules
            {
                Game = "poe1",
                MaxCharacterLimit = 250,
                SupportsLookahead = false,
                SupportsModNameAnchors = false,
                CustomKeywords = ["ilvl:", "str:", "dex:", "int:"],
            },
            GameTarget.PoE2 => new GameRules
            {
                Game = "poe2",
                MaxCharacterLimit = 250,
                SupportsLookahead = true,
                SupportsModNameAnchors = false,
                CustomKeywords = ["ilvl:", "str:", "dex:", "int:"],
            },
            _ => throw new ArgumentOutOfRangeException(nameof(target)),
        };

    /// <summary>Loads embedded rules for a game, applying an optional on-disk override.</summary>
    public static GameRules Load(GameTarget target, string? overridePath = null)
    {
        if (overridePath is null || !File.Exists(overridePath)) return For(target);
        try
        {
            var json = File.ReadAllText(overridePath);
            var overrides = System.Text.Json.JsonSerializer.Deserialize<GameRules>(json);
            if (overrides is null) return For(target);
            return overrides with { Game = target.ToRuleKey() };
        }
        catch
        {
            return For(target);
        }
    }
}

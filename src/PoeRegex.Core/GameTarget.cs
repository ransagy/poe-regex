namespace PoeRegex.Core;

public enum GameTarget
{
    PoE1 = 1,
    PoE2 = 2,
}

public static class GameTargetExtensions
{
    public static string ToRuleKey(this GameTarget target) =>
        target == GameTarget.PoE1 ? "poe1" : "poe2";

    public static GameTarget Parse(string key) =>
        key.Trim().ToLowerInvariant() switch
        {
            "poe1" => GameTarget.PoE1,
            "poe2" => GameTarget.PoE2,
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown game target"),
        };
}

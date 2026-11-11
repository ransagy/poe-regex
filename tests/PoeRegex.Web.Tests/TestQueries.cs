using PoeRegex.Web.Core;

namespace PoeRegex.Web.Tests;

/// <summary>
/// Queries assembled from the current catalog/table. Reconstruction tests
/// should not hardcode table fragments: a catalog or solver update may choose
/// a different valid fragment while the query behavior stays the same.
/// </summary>
internal static class TestQueries
{
    public const string SuffixNames =
        "Blasting|Carbonising|Cremating|Flaming|Incinerating|Scorching";

    /// <summary>The six-name LocalFireDamage suffix plus four ordinary picks.</summary>
    public static string MixedSuffixAndFragments(EngineService engine)
    {
        var fragments = engine.CompilePicks(
            [
                new WantedMod("IncreasedLife6"),
                new WantedMod("CriticalStrikeChance1"),
                new WantedMod("Intelligence1"),
                new WantedMod("ChaosResist1"),
            ], [], false, null).Query.Trim('"');
        return $"\"{SuffixNames}|{fragments}\"";
    }

    /// <summary>Two distinct all-of picks that compile to the same table group.</summary>
    public static string DuplicateAllOfGroups(EngineService engine) =>
        engine.CompilePicks(
            [new WantedMod("Strength1"), new WantedMod("Strength2")],
            [], true, "Gloves").Query;
}

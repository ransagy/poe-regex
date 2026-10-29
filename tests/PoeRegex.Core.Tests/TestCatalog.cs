using PoeRegex.Core.Data;
using PoeRegex.Core.Compilation;
using PoeRegex.Core.Search;

namespace PoeRegex.Core.Tests;

/// <summary>Tiny synthetic catalog for engine tests (no real game data needed).</summary>
public static class TestCatalog
{
    public static Catalog Build()
    {
        var p = SuffixIndex.DigitPlaceholder;
        var lines = new[]
        {
            ($"+24 to maximum Life", $"+{p} to maximum Life", 20.0, 30.0),
            ($"+10 to Strength", $"+{p} to Strength", 5.0, 15.0),
            ($"+12 to Dexterity", $"+{p} to Dexterity", 8.0, 16.0),
            ($"10% increased Rarity of Items found", $"{p}% increased Rarity of Items found", 5.0, 30.0),
            ($"Item Class: Rings", $"Item Class: Rings", 0.0, 0.0),
        };
        var classId = "Ring";
        var statLines = lines.Select(l => new StatLineEntry
        {
            Line = l.Item1,
            Pattern = l.Item2,
            MinValue = l.Item3,
            MaxValue = l.Item4,
            Fingerprints = new Dictionary<string, string> { [classId] = ShortestFingerprint(l.Item2) },
        }).ToList();

        return new Catalog
        {
            Game = "test",
            DataVersion = "test",
            ItemClasses = new[]
            {
                new ItemClassEntry { Id = classId, Name = "Rings", BaseFingerprint = "Ring" },
            },
            Mods = new[]
            {
                new ModEntry { Id = "IncreasedLife1", Name = "Healthy", GenerationType = "prefix", Classes = [classId], StatLines = [statLines[0]] },
                new ModEntry { Id = "Strength1", Name = "Brute", GenerationType = "suffix", Classes = [classId], StatLines = [statLines[1]] },
                new ModEntry { Id = "Dexterity1", Name = "Agile", GenerationType = "suffix", Classes = [classId], StatLines = [statLines[2]] },
                new ModEntry { Id = "ItemFoundRarityIncrease1", Name = "of Plunder", GenerationType = "suffix", Classes = [classId], StatLines = [statLines[3]] },
                new ModEntry { Id = "EmptyStatMod1", Name = "Empty", GenerationType = "prefix", Classes = [classId], StatLines = [] },
            },
        };
    }

    public static FragmentTable Fragments()
    {
        var p = SuffixIndex.DigitPlaceholder;
        var global = new Dictionary<string, string>
        {
            [$"+{p} to maximum Life"] = "maximum L",
            [$"+{p} to Strength"] = "St",
            [$"+{p} to Dexterity"] = "Dex",
            [$"{p}% increased Rarity of Items found"] = "Rar",
        };
        return new FragmentTable
        {
            Game = "test",
            DataVersion = "test",
            TargetModel = FragmentTable.AdvancedRanges,
            Global = global,
            ByClass = new Dictionary<string, Dictionary<string, string>>
            {
                ["Ring"] = new(global),
            },
            ClassAnchors = new Dictionary<string, string> { ["Ring"] = "Ri" },
        };
    }

    public static EngineCorpus Corpus() => new(Build(), Fragments());

    private static string ShortestFingerprint(string pattern)
    {
        // Real shortest-unique-substring for the tiny corpus; hard-coded per pattern.
        var p = SuffixIndex.DigitPlaceholder;
        return pattern switch
        {
            _ when pattern == $"+{p} to maximum Life" => "maximum L",
            _ when pattern == $"+{p} to Strength" => "St",
            _ when pattern == $"+{p} to Dexterity" => "Dex",
            _ when pattern == $"{p}% increased Rarity of Items found" => "Rar",
            _ => pattern,
        };
    }
}

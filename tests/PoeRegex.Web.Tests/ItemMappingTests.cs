using PoeRegex.Core;
using PoeRegex.Core.Data;
using PoeRegex.Web.Core;

namespace PoeRegex.Web.Tests;

/// <summary>
/// Mapping quality: pasted canonical lines resolve to catalog mods holding
/// the header's mod name; unknown lines map nowhere. Matcher verdicts follow
/// the engine's token semantics.
/// </summary>
public class ItemMappingTests
{
    private static readonly EngineService Engine = new();

    private static void AssertMapsTo(string? itemClassText, string text, string expectedModName, bool approximate = false)
    {
        var parsed = ItemTextParser.Parse($"Item Class: {itemClassText}\nRarity: Rare\nX\n--------\n{{ Suffix Modifier \"{expectedModName}\" }}\n{text}\n");
        var line = parsed.Mods[0].Lines[0];
        var classId = Engine.ResolveClassId(itemClassText);
        var mapping = Engine.MapLineToMods(classId, line.CanonicalText, line.Rolls);
        mapping.ModIds.Should().NotBeEmpty($"'{text}' should map to something");
        mapping.Approximate.Should().Be(approximate);
        var names = mapping.ModIds.Select(id => Engine.FindMod(id)?.Name).ToList();
        names.Should().Contain(expectedModName);
    }

    [Fact]
    public void ResolveClassId_MatchesPluralDisplayNames()
    {
        Engine.ResolveClassId("Crossbows").Should().Be("Crossbow");
        Engine.ResolveClassId("Belts").Should().Be("Belt");
        Engine.ResolveClassId("Talismans").Should().Be("Talisman");
        Engine.ResolveClassId("Staves").Should().Be("Staff");
        Engine.ResolveClassId("One Hand Maces").Should().Be("One Hand Mace");
        Engine.ResolveClassId("nope").Should().BeNull();
    }

    [Fact]
    public void MapLine_IntelligenceTier_MatchesSavant()
    {
        AssertMapsTo("Talismans", "+30(28-30) to Intelligence", "of the Savant");
    }

    [Fact]
    public void MapLine_LightningDamage_MatchesSnapping()
    {
        AssertMapsTo("Crossbows", "Adds 2(1-3) to 43(31-43) Lightning Damage", "Snapping");
    }

    [Fact]
    public void MapLine_RolllessLightRadius_MatchesOfLightExactly()
    {
        // The renderer keeps the bracket display alternative, so the catalog
        // line matches the game's "…increased Light Radius" text exactly.
        AssertMapsTo("One Hand Maces", "10% increased Light Radius", "of Light");
    }

    [Fact]
    public void MapLine_ImplicitBolt_ResolvesToTheSameTextSuffix()
    {
        // The catalog ships prefix/suffix generations only, but a crossbow
        // suffix ("of Shelling") renders the exact text of the Gemini
        // implicit; text matching cannot tell them apart.
        var classId = Engine.ResolveClassId("Crossbows");
        var mapping = Engine.MapLineToMods(classId, "Loads an additional bolt", []);
        mapping.ModIds.Select(id => Engine.FindMod(id)?.Name).Should().Contain("of Shelling");
    }

    [Fact]
    public void MapLine_UnknownLine_MapsNowhere()
    {
        Engine.MapLineToMods("Ring", "Summons 3 angry bees", []).ModIds.Should().BeEmpty();
    }

    [Fact]
    public void MapLineWithRanges_PinsTheRolledTier()
    {
        // Text alone matches every mace accuracy prefix tier (digit runs
        // become placeholders); the paste's (33-60) pair keeps only the tier
        // that actually rolls it.
        var loose = Engine.MapLineToMods("One Hand Mace", "+46 to Accuracy Rating", [46]);
        loose.ModIds.Should().HaveCountGreaterThan(1);
        var pinned = Engine.MapLineToMods("One Hand Mace", "+46 to Accuracy Rating", [46], [(33, 60)]);
        pinned.ModIds.Should().ContainSingle();
        Engine.FindMod(pinned.ModIds[0])!.Name.Should().Be("Reliable");
    }

    [Fact]
    public void MapLineWithRanges_UnmatchablePairsFallBackToTheLooseList()
    {
        // Renderer quirks occasionally shift numbers; pairs that kill every
        // candidate must not blank the line — the roll-filtered list stays.
        var loose = Engine.MapLineToMods("One Hand Mace", "+46 to Accuracy Rating", [46]);
        var weird = Engine.MapLineToMods("One Hand Mace", "+46 to Accuracy Rating", [46], [(999, 1000)]);
        weird.ModIds.Should().Equal(loose.ModIds);
    }

    [Fact]
    public void Matcher_IncludedAndExcludedTokenSemantics()
    {
        var lines = new[] { "+84 to maximum Life", "Item Class: Rings" };
        ItemMatcher.Match("\"to maximum Life\" !\"reflect\"", lines).Match.Should().BeTrue();
        ItemMatcher.Match("\"to maximum Life\" !\"reflect\"", ["+84 to maximum Life", "reflects damage"])
            .Match.Should().BeFalse("excluded token hits");
        ItemMatcher.Match("\"to maximum Life\"", ["+84 to maximum Mana"])
            .Match.Should().BeFalse("included token misses");
    }

    [Fact]
    public void Matcher_BlankQuery_MatchesNothing()
    {
        // A vacuous AND would light up every lab item; an empty query means
        // "nothing picked yet", which matches nothing.
        ItemMatcher.Match("", ["+84 to maximum Life"]).Match.Should().BeFalse();
        ItemMatcher.Match("   ", ["+84 to maximum Life"]).Match.Should().BeFalse();
    }

    [Fact]
    public void Matcher_ClassAnchor_MatchesItemClassLine()
    {
        // Slot and combined-paste queries start with a class anchor such as
        // `": (Bel)"`; item lines must expose the game's `Item Class:` line
        // or every anchored verdict comes back "no".
        var item = new TestItem { Name = "b", ClassId = "Belt", Parsed = ItemTextParser.Parse(FixtureTexts.Belt) };
        item.MatchLines().Should().Contain("Item Class: Belts");
        var verdict = ItemMatcher.Match("\": (Bel)\"", item.MatchLines());
        verdict.Match.Should().BeTrue();
    }

    [Fact]
    public void Matcher_CompiledDigitPattern_MatchesRoll()
    {
        var result = Engine.Compile(
            [new ModifierRequirement("IncreasedLife6", 80)], [],
            itemClass: "Ring");
        var life = AdvancedStatLine.For(Engine.FindMod("IncreasedLife6")!.StatLines[0]);
        ItemMatcher.Match(result.Query, [life, "Item Class: Rings"])
            .Match.Should().BeTrue();
        ItemMatcher.Match(result.Query, ["+10(1-10) to maximum Life", "Item Class: Rings"])
            .Match.Should().BeFalse("floor 80 excludes a 10 roll");
    }

    [Theory]
    [InlineData("Belt", 0, 2, 0)]
    [InlineData("Talisman", 2, 0, 0)]
    [InlineData("Crossbow", 4, 0, 1)]
    [InlineData("Staff", 1, 0, 0)]
    [InlineData("Mace", 5, 0, 0)]
    public void FixtureCoverage_PrefixSuffixLinesMapToHeaderName(
        string fixture, int mapped, int unmappedImplicits, int mappedImplicits)
    {
        var text = typeof(FixtureTexts).GetField(
            fixture,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null) as string;
        var item = ItemTextParser.Parse(text!);
        var classId = Engine.ResolveClassId(item.ItemClass);
        classId.Should().NotBeNull($"{fixture} item class should resolve");
        var hit = 0;
        var implicitEmpty = 0;
        var implicitHit = 0;
        foreach (var block in item.Mods)
        {
            foreach (var line in block.Lines)
            {
                var mapping = Engine.MapLineToMods(classId, line.CanonicalText, line.Rolls);
                if (block.Kind == "implicit")
                {
                    // Catalog ships prefix/suffix only, so implicits map
                    // nowhere — unless a real prefix/suffix shares the
                    // implicit's exact text (the crossbow bolt implicit vs
                    // the "of Shelling" suffix).
                    if (mapping.ModIds.Count == 0) implicitEmpty++;
                    else implicitHit++;
                    continue;
                }
                mapping.ModIds.Should().NotBeEmpty($"'{line.Text}' should map");
                var names = mapping.ModIds.Select(id => Engine.FindMod(id)?.Name);
                names.Should().Contain(block.Name, $"header name '{block.Name}' should be among candidates");
                hit++;
            }
        }
        hit.Should().Be(mapped);
        implicitEmpty.Should().Be(unmappedImplicits);
        implicitHit.Should().Be(mappedImplicits);
    }
}

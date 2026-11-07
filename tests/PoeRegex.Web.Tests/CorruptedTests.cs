using PoeRegex.Core;
using PoeRegex.Core.Data;
using PoeRegex.Web.Core;

namespace PoeRegex.Web.Tests;

/// <summary>
/// Corrupted mods are Vaal/corruption enchantments: a separate non-affix
/// source. Only scopes not already covered by a normal, desecrated, or
/// implicit family on the same class ship in the catalog.
/// </summary>
public class CorruptedTests
{
    private static readonly EngineService Engine = new();

    [Fact]
    public void Catalog_HasCorruptedOnlyScopes()
    {
        var corrupted = Engine.PlayableMods()
            .Where(m => m.Source == "corrupted")
            .ToList();
        corrupted.Should().HaveCount(44);
        corrupted.Should().OnlyContain(m =>
            m.GenerationType == "corrupted" && m.Classes.Count > 0);
        corrupted.SelectMany(m => m.StatLines)
            .Select(sl => sl.Pattern)
            .Distinct(StringComparer.Ordinal)
            .Should().HaveCount(43);
        corrupted.SelectMany(m => m.Classes)
            .Distinct(StringComparer.Ordinal)
            .Should().HaveCount(17);

        var ordinary = Engine.PlayableMods()
            .Where(m => m.Source != "corrupted")
            .ToList();
        foreach (var mod in corrupted)
        foreach (var line in mod.StatLines)
        foreach (var cls in mod.Classes)
            ordinary.Should().NotContain(m =>
                    m.Classes.Contains(cls) &&
                    m.StatLines.Any(sl => sl.Pattern == line.Pattern),
                $"{mod.Id} must not duplicate another source on {cls}");

        // No positive base/spawn reference; these are deliberately excluded.
        Engine.FindMod("CorruptionLifeRegenerationPercent1").Should().BeNull();
        Engine.FindMod("CorruptionWeaponSwapSpeed1").Should().BeNull();
    }

    [Fact]
    public void CorruptedFamily_IsItsOwnKindAndSource()
    {
        var family = ModFamilies.Group(Engine.ModsForClass("Bow"))
            .Single(f => f.IsCorrupted &&
                         f.Members.Any(m => m.Id == "CorruptionAdditionalArrows1"));

        family.Kind.Should().Be("corrupted");
        family.Source.Should().Be("corrupted");
        family.SourceTag.Should().Be("Corrupted");
        family.SourceCssClass.Should().Be("source-corrupted");
        family.IsCorrupted.Should().BeTrue();
        family.Tiers.Should().ContainSingle();
        ModFamilies.IsCommon(family).Should().BeFalse();
    }

    [Fact]
    public void CorruptedPick_CompilesOnItsClass()
    {
        var mod = Engine.FindMod("CorruptionAdditionalArrows1")!;
        mod.Classes.Should().Contain("Bow");
        mod.Classes.Should().NotContain("Crossbow");

        var compiled = Engine.CompilePicks(
            [new WantedMod(mod.Id)], [], matchAll: false, itemClass: "Bow");

        compiled.Query.Should().NotBeNullOrWhiteSpace();
        compiled.Exceeded.Should().BeFalse();
        var line = AdvancedStatLine.For(mod.StatLines[0]);
        ItemMatcher.Match(compiled.Query, ["Item Class: Bows", line]).Match
            .Should().BeTrue($"{compiled.Query} should match '{line}'");
    }

    [Fact]
    public void GameImplicitLine_StaysQuietOnImport()
    {
        var parsed = ItemTextParser.Parse("""
            Item Class: Bows
            Rarity: Rare
            Test Bow
            --------
            Item Level: 80
            --------
            { Implicit Modifier — Attack }
            Bow Attacks fire an additional Arrow
            --------
            Corrupted
            """);
        var mapped = ItemImport.Map(Engine, parsed);

        mapped.Lines.Values.SelectMany(m => m.ModIds)
            .Should().NotContain("CorruptionAdditionalArrows1",
                "game copy uses an implicit header, so corruption lines stay quiet");
    }

    [Fact]
    public void ExplicitCorruptedHeader_MapsOnImport()
    {
        var parsed = ItemTextParser.Parse("""
            Item Class: Bows
            Rarity: Rare
            Test Bow
            --------
            Item Level: 80
            --------
            { Corrupted Modifier }
            Bow Attacks fire an additional Arrow
            --------
            Corrupted
            """);
        var mapped = ItemImport.Map(Engine, parsed);

        mapped.Lines.Values.SelectMany(m => m.ModIds)
            .Should().Contain("CorruptionAdditionalArrows1");
    }
}

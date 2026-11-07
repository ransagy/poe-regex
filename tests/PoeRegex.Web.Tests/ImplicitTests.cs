using PoeRegex.Core;
using PoeRegex.Core.Data;
using PoeRegex.Web.Core;

namespace PoeRegex.Web.Tests;

/// <summary>
/// Base implicits are a separate kind from normal prefixes/suffixes. The
/// catalog adds only implicit-only patterns; import stays quiet and implicit
/// lines never consume affix slots.
/// </summary>
public class ImplicitTests
{
    private static readonly EngineService Engine = new();

    [Fact]
    public void Catalog_HasImplicitOnlyPatterns()
    {
        var implicitMods = Engine.PlayableMods()
            .Where(m => m.Source == "implicit")
            .ToList();
        implicitMods.Should().HaveCount(70);
        implicitMods.Should().OnlyContain(m =>
            m.GenerationType == "implicit" && m.Classes.Count > 0);

        var ordinaryPatterns = Engine.PlayableMods()
            .Where(m => m.Source != "implicit")
            .SelectMany(m => m.StatLines)
            .Select(sl => sl.Pattern)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var mod in implicitMods)
        foreach (var line in mod.StatLines)
            ordinaryPatterns.Should().NotContain(line.Pattern,
                $"{mod.Id} must not duplicate a normal family");

        var always = Engine.FindMod("MaceImplicitAlwaysHit1")!;
        always.Classes.Should().Equal(["One Hand Mace"]);
        always.StatLines.Should().ContainSingle()
            .Which.Line.Should().Be("Always Hits");
    }

    [Fact]
    public void ImplicitFamily_IsItsOwnKindAndSource()
    {
        var family = ModFamilies.Group(Engine.ModsForClass("One Hand Mace"))
            .Single(f => f.Key == "implicit:Always Hits");

        family.Kind.Should().Be("implicit");
        family.IsImplicit.Should().BeTrue();
        family.IsDesecrated.Should().BeFalse();
        family.SourceTag.Should().Be("Implicit");
        family.SourceCssClass.Should().Be("source-implicit");
        family.Tiers.Should().ContainSingle();
        ModFamilies.IsCommon(family).Should().BeFalse();
    }

    [Fact]
    public void ImplicitPick_CompilesOnItsClass()
    {
        var mod = Engine.FindMod("MaceImplicitAlwaysHit1")!;
        var compiled = Engine.CompilePicks(
            [new WantedMod(mod.Id)], [], matchAll: false, itemClass: "One Hand Mace");

        compiled.Query.Should().NotBeNullOrWhiteSpace();
        compiled.Exceeded.Should().BeFalse();
        var line = AdvancedStatLine.For(mod.StatLines[0]);
        ItemMatcher.Match(compiled.Query, ["Item Class: One Hand Maces", line]).Match
            .Should().BeTrue($"{compiled.Query} should match '{line}'");
    }

    [Fact]
    public void ImplicitLines_StayQuietOnImport()
    {
        var parsed = ItemTextParser.Parse(FixtureTexts.Belt);
        var mapped = ItemImport.Map(Engine, parsed);
        var implicitBlocks = parsed.Mods
            .Select((block, index) => (block, index))
            .Where(x => x.block.Kind == "implicit")
            .ToList();

        implicitBlocks.Should().NotBeEmpty();
        foreach (var (block, bi) in implicitBlocks)
        foreach (var li in Enumerable.Range(0, block.Lines.Count))
        {
            mapped.Lines[(bi, li)].ModIds.Should().BeEmpty(
                "implicit lines stay quiet even when the catalog has an implicit family");
        }
        ItemImport.UnmappedAffixCount(parsed, mapped).Should().Be(0);
    }
}

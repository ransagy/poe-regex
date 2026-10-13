using PoeRegex.Web.Core;

namespace PoeRegex.Web.Tests;

/// <summary>
/// Golden parser tests from the five maintainer-provided English advanced
/// item descriptions (normal belt, magic talisman, rare crossbow, socketed
/// staff, corrupted mace).
/// </summary>
public class ItemTextParserTests
{
    [Fact]
    public void Belt_Normal_TwoImplicits()
    {
        var item = ItemTextParser.Parse(FixtureTexts.Belt);
        item.ItemClass.Should().Be("Belts");
        item.Rarity.Should().Be("Normal");
        item.Name.Should().Be("Utility Belt");
        item.ItemLevel.Should().Be(80);
        item.Requires.Should().Be("Level 55");
        item.Mods.Should().HaveCount(2);
        item.Mods.Should().OnlyContain(m => m.Kind == "implicit");
        item.Mods[1].Tags.Should().Equal(["Charm"]);
        var charm = item.Mods[1].Lines[0];
        charm.Rolls.Should().Equal([1]);
        charm.CanonicalText.Should().Be("Has 1 Charm Slot");
    }

    [Fact]
    public void Talisman_Magic_PrefixAndSuffixWithTiers()
    {
        var item = ItemTextParser.Parse(FixtureTexts.Talisman);
        item.ItemClass.Should().Be("Talismans");
        item.Name.Should().Be("Annealed Wildwood Talisman of the Savant");
        item.ItemLevel.Should().Be(79);
        item.BaseLines.Should().Contain("Physical Damage: 93-159 (augmented)");
        item.Mods.Should().HaveCount(2);
        var pre = item.Mods[0];
        pre.Kind.Should().Be("prefix");
        pre.Name.Should().Be("Annealed");
        pre.Tier.Should().Be(4);
        pre.Tags.Should().Equal(["Damage", "Physical", "Attack"]);
        pre.Lines[0].Rolls.Should().Equal([26, 47]);
        pre.Lines[0].CanonicalText.Should().Be("Adds 26 to 47 Physical Damage");
        var suf = item.Mods[1];
        suf.Name.Should().Be("of the Savant");
        suf.Tier.Should().Be(2);
        suf.Lines[0].Rolls.Should().Equal([30]);
    }

    [Fact]
    public void Crossbow_Rare_TwoLineName_FourMods()
    {
        var item = ItemTextParser.Parse(FixtureTexts.Crossbow);
        item.ItemClass.Should().Be("Crossbows");
        item.Name.Should().Be("Foe Core Gemini Crossbow");
        item.BaseLines.Should().Contain("Reload Time: 1.10");
        item.BaseLines.Should().Contain("Lightning Damage: 2-43 (lightning)");
        item.Mods.Should().HaveCount(5);
        item.Mods.Count(m => m.Kind == "implicit").Should().Be(1);
        item.Mods.Count(m => m.Kind == "prefix").Should().Be(1);
        item.Mods.Count(m => m.Kind == "suffix").Should().Be(3);
        var leech = item.Mods.First(m => m.Name == "of the Parched");
        leech.Tier.Should().Be(4);
        leech.Lines[0].Rolls.Should().Equal([5.1]);
        leech.Lines[0].CanonicalText.Should().Be("Leeches 5.1% of Physical Damage as Mana");
    }

    [Fact]
    public void Staff_SocketsAndSkill_StrayLineTolerated()
    {
        var item = ItemTextParser.Parse(FixtureTexts.Staff);
        item.ItemClass.Should().Be("Staves");
        item.Name.Should().Be("Chiming Staff of the Glacier");
        item.Sockets.Should().Be("S S");
        item.GrantsSkill.Should().Be("Level 18 Sigil of Power");
        item.Mods.Should().HaveCount(1);
        item.Mods[0].Tier.Should().Be(3);
        item.Mods[0].Lines[0].Rolls.Should().Equal([60]);
    }

    [Fact]
    public void Mace_Corrupted_TwoLineSuffix()
    {
        var item = ItemTextParser.Parse(FixtureTexts.Mace);
        item.ItemClass.Should().Be("One Hand Maces");
        item.Name.Should().Be("Rift Blast Molten Hammer");
        item.Corrupted.Should().BeTrue();
        item.BaseLines.Should().Contain("Elemental Damage: 35-59 (fire), 4-64 (lightning)");
        var light = item.Mods.First(m => m.Name == "of Light");
        light.Lines.Should().HaveCount(2);
        light.Lines[1].Text.Should().Be("10% increased Light Radius");
        light.Lines[1].Rolls.Should().BeEmpty();
    }
}

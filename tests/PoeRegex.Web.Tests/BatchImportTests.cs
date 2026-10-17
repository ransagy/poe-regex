using PoeRegex.Web.Core;

namespace PoeRegex.Web.Tests;

/// <summary>
/// Multi-item paste: the `Item Class:` splitter and the batch import pipeline
/// over the five maintainer fixtures, joined with blank lines exactly like the
/// paste blob users drop into the import dialog.
/// </summary>
public class BatchImportTests
{
    private static readonly EngineService Engine = new();

    private static string Blob => string.Join("\n\n",
        FixtureTexts.Belt, FixtureTexts.Talisman, FixtureTexts.Crossbow,
        FixtureTexts.Staff, FixtureTexts.Mace);

    [Fact]
    public void SplitItems_SingleItem_OneBlock()
    {
        var blocks = ItemTextParser.SplitItems(FixtureTexts.Belt);
        blocks.Should().HaveCount(1);
        ItemTextParser.Parse(blocks[0]).Name.Should().Be("Utility Belt");
    }

    [Fact]
    public void SplitItems_FixtureBlob_FiveBlocksInOrder()
    {
        var blocks = ItemTextParser.SplitItems(Blob);
        var names = blocks
            .Select(t => ItemTextParser.Parse(t).Name)
            .ToList();
        names.Should().Equal(
            "Utility Belt",
            "Annealed Wildwood Talisman of the Savant",
            "Foe Core Gemini Crossbow",
            "Chiming Staff of the Glacier",
            "Rift Blast Molten Hammer");
        // The staff's stray pre-header `sockets` line sits after the crossbow's
        // trailing blank line — it must move to the staff, never leak into the
        // crossbow as a bogus value line.
        blocks[2].Should().NotContain("sockets");
        blocks[3].Should().StartWith("sockets");
        var staff = ItemTextParser.Parse(blocks[3]);
        staff.BaseLines.Should().Contain("sockets");
    }

    [Fact]
    public void SplitItems_StrayLineBeforeFirstHeader_StaysWithItsItem()
    {
        // The staff paste opens with a stray `sockets` line; it must stay
        // attached to the staff, not turn into an orphan block.
        var blocks = ItemTextParser.SplitItems($"sockets\n{FixtureTexts.Staff}");
        blocks.Should().HaveCount(1);
        var parsed = ItemTextParser.Parse(blocks[0]);
        parsed.Name.Should().Be("Chiming Staff of the Glacier");
        parsed.ItemClass.Should().Be("Staves");
    }

    [Fact]
    public void SplitItems_NoHeader_OneBlock()
    {
        ItemTextParser.SplitItems("no header here\njust text").Should().HaveCount(1);
    }

    [Fact]
    public void BatchImport_FixtureBlob_AllFiveProduceImportableItems()
    {
        var results = ItemTextParser.SplitItems(Blob)
            .Select(t => ItemImport.Import(Engine, ItemTextParser.Parse(t)))
            .ToList();
        results.Should().HaveCount(5);
        results.Should().OnlyContain(r => r.Item != null && r.Error == null);
        results.Select(r => r.Item!.ClassId)
            .Should().Equal("Belt", "Talisman", "Crossbow", "Staff", "One Hand Mace");
        // Unmapped-line counts match the single-item fixture coverage: only
        // the belt's two implicits map nowhere; everything else maps.
        results.Select(r => r.Item!.Mapped.Count(m => m.SelectedModId is null))
            .Should().Equal(2, 0, 0, 0, 0);
        // Nothing blocks, and unmapped implicits stay quiet: they never count
        // in the rules, so the blob raises no warnings either.
        results.SelectMany(r => r.Violations).Should().BeEmpty();
    }

    [Fact]
    public void UnmappedAffixCount_IgnoresImplicitBlocks()
    {
        // The belt's two unmapped lines are both implicit — the affix-only
        // verdict is clean, which is what the import dialog reports.
        var parsed = ItemTextParser.Parse(FixtureTexts.Belt);
        var mapped = ItemImport.Map(Engine, parsed);
        ItemImport.UnmappedAffixCount(parsed, mapped).Should().Be(0);
    }

    [Fact]
    public void UnmappedAffixCount_CountsUnmappedAffixLines()
    {
        var parsed = ItemTextParser.Parse(FixtureTexts.Mace);
        var mapped = ItemImport.Map(Engine, parsed);
        ItemImport.UnmappedAffixCount(parsed, mapped).Should().Be(0);
        var key = mapped.Lines.Keys.First(k => parsed.Mods[k.Block].Kind != "implicit");
        mapped.Lines.Remove(key);
        ItemImport.UnmappedAffixCount(parsed, mapped).Should().Be(1);
    }

    [Fact]
    public void Materialize_UsesTheProvidedPickOverTheDefault()
    {
        // The carousel's per-line picker edits row picks before `add all`
        // materializes; the edited candidate must win over the default.
        var parsed = ItemTextParser.Parse(FixtureTexts.Mace);
        var mapped = ItemImport.Map(Engine, parsed);
        var picks = ItemImport.DefaultPicks(Engine, parsed, mapped);
        var (block, line) = picks.Keys.First(k => mapped.Lines[k].ModIds.Count > 1);
        var other = mapped.Lines[(block, line)].ModIds.First(id => id != picks[(block, line)]);
        picks[(block, line)] = other;
        var item = ItemImport.Materialize(parsed, mapped, picks);
        var index = parsed.Mods.Take(block).Sum(m => m.Lines.Count) + line;
        item.Mapped[index].SelectedModId.Should().Be(other);
    }

    [Fact]
    public void BatchImport_FixtureBlob_AllFiveFitTheBackpackWithFootprints()
    {
        var state = new WorkbenchState();
        foreach (var text in ItemTextParser.SplitItems(Blob))
            state.AddTestItem(ItemImport.Import(Engine, ItemTextParser.Parse(text)).Item!)
                .Should().BeTrue();
        state.TestItems.Should().HaveCount(5);
        state.TestItems.Select(i => (i.ClassId, i.Cols, i.Rows)).Should().BeEquivalentTo(
        [
            ("Belt", 2, 1),
            ("Talisman", 2, 2),
            ("Crossbow", 2, 4),
            ("Staff", 1, 4),
            ("One Hand Mace", 2, 3),
        ]);
    }

    [Fact]
    public void Import_ClassOutsideTheCatalog_IsBlocked()
    {
        var result = ItemImport.Import(Engine, ItemTextParser.Parse(
            "Item Class: Currency\nRarity: Normal\nChaos Orb\n"));
        result.Item.Should().BeNull();
        result.Error.Should().NotBeNull();
    }

    [Fact]
    public void Materialize_AppendsMappedLinesInParseOrder()
    {
        // The preview card indexes Mapped by flattened parse order; keep it true.
        var parsed = ItemTextParser.Parse(FixtureTexts.Mace);
        var mapped = ItemImport.Map(Engine, parsed);
        var picks = ItemImport.DefaultPicks(Engine, parsed, mapped);
        var item = ItemImport.Materialize(parsed, mapped, picks);
        var expected = parsed.Mods.SelectMany(m => m.Lines.Select(l => l.Text)).ToList();
        item.Mapped.Select(m => m.Text).Should().Equal(expected);
    }

    [Fact]
    public void DefaultPicks_PreferTheHeaderAffixName()
    {
        // `+37 to Accuracy Rating` maps to both `Reliable` (prefix) and
        // `of Light` (suffix); the block header decides, so the pair never
        // collapses onto one mod (which would read as a duplicate error).
        var parsed = ItemTextParser.Parse(FixtureTexts.Mace);
        var mapped = ItemImport.Map(Engine, parsed);
        var picks = ItemImport.DefaultPicks(Engine, parsed, mapped);
        var reliable = picks[(0, 0)];
        var ofLight = picks[(2, 0)];
        Engine.FindMod(reliable)!.Name.Should().Be("Reliable");
        Engine.FindMod(ofLight)!.Name.Should().Be("of Light");
        reliable.Should().NotBe(ofLight);
    }
}

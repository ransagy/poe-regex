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
        // crossbow as a bogus value line. Parsing then drops it as paste noise.
        blocks[2].Should().NotContain("sockets");
        blocks[3].Should().StartWith("sockets");
        var staff = ItemTextParser.Parse(blocks[3]);
        staff.BaseLines.Should().NotContain("sockets");
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
    public void Map_PinsBracketedLinesByRanges()
    {
        // `+46 to Accuracy Rating` matches several mace tiers by text alone;
        // the (33-60) bracket pair keeps only the tier that rolls it.
        var byText = Engine.MapLineToMods("One Hand Mace", "+46 to Accuracy Rating", [46]);
        byText.ModIds.Should().HaveCountGreaterThan(1);
        var parsed = ItemTextParser.Parse(FixtureTexts.Mace);
        var mapped = ItemImport.Map(Engine, parsed);
        mapped.Lines[(0, 0)].ModIds.Should().ContainSingle();
        Engine.FindMod(mapped.Lines[(0, 0)].ModIds[0])!.Name.Should().Be("Reliable");
    }

    [Fact]
    public void Map_ResolvesMaceAffixesToTheirHeaderNames()
    {
        // Ranges pin the bracketed lines; `10% increased Light Radius` has no
        // numbers to check, so its header name picks among the three Light
        // Radius tiers. Either way ModIds[0] is the resolved affix.
        var parsed = ItemTextParser.Parse(FixtureTexts.Mace);
        var mapped = ItemImport.Map(Engine, parsed);
        var resolved = new (int Block, int Line, string Name)[]
        {
            (0, 0, "Reliable"), (1, 0, "Arcing"), (2, 0, "of Light"),
            (2, 1, "of Light"), (3, 0, "of Rage"),
        };
        foreach (var (block, line, name) in resolved)
        {
            var ids = mapped.Lines[(block, line)].ModIds;
            ids.Should().NotBeEmpty($"({block},{line}) should map");
            Engine.FindMod(ids[0])!.Name.Should().Be(name, $"({block},{line}) resolves the header affix first");
        }
    }

    [Fact]
    public void Map_EveryFixtureAffixResolvesToItsHeaderName()
    {
        foreach (var text in ItemTextParser.SplitItems(Blob))
        {
            var parsed = ItemTextParser.Parse(text);
            var mapped = ItemImport.Map(Engine, parsed);
            foreach (var (block, bi) in parsed.Mods.Select((b, i) => (b, i)))
            {
                if (block.Kind == "implicit") continue;
                var ids = Enumerable.Range(0, block.Lines.Count)
                    .Where(li => mapped.Lines.ContainsKey((bi, li)))
                    .SelectMany(li => mapped.Lines[(bi, li)].ModIds)
                    .ToList();
                ids.Should().NotBeEmpty($"'{block.Name}' in '{parsed.Name}' should map");
                ids.Select(id => Engine.FindMod(id)!.Name)
                    .Should().Contain(block.Name!, $"'{block.Name}' should resolve among the candidates");
            }
        }
    }

    [Fact]
    public void Validate_HeaderNameContradictingTheResolvedAffix_Warns()
    {
        // Swap a range-pinned header name for nonsense: the line still
        // resolves (ranges outrank the header), and the mismatch surfaces as
        // a warning rather than a silent rewrite.
        var text = FixtureTexts.Mace.Replace("\"of Light\"", "\"of Nonsense\"");
        var parsed = ItemTextParser.Parse(text);
        var mapped = ItemImport.Map(Engine, parsed);
        var violations = ItemImport.Validate(Engine, parsed, mapped);
        violations.Should().ContainSingle(v => v.Level == "warning"
            && v.Message.Contains("of Light") && v.Message.Contains("of Nonsense"));
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
        var item = ItemImport.Materialize(parsed, mapped);
        var expected = parsed.Mods.SelectMany(m => m.Lines.Select(l => l.Text)).ToList();
        item.Mapped.Select(m => m.Text).Should().Equal(expected);
    }

    [Fact]
    public void Map_PrefersTheHeaderAffixNameWhenTextIsAmbiguous()
    {
        // `10% increased Light Radius` matches all three Light Radius tiers
        // by digit-normalized text and carries no rolls or ranges to check;
        // the block header decides, so the pair never collapses onto one mod
        // (which would read as a duplicate error).
        var lightByText = Engine.MapLineToMods("One Hand Mace", "10% increased Light Radius", []);
        lightByText.ModIds.Should().HaveCount(3);
        var parsed = ItemTextParser.Parse(FixtureTexts.Mace);
        var mapped = ItemImport.Map(Engine, parsed);
        var light = mapped.Lines[(2, 1)].ModIds;
        light.Should().ContainSingle();
        Engine.FindMod(light[0])!.Name.Should().Be("of Light");
    }
}

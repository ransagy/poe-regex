using System.Text.Json;
using PoeRegex.Core.Compilation;
using PoeRegex.Core.Data;
using PoeRegex.Web.Core;

namespace PoeRegex.Web.Tests;

/// <summary>PoE1 normal-equipment catalog, parser, and import smoke coverage.</summary>
public class Poe1Tests
{
    private static readonly string Root =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));

    private static EngineService Engine()
    {
        var dir = Path.Combine(Root, "data", "catalogs");
        var catalog = new FileCatalogSource(Path.Combine(dir, "poe1.catalog.json")).Load();
        var table = JsonSerializer.Deserialize(
            File.ReadAllText(Path.Combine(dir, "poe1.fragments.json")),
            CatalogJsonContext.Default.FragmentTable)!;
        var vendor = JsonSerializer.Deserialize(
            File.ReadAllText(Path.Combine(dir, "poe1.vendor-corpus.json")),
            CatalogJsonContext.Default.VendorCorpusFile)!;
        return new EngineService(new GameData(catalog, table, vendor));
    }

    private static readonly string FracturedItem = """
Item Class: Body Armours
Rarity: Rare
Phoenix Suit
Glorious Plate
--------
Armour: 1362 (augmented)
--------
Requirements:
Level: 68
Str: 191
--------
Sockets: W-W
--------
Item Level: 83
--------
{ Fractured Prefix Modifier "Rotund" (Tier: 7) — Life }
+87(85-99) to maximum Life
{ Prefix Modifier "Thickened" (Tier: 4) — Defences, Armour }
69(68-79)% increased Armour
{ Suffix Modifier "of the Troll" (Tier: 6) — Life }
Regenerate 40.6(32.1-48) Life per second
{ Suffix Modifier "of Expulsion" (Tier: 3) — Chaos, Resistance }
+21(21-25)% to Chaos Resistance
--------
Fractured Item
""";

    private static readonly string CorruptedItem = """
Item Class: Amulets
Rarity: Rare
Apocalypse Torc
Coral Amulet
--------
Requirements:
Level: 48
--------
Item Level: 69
--------
{ Corruption Implicit Modifier }
4(4-5)% Chance to Block Attack Damage
--------
{ Prefix Modifier "Beryl" (Tier: 13) — Mana }
+17(15-19) to maximum Mana
--------
Corrupted
""";

    private static readonly string InfluencedItem = """
Item Class: Rings
Rarity: Rare
Dread Turn
Paua Ring
--------
Requirements:
Level: 48
--------
Item Level: 82
--------
{ Implicit Modifier — Mana }
+20(20-30) to maximum Mana
--------
{ Prefix Modifier "Gleaming" (Tier: 2) — Damage, Physical, Attack }
Adds 6(5-7) to 12(11-12) Physical Damage to Attacks
--------
Crusader Item
""";

    private static readonly string CapturedBoots = """
Item Class: Boots
Rarity: Rare
Rapture Track
Hydrascale Boots
--------
Armour: 346 (augmented)
Evasion Rating: 322 (augmented)
Memory Strands: 27
--------
Requirements:
Level: 60
Str: 56
Dex: 56
--------
Sockets: G
--------
Item Level: 84
--------
{ Prefix Modifier "Durable" (Tier: 1) — Defences, Armour, Evasion }
+69(49-85) to Armour
+56(49-85) to Evasion Rating
{ Prefix Modifier "Virile" (Tier: 2) — Life }
+104(100-114) to maximum Life
{ Prefix Modifier "Hero's" (Tier: 2) — Defences, Armour, Evasion }
84(80-91)% increased Armour and Evasion
{ Suffix Modifier "of the Maelstrom" (Tier: 3) — Elemental, Lightning, Resistance }
+36(36-41)% to Lightning Resistance
{ Suffix Modifier "of Abjuration" (Tier: 2) }
+11(11-12)% chance to Suppress Spell Damage
(40% of Damage from Suppressed Hits and Ailments they inflict is prevented)
{ Suffix Modifier "of the Volcano" (Tier: 3) — Elemental, Fire, Resistance }
+37(36-41)% to Fire Resistance
""";

    private static TestItem Item(string text)
    {
        var parsed = ItemTextParser.Parse(text);
        return new TestItem
        {
            Name = parsed.Name ?? "item",
            ClassId = parsed.ItemClass ?? "",
            Parsed = parsed,
        };
    }

    [Fact]
    public void Catalog_LoadsNormalEquipment()
    {
        var engine = Engine();
        engine.GameId.Should().Be("poe1");
        engine.InGameClasses().Should().HaveCount(23);
        engine.InGameClasses().Select(c => c.Id).Should().Contain([
            "Body Armour", "Thrusting One Hand Sword", "Warstaff",
        ]);
        var mod = engine.PlayableMods().First(m =>
            m.Classes.Contains("Body Armour") && m.StatLines[0].Line.Contains("maximum Life"));
        var compiled = engine.CompilePicks([new WantedMod(mod.Id)], [], false, "Body Armour");
        compiled.Diagnostics.Should().BeEmpty();
        compiled.Query.Should().Contain(": Bod");
        compiled.Length.Should().BeLessThan(250);
    }

    [Fact]
    public void Parser_ReadsPoe1HeadersAndFlags()
    {
        var fractured = ItemTextParser.Parse(FracturedItem);
        fractured.Fractured.Should().BeTrue();
        fractured.ItemClass.Should().Be("Body Armours");
        fractured.Mods.Should().HaveCount(4);
        fractured.Mods[0].Source.Should().Be("fractured");
        fractured.Mods[0].Tier.Should().Be(7);

        var corrupted = ItemTextParser.Parse(CorruptedItem);
        corrupted.Corrupted.Should().BeTrue();
        corrupted.Mods[0].Source.Should().Be("corrupted");
        corrupted.Mods[0].Kind.Should().Be("implicit");

        var influenced = ItemTextParser.Parse(InfluencedItem);
        influenced.Influence.Should().Be("Crusader");
    }

    [Fact]
    public void Import_MapsFracturedItemAndRejectsUnsupportedInfluence()
    {
        var engine = Engine();
        var imported = ItemImport.Import(engine, ItemTextParser.Parse(FracturedItem));
        imported.Error.Should().BeNull();
        imported.Item.Should().NotBeNull();
        imported.Item!.Mapped.Should().HaveCount(4);
        imported.Item.Mapped.Should().OnlyContain(m => m.SelectedModId != null);

        var unsupported = ItemImport.Import(engine, ItemTextParser.Parse(InfluencedItem));
        unsupported.Item.Should().BeNull();
        unsupported.Error.Should().Contain("influence");
    }

    [Fact]
    public void Import_RejectsCraftedVeiledEldritchAndInfluencedSources()
    {
        var engine = Engine();
        var crafted = ItemTextParser.Parse("""
Item Class: Body Armours
Rarity: Rare
Test Plate
Glorious Plate
--------
Item Level: 80
--------
{ Crafted Prefix Modifier "Test" (Rank: 1) — Life }
+20 to maximum Life
""");
        ItemImport.Import(engine, crafted).Error.Should().Contain("Crafted");

        var veiled = ItemTextParser.Parse("""
Item Class: Body Armours
Rarity: Rare
Test Plate
Glorious Plate
--------
Item Level: 80
--------
{ Veiled Prefix Modifier "Test" (Rank: 1) — Life }
+20 to maximum Life
""");
        veiled.Mods[0].Source.Should().Be("veiled");
        ItemImport.Import(engine, veiled).Error.Should().Contain("Veiled");

        var unique = ItemTextParser.Parse("""
Item Class: Body Armours
Rarity: Unique
Test Plate
Glorious Plate
--------
Item Level: 80
--------
""");
        ItemImport.Import(engine, unique).Error.Should().Contain("Unique");

        var eldritch = ItemTextParser.Parse("""
Item Class: Body Armours
Rarity: Rare
Test Plate
Glorious Plate
--------
Item Level: 80
--------
{ Searing Exarch Implicit Modifier }
+20 to maximum Life
--------
Searing Exarch Item
""");
        ItemImport.Import(engine, eldritch).Error.Should().Contain("Eldritch");
    }

    [Fact]
    public void Import_RejectsSynthesisedEnchantedAndNonEquipmentItems()
    {
        var engine = Engine();

        var synthesised = ItemTextParser.Parse("""
Item Class: Boots
Rarity: Rare
Rapture Track
Hydrascale Boots
--------
Sockets: G
--------
Item Level: 84
--------
{ Synthesised Implicit Modifier — Life }
+40 to maximum Life
--------
Synthesised Item
""");
        ItemImport.Import(engine, synthesised).Error.Should().Contain("Synthesised");

        var enchanted = ItemTextParser.Parse("""
Item Class: Boots
Rarity: Rare
Rapture Track
Hydrascale Boots
--------
Sockets: G
--------
Item Level: 84
--------
{ Enchantment Modifier — Speed }
10% increased Movement Speed
""");
        ItemImport.Import(engine, enchanted).Error.Should().Contain("Unsupported modifier header");

        var map = ItemTextParser.Parse("""
Item Class: Maps
Rarity: Rare
Strand Map
--------
Item Level: 80
""");
        ItemImport.Import(engine, map).Error.Should().Contain("no recognizable item class");
    }

    [Fact]
    public void ReportedMultiSlotString_OldAnchorMatches_NewAnchorDoesNot()
    {
        var engine = Engine();
        var item = Item(CapturedBoots);
        const string oldQuery =
            "\": (Be|Bod|Boo|G|H|Ri|T.+x)\" \": (Boo|G|Ri|T.+x)|o .+fe$\" " +
            "\": (Be|Bod|G|H|Ri|T.+x)|sed mo\" " +
            "\": (Be|Bod|Boo|H|T.+x)|fi.*o a|ld.+o a|n.+o at|p.*o at\" " +
            "\": (Be|Bod|Boo|G|H|Ri)|d p\"";
        const string newQuery =
            "\": (Be|Bod|Boo|Gl|He|Ri|T.+x)\" \": (Boo|Gl|Ri|T.+x)|o .+fe$\" " +
            "\": (Be|Bod|Gl|He|Ri|T.+x)|sed mo\" " +
            "\": (Be|Bod|Boo|He|T.+x)|fi.*o a|ld.+o a|n.+o at|p.*o at\" " +
            "\": (Be|Bod|Boo|Gl|He|Ri)|d p\"";

        ItemMatcher.Match(oldQuery, item.MatchLines(), engine.Syntax).Match
            .Should().BeTrue("the old G anchor matched the socket line");
        ItemMatcher.Match(newQuery, item.MatchLines(), engine.Syntax).Match
            .Should().BeFalse("the safe Gl anchor does not match Sockets: G");
    }

    [Fact]
    public void EveryCatalogForm_MapsBackToItsSourceMod()
    {
        var engine = Engine();
        var playableClasses = engine.InGameClasses().Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        var checkedCount = 0;
        var failures = new List<string>();
        foreach (var mod in engine.PlayableMods())
        foreach (var cls in mod.Classes.Where(playableClasses.Contains))
        foreach (var line in mod.StatLines)
        {
            foreach (var form in new[] { line.Line, line.RangeLine, AdvancedStatLine.For(line) })
            {
                if (string.IsNullOrWhiteSpace(form)) continue;
                checkedCount++;
                var mapped = engine.MapLineToMods(cls, form, [], []);
                if (!mapped.ModIds.Contains(mod.Id))
                {
                    failures.Add(
                        $"{cls} | {form} | expected {mod.Id}, got {string.Join(",", mapped.ModIds.Take(5))}");
                }
            }
        }

        failures.Take(20).Should().BeEmpty($"{failures.Count} of {checkedCount} forms failed");
        checkedCount.Should().BeGreaterThan(15000);
    }

    [Fact]
    public void CorruptedImplicit_MapsToTheCorruptedPool()
    {
        var engine = Engine();
        var imported = ItemImport.Import(engine, ItemTextParser.Parse(CorruptedItem));
        imported.Error.Should().BeNull();
        var line = imported.Item!.Mapped.First(m => m.Text.Contains("Block Attack Damage"));
        line.SelectedModId.Should().NotBeNull();
        engine.FindMod(line.SelectedModId!)!.Source.Should().Be("corrupted");
    }

    [Fact]
    public void Exclusion_UsesPoE1ShapeAndKeepsClaimIdentity()
    {
        var engine = Engine();
        var mod = engine.PlayableMods().First(m =>
            m.Classes.Contains("Body Armour") && m.StatLines[0].Line.Contains("maximum Life"));

        var fragment = engine.ExcludeFragment(mod.Id);
        fragment.Should().NotBeNullOrEmpty();
        var compiled = engine.CompilePicks([], [mod.Id], false, "Body Armour");
        compiled.Query.Should().Contain("\"!");
        compiled.Query.Should().NotContain("!\"");
        var claim = engine.ExcludeClaimKey(mod.Id, "Body Armour");
        claim.Should().NotBeNullOrEmpty();
        compiled.Query.Should().Contain($"\"!{claim}\"");
    }

    [Fact]
    public void Parser_IncludesSocketTextInMatchableLines()
    {
        var item = Item(CapturedBoots);
        item.MatchLines().Should().Contain("Sockets: G");
    }

    [Fact]
    public void ClassAnchors_AreAtLeastTwoCharacters()
    {
        var engine = Engine();
        engine.ClassNameAnchor("Gloves").Should().Be("Gl");
        engine.ClassNameAnchor("Helmet").Should().Be("He");
        engine.ClassNameAnchor("Amulet").Should().Be("Am");
    }

    [Fact]
    public void CapturedBootsWithoutMovementSpeed_DoesNotMatchMultiSlotPaste()
    {
        var engine = Engine();
        var movement = engine.PlayableMods().First(m =>
            m.Classes.Contains("Boots") &&
            m.StatLines.Any(l => l.Line.Contains("Movement Speed", StringComparison.OrdinalIgnoreCase)));
        var life = engine.PlayableMods().First(m =>
            m.Classes.Contains("Gloves") &&
            m.Classes.Contains("Ring") &&
            m.StatLines.Any(l => l.Line.Contains("maximum Life", StringComparison.OrdinalIgnoreCase)));
        var slots = new (string Label, string ClassId, IEnumerable<WantedMod> Includes)[]
        {
            ("Boots", "Boots", [new WantedMod(movement.Id)]),
            ("Gloves", "Gloves", [new WantedMod(life.Id)]),
            ("Ring", "Ring", [new WantedMod(life.Id)]),
        };
        var paste = engine.Combine(slots, [], allowOverflow: true);
        var item = Item(CapturedBoots);

        paste.Query.Should().Contain("Gl|Ri",
            "the Boots movement block must use the safe Gloves anchor");
        ItemMatcher.Match(paste.Query, item.MatchLines(), engine.Syntax).Match
            .Should().BeFalse("the item has no movement speed line");

        var withMovement = Item(CapturedBoots + "\n25% increased Movement Speed");
        ItemMatcher.Match(paste.Query, withMovement.MatchLines(), engine.Syntax).Match
            .Should().BeTrue("the movement speed line satisfies the Boots block");
    }

    [Fact]
    public void Snapshot_GameRoundTripsThroughShareLink()
    {
        var snapshot = WorkbenchSnapshot.Capture(new WorkbenchState(), "poe1");
        var decoded = SnapshotCodec.Decode(SnapshotCodec.Encode(snapshot))!;
        decoded.Game.Should().Be("poe1");
    }
}

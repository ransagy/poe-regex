using System.Text.Json;
using System.Text.RegularExpressions;
using PoeRegex.Core.Compilation;
using PoeRegex.Core.Data;
using PoeRegex.Data.Build.RePoE;

namespace PoeRegex.Data.Build.Tests;

/// <summary>
/// Guards the PoE1 normal-equipment data boundary. The catalog must not ship
/// legacy/event/race entries, and the foreign-line corpus must use the text
/// shapes the PoE1 client actually prints.
/// </summary>
public class Poe1DataValidityTests
{
    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(
                    directory.FullName, "data", "catalogs", "poe1.catalog.json")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("repository root not found");
    }

    [Fact]
    public void ShippedCatalog_ExcludesLegacyEventAndRaceMods()
    {
        var root = RepoRoot();
        var catalog = new FileCatalogSource(
            Path.Combine(root, "data", "catalogs", "poe1.catalog.json")).Load();

        catalog.Mods.Should().NotContain(m =>
            m.Id.Contains("Royale", StringComparison.OrdinalIgnoreCase));
        catalog.Mods.Should().NotContain(m =>
            m.Id.Contains("Talisman", StringComparison.OrdinalIgnoreCase));
        catalog.Mods.Should().NotContain(m =>
            m.Id.Contains("Demigod", StringComparison.OrdinalIgnoreCase));
        catalog.Mods.Should().NotContain(m =>
            m.Id.Contains("Descent", StringComparison.OrdinalIgnoreCase));
        catalog.Mods.Should().NotContain(m =>
            m.Id.Contains("StormBlade", StringComparison.OrdinalIgnoreCase));

        catalog.Mods.Should().OnlyContain(m =>
            m.Source == "" || m.Source == "implicit" || m.Source == "corrupted");
        catalog.Mods.Should().OnlyContain(m => m.Classes.Count > 0);
    }

    [Fact]
    public void ShippedVendorCorpus_UsesPoe1PropertyText()
    {
        var root = RepoRoot();
        var vendor = JsonSerializer.Deserialize<VendorCorpusFile>(
            File.ReadAllText(Path.Combine(
                root, "data", "catalogs", "poe1.vendor-corpus.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var lines = vendor.Lines.Select(l => l.Line).ToList();

        // PoE1 prints critical strike chance as a percentage with two
        // decimals, not the raw hundredths value from RePoE.
        lines.Should().NotContain(l =>
            Regex.IsMatch(l, @"^Critical Strike Chance: \d{3,}%"));
        lines.Should().Contain("Critical Strike Chance: 8.30%");
        lines.Should().Contain(l => l.StartsWith("Physical Damage: ", StringComparison.Ordinal));
        lines.Should().Contain(l => l.StartsWith("Attacks per Second: ", StringComparison.Ordinal));
        lines.Should().Contain(l => l.StartsWith("Weapon Range: ", StringComparison.Ordinal));
        lines.Should().Contain(l => l.StartsWith("Chance to Block: ", StringComparison.Ordinal));
        lines.Should().Contain(l => l.StartsWith("Movement Speed: -3%", StringComparison.Ordinal));
    }

    [Fact]
    public void ShippedImplicits_AreAnchoredToCurrentBases()
    {
        var root = RepoRoot();
        var rawPath = Path.Combine(root, "data", "raw", "poe1", "base_items.min.json");
        if (!File.Exists(rawPath)) return;

        var bases = RePoEJson.LoadBaseItems(rawPath);
        var catalog = new FileCatalogSource(
            Path.Combine(root, "data", "catalogs", "poe1.catalog.json")).Load();

        foreach (var mod in catalog.Mods.Where(m => m.Source == "implicit"))
        {
            var sources = bases
                .Where(kv => (kv.Value.Implicits ?? []).Contains(mod.Id, StringComparer.Ordinal))
                .ToList();
            sources.Should().NotBeEmpty($"{mod.Id} must have a base source");
            sources.Should().Contain(kv => !Poe1CatalogBuilder.IsLegacyBase(kv.Key, kv.Value),
                $"{mod.Id} must be obtainable on a current base");
        }
    }

    [Fact]
    public void ShippedCatalog_ContainsPoe330ImplicitValues()
    {
        var root = RepoRoot();
        var catalog = new FileCatalogSource(
            Path.Combine(root, "data", "catalogs", "poe1.catalog.json")).Load();

        catalog.DataVersion.Should().Be("3.30.0.2");

        string Line(string id) =>
            catalog.Mods.Single(m => m.Id == id).StatLines[0].Line;
        string Range(string id) =>
            catalog.Mods.Single(m => m.Id == id).StatLines[0].RangeLine;

        Line("DegenerationDamageImplicit1")
            .Should().Be("+18% to Damage over Time Multiplier");
        Range("DegenerationDamageImplicit1")
            .Should().Be("+14–18% to Damage over Time Multiplier");
        Line("SpellDamageImplicitGloves1").Should().Be("26% increased Spell Damage");
        Line("ProjectileAttackDamageImplicitGloves1")
            .Should().Be("28% increased Projectile Attack Damage");
        Line("MeleeDamageImplicitGloves1").Should().Be("30% increased Melee Damage");
        Line("MinionDamageImplicitHelmet1")
            .Should().Be("Minions deal 30% increased Damage");
        Line("LifeRegenerationImplicitAmulet2")
            .Should().Be("Regenerate 2% of Life per second");
        Line("ReducedEnergyShieldDelayImplicit1_")
            .Should().Be("30% faster start of Energy Shield Recharge");
        Line("ArmourAndEvasionImplicitBelt1")
            .Should().Be("+480 to Armour and Evasion Rating");
        Line("AdditionalAttackTotemsImplicit1")
            .Should().Be("Attack Skills have +1 to maximum number of Summoned Totems");
        Line("ColdAndLightningResistImplicitBoots1")
            .Should().Be("+20% to Cold and Lightning Resistances");
        Line("FireAndColdResistImplicitBoots1_")
            .Should().Be("+20% to Fire and Cold Resistances");
        Line("FireAndLightningResistImplicitBoots1")
            .Should().Be("+20% to Fire and Lightning Resistances");
        Line("LightRadiusAndAccuracy3")
            .Should().Be("30% increased Light Radius");
        Line("ItemFoundRarityIncrease1")
            .Should().Be("+10% to Item Rarity");

        catalog.Mods.Should().NotContain(m => m.Id == "SummonTotemCastSpeedImplicit1");

        // ExileData keeps keyword markup in its raw text; the shipped catalog
        // must contain the display text only.
        catalog.Mods.SelectMany(m => m.StatLines)
            .Should().OnlyContain(l => !l.Line.Contains('[') && !l.Line.Contains(']'));

        // The crushed implicit's second stat is present in ExileData's stats
        // but missing from its rendered text; the fallback restores it.
        catalog.Mods.Single(m => m.Id == "MaxChaosResistanceCrushedImplicitR1")
            .StatLines.Select(l => l.Line)
            .Should().Contain("-15% additional Physical Damage Reduction");

        Line("MinionGrantsConvocation1")
            .Should().Be("Grants Level 1 Convocation Skill");
    }

    [Fact]
    public void ShippedVendorCorpus_ContainsPoe330Lines()
    {
        var root = RepoRoot();
        var vendor = JsonSerializer.Deserialize<VendorCorpusFile>(
            File.ReadAllText(Path.Combine(
                root, "data", "catalogs", "poe1.vendor-corpus.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var lines = vendor.Lines.Select(l => l.Line).ToList();

        lines.Should().Contain("Item Tier: 2");
        lines.Should().Contain("Item Tier: 3");
        lines.Should().Contain("Item Tier: 4");
        lines.Should().Contain("Item Tier: 5");
        lines.Should().Contain("Attack Skills have +1 to maximum number of Summoned Totems");
        lines.Should().Contain("+10% to Item Rarity");
        lines.Should().OnlyContain(l => !l.Contains('[') && !l.Contains(']'));
    }

    [Fact]
    public void ShippedSnapshot_IsPinnedToAPoe330Source()
    {
        var root = RepoRoot();
        using var snapshot = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(root, "data", "poe1.snapshot.json")));
        var payload = snapshot.RootElement;
        payload.GetProperty("sourceKind").GetString().Should().BeOneOf("repoe", "exiledata");
        payload.GetProperty("dataVersion").GetString().Should().StartWith("3.30");
        payload.GetProperty("files").GetArrayLength().Should().BeGreaterThanOrEqualTo(4);

        var catalog = new FileCatalogSource(
            Path.Combine(root, "data", "catalogs", "poe1.catalog.json")).Load();
        catalog.DataVersion.Should().Be(payload.GetProperty("dataVersion").GetString());
    }

}

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
}

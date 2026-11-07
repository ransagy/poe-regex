using PoeRegex.Core.Data;
using PoeRegex.Data.Build.RePoE;

namespace PoeRegex.Data.Build.Tests;

/// <summary>
/// Release-prep provenance: every shipped corrupted mod must have a positive
/// PoE2 reference through mods_by_base and a positive spawn weight. Raw
/// exports are gitignored, so the check runs during local release prep and is
/// a no-op in CI, which validates the committed artifacts instead.
/// </summary>
public class CorruptedProvenanceTests
{
    [Fact]
    public void ShippedCorruptedMods_ResolveToReleasedPoE2Bases()
    {
        var root = RepoRoot();
        var raw = Path.Combine(root, "data", "raw", "poe2");
        var modsPath = Path.Combine(raw, "mods.min.json");
        var classesPath = Path.Combine(raw, "item_classes.min.json");
        if (!File.Exists(modsPath) || !File.Exists(classesPath)) return;

        var catalog = new FileCatalogSource(
            Path.Combine(root, "data", "catalogs", "poe2.catalog.json")).Load();
        var mods = RePoEJson.LoadMods(modsPath);
        var classes = RePoEJson.LoadItemClasses(classesPath);
        var classByMod = CatalogBuilder.MapModsToClasses(raw, classes);
        var shipped = catalog.Mods.Where(m => m.Source == "corrupted").ToList();

        shipped.Should().NotBeEmpty();
        foreach (var mod in shipped)
        {
            mods.Should().ContainKey(mod.Id);
            mods[mod.Id].Domain.Should().Be("item");
            mods[mod.Id].GenerationType.Should().Be("corrupted");
            (mods[mod.Id].SpawnWeights ?? [])
                .Should().Contain(w => w.Weight > 0,
                    $"{mod.Id} must have a positive spawn weight");
            classByMod.Should().ContainKey(mod.Id,
                $"{mod.Id} must be mapped through a regular PoE2 base");
            foreach (var cls in mod.Classes)
                classByMod[mod.Id].Should().Contain(cls,
                    $"{mod.Id} class '{cls}' must come from mods_by_base");
        }
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(
                    directory.FullName, "data", "catalogs", "poe2.catalog.json")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("repository root not found");
    }
}

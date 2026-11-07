using PoeRegex.Core.Data;
using PoeRegex.Data.Build.RePoE;

namespace PoeRegex.Data.Build.Tests;

/// <summary>
/// Release-prep provenance: every shipped implicit must be anchored to a
/// released PoE2 base item. Raw exports are gitignored, so the check runs when
/// the local release-prep export is present and is a no-op in CI, which
/// validates the committed artifacts instead.
/// </summary>
public class ImplicitProvenanceTests
{
    [Fact]
    public void ShippedImplicits_ResolveToReleasedPoE2Bases()
    {
        var root = RepoRoot();
        var rawPath = Path.Combine(root, "data", "raw", "poe2", "base_items.min.json");
        if (!File.Exists(rawPath)) return;

        var catalog = new FileCatalogSource(
            Path.Combine(root, "data", "catalogs", "poe2.catalog.json")).Load();
        var bases = RePoEJson.LoadBaseItems(rawPath);
        var implicitMods = catalog.Mods.Where(m => m.Source == "implicit").ToList();

        implicitMods.Should().NotBeEmpty();
        foreach (var mod in implicitMods)
        {
            mod.GenerationType.Should().Be("implicit");
            mod.Classes.Should().NotBeEmpty();

            var released = bases
                .Where(kv => kv.Value.Domain == "item"
                    && string.Equals(
                        kv.Value.ReleaseState, "released", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(kv.Value.Name)
                    && !kv.Key.Contains("Unique", StringComparison.OrdinalIgnoreCase)
                    && (kv.Value.Implicits ?? []).Contains(mod.Id, StringComparer.Ordinal))
                .ToList();

            released.Should().NotBeEmpty(
                $"{mod.Id} must be anchored to a released PoE2 base");
            foreach (var cls in mod.Classes)
                released.Should().Contain(r => r.Value.ItemClass == cls,
                    $"{mod.Id} class '{cls}' must come from a released PoE2 base");
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

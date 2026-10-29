using System.Text.Json;

namespace PoeRegex.Core.Data;

/// <summary>
/// Loads the embedded PoE2 catalog, keeping published binaries self-contained.
/// </summary>
public sealed class EmbeddedCatalogSource : ICatalogSource
{
    private const string ResourceName = "PoeRegex.Core.catalogs.poe2.catalog.json";

    public Catalog Load()
    {
        var assembly = typeof(EmbeddedCatalogSource).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
                           ?? throw new InvalidOperationException($"Embedded catalog not found: {ResourceName}");
        return JsonSerializer.Deserialize(stream, CatalogJsonContext.Default.Catalog)
               ?? throw new InvalidDataException($"Embedded catalog '{ResourceName}' is empty.");
    }
}

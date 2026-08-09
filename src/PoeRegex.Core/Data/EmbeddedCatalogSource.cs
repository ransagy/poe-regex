using System.Reflection;
using System.Text.Json;

namespace PoeRegex.Core.Data;

/// <summary>
/// Loads a catalog from an embedded resource, keeping published (Native AOT)
/// binaries self-contained. Logical names are pinned in PoeRegex.Core.csproj.
/// </summary>
public sealed class EmbeddedCatalogSource : ICatalogSource
{
    public Catalog Load(GameTarget game)
    {
        var name = $"PoeRegex.Core.catalogs.{game.ToRuleKey()}.catalog.json";
        var assembly = typeof(EmbeddedCatalogSource).Assembly;
        using var stream = assembly.GetManifestResourceStream(name)
                           ?? throw new InvalidOperationException($"Embedded catalog not found: {name}");
        return JsonSerializer.Deserialize(stream, CatalogJsonContext.Default.Catalog)
               ?? throw new InvalidDataException($"Embedded catalog '{name}' is empty.");
    }
}

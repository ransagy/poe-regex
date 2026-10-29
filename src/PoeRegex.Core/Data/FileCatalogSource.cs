using System.Text.Json;

namespace PoeRegex.Core.Data;

/// <summary>Loads a PoE2 catalog from a JSON file on disk.</summary>
public sealed class FileCatalogSource : ICatalogSource
{
    private readonly string _path;

    public FileCatalogSource(string path) => _path = path;

    public Catalog Load()
    {
        if (!File.Exists(_path))
        {
            throw new FileNotFoundException($"Catalog not found: {_path}", _path);
        }

        using var stream = File.OpenRead(_path);
        return JsonSerializer.Deserialize(stream, CatalogJsonContext.Default.Catalog)
               ?? throw new InvalidDataException($"Catalog at {_path} is empty.");
    }
}

using System.Text.Json;

namespace PoeRegex.Core.Data;

/// <summary>Loads a catalog from a JSON file on disk.</summary>
public sealed class FileCatalogSource : ICatalogSource
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    private readonly string _path;

    public FileCatalogSource(string path) => _path = path;

    public Catalog Load(GameTarget game)
    {
        var path = _path.Replace("{game}", game.ToRuleKey());
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Catalog not found: {path}", path);
        }

        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<Catalog>(stream, Options)
               ?? throw new InvalidDataException($"Catalog at {path} is empty.");
    }
}

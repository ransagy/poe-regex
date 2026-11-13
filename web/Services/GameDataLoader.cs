using System.Text.Json;
using PoeRegex.Core.Compilation;
using PoeRegex.Core.Data;

namespace web.Services;

/// <summary>Loads a game's catalog/table/corpus; PoE2 stays embedded, PoE1 is fetched once.</summary>
public sealed class GameDataLoader(HttpClient http)
{
    private readonly Dictionary<string, GameData> _cache = new(StringComparer.Ordinal);

    public async Task<GameData> LoadAsync(string game)
    {
        if (_cache.TryGetValue(game, out var cached)) return cached;
        if (string.Equals(game, "poe2", StringComparison.Ordinal))
        {
            var embedded = new GameData(new EmbeddedCatalogSource().Load());
            _cache[game] = embedded;
            return embedded;
        }

        var baseUri = $"data/poe1/";
        var catalogJson = await http.GetStringAsync(baseUri + "catalog.json");
        var tableJson = await http.GetStringAsync(baseUri + "fragments.json");
        var vendorJson = await http.GetStringAsync(baseUri + "vendor-corpus.json");
        var catalog = JsonSerializer.Deserialize(catalogJson, CatalogJsonContext.Default.Catalog)
                      ?? throw new InvalidDataException("PoE1 catalog is empty.");
        var table = JsonSerializer.Deserialize(tableJson, CatalogJsonContext.Default.FragmentTable)
                    ?? throw new InvalidDataException("PoE1 fragment table is empty.");
        var vendor = JsonSerializer.Deserialize(vendorJson, CatalogJsonContext.Default.VendorCorpusFile)
                     ?? throw new InvalidDataException("PoE1 corpus is empty.");
        var data = new GameData(catalog, table, vendor, []);
        _cache[game] = data;
        return data;
    }
}

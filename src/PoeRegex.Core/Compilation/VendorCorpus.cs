using System.Text.Json;
using System.Text.Json.Serialization;
using PoeRegex.Core.Data;

namespace PoeRegex.Core.Compilation;

/// <summary>
/// A line from the vendor/stash searchable-text corpus. ClassId is null for
/// lines that only participate in the global scope (currency names, etc.).
/// <see cref="Pattern"/> is the value-agnostic catalog pattern a displayed
/// variant belongs to (e.g. the actual roll/range form and the min/max form
/// of one stat share the catalog's pattern). Null means the line is foreign
/// text such as a name, caption, base property, or usage hint.
/// </summary>
public sealed record VendorLine(
    [property: JsonPropertyName("class")] string? ClassId,
    [property: JsonPropertyName("line")] string Line,
    [property: JsonPropertyName("kind")] string? Kind = null,
    [property: JsonPropertyName("pattern")] string? Pattern = null);

public sealed class VendorCorpusFile
{
    [property: JsonPropertyName("game")] public required string Game { get; init; }
    [property: JsonPropertyName("dataVersion")] public required string DataVersion { get; init; }
    [property: JsonPropertyName("lines")] public required IReadOnlyList<VendorLine> Lines { get; init; }
}

/// <summary>
/// Loads the derived vendor/stash search corpus. This deliberately excludes
/// unique-only mods/names/flavour and rare item names; the selector's output
/// is proven only over the modeled categories.
/// </summary>
public static class VendorCorpus
{
    private const string ResourceName = "PoeRegex.Core.catalogs.poe2.vendor-corpus.json";
    private static readonly object CacheLock = new();
    private static VendorCorpusFile? _cached;
    private static Task<VendorCorpusFile?>? _loadTask;

    /// <summary>Synchronous load for tests, CLI, and data builds.</summary>
    public static VendorCorpusFile? ForCurrentGame
    {
        get
        {
            if (_cached is not null) return _cached;
            lock (CacheLock)
            {
                if (_cached is not null) return _cached;
                _cached = LoadSync(ResourceName);
                return _cached;
            }
        }
    }

    /// <summary>
    /// Browser load. The deserializer reads through <see cref="YieldingStream"/>,
    /// which yields between small chunks so the 14.5 MB corpus never becomes a
    /// single long task on the WASM main thread.
    /// </summary>
    public static Task<VendorCorpusFile?> ForCurrentGameAsync()
    {
        if (_cached is not null) return Task.FromResult<VendorCorpusFile?>(_cached);
        lock (CacheLock)
        {
            if (_cached is not null) return Task.FromResult<VendorCorpusFile?>(_cached);
            return _loadTask ??= LoadAsync(ResourceName);
        }
    }

    private static VendorCorpusFile? LoadSync(string resourceName)
    {
        using var stream = typeof(VendorCorpus).Assembly.GetManifestResourceStream(resourceName);
        if (stream is null) return null;
        try
        {
            return JsonSerializer.Deserialize(stream, CatalogJsonContext.Default.VendorCorpusFile);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<VendorCorpusFile?> LoadAsync(string resourceName)
    {
        using var stream = typeof(VendorCorpus).Assembly.GetManifestResourceStream(resourceName);
        if (stream is null) return null;
        try
        {
            // Read the resource once, then parse only the lines array. The
            // browser can paint between element chunks; the array slice also
            // avoids the deserializer's many tiny reads on the embedded stream.
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            var json = buffer.ToArray();
            var (start, length, game, dataVersion) = FindLinesArray(json);
            using var array = new MemoryStream(json, start, length, writable: false);
            var lines = new List<VendorLine>(110_000);
            var count = 0;
            await foreach (var line in JsonSerializer.DeserializeAsyncEnumerable(
                array, CatalogJsonContext.Default.VendorLine).ConfigureAwait(false))
            {
                lines.Add(line!);
                if (++count % 500 == 0) await Task.Delay(1).ConfigureAwait(false);
            }
            var result = new VendorCorpusFile
            {
                Game = game,
                DataVersion = dataVersion,
                Lines = lines,
            };
            _cached = result;
            return result;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static (int Start, int Length, string Game, string DataVersion) FindLinesArray(byte[] json)
    {
        var reader = new Utf8JsonReader(json);
        string? game = null;
        string? dataVersion = null;
        while (reader.Read())
        {
            if (reader.TokenType != JsonTokenType.PropertyName) continue;
            if (reader.ValueTextEquals("game"))
            {
                reader.Read();
                game = reader.GetString();
            }
            else if (reader.ValueTextEquals("dataVersion"))
            {
                reader.Read();
                dataVersion = reader.GetString();
            }
            else if (reader.ValueTextEquals("lines"))
            {
                if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray)
                    throw new JsonException("lines is not an array");
                var start = (int)reader.TokenStartIndex;
                reader.Skip();
                var length = (int)reader.BytesConsumed - start;
                return (start, length, game ?? "", dataVersion ?? "");
            }
        }
        throw new JsonException("lines array not found");
    }

}

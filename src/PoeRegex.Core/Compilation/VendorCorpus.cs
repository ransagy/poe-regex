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
    [JsonPropertyName("game")] public required string Game { get; init; }
    [JsonPropertyName("dataVersion")] public required string DataVersion { get; init; }
    [JsonPropertyName("lines")] public required IReadOnlyList<VendorLine> Lines { get; init; }
}

/// <summary>
/// Loads the derived vendor/stash search corpus. This deliberately excludes
/// unique-only mods/names/flavour and rare item names; the selector's output
/// is proven only over the modeled categories.
/// </summary>
public static class VendorCorpus
{
    private static readonly Lazy<VendorCorpusFile?> Poe1 =
        new(() => Load("PoeRegex.Core.catalogs.poe1.vendor-corpus.json"));
    private static readonly Lazy<VendorCorpusFile?> Poe2 =
        new(() => Load("PoeRegex.Core.catalogs.poe2.vendor-corpus.json"));

    public static VendorCorpusFile? For(string game) => game switch
    {
        "poe1" => Poe1.Value,
        "poe2" => Poe2.Value,
        _ => null,
    };

    private static VendorCorpusFile? Load(string resourceName)
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
}

using System.Text.Json.Serialization;

namespace PoeRegex.Core.Data;

/// <summary>
/// Derived catalog emitted by PoeRegex.Data.Build and consumed by the engine.
/// The only game-data artifact the engine reads. No raw game data is stored.
/// </summary>
public sealed class Catalog
{
    [JsonPropertyName("game")]
    public required string Game { get; init; }

    [JsonPropertyName("dataVersion")]
    public required string DataVersion { get; init; }

    [JsonPropertyName("itemClasses")]
    public required IReadOnlyList<ItemClassEntry> ItemClasses { get; init; }

    [JsonPropertyName("mods")]
    public required IReadOnlyList<ModEntry> Mods { get; init; }
}

public sealed class ItemClassEntry
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>Fingerprint fragment matching the "Item Class: X" line for this class.</summary>
    [JsonPropertyName("baseFingerprint")]
    public required string BaseFingerprint { get; init; }
}

public sealed class ModEntry
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("generationType")]
    public required string GenerationType { get; init; }

    [JsonPropertyName("classes")]
    public required IReadOnlyList<string> Classes { get; init; }

    /// <summary>RePoE mod group names (one-mod-per-group exclusivity). Empty when unknown.</summary>
    [JsonPropertyName("groups")]
    public IReadOnlyList<string> Groups { get; init; } = [];

    [JsonPropertyName("statLines")]
    public required IReadOnlyList<StatLineEntry> StatLines { get; init; }
}

public sealed class StatLineEntry
{
    /// <summary>Human-readable rendered line with the representative (max) value.</summary>
    [JsonPropertyName("line")]
    public required string Line { get; init; }

    /// <summary>Value-agnostic form: every digit-run replaced by a single placeholder character.</summary>
    [JsonPropertyName("pattern")]
    public required string Pattern { get; init; }

    [JsonPropertyName("minValue")]
    public required double MinValue { get; init; }

    [JsonPropertyName("maxValue")]
    public required double MaxValue { get; init; }

    /// <summary>Shortest unique fragment per item class this line can appear on.</summary>
    [JsonPropertyName("fingerprints")]
    public required IReadOnlyDictionary<string, string> Fingerprints { get; init; }
}

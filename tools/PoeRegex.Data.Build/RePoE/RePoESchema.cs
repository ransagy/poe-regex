using System.Text.Json;
using System.Text.Json.Serialization;

namespace PoeRegex.Data.Build.RePoE;

/// <summary>Raw RePoE export schema types (poe1 monolithic + poe2 per-file formats).</summary>
public static class RePoESchema
{
    // ---- stat_value_handlers.json ----
    public sealed class StatValueHandler
    {
        [JsonPropertyName("type")] public required string Type { get; init; }
        [JsonPropertyName("multiplier")] public double? Multiplier { get; init; }
        [JsonPropertyName("divisor")] public double? Divisor { get; init; }
        [JsonPropertyName("addend")] public double? Addend { get; init; }
        [JsonPropertyName("precision")] public int? Precision { get; init; }
        [JsonPropertyName("fixed")] public bool? Fixed { get; init; }
        [JsonPropertyName("values")] public Dictionary<string, string>? Values { get; init; }
    }

    // ---- stat_translations.json (poe1) / *_stat_descriptions.json (poe2) ----
    public sealed class StatTranslationEntry
    {
        [JsonPropertyName("English")] public required List<TranslationVariant> English { get; init; }
        [JsonPropertyName("ids")] public required List<string> Ids { get; init; }
    }

    public sealed class TranslationVariant
    {
        [JsonPropertyName("condition")] public required List<Condition> Condition { get; init; }
        [JsonPropertyName("format")] public required List<string> Format { get; init; }
        [JsonPropertyName("index_handlers")] public required List<List<string>> IndexHandlers { get; init; }
        [JsonPropertyName("string")] public required string String { get; init; }
    }

    public sealed class Condition
    {
        [JsonPropertyName("min")] public int? Min { get; init; }
        [JsonPropertyName("max")] public int? Max { get; init; }
        [JsonPropertyName("negated")] public bool? Negated { get; init; }
    }

    // ---- mods.json (dict keyed by mod id) ----
    public sealed class Mod
    {
        [JsonPropertyName("name")] public string? Name { get; init; }
        [JsonPropertyName("text")] public string? Text { get; init; }
        [JsonPropertyName("domain")] public required string Domain { get; init; }
        [JsonPropertyName("generation_type")] public required string GenerationType { get; init; }
        [JsonPropertyName("stats")] public required List<ModStat> Stats { get; init; }
        [JsonPropertyName("groups")] public List<string>? Groups { get; init; }
        [JsonPropertyName("spawn_weights")] public List<SpawnWeight>? SpawnWeights { get; init; }
    }

    public sealed class ModStat
    {
        [JsonPropertyName("id")] public required string Id { get; init; }
        [JsonPropertyName("min")] public double Min { get; init; }
        [JsonPropertyName("max")] public double Max { get; init; }
    }

    public sealed class SpawnWeight
    {
        [JsonPropertyName("tag")] public required string Tag { get; init; }
        [JsonPropertyName("weight")] public int Weight { get; init; }
    }

    // ---- item_classes.json ----
    public sealed class ItemClass
    {
        [JsonPropertyName("id")] public string? Id { get; init; }
        [JsonPropertyName("name")] public required string Name { get; init; }
        [JsonPropertyName("category")] public string? Category { get; init; }
    }

    // ---- base_items.json ----
    public sealed class BaseItem
    {
        [JsonPropertyName("item_class")] public required string ItemClass { get; init; }
        [JsonPropertyName("name")] public required string Name { get; init; }
        [JsonPropertyName("tags")] public List<string>? Tags { get; init; }
    }
}

/// <summary>JSON helpers for loading raw RePoE exports.</summary>
public static class RePoEJson
{
    public static T Load<T>(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<T>(json, Options) ?? throw new InvalidDataException($"Empty JSON: {path}");
    }

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>Loads a mods.json where the top level is a dict keyed by mod id.</summary>
    public static Dictionary<string, RePoESchema.Mod> LoadMods(string path)
    {
        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        var result = new Dictionary<string, RePoESchema.Mod>();
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            var mod = prop.Value.Deserialize<RePoESchema.Mod>(Options);
            if (mod is null) continue;
            result[prop.Name] = mod;
        }
        return result;
    }

    /// <summary>Loads a stat_translations/stat_descriptions file (list of entries).</summary>
    public static List<RePoESchema.StatTranslationEntry> LoadTranslations(string path) =>
        Load<List<RePoESchema.StatTranslationEntry>>(path);

    /// <summary>Loads item_classes.json (dict keyed by id).</summary>
    public static Dictionary<string, RePoESchema.ItemClass> LoadItemClasses(string path)
    {
        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        var result = new Dictionary<string, RePoESchema.ItemClass>();
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            var cls = prop.Value.Deserialize<RePoESchema.ItemClass>(Options);
            if (cls is null) continue;
            result[prop.Name] = cls;
        }
        return result;
    }

    /// <summary>Loads base_items.json (dict keyed by metadata path).</summary>
    public static Dictionary<string, RePoESchema.BaseItem> LoadBaseItems(string path)
    {
        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        var result = new Dictionary<string, RePoESchema.BaseItem>();
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            var item = prop.Value.Deserialize<RePoESchema.BaseItem>(Options);
            if (item is null) continue;
            result[prop.Name] = item;
        }
        return result;
    }

    /// <summary>Loads stat_value_handlers.json (dict keyed by handler name).</summary>
    public static Dictionary<string, RePoESchema.StatValueHandler> LoadValueHandlers(string path) =>
        Load<Dictionary<string, RePoESchema.StatValueHandler>>(path);
}

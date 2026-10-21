using System.Text.Json;
using System.Text.Json.Serialization;

namespace PoeRegex.Core.Compilation;

/// <summary>
/// Precomputed minimal fragments for a catalog, one map for global searches
/// and one per item class. The tables are generated from
/// <see cref="FragmentSelector"/> at data time (see tools) so the browser and
/// tests never pay the exhaustive search on a fresh scope. A version mismatch
/// disables the table and the selector falls back to computing.
/// </summary>
public sealed class FragmentTable
{
    public string Game { get; init; } = "";
    public string DataVersion { get; init; } = "";
    public Dictionary<string, string> Global { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, Dictionary<string, string>> ByClass { get; init; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Patterns with no safe verbatim line-local fragment in the global scope,
    /// with the reason. The class tables may still resolve them; the UI must
    /// not silently emit a broad fragment for these.
    /// </summary>
    public Dictionary<string, string> GlobalUnresolved { get; init; } = new(StringComparer.Ordinal);

    public Dictionary<string, Dictionary<string, string>> ByClassUnresolved { get; init; } =
        new(StringComparer.Ordinal);

    public bool TryGet(string? scope, string pattern, out string? fragment)
    {
        if (scope is null) return Global.TryGetValue(pattern, out fragment);
        if (ByClass.TryGetValue(scope, out var map)) return map.TryGetValue(pattern, out fragment);
        fragment = null;
        return false;
    }

    public string? UnresolvedReason(string? scope, string pattern)
    {
        if (scope is null) return GlobalUnresolved.GetValueOrDefault(pattern);
        return ByClassUnresolved.TryGetValue(scope, out var map) ? map.GetValueOrDefault(pattern) : null;
    }
}

[JsonSerializable(typeof(FragmentTable))]
internal partial class FragmentTableJsonContext : JsonSerializerContext
{
}

/// <summary>Loads the embedded per-catalog fragment tables.</summary>
internal static class FragmentTables
{
    private static readonly Lazy<FragmentTable?> Poe1 =
        new(() => Load("PoeRegex.Core.catalogs.poe1.fragments.json"));
    private static readonly Lazy<FragmentTable?> Poe2 =
        new(() => Load("PoeRegex.Core.catalogs.poe2.fragments.json"));

    public static FragmentTable? For(string game) => game switch
    {
        "poe1" => Poe1.Value,
        "poe2" => Poe2.Value,
        _ => null,
    };

    private static FragmentTable? Load(string resourceName)
    {
        using var stream = typeof(FragmentTables).Assembly.GetManifestResourceStream(resourceName);
        if (stream is null) return null;
        try
        {
            return JsonSerializer.Deserialize(stream, FragmentTableJsonContext.Default.FragmentTable);
        }
        catch
        {
            return null;
        }
    }
}

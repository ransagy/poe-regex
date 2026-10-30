using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace PoeRegex.Web.Core;

/// <summary>A named set of picks (modes, classes, picks; never test items).</summary>
public sealed record SavedRecipe(string Name, DateTimeOffset SavedAt, WorkbenchSnapshot Snapshot)
{
    public int PickCount =>
        Snapshot.Global.Included.Count + Snapshot.Global.Excluded.Count +
        Snapshot.Slots.Values.Sum(c => c.Included.Count + c.Excluded.Count);
}

/// <summary>Captures picks as a detached copy and packs them into a share-link fragment.</summary>
public static class RecipeCodec
{
    public const string FragmentPrefix = "r=";
    private const int MaxJsonBytes = 64 * 1024;

    /// <summary>A deep copy: Capture hands out the live criteria objects.</summary>
    public static WorkbenchSnapshot CaptureCriteria(WorkbenchState state)
    {
        var json = JsonSerializer.Serialize(
            WorkbenchSnapshot.Capture(state) with { TestItems = [] }, WebJsonContext.Default.WorkbenchSnapshot);
        return JsonSerializer.Deserialize(json, WebJsonContext.Default.WorkbenchSnapshot)!;
    }

    public static string Encode(WorkbenchSnapshot snap)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(snap with { TestItems = [] }, WebJsonContext.Default.WorkbenchSnapshot);
        using var packed = new MemoryStream();
        using (var deflate = new DeflateStream(packed, CompressionLevel.SmallestSize, leaveOpen: true))
            deflate.Write(json);
        return Convert.ToBase64String(packed.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>Null for anything that is not a well-formed recipe; shared links are untrusted input.</summary>
    public static WorkbenchSnapshot? Decode(string token)
    {
        try
        {
            var b64 = token.Replace('-', '+').Replace('_', '/');
            b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
            using var inflate = new DeflateStream(new MemoryStream(Convert.FromBase64String(b64)), CompressionMode.Decompress);
            using var json = new MemoryStream();
            var buffer = new byte[8192];
            int read;
            while ((read = inflate.Read(buffer)) > 0)
            {
                if (json.Length + read > MaxJsonBytes) return null;
                json.Write(buffer, 0, read);
            }
            var snap = JsonSerializer.Deserialize(json.ToArray(), WebJsonContext.Default.WorkbenchSnapshot);
            if (snap?.Slots is null || snap.Global is null) return null;
            if (snap.Slots.Values.Any(c => c is null || c.Included is null || c.Excluded is null)) return null;
            if (snap.Global.Included is null || snap.Global.Excluded is null) return null;
            if (snap.Slots.Values.SelectMany(c => c.Included).Concat(snap.Global.Included).Any(m => m?.ModId is null)) return null;
            return snap with { TestItems = [] };
        }
        catch (Exception ex) when (ex is FormatException or InvalidDataException or JsonException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Reads "r=…" from a URL fragment (with or without the leading '#').</summary>
    public static string? TokenFromFragment(string? fragment)
    {
        if (string.IsNullOrEmpty(fragment)) return null;
        var f = fragment.TrimStart('#');
        return f.StartsWith(FragmentPrefix, StringComparison.Ordinal) && f.Length > FragmentPrefix.Length
            ? f[FragmentPrefix.Length..]
            : null;
    }
}

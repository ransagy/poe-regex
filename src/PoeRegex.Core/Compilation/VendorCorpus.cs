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
            using var yielding = new YieldingStream(stream);
            var result = await JsonSerializer.DeserializeAsync(
                yielding, CatalogJsonContext.Default.VendorCorpusFile).ConfigureAwait(false);
            _cached = result;
            return result;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Caps reads to 64 KiB and yields after each one. The JSON deserializer
    /// calls <c>ReadAsync</c> repeatedly, so the browser gets a chance to paint
    /// and handle input between chunks.
    /// </summary>
    private sealed class YieldingStream(Stream inner, int chunkSize = 64 * 1024) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count) =>
            inner.Read(buffer, offset, Math.Min(count, chunkSize));

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await inner.ReadAsync(
                buffer[..Math.Min(buffer.Length, chunkSize)], cancellationToken).ConfigureAwait(false);
            await Task.Delay(1, cancellationToken).ConfigureAwait(false);
            return read;
        }

        public override Task<int> ReadAsync(
            byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }
}

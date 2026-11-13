using System.Text.Json;
using PoeRegex.Core.Compilation;
using PoeRegex.Core.Data;

namespace PoeRegex.Data.Build;

/// <summary>Loads a catalog plus its optional foreign corpus for build tools.</summary>
internal static class CorpusLoader
{
    public static EngineCorpus Load(string catalogPath, string vendorPath)
    {
        var catalog = new FileCatalogSource(catalogPath).Load();
        VendorCorpusFile? vendor = null;
        if (vendorPath.Length > 0 && File.Exists(vendorPath))
        {
            vendor = JsonSerializer.Deserialize<VendorCorpusFile>(
                File.ReadAllText(vendorPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        return new EngineCorpus(catalog, null, vendor);
    }
}

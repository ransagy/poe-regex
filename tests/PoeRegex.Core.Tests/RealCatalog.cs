using PoeRegex.Core.Compilation;
using PoeRegex.Core.Data;

namespace PoeRegex.Core.Tests;

/// <summary>Loads the real PoE2 catalog from the repo for composition tests.</summary>
public static class RealCatalog
{
    public static EngineCorpus Corpus()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "data", "catalogs")))
            dir = Path.GetDirectoryName(dir);
        if (dir is null) throw new DirectoryNotFoundException("repo root not found");
        var path = Path.Combine(dir, "data", "catalogs", "poe2.catalog.json");
        return new EngineCorpus(new FileCatalogSource(path).Load());
    }
}

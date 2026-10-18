using PoeRegex.Core.Compilation;
using PoeRegex.Core.Data;

namespace PoeRegex.Core.Tests;

/// <summary>Loads a real catalog from the repo for composition tests.</summary>
public static class RealCatalog
{
    public static EngineCorpus Corpus(GameTarget game = GameTarget.PoE2)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "data", "catalogs")))
            dir = Path.GetDirectoryName(dir);
        if (dir is null) throw new DirectoryNotFoundException("repo root not found");
        var file = game == GameTarget.PoE1 ? "poe1.catalog.json" : "poe2.catalog.json";
        var path = Path.Combine(dir, "data", "catalogs", file);
        return new EngineCorpus(new FileCatalogSource(path).Load(game));
    }
}

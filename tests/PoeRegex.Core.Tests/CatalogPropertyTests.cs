using PoeRegex.Core.Compilation;
using PoeRegex.Core.Data;

namespace PoeRegex.Core.Tests;

/// <summary>
/// Property tests over the real derived catalogs (checked into data/catalogs).
/// These validate the catalog builder output end-to-end, not just the fixtures.
/// </summary>
public class CatalogPropertyTests
{
    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "data", "catalogs")))
        {
            dir = Path.GetDirectoryName(dir);
        }
        return dir ?? throw new DirectoryNotFoundException("repo root not found");
    }

    public static IEnumerable<object[]> Catalogs()
    {
        yield return new object[] { GameTarget.PoE1 };
        yield return new object[] { GameTarget.PoE2 };
    }

    [Theory]
    [MemberData(nameof(Catalogs))]
    public void EveryCatalogFingerprint_IsSafeOnItsClass(GameTarget game)
    {
        var path = Path.Combine(RepoRoot(), "data", "catalogs", $"{game.ToRuleKey()}.catalog.json");
        var corpus = new EngineCorpus(new FileCatalogSource(path).Load(game));

        var checkedCount = 0;
        foreach (var mod in corpus.Mods)
        {
            foreach (var line in mod.StatLines)
            {
                foreach (var (cls, fp) in line.Fingerprints)
                {
                    // The fingerprint must match at least one line of this pattern and
                    // no line of any other pattern in the class corpus.
                    corpus.FragmentSafeOnClass(cls, fp, new[] { line.Pattern }).Should().BeTrue(
                        $"fingerprint '{fp}' of {mod.Id} line '{line.Line}' must be safe on class {cls}");
                    checkedCount++;
                }
            }
        }
        checkedCount.Should().BeGreaterThan(1000, "expected a meaningful number of fingerprints to validate");
    }

    [Theory]
    [MemberData(nameof(Catalogs))]
    public void EveryClassWithMods_HasBaseFingerprint(GameTarget game)
    {
        var path = Path.Combine(RepoRoot(), "data", "catalogs", $"{game.ToRuleKey()}.catalog.json");
        var corpus = new EngineCorpus(new FileCatalogSource(path).Load(game));
        var withMods = corpus.Catalog.ItemClasses
            .Where(c => corpus.Mods.Any(m => m.Classes.Contains(c.Id)))
            .ToList();
        foreach (var cls in withMods)
        {
            var fp = corpus.ClassFingerprint(cls.Id);
            fp.Should().NotBeNullOrEmpty($"class {cls.Id} ({cls.Name}) needs a base fingerprint");
        }
    }
}

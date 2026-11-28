using System.Security.Cryptography;

namespace PoeRegex.Data.Build.Tests;

/// <summary>
/// PoE1 is served from <c>web/wwwroot/data/poe1</c>, not the embedded catalog.
/// The checked-in web copy must stay byte-identical to the source artifacts so
/// a data rebuild cannot silently ship an old fragment table.
/// </summary>
public class Poe1WebCopyTests
{
    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(
                    directory.FullName, "data", "catalogs", "poe1.catalog.json")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("repository root not found");
    }

    [Theory]
    [InlineData("catalog.json", "poe1.catalog.json")]
    [InlineData("fragments.json", "poe1.fragments.json")]
    [InlineData("vendor-corpus.json", "poe1.vendor-corpus.json")]
    public void ShippedWebCopy_MatchesTheCatalogArtifact(
        string webName, string catalogName)
    {
        var root = RepoRoot();
        var expected = Path.Combine(root, "data", "catalogs", catalogName);
        var actual = Path.Combine(root, "web", "wwwroot", "data", "poe1", webName);
        File.Exists(expected).Should().BeTrue(expected);
        File.Exists(actual).Should().BeTrue(
            $"{actual} is missing; run tools/sync-poe1-data.sh");
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(expected)))
            .Should().Be(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(actual))),
                $"{actual} is stale; run tools/sync-poe1-data.sh");
    }
}

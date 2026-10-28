using System.Text.Json;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using PoeRegex.Core;
using PoeRegex.Core.Data;

namespace PoeRegex.Data.Build.Tests;

public class CandidateMatrixTests
{
    [Fact]
    public void MergeBest_KeepsShorterScope()
    {
        var dir = Path.Combine(Path.GetTempPath(), "poe-regex-matrix-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var longFile = Path.Combine(dir, "long.html");
            var shortFile = Path.Combine(dir, "short.html");
            var output = Path.Combine(dir, "out.html");
            File.WriteAllText(longFile, Matrix("^long-form$", 10, "strict"));
            File.WriteAllText(shortFile, Matrix("short", 5, "strict"));

            var exit = CandidateMatrix.Run([
                "--game", "poe2",
                "--merge", longFile + "|" + shortFile,
                "--merge-best",
                "--max-length", "14",
                "--out", output,
            ]);

            exit.Should().Be(0);
            var html = File.ReadAllText(output);
            var match = Regex.Match(html, @"window\.MATRIX = (\{.*?\});</script>", RegexOptions.Singleline);
            using var document = JsonDocument.Parse(match.Groups[1].Value);
            var chosen = document.RootElement
                .GetProperty("families")[0]
                .GetProperty("patterns")[0]
                .GetProperty("scopes")[0]
                .GetProperty("chosen");
            chosen.GetProperty("fragment").GetString().Should().Be("short");
            chosen.GetProperty("length").GetInt32().Should().Be(5);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ShippedCatalog_DoesNotMapReducedCritToBuckler()
    {
        var root = RepoRoot();
        var catalog = new FileCatalogSource(
            Path.Combine(root, "data", "catalogs", "poe2.catalog.json")).Load(GameTarget.PoE2);
        var mod = catalog.Mods.First(m => m.Id.StartsWith("ReducedExtraDamageFromCrits"));
        mod.Classes.Should().Contain("Shield");
        mod.Classes.Should().NotContain("Buckler",
            "the only Buckler base with the str_dex_shield tag is unique-only");
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "data", "catalogs", "poe2.catalog.json")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("repository root not found");
    }

    private static string Matrix(string fragment, int length, string coverage) =>
        "<html><script>window.MATRIX = " +
        JsonSerializer.Serialize(new
        {
            game = "poe2",
            generated = "2026-01-01 00:00:00Z",
            families = new[]
            {
                new
                {
                    key = "TestFamily",
                    prefix = "suffix",
                    display = "test",
                    tiers = 1,
                    classes = Array.Empty<string>(),
                    patterns = new[]
                    {
                        new
                        {
                            pattern = "#% test",
                            display = "% test",
                            scopes = new[]
                            {
                                new
                                {
                                    kind = "global",
                                    classes = Array.Empty<string>(),
                                    current = "current",
                                    currentOption = new { fragment = "current" },
                                    chosen = new { fragment, length },
                                    coverage,
                                    bound = 14,
                                },
                            },
                        },
                    },
                },
            },
        }) +
        ";</script></html>";
}

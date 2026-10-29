using System.Text.Json;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using PoeRegex.Core;
using PoeRegex.Core.Compilation;
using PoeRegex.Core.Data;

namespace PoeRegex.Data.Build.Tests;

public class CandidateMatrixTests
{
    [Fact]
    public void ShippedCatalog_DoesNotMapReducedCritToBuckler()
    {
        var root = RepoRoot();
        var catalog = new FileCatalogSource(
            Path.Combine(root, "data", "catalogs", "poe2.catalog.json")).Load();
        var mod = catalog.Mods.First(m => m.Id.StartsWith("ReducedExtraDamageFromCrits"));
        mod.Classes.Should().Contain("Shield");
        mod.Classes.Should().NotContain("Buckler",
            "the only Buckler base with the str_dex_shield tag is unique-only");
    }

    [Fact]
    public void ShippedArtifacts_ExcludeNotInGameClasses()
    {
        var root = RepoRoot();
        var table = JsonSerializer.Deserialize<FragmentTable>(
            File.ReadAllText(Path.Combine(root, "data", "catalogs", "poe2.fragments.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        table.ByClass.Keys.Intersect(GameItemClasses.NotInGame).Should().BeEmpty();
        table.ClassAnchors.Keys.Intersect(GameItemClasses.NotInGame).Should().BeEmpty();

        var html = File.ReadAllText(Path.Combine(root, "docs", "CANDIDATE-MATRIX-poe2.html"));
        var match = Regex.Match(html, @"window\.MATRIX = (\{.*?\});</script>", RegexOptions.Singleline);
        match.Success.Should().BeTrue("the committed matrix artifact must embed its JSON");
        using var document = JsonDocument.Parse(match.Groups[1].Value);
        var leaked = new List<string>();
        foreach (var family in document.RootElement.GetProperty("families").EnumerateArray())
        foreach (var pattern in family.GetProperty("patterns").EnumerateArray())
        foreach (var scope in pattern.GetProperty("scopes").EnumerateArray())
        foreach (var cls in scope.GetProperty("classes").EnumerateArray())
        {
            var id = cls.GetString() ?? "";
            if (GameItemClasses.NotInGame.Contains(id)) leaked.Add(id);
        }
        leaked.Should().BeEmpty();
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
}

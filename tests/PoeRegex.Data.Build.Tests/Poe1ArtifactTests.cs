using System.Text.Json;
using PoeRegex.Core.Compilation;
using PoeRegex.Core.Data;

namespace PoeRegex.Data.Build.Tests;

/// <summary>
/// Independent strictness audit for the shipped PoE1 normal-equipment table.
/// Mirrors the PoE2 strict audit: every entry must match all target lines and
/// zero foreign lines in its scope.
/// </summary>
public class Poe1ArtifactTests
{
    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "data", "catalogs", "poe1.catalog.json")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("repository root not found");
    }

    [Fact]
    public void ShippedPoe1Table_IsStrictAndComplete()
    {
        var root = RepoRoot();
        var catalog = new FileCatalogSource(
            Path.Combine(root, "data", "catalogs", "poe1.catalog.json")).Load();
        var table = JsonSerializer.Deserialize<FragmentTable>(
            File.ReadAllText(Path.Combine(root, "data", "catalogs", "poe1.fragments.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var vendor = JsonSerializer.Deserialize<VendorCorpusFile>(
            File.ReadAllText(Path.Combine(root, "data", "catalogs", "poe1.vendor-corpus.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var corpus = new EngineCorpus(catalog, table, vendor);

        table.Game.Should().Be("poe1");
        table.DataVersion.Should().Be(catalog.DataVersion);
        table.GlobalUnresolved.Should().BeEmpty();
        table.ByClassUnresolved.Values.SelectMany(x => x).Should().BeEmpty();
        table.ByClass.Should().HaveCount(23);

        var failures = new List<string>();
        var checkedCount = 0;

        void CheckScope(string? scope, IReadOnlyDictionary<string, string> map)
        {
            var scopeCorpus = ScopeCorpus.Build(corpus, scope);
            foreach (var (pattern, fragment) in map)
            {
                checkedCount++;
                var option = ExactOracle.Check(scopeCorpus, pattern, fragment);
                if (option is null)
                    failures.Add($"{scope ?? "global"}/{pattern.Replace('\u0001', '#')}: invalid '{fragment}'");
                else if (option.ForeignLines > 0)
                    failures.Add($"{scope ?? "global"}/{pattern.Replace('\u0001', '#')}: {option.ForeignLines} foreign hit(s) for '{fragment}'");
            }
        }

        CheckScope(null, table.Global);
        foreach (var (cls, map) in table.ByClass) CheckScope(cls, map);

        failures.Take(20).Should().BeEmpty($"{failures.Count} PoE1 strict table failures");
        checkedCount.Should().Be(1584);
        table.Global.Should().HaveCount(367);
        table.ByClass.Values.Sum(x => x.Count).Should().Be(1217);
    }

    [Fact]
    public void CapturedBootsSafetyLine_MapsToTheCatalogPattern()
    {
        var root = RepoRoot();
        var catalog = new FileCatalogSource(
            Path.Combine(root, "data", "catalogs", "poe1.catalog.json")).Load();
        var vendor = JsonSerializer.Deserialize<VendorCorpusFile>(
            File.ReadAllText(Path.Combine(root, "data", "catalogs", "poe1.vendor-corpus.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var corpus = new EngineCorpus(catalog, null, vendor);

        corpus.SafetyItems.Should().NotBeEmpty();
        corpus.SafetyLines("Boots").Should().Contain(x =>
            x.Raw == "+69(49-85) to Armour" && x.Pattern == "\u0001 to Armour");

        var scope = ScopeCorpus.Build(corpus, "Boots");
        var check = ExactOracle.Check(scope, "\u0001 to Armour", "o ar");
        check.Should().NotBeNull();
        check!.ForeignLines.Should().Be(0);
    }

    [Fact]
    public void ShippedPoe1Status_ReportsAProvenTable()
    {
        var root = RepoRoot();
        using var status = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(root, "docs", "CANDIDATE-STATUS-poe1.json")));
        var payload = status.RootElement;
        payload.GetProperty("game").GetString().Should().Be("poe1");
        payload.GetProperty("targetModel").GetString().Should().Be(FragmentTable.AdvancedRanges);
        payload.GetProperty("status").GetProperty("stale").GetBoolean().Should().BeFalse();
        payload.GetProperty("status").GetProperty("shortestProven").GetBoolean().Should().BeTrue();
        payload.GetProperty("status").GetProperty("proof").GetProperty("failed").GetInt32()
            .Should().Be(0);
        payload.GetProperty("table").GetProperty("unresolvedGlobal").GetInt32().Should().Be(0);
        payload.GetProperty("table").GetProperty("unresolvedClass").GetInt32().Should().Be(0);
    }
}

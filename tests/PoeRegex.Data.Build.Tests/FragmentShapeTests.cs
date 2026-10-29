using System.Text.Json;
using System.Text.RegularExpressions;
using PoeRegex.Core;
using PoeRegex.Core.Compilation;
using PoeRegex.Core.Data;

namespace PoeRegex.Data.Build.Tests;

public class FragmentShapeTests
{
    [Fact]
    public void ShippedTableFragments_HaveUsableShape()
    {
        var root = RepoRoot();
        var tablePath = Path.Combine(root, "data", "catalogs", "poe2.fragments.json");
        var table = JsonSerializer.Deserialize<FragmentTable>(
            File.ReadAllText(tablePath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        var failures = new List<string>();
        foreach (var (pattern, fragment) in table.Global)
            Check("global", pattern, fragment, failures);
        foreach (var (cls, map) in table.ByClass)
            foreach (var (pattern, fragment) in map)
                Check(cls, pattern, fragment, failures);

        failures.Should().BeEmpty();
    }

    [Fact]
    public void MatrixArtifactFragments_HaveUsableShape()
    {
        var root = RepoRoot();
        var artifact = Path.Combine(root, "docs", "CANDIDATE-MATRIX-poe2.html");
        var html = File.ReadAllText(artifact);
        var match = Regex.Match(html, @"window\.MATRIX = (\{.*?\});</script>", RegexOptions.Singleline);
        match.Success.Should().BeTrue("the committed matrix artifact must embed its JSON");

        using var document = JsonDocument.Parse(match.Groups[1].Value);
        var failures = new List<string>();
        foreach (var family in document.RootElement.GetProperty("families").EnumerateArray())
        {
            var key = family.GetProperty("key").GetString() ?? "";
            foreach (var pattern in family.GetProperty("patterns").EnumerateArray())
            {
                var stat = pattern.GetProperty("pattern").GetString() ?? "";
                foreach (var scope in pattern.GetProperty("scopes").EnumerateArray())
                {
                    var kind = scope.GetProperty("kind").GetString() ?? "";
                    CheckOption($"{key}/{stat}/{kind}/current", scope, "currentOption", failures);
                    CheckOption($"{key}/{stat}/{kind}/chosen", scope, "chosen", failures);
                }
            }
        }
        failures.Should().BeEmpty();
    }

    /// <summary>
    /// The status report is a build artifact; it must agree with the matrix
    /// and table it describes, otherwise stale-scope counts start lying.
    /// </summary>
    [Fact]
    public void MatrixStatus_MatchesArtifacts()
    {
        var root = RepoRoot();
        using var status = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(root, "docs", "CANDIDATE-STATUS-poe2.json")));
        var matrixHtml = File.ReadAllText(
            Path.Combine(root, "docs", "CANDIDATE-MATRIX-poe2.html"));
        var matrixMatch = Regex.Match(
            matrixHtml, @"window\.MATRIX = (\{.*?\});</script>", RegexOptions.Singleline);
        using var matrix = JsonDocument.Parse(matrixMatch.Groups[1].Value);
        var table = JsonSerializer.Deserialize<FragmentTable>(File.ReadAllText(
            Path.Combine(root, "data", "catalogs", "poe2.fragments.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        var families = 0;
        var scopes = 0;
        var longCurrentOnly = 0;
        foreach (var family in matrix.RootElement.GetProperty("families").EnumerateArray())
        {
            families++;
            foreach (var pattern in family.GetProperty("patterns").EnumerateArray())
            foreach (var scope in pattern.GetProperty("scopes").EnumerateArray())
            {
                scopes++;
                if (scope.GetProperty("coverage").GetString() != "current-only") continue;
                var bound = scope.GetProperty("bound").GetInt32();
                var length = scope.GetProperty("chosen").GetProperty("length").GetInt32();
                if (length > bound) longCurrentOnly++;
            }
        }

        var statusRoot = status.RootElement;
        statusRoot.GetProperty("dataVersion").GetString().Should().Be(table.DataVersion);
        statusRoot.GetProperty("matrix").GetProperty("families").GetInt32().Should().Be(families);
        statusRoot.GetProperty("matrix").GetProperty("scopes").GetInt32().Should().Be(scopes);
        statusRoot.GetProperty("matrix").GetProperty("longCurrentOnlyCount").GetInt32()
            .Should().Be(longCurrentOnly);
        statusRoot.GetProperty("table").GetProperty("global").GetInt32()
            .Should().Be(table.Global.Count);
        statusRoot.GetProperty("table").GetProperty("classAnchors").GetInt32()
            .Should().Be(table.ClassAnchors.Count);
        var statusAnchors = statusRoot.GetProperty("classAnchors");
        foreach (var (classId, anchor) in table.ClassAnchors)
            statusAnchors.GetProperty(classId).GetString().Should().Be(anchor);
        statusAnchors.GetProperty("Staff").GetString().Should().Be("Stav");
        statusAnchors.GetProperty("Focus").GetString().Should().Be("Fo");
    }

    /// <summary>
    /// Every shipped anchor must be a clean class-line selector and must not
    /// match a mod or universal line. This is the data-side guard for the
    /// anchor regeneration path.
    /// </summary>
    [Fact]
    public void ShippedClassAnchors_AreExactAndQuiet()
    {
        var root = RepoRoot();
        var table = JsonSerializer.Deserialize<FragmentTable>(
            File.ReadAllText(Path.Combine(root, "data", "catalogs", "poe2.fragments.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var catalog = new FileCatalogSource(
            Path.Combine(root, "data", "catalogs", "poe2.catalog.json")).Load();
        var corpus = new EngineCorpus(catalog);
        var failures = new List<string>();
        foreach (var (classId, anchor) in table.ClassAnchors)
        {
            if (!corpus.ClassTermExact(anchor, [classId]))
                failures.Add($"{classId}: '{anchor}' is not exact");
            if (!corpus.ClassTermQuiet(anchor))
                failures.Add($"{classId}: '{anchor}' matches a mod/universal line");
        }
        failures.Should().BeEmpty();
    }

    /// <summary>
    /// The family row shows the first pattern's global chosen form, quoted,
    /// right-aligned. Every family must therefore have one.
    /// </summary>
    [Fact]
    public void MatrixArtifact_EveryFamilyHasGlobalChosen()
    {
        var root = RepoRoot();
        var artifact = Path.Combine(root, "docs", "CANDIDATE-MATRIX-poe2.html");
        var html = File.ReadAllText(artifact);
        var match = Regex.Match(html, @"window\.MATRIX = (\{.*?\});</script>", RegexOptions.Singleline);
        match.Success.Should().BeTrue("the committed matrix artifact must embed its JSON");

        using var document = JsonDocument.Parse(match.Groups[1].Value);
        var missing = new List<string>();
        foreach (var family in document.RootElement.GetProperty("families").EnumerateArray())
        {
            var key = family.GetProperty("key").GetString() ?? "";
            var patterns = family.GetProperty("patterns");
            var first = patterns.EnumerateArray().First();
            var hasChosen = first.GetProperty("scopes").EnumerateArray().Any(scope =>
                scope.TryGetProperty("chosen", out var chosen) &&
                chosen.ValueKind == JsonValueKind.Object &&
                !string.IsNullOrEmpty(chosen.GetProperty("fragment").GetString()));
            if (!hasChosen) missing.Add(key);
        }

        missing.Should().BeEmpty(
            "the family row renders the first pattern's global chosen form");
    }

    private static void CheckOption(
        string where, JsonElement scope, string property, List<string> failures)
    {
        if (!scope.TryGetProperty(property, out var option) ||
            option.ValueKind != JsonValueKind.Object) return;
        CheckFragment(where, option, failures);
    }

    private static void CheckFragment(string where, JsonElement option, List<string> failures)
    {
        if (!option.TryGetProperty("fragment", out var value) ||
            value.ValueKind != JsonValueKind.String) return;
        var fragment = value.GetString() ?? "";
        foreach (var error in QueryShape.ValidateFragment(fragment))
            failures.Add($"{where}: {error}");
    }

    private static void Check(
        string scope, string pattern, string fragment, List<string> failures)
    {
        foreach (var error in QueryShape.ValidateFragment(fragment))
            failures.Add($"{scope}/{pattern.Replace('\u0001', '#')}: {error}");
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(
                    directory.FullName, "data", "catalogs", "poe2.catalog.json")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("repository root not found");
    }
}

using System.Text.Json;
using System.Text.RegularExpressions;
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
                    CheckOption($"{key}/{stat}/{kind}/floor", scope, "floor", failures);
                    foreach (var list in new[] { "known", "strict", "minion", "floors" })
                        foreach (var option in scope.GetProperty(list).EnumerateArray())
                            CheckFragment($"{key}/{stat}/{kind}/{list}", option, failures);
                }
            }
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
            var global = first.GetProperty("scopes").EnumerateArray()
                .FirstOrDefault(scope =>
                    scope.GetProperty("kind").GetString() == "global");
            if (global.ValueKind != JsonValueKind.Object ||
                !global.TryGetProperty("chosen", out var chosen) ||
                chosen.ValueKind != JsonValueKind.Object ||
                string.IsNullOrEmpty(chosen.GetProperty("fragment").GetString()))
                missing.Add(key);
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

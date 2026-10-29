using System.Text.RegularExpressions;
using AwesomeAssertions;
using PoeRegex.Core;
using PoeRegex.Core.Compilation;
using PoeRegex.Core.Data;

namespace PoeRegex.Data.Build.Tests;

public class ExactSolverTests
{
    [Fact]
    public void ExactSolver_FindsKnownStrictForms()
    {
        var root = RepoRoot();
        var catalog = new FileCatalogSource(
            Path.Combine(root, "data", "catalogs", "poe2.catalog.json")).Load();
        var corpus = new EngineCorpus(catalog);
        var scope = ScopeCorpus.Build(corpus, null);

        var life = ExactSearchSolver.Solve(
            scope, "\u0001% of Damage taken Recouped as Life", 3, 20);
        life.Should().NotBeNull();
        life!.Length.Should().Be(12);

        var armour = ExactSearchSolver.Solve(
            scope, "\u0001% increased Armour, Evasion and Energy Shield", 3, 20);
        armour.Should().NotBeNull();
        armour!.Fragment.Should().Be("-.*d a.+,");
        Regex.IsMatch(
                "18% increased Armour, Evasion and Energy Shield from Equipped Shield",
                armour.Fragment, RegexOptions.IgnoreCase)
            .Should().BeFalse("same-stat from-Equipped lines are foreign");
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

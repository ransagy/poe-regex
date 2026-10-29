using System.Text.RegularExpressions;
using AwesomeAssertions;
using PoeRegex.Core;
using PoeRegex.Core.Compilation;
using PoeRegex.Core.Data;

namespace PoeRegex.Data.Build.Tests;

public class ExactSolverDifferentialTests
{
    private static readonly Lazy<EngineCorpus> Corpus = new(() =>
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var path = Path.Combine(directory.FullName, "data", "catalogs", "poe2.catalog.json");
            if (File.Exists(path))
                return new EngineCorpus(new FileCatalogSource(path).Load());
            directory = directory.Parent;
        }
        throw new InvalidOperationException("repository root not found");
    });

    [Theory]
    [InlineData(new[] { "ab", "aab" }, new[] { "b", "ba" })]
    [InlineData(new[] { "abc", "abd" }, new[] { "abe", "abf" })]
    [InlineData(new[] { "xabc", "yabc" }, new[] { "xabd", "yabd" })]
    [InlineData(new[] { "abc", "xbc" }, new[] { "ab", "xab" })]
    [InlineData(new[] { "aXb", "aYb" }, new[] { "aZb" })]
    [InlineData(new[] { "a1b", "a2b" }, new[] { "a3b", "x1b" })]
    [InlineData(new[] { "ab" }, new[] { "a", "b", "ab" })]
    public void ExactSolver_MatchesTinyBruteForceReference(
        string[] targets, string[] foreign)
    {
        var scope = MakeScope(targets, foreign);
        var exact = ExactSearchSolver.Solve(scope, "target", 2, 8);
        var reference = BruteForce(targets, foreign, 8);

        if (reference is null)
        {
            exact.Should().BeNull();
            return;
        }

        exact.Should().NotBeNull();
        exact!.Length.Should().Be(reference.Length);
        MatchesAllTargets(exact.Fragment, targets).Should().BeTrue();
        MatchesAnyForeign(exact.Fragment, foreign).Should().BeFalse();
    }

    private static ScopeCorpus MakeScope(string[] targets, string[] foreign)
    {
        var lines = new List<SourceLine>();
        lines.AddRange(targets.Select(x =>
            new SourceLine("target", x)));
        lines.AddRange(foreign.Select(x =>
            new SourceLine("foreign", x)));
        return ScopeCorpus.FromLines(null, lines);
    }

    private static string? BruteForce(
        IReadOnlyList<string> targets,
        IReadOnlyList<string> foreign,
        int maxLength)
    {
        var alphabet = targets.SelectMany(x => x.ToLowerInvariant())
            .Distinct().OrderBy(x => x).ToArray();
        var tokens = alphabet.Select(c => c.ToString())
            .Concat([".", ".*", ".+", @"\d", @"\d+"])
            .ToArray();
        var costs = tokens.Select(t => t.Length).ToArray();
        var anchors = new[] { "", "^", "$", "^$" };
        string? best = null;

        void Consider(List<string> parts)
        {
            var core = string.Concat(parts);
            foreach (var anchor in anchors)
            {
                var fragment = anchor switch
                {
                    "" => core,
                    "^" => "^" + core,
                    "$" => core + "$",
                    _ => "^" + core + "$",
                };
                if (fragment.Length > maxLength) continue;
                if (fragment.Count(char.IsAsciiLetter) < 2) continue;
                if (!MatchesAllTargets(fragment, targets)) continue;
                if (MatchesAnyForeign(fragment, foreign)) continue;
                if (best is null || fragment.Length < best.Length ||
                    (fragment.Length == best.Length &&
                     string.CompareOrdinal(fragment, best) < 0))
                    best = fragment;
            }
        }

        void Rec(List<string> parts, int cost)
        {
            if (cost > maxLength) return;
            if (parts.Count > 0) Consider(parts);
            for (var i = 0; i < tokens.Length; i++)
            {
                if (cost + costs[i] > maxLength) break;
                parts.Add(tokens[i]);
                Rec(parts, cost + costs[i]);
                parts.RemoveAt(parts.Count - 1);
            }
        }

        Rec([], 0);
        return best;
    }

    private static bool MatchesAllTargets(string fragment, IReadOnlyList<string> targets)
    {
        var regex = new Regex(
            fragment, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return targets.All(regex.IsMatch);
    }

    private static bool MatchesAnyForeign(string fragment, IReadOnlyList<string> foreign)
    {
        var regex = new Regex(
            fragment, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return foreign.Any(regex.IsMatch);
    }
}

using System.Text.RegularExpressions;
using AwesomeAssertions;
using PoeRegex.Core;
using PoeRegex.Core.Compilation;
using PoeRegex.Core.Data;

namespace PoeRegex.Core.Tests;

/// <summary>
/// Independent strictness audit for the shipped table. Unlike the selector
/// audit, this does not allow minion/allies soft collisions: every table entry
/// must match all target lines and zero foreign or safety lines in its scope.
/// </summary>
public class StrictTableTests
{
    [Fact]
    public void ShippedTable_IsStrictTargetOnly()
    {
        var corpus = RealCatalog.Corpus();
        var failures = new List<string>();
        var checkedCount = 0;

        CheckScope(corpus, null, corpus.PrecomputedGlobal, failures, ref checkedCount);
        foreach (var cls in corpus.InGameClasses)
        {
            var table = corpus.PrecomputedForClass(cls.Id);
            if (table.Count == 0) continue;
            CheckScope(corpus, cls.Id, table, failures, ref checkedCount);
        }

        failures.Take(30).Should().BeEmpty($"{failures.Count} strict table failures");
        checkedCount.Should().BeGreaterThan(500);
    }

    private static void CheckScope(
        EngineCorpus corpus,
        string? scope,
        IReadOnlyDictionary<string, string> table,
        List<string> failures,
        ref int checkedCount)
    {
        var lines = scope is null
            ? corpus.RealLinesGlobal().ToList()
            : corpus.RealLinesForClass(scope).ToList();
        var safety = corpus.SafetyLines(scope).ToList();

        foreach (var (pattern, fragment) in table)
        {
            var name = $"{scope ?? "global"}/{pattern.Replace('\u0001', '#')}";
            Regex regex;
            try
            {
                regex = new Regex(
                    fragment,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }
            catch (ArgumentException ex)
            {
                failures.Add($"{name}: invalid regex '{fragment}': {ex.Message}");
                continue;
            }

            var targets = lines.Where(x => x.Pattern == pattern).Select(x => x.Real).ToList();
            if (targets.Count == 0)
            {
                failures.Add($"{name}: no target lines");
                continue;
            }
            if (targets.Any(t => !regex.IsMatch(t)))
                failures.Add($"{name}: misses target for '{fragment}'");

            var foreign = lines.Where(x => x.Pattern != pattern)
                .Select(x => x.Real)
                .Concat(safety.Where(x => x.Pattern != pattern).Select(x => x.Raw));
            foreach (var line in foreign)
            {
                if (!regex.IsMatch(line)) continue;
                failures.Add($"{name}: foreign hit '{line}' for '{fragment}'");
                break;
            }
            checkedCount++;
        }
    }
}

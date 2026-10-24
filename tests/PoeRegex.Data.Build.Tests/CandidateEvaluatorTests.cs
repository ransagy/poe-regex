using System.Collections.Concurrent;
using System.Text.Json;
using PoeRegex.Core;
using PoeRegex.Core.Compilation;
using PoeRegex.Core.Data;

namespace PoeRegex.Data.Build.Tests;

public class CandidateEvaluatorTests
{
    [Theory]
    [InlineData("MovementVelocity", "Boots", 24)]
    [InlineData("MovementVelocity", null, 8)]
    [InlineData("IncreasedLife", "Belt", 8)]
    [InlineData("LocalFireDamage", "Bow", 8)]
    [InlineData("FireResistance", "Ring", 8)]
    [InlineData("AdditionalArrowChanceCanExceed100%", "Bow", 8)]
    public void IndexedTop_MatchesExhaustiveProfileData(
        string family, string? scope, int maxLength)
    {
        var (catalog, corpus, table) = Load();
        var scopeCorpus = ScopeCorpus.Build(corpus, scope);
        var all = CandidateReport.ScopeLines(corpus, scope);

        foreach (var pattern in CandidateReport.TargetPatterns(catalog, family, scope)
                     .OrderBy(x => x, StringComparer.Ordinal))
        {
            var targets = scopeCorpus.Targets(pattern);
            if (targets.Count == 0) continue;

            var current = Current(table, scope, pattern);
            var foreign = all.Where(x => x.Pattern != pattern).ToList();
            var reference = CandidateReport.Generate(
                    corpus, targets, foreign, scope, maxLength, current)
                .Where(o => o.Plausible)
                .OrderBy(o => o.Length)
                .ThenBy(o => o.Fragment, StringComparer.Ordinal)
                .ToList();

            var optimized = CandidateEvaluator.EvaluateTop(
                scopeCorpus, pattern, current, maxLength);

            Project(optimized.Line).Should().Be(
                Project(reference.Where(o => o.ForeignLines == 0).Take(3)),
                $"{family}/{scope ?? "global"} line profile");
            Project(optimized.Relaxed).Should().Be(
                Project(reference.Where(o => o.ForeignOtherLines == 0).Take(3)),
                $"{family}/{scope ?? "global"} relaxed profile");

            var floor = reference.FirstOrDefault();
            if (floor is null)
            {
                optimized.Floor.Should().BeNull();
            }
            else
            {
                optimized.Floor.Should().NotBeNull();
                optimized.Floor!.Fragment.Should().Be(floor.Fragment);
                optimized.Floor.ForeignLines.Should().Be(floor.ForeignLines);
                optimized.Floor.ForeignMinionLines.Should().Be(floor.ForeignMinionLines);
            }
        }
    }

    [Theory]
    [InlineData("MovementVelocity", "Boots", 8)]
    [InlineData("IncreasedLife", "Belt", 8)]
    [InlineData("FireResistance", "Ring", 8)]
    public void IndexedTop_StrictAndMinionMatchExhaustiveCross(
        string family, string scope, int maxLength)
    {
        var (catalog, corpus, table) = Load();
        var scopeCorpus = ScopeCorpus.Build(corpus, scope);
        var all = CandidateReport.ScopeLines(corpus, scope);

        foreach (var pattern in CandidateReport.TargetPatterns(catalog, family, scope)
                     .OrderBy(x => x, StringComparer.Ordinal))
        {
            var targets = scopeCorpus.Targets(pattern);
            if (targets.Count == 0) continue;

            var current = Current(table, scope, pattern);
            var foreign = all.Where(x => x.Pattern != pattern).ToList();
            var reference = CandidateReport.Generate(
                    corpus, targets, foreign, scope, maxLength, current)
                .Where(o => o.Plausible)
                .Where(o => o.ForeignLines == 0 || o.ForeignOtherLines == 0)
                .OrderBy(o => o.Length)
                .ThenBy(o => o.Fragment, StringComparer.Ordinal)
                .ToList();
            foreach (var option in reference)
                CandidateReport.EvaluateCross(corpus, scope, option);

            var optimized = CandidateEvaluator.EvaluateTop(
                scopeCorpus, pattern, current, maxLength);

            Project(optimized.Strict).Should().Be(
                Project(reference.Where(o => o.ForeignLines == 0 && !o.HasCross).Take(3)),
                $"{family}/{scope} strict profile");
            Project(optimized.Minion).Should().Be(
                Project(reference.Where(o => o.ForeignOtherLines == 0 && !o.HasCross).Take(3)),
                $"{family}/{scope} minion profile");
        }
    }

    [Fact]
    public void IndexedTop_IsDeterministicUnderParallelUse()
    {
        var (catalog, corpus, table) = Load();
        var scopeCorpus = ScopeCorpus.Build(corpus, "Boots");
        var patterns = CandidateReport.TargetPatterns(catalog, "MovementVelocity", "Boots")
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        var sequential = patterns.ToDictionary(
            pattern => pattern,
            pattern => CandidateEvaluator.EvaluateTop(
                scopeCorpus, pattern, Current(table, "Boots", pattern), 24),
            StringComparer.Ordinal);

        var parallel = new ConcurrentDictionary<string, TopOptions>(StringComparer.Ordinal);
        Parallel.ForEach(patterns, pattern =>
            parallel[pattern] = CandidateEvaluator.EvaluateTop(
                scopeCorpus, pattern, Current(table, "Boots", pattern), 24));

        foreach (var pattern in patterns)
        {
            Project(parallel[pattern].Strict).Should().Be(
                Project(sequential[pattern].Strict), pattern);
            Project(parallel[pattern].Line).Should().Be(
                Project(sequential[pattern].Line), pattern);
            Project(parallel[pattern].Relaxed).Should().Be(
                Project(sequential[pattern].Relaxed), pattern);
        }
    }

    private static string Project(IEnumerable<CandidateReport.Option> options) =>
        JsonSerializer.Serialize(options.Select(o => new
        {
            o.Fragment,
            o.Length,
            o.ForeignPatterns,
            o.ForeignLines,
            o.ForeignMinionLines,
            o.ForeignOtherLines,
        }).ToList());

    private static string Current(FragmentTable? table, string? scope, string pattern)
    {
        if (table is null) return "";
        if (scope is null) return table.Global.GetValueOrDefault(pattern) ?? "";
        return table.ByClass.GetValueOrDefault(scope)?.GetValueOrDefault(pattern) ?? "";
    }

    private static (Catalog Catalog, EngineCorpus Corpus, FragmentTable? Table) Load()
    {
        var root = RepoRoot();
        var catalog = new FileCatalogSource(
            Path.Combine(root, "data", "catalogs", "poe2.catalog.json")).Load(GameTarget.PoE2);
        var tablePath = Path.Combine(root, "data", "catalogs", "poe2.fragments.json");
        FragmentTable? table = File.Exists(tablePath)
            ? JsonSerializer.Deserialize<FragmentTable>(
                File.ReadAllText(tablePath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            : null;
        return (catalog, new EngineCorpus(catalog), table);
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

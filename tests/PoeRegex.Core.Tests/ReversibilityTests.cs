using System.Text.RegularExpressions;
using PoeRegex.Core.Compilation;
using PoeRegex.Core.Data;
using PoeRegex.Core.Explain;
using PoeRegex.Core.Search;

namespace PoeRegex.Core.Tests;

/// <summary>
/// Reversibility guarantee: every generated query must decompose (via the
/// explainer) back into a candidate set that includes every source modifier.
/// Candidate sets are AND-intersections over included phrases under the game's
/// case-insensitive matching, so recall = 1 is the invariant that makes the
/// compiler's fragment selection sound. Uses the real derived catalogs.
/// </summary>
public class ReversibilityTests
{
    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "data", "catalogs")))
        {
            dir = Path.GetDirectoryName(dir);
        }
        return dir ?? throw new DirectoryNotFoundException("repo root not found");
    }

    private static (EngineCorpus Corpus, PoeRegexCompiler Compiler, Explainer Explainer) Create(GameTarget game)
    {
        var path = Path.Combine(RepoRoot(), "data", "catalogs", $"{game.ToRuleKey()}.catalog.json");
        var corpus = new EngineCorpus(new FileCatalogSource(path).Load(game));
        return (corpus, new PoeRegexCompiler(corpus, PoeRegexCompiler.StrategyFor(game)), new Explainer(corpus));
    }

    public static IEnumerable<object[]> Games()
    {
        yield return new object[] { GameTarget.PoE1 };
        yield return new object[] { GameTarget.PoE2 };
    }

    private static List<string> ModIds(ExplainResult explained) => explained.Mods.Select(m => m.Id).ToList();

    /// <summary>
    /// For every (mod, class) pair, a generated single-mod query must reverse to a
    /// candidate set containing that mod. Skips pairs the compiler flags as
    /// unresolvable (their line has no distinguishing copy-text fragment).
    /// </summary>
    [Theory]
    [MemberData(nameof(Games))]
    public void EveryClassModPair_ReversesToSourceMod(GameTarget game)
    {
        var (corpus, compiler, explainer) = Create(game);
        var checkedCount = 0;
        foreach (var mod in corpus.Mods)
        {
            foreach (var cls in mod.Classes)
            {
                var result = compiler.Compile(QuerySpecification.For(game) with
                {
                    ItemClass = cls,
                    IncludedModifiers = [new ModifierRequirement(mod.Id)],
                });
                if (result.Diagnostics.Any(d => d.Code == "unresolved-modifier")) continue;

                var explained = explainer.Explain(result.Query, cls);
                ModIds(explained).Should().Contain(
                    mod.Id,
                    $"query '{result.Query}' for {mod.Id} on {cls} must reverse to a candidate set including the source mod");
                checkedCount++;
            }
        }
        checkedCount.Should().BeGreaterThan(4000, "expected a meaningful number of pairs to reverse");
    }

    /// <summary>
    /// A sample of global (no-class) queries must reverse to a candidate set that
    /// includes the source mod across the union corpus.
    /// </summary>
    [Theory]
    [MemberData(nameof(Games))]
    public void GlobalQueries_ReverseToSourceMod(GameTarget game)
    {
        var (corpus, compiler, explainer) = Create(game);
        var mods = corpus.Mods.Where(m => m.StatLines.Count > 0).ToList();
        var step = Math.Max(1, mods.Count / 60);
        var checkedCount = 0;
        for (var i = 0; i < mods.Count; i += step)
        {
            var mod = mods[i];
            var result = compiler.Compile(QuerySpecification.For(game) with
            {
                IncludedModifiers = [new ModifierRequirement(mod.Id)],
            });
            if (result.Diagnostics.Any(d => d.Code == "unresolved-modifier")) continue;

            var explained = explainer.Explain(result.Query);
            ModIds(explained).Should().Contain(
                mod.Id,
                $"global query '{result.Query}' for {mod.Id} must reverse to a candidate set including the source mod");
            checkedCount++;
        }
        checkedCount.Should().BeGreaterThan(20, "expected a meaningful number of global queries to reverse");
    }

    /// <summary>
    /// For every class with at least three mods, a three-mod generated query must
    /// reverse to a candidate set containing all three source mods (factored
    /// alternation and AND-intersection must not drop any source).
    /// </summary>
    [Theory]
    [MemberData(nameof(Games))]
    public void SeededMultiMod_AllSourcesReverse(GameTarget game)
    {
        var (corpus, compiler, explainer) = Create(game);
        var groups = corpus.Mods
            .Select(m => m.Classes.FirstOrDefault())
            .Where(c => c is not null)
            .Distinct()
            .ToList();
        var checkedCount = 0;
        foreach (var cls in groups)
        {
            var candidates = corpus.Mods.Where(m => m.Classes.Contains(cls)).Take(3).ToList();
            if (candidates.Count < 3) continue;

            var result = compiler.Compile(QuerySpecification.For(game) with
            {
                ItemClass = cls,
                IncludedModifiers = candidates.Select(m => new ModifierRequirement(m.Id)).ToList(),
            });
            var explained = explainer.Explain(result.Query, cls);
            var ids = ModIds(explained);
            foreach (var mod in candidates)
            {
                ids.Should().Contain(mod.Id, $"multi-mod query '{result.Query}' on {cls} must reverse to include {mod.Id}");
            }
            checkedCount++;
        }
        checkedCount.Should().BeGreaterThan(10, "expected a meaningful number of multi-mod queries to reverse");
    }

    /// <summary>
    /// Threshold (value >= min) queries must reverse to a candidate set that still
    /// includes the source mod; the digit-range pattern matches at least one real
    /// copy-text roll of the source line.
    /// </summary>
    [Theory]
    [MemberData(nameof(Games))]
    public void Threshold_ReversesToSourceMod(GameTarget game)
    {
        var (corpus, compiler, explainer) = Create(game);
        var mods = corpus.Mods
            .Where(m => m.StatLines.Any(sl => sl.Pattern.Contains((char)SuffixIndex.DigitPlaceholder) && sl.MinValue > 0))
            .ToList();
        var step = Math.Max(1, mods.Count / 50);
        var checkedCount = 0;
        for (var i = 0; i < mods.Count; i += step)
        {
            var mod = mods[i];
            var min = (int)Math.Ceiling(mod.StatLines[0].MinValue);
            var result = compiler.Compile(QuerySpecification.For(game) with
            {
                ItemClass = mod.Classes[0],
                IncludedModifiers = [new ModifierRequirement(mod.Id, min)],
            });
            if (result.Diagnostics.Any(d => d.Code == "unresolved-modifier")) continue;

            var explained = explainer.Explain(result.Query, mod.Classes[0]);
            ModIds(explained).Should().Contain(
                mod.Id,
                $"threshold query '{result.Query}' for {mod.Id} must reverse to a candidate set including the source mod");
            checkedCount++;
        }
        checkedCount.Should().BeGreaterThan(20, "expected a meaningful number of threshold queries to reverse");
    }

    /// <summary>
    /// Exclusion queries must keep the source mod in the candidate set, and must
    /// drop the excluded mod when the emitted exclusion term actually matches the
    /// excluded mod's copy text.
    /// </summary>
    [Theory]
    [MemberData(nameof(Games))]
    public void Exclusions_KeepSourceAndDropExcluded(GameTarget game)
    {
        var (corpus, compiler, explainer) = Create(game);
        var mods = corpus.Mods.Where(m => m.Classes.Count > 0).ToList();
        var step = Math.Max(1, mods.Count / 60);
        var checkedCount = 0;
        for (var i = 0; i < mods.Count; i += step)
        {
            var source = mods[i];
            var cls = source.Classes[0];
            var excluded = mods.FirstOrDefault(m => m.Id != source.Id && m.Classes.Contains(cls));
            if (excluded is null) continue;

            // Skip degenerate pairs whose copy text is identical: an exclusion then
            // removes the source text too, which no query can express.
            var sharesPattern = source.StatLines.Any(a => excluded.StatLines.Any(b => b.Pattern == a.Pattern));
            if (sharesPattern) continue;

            var result = compiler.Compile(QuerySpecification.For(game) with
            {
                ItemClass = cls,
                IncludedModifiers = [new ModifierRequirement(source.Id)],
                ExcludedModifiers = [new ModifierRequirement(excluded.Id)],
            });
            if (result.Diagnostics.Any(d => d.Code == "unresolved-modifier")) continue;
            var explained = explainer.Explain(result.Query, cls);
            var ids = ModIds(explained);
            ids.Should().Contain(source.Id, $"exclusion query '{result.Query}' must keep the source mod {source.Id}");

            var m = Regex.Match(result.Query, @"!""([^""]*)""");
            if (m.Success)
            {
                var term = m.Groups[1].Value;
                var termMatchesExcluded = excluded.StatLines.Any(sl =>
                    Regex.IsMatch(sl.Line, term, RegexOptions.IgnoreCase));
                if (termMatchesExcluded)
                {
                    ids.Should().NotContain(excluded.Id, $"exclusion '{term}' in '{result.Query}' must drop {excluded.Id}");
                }
            }
            checkedCount++;
        }
        checkedCount.Should().BeGreaterThan(20, "expected a meaningful number of exclusion queries to reverse");
    }
}

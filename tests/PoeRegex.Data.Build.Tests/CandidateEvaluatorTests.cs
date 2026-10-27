using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
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

            Project(Top(reference, o => o.ForeignLines == 0)).Should().Be(
                Project(optimized.Strict),
                $"{family}/{scope ?? "global"} strict profile");
            Project(Top(reference, o => o.ForeignOtherLines == 0)).Should().Be(
                Project(optimized.Minion),
                $"{family}/{scope ?? "global"} minion profile");

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

    [Fact]
    public void NewGrammar_ReachesKnownMaximumLifeCandidate()
    {
        var (catalog, corpus, table) = Load();
        var scopeCorpus = ScopeCorpus.Build(corpus, null);
        const string pattern = "+\u0001 to maximum Life";
        var current = Current(table, null, pattern);
        var targets = scopeCorpus.Targets(pattern);
        var foreign = CandidateReport.ScopeLines(corpus, null)
            .Where(x => x.Pattern != pattern)
            .ToList();

        var options = CandidateReport.Generate(
                corpus, targets, foreign, null, 24, current)
            .Where(o => o.Plausible)
            .ToList();

        var known = options.SingleOrDefault(o =>
            string.Equals(o.Fragment, @"\+\d.+m life", StringComparison.OrdinalIgnoreCase));
        known.Should().NotBeNull("the digit-guarded known form must be generated");
        known!.ForeignLines.Should().Be(0, "it must be line-local-clean on the modeled corpus");

        var optimized = CandidateEvaluator.EvaluateTop(scopeCorpus, pattern, current, 24);
        optimized.Strict.Should().NotBeEmpty();
        optimized.Strict[0].Length.Should().BeLessThanOrEqualTo(12,
            "the new grammar must reach at least the known 12-character form");
    }

    [Fact]
    public void QuotedTerms_MatchTheThreeAmuletFixtures()
    {
        var agony = new[]
        {
            "+30(20-30) to maximum Energy Shield",
            "42(39-44)% increased Evasion Rating",
            "+65(60-69) to maximum Life",
            "22(22-24)% of Damage taken Recouped as Life",
            "+32(31-33) to Dexterity",
            "+25(24-27)% to Chaos Resistance",
        };
        var torment = new[]
        {
            "21(20-30)% increased Mana Regeneration Rate",
            "8(7-8)% increased maximum Life",
            "22(21-26)% increased Evasion Rating",
            "+16(15-24) to maximum Mana",
            "+12(9-12) to Strength",
            "59(50-59)% increased Mana Regeneration Rate",
            "+15(15-16)% to all Elemental Resistances",
        };
        var gale = new[]
        {
            "+12(10-15) to Strength",
            "26(21-26)% increased maximum Energy Shield",
            "+30(26-30)% to Fire Resistance",
            "24(22-24)% of Damage taken Recouped as Life",
            "+35(31-35)% to Cold Resistance",
        };

        const string known = @"\+\d.+m life";
        Quoted(known, agony).Should().BeTrue();
        Quoted(known, torment).Should().BeFalse();
        Quoted(known, gale).Should().BeFalse();

        // The 9-character form the new grammar actually prefers.
        const string shorter = @"\+\d.*ife";
        Quoted(shorter, agony).Should().BeTrue();
        Quoted(shorter, torment).Should().BeFalse();
        Quoted(shorter, gale).Should().BeFalse();

        // Unquoted, the space splits the pattern into two ANDed terms. Torment
        // Collar supplies "m" via "maximum Life" and "life" via the same line
        // family, but the +digit term has to come from another line.
        Unquoted(known, agony).Should().BeTrue();
        Unquoted(known, torment).Should().BeTrue();
        Unquoted(known, gale).Should().BeFalse(
            "Gale has no +digit line containing an m; it is not a term-split match for this pattern");

        // No space, so unquoted behaves like the quoted single term.
        Unquoted(shorter, agony).Should().BeTrue();
        Unquoted(shorter, torment).Should().BeFalse();
        Unquoted(shorter, gale).Should().BeFalse();

        // Direct token-splitting control: both bad amulets have "maximum" and
        // "Strength" on different lines.
        Quoted("maximum Strength", torment).Should().BeFalse();
        Quoted("maximum Strength", gale).Should().BeFalse();
        Unquoted("maximum Strength", torment).Should().BeTrue();
        Unquoted("maximum Strength", gale).Should().BeTrue();
    }

    [Fact]
    public void Selector_ReachesShortQuotedForms()
    {
        var (catalog, corpus, _) = Load();
        var selector = new FragmentSelector(
            corpus, PoeRegexCompiler.StrategyFor(GameTarget.PoE2),
            usePrecomputed: false, policy: FragmentPolicy.Shortest);

        var life = selector.SelectWithMinionFallback("+\u0001 to maximum Life", null);
        life.Should().NotBeNull();
        life!.Length.Should().BeLessThanOrEqualTo(9, $"got {life}");

        var movement = selector.SelectWithMinionFallback(
            "\u0001% increased Movement Speed", null);
        movement.Should().NotBeNull();
        movement!.Length.Should().BeLessThanOrEqualTo(12, $"got {movement}");

        var attack = selector.SelectWithMinionFallback(
            "\u0001% increased Attack Speed", null);
        attack.Should().NotBeNull();
        attack!.Length.Should().BeLessThanOrEqualTo(12, $"got {attack}");

        var classLife = selector.SelectWithMinionFallback(
            "+\u0001 to maximum Life", "Amulet");
        classLife.Should().NotBeNull();
        classLife!.Length.Should().BeLessThanOrEqualTo(7, $"got {classLife}");
    }

    [Fact]
    public void AnchoredDigitCandidate_CountsForeignLines()
    {
        var (catalog, corpus, _) = Load();
        var scope = ScopeCorpus.Build(corpus, "Shield");
        const string pattern = "\u0001% increased Energy Shield";

        var option = CandidateEvaluator.EvaluateFragment(scope, pattern, @"^\d.*eld");

        option.Should().NotBeNull();
        option!.ForeignOtherLines.Should().BeGreaterThan(0,
            "the anchor belongs to the regex, not to the first literal piece");
    }

    [Fact]
    public void NewGrammar_TopStrictCandidateIsSafeOnAmulets()
    {
        var (catalog, corpus, table) = Load();
        var scopeCorpus = ScopeCorpus.Build(corpus, null);
        const string pattern = "+\u0001 to maximum Life";
        var current = Current(table, null, pattern);
        var optimized = CandidateEvaluator.EvaluateTop(scopeCorpus, pattern, current, 24);

        var best = optimized.Strict[0].Fragment;
        var agony = new[] { "+65(60-69) to maximum Life" };
        var torment = new[] { "8(7-8)% increased maximum Life", "+12(9-12) to Strength" };
        var gale = new[] { "+12(10-15) to Strength", "24(22-24)% of Damage taken Recouped as Life" };

        Quoted(best, agony).Should().BeTrue($"{best} must match the real flat-life item");
        Quoted(best, torment).Should().BeFalse($"{best} must not match Str + % max life");
        Quoted(best, gale).Should().BeFalse($"{best} must not match Str + Recouped as Life");
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
            Project(parallel[pattern].Minion).Should().Be(
                Project(sequential[pattern].Minion), pattern);
        }
    }

    private static List<CandidateReport.Option> Top(
        IEnumerable<CandidateReport.Option> options,
        Func<CandidateReport.Option, bool> predicate,
        int count = 3)
    {
        var groups = new Dictionary<string, CandidateReport.Option>(StringComparer.Ordinal);
        foreach (var option in options.Where(predicate)
                     .Where(o => !o.Fragment.Contains(@"\d", StringComparison.Ordinal) ||
                                 o.GuardLetters >= 3)
                     .OrderBy(o => o.Length)
                     .ThenBy(o => o.Fragment, StringComparer.Ordinal))
        {
            var key = option.Fragment.StartsWith('^') || option.Fragment.EndsWith('$')
                ? "anchored"
                : "free";
            if (!groups.TryGetValue(key, out var existing) || Better(option, existing))
                groups[key] = option;
        }
        return groups.Values
            .OrderBy(o => o.Length)
            .ThenBy(o => o.Fragment, StringComparer.Ordinal)
            .Take(count)
            .ToList();
    }

    private static bool Better(
        CandidateReport.Option a, CandidateReport.Option b)
    {
        if (a.Length != b.Length) return a.Length < b.Length;
        if (a.ForeignOtherLines != b.ForeignOtherLines)
            return a.ForeignOtherLines < b.ForeignOtherLines;
        if (a.ForeignMinionLines != b.ForeignMinionLines)
            return a.ForeignMinionLines < b.ForeignMinionLines;
        if (a.TermSplits != b.TermSplits) return !a.TermSplits;
        if (a.GuardLetters != b.GuardLetters) return a.GuardLetters > b.GuardLetters;
        return StringComparer.Ordinal.Compare(a.Fragment, b.Fragment) < 0;
    }

    private static bool Quoted(string fragment, IEnumerable<string> lines) =>
        lines.Any(line => Regex.IsMatch(
            line, fragment, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));

    private static bool Unquoted(string fragment, IEnumerable<string> lines)
    {
        var terms = fragment.Split(
            [' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return terms.All(term => lines.Any(line => Regex.IsMatch(
            line, term, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)));
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
            o.TermSplits,
            o.MatchSetHash,
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

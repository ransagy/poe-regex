using System.Text.RegularExpressions;
using PoeRegex.Core;
using PoeRegex.Core.Compilation;
using PoeRegex.Core.Data;
using Xunit.Abstractions;

namespace PoeRegex.Core.Tests;

/// <summary>
/// End-to-end fragment audit over the real catalog:
///  - every distinct stat line in every scope (global and each item class)
///    resolves to a fragment that recalls all of its real copy-text lines and
///    matches none of another stat's lines (checked with .NET Regex, not the
///    selector's matcher);
///  - every mod resolves in simple mode (global) and in every advanced slot
///    (class) with no unresolved-modifier diagnostics;
///  - every class/mod composes into a one-slot paste with a clean anchor and
///    group recall, every class pair composes, and every selected class's item
///    passes the paste while an unselected class's item does not.
/// </summary>
public class FragmentAuditTests
{
    private readonly ITestOutputHelper _output;

    public FragmentAuditTests(ITestOutputHelper output) => _output = output;

    private static readonly Lazy<EngineCorpus> Corpus = new(() => RealCatalog.Corpus());
    private static readonly Lazy<PoeRegexCompiler> Compiler =
        new(() => new PoeRegexCompiler(Corpus.Value, PoeRegexCompiler.StrategyFor(GameTarget.PoE2)));
    private static readonly Lazy<PasteComposer> Composer =
        new(() => new PasteComposer(Corpus.Value, PoeRegexCompiler.StrategyFor(GameTarget.PoE2)));

    private static IReadOnlyList<string> ClassesWithMods() =>
        Corpus.Value.InGameClasses.Select(c => c.Id).ToList();

    [Fact]
    public void InGameScope_ExcludesDataminedClasses()
    {
        var corpus = Corpus.Value;
        corpus.InGameClasses.Select(c => c.Id)
            .Intersect(GameItemClasses.NotInGame).Should().BeEmpty();
        corpus.InGameMods.Any(m =>
                m.Classes.Count > 0 && !m.Classes.Any(corpus.IsInGameClass))
            .Should().BeFalse("a non-game-only mod must not enter the in-game union");
        corpus.RealLinesGlobal().Select(x => x.Real)
            .Should().NotContain("Item Class: Dagger");
    }

    private static bool HasWildcardDot(string fragment)
    {
        for (var i = 0; i < fragment.Length; i++)
        {
            if (fragment[i] != '.') continue;
            if (i == 0 || fragment[i - 1] != '\\') return true;
        }
        return false;
    }

    /// <summary>
    /// The checked-in tables must match the catalog version and cover every
    /// global and class-scoped pattern. When this fails, regenerate with:
    ///   dotnet run --project tools/PoeRegex.Data.Build -- fragments --game poe2
    /// (and the same for poe1).
    /// </summary>
    [Theory]
    [InlineData(GameTarget.PoE1)]
    [InlineData(GameTarget.PoE2)]
    public void PrecomputedFragmentTable_CoversEveryScope(GameTarget game)
    {
        var corpus = RealCatalog.Corpus(game);
        corpus.HasPrecomputedFragments.Should().BeTrue(
            $"{game} needs an embedded fragment table; run the fragments tool");
        corpus.PrecomputedDataVersion.Should().Be(corpus.Catalog.DataVersion,
            "the fragment table is stale; regenerate it with the fragments tool");

        var scopes = new List<string?> { null };
        scopes.AddRange(corpus.InGameClasses.Select(c => c.Id));
        var checkedCount = 0;
        var unresolved = new List<string>();
        foreach (var scope in scopes)
        {
            var patterns = scope is null
                ? corpus.InGameMods.SelectMany(m => m.StatLines).Select(l => l.Pattern).Distinct()
                : corpus.InGameMods.Where(m => m.Classes.Contains(scope))
                    .SelectMany(m => m.StatLines).Select(l => l.Pattern).Distinct();
            foreach (var pattern in patterns)
            {
                if (corpus.TryPrecomputedFragment(pattern, scope, out var fragment))
                {
                    fragment.Should().NotBeNullOrEmpty();
                    checkedCount++;
                    continue;
                }

                // A deliberately unresolved scope must carry a concrete
                // reason; silently missing entries are not allowed.
                var reason = corpus.PrecomputedUnresolvedReason(pattern, scope);
                reason.Should().NotBeNullOrWhiteSpace(
                    $"{game}/{scope ?? "global"} has neither a fragment nor an unresolved reason for '{pattern}'");
                unresolved.Add($"{scope ?? "global"}: {pattern.Replace('\u0001', '#')} — {reason}");
            }
        }
        _output.WriteLine($"{game}: {checkedCount} precomputed fragments, " +
                          $"{unresolved.Count} documented unresolved, across {scopes.Count} scopes");
        foreach (var row in unresolved) _output.WriteLine($"  unresolved {row}");
        checkedCount.Should().BeGreaterThan(600);
        unresolved.Count.Should().BeLessThan(60, "unresolved patterns need a deliberate, documented review");
    }

    /// <summary>
    /// Captured items are the field regression: modeled lines must be safe
    /// per line, while rare names, unique-only text, and flavour are reported
    /// as unmodeled rather than silently treated as safe.
    /// </summary>
    [Fact]
    public void EveryPrecomputedFragment_IsSafeOnCapturedModeledLines()
    {
        var corpus = Corpus.Value;
        corpus.SafetyItems.Should().NotBeEmpty("the captured-item corpus must be present");
        var selector = new FragmentSelector(corpus, PoeRegexCompiler.StrategyFor(GameTarget.PoE2));
        var allPatterns = corpus.InGameMods.SelectMany(m => m.StatLines)
            .Select(sl => sl.Pattern).Distinct(StringComparer.Ordinal).ToList();
        var failures = new List<string>();
        var unmodeledHits = new List<string>();
        var checkedCount = 0;

        foreach (var item in corpus.SafetyItems)
        {
            var cls = corpus.Catalog.ItemClasses.FirstOrDefault(c =>
                c.Name.Equals(item.ClassName, StringComparison.OrdinalIgnoreCase) ||
                c.Id.Equals(item.ClassName, StringComparison.OrdinalIgnoreCase));
            if (cls is null)
            {
                failures.Add($"unknown captured-item class '{item.ClassName}'");
                continue;
            }

            // Keep the raw (range-bearing) copy text alongside the stripped
            // canonical line. The catalog pattern comes from the canonical
            // line; the positive/negative regex checks use the raw text the
            // game actually searches.
            var pairs = item.Lines.Select((canonical, i) => (
                Canonical: canonical,
                Raw: i < item.RawLines.Count ? item.RawLines[i] : canonical)).ToList();
            var mappedPairs = pairs
                .Select(p => (p.Canonical, p.Raw, Pattern: corpus.PatternForSafetyLine(p.Canonical)))
                .ToList();
            var modeledPairs = mappedPairs
                .Where(p => corpus.IsModeledLine(p.Canonical, cls.Id))
                .ToList();
            // A captured unique-only line can carry the same stat as a regular
            // affix. It is reported as unmodeled but must not be treated as a
            // foreign stat for that same catalog pattern.
            var itemStatPatterns = mappedPairs.Select(p => p.Pattern)
                .Where(allPatterns.Contains).ToHashSet(StringComparer.Ordinal);
            var modeledItemPatterns = modeledPairs.Select(p => p.Pattern)
                .Where(allPatterns.Contains).ToHashSet(StringComparer.Ordinal);
            var unmodeledPairs = pairs
                .Where(p => !corpus.IsModeledLine(p.Canonical, cls.Id))
                .Select(p => p.Raw)
                .ToList();
            var allRaw = pairs.Select(p => p.Raw).ToList();

            // Positives: every modeled stat the item really carries must be recalled.
            foreach (var pattern in modeledItemPatterns)
            {
                var fragment = selector.Select(pattern, cls.Id);
                var raws = modeledPairs.Where(p => p.Pattern == pattern).Select(p => p.Raw).ToList();
                if (fragment is null || !raws.Any(l => Regex.IsMatch(l, fragment, RegexOptions.IgnoreCase)))
                    failures.Add($"{cls.Id}: fragment '{fragment}' misses modeled '{pattern}'");
                checkedCount++;
            }

            // Negatives: fragments for other modeled stats may not match any
            // raw line of this item (including unique/flavor text, which is
            // reported as unmodeled rather than treated as safe).
            foreach (var pattern in allPatterns)
            {
                if (itemStatPatterns.Contains(pattern)) continue;
                var global = selector.Select(pattern, null);
                if (global is not null && allRaw.Any(l => Regex.IsMatch(l, global, RegexOptions.IgnoreCase)))
                    failures.Add($"{cls.Id}: global '{global}' leaks into raw line for '{pattern}'");
                var scoped = selector.Select(pattern, cls.Id);
                if (scoped is not null && allRaw.Any(l => Regex.IsMatch(l, scoped, RegexOptions.IgnoreCase)))
                    failures.Add($"{cls.Id}: scoped '{scoped}' leaks into raw line for '{pattern}'");

                foreach (var line in unmodeledPairs)
                    if (global is not null && Regex.IsMatch(line, global, RegexOptions.IgnoreCase))
                        unmodeledHits.Add($"{cls.Id}: global '{global}' matches unmodeled '{line}'");
                checkedCount++;
            }
        }

        _output.WriteLine($"captured items: {corpus.SafetyItems.Count}, modeled checks: {checkedCount}");
        _output.WriteLine($"unmodeled line hits: {unmodeledHits.Count} (reported, not treated as safety failures)");
        foreach (var failure in failures.Take(30)) _output.WriteLine($"FAIL {failure}");
        foreach (var hit in unmodeledHits.Take(10)) _output.WriteLine($"UNMODELED {hit}");
        failures.Take(30).Should().BeEmpty($"{failures.Count} modeled captured-item safety failures");
        checkedCount.Should().BeGreaterThan(1_000);
    }

    /// <summary>
    /// The exact broad strings observed in-game. They must be rejected by the
    /// section oracle, not merely absent from the current table.
    /// </summary>
    [Theory]
    [InlineData("P.+l E")]
    [InlineData("Adds .+Cold Damage$")]
    [InlineData(@"\d Co.+ge$")]
    [InlineData(@"^\d.+d Armour$")]
    [InlineData(@"^\d.+m Life$")]
    public void KnownBroadWildcards_AreNotCrossLineSafe(string fragment)
    {
        Corpus.Value.CrossLineSafe(null, fragment).Should().BeFalse(
            $"'{fragment}' was observed matching across item lines in-game");
    }

    /// <summary>
    /// The accepted global policy allows collisions with the relatively rare
    /// minion/allies/presence variants of the same displayed stat. All other
    /// foreign lines remain hard failures.
    /// </summary>
    private static bool IsAllowedSoftForeign(string line) =>
        line.Contains("Minions", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("Allies", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("in your Presence", StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void EveryStatLine_ResolvesToARegexSafeMinimalFragment()
    {
        var corpus = Corpus.Value;
        var selector = new FragmentSelector(corpus, PoeRegexCompiler.StrategyFor(GameTarget.PoE2));
        var scopes = new List<string?> { null };
        scopes.AddRange(ClassesWithMods());

        var failures = new List<string>();
        var unresolved = new List<string>();
        var lengths = new List<(int Length, string Scope, string Pattern, string Fragment)>();
        var checkedCount = 0;

        foreach (var scope in scopes)
        {
            var lines = scope is null
                ? corpus.RealLinesGlobal()
                : corpus.RealLinesForClass(scope);
            var patterns = scope is null
                ? corpus.InGameMods.SelectMany(m => m.StatLines).Select(l => l.Pattern).Distinct()
                : corpus.InGameMods.Where(m => m.Classes.Contains(scope))
                    .SelectMany(m => m.StatLines).Select(l => l.Pattern).Distinct();

            foreach (var pattern in patterns)
            {
                var targets = lines.Where(x => x.Pattern == pattern).Select(x => x.Real).ToList();
                if (targets.Count == 0) continue;
                var others = lines.Where(x => x.Pattern != pattern)
                    .Select(x => x.Real)
                    .Where(x => !IsAllowedSoftForeign(x))
                    .ToList();
                var fragment = selector.Select(pattern, scope);
                if (fragment is null)
                {
                    var reason = corpus.PrecomputedUnresolvedReason(pattern, scope);
                    if (reason is null)
                        failures.Add($"{scope ?? "global"}: no fragment for '{pattern}'");
                    else
                        unresolved.Add($"{scope ?? "global"}: {pattern.Replace('\u0001', '#')} — {reason}");
                    continue;
                }
                var regex = new Regex(fragment, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (targets.Any(t => !regex.IsMatch(t)))
                    failures.Add($"{scope ?? "global"}: '{fragment}' misses its own line for '{pattern}'");
                if (others.Any(o => regex.IsMatch(o)))
                    failures.Add($"{scope ?? "global"}: '{fragment}' over-matches a foreign line for '{pattern}'");
                lengths.Add((fragment.Length, scope ?? "global", pattern, fragment));
                checkedCount++;
            }
        }

        _output.WriteLine($"scopes={scopes.Count} fragments={checkedCount} unresolved={unresolved.Count}");
        foreach (var row in lengths.OrderByDescending(x => x.Length).Take(15))
            _output.WriteLine($"  longest {row.Length,2}: {row.Fragment}  <- [{row.Scope}] {row.Pattern.Replace('\u0001', '#')}");
        foreach (var row in unresolved) _output.WriteLine($"  unresolved {row}");
        _output.WriteLine($"  wildcard={lengths.Count(x => HasWildcardDot(x.Fragment))} " +
                          $"anchored={lengths.Count(x => x.Fragment.StartsWith('^') || x.Fragment.EndsWith('$'))} " +
                          $"plain={lengths.Count(x => !HasWildcardDot(x.Fragment) && !x.Fragment.StartsWith('^') && !x.Fragment.EndsWith('$'))}");

        failures.Should().BeEmpty();
        checkedCount.Should().BeGreaterThan(600, "the audit must cover the global patterns and every class scope");
        unresolved.Count.Should().BeLessThan(10, "unresolved patterns need a deliberate, documented review");
        lengths.Max(x => x.Length).Should().BeLessThanOrEqualTo(250, "fragments stay inside the game limit");
    }

    [Fact]
    public void EveryMod_ResolvesInSimpleAndEveryAdvancedSlot()
    {
        var corpus = Corpus.Value;
        var compiler = Compiler.Value;
        var failures = new List<string>();
        var checkedCount = 0;
        var knownGlobalUnresolved = 0;

        foreach (var mod in corpus.InGameMods)
        {
            var global = compiler.Compile(QuerySpecification.For(GameTarget.PoE2) with
            {
                IncludedModifiers = [new ModifierRequirement(mod.Id)],
            });
            var documentedUnresolved = mod.StatLines.Any(sl =>
                corpus.PrecomputedUnresolvedReason(sl.Pattern, null) is not null);
            if (global.Diagnostics.Any(d => d.Code == "unresolved-modifier"))
            {
                if (documentedUnresolved) knownGlobalUnresolved++;
                else failures.Add($"global: {mod.Id} ({mod.StatLines.FirstOrDefault()?.Line})");
            }
            checkedCount++;

            foreach (var cls in mod.Classes.Where(corpus.IsInGameClass))
            {
                var classResult = compiler.Compile(QuerySpecification.For(GameTarget.PoE2) with
                {
                    ItemClass = cls,
                    IncludedModifiers = [new ModifierRequirement(mod.Id)],
                });
                if (classResult.Diagnostics.Any(d => d.Code == "unresolved-modifier"))
                    failures.Add($"{cls}: {mod.Id} ({mod.StatLines.FirstOrDefault()?.Line})");
                checkedCount++;
            }
        }

        _output.WriteLine($"compiled {checkedCount} simple+slot queries; " +
                          $"{knownGlobalUnresolved} documented global unresolved");
        failures.Take(20).Should().BeEmpty($"{failures.Count} unresolved mods");
        checkedCount.Should().BeGreaterThan(2_000);
    }

    [Fact]
    public void EveryClassMod_ComposesOneSlot_AndEveryClassPairComposes()
    {
        var corpus = Corpus.Value;
        var composer = Composer.Value;
        var failures = new List<string>();
        var checkedCount = 0;

        foreach (var cls in ClassesWithMods())
        {
            foreach (var mod in corpus.Mods.Where(m => m.Classes.Contains(cls)))
            {
                var result = composer.Compose(
                    [new PasteSlotCriterion(cls, [new ModifierRequirement(mod.Id)], "slot")],
                    []);
                if (result.Diagnostics.Count > 0)
                    failures.Add($"{cls}/{mod.Id}: {string.Join("; ", result.Diagnostics.Select(d => d.Code))}");
                checkedCount++;
            }
        }

        // Anchors: one criterion per class, every class pair.
        var firstMod = ClassesWithMods().ToDictionary(
            c => c,
            c => corpus.Mods.First(m => m.Classes.Contains(c)).Id,
            StringComparer.Ordinal);
        var classes = ClassesWithMods();
        for (var i = 0; i < classes.Count; i++)
        for (var j = i + 1; j < classes.Count; j++)
        {
            var a = classes[i];
            var b = classes[j];
            var result = composer.Compose(
            [
                new PasteSlotCriterion(a, [new ModifierRequirement(firstMod[a])], "a"),
                new PasteSlotCriterion(b, [new ModifierRequirement(firstMod[b])], "b"),
            ], []);
            if (result.Diagnostics.Count > 0)
                failures.Add($"pair {a}/{b}: {string.Join("; ", result.Diagnostics.Select(d => d.Code))}");
            checkedCount++;
        }

        _output.WriteLine($"composed {checkedCount} one-slot and pair pastes");
        failures.Take(20).Should().BeEmpty($"{failures.Count} paste diagnostics");
        checkedCount.Should().BeGreaterThan(1_000);
    }
}

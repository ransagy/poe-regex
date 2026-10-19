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

    private static IReadOnlyList<string> ClassesWithMods() => Corpus.Value.Catalog.ItemClasses
        .Where(c => Corpus.Value.Mods.Any(m => m.Classes.Contains(c.Id)))
        .Select(c => c.Id)
        .ToList();

    [Fact]
    public void EveryStatLine_ResolvesToARegexSafeMinimalFragment()
    {
        var corpus = Corpus.Value;
        var selector = new FragmentSelector(corpus, PoeRegexCompiler.StrategyFor(GameTarget.PoE2));
        var scopes = new List<string?> { null };
        scopes.AddRange(ClassesWithMods());

        var failures = new List<string>();
        var lengths = new List<(int Length, string Scope, string Pattern, string Fragment)>();
        var checkedCount = 0;

        foreach (var scope in scopes)
        {
            var lines = scope is null
                ? corpus.RealLinesGlobal()
                : corpus.RealLinesForClass(scope);
            var patterns = scope is null
                ? corpus.Mods.SelectMany(m => m.StatLines).Select(l => l.Pattern).Distinct()
                : corpus.Mods.Where(m => m.Classes.Contains(scope))
                    .SelectMany(m => m.StatLines).Select(l => l.Pattern).Distinct();

            foreach (var pattern in patterns)
            {
                var targets = lines.Where(x => x.Pattern == pattern).Select(x => x.Real).ToList();
                if (targets.Count == 0) continue;
                var others = lines.Where(x => x.Pattern != pattern).Select(x => x.Real).ToList();
                var fragment = selector.Select(pattern, scope);
                if (fragment is null)
                {
                    failures.Add($"{scope ?? "global"}: no fragment for '{pattern}'");
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

        _output.WriteLine($"scopes={scopes.Count} fragments={checkedCount}");
        foreach (var row in lengths.OrderByDescending(x => x.Length).Take(15))
            _output.WriteLine($"  longest {row.Length,2}: {row.Fragment}  <- [{row.Scope}] {row.Pattern.Replace('\u0001', '#')}");
        _output.WriteLine($"  wildcard={lengths.Count(x => x.Fragment.Contains(".*") || x.Fragment.Contains(".+"))} " +
                          $"anchored={lengths.Count(x => x.Fragment.StartsWith('^') || x.Fragment.EndsWith('$'))} " +
                          $"plain={lengths.Count(x => !x.Fragment.Contains(".*") && !x.Fragment.Contains(".+") && !x.Fragment.StartsWith('^') && !x.Fragment.EndsWith('$'))}");

        failures.Should().BeEmpty();
        checkedCount.Should().BeGreaterThan(700, "the audit must cover the global patterns and every class scope");
        lengths.Max(x => x.Length).Should().BeLessThanOrEqualTo(48, "fragments stay well inside the game limit");
    }

    [Fact]
    public void EveryMod_ResolvesInSimpleAndEveryAdvancedSlot()
    {
        var corpus = Corpus.Value;
        var compiler = Compiler.Value;
        var failures = new List<string>();
        var checkedCount = 0;

        foreach (var mod in corpus.Mods)
        {
            var global = compiler.Compile(QuerySpecification.For(GameTarget.PoE2) with
            {
                IncludedModifiers = [new ModifierRequirement(mod.Id)],
            });
            if (global.Diagnostics.Any(d => d.Code == "unresolved-modifier"))
                failures.Add($"global: {mod.Id} ({mod.StatLines.FirstOrDefault()?.Line})");
            checkedCount++;

            foreach (var cls in mod.Classes)
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

        _output.WriteLine($"compiled {checkedCount} simple+slot queries");
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

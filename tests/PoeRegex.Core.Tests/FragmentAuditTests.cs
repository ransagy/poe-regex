using PoeRegex.Core;
using PoeRegex.Core.Compilation;
using PoeRegex.Core.Data;
using Xunit.Abstractions;

namespace PoeRegex.Core.Tests;

/// <summary>
/// End-to-end audit over the real PoE2 catalog:
///  - the embedded table matches the catalog version and covers every scope;
///  - every mod resolves in simple mode and in every advanced slot;
///  - every class/mod composes into a one-slot paste, every class pair
///    composes, and the paste diagnostics stay clean.
/// </summary>
public class FragmentAuditTests
{
    private readonly ITestOutputHelper _output;

    public FragmentAuditTests(ITestOutputHelper output) => _output = output;

    private static readonly Lazy<EngineCorpus> Corpus = new(() => RealCatalog.Corpus());
    private static readonly Lazy<PoeRegexCompiler> Compiler =
        new(() => new PoeRegexCompiler(Corpus.Value));
    private static readonly Lazy<PasteComposer> Composer =
        new(() => new PasteComposer(Corpus.Value));

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

    /// <summary>
    /// The checked-in table must match the catalog version and cover every
    /// global and class-scoped pattern. When this fails, regenerate with:
    ///   dotnet run --project tools/PoeRegex.Data.Build -- matrix-solve
    /// </summary>
    [Fact]
    public void PrecomputedFragmentTable_CoversEveryScope()
    {
        var corpus = Corpus.Value;
        corpus.HasPrecomputedFragments.Should().BeTrue(
            "PoE2 needs an embedded fragment table; run matrix-solve");
        corpus.PrecomputedDataVersion.Should().Be(corpus.Catalog.DataVersion,
            "the fragment table is stale; regenerate it with matrix-solve");

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
                    $"{scope ?? "global"} has neither a fragment nor an unresolved reason for '{pattern}'");
                unresolved.Add($"{scope ?? "global"}: {pattern.Replace('\u0001', '#')} — {reason}");
            }
        }
        _output.WriteLine($"{checkedCount} precomputed fragments, " +
                          $"{unresolved.Count} documented unresolved, across {scopes.Count} scopes");
        foreach (var row in unresolved) _output.WriteLine($"  unresolved {row}");
        checkedCount.Should().BeGreaterThan(600);
        unresolved.Count.Should().BeLessThan(60, "unresolved patterns need a deliberate, documented review");
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
            var global = compiler.Compile(new QuerySpecification
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
                var classResult = compiler.Compile(new QuerySpecification
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

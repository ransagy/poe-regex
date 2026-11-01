using PoeRegex.Core;
using PoeRegex.Core.Compilation;
using PoeRegex.Core.Data;
using PoeRegex.Web.Core;

namespace PoeRegex.Web.Tests;

/// <summary>
/// Seeded round-trip fuzz: build random app states, compile them, reconstruct,
/// apply, and require the identical string. This is the acceptance test for
/// "any string built in the app comes back", outside the accepted losses
/// (minimum values, ring split, same-class all-of fallback).
/// </summary>
public class QueryRoundTripTests
{
    private static readonly EngineService Engine = new();
    private static readonly QueryReconstructionService Reconstruction = new(Engine);
    private static readonly IReadOnlyList<ModEntry> GlobalMods = Engine.PlayableMods();

    private static readonly string[] NonWeaponClasses =
        ["Crossbow", "Shield", "Staff", "Wand", "Focus"];

    private static readonly string[] FixedClasses =
        ["Ring", "Helmet", "Body Armour", "Gloves", "Boots", "Belt", "Amulet"];

    [Fact]
    public void RandomSimpleStates_RoundTripAndApply()
    {
        var rng = new Random(20261101);
        for (var i = 0; i < 120; i++)
        {
            var picks = RandomPicks(rng, null, 1 + rng.Next(4));
            var excludes = RandomExcludes(rng, GlobalMods, rng.Next(3));
            var matchAll = picks.Count > 1 && rng.Next(2) == 0;
            var compiled = Engine.CompilePicks(picks, excludes, matchAll, null);
            if (compiled.Query.Length == 0) continue;

            AssertRoundTrip(compiled.Query, $"simple seed {i}");
            var recovered = Reconstruction.ReconstructWorkbench(compiled.Query);
            var state = new WorkbenchState();
            recovered.ApplyTo(state);
            Engine.CompilePicks(state.Global.Included, state.Global.Excluded, state.Global.MatchAll, null)
                .Query.Should().Be(compiled.Query, $"simple apply seed {i}: {compiled.Query}");
        }
    }

    [Fact]
    public void RandomAdvancedStates_RoundTripAndApply()
    {
        var rng = new Random(20261102);
        var classes = FixedClasses.Concat(NonWeaponClasses).ToList();
        for (var i = 0; i < 120; i++)
        {
            var classId = classes[rng.Next(classes.Count)];
            var mods = Engine.ModsForClasses([classId]);
            var picks = RandomPicks(rng, classId, 1 + rng.Next(4));
            var excludes = RandomExcludes(rng, mods, rng.Next(3));
            var matchAll = picks.Count > 1 && rng.Next(2) == 0;
            var compiled = Engine.CompilePicks(picks, excludes, matchAll, classId);
            if (compiled.Query.Length == 0) continue;

            AssertRoundTrip(compiled.Query, $"advanced {classId} seed {i}");
            var recovered = Reconstruction.ReconstructWorkbench(compiled.Query);
            var state = new WorkbenchState();
            recovered.ApplyTo(state);
            var active = state.ActiveSlots();
            active.Should().ContainSingle($"advanced apply seed {i}");
            var crit = state.CriteriaFor(active[0]);
            Engine.CompilePicks(crit.Included, crit.Excluded, crit.MatchAll, crit.ClassId)
                .Query.Should().Be(compiled.Query, $"advanced apply {classId} seed {i}: {compiled.Query}");
        }
    }

    [Fact]
    public void RandomMultiClassStates_RoundTripAndApply()
    {
        var rng = new Random(20261103);
        for (var i = 0; i < 120; i++)
        {
            var classes = RandomClasses(rng);
            var slots = classes
                .Select(c => (c, Picks: RandomPicks(rng, c, 1 + rng.Next(3))))
                .ToList();
            var excludes = RandomExcludes(rng, GlobalMods, rng.Next(2));
            MultiPasteResult combined;
            try
            {
                combined = Engine.Combine(
                    slots.Select(s => (s.c, s.c, (IEnumerable<WantedMod>)s.Picks,
                        s.Picks.Count > 1 && rng.Next(2) == 0)).ToList(),
                    excludes);
            }
            catch (RegexLengthOverflowException)
            {
                continue; // the app refuses to compose this paste
            }
            if (combined.Query.Length == 0) continue;

            AssertRoundTrip(combined.Query, DescribeMulti(classes, slots, i));
            var recovered = Reconstruction.ReconstructWorkbench(combined.Query);
            var state = new WorkbenchState();
            recovered.ApplyTo(state);
            var active = state.ActiveSlots();
            var recompiled = Engine.Combine(
                active.Select(s => (s, state.CriteriaFor(s).ClassId,
                    (IEnumerable<WantedMod>)state.CriteriaFor(s).Included,
                    state.CriteriaFor(s).MatchAll)),
                active.SelectMany(s => state.CriteriaFor(s).Excluded).Distinct(StringComparer.Ordinal),
                allowOverflow: true).Query;
            recompiled.Should().Be(combined.Query, $"multi apply seed {i}");
        }
    }

    private static void AssertRoundTrip(string query, string label)
    {
        var recovered = Reconstruction.ReconstructWorkbench(query);
        recovered.Ok.Should().BeTrue(
            $"{label}: {string.Join("; ", recovered.Diagnostics)} | query: {query}");
        recovered.Recompile(Engine).Should().Be(query, label);
    }

    private static string DescribeMulti(
        List<string> classes, List<(string c, List<WantedMod> Picks)> slots, int seed) =>
        $"multi {string.Join("+", classes)} seed {seed}: " +
        string.Join(" | ", slots.Select(s =>
            s.c + ":[" + string.Join(",", s.Picks.Select(DescribePick)) + "]"));

    private static string DescribePick(WantedMod pick) =>
        pick.ModId +
        (pick.Names is { Count: > 0 } ? "{" + string.Join("/", pick.Names) + "}" : "") +
        (pick.VariantModIds is { Count: > 0 }
            ? "<" + string.Join("/", pick.VariantModIds) + ">"
            : "");

    private static List<WantedMod> RandomPicks(Random rng, string? classId, int count)
    {
        var picks = new List<WantedMod>();
        for (var i = 0; i < count; i++)
        {
            var pick = RandomPick(rng, classId);
            if (!picks.Any(p => p.SamePick(pick))) picks.Add(pick);
        }
        return picks;
    }

    private static WantedMod RandomPick(Random rng, string? classId)
    {
        var mods = classId is null ? GlobalMods : Engine.ModsForClasses([classId]);
        var families = ModFamilies.Group(mods)
            .Where(f => f.DisplayLines.Count > 0)
            .ToList();

        return rng.Next(3) switch
        {
            0 => new WantedMod(mods[rng.Next(mods.Count)].Id),
            1 => families[rng.Next(families.Count)].Pick(),
            _ => NamePick(rng, families),
        };
    }

    private static WantedMod NamePick(Random rng, IReadOnlyList<ModFamily> families)
    {
        var named = families
            .Where(f => f.Tiers.Any(t => !string.IsNullOrWhiteSpace(t.Name)))
            .ToList();
        var family = named[rng.Next(named.Count)];
        var tier = family.Tiers.Where(t => !string.IsNullOrWhiteSpace(t.Name)).ToList();
        var chosen = tier[rng.Next(tier.Count)];
        return new WantedMod(chosen.Anchor.Id, Names: family.NamesFrom(chosen));
    }

    private static List<string> RandomExcludes(Random rng, IReadOnlyList<ModEntry> mods, int count) =>
        Enumerable.Range(0, count)
            .Select(_ => mods[rng.Next(mods.Count)].Id)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    private static List<string> RandomClasses(Random rng)
    {
        var fixedCount = 1 + rng.Next(2);
        var nonWeaponCount = 1 + rng.Next(2);
        var classes = FixedClasses.OrderBy(_ => rng.Next()).Take(fixedCount)
            .Concat(NonWeaponClasses.OrderBy(_ => rng.Next()).Take(nonWeaponCount))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return classes;
    }
}

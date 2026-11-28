using System.Text.Json;
using PoeRegex.Core;
using PoeRegex.Core.Compilation;
using PoeRegex.Web.Core;

namespace PoeRegex.Web.Tests;

/// <summary>
/// Runtime tier ranges are compiled from value plans, not the plain fragment
/// table. These tests are deliberately family-agnostic: every normal family's
/// range forms and random cross-family groups must reconstruct, apply, and
/// survive the saved-pick/share codec.
/// </summary>
public class RangeQueryRoundTripTests
{
    private static readonly EngineService Engine = new();
    private static readonly QueryReconstructionService Reconstruction = new(Engine);
    private static readonly IReadOnlyList<ModFamily> Families = ModFamilies
        .Group(Engine.PlayableMods())
        .Where(family => family.Source.Length == 0 && !family.IsUnion)
        .GroupBy(family => family.Key)
        .Select(group => group.First())
        .OrderBy(family => family.Key, StringComparer.Ordinal)
        .ToList();

    [Fact]
    public void EveryNormalFamilyTierRange_ReconstructsAndRecompiles()
    {
        var checkedCount = 0;
        foreach (var family in Families)
        {
            if (family.Tiers.Count == 0) continue;
            foreach (var (tier, up) in new[]
                     {
                         (family.Tiers[0], true),
                         (family.Tiers[^1], false),
                     })
            {
                var pick = Engine.TierRangePick(family, tier, up);
                if (pick is null || !IsSnapshotSafe(pick)) continue;
                var compiled = Engine.CompilePicks([pick], [], false, null);
                if (compiled.Query.Length == 0) continue;
                checkedCount++;

                var recovered = Reconstruction.ReconstructWorkbench(compiled.Query);
                recovered.Ok.Should().BeTrue(
                    $"{family.Key}: {string.Join("; ", recovered.Diagnostics)} | {compiled.Query}");
                AssertEquivalent(recovered.Recompile(Engine), compiled.Query,
                    $"{family.Key} recompile");
            }
        }
        checkedCount.Should().BeGreaterThan(100,
            "the family-agnostic sweep must cover a meaningful slice of the catalog");
    }

    [Fact]
    public void ClassScopedRangePicks_ReconstructAndRecompile()
    {
        var checkedCount = 0;
        foreach (var itemClass in Engine.InGameClasses())
        {
            var families = ModFamilies.Group(Engine.ModsForClass(itemClass.Id))
                .Where(family => family.Source.Length == 0 && !family.IsUnion)
                .GroupBy(family => family.Key)
                .Select(group => group.First())
                .OrderBy(family => family.Key, StringComparer.Ordinal)
                .ToList();
            foreach (var family in families)
            {
                if (family.Tiers.Count == 0) continue;
                var pick = Engine.TierRangePick(family, family.Tiers[0], up: true);
                if (pick is null || !IsSnapshotSafe(pick)) continue;
                var compiled = Engine.CompilePicks([pick], [], false, itemClass.Id);
                if (compiled.Query.Length == 0) continue;
                checkedCount++;

                var recovered = Reconstruction.ReconstructWorkbench(compiled.Query);
                recovered.Ok.Should().BeTrue(
                    $"{itemClass.Id}/{family.Key}: " +
                    $"{string.Join("; ", recovered.Diagnostics)} | {compiled.Query}");
                AssertEquivalent(recovered.Recompile(Engine), compiled.Query,
                    $"{itemClass.Id}/{family.Key} recompile");
                break; // one representative range family per class
            }
        }
        checkedCount.Should().BeGreaterThan(10,
            "the class-scoped sweep must cover the classes with range picks");
    }

    [Fact]
    public void RandomRangeGroups_ReconstructApplyAndRecompile()
    {
        var rng = new Random(20261128);
        var checkedCount = 0;
        for (var i = 0; i < 20; i++)
        {
            var picks = RandomRangePicks(rng, 2 + rng.Next(4));
            if (picks.Count < 2) continue;
            var compiled = Engine.CompilePicks(picks, [], false, null);
            if (compiled.Query.Length == 0) continue;
            checkedCount++;

            var recovered = Reconstruction.ReconstructWorkbench(compiled.Query);
            recovered.Ok.Should().BeTrue(
                $"seed {i}: {string.Join("; ", recovered.Diagnostics)} | {compiled.Query}");

            var state = new WorkbenchState();
            recovered.ApplyTo(state);
            var applied = Engine.CompilePicks(
                state.Global.Included, state.Global.Excluded, state.Global.MatchAll, null).Query;
            AssertEquivalent(applied, compiled.Query, $"seed {i} apply");
        }
        checkedCount.Should().BeGreaterThan(10);
    }

    [Fact]
    public void UnfactoredRangeQueries_ReconstructToTheFactoredForm()
    {
        var rng = new Random(20261130);
        var checkedCount = 0;
        for (var i = 0; i < 20; i++)
        {
            var picks = RandomRangePicks(rng, 2 + rng.Next(3));
            if (picks.Count < 2) continue;
            var factored = Engine.CompilePicks(picks, [], false, null).Query;
            var branches = new List<string>();
            var usable = true;
            foreach (var pick in picks)
            {
                var single = Engine.CompilePicks([pick], [], false, null).Query;
                if (!single.StartsWith('"') || !single.EndsWith('"')) { usable = false; break; }
                branches.Add(single[1..^1]);
            }
            if (!usable) continue;
            var unfactored = "\"" + string.Join("|", branches.Distinct(StringComparer.Ordinal)) + "\"";
            checkedCount++;

            var recovered = Reconstruction.ReconstructWorkbench(unfactored);
            recovered.Ok.Should().BeTrue(
                $"seed {i}: {string.Join("; ", recovered.Diagnostics)} | {unfactored}");
            AssertEquivalent(recovered.Recompile(Engine), factored,
                $"seed {i} pre-factoring recompile");
        }
        checkedCount.Should().BeGreaterThan(10);
    }

    [Fact]
    public void RandomRangeStates_SurviveSavedPicksAndShareCodec()
    {
        var rng = new Random(20261129);
        for (var i = 0; i < 20; i++)
        {
            var picks = RandomRangePicks(rng, 2 + rng.Next(4));
            if (picks.Count < 2) continue;
            var compiled = Engine.CompilePicks(picks, [], false, null);
            if (compiled.Query.Length == 0) continue;

            var state = new WorkbenchState();
            foreach (var pick in picks) state.AddInclude(null, pick);
            var snapshot = WorkbenchSnapshot.Capture(state);

            // Saved picks store the snapshot as JSON, independent of token size.
            var json = JsonSerializer.Serialize(snapshot, WebJsonContext.Default.WorkbenchSnapshot);
            var fromJson = JsonSerializer.Deserialize(
                json, WebJsonContext.Default.WorkbenchSnapshot);
            fromJson.Should().NotBeNull($"seed {i} saved-pick JSON");
            SnapshotValidator.IsValid(fromJson!).Should().BeTrue($"seed {i} saved-pick JSON");
            AssertRestoredCompiles(fromJson!, compiled.Query, $"seed {i} saved picks");

            // Share links use the same snapshot through the compact codec. The
            // app refuses oversized links; the codec must reject them too.
            var token = SnapshotCodec.Encode(snapshot);
            if (token.Length <= SnapshotCodec.MaxTokenLength)
            {
                var decoded = SnapshotCodec.Decode(token);
                decoded.Should().NotBeNull($"seed {i} share token");
                SnapshotValidator.IsValid(decoded!).Should().BeTrue($"seed {i} share token");
                AssertRestoredCompiles(decoded!, compiled.Query, $"seed {i} share link");
            }
            else
            {
                SnapshotCodec.Decode(token).Should().BeNull(
                    $"seed {i}: oversized share tokens are rejected");
            }
        }
    }

    private static List<WantedMod> RandomRangePicks(Random rng, int count)
    {
        var picks = new List<WantedMod>();
        var available = Families.Where(family => family.Tiers.Count > 0).ToList();
        for (var attempt = 0; attempt < available.Count && picks.Count < count; attempt++)
        {
            var family = available[rng.Next(available.Count)];
            if (picks.Any(pick => pick.ModId == family.Anchor.Id)) continue;
            var tier = family.Tiers[rng.Next(family.Tiers.Count)];
            var pick = Engine.TierRangePick(family, tier, up: rng.Next(2) == 0);
            if (pick is null || !IsSnapshotSafe(pick)) continue;
            if (picks.Any(existing => existing.SamePick(pick))) continue;
            picks.Add(pick);
        }
        return picks;
    }

    private static bool IsSnapshotSafe(WantedMod? pick)
    {
        if (pick is null) return false;
        if (pick.RangeModIds is { Count: > 256 }) return false;
        if (pick.MinValue is { } min &&
            (!WantedMod.IsValidMinValue(min) || !WantedMod.IsValidMaxValue(pick.MaxValue ?? min)))
            return false;
        if (pick.MaxValue is { } max && !WantedMod.IsValidMaxValue(max)) return false;
        return pick.MinValue is not { } lo || pick.MaxValue is not { } hi || lo <= hi;
    }

    private static void AssertRestoredCompiles(
        WorkbenchSnapshot snapshot, string expected, string label)
    {
        var restored = new WorkbenchState();
        WorkbenchSnapshot.RestoreCriteria(restored, snapshot);
        var recompiled = Engine.CompilePicks(
            restored.Global.Included, restored.Global.Excluded,
            restored.Global.MatchAll, null).Query;
        AssertEquivalent(recompiled, expected, label);
    }

    private static void AssertEquivalent(string? actual, string expected, string label)
    {
        if (actual is null) throw new Xunit.Sdk.XunitException($"{label}: no recompile");
        if (actual == expected) return;
        QueryEquivalence.Equivalent(actual, expected, Engine.Syntax)
            .Should().BeTrue($"{label}: expected '{expected}', got '{actual}'");
    }
}

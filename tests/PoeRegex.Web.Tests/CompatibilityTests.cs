using System.IO.Compression;
using System.Text;
using System.Text.Json;
using PoeRegex.Core.Compilation;
using PoeRegex.Core.Data;
using PoeRegex.Web.Core;

namespace PoeRegex.Web.Tests;

/// <summary>
/// Fixtures captured from the previous shipped data/schema. These are not
/// regenerated from the branch under test; they keep old saved picks, share
/// links, and query strings loadable after catalog and table changes.
/// </summary>
public class CompatibilityTests
{
    private static readonly string Root =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));

    private static EngineService Poe1Engine()
    {
        var dir = Path.Combine(Root, "data", "catalogs");
        var catalog = new FileCatalogSource(Path.Combine(dir, "poe1.catalog.json")).Load();
        var table = JsonSerializer.Deserialize(
            File.ReadAllText(Path.Combine(dir, "poe1.fragments.json")),
            CatalogJsonContext.Default.FragmentTable)!;
        var vendor = JsonSerializer.Deserialize(
            File.ReadAllText(Path.Combine(dir, "poe1.vendor-corpus.json")),
            CatalogJsonContext.Default.VendorCorpusFile)!;
        return new EngineService(new GameData(catalog, table, vendor));
    }

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(
            Root, "tests", "PoeRegex.Web.Tests", "Fixtures", "Compatibility", name));

    private static WorkbenchSnapshot Snapshot(string name) =>
        JsonSerializer.Deserialize(
            Fixture(name), WebJsonContext.Default.WorkbenchSnapshot)!;

    [Fact]
    public void CurrentSnapshots_UseTheRangeAwareSchemaVersion()
    {
        var snapshot = WorkbenchSnapshot.Capture(new WorkbenchState());
        snapshot.Version.Should().Be(7);
        SnapshotValidator.IsValid(snapshot).Should().BeTrue();
    }

    [Fact]
    public void PreviousReleasePoe2Snapshot_RestoresAndCompiles()
    {
        var engine = new EngineService();
        var state = new WorkbenchState();

        var removed = WorkbenchSnapshot.Restore(
            state, Snapshot("poe2-v6-snapshot.json"), id => engine.FindMod(id) is not null);

        removed.Should().BeEmpty();
        state.Mode.Should().Be("advanced");
        state.ExternalIncludes.Should().ContainSingle();
        var boots = state.CriteriaFor("boots");
        boots.Included.Should().HaveCount(2);
        boots.Excluded.Should().Equal("ManaRegeneration1");

        var query = engine.CompilePicks(boots.Included, boots.Excluded, boots.MatchAll, boots.ClassId).Query;
        query.Should().NotBeEmpty();
    }

    [Fact]
    public void PreviousReleasePoe1Snapshot_RestoresAndCompiles()
    {
        var engine = Poe1Engine();
        var state = new WorkbenchState();

        var removed = WorkbenchSnapshot.Restore(
            state, Snapshot("poe1-v6-snapshot.json"), id => engine.FindMod(id) is not null);

        removed.Should().BeEmpty();
        state.Global.Included.Should().ContainSingle();
        var query = engine.CompilePicks(
            state.Global.Included, state.Global.Excluded, state.Global.MatchAll, null).Query;
        query.Should().NotBeEmpty();
    }

    [Fact]
    public void PreviousReleaseShareToken_DecodesAndRestores()
    {
        var token = RawToken(Fixture("poe2-v6-snapshot.json"));
        var snap = SnapshotCodec.Decode(token);
        var engine = new EngineService();

        snap.Should().NotBeNull();
        var state = new WorkbenchState();
        WorkbenchSnapshot.Restore(state, snap!, id => engine.FindMod(id) is not null);
        state.CriteriaFor("boots").Included.Should().HaveCount(2);
    }

    [Fact]
    public void PreviousReleaseSavedPicks_LoadFromJson()
    {
        var picks = JsonSerializer.Deserialize(
            Fixture("poe2-v6-saved-picks.json"),
            WebJsonContext.Default.ListSavedPick)!;

        picks.Should().ContainSingle();
        picks[0].Name.Should().Be("Legacy boots");
        picks[0].Game.Should().Be("poe2");
        SnapshotValidator.IsValid(picks[0]).Should().BeTrue();

        var engine = new EngineService();
        var state = new WorkbenchState();
        var removed = WorkbenchSnapshot.RestoreCriteria(
            state, picks[0].Snapshot, id => engine.FindMod(id) is not null);
        removed.Should().BeEmpty();
        state.Global.Included.Should().ContainSingle();
    }

    [Fact]
    public void CurrentRangeSnapshot_RestoresAndCompiles()
    {
        var engine = new EngineService();
        var state = new WorkbenchState();

        var removed = WorkbenchSnapshot.Restore(
            state, Snapshot("poe2-range-v6-snapshot.json"), id => engine.FindMod(id) is not null);

        removed.Should().BeEmpty();
        var pick = state.Global.Included.Should().ContainSingle().Subject;
        pick.IsTierRange.Should().BeTrue();
        pick.RangeModIds.Should().Equal("MovementVelocity2", "MovementVelocity4");
        var query = engine.CompilePicks([pick], [], false, null).Query;
        query.Should().Contain("eed");
    }

    [Fact]
    public void RetiredPoe1Pick_IsRemovedAndNeverCompiledAsAnId()
    {
        var engine = Poe1Engine();
        var state = new WorkbenchState();
        var snap = Snapshot("poe1-v6-retired-snapshot.json");
        SnapshotValidator.IsValid(snap).Should().BeTrue();

        var removed = WorkbenchSnapshot.Restore(state, snap, id => engine.FindMod(id) is not null);

        removed.Should().Equal("QuiverHasOneSocket");
        state.Global.Included.Should().BeEmpty();
        var query = engine.CompilePicks(
            state.Global.Included, state.Global.Excluded, state.Global.MatchAll, null).Query;
        query.Should().BeEmpty();
        query.Should().NotContain("QuiverHasOneSocket");
    }

    [Fact]
    public void RetiredPoe1Pick_InShareToken_IsDropped()
    {
        var engine = Poe1Engine();
        var token = RawToken(Fixture("poe1-v6-retired-snapshot.json"));
        var snap = SnapshotCodec.Decode(token);
        snap.Should().NotBeNull();
        var state = new WorkbenchState();

        var removed = WorkbenchSnapshot.RestoreCriteria(
            state, snap!,
            id => engine.FindMod(id) is not null,
            engine.IsKnownExclusion);

        removed.Should().Equal("QuiverHasOneSocket");
        state.Global.Included.Should().BeEmpty();
    }

    [Fact]
    public void RetiredPoe1Exclusion_IsRemoved()
    {
        var engine = Poe1Engine();
        var snap = Snapshot("poe1-v6-retired-snapshot.json");
        snap.Global.Included.Clear();
        snap.Global.Excluded.Add("QuiverHasOneSocket");
        var state = new WorkbenchState();

        var removed = WorkbenchSnapshot.Restore(state, snap, id => engine.FindMod(id) is not null);

        removed.Should().Equal("QuiverHasOneSocket");
        state.Global.Excluded.Should().BeEmpty();
    }

    [Fact]
    public void NamedTierExclusion_SurvivesCatalogValidation()
    {
        var engine = new EngineService();
        var snap = SnapshotCodec.CaptureCriteria(new WorkbenchState());
        snap.Global.Excluded.Add("Runner's");
        var state = new WorkbenchState();

        var removed = WorkbenchSnapshot.Restore(
            state, snap,
            id => engine.FindMod(id) is not null,
            engine.IsKnownExclusion);

        removed.Should().BeEmpty();
        state.Global.Excluded.Should().Equal("Runner's");
    }

    [Fact]
    public void RawTextPicks_SurviveCatalogValidation()
    {
        var snap = SnapshotCodec.CaptureCriteria(new WorkbenchState());
        snap.Global.Included.Add(new WantedMod(RawText.Encode("Item Class: (am|ri)")));
        var state = new WorkbenchState();

        var removed = WorkbenchSnapshot.Restore(state, snap, _ => false);

        removed.Should().BeEmpty();
        state.Global.Included.Should().ContainSingle();
        state.Global.Included[0].IsRawText.Should().BeTrue();
    }

    [Fact]
    public void PreviousReleaseQueryStrings_ImportOrFailClosed()
    {
        foreach (var (game, file) in new[]
                 {
                     ("poe2", "queries-poe2-before-tier-ranges.txt"),
                     ("poe1", "queries-poe1-before-repoe.txt"),
                 })
        {
            var engine = game == "poe1" ? Poe1Engine() : new EngineService();
            var importer = new QueryImportService(engine);
            var reconstruction = new QueryReconstructionService(engine);

            foreach (var query in File.ReadLines(Path.Combine(
                             Root, "tests", "PoeRegex.Web.Tests", "Fixtures", "Compatibility", file))
                         .Where(line => line.Length > 0))
            {
                var imported = importer.Import(query);
                imported.Tokens.Should().NotContain(
                    token => token.ModId == "QuiverHasOneSocket",
                    $"the retired id must not be revived by '{query}'");

                var recovered = reconstruction.ReconstructWorkbench(query);
                if (!recovered.Ok) continue;
                foreach (var pick in recovered.Slots.SelectMany(slot => slot.Includes))
                {
                    (pick.IsRawText || engine.FindMod(pick.ModId) is not null)
                        .Should().BeTrue($"'{query}' recovered unknown pick '{pick.ModId}'");
                }
                var recompiled = recovered.Recompile(engine);
                if (recompiled is not null)
                    recompiled.Should().NotContain("QuiverHasOneSocket");
            }
        }
    }

    private static string RawToken(string json)
    {
        using var packed = new MemoryStream();
        using (var deflate = new DeflateStream(packed, CompressionLevel.SmallestSize, leaveOpen: true))
            deflate.Write(Encoding.UTF8.GetBytes(json));
        return Convert.ToBase64String(packed.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}

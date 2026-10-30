using PoeRegex.Core.Data;
using PoeRegex.Core.Explain;
using PoeRegex.Web.Core;

namespace PoeRegex.Web.Tests;

public class MatchModeTests
{
    private static readonly EngineService Engine = new();

    private static string Line(string modId) =>
        AdvancedStatLine.For(Engine.FindMod(modId)!.StatLines[0]);

    [Fact]
    public void CompilePicks_MatchAll_EmitsOneBlockPerPick()
    {
        WantedMod[] picks = [new("IncreasedLife6"), new("Strength1")];

        var any = Engine.CompilePicks(picks, [], matchAll: false, itemClass: null);
        var all = Engine.CompilePicks(picks, [], matchAll: true, itemClass: null);

        Explainer.Tokenize(any.Query).Should().ContainSingle();
        Explainer.Tokenize(all.Query).Should().HaveCount(2);
        ItemMatcher.Match(all.Query, [Line("IncreasedLife6")]).Match.Should().BeFalse("one of two required mods");
        ItemMatcher.Match(all.Query, [Line("IncreasedLife6"), Line("Strength1")]).Match.Should().BeTrue();
        ItemMatcher.Match(any.Query, [Line("IncreasedLife6")]).Match.Should().BeTrue();
    }

    [Fact]
    public void Combine_MatchAllSlot_RequiresEveryPickOnThatClassOnly()
    {
        var paste = Engine.Combine(
        [
            ("ring1 (Ring)", "Ring", new[] { new WantedMod("IncreasedLife6"), new WantedMod("Strength1") }, true),
            ("amulet (Amulet)", "Amulet", Array.Empty<WantedMod>(), false),
        ], []);

        paste.Groups.Should().HaveCount(2);
        ItemMatcher.Match(paste.Query, ["Item Class: Rings", Line("IncreasedLife6")]).Match.Should().BeFalse();
        ItemMatcher.Match(paste.Query, ["Item Class: Rings", Line("IncreasedLife6"), Line("Strength1")]).Match.Should().BeTrue();
        ItemMatcher.Match(paste.Query, ["Item Class: Amulets"]).Match.Should().BeTrue();
    }

    [Fact]
    public void SetMinValue_KeepsPositionAndIdentity()
    {
        var state = new WorkbenchState();
        state.AddInclude(null, new WantedMod("IncreasedLife6"));
        state.AddInclude(null, new WantedMod("Strength1"));

        state.SetMinValue(null, new WantedMod("IncreasedLife6"), 80).Should().BeTrue();

        state.Global.Included[0].MinValue.Should().Be(80);
        state.Global.Included[0].SamePick(new WantedMod("IncreasedLife6")).Should().BeTrue();
        state.Global.Included[1].ModId.Should().Be("Strength1");
    }

    [Fact]
    public void SetMinValue_IgnoresNamePicks()
    {
        var state = new WorkbenchState();
        var pick = new WantedMod("IncreasedLife6", ["Stout"]);
        state.AddInclude(null, pick);

        state.SetMinValue(null, pick, 80).Should().BeFalse();
        state.Global.Included[0].MinValue.Should().BeNull();
    }

    [Fact]
    public void Snapshot_RoundTrips_MatchAllAndMinValue()
    {
        var state = new WorkbenchState();
        state.SetMode("advanced");
        state.AddInclude("helmet", new WantedMod("IncreasedLife6"));
        state.SetMinValue("helmet", new WantedMod("IncreasedLife6"), 70);
        state.SetMatchAll("helmet", true);
        state.SetMatchAll(null, true);

        var json = System.Text.Json.JsonSerializer.Serialize(
            WorkbenchSnapshot.Capture(state), WebJsonContext.Default.WorkbenchSnapshot);
        var back = System.Text.Json.JsonSerializer.Deserialize(json, WebJsonContext.Default.WorkbenchSnapshot)!;
        var restored = new WorkbenchState();
        WorkbenchSnapshot.Restore(restored, back);

        restored.CriteriaFor("helmet").MatchAll.Should().BeTrue();
        restored.CriteriaFor("helmet").Included[0].MinValue.Should().Be(70);
        restored.Global.MatchAll.Should().BeTrue();
    }

    [Fact]
    public void QueryImport_RecognisesAnAllOfString()
    {
        WantedMod[] picks = [new("IncreasedLife6"), new("Strength1")];
        var all = Engine.CompilePicks(picks, [], matchAll: true, itemClass: null);
        var any = Engine.CompilePicks(picks, [], matchAll: false, itemClass: null);

        new QueryImportService(Engine).Import(all.Query).RequiresAll.Should().BeTrue();
        new QueryImportService(Engine).Import(any.Query).RequiresAll.Should().BeFalse();
    }
}

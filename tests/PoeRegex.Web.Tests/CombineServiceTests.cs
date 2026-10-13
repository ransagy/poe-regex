using PoeRegex.Core.Compilation;
using PoeRegex.Web.Core;

namespace PoeRegex.Web.Tests;

/// <summary>
/// Combined-paste integration: structure, per-group recall cleanliness, and
/// lab-matcher agreement on item-level verdicts (ring+life bright, plain
/// ring dim, plain amulet bright).
/// </summary>
public class CombineServiceTests
{
    private static readonly EngineService Engine = new();

    [Fact]
    public void Combine_RingLife_AmuletAny_MatcherAgrees()
    {
        var paste = Engine.Combine(
        [
            ("weapon (Ring)", "Ring", new[] { new WantedMod("IncreasedLife6") }),
            ("amulet (Amulet)", "Amulet", []),
        ], []);
        paste.Diagnostics.Should().BeEmpty();
        paste.Groups.Should().HaveCount(1, "only the modded ring gets an explicit group");
        paste.Groups[0].TargetClasses.Should().Equal(["Ring"]);

        var lifeLine = Engine.FindMod("IncreasedLife6")!.StatLines[0].Line;
        ItemMatcher.Match(paste.Query, [lifeLine, "Item Class: Rings"])
            .Match.Should().BeTrue("ring with life satisfies anchor + ring group");
        ItemMatcher.Match(paste.Query, ["Item Class: Rings"])
            .Match.Should().BeFalse("plain ring fails the ring group");
        ItemMatcher.Match(paste.Query, ["Item Class: Amulets"])
            .Match.Should().BeTrue("unmodded amulet slot matches everything");
    }

    [Fact]
    public void Combine_Deterministic()
    {
        MultiPasteResult Build() => Engine.Combine(
        [
            ("weapon (Ring)", "Ring", new[] { new WantedMod("IncreasedLife6") }),
            ("amulet (Amulet)", "Amulet", []),
        ], []);
        Build().Query.Should().Be(Build().Query);
    }
}

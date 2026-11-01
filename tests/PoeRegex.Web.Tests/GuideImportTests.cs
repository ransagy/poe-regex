using PoeRegex.Web.Core;

namespace PoeRegex.Web.Tests;

public class GuideImportTests
{
    private static readonly EngineService Engine = new();
    private static readonly QueryReconstructionService Reconstruction = new(Engine);
    private static readonly GuideImportService Guide = new(Engine, Reconstruction);

    [Fact]
    public void StatPhrase_MapsToThePrimaryFamily()
    {
        var result = Guide.Import("maximum Life");

        result.Ok.Should().BeTrue(string.Join("; ", result.Warnings));
        result.Branches.Should().ContainSingle();
        result.Branches[0].Kind.Should().Be(GuideBranchKind.MultiFamily);
        result.Branches[0].Selected.Should().Be("IncreasedLife");
        result.Branches[0].Candidates.Select(c => c.Key).Should().Contain("IncreasedLife");
        result.Branches[0].Picks.Should().ContainSingle();
        result.CompiledQuery.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void AffixName_BecomesANamePick()
    {
        var result = Guide.Import("of the Bear");

        result.Ok.Should().BeTrue(string.Join("; ", result.Warnings));
        result.Branches.Should().ContainSingle();
        result.Branches[0].Kind.Should().Be(GuideBranchKind.AffixName);
        var pick = result.Branches[0].Picks.Should().ContainSingle().Subject;
        pick.IsNamePick.Should().BeTrue();
        pick.Names.Should().Contain("of the Bear");
    }

    [Fact]
    public void TableFragment_BecomesAFragmentPick()
    {
        var result = Guide.Import("\\+.*ife");

        result.Ok.Should().BeTrue(string.Join("; ", result.Warnings));
        result.Branches[0].Kind.Should().Be(GuideBranchKind.Fragment);
        result.Branches[0].Picks.Should().ContainSingle();
        result.Branches[0].Picks[0].IsNamePick.Should().BeFalse();
        result.CompiledQuery.Should().Contain("ife");
    }

    [Fact]
    public void UnknownBranch_IsDroppedWithAWarning()
    {
        var result = Guide.Import("zzzz");

        result.Ok.Should().BeFalse();
        result.Workbench.Should().BeNull();
        result.Branches.Should().ContainSingle();
        result.Branches[0].Kind.Should().Be(GuideBranchKind.Dropped);
        result.Warnings.Should().Contain(w => w.Contains("Dropped 'zzzz'"));
    }

    [Fact]
    public void MultiFamilyOverride_ChangesTheSelectedFamily()
    {
        var initial = Guide.Import("life");
        initial.Branches[0].Kind.Should().Be(GuideBranchKind.MultiFamily);
        initial.Branches[0].Candidates.Select(c => c.Key).Should().Contain("LifeGainPerTarget");

        var overridden = Guide.Import(
            "life", new Dictionary<int, string> { [0] = "LifeGainPerTarget" });

        overridden.Branches[0].Selected.Should().Be("LifeGainPerTarget");
        var initialPick = initial.Branches[0].Picks.Single();
        var overriddenPick = overridden.Branches[0].Picks.Single();
        overriddenPick.SamePick(initialPick).Should().BeFalse();
    }

    [Fact]
    public void MixedGuideFilter_AppliesMappedBranchesAndDropsTheRest()
    {
        var result = Guide.Import("of the Bear|\\+.*ife|resistance|zzzz");

        result.Ok.Should().BeTrue(string.Join("; ", result.Warnings));
        result.Mapped.Should().Be(3);
        result.Dropped.Should().Be(1);
        result.Branches.Should().HaveCount(4);

        var state = new WorkbenchState();
        result.Workbench!.ApplyTo(state);
        state.Mode.Should().Be("simple");
        state.Global.Included.Should().HaveCount(3);
        Engine.CompilePicks(
            state.Global.Included, state.Global.Excluded, state.Global.MatchAll, null)
            .Query.Should().Be(result.CompiledQuery);
    }

    [Fact]
    public void MultiLineInput_UsesTheFilterLine()
    {
        var result = Guide.Import(
            "Some guide prose\nlife|of the Bear\nanother prose line");

        result.Ok.Should().BeTrue(string.Join("; ", result.Warnings));
        result.Warnings.Should().Contain(w => w.Contains("Used one filter line"));
        result.Branches.Should().HaveCount(2);
    }
}

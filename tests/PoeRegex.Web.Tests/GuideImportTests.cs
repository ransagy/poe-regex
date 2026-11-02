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
        result.Branches[0].Selected.Should().Contain("IncreasedLife");
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
    public void BareMovementKeyword_MapsToMovementSpeed()
    {
        var result = Guide.Import("mov");

        result.Ok.Should().BeTrue(string.Join("; ", result.Warnings));
        result.Branches[0].Candidates.Select(c => c.Key)
            .Should().Contain("MovementVelocity");
        result.Branches[0].Selected.Should().Contain("MovementVelocity");
    }

    [Fact]
    public void QuotedGroups_AreKeptTogetherAndExclusionIsReported()
    {
        var result = Guide.Import("\"!(uiv)\" \"cross|mov|[egdl] da.* to a\"");

        result.Branches.Should().NotContain(b => b.Text.Contains("cross|mov"));
        result.Branches.Should().Contain(b =>
            b.Kind == GuideBranchKind.Dropped && b.Text.Contains("!(uiv)"));
        var movement = result.Branches.Single(b => b.Text == "mov");
        movement.Picks.Should().NotBeEmpty();
        movement.Candidates.Select(c => c.Key).Should().Contain("MovementVelocity");
        result.Warnings.Should().NotContain(w => w.Contains("Multiple positive groups"));
    }

    [Fact]
    public void MovementRegex_MapsToMovementSpeed()
    {
        var result = Guide.Import("i.+mov");

        result.Ok.Should().BeTrue(string.Join("; ", result.Warnings));
        result.Branches[0].Candidates.Select(c => c.Key)
            .Should().Contain("MovementVelocity");
        result.Branches[0].Selected.Should().Contain("MovementVelocity");
    }

    [Fact]
    public void LifeRegex_MapsToIncreasedLife()
    {
        var result = Guide.Import("\\+.*life");

        result.Ok.Should().BeTrue(string.Join("; ", result.Warnings));
        result.Branches[0].Candidates.Select(c => c.Key)
            .Should().Contain("IncreasedLife");
        result.Branches[0].Selected.Should().Contain("IncreasedLife");
    }

    [Fact]
    public void ResistanceRegex_SelectsMultipleCommonFamilies()
    {
        var result = Guide.Import("i.+res");

        result.Ok.Should().BeTrue(string.Join("; ", result.Warnings));
        result.Branches[0].Kind.Should().Be(GuideBranchKind.MultiFamily);
        var keys = result.Branches[0].Candidates.Select(c => c.Key).ToList();
        keys.Should().Contain("FireResistance");
        keys.Should().Contain("LightningResistance");
        result.Branches[0].Selected.Should().HaveCountGreaterThan(1);
        result.Branches[0].Picks.Should().HaveCountGreaterThan(1);
    }

    [Fact]
    public void MaxLifeRegex_MapsToLifeFamily()
    {
        var result = Guide.Import("max.*life");

        result.Ok.Should().BeTrue(string.Join("; ", result.Warnings));
        result.Branches[0].Selected.Should().Contain("IncreasedLife");
    }

    [Fact]
    public void DamageRegex_MapsToDamageFamilies()
    {
        var result = Guide.Import("i.+damage");

        result.Ok.Should().BeTrue(string.Join("; ", result.Warnings));
        result.Branches[0].Candidates
            .Any(c => c.Key.Contains("Damage", StringComparison.Ordinal))
            .Should().BeTrue();
    }

    [Fact]
    public void EvasionRegex_MapsToAnEvasionFamily()
    {
        var result = Guide.Import("o Ev");

        result.Ok.Should().BeTrue(string.Join("; ", result.Warnings));
        result.Branches[0].Candidates.Select(c => c.Key)
            .Any(k => k.Contains("Evasion", StringComparison.Ordinal))
            .Should().BeTrue();
    }

    [Fact]
    public void UnknownBranch_IsDroppedWithANote()
    {
        var result = Guide.Import("zzzz");

        result.Ok.Should().BeFalse();
        result.Workbench.Should().BeNull();
        result.Branches.Should().ContainSingle();
        result.Branches[0].Kind.Should().Be(GuideBranchKind.Dropped);
        result.Branches[0].Note.Should().Contain("no mod family");
    }

    [Fact]
    public void MultiFamilyOverride_ChangesTheSelectedFamilies()
    {
        var initial = Guide.Import("life");
        initial.Branches[0].Kind.Should().Be(GuideBranchKind.MultiFamily);
        initial.Branches[0].Candidates.Select(c => c.Key).Should().Contain("LifeGainPerTarget");

        var overridden = Guide.Import(
            "life",
            new Dictionary<int, IReadOnlyList<string>>
            {
                [0] = ["LifeGainPerTarget"],
            });

        overridden.Branches[0].Selected.Should().Equal("LifeGainPerTarget");
        var initialPick = initial.Branches[0].Picks.Single();
        var overriddenPick = overridden.Branches[0].Picks.Single();
        overriddenPick.SamePick(initialPick).Should().BeFalse();
    }

    [Fact]
    public void MultiFamilyOverride_CanSelectSeveralFamilies()
    {
        var result = Guide.Import(
            "i.+res",
            new Dictionary<int, IReadOnlyList<string>>
            {
                [0] = ["FireResistance", "LightningResistance"],
            });

        result.Branches[0].Selected.Should().Equal(
            "FireResistance", "LightningResistance");
        result.Branches[0].Picks.Should().HaveCount(2);
    }

    [Fact]
    public void MixedGuideFilter_AppliesMappedBranchesAndDropsTheRest()
    {
        var result = Guide.Import("of the Bear|\\+.*ife|zzzz");

        result.Ok.Should().BeTrue(string.Join("; ", result.Warnings));
        result.Mapped.Should().Be(2);
        result.Dropped.Should().Be(1);
        result.Branches.Should().HaveCount(3);

        var state = new WorkbenchState();
        result.Workbench!.ApplyTo(state);
        state.Mode.Should().Be("simple");
        state.Global.Included.Should().HaveCount(2);
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

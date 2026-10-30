using System.IO.Compression;
using PoeRegex.Web.Core;

namespace PoeRegex.Web.Tests;

public class RecipeTests
{
    private static WorkbenchState Picked()
    {
        var state = new WorkbenchState();
        state.SetMode("advanced");
        state.SetSlotClass("weapon", "Crossbow");
        state.AddInclude("weapon", new WantedMod("IncreasedLife6"));
        state.AddInclude("boots", new WantedMod("IncreasedLife6", ["Stout", "Robust"]));
        state.AddExclude("boots", "ManaRegeneration1");
        state.SetMatchAll("boots", true);
        state.SetMinValue("weapon", new WantedMod("IncreasedLife6"), 50);
        return state;
    }

    [Fact]
    public void Encode_Decode_RoundTripsPicksWithoutTestItems()
    {
        var state = Picked();
        state.AddTestItem(new TestItem { Name = "x", ClassId = "Belt", Parsed = ItemTextParser.Parse(FixtureTexts.Belt) });

        var token = RecipeCodec.Encode(WorkbenchSnapshot.Capture(state));
        var back = RecipeCodec.Decode(token)!;

        token.Should().MatchRegex("^[A-Za-z0-9_-]+$");
        back.TestItems.Should().BeEmpty();
        var restored = new WorkbenchState();
        WorkbenchSnapshot.RestoreCriteria(restored, back);
        restored.Mode.Should().Be("advanced");
        restored.CriteriaFor("weapon").ClassId.Should().Be("Crossbow");
        restored.CriteriaFor("weapon").Included[0].MinValue.Should().Be(50);
        restored.CriteriaFor("boots").Included[0].Names.Should().Equal(["Stout", "Robust"]);
        restored.CriteriaFor("boots").Excluded.Should().Equal(["ManaRegeneration1"]);
        restored.CriteriaFor("boots").MatchAll.Should().BeTrue();
    }

    [Fact]
    public void CaptureCriteria_IsDetachedFromLaterEdits()
    {
        var state = Picked();
        var saved = RecipeCodec.CaptureCriteria(state);

        state.ClearChips("boots");

        saved.Slots["boots"].Included.Should().ContainSingle();
    }

    [Fact]
    public void RestoreCriteria_KeepsTheBackpackAndClearsOtherPicks()
    {
        var target = new WorkbenchState();
        target.AddInclude("helmet", new WantedMod("Strength1"));
        target.AddTestItem(new TestItem { Name = "x", ClassId = "Belt", Parsed = ItemTextParser.Parse(FixtureTexts.Belt) });

        WorkbenchSnapshot.RestoreCriteria(target, RecipeCodec.CaptureCriteria(Picked()));

        target.TestItems.Should().ContainSingle();
        target.CriteriaFor("helmet").Included.Should().BeEmpty();
        target.CriteriaFor("weapon").Included.Should().ContainSingle();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not base64 !!")]
    [InlineData("AAAA")]
    public void Decode_RejectsMalformedTokens(string token) =>
        RecipeCodec.Decode(token).Should().BeNull();

    [Fact]
    public void Decode_RejectsOversizedPayloads()
    {
        using var packed = new MemoryStream();
        using (var deflate = new DeflateStream(packed, CompressionLevel.SmallestSize, leaveOpen: true))
            deflate.Write(new byte[1024 * 1024]);
        var token = Convert.ToBase64String(packed.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        RecipeCodec.Decode(token).Should().BeNull();
    }

    [Theory]
    [InlineData("#r=abc", "abc")]
    [InlineData("r=abc", "abc")]
    [InlineData("#r=", null)]
    [InlineData("#other", null)]
    [InlineData(null, null)]
    public void TokenFromFragment_ReadsOnlyRecipeFragments(string? fragment, string? expected) =>
        RecipeCodec.TokenFromFragment(fragment).Should().Be(expected);
}

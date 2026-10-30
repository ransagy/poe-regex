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
        state.AddInclude("boots", new WantedMod("MovementVelocity1", VariantModIds: ["MovementVelocity1", "MovementVelocity2"]));
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

        saved.Slots["boots"].Included.Should().HaveCount(2);
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

    [Fact]
    public void Decode_RejectsTokensPastTheLengthCap()
    {
        var token = RecipeCodec.Encode(WorkbenchSnapshot.Capture(Picked()));
        // Base64 skips whitespace, so only the length cap stops this padded token.
        var padded = new string(' ', RecipeCodec.MaxTokenLength / 4 * 4) + token;

        RecipeCodec.Decode(token).Should().NotBeNull();
        RecipeCodec.Decode(padded).Should().BeNull();
    }

    [Theory]
    [InlineData("null slot class")]
    [InlineData("blank slot class")]
    [InlineData("null global class")]
    [InlineData("null included entry")]
    [InlineData("blank mod id")]
    [InlineData("null excluded entry")]
    [InlineData("blank excluded entry")]
    [InlineData("null name")]
    [InlineData("empty name")]
    [InlineData("null variant")]
    [InlineData("zero minimum")]
    [InlineData("negative minimum")]
    [InlineData("huge minimum")]
    [InlineData("minimum on a name pick")]
    [InlineData("unknown slot")]
    [InlineData("null criteria")]
    [InlineData("blank mode")]
    [InlineData("blank focus")]
    [InlineData("version 0")]
    [InlineData("future version")]
    [InlineData("too many picks")]
    [InlineData("too many names")]
    public void Decode_RejectsInvalidSnapshots(string spoil)
    {
        var snap = Spoiled(spoil);

        SnapshotValidator.IsValid(snap).Should().BeFalse();
        RecipeCodec.Decode(RecipeCodec.Encode(snap)).Should().BeNull();
    }

    [Fact]
    public void Decode_RejectsNullCriteriaInJson()
    {
        var token = RawToken("""{"Version":4,"Mode":"simple","Slots":{},"Global":null,"TestItems":[]}""");

        RecipeCodec.Decode(token).Should().BeNull();
    }

    [Fact]
    public void Validator_AcceptsCapturedPicks() =>
        SnapshotValidator.IsValid(RecipeCodec.CaptureCriteria(Picked())).Should().BeTrue();

    [Fact]
    public void Validator_RejectsBrokenTestItems()
    {
        var snap = RecipeCodec.CaptureCriteria(Picked());
        var item = new TestItem
        {
            Name = "item",
            ClassId = "Belt",
            Parsed = ItemTextParser.Parse(FixtureTexts.Belt),
        };
        snap.TestItems.Add(item);
        SnapshotValidator.IsValid(snap).Should().BeTrue();

        item.Mapped = null!;
        SnapshotValidator.IsValid(snap).Should().BeFalse();
    }

    [Theory]
    [InlineData("Boots", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    public void Validator_ChecksRecipeNames(string? name, bool valid) =>
        SnapshotValidator.IsValid(new SavedRecipe(name!, DateTimeOffset.UnixEpoch, RecipeCodec.CaptureCriteria(Picked())))
            .Should().Be(valid);

    [Fact]
    public void Validator_RejectsLongRecipeNamesAndMissingSnapshots()
    {
        var picks = RecipeCodec.CaptureCriteria(Picked());

        SnapshotValidator.IsValid(new SavedRecipe(new string('a', SnapshotValidator.MaxRecipeName + 1), DateTimeOffset.UnixEpoch, picks))
            .Should().BeFalse();
        SnapshotValidator.IsValid(new SavedRecipe("Boots", DateTimeOffset.UnixEpoch, null!)).Should().BeFalse();
        SnapshotValidator.IsValid((SavedRecipe?)null).Should().BeFalse();
    }

    private static WorkbenchSnapshot Spoiled(string spoil)
    {
        var snap = RecipeCodec.CaptureCriteria(Picked());
        var weapon = snap.Slots["weapon"];
        var boots = snap.Slots["boots"];
        switch (spoil)
        {
            case "null slot class": boots.ClassId = null!; break;
            case "blank slot class": boots.ClassId = " "; break;
            case "null global class": snap.Global.ClassId = null!; break;
            case "null included entry": boots.Included.Add(null!); break;
            case "blank mod id": boots.Included.Add(new WantedMod(" ")); break;
            case "null excluded entry": boots.Excluded.Add(null!); break;
            case "blank excluded entry": boots.Excluded.Add("\t"); break;
            case "null name": boots.Included[0] = boots.Included[0] with { Names = ["Stout", null!] }; break;
            case "empty name": boots.Included[0] = boots.Included[0] with { Names = [""] }; break;
            case "null variant": boots.Included[1] = boots.Included[1] with { VariantModIds = [null!] }; break;
            case "zero minimum": weapon.Included[0] = weapon.Included[0] with { MinValue = 0 }; break;
            case "negative minimum": weapon.Included[0] = weapon.Included[0] with { MinValue = -5 }; break;
            case "huge minimum": weapon.Included[0] = weapon.Included[0] with { MinValue = 3e9 }; break;
            case "minimum on a name pick": boots.Included[0] = boots.Included[0] with { MinValue = 50 }; break;
            case "unknown slot": snap.Slots["cape"] = new SlotCriteria { ClassId = "Cape" }; break;
            case "null criteria": snap.Slots["boots"] = null!; break;
            case "blank mode": return snap with { Mode = "" };
            case "blank focus": return snap with { MultiFocus = " " };
            case "version 0": return snap with { Version = 0 };
            case "future version": return snap with { Version = WorkbenchSnapshot.CurrentVersion + 1 };
            case "too many picks": boots.Excluded.AddRange(Enumerable.Range(0, 300).Select(i => $"Mod{i}")); break;
            case "too many names":
                boots.Included[0] = boots.Included[0] with { Names = Enumerable.Range(0, 100).Select(i => $"Name{i}").ToList() };
                break;
            default: throw new ArgumentOutOfRangeException(nameof(spoil), spoil, null);
        }
        return snap;
    }

    private static string RawToken(string json)
    {
        using var packed = new MemoryStream();
        using (var deflate = new DeflateStream(packed, CompressionLevel.SmallestSize, leaveOpen: true))
            deflate.Write(System.Text.Encoding.UTF8.GetBytes(json));
        return Convert.ToBase64String(packed.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
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

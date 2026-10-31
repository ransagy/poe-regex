using PoeRegex.Core;
using PoeRegex.Web.Core;

namespace PoeRegex.Web.Tests;

/// <summary>
/// Phase-1 reverse mapping: our own simple / single-slot advanced strings
/// reconstruct to canonical picks and recompile identically. Factored and
/// multi-class forms fail closed until their phases land.
/// </summary>
public class QueryReconstructionTests
{
    private static readonly EngineService Engine = new();
    private static readonly QueryReconstructionService Reconstruction = new(Engine);

    private static (ModFamily Family, ModTier Tier) NamedRingFamily()
    {
        var family = ModFamilies.Group(Engine.ModsForClasses(["Ring"]))
            .Where(f => f.Tiers.Count >= 2 && f.Tiers.Any(t => !string.IsNullOrWhiteSpace(t.Name)))
            .First();
        return (family, family.Tiers.First(t => !string.IsNullOrWhiteSpace(t.Name)));
    }

    private static WantedMod RingNamePick()
    {
        var (family, tier) = NamedRingFamily();
        return new WantedMod(tier.Anchor.Id, Names: family.NamesFrom(tier));
    }

    /// <summary>A name set that occurs in exactly one family, so the global scope is unambiguous.</summary>
    private static WantedMod GlobalNamePick()
    {
        var family = ModFamilies.Group(Engine.PlayableMods())
            .First(f => f.Key == "Strength" && f.Tiers.Count >= 2);
        var tier = family.Tiers.First(t => !string.IsNullOrWhiteSpace(t.Name));
        // One name is unfactored and globally unique; the multi-name form
        // shares the "of the " prefix and belongs to the Phase 2 resolver.
        return new WantedMod(tier.Anchor.Id, Names: [tier.Name]);
    }

    private static WantedMod FamilyPick(string itemClass)
    {
        var family = ModFamilies.Group(Engine.ModsForClasses([itemClass]))
            .First(f => f.Tiers.Count >= 2 && f.DisplayLines.Count > 0);
        return family.Pick();
    }

    [Fact]
    public void Simple_PlainPick_RoundTrips()
    {
        var compiled = Engine.CompilePicks([new WantedMod("IncreasedLife6")], [], false, null);

        var recovered = Reconstruction.Reconstruct(compiled.Query);

        recovered.Ok.Should().BeTrue(recovered.Diagnostics.FirstOrDefault());
        recovered.Mode.Should().Be("simple");
        recovered.ItemClass.Should().BeNull();
        recovered.MatchAll.Should().BeFalse();
        recovered.Includes.Should().ContainSingle();
        recovered.Includes[0].VariantModIds.Should().NotBeNullOrEmpty("the query is tierless");
        recovered.Recompile(Engine)!.Query.Should().Be(compiled.Query);
    }

    [Fact]
    public void Simple_NamePick_RoundTrips()
    {
        var pick = GlobalNamePick();
        var compiled = Engine.CompilePicks([pick], [], false, null);

        var recovered = Reconstruction.Reconstruct(compiled.Query);

        recovered.Ok.Should().BeTrue(recovered.Diagnostics.FirstOrDefault());
        recovered.Includes.Should().ContainSingle();
        recovered.Includes[0].Names.Should().BeEquivalentTo(pick.Names);
        recovered.Recompile(Engine)!.Query.Should().Be(compiled.Query);
    }

    [Fact]
    public void Simple_AmbiguousName_UsesOneRepresentativeFamily()
    {
        // "Clouded|..." exists on both Ring and weapon families. The term is
        // just the name list, so any owning family reconstructs the same query.
        var pick = RingNamePick();
        var compiled = Engine.CompilePicks([pick], [], false, null);

        var recovered = Reconstruction.Reconstruct(compiled.Query);

        recovered.Ok.Should().BeTrue(recovered.Diagnostics.FirstOrDefault());
        recovered.Includes.Should().ContainSingle();
        recovered.Includes[0].Names.Should().BeEquivalentTo(pick.Names);
        recovered.Recompile(Engine)!.Query.Should().Be(compiled.Query);
    }

    [Fact]
    public void Simple_AnyOf_Unfactored_RoundTrips()
    {
        var compiled = Engine.CompilePicks(
            [new WantedMod("IncreasedLife6"), new WantedMod("Strength1")], [], false, null);

        var recovered = Reconstruction.Reconstruct(compiled.Query);

        recovered.Ok.Should().BeTrue(recovered.Diagnostics.FirstOrDefault());
        recovered.Includes.Should().HaveCount(2);
        recovered.MatchAll.Should().BeFalse();
        recovered.Recompile(Engine)!.Query.Should().Be(compiled.Query);
    }

    [Fact]
    public void Simple_AllOf_RoundTrips()
    {
        var compiled = Engine.CompilePicks(
            [new WantedMod("IncreasedLife6"), new WantedMod("Strength1")], [], true, null);

        var recovered = Reconstruction.Reconstruct(compiled.Query);

        recovered.Ok.Should().BeTrue(recovered.Diagnostics.FirstOrDefault());
        recovered.Includes.Should().HaveCount(2);
        recovered.MatchAll.Should().BeTrue();
        recovered.Recompile(Engine)!.Query.Should().Be(compiled.Query);
    }

    [Fact]
    public void Simple_Exclusion_RoundTrips()
    {
        var compiled = Engine.CompilePicks(
            [new WantedMod("IncreasedLife6")], ["Strength1"], false, null);

        var recovered = Reconstruction.Reconstruct(compiled.Query);

        recovered.Ok.Should().BeTrue(recovered.Diagnostics.FirstOrDefault());
        recovered.Excludes.Should().ContainSingle();
        recovered.Recompile(Engine)!.Query.Should().Be(compiled.Query);
    }

    [Fact]
    public void Advanced_PlainPick_RoundTrips()
    {
        var compiled = Engine.CompilePicks([new WantedMod("IncreasedLife6")], [], false, "Ring");

        var recovered = Reconstruction.Reconstruct(compiled.Query);

        recovered.Ok.Should().BeTrue(recovered.Diagnostics.FirstOrDefault());
        recovered.Mode.Should().Be("advanced");
        recovered.ItemClass.Should().Be("Ring");
        recovered.Includes[0].VariantModIds.Should().NotBeNullOrEmpty("the query is tierless");
        recovered.Recompile(Engine)!.Query.Should().Be(compiled.Query);
    }

    [Fact]
    public void Advanced_NamePick_RoundTrips()
    {
        var pick = RingNamePick();
        var compiled = Engine.CompilePicks([pick], [], false, "Ring");

        var recovered = Reconstruction.Reconstruct(compiled.Query);

        recovered.Ok.Should().BeTrue(recovered.Diagnostics.FirstOrDefault());
        recovered.ItemClass.Should().Be("Ring");
        recovered.Includes[0].Names.Should().BeEquivalentTo(pick.Names);
        recovered.Recompile(Engine)!.Query.Should().Be(compiled.Query);
    }

    [Fact]
    public void Advanced_ClassAnchorOnly_RoundTrips()
    {
        var compiled = Engine.Compile([], [], "Ring");

        var recovered = Reconstruction.Reconstruct(compiled.Query);

        recovered.Ok.Should().BeTrue(recovered.Diagnostics.FirstOrDefault());
        recovered.ItemClass.Should().Be("Ring");
        recovered.Includes.Should().BeEmpty();
        recovered.Recompile(Engine)!.Query.Should().Be(compiled.Query);
    }

    [Fact]
    public void FactoredNameSet_FailsClosed()
    {
        // The compiler factors a common name prefix into `...(a|b)`. Phase 1
        // deliberately does not split factored alternations.
        var compiled = Engine.CompilePicks(
            [new WantedMod("Strength1", Names: ["of the Bear", "of the Beast"])], [], false, null);
        compiled.Query.Should().Contain("(");

        var recovered = Reconstruction.Reconstruct(compiled.Query);

        recovered.Ok.Should().BeFalse();
        recovered.Diagnostics.Should().Contain(d => d.Contains("unfactored"));
    }

    [Fact]
    public void MultiClassPaste_FailsClosed()
    {
        var combined = Engine.Combine(
        [
            ("weapon", "Crossbow", new[] { FamilyPick("Crossbow") }),
            ("offhand", "Shield", new[] { FamilyPick("Shield") }),
        ], []);

        var recovered = Reconstruction.Reconstruct(combined.Query);

        recovered.Ok.Should().BeFalse();
        recovered.Diagnostics.Should().Contain(d => d.Contains("multi-class"));
    }

    [Fact]
    public void FreeText_FailsClosed()
    {
        var recovered = Reconstruction.Reconstruct("\"zzzz\"");

        recovered.Ok.Should().BeFalse();
        recovered.Diagnostics.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("Ring", "ring1", "ring2", true)]
    [InlineData("Crossbow", "weapon", "offhand", false)]
    [InlineData("Helmet", "helmet", "body", false)]
    [InlineData("Ring", "weapon", "offhand", false)]
    public void DuplicateClassRule_MatchesTheSlotPolicy(string classId, string slotA, string slotB, bool expected)
    {
        SlotClasses.AllowsDuplicateClass(classId, slotA, slotB).Should().Be(expected);
    }
}

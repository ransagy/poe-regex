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

    private static WantedMod FactoredRingNamePick()
    {
        var family = ModFamilies.Group(Engine.ModsForClasses(["Ring"]))
            .First(f => f.Key == "Strength" && f.Tiers.Count >= 2);
        var tier = family.Tiers.First(t => !string.IsNullOrWhiteSpace(t.Name));
        return new WantedMod(tier.Anchor.Id, Names: family.NamesFrom(tier));
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
    public void GroupClassPrefix_ResolvesAllClasses()
    {
        var imported = new QueryImportService(Engine).Import("\": (Cro|Fo|Gl)\"");
        imported.Classes.Should().BeEquivalentTo(["Crossbow", "Focus", "Gloves"]);
    }

    [Fact]
    public void DuplicateExclusions_RoundTripAndApply()
    {
        // Two distinct mods compile to the same `!"o Ar"`; both must survive.
        const string query =
            "\"\\) co.*e$\" \"Antelope's|Buck's|Caribou's|Deer's|Moose's\" \"\\+.*bo\" !\"o Ar\" !\"o Ar\"";

        var recovered = Reconstruction.ReconstructWorkbench(query);

        recovered.Ok.Should().BeTrue(recovered.Diagnostics.FirstOrDefault());
        recovered.Recompile(Engine).Should().Be(query);

        var state = new WorkbenchState();
        recovered.ApplyTo(state);
        state.Mode.Should().Be("simple");
        Engine.CompilePicks(state.Global.Included, state.Global.Excluded, state.Global.MatchAll, null)
            .Query.Should().Be(query);
    }

    [Fact]
    public void DuplicateAllOfGroups_RoundTripAndApply()
    {
        // Two distinct picks compile to the same `"gt"` group in one all-of slot.
        const string query = "\": Gl\" \"gt\" \",.+ld$\" \"ur .+y\" \"gt\" !\"m M\" !\"tn.+o\"";

        var recovered = Reconstruction.ReconstructWorkbench(query);

        recovered.Ok.Should().BeTrue(recovered.Diagnostics.FirstOrDefault());
        recovered.Slots.Should().ContainSingle();
        recovered.Slots[0].MatchAll.Should().BeTrue();
        recovered.Recompile(Engine).Should().Be(query);

        var state = new WorkbenchState();
        recovered.ApplyTo(state);
        var active = state.ActiveSlots();
        active.Should().ContainSingle();
        var crit = state.CriteriaFor(active[0]);
        Engine.CompilePicks(crit.Included, crit.Excluded, crit.MatchAll, crit.ClassId)
            .Query.Should().Be(query);
    }

    [Fact]
    public void SameLineDifferentFragments_RoundTripAndApply()
    {
        // Body Armour and Gloves both want "+ to Evasion Rating", but the app
        // compiled them with different per-class fragments.
        const string query =
            "\": (Bod|Cro|Fo|Gl)\" \": (Cro|Fo|Gl)|o Ev\" " +
            "\": (Bod|Fo|Gl)|nus|of (Light|Radiance|Shining)\" " +
            "\": (Bod|Cro|Gl)|of the Virtuoso\" \": (Bod|Cro|Gl)|o Ch\" " +
            "\": (Bod|Cro|Fo)|^\\+.*on\"";

        var recovered = Reconstruction.ReconstructWorkbench(query);

        recovered.Ok.Should().BeTrue(recovered.Diagnostics.FirstOrDefault());
        recovered.Recompile(Engine).Should().Be(query);

        var state = new WorkbenchState();
        recovered.ApplyTo(state);
        var active = state.ActiveSlots();
        var recompiled = Engine.Combine(
            active.Select(s => (s, state.CriteriaFor(s).ClassId,
                (IEnumerable<WantedMod>)state.CriteriaFor(s).Included,
                state.CriteriaFor(s).MatchAll)),
            active.SelectMany(s => state.CriteriaFor(s).Excluded).Distinct(StringComparer.Ordinal),
            allowOverflow: true).Query;
        recompiled.Should().Be(query);
    }

    [Fact]
    public void MixedGroup_NameSuffix_CollapsesToTierOrBetterPick()
    {
        // The six names are exactly the LocalFireDamage suffix from Flaming
        // up; the four fragments are separate picks in the same any-of group.
        const string query =
            "\"Blasting|Carbonising|Cremating|Flaming|Incinerating|Scorching|\\+.*ife|^\\+.*cc|o Int|o cr.*ha\"";

        var recovered = Reconstruction.ReconstructWorkbench(query);

        recovered.Ok.Should().BeTrue(recovered.Diagnostics.FirstOrDefault());
        recovered.Recompile(Engine).Should().Be(query);
        recovered.Slots.Should().ContainSingle();
        var namePicks = recovered.Slots[0].Includes.Where(p => p.IsNamePick).ToList();
        namePicks.Should().ContainSingle("the suffix becomes one tier-or-better pick");
        namePicks[0].Names.Should().Equal(
            "Flaming", "Scorching", "Incinerating", "Blasting", "Cremating", "Carbonising");

        var state = new WorkbenchState();
        recovered.ApplyTo(state);
        state.Global.Included.Should().HaveCount(5);
        state.Global.Included.Count(p => p.IsNamePick).Should().Be(1);
        Engine.CompilePicks(state.Global.Included, state.Global.Excluded, state.Global.MatchAll, null)
            .Query.Should().Be(query);
    }

    [Fact]
    public void MixedGroup_NonSuffixNames_StayIndividual()
    {
        // A subset of a family suffix is not "or better"; keep the two names
        // as their own picks so the search is not broadened.
        const string query = "\"Flaming|Scorching|\\+.*ife\"";

        var recovered = Reconstruction.ReconstructWorkbench(query);

        recovered.Ok.Should().BeTrue(recovered.Diagnostics.FirstOrDefault());
        recovered.Recompile(Engine).Should().Be(query);
        recovered.Slots.Should().ContainSingle();
        var namePicks = recovered.Slots[0].Includes.Where(p => p.IsNamePick).ToList();
        namePicks.Should().HaveCount(2);
        namePicks.SelectMany(p => p.Names!).Should().BeEquivalentTo(["Flaming", "Scorching"]);
    }

    [Fact]
    public void Simple_MixedNameAndFragmentGroup_RoundTrips()
    {
        // A real app string: six affix names plus four fragments in one group.
        const string query =
            "\"Blasting|Carbonising|Cremating|Flaming|Incinerating|Scorching|\\+.*ife|^\\+.*cc|o Int|o cr.*ha\"";

        var recovered = Reconstruction.ReconstructWorkbench(query);

        recovered.Ok.Should().BeTrue(recovered.Diagnostics.FirstOrDefault());
        recovered.Mode.Should().Be("simple");
        recovered.Recompile(Engine).Should().Be(query);
    }

    [Fact]
    public void AllOf_FamilyPick_StaysOnePick()
    {
        var compiled = Engine.CompilePicks(
            [FamilyPick("Ring"), new WantedMod("Strength1")], [], true, "Ring");

        var recovered = Reconstruction.ReconstructWorkbench(compiled.Query);

        recovered.Ok.Should().BeTrue(recovered.Diagnostics.FirstOrDefault());
        recovered.Slots.Should().ContainSingle();
        recovered.Slots[0].MatchAll.Should().BeTrue();
        recovered.Slots[0].Includes.Should().Contain(
            p => p.VariantModIds != null && p.VariantModIds.Count > 0,
            "the family's variants are ORed inside one all-of pick");
        recovered.Recompile(Engine).Should().Be(compiled.Query);
    }

    [Fact]
    public void Advanced_OnlyExclusion_RoundTripsAndApplies()
    {
        var compiled = Engine.CompilePicks([], ["Strength1"], false, "Ring");
        var recovered = Reconstruction.ReconstructWorkbench(compiled.Query);

        recovered.Ok.Should().BeTrue(recovered.Diagnostics.FirstOrDefault());
        recovered.Excludes.Should().ContainSingle();

        var state = new WorkbenchState();
        recovered.ApplyTo(state);
        state.Mode.Should().Be("advanced");
        state.ActiveSlots().Should().Contain("ring1");
        var crit = state.CriteriaFor("ring1");
        Engine.CompilePicks(crit.Included, crit.Excluded, crit.MatchAll, crit.ClassId)
            .Query.Should().Be(compiled.Query);
    }

    [Fact]
    public void Simple_OnlyExclusion_RoundTripsAndApplies()
    {
        var compiled = Engine.CompilePicks([], ["Strength1"], false, null);
        var recovered = Reconstruction.ReconstructWorkbench(compiled.Query);

        recovered.Ok.Should().BeTrue(recovered.Diagnostics.FirstOrDefault());

        var state = new WorkbenchState();
        recovered.ApplyTo(state);
        state.Mode.Should().Be("simple");
        Engine.CompilePicks(state.Global.Included, state.Global.Excluded, state.Global.MatchAll, null)
            .Query.Should().Be(compiled.Query);
    }

    [Fact]
    public void MultiClass_OnlyExclusion_RoundTripsAndApplies()
    {
        var combined = Engine.Combine(
        [
            ("weapon", "Crossbow", Array.Empty<WantedMod>()),
            ("offhand", "Shield", Array.Empty<WantedMod>()),
        ], ["Strength1"]);
        var recovered = Reconstruction.ReconstructWorkbench(combined.Query);

        recovered.Ok.Should().BeTrue(recovered.Diagnostics.FirstOrDefault());

        var state = new WorkbenchState();
        recovered.ApplyTo(state);
        var active = state.ActiveSlots();
        active.Should().HaveCount(2);
        var recompiled = Engine.Combine(
            active.Select(s => (s, state.CriteriaFor(s).ClassId,
                (IEnumerable<WantedMod>)state.CriteriaFor(s).Included,
                state.CriteriaFor(s).MatchAll)),
            active.SelectMany(s => state.CriteriaFor(s).Excluded).Distinct(StringComparer.Ordinal),
            allowOverflow: true).Query;
        recompiled.Should().Be(combined.Query);
    }

    [Fact]
    public void Advanced_FactoredNameSet_RoundTrips()
    {
        var compiled = Engine.CompilePicks([FactoredRingNamePick()], [], false, "Ring");
        compiled.Query.Should().Contain("(");

        var recovered = Reconstruction.ReconstructWorkbench(compiled.Query);

        recovered.Ok.Should().BeTrue(recovered.Diagnostics.FirstOrDefault());
        recovered.Recompile(Engine).Should().Be(compiled.Query);
    }

    [Fact]
    public void MultiClassPaste_RoundTripsAndApplies()
    {
        var combined = Engine.Combine(
        [
            ("weapon", "Crossbow", new[] { FamilyPick("Crossbow") }),
            ("offhand", "Shield", new[] { FamilyPick("Shield") }),
        ], []);

        var recovered = Reconstruction.ReconstructWorkbench(combined.Query);

        recovered.Ok.Should().BeTrue(recovered.Diagnostics.FirstOrDefault());
        recovered.Mode.Should().Be("advanced");
        recovered.Slots.Select(s => s.ClassId).Should().BeEquivalentTo(["Crossbow", "Shield"]);
        recovered.Recompile(Engine).Should().Be(combined.Query);

        var state = new WorkbenchState();
        recovered.ApplyTo(state);
        var active = state.ActiveSlots();
        active.Should().HaveCount(2);
        var recompiled = Engine.Combine(
            active.Select(s => (s, state.CriteriaFor(s).ClassId,
                (IEnumerable<WantedMod>)state.CriteriaFor(s).Included,
                state.CriteriaFor(s).MatchAll)),
            state.Global.Excluded, allowOverflow: true).Query;
        recompiled.Should().Be(combined.Query);
    }

    [Fact]
    public void MultiArmourPaste_RoundTrips()
    {
        var combined = Engine.Combine(
        [
            ("helmet", "Helmet", new[] { FamilyPick("Helmet") }),
            ("boots", "Boots", new[] { FamilyPick("Boots") }),
            ("gloves", "Gloves", new[] { FamilyPick("Gloves") }),
        ], []);

        var recovered = Reconstruction.ReconstructWorkbench(combined.Query);

        recovered.Ok.Should().BeTrue(recovered.Diagnostics.FirstOrDefault());
        recovered.Slots.Select(s => s.SlotId).Should().BeEquivalentTo(["helmet", "boots", "gloves"]);
        recovered.Recompile(Engine).Should().Be(combined.Query);
    }

    [Fact]
    public void Simple_Apply_RoundTrips()
    {
        var compiled = Engine.CompilePicks(
            [new WantedMod("IncreasedLife6"), new WantedMod("Strength1")], [], false, null);
        var recovered = Reconstruction.ReconstructWorkbench(compiled.Query);
        recovered.Ok.Should().BeTrue(recovered.Diagnostics.FirstOrDefault());

        var state = new WorkbenchState();
        recovered.ApplyTo(state);

        state.Mode.Should().Be("simple");
        Engine.CompilePicks(state.Global.Included, state.Global.Excluded, state.Global.MatchAll, null)
            .Query.Should().Be(compiled.Query);
    }

    [Fact]
    public void Advanced_Apply_RoundTrips()
    {
        var compiled = Engine.CompilePicks([new WantedMod("IncreasedLife6")], [], false, "Ring");
        var recovered = Reconstruction.ReconstructWorkbench(compiled.Query);
        recovered.Ok.Should().BeTrue(recovered.Diagnostics.FirstOrDefault());

        var state = new WorkbenchState();
        recovered.ApplyTo(state);

        state.Mode.Should().Be("advanced");
        state.ActiveSlots().Should().Contain("ring1");
        var crit = state.CriteriaFor("ring1");
        Engine.CompilePicks(crit.Included, crit.Excluded, crit.MatchAll, crit.ClassId)
            .Query.Should().Be(compiled.Query);
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
    public void FactoredNameSet_WithUnknownName_FailsClosed()
    {
        var compiled = Engine.CompilePicks(
            [new WantedMod("Strength1", Names: ["of the Bear", "of the Beast"])], [], false, null);
        compiled.Query.Should().Contain("(");

        var recovered = Reconstruction.Reconstruct(compiled.Query);

        recovered.Ok.Should().BeFalse();
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

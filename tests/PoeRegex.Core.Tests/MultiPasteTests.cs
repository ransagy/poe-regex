using PoeRegex.Core.Compilation;
using PoeRegex.Core.Data;

namespace PoeRegex.Core.Tests;

/// <summary>
/// Combined-paste tests over the real poe2 catalog: anchor + exclusion-group
/// structure, per-group recall validation, same-class merging, exclusion
/// union, determinism, and budget enforcement.
/// </summary>
public class MultiPasteTests
{
    private static EngineCorpus Corpus() => RealCatalog.Corpus();

    private static PasteComposer Composer(EngineCorpus corpus) =>
        new(corpus);

    [Fact]
    public void Combine_RingAmuletHelmet_StructureAndRecall()
    {
        var corpus = Corpus();
        var result = Composer(corpus).Compose(
        [
            new PasteSlotCriterion("Ring", [new ModifierRequirement("IncreasedLife6")], "weapon"),
            new PasteSlotCriterion("Amulet", [], "amulet"),
            new PasteSlotCriterion("Helmet", [new ModifierRequirement("Strength1")], "helmet"),
        ], []);
        // Anchor lists every selected class; the unmodded amulet gets no group.
        result.Groups.Should().HaveCount(2);
        result.Groups.Select(g => g.Label).Should().BeEquivalentTo(["helmet", "weapon"]);
        var ring = result.Groups.Single(g => g.Label == "weapon");
        ring.TargetClasses.Should().Equal(["Ring"]);
        ring.Token.Should().NotContain("Ri");
        ring.Token.Should().Contain("fe");
        // Recall + anchor validation are clean on real data.
        result.Diagnostics.Should().BeEmpty();
        // Deterministic.
        var again = Composer(corpus).Compose(
        [
            new PasteSlotCriterion("Ring", [new ModifierRequirement("IncreasedLife6")], "weapon"),
            new PasteSlotCriterion("Amulet", [], "amulet"),
            new PasteSlotCriterion("Helmet", [new ModifierRequirement("Strength1")], "helmet"),
        ], []);
        again.Query.Should().Be(result.Query);
    }

    [Fact]
    public void Combine_ClassPrefixes_AreOracleClean()
    {
        var corpus = Corpus();
        var composer = Composer(corpus);
        foreach (var cls in corpus.Catalog.ItemClasses.Where(c =>
                     corpus.Mods.Any(m => m.Classes.Contains(c.Id))))
        {
            var prefix = composer.ClassPrefix(cls.Id);
            prefix.Should().NotBeNullOrEmpty();
            cls.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase).Should().BeTrue();
        }
    }

    [Fact]
    public void Combine_SameClass_MergesIntoOneGroup()
    {
        var corpus = Corpus();
        var result = Composer(corpus).Compose(
        [
            new PasteSlotCriterion("Ring", [new ModifierRequirement("IncreasedLife6")], "ring1"),
            new PasteSlotCriterion("Ring", [new ModifierRequirement("Strength1")], "ring2"),
        ], []);
        result.Groups.Should().HaveCount(1);
        result.Groups[0].Label.Should().Contain("ring1").And.Contain("ring2");
        result.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void Combine_Exclusions_UnionGlobally()
    {
        var corpus = Corpus();
        var result = Composer(corpus).Compose(
        [
            new PasteSlotCriterion("Ring", [new ModifierRequirement("IncreasedLife6")], "weapon"),
        ],
        [new ModifierRequirement("reflect")]);
        result.Query.Should().Contain("!\"reflect\"");
    }

    [Fact]
    public void Combine_FoldsSharedClassSuffixes_InAnchorAndOthers()
    {
        var corpus = Corpus();
        // Free-text requirements keep the test independent of fragment
        // resolution: the groups still form and validate.
        var result = Composer(corpus).Compose(
        [
            new PasteSlotCriterion("Belt", [new ModifierRequirement("qqqq")], "belt"),
            new PasteSlotCriterion("One Hand Mace", [new ModifierRequirement("wwww")], "weapon"),
            new PasteSlotCriterion("Two Hand Mace", [new ModifierRequirement("wwww")], "offhand"),
        ], []);
        result.Anchor.Should().Be(": (Bel|.*Hand M)");
        var belt = result.Groups.Single(g => g.TargetClasses.SequenceEqual(["Belt"]));
        belt.Token.Should().StartWith(": (.*Hand M)|");
        var maces = result.Groups.Single(g => g.TargetClasses.SequenceEqual(["One Hand Mace", "Two Hand Mace"]));
        maces.Token.Should().StartWith(": (Bel)|");
        result.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void Combine_Overflow_ThrowsUnlessAllowed()
    {
        var corpus = Corpus();
        var many = new[] { "alpha", "bravo", "charlie", "delta", "echo", "foxtrot" }
            .Select(w => new ModifierRequirement(
                $"{w}-qqqqqqqqqq-unique-filler-text-padding-padding-padding-{w}"))
            .ToList();
        var act = () => Composer(corpus).Compose(
            [new PasteSlotCriterion("Ring", many, "weapon")], []);
        act.Should().Throw<RegexLengthOverflowException>();
        var loose = Composer(corpus).Compose(
            [new PasteSlotCriterion("Ring", many, "weapon")], [], allowOverflow: true);
        loose.Exceeded.Should().BeTrue();
    }
}

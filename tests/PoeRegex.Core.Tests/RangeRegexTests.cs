using PoeRegex.Core.Compilation;
using PoeRegex.Core.Data;

namespace PoeRegex.Core.Tests;

public class RangeRegexTests
{
    private static EngineCorpus Corpus() => RealCatalog.Corpus();

    private static string Fragment(
        EngineCorpus corpus, string itemClass, params string[] modIds)
    {
        var anchor = modIds[0];
        var result = new CompactionEngine(corpus).SelectFragment(
            new ModifierRequirement(anchor, RangeModIds: modIds), itemClass);
        result.Should().NotBeNullOrEmpty();
        return result!;
    }

    [Fact]
    public void IntegerCandidates_UseClassesAndRejectWorseValues()
    {
        var candidates = RangeRegex.Candidates([(20, 35)], 0);
        candidates.Should().Contain("[23]\\d");
        RangeRegex.MatchesIntervals("[23]\\d", [(20, 35)], 0).Should().BeTrue();
        RangeRegex.MatchesIntervals(
            "[23]\\d", [(20, 20), (25, 25), (30, 30), (35, 35)], 0).Should().BeTrue();
    }

    [Fact]
    public void ExactIntegerCandidates_UseTrieRangesAndSkipGaps()
    {
        var candidates = RangeRegex.ExactIntegerCandidates(
            [15, 20, 25, 30, 35]);

        candidates.Should().ContainSingle().Which.Should().Be("15|[23][05]");
        var regex = new System.Text.RegularExpressions.Regex(
            "^(?:" + candidates[0] + ")$");
        foreach (var value in new[] { 15, 20, 25, 30, 35 })
            regex.IsMatch(value.ToString()).Should().BeTrue();
        foreach (var value in new[] { 10, 16, 24, 36, 40 })
            regex.IsMatch(value.ToString()).Should().BeFalse();
    }

    [Fact]
    public void IntegerCandidates_CompressConsecutiveDigits()
    {
        var candidates = RangeRegex.Candidates([(120, 179)], 0);

        candidates.Should().Contain("1[2-7]\\d");
        RangeRegex.MatchesIntervals("1[2-7]\\d", [(120, 179)], 0).Should().BeTrue();
        System.Text.RegularExpressions.Regex.IsMatch(
            "119", "^(?:1[2-7]\\d)$").Should().BeFalse();
        System.Text.RegularExpressions.Regex.IsMatch(
            "180", "^(?:1[2-7]\\d)$").Should().BeFalse();
    }

    [Fact]
    public void IntegerCandidates_CrossDigitWidth_CombineWidths()
    {
        var candidates = RangeRegex.Candidates([(9, 12)], 0);
        var matching = candidates.Where(c => RangeRegex.MatchesIntervals(c, [(9, 12)], 0)).ToList();
        matching.Should().NotBeEmpty("a 9–12 span crosses the one/two-digit boundary");
        matching.Should().Contain(c =>
            System.Text.RegularExpressions.Regex.IsMatch("9", "^(?:" + c + ")$") &&
            System.Text.RegularExpressions.Regex.IsMatch("12", "^(?:" + c + ")$"));
    }

    [Fact]
    public void StrengthTwo_BoundedRange_MatchesOnlyTheRequestedRolls()
    {
        var corpus = Corpus();
        var mod = corpus.FindMod("Strength2")!;
        var itemClass = mod.Classes[0];
        var fragment = new CompactionEngine(corpus).SelectFragment(
            new ModifierRequirement("Strength2", 10, MaxValue: 12), itemClass);
        fragment.Should().NotBeNullOrEmpty();

        foreach (var line in new[]
                 {
                     "+10(9-12) to Strength",
                     "+12(9-12) to Strength",
                     "+10 to Strength",
                 })
        {
            System.Text.RegularExpressions.Regex.IsMatch(
                line, fragment!, System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                .Should().BeTrue(line);
        }
        System.Text.RegularExpressions.Regex.IsMatch(
            "+9(9-12) to Strength", fragment!,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase).Should().BeFalse();
    }

    [Fact]
    public void DecimalCandidates_MatchExtraDisplayedPrecision()
    {
        var candidates = RangeRegex.Candidates([(1.5, 2.5)], 1);
        var matching = candidates.Where(c => RangeRegex.MatchesIntervals(c, [(1.5, 2.5)], 1)).ToList();
        matching.Should().NotBeEmpty();
        matching.Should().Contain(c =>
            System.Text.RegularExpressions.Regex.IsMatch(
                "2.03", "^(?:" + c + ")$"),
            "a 1.5–2.5 stat can display a 2.03 roll");
    }

    [Fact]
    public void BootsMovementSpeed_TierRange_IsOneCombinedFragment()
    {
        var corpus = Corpus();
        var fragment = Fragment(
            corpus, "Boots",
            "MovementVelocity3", "MovementVelocity4", "MovementVelocity5", "MovementVelocity6");

        fragment.Should().Contain("eed$");
        System.Text.RegularExpressions.Regex.IsMatch(
            "20% increased Movement Speed", fragment,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase).Should().BeTrue();
        System.Text.RegularExpressions.Regex.IsMatch(
            "35% increased Movement Speed", fragment,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase).Should().BeTrue();
        System.Text.RegularExpressions.Regex.IsMatch(
            "15% increased Movement Speed", fragment,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase).Should().BeFalse();
        System.Text.RegularExpressions.Regex.IsMatch(
            "10% increased Movement Speed", fragment,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase).Should().BeFalse();
    }

    [Fact]
    public void BodyLife_TierRange_MatchesAdvancedRollsButNotWorseTiers()
    {
        var corpus = Corpus();
        var fragment = Fragment(
            corpus, "Body Armour",
            "IncreasedLife8", "IncreasedLife9", "IncreasedLife10",
            "IncreasedLife11", "IncreasedLife12", "IncreasedLife13");

        fragment.Should().Contain("fe");
        foreach (var line in new[]
                 {
                     "+100(100-119) to maximum Life",
                     "+119(100-119) to maximum Life",
                     "+174(150-174) to maximum Life",
                     "+214(200-214) to maximum Life",
                 })
        {
            System.Text.RegularExpressions.Regex.IsMatch(
                line, fragment, System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                .Should().BeTrue(line);
        }
        System.Text.RegularExpressions.Regex.IsMatch(
            "+99(85-99) to maximum Life", fragment,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase).Should().BeFalse();
    }

    [Fact]
    public void FlaskCharge_DecimalRange_MatchesFractionalTier()
    {
        var corpus = Corpus();
        var fragment = Fragment(
            corpus, "Amulet", "CorruptionLifeFlaskChargeGeneration1");
        foreach (var line in new[]
                 {
                     "Life Flasks gain 0.08(0.08-0.17) charges per Second",
                     "Life Flasks gain 0.12(0.08-0.17) charges per Second",
                     "Life Flasks gain 0.17(0.08-0.17) charges per Second",
                 })
        {
            System.Text.RegularExpressions.Regex.IsMatch(
                line, fragment, System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                .Should().BeTrue(line);
        }
    }

    [Fact]
    public void BodyLifeRegen_DecimalRange_MatchesRolledPrecision()
    {
        var corpus = Corpus();
        var fragment = Fragment(
            corpus, "Body Armour", "BodyArmourImplicitLifeRegenerationPercent1");

        System.Text.RegularExpressions.Regex.IsMatch(
            "Regenerate 2.03(1.5-2.5)% of maximum Life per second", fragment,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase).Should().BeTrue();
        System.Text.RegularExpressions.Regex.IsMatch(
            "Regenerate 1.5(1.5-2.5)% of maximum Life per second", fragment,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase).Should().BeTrue();
        System.Text.RegularExpressions.Regex.IsMatch(
            "Regenerate 2.5(1.5-2.5)% of maximum Life per second", fragment,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase).Should().BeTrue();
        System.Text.RegularExpressions.Regex.IsMatch(
            "Regenerate 0.5(1.5-2.5)% of maximum Life per second", fragment,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase).Should().BeFalse(
            "the range endpoint must not stand in for a worse roll");
    }
}

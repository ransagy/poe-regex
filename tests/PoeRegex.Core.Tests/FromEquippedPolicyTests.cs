using System.Text.RegularExpressions;
using AwesomeAssertions;
using PoeRegex.Core;
using PoeRegex.Core.Compilation;
using PoeRegex.Core.Explain;

namespace PoeRegex.Core.Tests;

/// <summary>
/// Policy guard for non-catalog "from Equipped" lines. Target membership is
/// strict: a same-stat source variant or a cross-stat line is foreign unless
/// the target pattern itself includes that source/condition.
/// </summary>
public class FromEquippedPolicyTests
{
    private static readonly EngineCorpus Corpus = RealCatalog.Corpus();

    private const string JewelShield =
        "18% increased Armour, Evasion and Energy Shield from Equipped Shield";
    private const string AmuletArmour =
        "35% increased Armour from Equipped Body Armour";
    private const string AmuletEvasion =
        "35% increased Evasion Rating from Equipped Body Armour";
    private const string AmuletEnergyShield =
        "35% increased Energy Shield from Equipped Body Armour";
    private const string BowQuiver =
        "30% increased bonuses gained from Equipped Quiver";

    [Fact]
    public void SameStatFromEquippedLines_AreForeign()
    {
        var armourHybrid =
            Corpus.PrecomputedGlobal["\u0001% increased Armour, Evasion and Energy Shield"];
        Matches(JewelShield, armourHybrid).Should().BeFalse(
            "the jewel source line is not the armour hybrid target");

        var amuletArmour = Corpus.PrecomputedForClass("Amulet")["\u0001% increased Armour"];
        Matches(AmuletArmour, amuletArmour).Should().BeFalse(
            "the desecrated source line is not the plain armour target");

        var amuletEvasion =
            Corpus.PrecomputedForClass("Amulet")["\u0001% increased Evasion Rating"];
        Matches(AmuletEvasion, amuletEvasion).Should().BeFalse(
            "the desecrated source line is not the plain evasion target");
    }

    [Fact]
    public void CrossStatCollisions_AreExcluded()
    {
        var amuletEvasion =
            Corpus.PrecomputedForClass("Amulet")["\u0001% increased Evasion Rating"];
        Matches("35(35-50)% increased Evasion Rating", amuletEvasion).Should().BeTrue();
        Matches(AmuletEnergyShield, amuletEvasion).Should().BeFalse(
            "increased Energy Shield must not answer an Evasion query");

        var bowCrit =
            Corpus.PrecomputedForClass("Bow")["+\u0001% to Critical Damage Bonus"];
        Matches("+15% to Critical Damage Bonus", bowCrit).Should().BeTrue();
        Matches(BowQuiver, bowCrit).Should().BeFalse(
            "increased bonuses gained must not answer a Critical Damage Bonus query");
    }

    [Fact]
    public void Explain_GlobalScope_ExcludesNotInGameClasses()
    {
        var explained = new Explainer(Corpus).Explain("\": Dag\"");
        explained.Tokens.Should().ContainSingle();
        explained.Tokens[0].Kind.Should().NotBe("class");
        explained.Tokens[0].Classes.Should().BeEmpty();
    }

    private static bool Matches(string line, string fragment) =>
        Regex.IsMatch(line, fragment, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}

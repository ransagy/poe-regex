using System.Text.RegularExpressions;
using AwesomeAssertions;
using PoeRegex.Core;
using PoeRegex.Core.Compilation;

namespace PoeRegex.Core.Tests;

/// <summary>
/// Policy guard for non-catalog "from Equipped" lines. Same-stat source
/// collisions are accepted; cross-stat collisions must be avoided by the
/// shortest fragment that is still clean for the catalog scope.
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
    public void AcceptedSameStatCollisions_AreRecorded()
    {
        Corpus.PrecomputedGlobal["\u0001% increased Armour, Evasion and Energy Shield"]
            .Should().Be("d Armour,");
        Matches(JewelShield,
            Corpus.PrecomputedGlobal["\u0001% increased Armour, Evasion and Energy Shield"])
            .Should().BeTrue();

        Corpus.PrecomputedForClass("Amulet")["\u0001% increased Armour"]
            .Should().Be("d Ar");
        Matches(AmuletArmour,
            Corpus.PrecomputedForClass("Amulet")["\u0001% increased Armour"])
            .Should().BeTrue();

        Corpus.PrecomputedForClass("Amulet")["\u0001% increased Evasion Rating"]
            .Should().Be("d Ev");
        Matches(AmuletEvasion,
            Corpus.PrecomputedForClass("Amulet")["\u0001% increased Evasion Rating"])
            .Should().BeTrue();
        Matches(AmuletEvasion,
            Corpus.PrecomputedGlobal["\u0001% increased Evasion Rating"])
            .Should().BeTrue("the global Evasion fragment still covers the same stat");
    }

    [Fact]
    public void CrossStatCollisions_AreExcluded()
    {
        var amuletEvasion = Corpus.PrecomputedForClass("Amulet")["\u0001% increased Evasion Rating"];
        amuletEvasion.Should().Be("d Ev");
        Matches(AmuletEnergyShield, amuletEvasion).Should().BeFalse(
            "increased Energy Shield must not answer an Evasion query");

        var bowCrit = Corpus.PrecomputedForClass("Bow")["+\u0001% to Critical Damage Bonus"];
        bowCrit.Should().Be("nus$");
        Matches("+15% to Critical Damage Bonus", bowCrit).Should().BeTrue();
        Matches(BowQuiver, bowCrit).Should().BeFalse(
            "increased bonuses gained must not answer a Critical Damage Bonus query");
    }

    private static bool Matches(string line, string fragment) =>
        Regex.IsMatch(line, fragment, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}

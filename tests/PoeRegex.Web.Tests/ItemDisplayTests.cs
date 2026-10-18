using PoeRegex.Web.Core;

namespace PoeRegex.Web.Tests;

/// <summary>
/// Canonical item-display order: the game lists implicits first, then all
/// prefixes, then all suffixes; the sort must be stable.
/// </summary>
public class ItemDisplayTests
{
    private static readonly EngineService Engine = new();

    [Fact]
    public void Rank_OrdersImplicitPrefixSuffix_UnknownLast()
    {
        ItemDisplay.Rank("implicit").Should().Be(0);
        ItemDisplay.Rank("Prefix").Should().Be(1);
        ItemDisplay.Rank("suffix").Should().Be(2);
        ItemDisplay.Rank("explicit").Should().Be(3);
        ItemDisplay.Rank(null).Should().Be(3);
    }

    [Fact]
    public void OrderModIds_PrefixesBeforeSuffixes_StableWithinKind()
    {
        var ordered = ItemDisplay.OrderModIds(Engine,
        [
            "Strength4",                    // suffix
            "IncreasedLife2",               // prefix
            "IncreasedMana2",               // prefix
            "BeltFlaskManaRecoveryRate1_",  // prefix
        ]);
        ordered.Should().Equal(
            "IncreasedLife2", "IncreasedMana2", "BeltFlaskManaRecoveryRate1_", "Strength4");
    }

    [Fact]
    public void OrderModIds_UnknownIdsLast()
    {
        ItemDisplay.OrderModIds(Engine, ["not-a-mod", "Strength4"])
            .Should().Equal("Strength4", "not-a-mod");
    }
}

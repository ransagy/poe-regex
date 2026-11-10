using PoeRegex.Web.Core;

namespace PoeRegex.Web.Tests;

/// <summary>
/// Roll alternatives share one stat phrase; numeric tokens stay whole so a
/// tier never shows a truncated range or repeats the stat like a hybrid mod.
/// </summary>
public class CompactionTests
{
    [Fact]
    public void CompactAlternatives_KeepsRangeTokensWhole()
    {
        ModFamilies.CompactAlternatives([
            "10–19% increased Mana Regeneration Rate",
            "15–29% increased Mana Regeneration Rate"
        ]).Should().Be("10–19 / 15–29% increased Mana Regeneration Rate");
    }

    [Fact]
    public void CompactAlternatives_RepeatsNumericPrefix()
    {
        ModFamilies.CompactAlternatives([
            "10–15% chance for Flasks you use to not consume Charges",
            "10–18% chance for Flasks you use to not consume Charges"
        ]).Should().Be("10–15 / 10–18% chance for Flasks you use to not consume Charges");
    }

    [Fact]
    public void CompactAlternatives_KeepsLongTextPrefixOnce()
    {
        ModFamilies.CompactAlternatives([
            "Break Armour equal to 2–4% of Physical Damage dealt",
            "Break Armour equal to 4–7% of Physical Damage dealt"
        ]).Should().Be("Break Armour equal to 2–4 / 4–7% of Physical Damage dealt");
    }

    [Fact]
    public void CompactAlternatives_KeepsShortPrefixPerAlternative()
    {
        ModFamilies.CompactAlternatives([
            "+1 to Level of all Projectile Skills",
            "+2 to Level of all Projectile Skills"
        ]).Should().Be("+1 / +2 to Level of all Projectile Skills");
    }

    [Fact]
    public void CompactAlternatives_AllowsEmptyPrefix()
    {
        ModFamilies.CompactAlternatives([
            "20–29% increased Mana Regeneration Rate",
            "30–44% increased Mana Regeneration Rate"
        ]).Should().Be("20–29 / 30–44% increased Mana Regeneration Rate");
    }

    [Fact]
    public void CompactAlternatives_DoesNotShareASplitNumber()
    {
        ModFamilies.CompactAlternatives([
            "5% increased Light Radius",
            "50% increased Light Radius"
        ]).Should().BeNull();
    }
}

using PoeRegex.Web.Core;

namespace PoeRegex.Web.Tests;

/// <summary>
/// The pre-typing shortlist is contributor data, not hardcoded family keys.
/// Resolve every entry against the shipped catalog and keep the new Helmet /
/// Gloves goals visible without typing.
/// </summary>
public class CommonPickTests
{
    private static readonly EngineService Engine = new();

    [Fact]
    public void CommonPicks_AllEntriesResolveInTheCatalog()
    {
        var families = ModFamilies.Group(Engine.PlayableMods());
        foreach (var entry in CommonPicks.Entries)
        {
            entry.Label.Should().NotBeNullOrWhiteSpace();
            (entry.Family is not null || entry.Match is not null).Should().BeTrue(
                $"common pick '{entry.Label}' needs a family key or visible match");

            if (entry.Family is { } family)
            {
                families.Should().Contain(f =>
                        f.Key == family || f.Members.Any(m => ModFamilies.KeyOf(m) == family),
                    $"common pick '{entry.Label}' family '{family}'");
            }
            else
            {
                families.Should().Contain(f => f.DisplayText == entry.Match,
                    $"common pick '{entry.Label}' match '{entry.Match}'");
            }
        }
    }

    [Fact]
    public void CommonShortlist_AddsMinionLevelsOnHelmetAndAttackDamageOnGloves()
    {
        var helmet = ModFamilies.Group(Engine.ModsForClass("Helmet"));
        helmet.Should().Contain(f =>
            f.Key == "GlobalIncreaseMinionSpellSkillGemLevel" &&
            ModFamilies.IsCommon(f));

        var gloves = ModFamilies.Group(Engine.ModsForClass("Gloves"));
        foreach (var key in new[]
                 {
                     "PhysicalDamage", "FireDamage", "ColdDamage", "LightningDamage",
                 })
        {
            gloves.Should().Contain(f => f.Key == key && ModFamilies.IsCommon(f),
                $"gloves common pick '{key}'");
        }
    }

    [Fact]
    public void EveryMatchText_ResolvesAtLeastOneFamily()
    {
        var families = ModFamilies.Group(Engine.PlayableMods());
        foreach (var entry in CommonPicks.Entries.Where(e => e.Match is not null))
        {
            families.Should().Contain(f => f.DisplayText == entry.Match,
                $"common pick '{entry.Label}' match '{entry.Match}'");
        }
    }
}

using PoeRegex.Web.Core;

namespace PoeRegex.Web.Tests;

/// <summary>
/// The pre-typing shortlist is contributor data, not hardcoded family keys.
/// Class-scoped entries apply only in their class views; the All-gear view
/// keeps the global picks, including the normal weapon-local damage families.
/// </summary>
public class CommonPickTests
{
    private static readonly EngineService Engine = new();
    private static readonly string[] LocalDamageKeys =
    [
        "LocalPhysicalDamage", "LocalFireDamage", "LocalColdDamage", "LocalLightningDamage",
    ];
    private static readonly string[] AttackDamageKeys =
    [
        "PhysicalDamage", "FireDamage", "ColdDamage", "LightningDamage",
    ];

    [Fact]
    public void CommonPicks_AllEntriesResolveInTheirScopes()
    {
        foreach (var entry in CommonPicks.Entries)
        {
            entry.Label.Should().NotBeNullOrWhiteSpace();
            (entry.Family is not null || entry.Match is not null).Should().BeTrue(
                $"common pick '{entry.Label}' needs a family key or visible match");

            IEnumerable<string?> scopes = entry.Classes.Count == 0
                ? new string?[] { null }
                : entry.Classes.Cast<string?>();
            foreach (var cls in scopes)
            {
                var families = ModFamilies.Group(
                    cls is null ? Engine.PlayableMods() : Engine.ModsForClass(cls));
                var resolved = families.Where(f =>
                        (entry.Family is { } family &&
                         (f.Key == family ||
                          f.Members.Any(m => ModFamilies.KeyOf(m) == family))) ||
                        (entry.Match is { } match && f.DisplayText == match))
                    .ToList();
                resolved.Should().NotBeEmpty(
                    $"common pick '{entry.Label}' in scope '{cls ?? "All gear"}'");
                resolved.Should().Contain(f => ModFamilies.IsCommon(f, cls),
                    $"common pick '{entry.Label}' must be common in its scope");
            }
        }
    }

    [Fact]
    public void CommonShortlist_ScopesMinionLevelsToHelmetAndAttackDamageToGloves()
    {
        var helmet = ModFamilies.Group(Engine.ModsForClass("Helmet"));
        var minion = helmet.Single(f => f.Key == "GlobalIncreaseMinionSpellSkillGemLevel");
        ModFamilies.IsCommon(minion, "Helmet").Should().BeTrue();
        ModFamilies.IsCommon(minion, null).Should().BeFalse(
            "a Helmet-scoped pick stays out of the All-gear shortlist");

        var gloves = ModFamilies.Group(Engine.ModsForClass("Gloves"));
        foreach (var key in AttackDamageKeys)
        {
            var family = gloves.Single(f => f.Key == key);
            ModFamilies.IsCommon(family, "Gloves").Should().BeTrue();
            ModFamilies.IsCommon(family, null).Should().BeFalse(
                "attack-only glove picks stay out of the All-gear shortlist");
        }
    }

    [Fact]
    public void GlobalCommonPicks_UseNormalWeaponDamageNotAttackOnlyVariants()
    {
        var global = ModFamilies.Group(Engine.PlayableMods());
        foreach (var key in LocalDamageKeys)
        {
            global.Should().Contain(f => f.Key == key && ModFamilies.IsCommon(f, null),
                $"normal local damage '{key}' stays in the All-gear shortlist");
        }
        foreach (var key in AttackDamageKeys)
        {
            global.Should().Contain(f => f.Key == key);
            global.Should().NotContain(f => f.Key == key && ModFamilies.IsCommon(f, null),
                $"attack-only '{key}' is a Gloves pick, not an All-gear pick");
        }

        var weapon = ModFamilies.Group(Engine.ModsForClass("One Hand Mace"));
        foreach (var key in LocalDamageKeys)
        {
            weapon.Should().Contain(f => f.Key == key &&
                ModFamilies.IsCommon(f, "One Hand Mace"),
                $"weapon common pick '{key}'");
        }
    }

    [Fact]
    public void EveryMatchText_ResolvesAtLeastOneFamilyInItsScope()
    {
        foreach (var entry in CommonPicks.Entries.Where(e => e.Match is not null))
        {
            IEnumerable<string?> scopes = entry.Classes.Count == 0
                ? new string?[] { null }
                : entry.Classes.Cast<string?>();
            foreach (var cls in scopes)
            {
                var families = ModFamilies.Group(
                    cls is null ? Engine.PlayableMods() : Engine.ModsForClass(cls));
                families.Should().Contain(f => f.DisplayText == entry.Match,
                    $"common pick '{entry.Label}' match '{entry.Match}' in '{cls ?? "All gear"}'");
            }
        }
    }
}

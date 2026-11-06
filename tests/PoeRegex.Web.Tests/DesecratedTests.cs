using PoeRegex.Core.Data;
using PoeRegex.Web.Core;

namespace PoeRegex.Web.Tests;

/// <summary>
/// Abyss desecrated mods are item prefixes/suffixes with their own source.
/// They share the picker/corpus path with explicit affixes but must never
/// merge into a normal family with the same raw family key or text.
/// </summary>
public class DesecratedTests
{
    private static readonly EngineService Engine = new();

    [Fact]
    public void Catalog_HasItemDesecratedModsWithClasses()
    {
        var desecrated = Engine.PlayableMods()
            .Where(m => m.Source == "desecrated")
            .ToList();

        desecrated.Should().HaveCount(197);
        desecrated.Should().OnlyContain(m =>
            m.GenerationType == "prefix" || m.GenerationType == "suffix");
        desecrated.Should().OnlyContain(m => m.Classes.Count > 0);

        var helmet = desecrated.Single(m => m.Id == "AbyssModHelmUlamanSuffixMarkedEnemyTakeIncreasedDamage");
        helmet.Classes.Should().Equal(["Helmet"]);
        helmet.GenerationType.Should().Be("suffix");
        helmet.StatLines.Should().ContainSingle();
    }

    [Fact]
    public void Families_KeepDesecratedRowsSeparate()
    {
        var families = ModFamilies.Group(Engine.ModsForClass("Helmet"));
        var normal = families.Single(f => f.Key == "IncreasedLife");
        var desecrated = families.Single(f =>
            f.Key == "desecrated:MarkedEnemyTakesIncreasedDamage");

        normal.Source.Should().BeEmpty();
        normal.IsDesecrated.Should().BeFalse();
        desecrated.Source.Should().Be("desecrated");
        desecrated.IsDesecrated.Should().BeTrue();
        desecrated.IsPrefix.Should().BeFalse();
        desecrated.DisplayText.Should().Contain("Mark");
    }

    [Fact]
    public void Compile_DesecratedHelmetPick_ProducesExactFragment()
    {
        var mod = Engine.ModsForClass("Helmet")
            .Single(m => m.Id == "AbyssModHelmUlamanSuffixMarkedEnemyTakeIncreasedDamage");

        var compiled = Engine.CompilePicks(
            [new WantedMod(mod.Id)], [], matchAll: false, itemClass: "Helmet");

        compiled.Query.Should().NotBeNullOrWhiteSpace();
        compiled.Exceeded.Should().BeFalse();
        var line = AdvancedStatLine.For(mod.StatLines[0]);
        ItemMatcher.Match(compiled.Query, ["Item Class: Helmets", line])
            .Match.Should().BeTrue();
    }
}

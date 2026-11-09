using Bunit;
using Microsoft.Extensions.DependencyInjection;
using PoeRegex.Core;
using PoeRegex.Core.Data;
using PoeRegex.Web.Core;
using web.Components;
using web.Services;

namespace PoeRegex.Web.Tests;

/// <summary>
/// Basic-rune augments are a separate non-affix source. The export's target
/// category decides which item classes can carry the effect; tier rows are
/// display-only in this first pass and the family + selects the effect across
/// every tier.
/// </summary>
public class AugmentTests
{
    private static readonly EngineService Engine = new();

    [Fact]
    public void Catalog_HasBasicRuneAugmentsWithTargetScopes()
    {
        var augments = Engine.PlayableMods()
            .Where(m => m.Source == "augment")
            .ToList();

        augments.Should().HaveCount(134);
        augments.Should().OnlyContain(m =>
            m.GenerationType == "augment" &&
            m.AugmentType == "Rune" &&
            m.Classes.Count > 0 &&
            m.StatLines.Count > 0);
        augments.Select(m => m.AugmentTarget).Distinct(StringComparer.Ordinal)
            .Should().BeEquivalentTo(["Armour", "Martial Weapon", "Wand or Staff"]);
        augments.Should().NotContain(m => m.Id.EndsWith(":all", StringComparison.Ordinal),
            "attribute runes use the All Equipment target and are deferred");
    }

    [Fact]
    public void Augments_AreNotInTheDefaultCommonShortlist()
    {
        using var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton(Engine);
        ctx.Services.AddSingleton(new PersistenceService(ctx.JSInterop.JSRuntime));
        ctx.Services.AddSingleton(new PreferencesState(ctx.JSInterop.JSRuntime));
        var state = new WorkbenchState();
        var mods = Engine.ModsForClass("Helmet");

        var cut = ctx.Render<ModPicker>(p => p
            .Add(x => x.State, state)
            .Add(x => x.Mods, mods));

        cut.FindAll(".mod-sec.augment-sec").Should().BeEmpty(
            "basic runes are a separate source, not generic common picks");
        cut.Find(".mod-scope").TextContent.Should().Contain("Common picks");

        cut.Find("#mod-search").Input("iron");
        cut.FindAll(".mod-sec.augment-sec").Should().ContainSingle();
    }

    [Fact]
    public void LesserDesertRune_HasCorrectTargetScopes()
    {
        var armour = Engine.FindMod("augment:RuneFireLesser:armour")!;
        armour.StatLines.Should().ContainSingle()
            .Which.Line.Should().Be("+10% to Fire Resistance");
        armour.Classes.Should().Contain(["Body Armour", "Helmet", "Gloves", "Boots", "Shield", "Buckler", "Focus"]);
        armour.Classes.Should().NotContain("Bow");

        var martial = Engine.FindMod("augment:RuneFireLesser:martial-weapon")!;
        martial.StatLines.Should().ContainSingle()
            .Which.Line.Should().Be("Adds 4 to 6 Fire Damage");
        martial.Classes.Should().Contain(["Bow", "Crossbow", "One Hand Mace", "Two Hand Mace", "Warstaff", "Spear", "Talisman"]);
        martial.Classes.Should().NotContain(["Wand", "Staff"]);

        var caster = Engine.FindMod("augment:RuneFireLesser:wand-or-staff")!;
        caster.StatLines.Should().ContainSingle()
            .Which.Line.Should().Be("Gain 6% of Damage as Extra Fire Damage");
        caster.Classes.Should().BeEquivalentTo(["Wand", "Staff"]);
    }

    [Fact]
    public void AugmentFamily_IsItsOwnSourceAndGroupsRuneTiers()
    {
        var family = ModFamilies.Group(Engine.ModsForClass("Helmet"))
            .Single(f => f.Key == "augment:rune:Iron Rune:armour");

        family.IsAugment.Should().BeTrue();
        family.Source.Should().Be("augment");
        family.Kind.Should().Be("augment");
        family.SourceTag.Should().Be("Rune · Armour");
        family.SourceCssClass.Should().Be("source-augment");
        family.Tiers.Should().HaveCount(4);
        family.Tiers.Select(t => t.DisplayLabel).Should().Equal(
            "Lesser Iron Rune", "Iron Rune", "Greater Iron Rune", "Perfect Iron Rune");
        family.Tiers.Select(t => t.RequiredLevel).Should().BeInAscendingOrder();

        var compiled = Engine.CompilePicks([family.Pick()], [], false, "Helmet");
        compiled.Query.Should().NotBeNullOrWhiteSpace();
        foreach (var tier in family.Tiers)
        {
            var line = AdvancedStatLine.For(tier.Anchor.StatLines[0]);
            ItemMatcher.Match(compiled.Query, ["Item Class: Helmets", line]).Match
                .Should().BeTrue($"the family pick should match every rune tier: {line}");
        }
    }

    [Fact]
    public void AugmentTierRows_HaveNoExactTierControls()
    {
        using var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton(Engine);
        ctx.Services.AddSingleton(new PersistenceService(ctx.JSInterop.JSRuntime));
        ctx.Services.AddSingleton(new PreferencesState(ctx.JSInterop.JSRuntime));
        var state = new WorkbenchState();
        state.SetMode("advanced");
        state.SelectSlot("helmet");
        var mods = Engine.ModsForClass("Helmet");
        var family = ModFamilies.Group(mods)
            .Single(f => f.Key == "augment:rune:Iron Rune:armour");

        var cut = ctx.Render<ModPicker>(p => p
            .Add(x => x.State, state)
            .Add(x => x.Scope, "helmet")
            .Add(x => x.Mods, mods));

        cut.Find("#mod-search").Input("iron");
        cut.Find($"[id='fam-toggle-{family.Anchor.Id}']").Click();
        cut.FindAll($"[id='pick-tier-{family.Tiers[0].Anchor.Id}']").Should().BeEmpty(
            "basic-rune tiers are display-only; the family + selects the effect");
        cut.FindAll($"[id='pick-tier-exc-{family.Tiers[0].Anchor.Id}']").Should().BeEmpty();

        cut.Find($"[id='pick-inc-{family.Anchor.Id}']").Click();
        cut.Find($"[id='pick-inc-{family.Anchor.Id}']").GetAttribute("aria-pressed").Should().Be("true");
        state.CriteriaFor("helmet").Included.Should().ContainSingle();
    }
}

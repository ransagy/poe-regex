using Bunit;
using Microsoft.Extensions.DependencyInjection;
using PoeRegex.Core;
using PoeRegex.Core.Compilation;
using PoeRegex.Core.Data;
using PoeRegex.Web.Core;
using web.Components;
using web.Services;

namespace PoeRegex.Web.Tests;

/// <summary>
/// Claim-covered selection: the compiled query is the identity. Same-query
/// mods and tiers share one claim; changing the claim switches the pick label
/// without adding a second pick. Different queries remain separate.
/// </summary>
public class ClaimTests
{
    private static readonly EngineService Engine = new();

    private static ModFamily Family(IReadOnlyList<ModEntry> mods, string key) =>
        ModFamilies.Group(mods).First(f => f.Key == key);

    private static BunitContext Context()
    {
        var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton(Engine);
        ctx.Services.AddSingleton(new PersistenceService(ctx.JSInterop.JSRuntime));
        ctx.Services.AddSingleton(new PreferencesState(ctx.JSInterop.JSRuntime));
        return ctx;
    }

    [Fact]
    public void PickClaimKey_IsPerScopeAndCanonical()
    {
        const string cls = "One Hand Mace";
        var mods = Engine.ModsForClass(cls);
        var pure = Family(mods, "LocalPhysicalDamagePercent");
        var kurgal = Family(mods, "desecrated:LocalIncreasedPhysicalDamageAttackSpeedHybrid");
        Engine.PickClaimKey(pure.Pick(), cls)
            .Should().Be(Engine.PickClaimKey(kurgal.Pick(), cls));
        Engine.PickClaimKey(pure.Pick(), null)
            .Should().NotBe(Engine.PickClaimKey(kurgal.Pick(), null));

        var gloves = Engine.ModsForClass("Gloves");
        var a = gloves.First(m => m.Id == "HandWrapsImplicitLocalBaseEvasionAndEnergyShieldPerLevel");
        var b = gloves.First(m => m.Id == "HandWrapsImplicitLocalBaseEvasionEnergyShieldAndWardPerLevel");
        var aFamily = ModFamilies.IndexOf(ModFamilies.Group(gloves)).GetValueOrDefault(a.Id)!;
        var bFamily = ModFamilies.IndexOf(ModFamilies.Group(gloves)).GetValueOrDefault(b.Id)!;
        Engine.PickClaimKey(aFamily.Pick(), "Gloves")
            .Should().Be(Engine.PickClaimKey(bFamily.Pick(), "Gloves"));
        Engine.PickClaimKey(aFamily.Pick(), null)
            .Should().Be(Engine.PickClaimKey(bFamily.Pick(), null));

        var global = Engine.PlayableMods();
        var mana = ModFamilies.Group(global).Single(f => f.Key == "ManaRegeneration");
        var desecrated = ModFamilies.Group(global)
            .Single(f => f.Key == "desecrated:ManaRegeneration");
        Engine.PickClaimKey(mana.Pick(), null)
            .Should().Be(Engine.PickClaimKey(desecrated.Pick(), null));
    }

    [Fact]
    public void Normalize_KeepsOneIncludeAndOneExclusionPerClaim()
    {
        var state = new WorkbenchState();
        var mods = Engine.ModsForClass("One Hand Mace");
        var pure = Family(mods, "LocalPhysicalDamagePercent");
        var kurgal = Family(mods, "desecrated:LocalIncreasedPhysicalDamageAttackSpeedHybrid");
        state.AddIncludes("weapon", [pure.Pick(), kurgal.Pick()]);
        state.AddExclude("weapon", pure.Anchor.Id);

        var crit = state.CriteriaFor("weapon");
        PickClaims.Normalize(crit, Engine, "One Hand Mace");

        crit.Included.Should().ContainSingle();
        crit.Excluded.Should().BeEmpty(
            "the include wins when legacy state contains both sides of one claim");
    }

    [Fact]
    public void Compiler_DedupesExclusionFragments()
    {
        var result = Engine.CompilePicks(
            [],
            [
                "HandWrapsImplicitLocalBaseEvasionAndEnergyShieldPerLevel",
                "HandWrapsImplicitLocalBaseEvasionEnergyShieldAndWardPerLevel"
            ],
            matchAll: false,
            itemClass: "Gloves");

        result.Query.Split("!\"g p\"", StringSplitOptions.None).Length.Should().Be(2,
            "both implicit exclusions resolve to one fragment");
    }

    [Fact]
    public void Compiler_ReportsContradictoryIncludeAndExclusion()
    {
        var result = Engine.Compile(
            [new ModifierRequirement("IncreasedLife1")],
            [new ModifierRequirement("IncreasedLife1")],
            itemClass: "Body Armour",
            allowOverflow: true);

        result.Diagnostics.Should().Contain(d => d.Code == "contradictory-filter");
    }

    [Fact]
    public void Picker_ClaimSwitch_ReplacesPickInsteadOfAdding()
    {
        using var ctx = Context();
        var mods = Engine.ModsForClass("One Hand Mace");
        var pure = Family(mods, "LocalPhysicalDamagePercent");
        var kurgal = Family(mods, "desecrated:LocalIncreasedPhysicalDamageAttackSpeedHybrid");
        var state = new WorkbenchState();
        state.SetMode("advanced");
        state.SelectSlot("weapon");
        state.SetSlotClass("weapon", "One Hand Mace");

        var cut = ctx.Render<ModPicker>(p => p
            .Add(x => x.State, state)
            .Add(x => x.Scope, "weapon")
            .Add(x => x.Mods, mods));
        cut.Find("#mod-search").Input("physical");

        cut.Find($"[id='pick-inc-{pure.Anchor.Id}']").Click();
        state.CriteriaFor("weapon").Included.Should().ContainSingle();
        cut.FindAll(".covered-note").Should().Contain(e => e.TextContent.Contains("covered"));
        cut.Find($"[id='pick-inc-{pure.Anchor.Id}']").TextContent.Should().Be("✓");
        cut.Find($"[id='pick-inc-{kurgal.Anchor.Id}']").TextContent.Should().Be("⇄");

        cut.Find($"[id='pick-inc-{kurgal.Anchor.Id}']").Click();
        state.CriteriaFor("weapon").Included.Should().ContainSingle();
        state.CriteriaFor("weapon").Included[0].ModId.Should().Be(kurgal.Anchor.Id);
        cut.Find($"[id='pick-inc-{kurgal.Anchor.Id}']").TextContent.Should().Be("✓");
        cut.Find($"[id='pick-inc-{pure.Anchor.Id}']").TextContent.Should().Be("⇄");
    }

    [Fact]
    public void Picker_HelpTip_ExplainsModTypesAndClaims()
    {
        using var ctx = Context();
        var mods = Engine.ModsForClass("Body Armour");
        var state = new WorkbenchState();
        state.SetMode("advanced");
        state.SelectSlot("body");

        var cut = ctx.Render<ModPicker>(p => p
            .Add(x => x.State, state)
            .Add(x => x.Scope, "body")
            .Add(x => x.Mods, mods));

        cut.FindAll(".mod-shape").Should().BeEmpty();
        cut.FindAll(".glyph-legend").Should().BeEmpty(
            "the legend stays out of the quiet initial state");
        cut.Find(".help").ClassList.Should().Contain("help-fixed");
        cut.Find(".help-btn").Click();
        var help = cut.Find(".help-pop").TextContent;
        help.Should().Contain("selected (✓)").And.Contain("covered")
            .And.Contain("Duplicate mods").And.NotContain("augment");
    }

    [Fact]
    public void Picker_LegendAppearsWhenClaimStateExists()
    {
        using var ctx = Context();
        var mods = Engine.ModsForClass("Body Armour");
        var life = Family(mods, "IncreasedLife");
        var state = new WorkbenchState();
        state.SetMode("advanced");
        state.SelectSlot("body");

        var cut = ctx.Render<ModPicker>(p => p
            .Add(x => x.State, state)
            .Add(x => x.Scope, "body")
            .Add(x => x.Mods, mods));
        cut.FindAll(".glyph-legend").Should().BeEmpty();
        cut.Find($"[id='pick-inc-{life.Anchor.Id}']").Click();
        var legend = cut.Find(".glyph-legend").TextContent;
        legend.Should().Contain("Add").And.Contain("Selected").And.Contain("Switch")
            .And.Contain("Exclude").And.Contain("Excluded").And.Contain("Better")
            .And.Contain("covered");
    }

    [Fact]
    public void Picker_CoveredRow_ActionMentionsSwitchingTheClaim()
    {
        using var ctx = Context();
        var mods = Engine.ModsForClass("One Hand Mace");
        var pure = Family(mods, "LocalPhysicalDamagePercent");
        var kurgal = Family(mods, "desecrated:LocalIncreasedPhysicalDamageAttackSpeedHybrid");
        var state = new WorkbenchState();
        state.SetMode("advanced");
        state.SelectSlot("weapon");
        state.SetSlotClass("weapon", "One Hand Mace");

        var cut = ctx.Render<ModPicker>(p => p
            .Add(x => x.State, state)
            .Add(x => x.Scope, "weapon")
            .Add(x => x.Mods, mods));
        cut.Find("#mod-search").Input("physical");
        cut.Find($"[id='pick-inc-{pure.Anchor.Id}']").Click();

        var covered = cut.Find($"[id='pick-inc-{kurgal.Anchor.Id}']");
        covered.GetAttribute("title")!.Should().Contain("Use ⇄ to switch");
        covered.ParentElement!.QuerySelector(".covered-note")!.TextContent.Should().Be("covered");
    }

    [Fact]
    public void TierClaimKeys_ShareOneClaimAcrossFamilies()
    {
        var mods = Engine.PlayableMods();
        var tiers = ModFamilies.Group(mods)
            .SelectMany(f => f.Tiers.Select(t => (Family: f, Tier: t)))
            .Where(x => x.Tier.Name == "Glinting")
            .Select(x => new WantedMod(x.Tier.Anchor.Id, [x.Tier.Name]))
            .ToList();
        tiers.Should().HaveCountGreaterThan(1);
        tiers.Select(p => Engine.PickClaimKey(p, null)).Distinct().Should().ContainSingle();

        var state = new WorkbenchState();
        state.AddIncludes(null, tiers);
        PickClaims.Normalize(state.Global, Engine, null);
        state.Global.Included.Should().ContainSingle();
    }

    [Fact]
    public void Picker_ImplicitVariants_SwitchClaimInsteadOfAdding()
    {
        using var ctx = Context();
        var mods = Engine.ModsForClass("Gloves");
        var a = mods.Single(m => m.Id == "HandWrapsImplicitLocalBaseEvasionAndEnergyShieldPerLevel");
        var b = mods.Single(m => m.Id == "HandWrapsImplicitLocalBaseEvasionEnergyShieldAndWardPerLevel");
        var state = new WorkbenchState();
        state.SetMode("advanced");
        state.SelectSlot("gloves");
        state.SetSlotClass("gloves", "Gloves");

        var cut = ctx.Render<ModPicker>(p => p
            .Add(x => x.State, state)
            .Add(x => x.Scope, "gloves")
            .Add(x => x.Mods, mods));
        cut.Find("#mod-search").Input("evasion");

        cut.Find($"[id='pick-inc-{a.Id}']").Click();
        cut.Find($"[id='pick-inc-{b.Id}']").Click();
        state.CriteriaFor("gloves").Included.Should().ContainSingle();
        state.CriteriaFor("gloves").Included[0].ModId.Should().Be(b.Id);
    }

    [Fact]
    public void Picker_IncludeAndExcludeOnSameClaim_AreMutuallyExclusive()
    {
        using var ctx = Context();
        var mods = Engine.ModsForClass("One Hand Mace");
        var pure = Family(mods, "LocalPhysicalDamagePercent");
        var state = new WorkbenchState();
        state.SetMode("advanced");
        state.SelectSlot("weapon");
        state.SetSlotClass("weapon", "One Hand Mace");

        var cut = ctx.Render<ModPicker>(p => p
            .Add(x => x.State, state)
            .Add(x => x.Scope, "weapon")
            .Add(x => x.Mods, mods));
        cut.Find("#mod-search").Input("physical");

        cut.Find($"[id='pick-exc-{pure.Anchor.Id}']").Click();
        state.CriteriaFor("weapon").Excluded.Should().ContainSingle();
        state.CriteriaFor("weapon").Included.Should().BeEmpty();

        cut.Find($"[id='pick-inc-{pure.Anchor.Id}']").Click();
        state.CriteriaFor("weapon").Included.Should().ContainSingle();
        state.CriteriaFor("weapon").Excluded.Should().BeEmpty();
    }
}

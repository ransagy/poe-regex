using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using PoeRegex.Web.Core;
using web.Components;
using web.Services;

namespace PoeRegex.Web.Tests;

/// <summary>UX polish: ruler segmentation, min-input capability, and family tier counts.</summary>
public class PickerPolishTests
{
    private static BunitContext Context()
    {
        var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton(new EngineService());
        ctx.Services.AddSingleton(new PersistenceService(ctx.JSInterop.JSRuntime));
        ctx.Services.AddSingleton(new PreferencesState(ctx.JSInterop.JSRuntime));
        return ctx;
    }

    private static (EngineService Engine, WorkbenchState State, SearchModel Model) Picked(
        params WantedMod[] picks)
    {
        var engine = new EngineService();
        var state = new WorkbenchState();
        state.AddIncludes(null, picks);
        var model = new SearchModel(engine, state);
        model.Update(engine.CompilePicks(state.Global.Included, [], state.Global.MatchAll, null), null);
        return (engine, state, model);
    }

    [Fact]
    public void SearchModel_SimpleAnyOf_SplitsRulerIntoOneSegmentPerPick()
    {
        var (_, state, model) = Picked(
            new WantedMod("IncreasedLife6"), new WantedMod("Strength1"));

        model.RulerSegments.Should().HaveCountGreaterThanOrEqualTo(2);
        model.RulerSegments.Sum(s => s.Cost).Should().Be(model.Length,
            "the ruler segments cover the whole string");
        var first = model.RulerSegments.Single(s => s.Picks.Contains(state.Global.Included[0]));
        var second = model.RulerSegments.Single(s => s.Picks.Contains(state.Global.Included[1]));
        first.Index.Should().NotBe(second.Index);

        model.HotPick(state.Global.Included[0]);
        model.IsSegmentHot(first.Index).Should().BeTrue();
        model.IsPickHot(state.Global.Included[0]).Should().BeTrue();
        model.IsSegmentHot(second.Index).Should().BeFalse();

        model.Cool();
        model.IsSegmentHot(first.Index).Should().BeFalse();

        model.HotSegment(second.Index);
        model.IsPickHot(state.Global.Included[1]).Should().BeTrue();
        model.IsPickHot(state.Global.Included[0]).Should().BeFalse();
        model.IsHot(second.BlockIndex).Should().BeTrue("the quoted group stays lit too");
    }

    [Fact]
    public void SearchModel_SimpleAllOf_KeepsOneSegmentPerQuotedBlock()
    {
        var (engine, state, model) = Picked(
            new WantedMod("IncreasedLife6"), new WantedMod("Strength1"));
        state.SetMatchAll(null, true);
        model.Update(engine.CompilePicks(state.Global.Included, [], true, null), null);

        model.RulerSegments.Should().HaveCount(model.Blocks.Count);
        model.RulerSegments.Should().OnlyContain(s => s.Picks.Count == 0);
        model.RulerSegments.Sum(s => s.Cost).Should().Be(model.Length);
    }

    [Fact]
    public void SearchBar_KeepsRulerAndPinnedBlockWhenQueryIsEmpty()
    {
        using var ctx = Context();
        var engine = ctx.Services.GetRequiredService<EngineService>();
        var state = new WorkbenchState();
        var model = new SearchModel(engine, state);
        model.Update(null, null);

        var cut = ctx.Render<SearchBar>(p => p
            .Add(x => x.Model, model)
            .Add(x => x.State, state));

        cut.Find(".hero-pinned").Should().NotBeNull("the compact layout pins this block");
        cut.Find(".hero-tools").Should().NotBeNull("the tools stay below the pinned block");
        cut.Find(".ruler").Should().NotBeNull("the track reserves its height while empty");
        cut.Find(".ruler.empty").Should().NotBeNull();
        cut.FindAll(".ruler-seg").Should().BeEmpty();
        cut.Find("#slot-copy-simple").HasAttribute("disabled").Should().BeTrue();

        state.AddInclude(null, new WantedMod("IncreasedLife6"));
        model.Update(engine.CompilePicks(state.Global.Included, [], false, null), null);
        cut.Render();

        cut.Find(".ruler").Should().NotBeNull();
        cut.FindAll(".ruler-seg").Should().NotBeEmpty();
        cut.FindAll(".ruler.empty").Should().BeEmpty();
        cut.Find("#slot-copy-simple").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void PickBreakdown_ShowsMinMaxForPicksWithMultipleValues()
    {
        using var ctx = Context();
        var engine = ctx.Services.GetRequiredService<EngineService>();
        var boots = ModFamilies.Group(engine.ModsForClass("Boots"))
            .First(f => f.Key == "MovementVelocity");
        var state = new WorkbenchState();
        state.AddInclude(null, new WantedMod("IncreasedLife6")); // ranged family
        state.AddInclude(null, boots.Pick()); // fixed-tier family (10–35)
        state.AddInclude(null, new WantedMod("MovementVelocity1")); // single fixed value
        state.AddInclude(null, new WantedMod("MovementVelocity1", ["Runner's"])); // name pick
        var model = new SearchModel(engine, state);
        model.Update(engine.CompilePicks(state.Global.Included, [], false, null), null);

        var cut = ctx.Render<PickBreakdown>(p => p
            .Add(x => x.Model, model)
            .Add(x => x.State, state));

        cut.FindAll(".bd-row.inc").Should().HaveCount(4);
        cut.FindAll(".bd-min").Should().HaveCount(4, "min+max for the life and movement-speed families");
        cut.Find("#min-global-IncreasedLife6").Should().NotBeNull();
        cut.Find("#max-global-IncreasedLife6").Should().NotBeNull();
        cut.Find("#min-global-MovementVelocity1-v").Should().NotBeNull();
        cut.Find("#max-global-MovementVelocity1-v").Should().NotBeNull();
        cut.FindAll(".bd-min.ignored").Should().BeEmpty();
        cut.FindAll(".bd-min.noop").Should().BeEmpty();
    }

    [Fact]
    public void PickBreakdown_ShowsVisibleRangeFallback()
    {
        using var ctx = Context();
        var engine = ctx.Services.GetRequiredService<EngineService>();
        var state = new WorkbenchState();
        // All resistances has a real span but its family fragment already
        // covers every roll, so a min cannot change the compiled text.
        state.AddInclude(null, new WantedMod("AllResistances1", MinValue: 4));
        var model = new SearchModel(engine, state);
        model.Update(engine.CompilePicks(state.Global.Included, [], false, null), null);

        var cut = ctx.Render<PickBreakdown>(p => p
            .Add(x => x.Model, model)
            .Add(x => x.State, state));

        cut.FindAll(".bd-min").Should().HaveCount(2);
        cut.Find(".bd-warn").TextContent.Should().Contain("range not enforceable");
    }

    [Fact]
    public void PickBreakdown_TierRange_ShowsRangeLabelAndMinMax()
    {
        using var ctx = Context();
        var engine = ctx.Services.GetRequiredService<EngineService>();
        var family = ModFamilies.Group(engine.ModsForClass("Body Armour"))
            .First(f => f.Key == "IncreasedLife");
        var tier = family.Tiers.Single(t => t.Name == "Virile");
        var pick = engine.TierRangePick(family, tier, up: true)!;
        var state = new WorkbenchState();
        state.AddInclude(null, pick);
        var model = new SearchModel(engine, state);
        model.Update(engine.CompilePicks(state.Global.Included, [], false, null), null);

        var cut = ctx.Render<PickBreakdown>(p => p
            .Add(x => x.Model, model)
            .Add(x => x.State, state));

        var label = cut.Find(".bd-row.inc .bd-text").TextContent;
        label.Should().Contain("100–214");
        label.Should().NotContain("or better");
        cut.FindAll(".bd-min").Should().HaveCount(2);
        cut.Find("#min-global-" + pick.ModId + "-r").Should().NotBeNull();
        cut.Find("#max-global-" + pick.ModId + "-r").Should().NotBeNull();
        cut.FindAll(".bd-warn").Should().BeEmpty(
            "the tier's own min/max are natural bounds, not an ignored filter");
    }

    [Fact]
    public void PickBreakdown_NonConsecutiveTierRange_HidesMinMaxAndListsValues()
    {
        using var ctx = Context();
        var engine = ctx.Services.GetRequiredService<EngineService>();
        var pick = new WantedMod(
            "MovementVelocity2",
            MinValue: 15,
            MaxValue: 25,
            RangeModIds: ["MovementVelocity2", "MovementVelocity4"]);
        var state = new WorkbenchState();
        state.AddInclude(null, pick);
        var model = new SearchModel(engine, state);
        model.Update(engine.CompilePicks(state.Global.Included, [], false, null), null);

        var cut = ctx.Render<PickBreakdown>(p => p
            .Add(x => x.Model, model)
            .Add(x => x.State, state));

        cut.Find(".bd-row.inc .bd-text").TextContent
            .Should().Contain("15 or 25% increased Movement Speed");
        cut.FindAll(".bd-min").Should().BeEmpty(
            "a min filter would fill the gap between non-consecutive tiers");
        cut.FindAll(".bd-max").Should().BeEmpty();
    }

    [Fact]
    public void PickBreakdown_FirstTierOrBetter_UsesTheFamilyLabel()
    {
        using var ctx = Context();
        var engine = ctx.Services.GetRequiredService<EngineService>();
        var boots = ModFamilies.Group(engine.ModsForClass("Boots"))
            .First(f => f.Key == "MovementVelocity");
        var first = boots.Tiers.OrderBy(t => t.RequiredLevel).First();
        var pick = engine.BestTierPick(boots, first, up: true, "Boots")!;
        var state = new WorkbenchState();
        state.SetMode("advanced");
        state.SelectSlot("boots");
        state.AddInclude("boots", pick);
        var paste = engine.Combine(
            [("boots (Boots)", "Boots",
                (IEnumerable<WantedMod>)state.CriteriaFor("boots").Included, false)],
            [], allowOverflow: true);
        var model = new SearchModel(engine, state);
        model.Update(null, paste);

        var cut = ctx.Render<PickBreakdown>(p => p
            .Add(x => x.Model, model)
            .Add(x => x.State, state));

        var label = cut.Find(".bd-row.inc .bd-text").TextContent;
        label.Should().NotContain("10–35");
        label.Should().Contain("Movement Speed");
    }

    [Fact]
    public async Task PickBreakdown_MovementSpeedFamily_MinFiltersFixedTiers()
    {
        using var ctx = Context();
        var engine = ctx.Services.GetRequiredService<EngineService>();
        var boots = ModFamilies.Group(engine.ModsForClass("Boots"))
            .First(f => f.Key == "MovementVelocity");
        var state = new WorkbenchState();
        state.SetMode("advanced");
        state.SelectSlot("boots");
        state.AddInclude("boots", boots.Pick());
        var paste = engine.Combine(
            [("boots (Boots)", "Boots",
                (IEnumerable<WantedMod>)state.CriteriaFor("boots").Included, false)],
            [], allowOverflow: true);
        var model = new SearchModel(engine, state);
        model.Update(null, paste);

        var cut = ctx.Render<PickBreakdown>(p => p
            .Add(x => x.Model, model)
            .Add(x => x.State, state));

        cut.FindAll(".bd-min").Should().HaveCount(2);
        await cut.Find("#min-boots-MovementVelocity1-v").ChangeAsync("20");

        var pick = state.CriteriaFor("boots").Included.Should().ContainSingle().Subject;
        pick.MinValue.Should().Be(20);
        var query = engine.Combine(
            [("boots (Boots)", "Boots",
                (IEnumerable<WantedMod>)state.CriteriaFor("boots").Included, false)],
            [], allowOverflow: true).Query;
        foreach (var value in new[] { 20, 25, 30, 35 })
        {
            ItemMatcher.Match(query,
                ["Item Class: Boots", $"{value}% increased Movement Speed"]).Match
                .Should().BeTrue($"{value}% is inside the 20+ filter");
        }
        foreach (var value in new[] { 10, 15 })
        {
            ItemMatcher.Match(query,
                ["Item Class: Boots", $"{value}% increased Movement Speed"]).Match
                .Should().BeFalse($"{value}% is below the 20+ filter");
        }
    }

    [Fact]
    public async Task PickBreakdown_ExactRangedName_ConvertsToRangeOnMin()
    {
        using var ctx = Context();
        var engine = ctx.Services.GetRequiredService<EngineService>();
        var family = ModFamilies.Group(engine.ModsForClass("Body Armour"))
            .First(f => f.Key == "IncreasedLife");
        var tier = family.Tiers.Single(t => t.Name == "Virile");
        var state = new WorkbenchState();
        state.SetMode("advanced");
        state.SelectSlot("body");
        state.AddInclude("body", new WantedMod(tier.Anchor.Id, [tier.Name]));
        var paste = engine.Combine(
            [("body (Body Armour)", "Body Armour",
                (IEnumerable<WantedMod>)state.CriteriaFor("body").Included, false)],
            [], allowOverflow: true);
        var model = new SearchModel(engine, state);
        model.Update(null, paste);

        var cut = ctx.Render<PickBreakdown>(p => p
            .Add(x => x.Model, model)
            .Add(x => x.State, state));

        cut.FindAll(".bd-min").Should().HaveCount(2,
            "an exact ranged tier has a min/max even when its short name is chosen");
        await cut.Find($"#min-body-{tier.Anchor.Id}-n-Virile").ChangeAsync("110");

        var pick = state.CriteriaFor("body").Included.Should().ContainSingle().Subject;
        pick.IsTierRange.Should().BeTrue("the first filter converts the name pick");
        pick.MinValue.Should().Be(110);
        pick.MaxValue.Should().BeNull();

        var query = engine.Combine(
            [("body (Body Armour)", "Body Armour",
                (IEnumerable<WantedMod>)state.CriteriaFor("body").Included, false)],
            [], allowOverflow: true).Query;
        ItemMatcher.Match(query,
            ["Item Class: Body Armours", "+110(100-119) to maximum Life"]).Match
            .Should().BeTrue();
        ItemMatcher.Match(query,
            ["Item Class: Body Armours", "+109(100-119) to maximum Life"]).Match
            .Should().BeFalse();
    }

    [Fact]
    public void PickBreakdown_NameUp_ShowsCompactEquivalentRange()
    {
        using var ctx = Context();
        var engine = ctx.Services.GetRequiredService<EngineService>();
        var family = ModFamilies.Group(engine.ModsForClass("Body Armour"))
            .First(f => f.Key == "IncreasedLife");
        var tier = family.Tiers.Single(t => t.Name == "Virile");
        var state = new WorkbenchState();
        state.AddInclude(null, new WantedMod(tier.Anchor.Id, family.NamesFrom(tier)));
        var model = new SearchModel(engine, state);
        model.Update(engine.CompilePicks(state.Global.Included, [], false, null), null);

        var cut = ctx.Render<PickBreakdown>(p => p
            .Add(x => x.Model, model)
            .Add(x => x.State, state));

        cut.Find(".bd-row.inc .bd-text").TextContent.Should().Contain("\"Virile\" or better");
        cut.Find(".bd-row.inc .bd-detail").TextContent
            .Should().Contain("= +100–214 to maximum Life");
    }

    [Fact]
    public void PickBreakdown_DesecratedTagFollowsTheModLabel()
    {
        using var ctx = Context();
        var engine = ctx.Services.GetRequiredService<EngineService>();
        var mod = engine.FindMod("AbyssModHelmUlamanSuffixMarkedEnemyTakeIncreasedDamage")!;
        var state = new WorkbenchState();
        state.AddInclude(null, new WantedMod(mod.Id));
        var model = new SearchModel(engine, state);
        model.Update(engine.CompilePicks(state.Global.Included, [], false, null), null);

        var cut = ctx.Render<PickBreakdown>(p => p
            .Add(x => x.Model, model)
            .Add(x => x.State, state));

        var label = cut.Find(".bd-row.inc .bd-label");
        label.TextContent.Should().EndWith("Ulaman");
        label.LastElementChild!.TextContent.Should().Be("Ulaman");
    }

    [Fact]
    public void PickBreakdown_AllGearAugmentTagOmitsTargets()
    {
        using var ctx = Context();
        var engine = ctx.Services.GetRequiredService<EngineService>();
        var state = new WorkbenchState();
        state.AddInclude(null, new WantedMod("augment:SoulCoreMaxLife:body-armours"));
        var model = new SearchModel(engine, state);
        model.Update(engine.CompilePicks(state.Global.Included, [], false, null), null);

        var cut = ctx.Render<PickBreakdown>(p => p
            .Add(x => x.Model, model)
            .Add(x => x.State, state));

        cut.Find(".bd-row.inc .mod-source").TextContent.Trim()
            .Should().Be("Soul Core of Jiquani");
    }

    [Fact]
    public void PickBreakdown_GlobalNamePickShowsMergedRollAlternatives()
    {
        using var ctx = Context();
        var engine = ctx.Services.GetRequiredService<EngineService>();
        var state = new WorkbenchState();
        state.AddInclude(null,
            new WantedMod("GlobalProjectileSkillGemLevelWeapon2", ["of the Fletcher"]));
        var model = new SearchModel(engine, state);
        model.Update(engine.CompilePicks(state.Global.Included, [], false, null), null);

        var cut = ctx.Render<PickBreakdown>(p => p
            .Add(x => x.Model, model)
            .Add(x => x.State, state));

        cut.Find(".bd-row.inc .bd-label").TextContent.Should().Contain("of the Fletcher");
        cut.Find(".bd-row.inc .bd-detail").TextContent.Trim()
            .Should().Contain("+1 / +2 to Level of all Projectile Skills");
    }

    [Fact]
    public void PickBreakdown_ShowsAnyOfCustomTextInsideThePickGroup()
    {
        using var ctx = Context();
        var engine = ctx.Services.GetRequiredService<EngineService>();
        var state = new WorkbenchState();
        state.AddInclude(null, new WantedMod("IncreasedLife6"));
        state.AddInclude(null, new WantedMod(RawText.Encode("Item Class: (am|ri|be|Cross)")));
        var model = new SearchModel(engine, state);
        model.Update(engine.CompilePicks(state.Global.Included, [], false, null), null);

        var cut = ctx.Render<PickBreakdown>(p => p
            .Add(x => x.Model, model)
            .Add(x => x.State, state));

        cut.FindAll(".bd-row.inc").Should().ContainSingle();
        var custom = cut.FindAll(".bd-sec")
            .Single(s => s.TextContent.Contains("Custom text"));
        custom.TextContent.Should().Contain("Any of");
        custom.QuerySelectorAll(".bd-row.imported").Length.Should().Be(1);
        custom.TextContent.Should().Contain("Item Class: (am|ri|be|Cross)");
        model.Blocks.Count(b => b.Role == "inc").Should().Be(1);
        model.Blocks.Should().NotContain(b => b.Role == "external");

        // Hovering a normal mod lights the shared block, but must not light the
        // custom row that happens to live in the same any-of group.
        cut.Find(".bd-row.inc").TriggerEvent("onmouseenter", new MouseEventArgs());
        cut.Find(".bd-row.imported").ClassList.Should().NotContain("hot");
        cut.Find(".bd-row.imported").TriggerEvent("onmouseenter", new MouseEventArgs());
        cut.Find(".bd-row.imported").ClassList.Should().Contain("hot");
        cut.Find(".bd-row.imported").TriggerEvent("onmouseleave", new MouseEventArgs());
        cut.Find(".bd-row.imported").ClassList.Should().NotContain("hot");
    }

    [Fact]
    public void PickBreakdown_SeparatesCustomTextFromNormalExcludes()
    {
        using var ctx = Context();
        var engine = ctx.Services.GetRequiredService<EngineService>();
        var state = new WorkbenchState();
        state.AddInclude(null, new WantedMod("IncreasedLife6"));
        state.AddExclude(null, "ManaRegeneration1");
        state.AddExclude(null, RawExclusion.Encode("parry|bow"));
        state.SetExternalIncludes(["Item Class: (am|ri|be|Cross)"]);
        var model = new SearchModel(engine, state);
        model.Update(
            engine.CompilePicks(
                state.Global.Included, state.Global.Excluded, false, null,
                state.ExternalIncludes),
            null);

        var cut = ctx.Render<PickBreakdown>(p => p
            .Add(x => x.Model, model)
            .Add(x => x.State, state));

        var custom = cut.FindAll(".bd-sec")
            .Single(s => s.TextContent.Contains("Custom text"));
        custom.QuerySelectorAll(".bd-row.imported").Length.Should().Be(2);
        custom.QuerySelectorAll(".bd-row.exc").Length.Should().Be(1);
        custom.TextContent.Should().Contain("Must match");
        custom.TextContent.Should().Contain("None of");
        // The custom row shows the text once (as the label), not again as a fragment.
        custom.QuerySelectorAll(".bd-row.imported")
            .Count(r => r.TextContent.Contains("Item Class: (am|ri|be|Cross)"))
            .Should().Be(1);
        cut.FindAll(".bd-row.exc:not(.imported)").Count.Should().Be(1);
        cut.FindAll(".bd-tag").Should().BeEmpty();
    }

    [Fact]
    public void Engine_CanApplyMin_OnlyForPicksWhoseFragmentChanges()
    {
        var engine = new EngineService();
        engine.CanApplyMin(new WantedMod("IncreasedLife6"), null).Should().BeTrue();
        engine.CanApplyMin(new WantedMod("IncreasedLife1", ["Hale"]), null)
            .Should().BeFalse("a name pick never carries a minimum");

        var families = ModFamilies.Group(engine.PlayableMods());
        engine.CanApplyMin(families.First(f => f.Key == "IncreasedLife").Pick(), null)
            .Should().BeTrue("the family fragments all carry a thresholdable value");
        engine.CanApplyMin(families.First(f => f.Key == "MovementVelocity").Pick(), null)
            .Should().BeFalse("its precomputed fragment already covers every roll");
    }

    [Fact]
    public void PickBreakdown_AdvancedScope_UsesClassScopedMinCapability()
    {
        using var ctx = Context();
        var engine = ctx.Services.GetRequiredService<EngineService>();
        var state = new WorkbenchState();
        state.SetMode("advanced");
        state.SelectSlot("ring1");
        state.AddInclude("ring1", new WantedMod("IncreasedLife6"));
        var paste = engine.Combine(
            [("ring1 (Ring)", "Ring", (IEnumerable<WantedMod>)state.CriteriaFor("ring1").Included, false)],
            [], allowOverflow: true);
        var model = new SearchModel(engine, state);
        model.Update(null, paste);

        var cut = ctx.Render<PickBreakdown>(p => p
            .Add(x => x.Model, model)
            .Add(x => x.State, state));

        cut.Find("#min-ring1-IncreasedLife6").Should().NotBeNull();
        cut.Find("#max-ring1-IncreasedLife6").Should().NotBeNull();
        cut.FindAll(".bd-min").Should().HaveCount(2);
    }

    [Fact]
    public void ValuePrewarmTargets_SimpleScopeOnlyWhenMinCapable()
    {
        var engine = new EngineService();
        var state = new WorkbenchState();
        state.AddInclude(null, new WantedMod("IncreasedLife6"));

        var targets = ValuePrewarmTargets.For(state, engine);

        targets.Should().ContainSingle();
        targets[0].ItemClass.Should().BeNull();
        targets[0].Key.Should().Be("simple|global");
    }

    [Fact]
    public void ValuePrewarmTargets_WarmsEveryActiveAdvancedClass()
    {
        var engine = new EngineService();
        var state = new WorkbenchState();
        state.SetMode("advanced");
        state.AddInclude("ring1", new WantedMod("IncreasedLife6"));
        state.AddInclude("helmet", new WantedMod("IncreasedLife6"));
        state.AddInclude("boots", new WantedMod("MovementVelocity1", ["Runner's"]));

        var targets = ValuePrewarmTargets.For(state, engine);

        targets.Select(t => t.ItemClass).Should().BeEquivalentTo(["Ring", "Helmet"]);
        targets.Select(t => t.Key).Should().BeEquivalentTo(["advanced|Ring", "advanced|Helmet"]);
    }

    [Fact]
    public void ValuePrewarmTargets_DeduplicatesSameClassSlots()
    {
        var engine = new EngineService();
        var state = new WorkbenchState();
        state.SetMode("advanced");
        state.AddInclude("ring1", new WantedMod("IncreasedLife6"));
        state.AddInclude("ring2", new WantedMod("IncreasedLife6"));

        var targets = ValuePrewarmTargets.For(state, engine);

        targets.Should().ContainSingle();
        targets[0].ItemClass.Should().Be("Ring");
    }

    [Fact]
    public async Task Engine_ValuePathReady_GatesTheExactMinProbe()
    {
        var engine = new EngineService();
        engine.ValuePathReady.Should().BeFalse("drawing a chip must not load the value corpus");
        engine.MayAcceptMin(new WantedMod("IncreasedLife6")).Should().BeTrue();
        engine.PrecomputedMinCapability(new WantedMod("IncreasedLife6"), null)
            .Should().BeTrue("the shipped table answers without the corpus");

        await engine.PrewarmValuePathAsync(null);

        engine.ValuePathReady.Should().BeTrue();
        engine.IsValueScopeReady(null).Should().BeTrue();
        engine.IsValueScopeReady("Ring").Should().BeFalse("only the warmed scope may probe");
        engine.CanApplyMin(new WantedMod("IncreasedLife6"), null).Should().BeTrue();
    }

    [Fact]
    public void ModPicker_FamilyRowShowsPickedTierCountInsteadOfBorder()
    {
        using var ctx = Context();
        var engine = ctx.Services.GetRequiredService<EngineService>();
        var state = new WorkbenchState();
        var family = ModFamilies.Group(engine.PlayableMods()).First(f => f.Key == "IncreasedLife");
        var tier = family.Tiers[0];
        state.AddInclude(null, new WantedMod(tier.Anchor.Id, [tier.Name]));

        var cut = ctx.Render<ModPicker>(p => p
            .Add(x => x.State, state)
            .Add(x => x.Mods, engine.PlayableMods()));

        var toggle = cut.Find($"#fam-toggle-{tier.Anchor.Id}");
        var row = toggle.ParentElement!;
        row.ClassList.Should().NotContain("has-tier");
        row.TextContent.Should().Contain("1 picked");

        var status = row.QuerySelector(".fam-status")!;
        status.QuerySelector(".picked-count").Should().NotBeNull();
        toggle.QuerySelector(".picked-count").Should().BeNull(
            "status notes stay outside the expand toggle");
        toggle.QuerySelector(".tier-count").Should().NotBeNull(
            "the tier count stays with the mod text");
        status.NextElementSibling!.Id.Should().Be($"pick-inc-{family.Anchor.Id}",
            "the status group sits immediately before the actions");
    }
}

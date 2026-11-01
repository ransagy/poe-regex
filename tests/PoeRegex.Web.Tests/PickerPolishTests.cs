using Bunit;
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
    public void SearchBar_KeepsRulerAndDockWhenQueryIsEmpty()
    {
        using var ctx = Context();
        var engine = ctx.Services.GetRequiredService<EngineService>();
        var state = new WorkbenchState();
        var model = new SearchModel(engine, state);
        model.Update(null, null);

        var cut = ctx.Render<SearchBar>(p => p
            .Add(x => x.Model, model)
            .Add(x => x.State, state));

        cut.Find(".ruler").Should().NotBeNull("the track reserves its height while empty");
        cut.Find(".ruler.empty").Should().NotBeNull();
        cut.FindAll(".ruler-seg").Should().BeEmpty();
        cut.Find(".out-dock").Should().NotBeNull("the dock keeps its slot in compact layouts");
        cut.Find("#dock-copy").HasAttribute("disabled").Should().BeTrue();

        state.AddInclude(null, new WantedMod("IncreasedLife6"));
        model.Update(engine.CompilePicks(state.Global.Included, [], false, null), null);
        cut.Render();

        cut.Find(".ruler").Should().NotBeNull();
        cut.FindAll(".ruler-seg").Should().NotBeEmpty();
        cut.FindAll(".ruler.empty").Should().BeEmpty();
        cut.Find("#dock-copy").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void PickBreakdown_ShowsMinOnlyWhenItChangesTheFragment()
    {
        using var ctx = Context();
        var engine = ctx.Services.GetRequiredService<EngineService>();
        var state = new WorkbenchState();
        state.AddInclude(null, new WantedMod("IncreasedLife6")); // supports a min
        state.AddInclude(null, new WantedMod("MovementVelocity1", ["Runner's"])); // name pick
        state.AddInclude(null, new WantedMod("AllResistances1")); // min would be ignored
        var model = new SearchModel(engine, state);
        model.Update(engine.CompilePicks(state.Global.Included, [], false, null), null);

        var cut = ctx.Render<PickBreakdown>(p => p
            .Add(x => x.Model, model)
            .Add(x => x.State, state));

        cut.FindAll(".bd-row.inc").Should().HaveCount(3);
        cut.FindAll(".bd-min").Should().HaveCount(1);
        cut.Find("#min-global-IncreasedLife6").Should().NotBeNull();
        cut.FindAll(".bd-min.ignored").Should().BeEmpty();
        cut.FindAll(".bd-min.noop").Should().BeEmpty();
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
        state.AddInclude("ring1", new WantedMod("IncreasedLife1", ["Hale"]));
        var paste = engine.Combine(
            [("ring1 (Ring)", "Ring", (IEnumerable<WantedMod>)state.CriteriaFor("ring1").Included, false)],
            [], allowOverflow: true);
        var model = new SearchModel(engine, state);
        model.Update(null, paste);

        var cut = ctx.Render<PickBreakdown>(p => p
            .Add(x => x.Model, model)
            .Add(x => x.State, state));

        cut.Find("#min-ring1-IncreasedLife6").Should().NotBeNull();
        cut.FindAll(".bd-min").Should().HaveCount(1);
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

        var row = cut.Find($"#fam-toggle-{tier.Anchor.Id}").ParentElement!;
        row.ClassList.Should().NotContain("has-tier");
        row.TextContent.Should().Contain("1 picked");
        row.QuerySelector(".picked-count").Should().NotBeNull();
    }
}

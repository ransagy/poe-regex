using PoeRegex.Core.Data;
using PoeRegex.Web.Core;

namespace PoeRegex.Web.Tests;

/// <summary>
/// Wave 43 — family/tier grouped picks: grouping and display, name-literal
/// compile semantics, tier-or-better name sets, and adv-header matching in
/// the lab (the game searches the full copied advanced description, where
/// affix names live only in the `{ ... }` captions).
/// </summary>
public class FamilyPickTests
{
    private static readonly EngineService Engine = new();

    private static ModFamily Family(string classId, string familyKey) =>
        ModFamilies.Group(Engine.ModsForClass(classId))
            .First(f => f.Key == familyKey);

    [Fact]
    public void CommonFamilies_AllExist_AndCoverBothGens()
    {
        var families = ModFamilies.Group(Engine.PlayableMods());
        var keys = families.Select(f => f.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var key in ModFamilies.CommonKeys)
            keys.Should().Contain(key, $"curated family '{key}' must exist in the catalog");
        var common = families.Where(ModFamilies.IsCommon).ToList();
        common.Should().Contain(f => f.IsPrefix);
        common.Should().Contain(f => !f.IsPrefix);
        common.Should().HaveCountLessThan(30, "the pre-typing list stays a shortlist");
    }

    [Fact]
    public void Group_LifeOnBodyArmour_ShapeTextAndTierOrder()
    {
        var fam = Family("Body Armour", "IncreasedLife");
        fam.IsPrefix.Should().BeTrue();
        fam.Tiers.Should().HaveCount(13);
        fam.Tiers[0].Name.Should().Be("Hale");
        fam.Tiers[^1].Name.Should().Be("Prime");
        fam.DisplayText.Should().Be("+ to maximum Life",
            "family rows drop the numbers and just name the stat");
        fam.Anchor.StatLines.Should().NotBeEmpty();
    }

    [Fact]
    public void Group_ColdWeaponPrefix_MergesOneHandAndTwoHandTiers()
    {
        var fam = ModFamilies.Group(Engine.PlayableMods())
            .First(f => f.Key == "ColdDamageWeaponPrefix");
        var snowy = fam.Tiers.First(t => t.Name == "Snowy");
        snowy.Members.Should().HaveCount(2, "wand/focus and staff variants share the name");
        fam.DisplayText.Should().Be("% increased Cold Damage");
        fam.NamesFrom(snowy).Should().Equal(
            ["Snowy", "Hailing", "Arctic", "Crystalline", "Cryomancer's"],
            "tier-or-better is by required level, never game tier numbers");
        fam.NamesFrom(fam.Tiers[^1]).Should().Equal(["Cryomancer's"]);
    }

    [Fact]
    public void Group_AddedDamage_ShapeDropsBothNumbers()
    {
        var fam = Family("Crossbow", "LocalLightningDamage");
        fam.Tiers.Should().HaveCount(10);
        fam.Tiers[0].Name.Should().Be("Humming");
        fam.DisplayText.Should().Be("Adds to Lightning Damage");
    }

    [Fact]
    public void Group_HybridMod_EveryLineKeptAsShape()
    {
        var fam = Family("Crossbow", "LocalIncreasedPhysicalDamagePercentAndAccuracyRating");
        fam.DisplayLines.Should().Equal(
            ["% increased Physical Damage", "+ to Accuracy Rating"],
            "multi-line families keep one shape per line (rendered stacked)");
        fam.DisplayText.Should().Be("% increased Physical Damage / + to Accuracy Rating",
            "chips join the lines with a slash");
        fam.Tiers.Should().NotBeEmpty();
    }

    [Fact]
    public void Group_NamesUniqueWithinFamilyAndScope()
    {
        foreach (var classId in new[] { "Ring", "Crossbow", "Body Armour", "Staff" })
        {
            var families = ModFamilies.Group(Engine.ModsForClass(classId));
            foreach (var fam in families)
                fam.Tiers.Select(t => t.Name).Should().OnlyHaveUniqueItems(
                    $"affix names are the tier key on {classId}/{fam.Key}");
        }
    }

    [Fact]
    public void PlainPick_CompilesStatFragment_NamePick_CompilesAffixLiteral()
    {
        var snowy = Engine.FindMod("ColdDamagePrefixOnWeapon4")!;
        var statLine = snowy.StatLines[0].Line;
        const string header = "{ Prefix Modifier \"Snowy\" (Tier: 4) — Elemental, Cold, Ailment }";

        var plain = Engine.Compile(new WantedMod(snowy.Id).Requirements(), []);
        var exact = Engine.Compile(new WantedMod(snowy.Id, ["Snowy"]).Requirements(), []);

        plain.Query.Should().NotBe(exact.Query);
        ItemMatcher.Match(plain.Query, [statLine]).Match.Should().BeTrue(
            "plain picks stay stat fragments and match the stat text on any tier");
        ItemMatcher.Match(plain.Query, [header]).Match.Should().BeFalse(
            "the stat fragment does not key on the affix name");
        ItemMatcher.Match(exact.Query, [statLine]).Match.Should().BeFalse(
            "the name replaces the fragment");
        ItemMatcher.Match(exact.Query, [header]).Match.Should().BeTrue(
            "name picks match the affix header the game searches");
    }

    [Fact]
    public void TierOrBetter_MatchesEveryNameViaLabMatcher()
    {
        var fam = ModFamilies.Group(Engine.PlayableMods())
            .First(f => f.Key == "ColdDamageWeaponPrefix");
        var snowy = fam.Tiers.First(t => t.Name == "Snowy");
        var pick = new WantedMod(snowy.Anchor.Id, fam.NamesFrom(snowy));
        var query = Engine.Compile(pick.Requirements(), []).Query;

        static string Header(string name) => $"{{ Prefix Modifier \"{name}\" (Tier: 4) — Elemental, Cold, Ailment }}";
        foreach (var name in fam.NamesFrom(snowy))
            ItemMatcher.Match(query, [Header(name)]).Match.Should().BeTrue($"\"{name}\" is in the set");
        ItemMatcher.Match(query, [Header("Bitter")]).Match.Should().BeFalse(
            "tiers below the pick are not included");
    }

    [Fact]
    public void PlainPick_MatchesEveryTierThroughStatText()
    {
        var fam = Family("Crossbow", "LocalAccuracyRating");
        var query = Engine.Compile(new WantedMod(fam.Anchor.Id).Requirements(), []).Query;
        var hot = fam.Tiers.First(t => t.Name == "Reliable");
        ItemMatcher.Match(query, hot.DisplayLines).Match.Should().BeTrue(
            "the fragment matches this stat's text on any tier");
    }

    [Fact]
    public void ImportedItem_HeadersAreMatchable()
    {
        var item = ItemTextParser.Parse(FixtureTexts.Crossbow);
        var lines = item.AllLines();
        lines.Should().Contain("{ Prefix Modifier \"Snapping\" (Tier: 8) — Damage, Elemental, Lightning, Attack }");
        ItemMatcher.Match("\"Snapping\"", lines).Match.Should().BeTrue();
        ItemMatcher.Match("\"Snowy\"", lines).Match.Should().BeFalse();
        ItemMatcher.Match(@"\(Tier: 8\)", lines).Match.Should().BeTrue();
    }

    [Fact]
    public void BuiltItem_RehealAddsHeaders_NameQueryMatches()
    {
        var mod = Engine.FindMod("ColdDamagePrefixOnWeapon4")!;
        var parsed = new ParsedItem("Foci", "Magic", "Built Focus", [], null, null, null, null, false, []);
        var item = new TestItem { Name = parsed.Name!, ClassId = "Focus", Parsed = parsed, Built = true };
        item.Mapped.Add(new MappedLine(mod.StatLines[0].Line, mod.StatLines[0].Line, [], [mod.Id], mod.Id));

        item.Reheal(Engine).Should().BeTrue();
        item.MatchLines().Should().Contain("{ Prefix Modifier \"Snowy\" }");
        ItemMatcher.Match("\"Snowy\"", item.MatchLines()).Match.Should().BeTrue();
    }

    [Fact]
    public void BuiltItem_Reheal_IsIdempotent_NoDuplicateLines()
    {
        var life = Engine.FindMod("IncreasedLife2")!;
        var strength = Engine.FindMod("Strength4")!;
        var parsed = new ParsedItem("Rings", "Rare", "Built Ring", [], null, null, null, null, false, []);
        var item = new TestItem { Name = parsed.Name!, ClassId = "Ring", Parsed = parsed, Built = true };
        foreach (var mod in new[] { life, strength })
            foreach (var sl in mod.StatLines)
                item.Mapped.Add(new MappedLine(sl.Line, sl.Line, [], [mod.Id], mod.Id));

        item.Reheal(Engine).Should().BeTrue("the first heal adds headers");
        var healed = item.Mapped.Select(m => m.Text).ToList();
        healed.Should().OnlyHaveUniqueItems();
        item.Reheal(Engine).Should().BeFalse("a healed item is stable");
        item.Mapped.Select(m => m.Text).Should().Equal(healed);
        healed.Should().Contain("{ Prefix Modifier \"Healthy\" }");
        healed.Should().Contain("{ Suffix Modifier \"of the Lion\" }");
    }

    [Fact]
    public void Combine_NamePick_FlowsThroughAsLiteral()
    {
        var paste = Engine.Combine(
            [("focus (Focus)", "Focus", new[] { new WantedMod("ColdDamagePrefixOnWeapon4", ["Snowy"]) })],
            []);
        paste.Diagnostics.Should().BeEmpty();
        paste.Query.Should().Contain("\"Snowy\"", "names compile as free-text literals in the combined paste too");
    }

    [Fact]
    public void WantedMod_Requirements_PlainAndNamed()
    {
        new WantedMod("IncreasedLife6").Requirements().Should()
            .Equal([new PoeRegex.Core.ModifierRequirement("IncreasedLife6")]);
        new WantedMod("x", ["Snowy", "Hailing"]).Requirements().Should()
            .Equal([new PoeRegex.Core.ModifierRequirement("Snowy"), new PoeRegex.Core.ModifierRequirement("Hailing")]);
    }
}

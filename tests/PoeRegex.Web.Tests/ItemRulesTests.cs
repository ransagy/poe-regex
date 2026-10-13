using PoeRegex.Core;
using PoeRegex.Web.Core;

namespace PoeRegex.Web.Tests;

/// <summary>
/// Enforcement: the five real fixtures pass with zero errors; synthetic
/// violations (over-cap, group clash, wrong rarity) error; unknowns warn.
/// </summary>
public class ItemRulesTests
{
    private static readonly EngineService Engine = new();

    private static IReadOnlyList<RuleViolation> ValidateFixture(string text)
    {
        var item = ItemTextParser.Parse(text);
        var classId = Engine.ResolveClassId(item.ItemClass);
        var lines = new List<RuleLine>();
        foreach (var block in item.Mods)
        {
            foreach (var line in block.Lines)
            {
                string? selected = null;
                if (block.Kind != "implicit")
                {
                    var map = Engine.MapLineToMods(classId, line.CanonicalText, line.Rolls);
                    selected = map.ModIds.FirstOrDefault(id =>
                        Engine.FindMod(id)?.Name == block.Name);
                    selected.Should().NotBeNull($"'{line.Text}' should resolve to '{block.Name}'");
                }
                lines.Add(new RuleLine(block.Kind, selected));
            }
        }
        return ItemRules.Validate(Engine, lines, item.Rarity);
    }

    [Theory]
    [InlineData("Belt")]
    [InlineData("Talisman")]
    [InlineData("Crossbow")]
    [InlineData("Staff")]
    [InlineData("Mace")]
    public void RealFixtures_PassWithZeroErrors(string fixture)
    {
        var text = typeof(FixtureTexts).GetField(
            fixture,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null) as string;
        var violations = ValidateFixture(text!);
        violations.Where(v => v.Level == "error").Should().BeEmpty();
    }

    private static (string A, string B) SameGroupPrefixPair(string classId)
    {
        var byGroup = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var mod in Engine.ModsForClass(classId))
        {
            if (mod.GenerationType != "prefix") continue;
            foreach (var g in Engine.FindMod(mod.Id)?.Groups ?? [])
            {
                if (byGroup.TryGetValue(g, out var other) && other != mod.Id)
                    return (other, mod.Id);
                byGroup[g] = mod.Id;
            }
        }
        throw new InvalidOperationException($"no same-group prefix pair on {classId}");
    }

    [Fact]
    public void Rare_FourPrefixes_Error()
    {
        var mods = Engine.ModsForClass("Ring")
            .Where(m => m.GenerationType == "prefix").Take(4).Select(m => m.Id).ToList();
        mods.Should().HaveCount(4);
        var violations = ItemRules.Validate(Engine,
            mods.Select(id => new RuleLine("prefix", id)), "Rare");
        violations.Should().Contain(v => v.Level == "error" && v.Message.Contains("prefix"));
    }

    [Fact]
    public void SameGroupPair_ClashError()
    {
        var (a, b) = SameGroupPrefixPair("Ring");
        var violations = ItemRules.Validate(Engine,
            [new RuleLine("prefix", a), new RuleLine("prefix", b)], "Rare");
        violations.Should().Contain(v => v.Level == "error" && v.Message.Contains("clash"));
    }

    [Fact]
    public void Magic_TwoPrefixes_Error_NormalAnyAffix_Error()
    {
        var mods = Engine.ModsForClass("Ring")
            .Where(m => m.GenerationType == "prefix").Take(2).Select(m => m.Id).ToList();
        ItemRules.Validate(Engine, mods.Select(id => new RuleLine("prefix", id)), "Magic")
            .Should().Contain(v => v.Level == "error");
        ItemRules.Validate(Engine, [new RuleLine("suffix", mods[0])], "Normal")
            .Should().Contain(v => v.Level == "error");
    }

    [Fact]
    public void UnknownRarity_Warns_UnknownKind_Warns()
    {
        ItemRules.Validate(Engine, [new RuleLine("prefix", null)], "Unique")
            .Should().Contain(v => v.Level == "warning");
        ItemRules.Validate(Engine, [new RuleLine("enchant", null)], "Rare")
            .Should().Contain(v => v.Level == "warning");
    }
}

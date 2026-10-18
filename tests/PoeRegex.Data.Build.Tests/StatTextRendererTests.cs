using PoeRegex.Data.Build.RePoE;
using PoeRegex.Data.Build.Rendering;

namespace PoeRegex.Data.Build.Tests;

public class StatTextRendererTests
{
    private static StatTextRenderer CreateRenderer(string handlersJson, string translationsJson)
    {
        var dir = Path.Combine(Path.GetTempPath(), "poe-regex-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var handlersPath = Path.Combine(dir, "handlers.json");
        var translationsPath = Path.Combine(dir, "translations.json");
        File.WriteAllText(handlersPath, handlersJson);
        File.WriteAllText(translationsPath, translationsJson);
        var handlers = RePoEJson.Load<Dictionary<string, RePoESchema.StatValueHandler>>(handlersPath);
        var translations = RePoEJson.Load<List<RePoESchema.StatTranslationEntry>>(translationsPath);
        return new StatTextRenderer(handlers, translations);
    }

    [Fact]
    public void FormatPlusPrefix()
    {
        var renderer = CreateRenderer("{}", """
[
  { "English": [ { "condition": [{}], "format": ["+#"], "index_handlers": [[]], "string": "{0} to maximum Life" } ],
    "ids": ["maximum_life"] }
]
""");
        var mod = new RePoESchema.Mod
        {
            Domain = "item",
            GenerationType = "prefix",
            Stats = [new RePoESchema.ModStat { Id = "maximum_life", Min = 20, Max = 30 }],
        };
        var lines = renderer.RenderMod(mod.Stats);
        lines.Should().ContainSingle();
        lines[0].Line.Should().Be("+30 to maximum Life");
        lines[0].RangeLine.Should().Be("+20–30 to maximum Life");
    }

    [Fact]
    public void MultiplyByHundredHandler()
    {
        var renderer = CreateRenderer(
            """{"multiply_hundred": { "type": "int", "multiplier": 100 }}""",
            """
[
  { "English": [ { "condition": [{}], "format": ["#%"], "index_handlers": [["multiply_hundred"]], "string": "{0} increased Attack Speed" } ],
    "ids": ["attack_speed_incr"] }
]
""");
        var mod = new RePoESchema.Mod
        {
            Domain = "item",
            GenerationType = "prefix",
            Stats = [new RePoESchema.ModStat { Id = "attack_speed_incr", Min = 0.2, Max = 0.25 }],
        };
        var lines = renderer.RenderMod(mod.Stats);
        lines.Should().ContainSingle();
        lines[0].Line.Should().Be("25% increased Attack Speed");
        lines[0].RangeLine.Should().Be("20–25% increased Attack Speed");
    }

    [Fact]
    public void MultiIndexTemplate()
    {
        var renderer = CreateRenderer("{}", """
[
  { "English": [ { "condition": [{}], "format": ["#", "#"], "index_handlers": [[], []],
                  "string": "Adds {0} to {1} Physical Damage to Attacks" } ],
    "ids": ["physical_damage_min", "physical_damage_max"] }
]
""");
        var mod = new RePoESchema.Mod
        {
            Domain = "item",
            GenerationType = "prefix",
            Stats =
            [
                new RePoESchema.ModStat { Id = "physical_damage_min", Min = 1, Max = 2 },
                new RePoESchema.ModStat { Id = "physical_damage_max", Min = 3, Max = 4 },
            ],
        };
        var lines = renderer.RenderMod(mod.Stats);
        lines.Should().ContainSingle();
        lines[0].Line.Should().Be("Adds 2 to 4 Physical Damage to Attacks");
        lines[0].RangeLine.Should().Be("Adds 1–2 to 3–4 Physical Damage to Attacks");
    }

    [Fact]
    public void MergedEntry_TreatsAbsentStatAsZero()
    {
        // Mirrors local_physical_damage_+% ↔ local_weapon_no_physical_damage:
        // the paired "no physical damage" stat only exists on uniques, but the
        // normal variant is keyed on it being absent (value 0).
        var renderer = CreateRenderer("{}", """
[
  { "English": [
      { "condition": [{}, {"min": 1}], "format": ["ignore", "ignore"], "index_handlers": [[], []],
        "string": "No [Physical] Damage" },
      { "condition": [{"min": 1}, {"min": 0, "max": 0}], "format": ["#", "ignore"], "index_handlers": [[], []],
        "string": "{0}% increased [Physical] Damage" }
    ],
    "ids": ["local_physical_damage_+%", "local_weapon_no_physical_damage"] }
]
""");
        var mod = new RePoESchema.Mod
        {
            Domain = "item",
            GenerationType = "prefix",
            Stats = [new RePoESchema.ModStat { Id = "local_physical_damage_+%", Min = 40, Max = 49 }],
        };

        var lines = renderer.RenderMod(mod.Stats);

        lines.Should().ContainSingle();
        lines[0].Line.Should().Be("49% increased Physical Damage");
        lines[0].RangeLine.Should().Be("40–49% increased Physical Damage");
    }

    [Fact]
    public void TighterConditionWins()
    {
        // RePoE's stat_translations docs: each variant has a condition per id;
        // the variant that pins the value most precisely describes it.
        var renderer = CreateRenderer("{}", """
[
  { "English": [
      { "condition": [{}], "format": ["#"], "index_handlers": [[]], "string": "generic {0}" },
      { "condition": [{"min": 1}], "format": ["#"], "index_handlers": [[]], "string": "bounded {0}" },
      { "condition": [{"min": 1, "max": 10}], "format": ["#"], "index_handlers": [[]], "string": "tight {0}" }
    ],
    "ids": ["value_stat"] }
]
""");
        var mod = new RePoESchema.Mod
        {
            Domain = "item",
            GenerationType = "prefix",
            Stats = [new RePoESchema.ModStat { Id = "value_stat", Min = 5, Max = 5 }],
        };
        var lines = renderer.RenderMod(mod.Stats);
        lines.Should().ContainSingle();
        lines[0].Line.Should().Be("tight 5");
    }

    [Fact]
    public void EqualConditionsKeepTheFirstVariant()
    {
        var renderer = CreateRenderer("{}", """
[
  { "English": [
      { "condition": [{}], "format": ["#"], "index_handlers": [[]], "string": "first {0}" },
      { "condition": [{}], "format": ["#"], "index_handlers": [[]], "string": "second {0}" }
    ],
    "ids": ["value_stat"] }
]
""");
        var mod = new RePoESchema.Mod
        {
            Domain = "item",
            GenerationType = "prefix",
            Stats = [new RePoESchema.ModStat { Id = "value_stat", Min = 5, Max = 5 }],
        };
        renderer.RenderMod(mod.Stats).Should().ContainSingle()
            .Which.Line.Should().Be("first 5");
    }

    [Fact]
    public void MaxRollDrivesVariantSelection()
    {
        // The displayed line is the max roll, and the variant conditions are
        // matched against that same value (a range's high end).
        var renderer = CreateRenderer("{}", """
[
  { "English": [
      { "condition": [{"max": 15}], "format": ["#"], "index_handlers": [[]], "string": "low {0}" },
      { "condition": [{"min": 16}], "format": ["#"], "index_handlers": [[]], "string": "high {0}" }
    ],
    "ids": ["value_stat"] }
]
""");
        var mod = new RePoESchema.Mod
        {
            Domain = "item",
            GenerationType = "prefix",
            Stats = [new RePoESchema.ModStat { Id = "value_stat", Min = 10, Max = 20 }],
        };
        renderer.RenderMod(mod.Stats).Should().ContainSingle()
            .Which.Line.Should().Be("high 20");
    }

    [Fact]
    public void Pattern_NormalizesDigits()
    {
        var renderer = CreateRenderer("{}", """
[
  { "English": [ { "condition": [{}], "format": ["+#"], "index_handlers": [[]], "string": "{0} to maximum Life" } ],
    "ids": ["maximum_life"] }
]
""");
        var mod = new RePoESchema.Mod
        {
            Domain = "item",
            GenerationType = "prefix",
            Stats = [new RePoESchema.ModStat { Id = "maximum_life", Min = 20, Max = 30 }],
        };
        var lines = renderer.RenderMod(mod.Stats);
        lines[0].Pattern.Should().Be("+\x01 to maximum Life");
        lines[0].MinValue.Should().Be(20);
        lines[0].MaxValue.Should().Be(30);
    }
}

public class NormalizeTests
{
    [Fact]
    public void StripBracketMarkup_KeepsDisplayAlternative()
    {
        StatTextRenderer.StripBracketMarkup("+8 to [Strength|Strength]").Should().Be("+8 to Strength");
        StatTextRenderer.StripBracketMarkup("+8 to maximum [EnergyShield|Energy Shield]")
            .Should().Be("+8 to maximum Energy Shield");
        StatTextRenderer.StripBracketMarkup("{0}% increased [ElementalDamage|Elemental] Damage with [Attack|Attacks]")
            .Should().Be("{0}% increased Elemental Damage with Attacks");
        StatTextRenderer.StripBracketMarkup("Consuming [Glory] grants you {0}%").Should().Be("Consuming Glory grants you {0}%");
        StatTextRenderer.StripBracketMarkup("no brackets").Should().Be("no brackets");
    }

    [Fact]
    public void NormalizeDigits_CollapsesRuns()
    {
        StatTextRenderer.NormalizeDigits("Adds 12 to 34 Physical Damage").Should().Be("Adds \x01 to \x01 Physical Damage");
        StatTextRenderer.NormalizeDigits("no digits").Should().Be("no digits");
    }
}

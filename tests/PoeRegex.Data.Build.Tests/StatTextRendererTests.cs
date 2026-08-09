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
    public void StripBracketMarkup_TakesFirstAlternative()
    {
        StatTextRenderer.StripBracketMarkup("+8 to [Strength|Strength]").Should().Be("+8 to Strength");
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

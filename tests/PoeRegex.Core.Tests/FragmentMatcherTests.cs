using System.Text.RegularExpressions;
using PoeRegex.Core.Compilation;
using PoeRegex.Core.Data;

namespace PoeRegex.Core.Tests;

/// <summary>
/// The selector validates candidates with a purpose-built matcher; these tests
/// prove it agrees with .NET's regex engine on the real catalog, so an audit
/// result cannot be a matcher artefact.
/// </summary>
public class FragmentMatcherTests
{
    [Fact]
    public void Matcher_AgreesWithRegex_OnSelectedFragmentsAcrossTheCatalog()
    {
        var corpus = RealCatalog.Corpus();
        var selector = new FragmentSelector(corpus, PoeRegexCompiler.StrategyFor(GameTarget.PoE2));
        var scopes = new List<string?> { null };
        scopes.AddRange(corpus.Catalog.ItemClasses
            .Where(c => corpus.Mods.Any(m => m.Classes.Contains(c.Id)))
            .Select(c => c.Id));

        var checkedCount = 0;
        foreach (var scope in scopes)
        {
            var lines = scope is null
                ? corpus.RealLinesGlobal()
                : corpus.RealLinesForClass(scope);
            foreach (var pattern in lines.Select(x => x.Pattern).Distinct())
            {
                var fragment = selector.Select(pattern, scope);
                if (fragment is null) continue;
                var matcher = FragmentMatcher.Parse(fragment);
                foreach (var (_, real) in lines)
                {
                    var expected = Regex.IsMatch(real, fragment, RegexOptions.IgnoreCase);
                    matcher.IsMatch(real).Should().Be(expected,
                        $"matcher disagrees with regex for '{fragment}' on '{real}'");
                    checkedCount++;
                }
            }
        }
        checkedCount.Should().BeGreaterThan(10_000);
    }

    [Theory]
    [InlineData(@"\d Cold Damage$", "Adds 4 to 8 Cold Damage")]
    [InlineData(@"^\d+% inc.+al Damage Bonus$", "12% increased Critical Damage Bonus")]
    [InlineData(@"^.% inc.+Life", "4% increased maximum Life")]
    [InlineData(@"to.+Life", "+ to maximum Life")]
    [InlineData(@"Allies.*ces$", "Allies in your Presence have +8% to all Elemental Resistances")]
    [InlineData(@"^Allies.*Resistances$", "Allies in your Presence have +8% to all Elemental Resistances")]
    [InlineData(@"^Ad.*d Damage$", "Adds 2 to 4 Cold Damage")]
    [InlineData(@"d Armour$", "24% increased Armour")]
    [InlineData(@"Cold.*Attacks$", "Adds 2 to 4 Cold damage to Attacks")]
    public void Matcher_HandlesSelectedShapes(string fragment, string line)
    {
        FragmentMatcher.Parse(fragment).IsMatch(line).Should().Be(
            Regex.IsMatch(line, fragment, RegexOptions.IgnoreCase));
    }
}

using PoeRegex.Core.Data;
using PoeRegex.Web.Core;
using Xunit.Abstractions;

namespace PoeRegex.Web.Tests;

/// <summary>
/// Flow-level audit of picks in both modes, over the real embedded catalog:
/// every family anchor compiles in simple mode and in its advanced slot, the
/// resulting strings recall the stat text, and the class anchor in a combined
/// paste excludes a different class carrying the same text. Slotted pastes are
/// also checked pairwise.
/// </summary>
public class FragmentFlowAuditTests
{
    private static readonly EngineService Engine = new();
    private readonly ITestOutputHelper _output;

    public FragmentFlowAuditTests(ITestOutputHelper output) => _output = output;

    private static string ClassLine(string classId) =>
        $"Item Class: {Engine.Classes.First(c => c.Id == classId).Name}";

    private static IReadOnlyList<string> StatLines(ModEntry mod) =>
        mod.StatLines.Select(l => l.Line).ToList();

    [Fact]
    public void EveryFamilyAnchor_WorksInSimpleAndAdvanced()
    {
        var failures = new List<string>();
        var checkedCount = 0;
        var simpleFamilies = ModFamilies.Group(Engine.PlayableMods());
        _output.WriteLine($"simple families: {simpleFamilies.Count}");

        foreach (var family in simpleFamilies)
        {
            var anchor = family.Anchor;
            if (anchor.StatLines.Count == 0) continue;
            var lines = StatLines(anchor);
            var pick = family.Pick();
            var simple = Engine.Compile(pick.Requirements(), []);
            if (simple.Diagnostics.Count > 0)
                failures.Add($"simple {family.Key}: {string.Join("; ", simple.Diagnostics.Select(d => d.Code))}");
            if (!ItemMatcher.Match(simple.Query, lines).Match)
                failures.Add($"simple {family.Key}: query does not match '{string.Join(" / ", lines)}'");
            // A family pick matches every tier of the family, not just the anchor.
            foreach (var member in family.Members.Where(m => m.StatLines.Count > 0))
            {
                if (!ItemMatcher.Match(simple.Query, StatLines(member)).Match)
                    failures.Add($"simple {family.Key}: misses tier {member.Id}");
            }
            checkedCount++;
        }

        foreach (var cls in Engine.InGameClasses())
        {
            var families = ModFamilies.Group(Engine.ModsForClass(cls.Id));
            _output.WriteLine($"{cls.Id}: {families.Count} families");
            foreach (var family in families)
            {
                var anchor = family.Anchor;
                if (anchor.StatLines.Count == 0) continue;
                var lines = StatLines(anchor);
                var pick = family.Pick();
                var paste = Engine.Combine(
                    new (string, string, IEnumerable<WantedMod>)[] { ("slot", cls.Id, new[] { pick }) },
                    []);
                if (paste.Diagnostics.Count > 0)
                    failures.Add($"{cls.Id}/{family.Key}: {string.Join("; ", paste.Diagnostics.Select(d => d.Code))}");
                if (!ItemMatcher.Match(paste.Query, lines.Prepend(ClassLine(cls.Id))).Match)
                    failures.Add($"{cls.Id}/{family.Key}: paste does not match its own item");
                // The class anchor must exclude a different class carrying the same text.
                var other = Engine.InGameClasses().First(c => c.Id != cls.Id);
                if (ItemMatcher.Match(paste.Query, lines.Prepend(ClassLine(other.Id))).Match)
                    failures.Add($"{cls.Id}/{family.Key}: paste leaks to {other.Id}");
                checkedCount++;
            }
        }

        _output.WriteLine($"family flow cases: {checkedCount}");
        foreach (var failure in failures.Take(25)) _output.WriteLine($"FAIL {failure}");
        failures.Take(25).Should().BeEmpty($"{failures.Count} family-flow failures");
        checkedCount.Should().BeGreaterThan(700, "the catalog should offer hundreds of family picks");
    }

    [Fact]
    public void EveryClassPair_ComposesCleanly_WithAnchorsAndRecall()
    {
        var classes = Engine.InGameClasses().Select(c => c.Id).ToList();
        var representative = classes.ToDictionary(
            c => c,
            c => ModFamilies.Group(Engine.ModsForClass(c))
                .First(f => f.Anchor.StatLines.Count > 0).Anchor.Id,
            StringComparer.Ordinal);

        var failures = new List<string>();
        var checkedCount = 0;
        for (var i = 0; i < classes.Count; i++)
        for (var j = i + 1; j < classes.Count; j++)
        {
            var a = classes[i];
            var b = classes[j];
            var paste = Engine.Combine(
                new (string, string, IEnumerable<WantedMod>)[]
                {
                    ("a", a, new[] { new WantedMod(representative[a]) }),
                    ("b", b, new[] { new WantedMod(representative[b]) }),
                }, []);
            if (paste.Diagnostics.Count > 0)
                failures.Add($"{a}/{b}: {string.Join("; ", paste.Diagnostics.Select(d => d.Code))}");

            var linesA = StatLines(Engine.FindMod(representative[a])!);
            var linesB = StatLines(Engine.FindMod(representative[b])!);
            if (!ItemMatcher.Match(paste.Query, linesA.Prepend(ClassLine(a))).Match)
                failures.Add($"{a}/{b}: does not match {a}");
            if (!ItemMatcher.Match(paste.Query, linesB.Prepend(ClassLine(b))).Match)
                failures.Add($"{a}/{b}: does not match {b}");
            // A selected class without its own picked mod must not pass its group.
            if (ItemMatcher.Match(paste.Query, [ClassLine(a), "Rarity: Rare"]).Match)
                failures.Add($"{a}/{b}: {a} passes without its mod");
            // A third class is excluded by the anchor.
            var third = classes.First(c => c != a && c != b);
            if (ItemMatcher.Match(paste.Query, linesA.Prepend(ClassLine(third))).Match)
                failures.Add($"{a}/{b}: leaks to {third}");
            checkedCount++;
        }

        _output.WriteLine($"class pairs: {checkedCount}");
        failures.Take(25).Should().BeEmpty($"{failures.Count} pair failures");
        checkedCount.Should().BeGreaterThan(200);
    }
}

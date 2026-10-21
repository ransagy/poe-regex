using PoeRegex.Core.Compilation;
using PoeRegex.Core.Data;
using PoeRegex.Core.Search;

namespace PoeRegex.Core.Tests;

/// <summary>
/// Property tests over the real derived catalogs (checked into data/catalogs).
/// These validate the catalog builder output end-to-end, not just the fixtures.
/// </summary>
public class CatalogPropertyTests
{
    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "data", "catalogs")))
        {
            dir = Path.GetDirectoryName(dir);
        }
        return dir ?? throw new DirectoryNotFoundException("repo root not found");
    }

    public static IEnumerable<object[]> Catalogs()
    {
        yield return new object[] { GameTarget.PoE1 };
        yield return new object[] { GameTarget.PoE2 };
    }

    [Theory]
    [MemberData(nameof(Catalogs))]
    public void EveryCatalogFingerprint_IsSafeOnItsClass(GameTarget game)
    {
        var path = Path.Combine(RepoRoot(), "data", "catalogs", $"{game.ToRuleKey()}.catalog.json");
        var corpus = new EngineCorpus(new FileCatalogSource(path).Load(game));

        var checkedCount = 0;
        foreach (var mod in corpus.Mods)
        {
            foreach (var line in mod.StatLines)
            {
                foreach (var (cls, fp) in line.Fingerprints)
                {
                    // The fingerprint must match at least one line of this pattern and
                    // no line of any other pattern in the class corpus.
                    corpus.FragmentSafeOnClass(cls, fp, new[] { line.Pattern }, includeSafety: false, includeUniversal: false).Should().BeTrue(
                        $"fingerprint '{fp}' of {mod.Id} line '{line.Line}' must be safe on class {cls}");
                    checkedCount++;
                }
            }
        }
        checkedCount.Should().BeGreaterThan(1000, "expected a meaningful number of fingerprints to validate");
    }

    [Theory]
    [MemberData(nameof(Catalogs))]
    public void EveryClassWithMods_HasBaseFingerprint(GameTarget game)
    {
        var path = Path.Combine(RepoRoot(), "data", "catalogs", $"{game.ToRuleKey()}.catalog.json");
        var corpus = new EngineCorpus(new FileCatalogSource(path).Load(game));
        var withMods = corpus.Catalog.ItemClasses
            .Where(c => corpus.Mods.Any(m => m.Classes.Contains(c.Id)))
            .ToList();
        foreach (var cls in withMods)
        {
            var fp = corpus.ClassFingerprint(cls.Id);
            fp.Should().NotBeNullOrEmpty($"class {cls.Id} ({cls.Name}) needs a base fingerprint");
            var classLinePattern = CatalogLineNormalizer.Normalize($"Item Class: {cls.Name}");
            corpus.FragmentSafeOnClass(cls.Id, fp!, new[] { classLinePattern }, includeSafety: false, includeUniversal: false).Should().BeTrue(
                $"base fingerprint '{fp}' of class {cls.Id} must match only its own class line");
        }
    }

    /// <summary>
    /// Regression guard for the digit-placeholder leak and unresolved picks:
    /// every mod compiles on every class, the emitted fragment contains no
    /// control character, and it matches one of the mod's real copy-text lines.
    /// The fragment audit separately proves recall and false-positive safety.
    /// </summary>
    [Theory]
    [MemberData(nameof(Catalogs))]
    public void EveryGeneratedSingleModQuery_ResolvesAndAvoidsPlaceholder(GameTarget game)
    {
        var path = Path.Combine(RepoRoot(), "data", "catalogs", $"{game.ToRuleKey()}.catalog.json");
        var corpus = new EngineCorpus(new FileCatalogSource(path).Load(game));
        var compiler = new PoeRegexCompiler(corpus, PoeRegexCompiler.StrategyFor(game));
        var placeholder = new string(SuffixIndex.DigitPlaceholder, 1);
        var quoted = new System.Text.RegularExpressions.Regex("\"([^\"]*)\"");

        var checkedCount = 0;
        var unresolvedCount = 0;
        foreach (var mod in corpus.Mods)
        {
            foreach (var cls in mod.Classes)
            {
                var result = compiler.Compile(QuerySpecification.For(game) with
                {
                    ItemClass = cls,
                    IncludedModifiers = [new ModifierRequirement(mod.Id)],
                });
                if (result.Diagnostics.Any(d => d.Code == "unresolved-modifier"))
                {
                    // A class scope may deliberately leave a mixed-shape stat
                    // unresolved. It must carry a documented reason for this
                    // class; a silent gap is a data bug.
                    mod.StatLines.Should().Contain(
                        sl => !string.IsNullOrWhiteSpace(corpus.PrecomputedUnresolvedReason(sl.Pattern, cls)),
                        $"mod {mod.Id} on {cls} is unresolved without a documented reason");
                    unresolvedCount++;
                    continue;
                }
                result.Query.Should().NotContain(
                    placeholder,
                    $"query for {mod.Id} on {cls} must not contain the digit placeholder (got '{result.Query}')");
                var fragment = quoted.Match(result.Query);
                fragment.Success.Should().BeTrue($"query for {mod.Id} on {cls} must carry a quoted fragment");
                var text = fragment.Groups[1].Value;
                mod.StatLines.Any(sl => System.Text.RegularExpressions.Regex.IsMatch(
                        AdvancedStatLine.For(sl), text, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    .Should().BeTrue($"fragment '{text}' of {mod.Id} must match one of its copy-text lines");
                checkedCount++;
            }
        }
        checkedCount.Should().BeGreaterThan(1000, "expected a meaningful number of queries to validate");
        unresolvedCount.Should().BeLessThan(500, "deliberate unresolved scopes need review");
    }

    /// <summary>
    /// Global (no class) queries resolve every catalog mod to a fragment with
    /// no control char; the audit proves recall/safety over the whole corpus.
    /// </summary>
    [Theory]
    [MemberData(nameof(Catalogs))]
    public void EveryGlobalQuery_ResolvesToAMatchingFragment(GameTarget game)
    {
        var path = Path.Combine(RepoRoot(), "data", "catalogs", $"{game.ToRuleKey()}.catalog.json");
        var corpus = new EngineCorpus(new FileCatalogSource(path).Load(game));
        var compiler = new PoeRegexCompiler(corpus, PoeRegexCompiler.StrategyFor(game));
        var placeholder = new string(SuffixIndex.DigitPlaceholder, 1);

        var checkedCount = 0;
        var unresolvedCount = 0;
        foreach (var mod in corpus.Mods)
        {
            var result = compiler.Compile(QuerySpecification.For(game) with
            {
                IncludedModifiers = [new ModifierRequirement(mod.Id)],
            });
            if (result.Diagnostics.Any(d => d.Code == "unresolved-modifier"))
            {
                // A global pattern may be deliberately unresolved when no
                // line-local guard exists (Movement Speed). It must carry a
                // documented reason in the table, never disappear silently.
                mod.StatLines.Should().Contain(
                    sl => !string.IsNullOrWhiteSpace(corpus.PrecomputedUnresolvedReason(sl.Pattern, null)),
                    $"global mod {mod.Id} is unresolved without a documented reason");
                unresolvedCount++;
                continue;
            }

            result.Query.Should().NotContain(placeholder, $"global query for {mod.Id} must not contain the digit placeholder (got '{result.Query}')");

            var fragment = result.Query.Trim().Trim('"');
            fragment.Should().NotBeNullOrEmpty($"global query for {mod.Id} must resolve to a fragment");
            var matches = mod.StatLines.Any(sl => System.Text.RegularExpressions.Regex.IsMatch(AdvancedStatLine.For(sl), fragment));
            matches.Should().BeTrue($"global fragment '{fragment}' of {mod.Id} must match one of its copy-text lines");
            checkedCount++;
        }
        unresolvedCount.Should().BeLessThan(400, "deliberate unresolved globals need review");
        checkedCount.Should().BeGreaterThan(1000, "expected every mod to resolve globally");
    }

    /// <summary>
    /// Regression guard for the PoE2 stat-text rendering fixes: bracket markup
    /// must render its display alternative (never the schema key), and mods whose
    /// translation entry pairs a stat the mod does not carry must still render.
    /// </summary>
    [Fact]
    public void PoE2Catalog_RendersDisplayTextNotSchemaKeys()
    {
        var path = Path.Combine(RepoRoot(), "data", "catalogs", "poe2.catalog.json");
        var catalog = new FileCatalogSource(path).Load(GameTarget.PoE2);

        // The local "% increased Physical Damage" family (Heavy … Merciless).
        catalog.Mods
            .Where(m => m.Classes.Contains("Two Hand Sword"))
            .SelectMany(m => m.StatLines)
            .Should().Contain(sl => sl.RangeLine.Contains("increased Physical Damage"));

        // Bracket markup schema keys must never leak into display text.
        string[] keys =
        [
            "EnergyShield", "HitDamage", "BuffMagnitude", "CriticalDamageBonus",
            "LowLife", "MaximumResistances", "ElementalDamage", "AccuracyRating",
        ];
        var leak = new System.Text.RegularExpressions.Regex(@"\b(" + string.Join('|', keys) + @")\b");
        foreach (var mod in catalog.Mods)
        {
            foreach (var sl in mod.StatLines)
            {
                leak.IsMatch(sl.Line).Should().BeFalse($"{mod.Id} line '{sl.Line}' leaks a bracket schema key");
                leak.IsMatch(sl.RangeLine).Should().BeFalse($"{mod.Id} range '{sl.RangeLine}' leaks a bracket schema key");
            }
        }
    }

}

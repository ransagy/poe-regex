using PoeRegex.Web.Core;

namespace PoeRegex.Web.Tests;

/// <summary>
/// Sourced families that compile to the same search fragment as a normal,
/// implicit, corrupted or desecrated family are display duplicates. Keep the
/// higher-priority source; an unfiltered list should not show the same stat
/// several times.
/// </summary>
public class SourceDedupeTests
{
    private static ModFamily Family(
        string key, string source, string display, string kind = "prefix") =>
        new(key, kind == "prefix", [])
        {
            Source = source,
            Kind = kind,
            DisplayLines = [display],
        };

    private static IReadOnlyList<string> Keys(
        IReadOnlyList<ModFamily> families, Func<ModFamily, string?> fragmentOf) =>
        ModFamilies.DedupeSources(families, fragmentOf).Select(f => f.Key).ToList();

    [Fact]
    public void DedupeSources_KeepsNormalOverEverySourcedVariant()
    {
        const string display = "% increased Mana Regeneration Rate";
        var families = new[]
        {
            Family("normal", "", display),
            Family("implicit", "implicit", display, "implicit"),
            Family("corrupted", "corrupted", display, "corrupted"),
            Family("desecrated", "desecrated", display, "suffix"),
            Family("augment", "augment", display, "augment"),
        };

        Keys(families, _ => "same").Should().Equal(["normal"]);
    }

    [Fact]
    public void DedupeSources_KeepsImplicitWhenThereIsNoNormal()
    {
        const string display = "+ to maximum Runic Ward";
        var families = new[]
        {
            Family("implicit", "implicit", display, "implicit"),
            Family("augment", "augment", display, "augment"),
        };

        Keys(families, _ => "same").Should().Equal(["implicit"]);
    }

    [Fact]
    public void DedupeSources_KeepsBothNormalGenerationKinds()
    {
        const string display = "% increased Rarity of Items found";
        var families = new[]
        {
            Family("prefix", "", display),
            Family("suffix", "", display, "suffix"),
        };

        Keys(families, _ => "same").Should().Equal(["prefix", "suffix"]);
    }

    [Fact]
    public void DedupeSources_KeepsSourcedVariantsWithDifferentFragments()
    {
        const string display = "% increased Mana Regeneration Rate";
        var families = new[]
        {
            Family("implicit", "implicit", display, "implicit"),
            Family("augment", "augment", display, "augment"),
        };

        Keys(families, f => f.Key == "implicit" ? "a" : "b")
            .Should().Equal(["implicit", "augment"]);
    }

    [Fact]
    public void DedupeSources_KeepsDifferentFragmentsShapesAndUnresolved()
    {
        const string display = "% increased Mana Regeneration Rate";
        var families = new[]
        {
            Family("normal", "", display),
            Family("different-fragment", "augment", display, "augment"),
            Family("different-shape", "augment", "+ to maximum Mana", "augment"),
            Family("unresolved", "augment", display, "augment"),
        };

        Keys(families, f => f.Key switch
        {
            "normal" => "a",
            "different-fragment" => "b",
            "different-shape" => "a",
            "unresolved" => null,
            _ => "a",
        }).Should().Equal(["normal", "different-fragment", "different-shape", "unresolved"]);
    }
}

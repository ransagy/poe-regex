using PoeRegex.Data.Build.RePoE;

namespace PoeRegex.Data.Build.Tests;

public class AugmentCatalogTests
{
    [Fact]
    public void EveryExportedTargetResolvesToClasses()
    {
        var root = RepoRoot();
        var raw = Path.Combine(root, "data", "raw", "poe2");
        var targets = AugmentCatalogBuilder.BuildTargetMap(
            RePoEJson.LoadBaseItems(Path.Combine(raw, "base_items.min.json")),
            RePoEJson.LoadItemClasses(Path.Combine(raw, "item_classes.min.json")));
        var augments = RePoEJson.LoadAugments(Path.Combine(raw, "augments.min.json"));

        var exported = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var augment in augments.Values)
        foreach (var category in augment.Categories.Values)
        {
            var target = AugmentCatalogBuilder.TargetText(category.Target);
            if (target.Length > 0) exported.Add(target);
        }

        exported.Should().NotBeEmpty();
        foreach (var target in exported)
            AugmentCatalogBuilder.ResolveTarget(targets, target).Should().NotBeEmpty(
                $"target '{target}' must map to at least one in-game class");
    }

    [Fact]
    public void WeaponAndAllEquipmentIncludeCasterWeapons()
    {
        var root = RepoRoot();
        var raw = Path.Combine(root, "data", "raw", "poe2");
        var targets = AugmentCatalogBuilder.BuildTargetMap(
            RePoEJson.LoadBaseItems(Path.Combine(raw, "base_items.min.json")),
            RePoEJson.LoadItemClasses(Path.Combine(raw, "item_classes.min.json")));

        targets["Weapon"].Should().Contain(["Wand", "Staff", "Sceptre", "Bow", "One Hand Mace"]);
        targets["All Equipment"].Should().Contain([
            "Wand", "Staff", "Sceptre", "Body Armour", "Helmet", "Gloves", "Boots", "Shield",
        ]);
        targets["All Equipment"].Should().NotContain(["Belt", "Amulet", "Ring", "Quiver"]);
        targets["Martial Weapon"].Should().NotContain(["Wand", "Staff", "Sceptre"]);
    }

    [Fact]
    public void CompoundTargetsResolveToTheirAlternatives()
    {
        var root = RepoRoot();
        var raw = Path.Combine(root, "data", "raw", "poe2");
        var targets = AugmentCatalogBuilder.BuildTargetMap(
            RePoEJson.LoadBaseItems(Path.Combine(raw, "base_items.min.json")),
            RePoEJson.LoadItemClasses(Path.Combine(raw, "item_classes.min.json")));

        AugmentCatalogBuilder.ResolveTarget(targets, "One Hand Mace or Quarterstaff")
            .Should().BeEquivalentTo(["One Hand Mace", "Warstaff"]);
        AugmentCatalogBuilder.ResolveTarget(targets, "Crossbow, Bow or Spear")
            .Should().BeEquivalentTo(["Crossbow", "Bow", "Spear"]);
        AugmentCatalogBuilder.ResolveTarget(targets, "Shields and Bucklers")
            .Should().BeEquivalentTo(["Shield", "Buckler"]);
        AugmentCatalogBuilder.ResolveTarget(targets, "Martial Weapon, Wand or Staff")
            .Should().Contain(["One Hand Mace", "Wand", "Staff"]);
        Assert.Throws<InvalidOperationException>(
            () => AugmentCatalogBuilder.ResolveTarget(targets, "Not a real target"));
    }

    [Fact]
    public void Build_EmitsOnlyEnabledTypesAndBondedRows()
    {
        var root = RepoRoot();
        var result = AugmentCatalogBuilder.Build(
            Path.Combine(root, "data", "raw", "poe2"));

        result.UnmappedTargets.Should().BeEmpty();
        result.Mods.Should().NotBeEmpty();
        result.Mods.Select(m => m.TypeName).Distinct(StringComparer.Ordinal)
            .Should().BeEquivalentTo(["Rune", "Soul Core", "Abyssal Eye"]);
        result.Mods.Should().Contain(m => m.Bonded);
        result.Mods.Should().Contain(m => !m.Bonded);
        result.Mods.Select(m => m.Id).Should().OnlyHaveUniqueItems();
        result.Mods.Should().OnlyContain(m =>
            m.Classes.Count > 0 &&
            m.Lines.Count > 0 &&
            m.Family.Length > 0);
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(
                    directory.FullName, "data", "catalogs", "poe2.catalog.json")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("repository root not found");
    }
}

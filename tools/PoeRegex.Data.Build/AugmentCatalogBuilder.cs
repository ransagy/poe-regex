using System.Globalization;
using System.Text.RegularExpressions;
using PoeRegex.Core.Data;
using PoeRegex.Data.Build.RePoE;
using PoeRegex.Data.Build.Rendering;

namespace PoeRegex.Data.Build;

/// <summary>
/// Builds selectable augment families from RePoE's augments.json. Augment
/// effects are conditional on the socketed item's target category, which the
/// export supplies as a separate category record. This pass enables runes,
/// Soul Cores, idols and Abyssal Eyes; jewels and the socket model stay in
/// their own phases.
/// </summary>
public static class AugmentCatalogBuilder
{
    public sealed record AugmentBuild(
        string Id,
        string Name,
        string Family,
        string TargetKey,
        string TargetDisplay,
        string TypeName,
        int RequiredLevel,
        IReadOnlyList<string> Classes,
        IReadOnlyList<StatTextRenderer.RenderedLine> Lines,
        bool Bonded = false);

    public sealed record Result(
        IReadOnlyList<AugmentBuild> Mods,
        int SkippedCategories,
        int RemovedDuplicateRows,
        IReadOnlyList<string> UnmappedTargets);

    private static readonly string[] EnabledTypes =
        ["Rune", "SoulCore", "Idol", "AbyssalEye"];

    private static readonly Regex NumberRe = new(
        @"[-+]?\d+(?:\.\d+)?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static Result Build(
        string rawDir,
        IReadOnlyDictionary<string, HashSet<string>>? coveredByClass = null)
    {
        var augments = RePoEJson.LoadAugments(Path.Combine(rawDir, "augments.min.json"));
        var baseItems = RePoEJson.LoadBaseItems(Path.Combine(rawDir, "base_items.min.json"));
        var itemClasses = RePoEJson.LoadItemClasses(Path.Combine(rawDir, "item_classes.min.json"));
        var targets = BuildTargetMap(baseItems, itemClasses);

        var candidates = new List<Candidate>();
        var skipped = 0;
        var unmapped = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var (path, augment) in augments
                     .OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (!EnabledTypes.Contains(augment.TypeId, StringComparer.Ordinal))
                continue;
            if (!baseItems.TryGetValue(path, out var baseItem)) continue;
            if (!string.Equals(baseItem.ReleaseState, "released", StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.IsNullOrWhiteSpace(baseItem.Name)) continue;
            if (baseItem.Name.StartsWith("[DNT", StringComparison.OrdinalIgnoreCase)) continue;

            var familyName = NormalizeFamilyName(baseItem.Name);
            var typeName = Clean(augment.TypeName);
            if (typeName.Length == 0) typeName = augment.TypeId ?? "Augment";
            var requiredLevel = augment.RequiredLevel > 0 ? augment.RequiredLevel : 0;

            foreach (var (_, category) in augment.Categories.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                var target = TargetText(category.Target);
                if (target.Length == 0)
                {
                    skipped++;
                    continue;
                }
                HashSet<string> classes;
                try
                {
                    classes = ResolveTarget(targets, target);
                }
                catch (InvalidOperationException)
                {
                    unmapped.Add(target);
                    skipped++;
                    continue;
                }
                if (classes.Count == 0)
                {
                    unmapped.Add(target);
                    skipped++;
                    continue;
                }

                var normalLines = RenderLines(category.StatText);
                var bondedLines = RenderLines(category.BondedStatText);
                if (normalLines.Count == 0 && bondedLines.Count == 0)
                {
                    skipped++;
                    continue;
                }

                var targetKey = TargetKey(target);
                var orderedClasses = classes.OrderBy(c => c, StringComparer.Ordinal).ToList();
                if (normalLines.Count > 0)
                {
                    candidates.Add(new Candidate(
                        false, augment.TypeId!, baseItem.Name.Trim(), familyName, targetKey,
                        target, typeName, requiredLevel, orderedClasses, normalLines, Leaf(path)));
                }
                if (bondedLines.Count > 0)
                {
                    candidates.Add(new Candidate(
                        true, augment.TypeId!, baseItem.Name.Trim(), familyName, targetKey,
                        target, typeName, requiredLevel, orderedClasses, bondedLines, Leaf(path)));
                }
            }
        }

        // Group tiers/qualities by their normalized family and target. The
        // same effect under two different augment names is rejected by the
        // sequential coverage pass below, so the catalog never carries two
        // families that search for exactly the same class lines.
        var families = candidates
            .GroupBy(c => (c.Bonded, c.TypeId, c.FamilyName, c.TargetKey))
            .Select(g => new FamilyGroup(
                g.Key.Bonded,
                g.Key.TypeId,
                g.Key.FamilyName,
                g.Key.TargetKey,
                g.First().TargetDisplay,
                g.First().TypeName,
                g.SelectMany(c => c.Lines)
                    .Select(l => l.Pattern)
                    .Distinct(StringComparer.Ordinal)
                    .ToList(),
                g.SelectMany(c => c.Classes)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(c => c, StringComparer.Ordinal)
                    .ToList(),
                g.OrderBy(c => c.RequiredLevel)
                    .ThenBy(c => c.Name, StringComparer.Ordinal)
                    .ToList()))
            .OrderBy(f => f.Bonded)
            .ThenBy(f => TypeOrder(f.TypeId))
            .ThenBy(f => f.TargetKey, StringComparer.Ordinal)
            .ThenBy(f => f.FamilyName, StringComparer.Ordinal)
            .ToList();

        var covered = coveredByClass is null
            ? new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
            : coveredByClass.ToDictionary(
                kv => kv.Key,
                kv => new HashSet<string>(kv.Value, StringComparer.Ordinal),
                StringComparer.Ordinal);
        var selected = new List<AugmentBuild>();
        var removed = 0;

        foreach (var family in families)
        {
            var classes = family.Classes
                .Where(cls => family.Patterns.Any(pattern =>
                    !covered.TryGetValue(cls, out var set) || !set.Contains(pattern)))
                .ToList();
            if (classes.Count == 0)
            {
                removed += family.Rows.Count;
                continue;
            }

            foreach (var row in family.Rows)
            {
                selected.Add(new AugmentBuild(
                    row.Bonded
                        ? $"bonded:{row.Leaf}:{row.TargetKey}"
                        : $"augment:{row.Leaf}:{row.TargetKey}",
                    row.Name,
                    $"{TypeSlug(row.TypeId)}:{row.FamilyName}:{row.TargetKey}",
                    row.TargetKey,
                    row.TargetDisplay,
                    row.TypeName,
                    row.RequiredLevel,
                    classes,
                    row.Lines,
                    row.Bonded));
            }

            foreach (var cls in classes)
            {
                if (!covered.TryGetValue(cls, out var set))
                {
                    set = new HashSet<string>(StringComparer.Ordinal);
                    covered[cls] = set;
                }
                foreach (var pattern in family.Patterns) set.Add(pattern);
            }
        }

        return new Result(selected, skipped, removed, unmapped.ToList());
    }

    /// <summary>
    /// Maps every display target category in augments.json to concrete
    /// in-game item classes and augment-eligible equipment sets. The mapping
    /// is derived from released base-item tags and item-class names, not from
    /// augment names.
    /// </summary>
    internal static Dictionary<string, HashSet<string>> BuildTargetMap(
        IReadOnlyDictionary<string, RePoESchema.BaseItem> baseItems,
        IReadOnlyDictionary<string, RePoESchema.ItemClass> itemClasses)
    {
        var classTags = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var released = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (_, item) in baseItems)
        {
            if (!string.Equals(item.ReleaseState, "released", StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.IsNullOrWhiteSpace(item.ItemClass)) continue;
            if (!itemClasses.ContainsKey(item.ItemClass)) continue;
            if (!GameItemClasses.IsInGame(item.ItemClass)) continue;
            released.Add(item.ItemClass);
            if (!classTags.TryGetValue(item.ItemClass, out var tags))
            {
                tags = new HashSet<string>(StringComparer.Ordinal);
                classTags[item.ItemClass] = tags;
            }
            foreach (var tag in item.Tags ?? []) tags.Add(tag);
        }

        var armour = classTags
            .Where(kv => kv.Value.Contains("armour"))
            .Select(kv => kv.Key)
            .ToHashSet(StringComparer.Ordinal);
        // Wand/staff/sceptre bases carry their own tags instead of `weapon`,
        // so the weapon set must include both groups.
        var caster = classTags
            .Where(kv => kv.Value.Overlaps(["wand", "staff", "sceptre"]))
            .Select(kv => kv.Key)
            .ToHashSet(StringComparer.Ordinal);
        var weapons = classTags
            .Where(kv => kv.Value.Contains("weapon"))
            .Select(kv => kv.Key)
            .Concat(caster)
            .ToHashSet(StringComparer.Ordinal);
        var wandOrStaff = new HashSet<string>(["Wand", "Staff"], StringComparer.Ordinal);
        var martial = weapons
            .Where(cls => !caster.Contains(cls))
            .ToHashSet(StringComparer.Ordinal);

        var map = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Armour"] = armour,
            ["Martial Weapon"] = martial,
            ["Wand or Staff"] = wandOrStaff,
            ["Weapon"] = weapons,
            ["All Equipment"] = weapons.Concat(armour).ToHashSet(StringComparer.Ordinal),
            ["Shields"] = new HashSet<string>(["Shield"], StringComparer.Ordinal),
            ["Shields and Bucklers"] = new HashSet<string>(["Shield", "Buckler"], StringComparer.Ordinal),
            ["Caster Weapon"] = caster,
        };

        foreach (var (id, cls) in itemClasses)
        {
            if (!released.Contains(id)) continue;
            var set = new HashSet<string>([id], StringComparer.Ordinal);
            if (!string.IsNullOrWhiteSpace(cls.Name)) map[cls.Name] = set;
            map[id] = set;
        }

        map["Quarterstaff"] = new HashSet<string>(["Warstaff"], StringComparer.Ordinal);
        return map;
    }

    /// <summary>Resolve a target string, including comma/or compounds.</summary>
    internal static HashSet<string> ResolveTarget(
        IReadOnlyDictionary<string, HashSet<string>> targets, string target)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var chunk in target.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var part in chunk.Split(" or ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!targets.TryGetValue(part, out var classes) || classes.Count == 0)
                    throw new InvalidOperationException($"Unmapped augment target '{part}'.");
                result.UnionWith(classes);
            }
        }
        return result;
    }

    private static IEnumerable<string> SplitLines(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static List<StatTextRenderer.RenderedLine> RenderLines(IReadOnlyList<string>? text) =>
        text is null
            ? []
            : text
                .SelectMany(SplitLines)
                .Select(ToRenderedLine)
                .Where(line => line is not null)
                .Select(line => line!)
                .ToList();

    private static StatTextRenderer.RenderedLine? ToRenderedLine(string text)
    {
        var clean = Clean(text);
        if (clean.Length == 0) return null;

        var numbers = NumberRe.Matches(clean)
            .Select(m => double.TryParse(m.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
                ? v
                : (double?)null)
            .Where(v => v is not null)
            .Select(v => v!.Value)
            .ToList();
        var min = numbers.Count > 0 ? numbers.Min() : 0;
        var max = numbers.Count > 0 ? numbers.Max() : 0;
        // Augment tiers are display-only in this pass. Compile the
        // value-agnostic effect shape, so an augment line shares the normal
        // stat family instead of becoming foreign text for it. The tier rows
        // still show the concrete name and value.
        return new StatTextRenderer.RenderedLine(
            clean, clean, StatTextRenderer.NormalizeDigits(clean), min, max);
    }

    private static string Clean(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? ""
            : StatTextRenderer.StripBracketMarkup(text).Trim();

    private static string NormalizeFamilyName(string name)
    {
        var value = name.Trim();
        foreach (var prefix in new[] { "Lesser ", "Greater ", "Perfect " })
        {
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                value = value[prefix.Length..];
                break;
            }
        }
        return value;
    }

    private static string TypeSlug(string typeId) => typeId switch
    {
        "SoulCore" => "soulcore",
        "Idol" => "idol",
        "AbyssalEye" => "abyssaleye",
        _ => "rune",
    };

    private static int TypeOrder(string typeId) => typeId switch
    {
        "Rune" => 0,
        "SoulCore" => 1,
        "Idol" => 2,
        "AbyssalEye" => 3,
        _ => 4,
    };

    internal static string TargetText(System.Text.Json.JsonElement target) =>
        target.ValueKind switch
        {
            System.Text.Json.JsonValueKind.String => Clean(target.GetString()),
            System.Text.Json.JsonValueKind.Array => string.Join(", ",
                target.EnumerateArray()
                    .Where(x => x.ValueKind == System.Text.Json.JsonValueKind.String)
                    .Select(x => Clean(x.GetString()))
                    .Where(x => x.Length > 0)),
            _ => "",
        };

    private static string TargetKey(string target) =>
        target.Trim().ToLowerInvariant()
            .Replace(" / ", "-", StringComparison.Ordinal)
            .Replace(' ', '-');

    private static string Leaf(string path)
    {
        var slash = path.LastIndexOf('/');
        return slash >= 0 ? path[(slash + 1)..] : path;
    }

    private sealed record Candidate(
        bool Bonded,
        string TypeId,
        string Name,
        string FamilyName,
        string TargetKey,
        string TargetDisplay,
        string TypeName,
        int RequiredLevel,
        IReadOnlyList<string> Classes,
        IReadOnlyList<StatTextRenderer.RenderedLine> Lines,
        string Leaf);

    private sealed record FamilyGroup(
        bool Bonded,
        string TypeId,
        string FamilyName,
        string TargetKey,
        string TargetDisplay,
        string TypeName,
        IReadOnlyList<string> Patterns,
        IReadOnlyList<string> Classes,
        IReadOnlyList<Candidate> Rows);
}

using System.Globalization;
using System.Text.RegularExpressions;
using PoeRegex.Core.Data;
using PoeRegex.Data.Build.RePoE;
using PoeRegex.Data.Build.Rendering;

namespace PoeRegex.Data.Build;

/// <summary>
/// Builds selectable augment families from RePoE's augments.json. Only the
/// basic tiered runes are enabled in this pass: their effect is conditional on
/// the socketed item's target category, which the export supplies as a
/// separate category record. Special/transformative runes and bonded stats are
/// deliberately deferred until their socket/target semantics are reviewed.
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
        IReadOnlyList<StatTextRenderer.RenderedLine> Lines);

    public sealed record Result(
        IReadOnlyList<AugmentBuild> Mods,
        int SkippedCategories);

    private static readonly string[] BasicRuneTags =
        ["rune_normal", "rune_lesser", "rune_greater", "rune_perfect"];

    private static readonly Regex NumberRe = new(
        @"[-+]?\d+(?:\.\d+)?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static Result Build(string rawDir)
    {
        var augments = RePoEJson.LoadAugments(Path.Combine(rawDir, "augments.min.json"));
        var baseItems = RePoEJson.LoadBaseItems(Path.Combine(rawDir, "base_items.min.json"));
        var itemClasses = RePoEJson.LoadItemClasses(Path.Combine(rawDir, "item_classes.min.json"));
        var targets = BuildTargetMap(baseItems, itemClasses);

        var mods = new List<AugmentBuild>();
        var skipped = 0;
        foreach (var (path, augment) in augments
                     .OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (!string.Equals(augment.TypeId, "Rune", StringComparison.Ordinal))
                continue;
            if (!baseItems.TryGetValue(path, out var baseItem)) continue;
            if (!string.Equals(baseItem.ReleaseState, "released", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!BasicRuneTags.Any(tag => baseItem.Tags?.Contains(tag, StringComparer.Ordinal) == true))
                continue;
            if (string.IsNullOrWhiteSpace(baseItem.Name)) continue;

            var familyName = NormalizeRuneFamily(baseItem.Name);
            var typeName = Clean(augment.TypeName);
            if (typeName.Length == 0) typeName = "Augment";

            foreach (var (_, category) in augment.Categories.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                var target = TargetText(category.Target);
                if (target.Length == 0 ||
                    !targets.TryGetValue(target, out var classes) ||
                    classes.Count == 0 ||
                    category.StatText is not { Count: > 0 })
                {
                    skipped++;
                    continue;
                }

                var lines = category.StatText
                    .SelectMany(SplitLines)
                    .Select(ToRenderedLine)
                    .Where(line => line is not null)
                    .Select(line => line!)
                    .ToList();
                if (lines.Count == 0)
                {
                    skipped++;
                    continue;
                }

                var targetKey = TargetKey(target);
                mods.Add(new AugmentBuild(
                    $"augment:{Leaf(path)}:{targetKey}",
                    baseItem.Name.Trim(),
                    $"rune:{familyName}:{targetKey}",
                    targetKey,
                    target,
                    typeName,
                    augment.RequiredLevel > 0 ? augment.RequiredLevel : 0,
                    classes.OrderBy(c => c, StringComparer.Ordinal).ToList(),
                    lines));
            }
        }

        return new Result(mods, skipped);
    }

    /// <summary>
    /// Maps the display target categories used by basic runes to concrete
    /// in-game item classes. The mapping is derived from released base-item
    /// tags, not from rune names.
    /// </summary>
    internal static Dictionary<string, HashSet<string>> BuildTargetMap(
        IReadOnlyDictionary<string, RePoESchema.BaseItem> baseItems,
        IReadOnlyDictionary<string, RePoESchema.ItemClass> itemClasses)
    {
        var classTags = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var (path, item) in baseItems)
        {
            if (!string.Equals(item.ReleaseState, "released", StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.IsNullOrWhiteSpace(item.ItemClass)) continue;
            if (!itemClasses.ContainsKey(item.ItemClass)) continue;
            if (!GameItemClasses.IsInGame(item.ItemClass)) continue;
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

        // A martial weapon is a weapon that is not a wand, staff or sceptre.
        // Talisman carries the weapon tag in the export and is kept here only
        // when that holds; special cases are handled by their own targets.
        var martial = classTags
            .Where(kv => kv.Value.Contains("weapon") &&
                         !kv.Value.Contains("wand") &&
                         !kv.Value.Contains("staff") &&
                         !kv.Value.Contains("sceptre"))
            .Select(kv => kv.Key)
            .ToHashSet(StringComparer.Ordinal);

        return new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Armour"] = armour,
            ["Martial Weapon"] = martial,
            ["Wand or Staff"] = classTags
                .Where(kv => kv.Key is "Wand" or "Staff")
                .Select(kv => kv.Key)
                .ToHashSet(StringComparer.Ordinal),
        };
    }

    private static string TargetText(System.Text.Json.JsonElement target) =>
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

    private static IEnumerable<string> SplitLines(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

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
        // value-agnostic effect shape, so a rune line shares the normal stat
        // family instead of becoming foreign text for it. The tier rows still
        // show the concrete rune name and value.
        return new StatTextRenderer.RenderedLine(
            clean, clean, StatTextRenderer.NormalizeDigits(clean), min, max);
    }

    private static string Clean(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? ""
            : StatTextRenderer.StripBracketMarkup(text).Trim();

    private static string NormalizeRuneFamily(string name)
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

    private static string TargetKey(string target) =>
        target.Trim().ToLowerInvariant()
            .Replace(" / ", "-", StringComparison.Ordinal)
            .Replace(' ', '-');

    private static string Leaf(string path)
    {
        var slash = path.LastIndexOf('/');
        return slash >= 0 ? path[(slash + 1)..] : path;
    }
}

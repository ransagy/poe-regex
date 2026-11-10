using System.Text.Json;

namespace PoeRegex.Web.Core;

/// <summary>
/// One curated pre-typing pick. <see cref="Family"/> is the explicit RePoE
/// family key; <see cref="Match"/> is the visible family display text. A
/// contributor can use either, and may scope the pick to class views with
/// <see cref="Classes"/>.
/// </summary>
public sealed class CommonPick
{
    public string Label { get; init; } = "";
    public string? Family { get; init; }
    public string? Match { get; init; }
    public IReadOnlyList<string> Classes { get; init; } = [];
    public string? Note { get; init; }
}

/// <summary>
/// Curated common shortlist loaded from the embedded
/// <c>data/common-picks.json</c>. Runtime matching accepts either an explicit
/// family key or the family's visible display text, so contributors do not
/// need to know internal mod ids for new entries.
/// </summary>
public static class CommonPicks
{
    private const string ResourceName = "PoeRegex.Web.Core.Data.common-picks.json";

    public static IReadOnlyList<CommonPick> Entries { get; }
    public static IReadOnlySet<string> FamilyKeys { get; }
    public static IReadOnlySet<string> MatchTexts { get; }

    private static readonly IReadOnlySet<string> GlobalFamilyKeys;
    private static readonly IReadOnlySet<string> GlobalMatchTexts;
    private static readonly IReadOnlyDictionary<string, ScopeSets> ByClass;

    private sealed record ScopeSets(
        IReadOnlySet<string> FamilyKeys, IReadOnlySet<string> MatchTexts);

    static CommonPicks()
    {
        using var stream = typeof(CommonPicks).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Missing embedded common picks resource '{ResourceName}'.");
        using var document = JsonDocument.Parse(stream);
        if (!document.RootElement.TryGetProperty("picks", out var picks) ||
            picks.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException(
                "Common picks file needs a top-level 'picks' array.");
        }

        var entries = new List<CommonPick>();
        foreach (var item in picks.EnumerateArray())
        {
            var classes = item.TryGetProperty("classes", out var classList) &&
                          classList.ValueKind == JsonValueKind.Array
                ? classList.EnumerateArray()
                    .Select(c => c.GetString())
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Select(c => c!)
                    .ToList()
                : [];
            entries.Add(new CommonPick
            {
                Label = item.TryGetProperty("label", out var label)
                    ? label.GetString() ?? ""
                    : "",
                Family = item.TryGetProperty("family", out var family)
                    ? family.GetString()
                    : null,
                Match = item.TryGetProperty("match", out var match)
                    ? match.GetString()
                    : null,
                Classes = classes,
                Note = item.TryGetProperty("note", out var note)
                    ? note.GetString()
                    : null,
            });
        }

        Entries = entries;
        foreach (var entry in Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Label) ||
                (string.IsNullOrWhiteSpace(entry.Family) && string.IsNullOrWhiteSpace(entry.Match)))
            {
                throw new InvalidOperationException(
                    "Every common pick needs a label and a family key or visible match.");
            }
        }
        var allFamilyKeys = new HashSet<string>(StringComparer.Ordinal);
        var allMatchTexts = new HashSet<string>(StringComparer.Ordinal);
        var globalFamilyKeys = new HashSet<string>(StringComparer.Ordinal);
        var globalMatchTexts = new HashSet<string>(StringComparer.Ordinal);
        var byClass = new Dictionary<string, (HashSet<string> Families, HashSet<string> Matches)>(
            StringComparer.Ordinal);

        foreach (var entry in Entries)
        {
            var family = entry.Family?.Trim();
            var match = entry.Match?.Trim();
            if (!string.IsNullOrEmpty(family)) allFamilyKeys.Add(family);
            if (!string.IsNullOrEmpty(match)) allMatchTexts.Add(match);

            if (entry.Classes.Count == 0)
            {
                if (!string.IsNullOrEmpty(family)) globalFamilyKeys.Add(family);
                if (!string.IsNullOrEmpty(match)) globalMatchTexts.Add(match);
                continue;
            }

            foreach (var cls in entry.Classes.Distinct(StringComparer.Ordinal))
            {
                if (!byClass.TryGetValue(cls, out var sets))
                {
                    sets = (new HashSet<string>(StringComparer.Ordinal),
                        new HashSet<string>(StringComparer.Ordinal));
                    byClass[cls] = sets;
                }
                if (!string.IsNullOrEmpty(family)) sets.Families.Add(family);
                if (!string.IsNullOrEmpty(match)) sets.Matches.Add(match);
            }
        }

        FamilyKeys = allFamilyKeys;
        MatchTexts = allMatchTexts;
        GlobalFamilyKeys = globalFamilyKeys;
        GlobalMatchTexts = globalMatchTexts;
        ByClass = byClass.ToDictionary(
            kv => kv.Key,
            kv => new ScopeSets(kv.Value.Families, kv.Value.Matches),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// True when the family is in the common shortlist. A null
    /// <paramref name="classId"/> is the All-gear/global view; entries with a
    /// class scope are omitted there and only apply in their class views.
    /// </summary>
    public static bool IsCommon(ModFamily family, string? classId = null)
    {
        if (Matches(family, GlobalFamilyKeys, GlobalMatchTexts)) return true;
        if (classId is null || !ByClass.TryGetValue(classId, out var scoped)) return false;
        return Matches(family, scoped.FamilyKeys, scoped.MatchTexts);
    }

    private static bool Matches(
        ModFamily family, IReadOnlySet<string> familyKeys, IReadOnlySet<string> matchTexts) =>
        familyKeys.Contains(family.Key) ||
        family.Members.Any(m => familyKeys.Contains(ModFamilies.KeyOf(m))) ||
        matchTexts.Contains(family.DisplayText);
}

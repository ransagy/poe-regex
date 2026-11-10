using System.Text.Json;

namespace PoeRegex.Web.Core;

/// <summary>
/// One curated pre-typing pick. <see cref="Family"/> is the explicit RePoE
/// family key; <see cref="Match"/> is the visible family display text. A
/// contributor can use either, and may scope validation/documentation with
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
        FamilyKeys = Entries
            .Select(e => e.Family?.Trim())
            .Where(k => !string.IsNullOrEmpty(k))
            .Select(k => k!)
            .ToHashSet(StringComparer.Ordinal);
        MatchTexts = Entries
            .Select(e => e.Match?.Trim())
            .Where(m => !string.IsNullOrEmpty(m))
            .Select(m => m!)
            .ToHashSet(StringComparer.Ordinal);
    }

    public static bool IsCommon(ModFamily family) =>
        FamilyKeys.Contains(family.Key) ||
        family.Members.Any(m => FamilyKeys.Contains(ModFamilies.KeyOf(m))) ||
        MatchTexts.Contains(family.DisplayText);
}

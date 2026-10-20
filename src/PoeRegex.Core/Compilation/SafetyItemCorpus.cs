using System.Text.RegularExpressions;

namespace PoeRegex.Core.Compilation;

/// <summary>
/// Real item texts captured from the game (advanced descriptions) used as
/// additional false-positive lines: item names, base types, affix headers,
/// base properties, flavor text, and jewel usage hints. The catalog only
/// models stat lines; the in-game search sees all of these lines too.
/// Parenthetical ranges/markers are stripped to the canonical search text.
/// </summary>
public sealed record SafetyItem(string ClassName, IReadOnlyList<string> Lines);

public static class SafetyItemCorpus
{
    private static readonly Regex Parenthetical = new(@"\s*\([^)]*\)", RegexOptions.Compiled);

    private static readonly Lazy<IReadOnlyList<SafetyItem>> Poe2 =
        new(() => Load("PoeRegex.Core.catalogs.poe2.safety-items.txt"));

    public static IReadOnlyList<SafetyItem> For(string game) =>
        game == "poe2" ? Poe2.Value : [];

    /// <summary>Parses item blocks; the class display name is resolved by the caller's catalog.</summary>
    public static IReadOnlyList<RawSafetyItem> Parse(string text)
    {
        var items = new List<RawSafetyItem>();
        string? className = null;
        List<string>? lines = null;
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.StartsWith("Item Class:", StringComparison.OrdinalIgnoreCase))
            {
                if (className is not null && lines is not null) items.Add(new RawSafetyItem(className, lines));
                className = line["Item Class:".Length..].Trim();
                lines = [];
                continue;
            }
            if (className is null || lines is null) continue;
            if (line.Trim().Length == 0) continue;
            if (line.Trim().All(c => c == '-')) continue;
            var canonical = Parenthetical.Replace(line, "").Trim();
            if (canonical.Length == 0) continue;
            lines.Add(canonical);
        }
        if (className is not null && lines is not null) items.Add(new RawSafetyItem(className, lines));
        return items;
    }

    private static IReadOnlyList<SafetyItem> Load(string resourceName)
    {
        using var stream = typeof(SafetyItemCorpus).Assembly.GetManifestResourceStream(resourceName);
        if (stream is null) return [];
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd())
            .Select(x => new SafetyItem(x.ClassName, x.Lines))
            .ToList();
    }

    public sealed record RawSafetyItem(string ClassName, IReadOnlyList<string> Lines);
}

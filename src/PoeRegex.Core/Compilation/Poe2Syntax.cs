using System.Text.RegularExpressions;

namespace PoeRegex.Core.Compilation;

/// <summary>
/// The single query syntax used by the PoE2 product: quoted terms, per-line
/// matching, `!` exclusion blocks, the game's 250-character limit, and the
/// custom keyword prefixes. Fragment grammar itself is proven by the exact
/// table; this type only assembles and validates query text.
/// </summary>
public static class Poe2Syntax
{
    public const int MaxCharacterLimit = 250;

    public static readonly IReadOnlyList<string> CustomKeywords =
        ["ilvl:", "str:", "dex:", "int:"];

    private static readonly char[] RegexMeta =
        ['\\', '.', '+', '*', '?', '(', ')', '[', ']', '{', '}', '^', '$', '|'];

    public static CompilerDiagnostic? ValidateFragment(string fragment)
    {
        if (Regex.IsMatch(fragment, @"\\b"))
        {
            return new CompilerDiagnostic(DiagnosticLevel.Warning, "word-boundary",
                @"\b word boundaries are not verified in-game; the engine never emits them.");
        }
        return null;
    }

    public static string EscapeLiteral(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length + 8);
        foreach (var c in text)
        {
            if (RegexMeta.Contains(c)) sb.Append('\\');
            sb.Append(c);
        }
        return sb.ToString();
    }

    public static string Quote(string term) => $"\"{term}\"";

    public static string FormatExclusion(string term) => $"!\"{term}\"";
}

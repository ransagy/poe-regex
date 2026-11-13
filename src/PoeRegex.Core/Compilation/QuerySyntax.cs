using System.Text.RegularExpressions;

namespace PoeRegex.Core.Compilation;

/// <summary>
/// One game's in-client search dialect: escaping, quoting, exclusion shape,
/// keyword prefixes, tokenization, and the shared 250-character budget.
/// Fragment grammar itself is proven by the exact table; this type only
/// assembles, decomposes, and validates query text.
/// </summary>
public sealed class QuerySyntax
{
    public const int MaxCharacterLimit = 250;

    public static readonly IReadOnlyList<string> CustomKeywords =
        ["ilvl:", "str:", "dex:", "int:"];

    private static readonly char[] RegexMeta =
        ['\\', '.', '+', '*', '?', '(', ')', '[', ']', '{', '}', '^', '$', '|'];

    /// <summary>PoE2: a leading bang before the quoted term (<c>!"term"</c>).</summary>
    public static QuerySyntax Poe2 { get; } = new("poe2", exclusionInsideQuotes: false);

    /// <summary>PoE1: the bang lives inside the quotes (<c>"!term"</c>).</summary>
    public static QuerySyntax Poe1 { get; } = new("poe1", exclusionInsideQuotes: true);

    private readonly bool _exclusionInsideQuotes;

    private QuerySyntax(string game, bool exclusionInsideQuotes)
    {
        Game = game;
        _exclusionInsideQuotes = exclusionInsideQuotes;
    }

    public string Game { get; }

    /// <summary>The profile for a catalog game id; unknown ids keep PoE2 behavior.</summary>
    public static QuerySyntax ForGame(string? game) =>
        string.Equals(game, "poe1", StringComparison.OrdinalIgnoreCase) ? Poe1 : Poe2;

    public CompilerDiagnostic? ValidateFragment(string fragment)
    {
        if (Regex.IsMatch(fragment, @"\\b"))
        {
            return new CompilerDiagnostic(DiagnosticLevel.Warning, "word-boundary",
                @"\b word boundaries are not verified in-game; the engine never emits them.");
        }
        return null;
    }

    public string EscapeLiteral(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length + 8);
        foreach (var c in text)
        {
            if (RegexMeta.Contains(c)) sb.Append('\\');
            sb.Append(c);
        }
        return sb.ToString();
    }

    public string Quote(string term) => $"\"{term}\"";

    public string FormatExclusion(string term) =>
        _exclusionInsideQuotes ? $"\"!{term}\"" : $"!\"{term}\"";

    /// <summary>
    /// Split query text into quoted terms and their exclusion state. Quoted
    /// blocks with embedded spaces stay one term; bare terms are returned as
    /// written so shape validation can flag them.
    /// </summary>
    public List<(string Phrase, bool Excluded)> Tokenize(string query)
    {
        var tokens = new List<(string, bool)>();
        var i = 0;
        while (i < query.Length)
        {
            if (char.IsWhiteSpace(query[i]))
            {
                i++;
                continue;
            }

            var excluded = false;
            if (query[i] == '!')
            {
                excluded = true;
                i++;
            }

            if (i < query.Length && query[i] == '"')
            {
                var end = query.IndexOf('"', i + 1);
                var phrase = end < 0
                    ? query[(i + 1)..]
                    : query.Substring(i + 1, end - i - 1);
                if (_exclusionInsideQuotes && phrase.StartsWith('!'))
                {
                    excluded = true;
                    phrase = phrase[1..];
                }
                tokens.Add((phrase, excluded));
                i = end < 0 ? query.Length : end + 1;
            }
            else
            {
                var start = i;
                while (i < query.Length && !char.IsWhiteSpace(query[i])) i++;
                tokens.Add((query[start..i], excluded));
            }
        }
        return tokens;
    }
}

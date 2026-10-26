using System.Text.RegularExpressions;

namespace PoeRegex.Core.Compilation;

/// <summary>
/// Structural checks for compiled queries and fragments. The game treats an
/// unquoted space as a term separator, so every included fragment/group must
/// be inside one quoted block. A fragment made only of regex tokens (".*",
/// "\d+", "^$", ...) is not a stat selector and is rejected.
/// </summary>
public static class QueryShape
{
    public sealed record ShapeToken(string Phrase, bool Excluded, bool Quoted);

    /// <summary>
    /// Returns shape errors. An empty list means the query is quoted as
    /// intended and contains no bare term.
    /// </summary>
    public static IReadOnlyList<string> Validate(string query)
    {
        var errors = new List<string>();
        if (query.Contains('\n') || query.Contains('\r'))
            errors.Add("query contains a line break");

        var tokens = Scan(query, errors);
        foreach (var token in tokens)
        {
            if (!token.Quoted)
                errors.Add($"unquoted term '{Shorten(token.Phrase)}' would split at spaces");
            ValidatePhrase(token.Phrase, "term", errors);
        }
        return errors;
    }

    /// <summary>Returns shape errors for one fragment as stored in the table.</summary>
    public static IReadOnlyList<string> ValidateFragment(string fragment)
    {
        var errors = new List<string>();
        ValidatePhrase(fragment, "fragment", errors);
        return errors;
    }

    private static void ValidatePhrase(
        string phrase, string kind, List<string> errors)
    {
        if (phrase.Length == 0)
        {
            errors.Add($"{kind} is empty");
            return;
        }
        if (phrase.Contains('\n') || phrase.Contains('\r'))
            errors.Add($"{kind} contains a line break");
        if (phrase.Contains('"'))
            errors.Add($"{kind} contains a quote");
        if (!HasSemanticContent(phrase))
            errors.Add($"{kind} '{Shorten(phrase)}' is only regex syntax");
        try
        {
            _ = new Regex(phrase,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(50));
        }
        catch (ArgumentException ex)
        {
            errors.Add($"{kind} '{Shorten(phrase)}' is not a valid regex: {ex.Message}");
        }
    }

    /// <summary>
    /// True when the phrase contains a literal letter or digit. Escaped
    /// metacharacters ("\+", "\.") do not count; character-class tokens
    /// ("\d", "\w", ...) do not count.
    /// </summary>
    private static bool HasSemanticContent(string phrase)
    {
        for (var i = 0; i < phrase.Length; i++)
        {
            var c = phrase[i];
            if (c == '\\' && i + 1 < phrase.Length)
            {
                var next = phrase[i + 1];
                if (next is 'd' or 'D' or 'w' or 'W' or 's' or 'S' or 'b' or 'B')
                {
                    i++;
                    continue;
                }
                if (char.IsAsciiLetterOrDigit(next)) return true;
                i++;
                continue;
            }
            if (char.IsAsciiLetterOrDigit(c)) return true;
        }
        return false;
    }

    private static List<ShapeToken> Scan(string query, List<string> errors)
    {
        var tokens = new List<ShapeToken>();
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
                if (end < 0)
                {
                    errors.Add("unterminated quoted block");
                    break;
                }
                tokens.Add(new ShapeToken(query[(i + 1)..end], excluded, true));
                i = end + 1;
                continue;
            }

            var start = i;
            while (i < query.Length && !char.IsWhiteSpace(query[i])) i++;
            tokens.Add(new ShapeToken(query[start..i], excluded, false));
        }
        return tokens;
    }

    private static string Shorten(string value)
    {
        const int limit = 40;
        return value.Length <= limit ? value : value[..limit] + "…";
    }
}

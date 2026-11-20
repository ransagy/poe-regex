using PoeRegex.Core.Explain;

namespace PoeRegex.Core.Compilation;

/// <summary>
/// Order-insensitive query equivalence: quoted blocks are ANDed, top-level
/// alternatives inside a block are ORed. The canonical planner may reorder
/// either, so import/round-trip checks compare the semantic shape rather than
/// the exact string.
/// </summary>
public static class QueryEquivalence
{
    public static bool Equivalent(string left, string right, QuerySyntax syntax) =>
        string.Equals(Normalize(left, syntax), Normalize(right, syntax), StringComparison.Ordinal);

    public static string Normalize(string query, QuerySyntax syntax)
    {
        var blocks = Explainer.Tokenize(query, syntax)
            .Select(token =>
            {
                var alternatives = SplitTopLevel(token.Phrase)
                    .Select(part => part.Trim())
                    .Where(part => part.Length > 0)
                    .OrderBy(part => part, StringComparer.Ordinal);
                return (token.Excluded, Text: string.Join("|", alternatives));
            })
            .OrderBy(block => block.Excluded)
            .ThenBy(block => block.Text, StringComparer.Ordinal);
        return string.Join("\n", blocks.Select(block =>
            (block.Excluded ? "!" : "") + block.Text));
    }

    /// <summary>Split top-level `|` alternatives without cutting classes/groups/escapes.</summary>
    private static IEnumerable<string> SplitTopLevel(string phrase)
    {
        var start = 0;
        var depth = 0;
        var inClass = false;
        var escaped = false;
        for (var i = 0; i < phrase.Length; i++)
        {
            var c = phrase[i];
            if (escaped)
            {
                escaped = false;
                continue;
            }
            if (c == '\\')
            {
                escaped = true;
                continue;
            }
            if (inClass)
            {
                if (c == ']') inClass = false;
                continue;
            }
            if (c == '[')
            {
                inClass = true;
                continue;
            }
            if (c == '(')
            {
                depth++;
                continue;
            }
            if (c == ')')
            {
                if (depth > 0) depth--;
                continue;
            }
            if (c == '|' && depth == 0)
            {
                yield return phrase[start..i];
                start = i + 1;
            }
        }
        yield return phrase[start..];
    }
}

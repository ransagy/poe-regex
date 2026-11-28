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
                    .SelectMany(ExpandAlternationGroups)
                    .OrderBy(part => part, StringComparer.Ordinal);
                return (token.Excluded, Text: string.Join("|", alternatives));
            })
            .OrderBy(block => block.Excluded)
            .ThenBy(block => block.Text, StringComparer.Ordinal);
        return string.Join("\n", blocks.Select(block =>
            (block.Excluded ? "!" : "") + block.Text));
    }

    private const int MaxExpandedAlternatives = 256;

    /// <summary>
    /// Expands plain alternation groups for comparison, so a shared factored
    /// prefix/suffix form and its unfactored union normalize to one set.
    /// Quantified groups, lookarounds, classes, and escapes are left intact.
    /// </summary>
    private static IEnumerable<string> ExpandAlternationGroups(string term)
    {
        var output = new List<string>();
        ExpandAlternationGroups(term, output, 0);
        return output;
    }

    private static void ExpandAlternationGroups(
        string term, List<string> output, int depth)
    {
        if (output.Count >= MaxExpandedAlternatives || depth >= 8)
        {
            output.Add(term);
            return;
        }
        if (!TryFindAlternationGroup(term, out var open, out var close, out var alternatives))
        {
            output.Add(term);
            return;
        }
        var prefix = term[..open];
        var suffix = term[(close + 1)..];
        foreach (var alternative in alternatives)
            ExpandAlternationGroups(prefix + alternative + suffix, output, depth + 1);
    }

    private static bool TryFindAlternationGroup(
        string term, out int open, out int close, out IReadOnlyList<string> alternatives)
    {
        open = close = -1;
        alternatives = [];
        var inClass = false;
        for (var i = 0; i < term.Length; i++)
        {
            var c = term[i];
            if (c == '\\')
            {
                i++;
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
            if (c != '(' || IsEscaped(term, i)) continue;

            var depth = 1;
            var innerClass = false;
            var nestedClose = -1;
            for (var j = i + 1; j < term.Length && depth > 0; j++)
            {
                var inner = term[j];
                if (inner == '\\')
                {
                    j++;
                    continue;
                }
                if (innerClass)
                {
                    if (inner == ']') innerClass = false;
                    continue;
                }
                if (inner == '[')
                {
                    innerClass = true;
                    continue;
                }
                if (inner == '(') depth++;
                else if (inner == ')')
                {
                    depth--;
                    if (depth == 0) nestedClose = j;
                }
            }
            if (nestedClose < 0) return false;

            var content = term[(i + 1)..nestedClose];
            if (content.Length == 0 || content[0] == '?' ||
                nestedClose + 1 < term.Length &&
                term[nestedClose + 1] is '*' or '+' or '?' or '{')
            {
                i = nestedClose;
                continue;
            }
            var parts = SplitTopLevel(content)
                .Select(part => part.Trim())
                .Where(part => part.Length > 0)
                .ToList();
            if (parts.Count < 2) continue; // keep scanning inside this group
            open = i;
            close = nestedClose;
            alternatives = parts;
            return true;
        }
        return false;
    }

    private static bool IsEscaped(string value, int index)
    {
        var count = 0;
        for (var i = index - 1; i >= 0 && value[i] == '\\'; i--) count++;
        return count % 2 == 1;
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

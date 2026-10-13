using System.Text.RegularExpressions;
using PoeRegex.Core.Explain;

namespace PoeRegex.Web.Core;

/// <summary>Per-token verdict of matching a compiled query against one item's lines.</summary>
public sealed record TokenVerdict(string Phrase, bool Excluded, bool Hit);

/// <summary>Overall verdict: every included token hit ≥1 line, no excluded token hit any.</summary>
public sealed record ItemVerdict(bool Match, IReadOnlyList<TokenVerdict> Tokens);

/// <summary>
/// Lab matcher: applies a compiled query to pasted item lines with the same
/// token semantics the engine guarantees (`Explainer.Tokenize`: quoted blocks
/// AND, `!` negates, case-insensitive per-line regex). Pure and browser-safe.
/// </summary>
public static partial class ItemMatcher
{
    public static ItemVerdict Match(string query, IEnumerable<string> lines)
    {
        var list = lines.ToList();
        var verdicts = new List<TokenVerdict>();
        foreach (var (phrase, excluded) in Explainer.Tokenize(query))
        {
            var hit = false;
            try
            {
                hit = list.Any(l => Regex.IsMatch(l, phrase, RegexOptions.IgnoreCase));
            }
            catch (ArgumentException)
            {
                hit = false; // defensive: a non-regex-safe phrase never matches
            }
            verdicts.Add(new TokenVerdict(phrase, excluded, hit));
        }
        var match = verdicts
            .Where(t => !t.Excluded).All(t => t.Hit)
            && verdicts.Where(t => t.Excluded).All(t => !t.Hit);
        return new ItemVerdict(match, verdicts);
    }
}

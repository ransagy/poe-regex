using System.Text;

namespace PoeRegex.Core.Compilation;

/// <summary>
/// Fast matcher for the small regex vocabulary the fragment selector emits:
/// literals (escaped as needed), <c>\d</c>, <c>\d+</c>, <c>.</c>, <c>.*</c>,
/// <c>.+</c>, and the per-line anchors <c>^</c>/<c>$</c>. Matching is
/// case-insensitive and always operates on one copy-text line.
///
/// The selector validates many candidates against every line of a scope, so
/// this matcher avoids allocating a regex or a per-line memo table. Candidates
/// contain at most one wildcard; the left and right literal runs are matched
/// directly. <see cref="MatchesRegex"/> exists so tests can prove the two
/// implementations agree.
/// </summary>
public sealed class FragmentMatcher
{
    private enum TokenKind { Literal, Digit, DigitRun, Any, Star, Plus }

    private readonly record struct Token(TokenKind Kind, char Literal);

    private readonly Token[] _tokens;
    private readonly bool _anchoredStart;
    private readonly bool _anchoredEnd;
    private readonly int _wildcardIndex;
    private readonly System.Text.RegularExpressions.Regex? _fallback;

    private FragmentMatcher(
        Token[] tokens, bool anchoredStart, bool anchoredEnd, int wildcardIndex,
        System.Text.RegularExpressions.Regex? fallback)
    {
        _tokens = tokens;
        _anchoredStart = anchoredStart;
        _anchoredEnd = anchoredEnd;
        _wildcardIndex = wildcardIndex;
        _fallback = fallback;
    }

    public static FragmentMatcher Parse(string fragment)
    {
        var tokens = new List<Token>(fragment.Length);
        var start = false;
        var end = false;
        var i = 0;
        if (fragment.StartsWith('^'))
        {
            start = true;
            i = 1;
        }
        var wildcard = -1;
        var wildcardCount = 0;
        while (i < fragment.Length)
        {
            var c = fragment[i];
            if (c == '$' && i == fragment.Length - 1)
            {
                end = true;
                i++;
                continue;
            }
            if (c == '\\' && i + 1 < fragment.Length)
            {
                var n = fragment[i + 1];
                if (n == 'd')
                {
                    if (i + 2 < fragment.Length && fragment[i + 2] == '+')
                    {
                        tokens.Add(new Token(TokenKind.DigitRun, '\0'));
                        i += 3;
                    }
                    else
                    {
                        tokens.Add(new Token(TokenKind.Digit, '\0'));
                        i += 2;
                    }
                    continue;
                }
                tokens.Add(new Token(TokenKind.Literal, n));
                i += 2;
                continue;
            }
            if (c == '.')
            {
                if (i + 1 < fragment.Length && fragment[i + 1] == '*')
                {
                    tokens.Add(new Token(TokenKind.Star, '\0'));
                    if (wildcard < 0) wildcard = tokens.Count - 1;
                    wildcardCount++;
                    i += 2;
                    continue;
                }
                if (i + 1 < fragment.Length && fragment[i + 1] == '+')
                {
                    tokens.Add(new Token(TokenKind.Plus, '\0'));
                    if (wildcard < 0) wildcard = tokens.Count - 1;
                    wildcardCount++;
                    i += 2;
                    continue;
                }
                tokens.Add(new Token(TokenKind.Any, '\0'));
                i++;
                continue;
            }
            tokens.Add(new Token(TokenKind.Literal, c));
            i++;
        }
        System.Text.RegularExpressions.Regex? fallback = null;
        if (wildcardCount > 1)
        {
            try
            {
                fallback = new System.Text.RegularExpressions.Regex(
                    fragment,
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase |
                    System.Text.RegularExpressions.RegexOptions.CultureInvariant,
                    TimeSpan.FromSeconds(1));
            }
            catch (ArgumentException)
            {
                // Keep the token matcher; an invalid fragment never matches.
            }
        }
        return new FragmentMatcher(tokens.ToArray(), start, end, wildcard, fallback);
    }

    public bool IsMatch(string line)
    {
        if (_fallback is not null) return _fallback.IsMatch(line);
        if (_wildcardIndex < 0)
            return MatchSimple(line);
        return MatchWithWildcard(line);
    }

    private bool MatchSimple(string line)
    {
        var startLimit = _anchoredStart ? 1 : line.Length + 1;
        for (var start = 0; start < startLimit; start++)
            if (MatchTokens(0, start, line))
                return true;
        return false;
    }

    /// <summary>Matches the whole token list from a position, with optional end anchor.</summary>
    private bool MatchTokens(int ti, int si, string line)
    {
        if (ti == _tokens.Length) return !_anchoredEnd || si == line.Length;
        var t = _tokens[ti];
        switch (t.Kind)
        {
            case TokenKind.Literal:
                return si < line.Length
                       && char.ToLowerInvariant(line[si]) == char.ToLowerInvariant(t.Literal)
                       && MatchTokens(ti + 1, si + 1, line);
            case TokenKind.Digit:
                return si < line.Length && char.IsAsciiDigit(line[si]) && MatchTokens(ti + 1, si + 1, line);
            case TokenKind.Any:
                return si < line.Length && MatchTokens(ti + 1, si + 1, line);
            case TokenKind.DigitRun:
            {
                var end = si;
                while (end < line.Length && char.IsAsciiDigit(line[end])) end++;
                for (var next = end; next > si; next--)
                    if (MatchTokens(ti + 1, next, line)) return true;
                return false;
            }
            default:
                return false; // wildcard handled by MatchWithWildcard
        }
    }

    private bool MatchWithWildcard(string line)
    {
        var star = _tokens[_wildcardIndex].Kind == TokenKind.Star;
        var left = _tokens.AsSpan(0, _wildcardIndex);
        var right = _tokens.AsSpan(_wildcardIndex + 1);
        var startLimit = _anchoredStart ? 1 : line.Length + 1;
        for (var start = 0; start < startLimit; start++)
        {
            foreach (var leftEnd in LeftEnds(left, start, line))
            {
                var minRight = leftEnd + (star ? 0 : 1);
                if (minRight > line.Length) continue;
                // `.*`/`.+` may consume any run; try each right start position.
                for (var rightStart = minRight; rightStart <= line.Length; rightStart++)
                {
                    if (MatchRight(right, rightStart, line)) return true;
                }
            }
        }
        return false;
    }

    /// <summary>All positions after matching the left run; there is normally one.</summary>
    private static List<int> LeftEnds(ReadOnlySpan<Token> tokens, int start, string line)
    {
        var ends = new List<int>(4);
        CollectEnds(tokens, 0, start, line, ends);
        return ends;
    }

    private static void CollectEnds(ReadOnlySpan<Token> tokens, int ti, int si, string line, List<int> ends)
    {
        if (ti == tokens.Length)
        {
            ends.Add(si);
            return;
        }
        var t = tokens[ti];
        switch (t.Kind)
        {
            case TokenKind.Literal:
                if (si < line.Length && char.ToLowerInvariant(line[si]) == char.ToLowerInvariant(t.Literal))
                    CollectEnds(tokens, ti + 1, si + 1, line, ends);
                break;
            case TokenKind.Digit:
                if (si < line.Length && char.IsAsciiDigit(line[si]))
                    CollectEnds(tokens, ti + 1, si + 1, line, ends);
                break;
            case TokenKind.Any:
                if (si < line.Length) CollectEnds(tokens, ti + 1, si + 1, line, ends);
                break;
            case TokenKind.DigitRun:
            {
                var end = si;
                while (end < line.Length && char.IsAsciiDigit(line[end])) end++;
                for (var next = si + 1; next <= end; next++)
                    CollectEnds(tokens, ti + 1, next, line, ends);
                break;
            }
        }
    }

    private bool MatchRight(ReadOnlySpan<Token> tokens, int si, string line) =>
        MatchRightCore(tokens, 0, si, line);

    private bool MatchRightCore(ReadOnlySpan<Token> tokens, int ti, int si, string line)
    {
        if (ti == tokens.Length) return !_anchoredEnd || si == line.Length;
        var t = tokens[ti];
        switch (t.Kind)
        {
            case TokenKind.Literal:
                return si < line.Length
                       && char.ToLowerInvariant(line[si]) == char.ToLowerInvariant(t.Literal)
                       && MatchRightCore(tokens, ti + 1, si + 1, line);
            case TokenKind.Digit:
                return si < line.Length && char.IsAsciiDigit(line[si]) && MatchRightCore(tokens, ti + 1, si + 1, line);
            case TokenKind.Any:
                return si < line.Length && MatchRightCore(tokens, ti + 1, si + 1, line);
            case TokenKind.DigitRun:
            {
                var end = si;
                while (end < line.Length && char.IsAsciiDigit(line[end])) end++;
                for (var next = end; next > si; next--)
                    if (MatchRightCore(tokens, ti + 1, next, line)) return true;
                return false;
            }
            default:
                return false;
        }
    }

    /// <summary>Reference implementation used by tests to cross-check the fast matcher.</summary>
    public static bool MatchesRegex(string fragment, string line) =>
        System.Text.RegularExpressions.Regex.IsMatch(
            line, fragment, System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>
    /// Escapes a literal run for the fragment language (same set as the
    /// compiler strategies).
    /// </summary>
    public static string Escape(string text)
    {
        var sb = new StringBuilder(text.Length + 4);
        foreach (var c in text)
        {
            if (c is '\\' or '.' or '+' or '*' or '?' or '(' or ')' or '[' or ']' or '{' or '}' or '^' or '$' or '|')
                sb.Append('\\');
            sb.Append(c);
        }
        return sb.ToString();
    }
}

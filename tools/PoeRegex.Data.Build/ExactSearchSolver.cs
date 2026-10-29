using System.Text;
using System.Text.RegularExpressions;

namespace PoeRegex.Data.Build;

/// <summary>
/// Exact shortest-term search over a product NFA state space. Each target and
/// counterexample line carries a bitset of possible match positions. Pattern
/// tokens advance those bitsets; the first goal state is the shortest legal
/// form. A counterexample-guided outer loop adds every foreign line matched by
/// a candidate until the candidate is globally clean.
///
/// This is the exact search behind matrix-solve and solve-exact.
/// </summary>
internal static class ExactSearchSolver
{
    internal sealed record Result(string Fragment, int Length, int Iterations, int Negatives);

    private sealed class Line
    {
        public required string Text { get; init; }
        public required int Offset { get; init; }
        public required int Words { get; init; }
        public int Length => Text.Length;
    }

    private sealed class StateKey
    {
        public required ulong[] Bits { get; init; }
        public required int Letters { get; init; }

        public override int GetHashCode()
        {
            var hash = new HashCode();
            foreach (var word in Bits) hash.Add(word);
            hash.Add(Letters);
            return hash.ToHashCode();
        }

        public override bool Equals(object? obj) =>
            obj is StateKey other &&
            other.Letters == Letters &&
            other.Bits.AsSpan().SequenceEqual(Bits);
    }

    private sealed class Node
    {
        public required StateKey Key { get; init; }
        public required int Cost { get; init; }
        public Node? Parent { get; init; }
        public string Token { get; init; } = "";
    }

    public static Result? Solve(
        ScopeCorpus scope,
        string pattern,
        int minLength,
        int maxLength,
        int maxLetters = 2)
    {
        var targets = scope.Targets(pattern);
        if (targets.Count == 0) return null;
        var foreignEntries = scope.Lines
            .Where(x => !string.Equals(x.Pattern, pattern, StringComparison.Ordinal))
            .GroupBy(x => x.Real, StringComparer.Ordinal)
            .Select(g => (Raw: g.Key, Pattern: g.First().Pattern))
            .ToList();
        var foreign = foreignEntries.Select(x => x.Raw).ToList();
        if (foreign.Count == 0) return null;
        var patternByLine = foreignEntries
            .GroupBy(x => x.Raw, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Pattern, StringComparer.Ordinal);

        var negatives = SeedNegatives(pattern, targets, foreign);
        for (var iteration = 1; iteration <= 200; iteration++)
        {
            var fragment = SolveOnce(
                targets, negatives.ToList(), minLength, maxLength, maxLetters);
            if (fragment is null) return null;

            var regex = new Regex(
                fragment, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (targets.Any(t => !regex.IsMatch(t)))
                throw new InvalidOperationException($"synthesized fragment misses target: {fragment}");

            var hits = foreign.Where(f => regex.IsMatch(f)).ToList();
            if (hits.Count == 0)
                return new Result(fragment, fragment.Length, iteration, negatives.Count);

            var added = 0;
            foreach (var group in hits
                         .GroupBy(h => patternByLine.GetValueOrDefault(h, ""), StringComparer.Ordinal)
                         .OrderBy(g => g.Key.StartsWith('\u0003') ? 1 : 0)
                         .ThenBy(g => g.Key, StringComparer.Ordinal))
            {
                var representative = group
                    .OrderBy(x => x.Length)
                    .ThenBy(x => x, StringComparer.Ordinal)
                    .First();
                if (negatives.Add(representative)) added++;
                if (added >= 5) break;
            }
            if (added == 0)
                throw new InvalidOperationException(
                    $"synthesized fragment is invalid but no new counterexample: {fragment}");
        }
        return null;
    }

    private static HashSet<string> SeedNegatives(
        string pattern,
        IReadOnlyList<string> targets,
        IReadOnlyList<string> foreign)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var seed = PatternSeed(pattern);
        if (seed.Length < 3) seed = LongestLetterSubstring(targets);
        if (seed.Length < 3) return result;
        foreach (var line in foreign)
        {
            if (line.Contains(seed, StringComparison.OrdinalIgnoreCase))
                result.Add(line);
            if (result.Count >= 200) break;
        }
        return result;
    }

    private static string PatternSeed(string pattern)
    {
        var segments = pattern
            .Split('\u0001', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => new string(s.Where(c => char.IsAsciiLetter(c) || c == ' ').ToArray()).Trim())
            .OrderByDescending(s => s.Length)
            .ToList();
        return segments.FirstOrDefault() ?? "";
    }

    private static string LongestLetterSubstring(IReadOnlyList<string> values)
    {
        var shortest = values.OrderBy(x => x.Length).First();
        var best = "";
        for (var start = 0; start < shortest.Length; start++)
        {
            for (var len = 1; len <= shortest.Length - start; len++)
            {
                var candidate = shortest.Substring(start, start + len - start);
                if (candidate.Length <= best.Length) continue;
                if (candidate.Any(c => !char.IsAsciiLetter(c) && c != ' ')) continue;
                if (values.All(v => v.Contains(candidate, StringComparison.OrdinalIgnoreCase)))
                    best = candidate;
            }
        }
        return best.Trim();
    }

    private static string? SolveOnce(
        IReadOnlyList<string> targets,
        IReadOnlyList<string> negatives,
        int minLength,
        int maxLength,
        int maxLetters)
    {
        var alphabet = targets
            .SelectMany(x => x.ToLowerInvariant())
            .Distinct()
            .OrderBy(x => x)
            .ToArray();
        var literalTokens = alphabet
            .Select(c => (Char: c, Token: EscapeLiteral(c), Cost: IsMeta(c) ? 2 : 1,
                IsLetter: char.IsAsciiLetter(c)))
            .ToArray();

        var lines = new List<Line>();
        var offset = 0;
        foreach (var text in targets.Concat(negatives))
        {
            var words = (text.Length + 1 + 63) / 64;
            lines.Add(new Line { Text = text, Offset = offset, Words = words });
            offset += words;
        }
        var targetCount = targets.Count;

        var initialBits = new ulong[offset];
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (i < targetCount)
            {
                SetRange(initialBits, line, 0, line.Length);
            }
            else
            {
                SetRange(initialBits, line, 0, line.Length);
            }
        }

        var startBits = (ulong[])initialBits.Clone();
        for (var i = 0; i < lines.Count; i++)
        {
            ClearRange(startBits, lines[i], 0, lines[i].Length);
            SetBit(startBits, lines[i], 0);
        }

        var queue = new PriorityQueue<Node, int>();
        var best = new Dictionary<StateKey, int>();
        Node? bestGoal = null;
        var bestGoalCost = int.MaxValue;

        void Push(Node node)
        {
            if (node.Cost > maxLength) return;
            var key = node.Key;
            if (best.TryGetValue(key, out var old) && old <= node.Cost) return;
            best[key] = node.Cost;
            queue.Enqueue(node, node.Cost);
        }

        var unanchored = new Node
        {
            Key = new StateKey { Bits = initialBits, Letters = 0 },
            Cost = 0,
            Token = "",
        };
        Push(unanchored);
        Push(new Node
        {
            Key = new StateKey { Bits = startBits, Letters = 0 },
            Cost = 1,
            Parent = null,
            Token = "^",
        });

        while (queue.TryDequeue(out var node, out var priority))
        {
            if (priority >= bestGoalCost) break;
            if (node.Cost < minLength)
            {
                // still expand
            }
            else if (node.Key.Letters >= 2 &&
                     AllTargetsMatch(node.Key.Bits, lines, targetCount) &&
                     (bestGoal is null || node.Cost < bestGoalCost))
            {
                bestGoal = node;
                bestGoalCost = node.Cost;
                break;
            }

            // End anchor is a final token.
            if (node.Key.Letters >= 2 &&
                node.Cost + 1 >= minLength &&
                node.Cost + 1 <= maxLength &&
                node.Cost + 1 < bestGoalCost &&
                AllTargetsEnd(node.Key.Bits, lines, targetCount))
            {
                bestGoal = new Node
                {
                    Key = node.Key,
                    Cost = node.Cost + 1,
                    Parent = node,
                    Token = "$",
                };
                bestGoalCost = node.Cost + 1;
            }

            if (node.Key.Letters >= maxLetters)
            {
                // Still allow wildcards/anchors if they could produce a clean
                // target match at a shorter total cost.
            }

            foreach (var literal in literalTokens)
            {
                var next = AdvanceLiteral(node.Key.Bits, lines, targetCount, literal.Char);
                if (next is null) continue;
                var letters = Math.Min(maxLetters,
                    node.Key.Letters + (literal.IsLetter ? 1 : 0));
                Push(new Node
                {
                    Key = new StateKey { Bits = next, Letters = letters },
                    Cost = node.Cost + literal.Cost,
                    Parent = node,
                    Token = literal.Token,
                });
            }

            var dot = AdvanceDot(node.Key.Bits, lines, targetCount);
            if (dot is not null)
                Push(new Node
                {
                    Key = new StateKey { Bits = dot, Letters = node.Key.Letters },
                    Cost = node.Cost + 1,
                    Parent = node,
                    Token = ".",
                });

            var star = AdvanceClosure(node.Key.Bits, lines, targetCount, plus: false);
            if (star is not null)
                Push(new Node
                {
                    Key = new StateKey { Bits = star, Letters = node.Key.Letters },
                    Cost = node.Cost + 2,
                    Parent = node,
                    Token = ".*",
                });

            var plus = AdvanceClosure(node.Key.Bits, lines, targetCount, plus: true);
            if (plus is not null)
                Push(new Node
                {
                    Key = new StateKey { Bits = plus, Letters = node.Key.Letters },
                    Cost = node.Cost + 2,
                    Parent = node,
                    Token = ".+",
                });

            var digit = AdvanceDigit(node.Key.Bits, lines, targetCount, plus: false);
            if (digit is not null)
                Push(new Node
                {
                    Key = new StateKey { Bits = digit, Letters = node.Key.Letters },
                    Cost = node.Cost + 2,
                    Parent = node,
                    Token = @"\d",
                });

            var digitPlus = AdvanceDigit(node.Key.Bits, lines, targetCount, plus: true);
            if (digitPlus is not null)
                Push(new Node
                {
                    Key = new StateKey { Bits = digitPlus, Letters = node.Key.Letters },
                    Cost = node.Cost + 3,
                    Parent = node,
                    Token = @"\d+",
                });
        }

        if (bestGoal is null) return null;
        var tokens = new List<string>();
        for (var current = bestGoal; current is not null; current = current.Parent)
            if (current.Token.Length > 0) tokens.Add(current.Token);
        tokens.Reverse();
        return string.Concat(tokens);
    }

    private static bool AllTargetsMatch(ulong[] bits, IReadOnlyList<Line> lines, int targetCount)
    {
        for (var i = 0; i < targetCount; i++)
            if (!HasAny(bits, lines[i])) return false;
        for (var i = targetCount; i < lines.Count; i++)
            if (HasAny(bits, lines[i])) return false;
        return true;
    }

    private static bool AllTargetsEnd(ulong[] bits, IReadOnlyList<Line> lines, int targetCount)
    {
        for (var i = 0; i < targetCount; i++)
            if (!GetBit(bits, lines[i], lines[i].Length)) return false;
        for (var i = targetCount; i < lines.Count; i++)
            if (GetBit(bits, lines[i], lines[i].Length)) return false;
        return true;
    }

    private static ulong[]? AdvanceLiteral(ulong[] bits, IReadOnlyList<Line> lines, int targetCount, char c)
    {
        var next = (ulong[])bits.Clone();
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            ClearLine(next, line);
            var text = line.Text;
            for (var pos = 0; pos < text.Length; pos++)
            {
                if (!GetBit(bits, line, pos)) continue;
                if (char.ToLowerInvariant(text[pos]) == c)
                    SetBit(next, line, pos + 1);
            }
            if (i < targetCount && !HasAny(next, line))
                return null;
        }
        return next;
    }

    private static ulong[]? AdvanceDot(ulong[] bits, IReadOnlyList<Line> lines, int targetCount)
    {
        var next = (ulong[])bits.Clone();
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            ClearLine(next, line);
            for (var pos = 0; pos < line.Length; pos++)
                if (GetBit(bits, line, pos)) SetBit(next, line, pos + 1);
            if (i < targetCount && !HasAny(next, line)) return null;
        }
        return next;
    }

    private static ulong[]? AdvanceClosure(ulong[] bits, IReadOnlyList<Line> lines, int targetCount, bool plus)
    {
        var next = (ulong[])bits.Clone();
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            ClearLine(next, line);
            var first = FirstBit(bits, line);
            if (first < 0) continue;
            var start = plus ? first + 1 : first;
            SetRange(next, line, start, line.Length);
            if (i < targetCount && !HasAny(next, line)) return null;
        }
        return next;
    }

    private static ulong[]? AdvanceDigit(ulong[] bits, IReadOnlyList<Line> lines, int targetCount, bool plus)
    {
        var next = (ulong[])bits.Clone();
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            ClearLine(next, line);
            for (var pos = 0; pos < line.Length; pos++)
            {
                if (!GetBit(bits, line, pos)) continue;
                if (!char.IsAsciiDigit(line.Text[pos])) continue;
                if (!plus)
                {
                    SetBit(next, line, pos + 1);
                    continue;
                }
                var end = pos;
                while (end < line.Length && char.IsAsciiDigit(line.Text[end]))
                {
                    end++;
                    SetBit(next, line, end);
                }
            }
            if (i < targetCount && !HasAny(next, line)) return null;
        }
        return next;
    }

    private static void SetBit(ulong[] bits, Line line, int position)
    {
        if (position < 0 || position > line.Length) return;
        var index = line.Offset + position / 64;
        bits[index] |= 1UL << (position % 64);
    }

    private static bool GetBit(ulong[] bits, Line line, int position)
    {
        if (position < 0 || position > line.Length) return false;
        var index = line.Offset + position / 64;
        return (bits[index] & (1UL << (position % 64))) != 0;
    }

    private static bool HasAny(ulong[] bits, Line line)
    {
        for (var i = 0; i < line.Words; i++)
            if (bits[line.Offset + i] != 0) return true;
        return false;
    }

    private static int FirstBit(ulong[] bits, Line line)
    {
        for (var w = 0; w < line.Words; w++)
        {
            var word = bits[line.Offset + w];
            if (word != 0) return w * 64 + System.Numerics.BitOperations.TrailingZeroCount(word);
        }
        return -1;
    }

    private static void ClearLine(ulong[] bits, Line line)
    {
        for (var i = 0; i < line.Words; i++) bits[line.Offset + i] = 0;
    }

    private static void ClearRange(ulong[] bits, Line line, int start, int end)
    {
        for (var position = start; position <= end; position++)
            if (GetBit(bits, line, position))
            {
                var index = line.Offset + position / 64;
                bits[index] &= ~(1UL << (position % 64));
            }
    }

    private static void SetRange(ulong[] bits, Line line, int start, int end)
    {
        for (var position = start; position <= end; position++)
            SetBit(bits, line, position);
    }

    private static bool IsMeta(char c) =>
        c is '\\' or '.' or '+' or '*' or '?' or '(' or ')' or '[' or ']' or
            '{' or '}' or '^' or '$' or '|';

    private static string EscapeLiteral(char c) => IsMeta(c) ? "\\" + c : c.ToString();
}

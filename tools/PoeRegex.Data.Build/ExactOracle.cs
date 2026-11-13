using System.Text.RegularExpressions;
using PoeRegex.Core.Compilation;

namespace PoeRegex.Data.Build;

/// <summary>The target/foreign line set for one matrix scope.</summary>
internal sealed class ScopeCorpus
{
    private readonly Dictionary<string, int[]> _byPattern;

    private ScopeCorpus(string? scope, IReadOnlyList<SourceLine> lines)
    {
        Scope = scope;
        Lines = lines;
        _byPattern = lines
            .Select((line, index) => (line.Pattern, index))
            .GroupBy(x => x.Pattern, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => x.index).ToArray(),
                StringComparer.Ordinal);
    }

    public string? Scope { get; }
    public IReadOnlyList<SourceLine> Lines { get; }

    public static ScopeCorpus Build(EngineCorpus corpus, string? scope) =>
        new(scope, MatrixModel.ScopeLines(corpus, scope));

    internal static ScopeCorpus FromLines(string? scope, IReadOnlyList<SourceLine> lines) =>
        new(scope, lines);

    public bool HasPattern(string pattern) => _byPattern.ContainsKey(pattern);

    public IReadOnlyList<string> Targets(string pattern) =>
        _byPattern.TryGetValue(pattern, out var indexes)
            ? indexes.Select(i => Lines[i].Real)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(x => x.Length)
                .ThenBy(x => x, StringComparer.Ordinal)
                .ToList()
            : [];

    /// <summary>Lines from other patterns in this scope.</summary>
    public IReadOnlyList<SourceLine> ForeignLines(string pattern) =>
        Lines.Where(x => !string.Equals(x.Pattern, pattern, StringComparison.Ordinal)).ToList();
}

/// <summary>
/// Regex oracle over a matrix scope. The exact solver uses the same line set;
/// this type is the independent verification used by the table and proof steps.
/// </summary>
internal static class ExactOracle
{
    public static MatrixOption? Check(ScopeCorpus scope, string pattern, string fragment)
    {
        var targets = scope.Targets(pattern);
        if (targets.Count == 0) return null;

        Regex regex;
        try
        {
            regex = new Regex(
                fragment,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1));
        }
        catch (ArgumentException)
        {
            return null;
        }

        if (targets.Any(t => !regex.IsMatch(t))) return null;

        var foreign = pattern.Contains('\n')
            ? []
            : scope.ForeignLines(pattern)
                .Select(x => x.Real)
                .Distinct(StringComparer.Ordinal);
        var minion = 0;
        var other = 0;
        foreach (var line in foreign)
        {
            if (!regex.IsMatch(line)) continue;
            if (IsMinionAlliesLine(line)) minion++;
            else other++;
        }
        return new MatrixOption(fragment, fragment.Length, minion + other, minion, other);
    }

    public static MatrixOption? Solve(
        ScopeCorpus scope, string pattern, int minLength, int maxLength,
        TimeSpan? timeout = null, bool anchoredFallback = false)
    {
        var result = ExactSearchSolver.Solve(scope, pattern, minLength, maxLength, timeout: timeout);
        if (result is not null)
        {
            var option = Check(scope, pattern, result.Fragment);
            if (option is null)
                throw new InvalidOperationException(
                    $"exact solver returned a fragment that fails verification: {result.Fragment}");
            return option;
        }

        if (!anchoredFallback) return null;

        // A few PoE1 signed/advanced forms cannot be expressed by the
        // solver's sign-free token grammar. A full-line anchored alternation
        // over the actual target texts is the remaining strict shape; the
        // oracle still proves zero foreign matches. This fallback is opt-in so
        // the PoE2 proof keeps its original grammar and cost.
        var anchored = scope.Targets(pattern)
            .Select(t => "^" + DigitsToRegex(t) + "$")
            .Distinct(StringComparer.Ordinal)
            .Where(c => c.Length >= minLength && c.Length <= maxLength)
            .ToList();
        foreach (var candidate in anchored)
        {
            var option = Check(scope, pattern, candidate);
            if (option is { ForeignLines: 0 }) return option;
        }
        if (anchored.Count > 1)
        {
            var combined = "^(" + string.Join("|", anchored.Select(a => a[1..^1])) + ")$";
            if (combined.Length <= maxLength)
            {
                var option = Check(scope, pattern, combined);
                if (option is { ForeignLines: 0 }) return option;
            }
        }
        return null;
    }

    private static string DigitsToRegex(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length + 8);
        for (var i = 0; i < text.Length;)
        {
            if (char.IsDigit(text[i]))
            {
                while (i < text.Length && char.IsDigit(text[i])) i++;
                sb.Append(@"\d+");
                continue;
            }
            var c = text[i++];
            if ("\\.+*?()[]{}^$|".Contains(c)) sb.Append('\\');
            sb.Append(c);
        }
        return sb.ToString();
    }

    public static bool IsMinionAlliesLine(string line) =>
        line.Contains("Minions", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("Allies", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("in your Presence", StringComparison.OrdinalIgnoreCase);
}

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

        var foreign = scope.Lines
            .Where(x => !string.Equals(x.Pattern, pattern, StringComparison.Ordinal))
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
        ScopeCorpus scope, string pattern, int minLength, int maxLength)
    {
        var result = ExactSearchSolver.Solve(scope, pattern, minLength, maxLength);
        if (result is null) return null;
        var option = Check(scope, pattern, result.Fragment);
        if (option is null)
            throw new InvalidOperationException(
                $"exact solver returned a fragment that fails verification: {result.Fragment}");
        return option;
    }

    public static bool IsMinionAlliesLine(string line) =>
        line.Contains("Minions", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("Allies", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("in your Presence", StringComparison.OrdinalIgnoreCase);
}

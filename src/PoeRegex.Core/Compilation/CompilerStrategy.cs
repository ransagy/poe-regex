using System.Text.RegularExpressions;

namespace PoeRegex.Core.Compilation;

/// <summary>Shared validation/escaping for both games.</summary>
public abstract class CompilerStrategyBase : ICompilerStrategy
{
    public abstract GameRules GameRules { get; }

    private static readonly char[] RegexMeta = ['\\', '.', '+', '*', '?', '(', ')', '[', ']', '{', '}', '^', '$', '|'];

    public virtual CompilerDiagnostic? ValidateFragment(string fragment)
    {
        if (Regex.IsMatch(fragment, @"\\b"))
        {
            return new CompilerDiagnostic(DiagnosticLevel.Warning, "word-boundary",
                @"\b word boundaries are not verified in-game; the engine never emits them.");
        }
        if (GameRules.SupportsLookahead == false && (fragment.Contains("(?=") || fragment.Contains("(?!")))
        {
            return new CompilerDiagnostic(DiagnosticLevel.Warning, "lookahead",
                $"Lookahead is not supported by {GameRules.Game}; results may not behave as expected.");
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

    public abstract string FormatExclusion(string term);

    public string Quote(string term) => $"\"{term}\"";
}

public sealed class PathOfExileOneStrategy : CompilerStrategyBase
{
    public override GameRules GameRules { get; } = GameRules.For(GameTarget.PoE1);

    public override string FormatExclusion(string term) => $"!\"{term}\"";
}

public sealed class PathOfExileTwoStrategy : CompilerStrategyBase
{
    public override GameRules GameRules { get; } = GameRules.For(GameTarget.PoE2);

    public override string FormatExclusion(string term) => $"!\"{term}\"";
}

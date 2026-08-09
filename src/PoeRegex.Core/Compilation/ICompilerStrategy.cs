namespace PoeRegex.Core.Compilation;

/// <summary>
/// Per-game rules for validating fragments and assembling a query.
/// Strategies never emit constructs the game does not support.
/// </summary>
public interface ICompilerStrategy
{
    GameRules GameRules { get; }

    /// <summary>
    /// Validates a user-supplied fragment. Returns a warning diagnostic for
    /// constructs the game does not support (or that are unverified) without
    /// rejecting them; the fragment itself is escaped for literal matching.
    /// </summary>
    CompilerDiagnostic? ValidateFragment(string fragment);

    /// <summary>Escapes regex metacharacters so free text matches literally.</summary>
    string EscapeLiteral(string text);

    /// <summary>Wraps a term in the game's exclusion syntax.</summary>
    string FormatExclusion(string term);

    /// <summary>Wraps a term in the game's quoted-block syntax (if required).</summary>
    string Quote(string term);
}

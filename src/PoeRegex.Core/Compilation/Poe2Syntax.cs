namespace PoeRegex.Core.Compilation;

/// <summary>
/// The PoE2 query dialect: quoted terms, per-line matching, <c>!"term"</c>
/// exclusions, the game's 250-character limit, and the custom keyword
/// prefixes. Kept as the PoE2-named entry point; per-game code should use
/// <see cref="EngineCorpus.Syntax"/> or <see cref="QuerySyntax.ForGame"/>.
/// </summary>
public static class Poe2Syntax
{
    public const int MaxCharacterLimit = QuerySyntax.MaxCharacterLimit;

    public static readonly IReadOnlyList<string> CustomKeywords = QuerySyntax.CustomKeywords;

    public static CompilerDiagnostic? ValidateFragment(string fragment) =>
        QuerySyntax.Poe2.ValidateFragment(fragment);

    public static string EscapeLiteral(string text) => QuerySyntax.Poe2.EscapeLiteral(text);

    public static string Quote(string term) => QuerySyntax.Poe2.Quote(term);

    public static string FormatExclusion(string term) => QuerySyntax.Poe2.FormatExclusion(term);
}

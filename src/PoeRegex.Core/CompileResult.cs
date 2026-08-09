namespace PoeRegex.Core;

/// <summary>Result of a successful (or best-effort) compile.</summary>
public sealed record CompileResult
{
    public required string Query { get; init; }

    public required int Length { get; init; }

    public required int Limit { get; init; }

    public IReadOnlyList<CompilerDiagnostic> Diagnostics { get; init; } = [];

    public bool Exceeded => Length > Limit;
}

namespace PoeRegex.Core;

public enum DiagnosticLevel
{
    Info,
    Warning,
    Error,
}

/// <summary>A non-fatal message produced while compiling a query.</summary>
public sealed record CompilerDiagnostic(DiagnosticLevel Level, string Code, string Message);

using System.Text.Json.Serialization;

namespace PoeRegex.Cli;

/// <summary>Named model for `generate --json` output (kept AOT-safe via source-gen).</summary>
public sealed record JsonEmitResult
{
    public required string Query { get; init; }

    public required int Length { get; init; }

    public required int Limit { get; init; }

    public required bool Exceeded { get; init; }

    public required IReadOnlyList<JsonEmitDiagnostic> Diagnostics { get; init; }
}

public sealed record JsonEmitDiagnostic
{
    public required string Level { get; init; }

    public required string Code { get; init; }

    public required string Message { get; init; }
}

[JsonSerializable(typeof(JsonEmitResult))]
[JsonSerializable(typeof(JsonEmitDiagnostic))]
public sealed partial class CliJsonContext : JsonSerializerContext;

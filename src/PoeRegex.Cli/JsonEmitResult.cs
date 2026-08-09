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

/// <summary>Named model for `list-classes --json` output.</summary>
public sealed record JsonClassEntry
{
    public required string Id { get; init; }

    public required string Name { get; init; }
}

/// <summary>Named model for `list-mods --json` output.</summary>
public sealed record JsonModEntry
{
    public required string Id { get; init; }

    public string? Name { get; init; }

    /// <summary>First rendered stat line, mirroring the text output.</summary>
    public required string Line { get; init; }
}

/// <summary>Named model for `validate --json` output.</summary>
public sealed record JsonValidateResult
{
    public required bool Ok { get; init; }

    public required string Game { get; init; }

    public JsonEmitDiagnostic? Warning { get; init; }
}

[JsonSerializable(typeof(JsonEmitResult))]
[JsonSerializable(typeof(JsonEmitDiagnostic))]
[JsonSerializable(typeof(JsonClassEntry))]
[JsonSerializable(typeof(List<JsonClassEntry>))]
[JsonSerializable(typeof(JsonModEntry))]
[JsonSerializable(typeof(List<JsonModEntry>))]
[JsonSerializable(typeof(JsonValidateResult))]
public sealed partial class CliJsonContext : JsonSerializerContext;

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

    /// <summary>Friendly per-number range rendering (falls back to Line when empty).</summary>
    public string Range { get; init; } = "";
}

/// <summary>Named model for `validate --json` output.</summary>
public sealed record JsonValidateResult
{
    public required bool Ok { get; init; }

    public required string Game { get; init; }

    public JsonEmitDiagnostic? Warning { get; init; }
}

/// <summary>Named model for `explain --json` output (query reversal).</summary>
public sealed record JsonExplainResult
{
    public required string Query { get; init; }

    public required string Game { get; init; }

    public required IReadOnlyList<JsonExplainClass> Classes { get; init; }

    public required IReadOnlyList<JsonExplainToken> Tokens { get; init; }

    public required IReadOnlyList<JsonExplainMod> Mods { get; init; }
}

public sealed record JsonExplainClass
{
    public required string Id { get; init; }

    public required string Name { get; init; }
}

public sealed record JsonExplainToken
{
    public required int Index { get; init; }

    public required string Phrase { get; init; }

    public required bool Excluded { get; init; }

    /// <summary>included | excluded | class | keyword</summary>
    public required string Kind { get; init; }

    public required IReadOnlyList<string> Classes { get; init; }

    public required IReadOnlyList<JsonExplainMatch> Matches { get; init; }
}

public sealed record JsonExplainMatch
{
    public required string Class { get; init; }

    public required string Line { get; init; }

    public required string Pattern { get; init; }

    public required IReadOnlyList<string> ModIds { get; init; }
}

public sealed record JsonExplainMod
{
    public required string Id { get; init; }

    public string? Name { get; init; }

    public required IReadOnlyList<string> Classes { get; init; }

    public required IReadOnlyList<string> Phrases { get; init; }

    public required IReadOnlyList<JsonExplainLine> Lines { get; init; }
}

public sealed record JsonExplainLine
{
    public required string Class { get; init; }

    public required string Line { get; init; }
}

[JsonSerializable(typeof(JsonEmitResult))]
[JsonSerializable(typeof(JsonEmitDiagnostic))]
[JsonSerializable(typeof(JsonClassEntry))]
[JsonSerializable(typeof(List<JsonClassEntry>))]
[JsonSerializable(typeof(JsonModEntry))]
[JsonSerializable(typeof(List<JsonModEntry>))]
[JsonSerializable(typeof(JsonValidateResult))]
[JsonSerializable(typeof(JsonExplainResult))]
[JsonSerializable(typeof(JsonExplainClass))]
[JsonSerializable(typeof(JsonExplainToken))]
[JsonSerializable(typeof(JsonExplainMatch))]
[JsonSerializable(typeof(JsonExplainMod))]
[JsonSerializable(typeof(JsonExplainLine))]
public sealed partial class CliJsonContext : JsonSerializerContext;

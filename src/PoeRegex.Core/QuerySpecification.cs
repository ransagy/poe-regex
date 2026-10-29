namespace PoeRegex.Core;

/// <summary>One modifier requirement: a catalog ModId or free text, with an optional minimum value.</summary>
public sealed record ModifierRequirement(string Value, double? MinValue = null);

/// <summary>
/// The full description of a desired trade-filter query. Each included/excluded
/// modifier is either a catalog mod id (fingerprinted from the catalog) or free
/// text (validated and emitted as a literal fragment).
/// </summary>
public sealed record QuerySpecification
{
    /// <summary>Item class id (e.g. "Helmet"), or null to omit the class anchor.</summary>
    public string? ItemClass { get; init; }

    public IReadOnlyList<ModifierRequirement> IncludedModifiers { get; init; } = [];

    public IReadOnlyList<ModifierRequirement> ExcludedModifiers { get; init; } = [];

    /// <summary>Custom keywords (e.g. "ilvl:60").</summary>
    public IReadOnlyList<string> Keywords { get; init; } = [];

    /// <summary>When true, a query that exceeds the character limit is returned best-effort with a diagnostic.</summary>
    public bool AllowOverflow { get; init; }

}

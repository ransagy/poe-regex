namespace PoeRegex.Core;

/// <summary>
/// One modifier requirement: a catalog ModId or free text, with an optional
/// numeric range. <see cref="RangeModIds"/> turns the requirement into one
/// combined family+range fragment covering every listed tier variant.
/// <see cref="FullFamily"/> marks a picker family pick whose listed
/// <see cref="RangeModIds"/> are every tier that class can roll: the family
/// fragment covers it, and the requirement groups with the same family on
/// other classes without enumerating each tier.
/// </summary>
public sealed record ModifierRequirement(
    string Value,
    double? MinValue = null,
    bool IsRegex = false,
    double? MaxValue = null,
    IReadOnlyList<string>? RangeModIds = null,
    bool FullFamily = false);

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

    /// <summary>Each group becomes its own quoted block, so an item must match every group (any member of a group).</summary>
    public IReadOnlyList<IReadOnlyList<ModifierRequirement>> RequiredGroups { get; init; } = [];

    public IReadOnlyList<ModifierRequirement> ExcludedModifiers { get; init; } = [];

    /// <summary>Custom keywords (e.g. "ilvl:60").</summary>
    public IReadOnlyList<string> Keywords { get; init; } = [];

    /// <summary>When true, a query that exceeds the character limit is returned best-effort with a diagnostic.</summary>
    public bool AllowOverflow { get; init; }

}

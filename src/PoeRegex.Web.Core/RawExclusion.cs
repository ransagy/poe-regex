namespace PoeRegex.Web.Core;

/// <summary>
/// A raw regex term, not a catalog mod id. Workbench values carry this marker
/// so the compiler can emit them verbatim and the UI can label them without
/// pretending they are catalog picks.
/// </summary>
public static class RawText
{
    public const string Prefix = "regex:";

    public static bool IsRaw(string value) =>
        value.StartsWith(Prefix, StringComparison.Ordinal);

    public static string Encode(string pattern) => Prefix + pattern;

    public static string Decode(string value) =>
        IsRaw(value) ? value[Prefix.Length..] : value;
}

/// <summary>Compatibility alias for exclusion callers.</summary>
public static class RawExclusion
{
    public const string Prefix = RawText.Prefix;

    public static bool IsRaw(string value) => RawText.IsRaw(value);

    public static string Encode(string pattern) => RawText.Encode(pattern);

    public static string Decode(string value) => RawText.Decode(value);
}

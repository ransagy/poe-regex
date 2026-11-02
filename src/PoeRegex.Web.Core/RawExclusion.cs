namespace PoeRegex.Web.Core;

/// <summary>
/// A guide exclusion is a raw regex group, not a catalog mod id. Workbench
/// exclusions carry this marker so the compiler can emit it verbatim and the
/// UI can label it without pretending it is a mod.
/// </summary>
public static class RawExclusion
{
    public const string Prefix = "regex:";

    public static bool IsRaw(string value) =>
        value.StartsWith(Prefix, StringComparison.Ordinal);

    public static string Encode(string pattern) => Prefix + pattern;

    public static string Decode(string value) =>
        IsRaw(value) ? value[Prefix.Length..] : value;
}

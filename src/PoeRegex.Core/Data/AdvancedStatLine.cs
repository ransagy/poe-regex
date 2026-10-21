using System.Text.RegularExpressions;

namespace PoeRegex.Core.Data;

/// <summary>
/// Builds the advanced-description form of a catalog stat line, e.g.
/// "+44(33-60) to Accuracy Rating" or "6(6-13)% increased Armour and Evasion".
/// The game shows the rolled value followed by the tier's min-max span; tests
/// and fixtures must use this form rather than the max-only catalog
/// representative. A fixed line (rangeLine == line) is returned unchanged.
/// </summary>
public static class AdvancedStatLine
{
    private static readonly Regex RangePair = new(
        @"(?<![\d.])(?<low>\+?\d+(?:\.\d+)?)\s*[-\u2013]\s*(?<high>\+?\d+(?:\.\d+)?)(?![\d.])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string For(StatLineEntry stat)
    {
        var range = stat.RangeLine;
        if (string.IsNullOrWhiteSpace(range) ||
            string.Equals(range, stat.Line, StringComparison.Ordinal))
            return stat.Line;

        var replaced = RangePair.Replace(range, match =>
        {
            var low = match.Groups["low"].Value.TrimStart('+');
            var high = match.Groups["high"].Value.TrimStart('+');
            var plus = match.Groups["low"].Value.StartsWith('+') ||
                       match.Groups["high"].Value.StartsWith('+');
            return (plus ? "+" : "") + high + "(" + low + "-" + high + ")";
        });

        if (string.Equals(replaced, range, StringComparison.Ordinal)) return stat.Line;
        if (stat.Line.StartsWith('+') && !replaced.StartsWith('+')) replaced = "+" + replaced;
        return replaced;
    }
}

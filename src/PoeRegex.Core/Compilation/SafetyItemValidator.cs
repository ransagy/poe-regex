using System.Text.RegularExpressions;

namespace PoeRegex.Core.Compilation;

/// <summary>
/// Whole-item safety check on captured real items. The per-line opcode oracle
/// rejects foreign catalog/universal/name/flavor lines; this adds the
/// worst-case dot-all whole-item check so a fragment cannot bridge two lines
/// of an item that does not carry the target stat. The target item is allowed.
/// </summary>
public static class SafetyItemValidator
{
    private static readonly RegexOptions Options =
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Singleline;

    public static bool IsSafe(EngineCorpus corpus, string pattern, string candidate)
    {
        if (corpus.SafetyItems.Count == 0) return true;
        Regex regex;
        try { regex = new Regex(candidate, Options); }
        catch (ArgumentException) { return false; }

        foreach (var item in corpus.SafetyItems)
        {
            var hasTarget = item.Lines.Any(line =>
                string.Equals(corpus.PatternForSafetyLine(line), pattern, StringComparison.Ordinal));
            if (hasTarget) continue; // the fragment targets this item's stat
            var whole = string.Join("\n", item.Lines);
            if (regex.IsMatch(whole)) return false;
        }
        return true;
    }
}

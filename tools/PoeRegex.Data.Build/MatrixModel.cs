using PoeRegex.Core.Compilation;
using PoeRegex.Core.Data;

namespace PoeRegex.Data.Build;

/// <summary>One modeled copy-text line, tagged with its normalized pattern.</summary>
internal sealed record SourceLine(string Pattern, string Real);

/// <summary>The fields the matrix/proof artifacts need for one fragment.</summary>
internal sealed record MatrixOption(
    string Fragment,
    int Length,
    int ForeignLines,
    int ForeignMinionLines,
    int ForeignOtherLines)
{
    public bool TermSplits => Fragment.Any(char.IsWhiteSpace);

    public bool HasWildcard => Fragment.Any(c => c is '.' or '*' or '+');

    public bool Anchored => Fragment.StartsWith('^') || Fragment.EndsWith('$');

    public int GuardLetters => LongestLiteralLetterRun(Fragment);

    public string Quoted => "\"" + Fragment + "\"";

    public int QuotedLength => Length + 2;

    private static int LongestLiteralLetterRun(string fragment)
    {
        var best = 0;
        var current = 0;
        for (var i = 0; i < fragment.Length; i++)
        {
            var c = fragment[i];
            if (c == '\\' && i + 1 < fragment.Length)
            {
                var next = fragment[i + 1];
                if (next == 'd')
                {
                    best = Math.Max(best, current);
                    current = 0;
                    i++;
                    if (i + 1 < fragment.Length && fragment[i + 1] == '+') i++;
                    continue;
                }
                current++;
                i++;
                continue;
            }
            if (char.IsAsciiLetter(c)) current++;
            else
            {
                best = Math.Max(best, current);
                current = 0;
            }
        }
        return Math.Max(best, current);
    }
}

internal static class MatrixModel
{
    public static string FamilyKey(ModEntry mod) =>
        mod.Family.Length > 0 ? mod.Family : mod.Id;

    public static IReadOnlyList<string> AllFamilies(Catalog catalog) =>
        catalog.Mods
            .Select(FamilyKey)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

    public static List<SourceLine> ScopeLines(EngineCorpus corpus, string? scope)
    {
        var all = new List<SourceLine>();
        if (scope is null)
            all.AddRange(corpus.RealLinesGlobal().Select(x => new SourceLine(x.Pattern, x.Real)));
        else
            all.AddRange(corpus.RealLinesForClass(scope).Select(x => new SourceLine(x.Pattern, x.Real)));
        foreach (var (pattern, raw) in corpus.SafetyLines(scope))
            all.Add(new SourceLine(pattern, raw));
        return all;
    }

    public static string CurrentFragment(FragmentTable? table, string? scope, string pattern)
    {
        if (table is null) return "";
        if (scope is null) return table.Global.GetValueOrDefault(pattern) ?? "";
        return table.ByClass.GetValueOrDefault(scope)?.GetValueOrDefault(pattern) ?? "";
    }
}

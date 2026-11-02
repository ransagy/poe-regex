using System.Text;
using System.Text.RegularExpressions;
using PoeRegex.Core.Data;

namespace PoeRegex.Web.Core;

public enum GuideBranchKind
{
    Fragment,
    AffixName,
    Family,
    MultiFamily,
    Dropped,
}

public sealed record GuideCandidate(string Key, string Label);

/// <summary>One top-level guide-filter branch and how it was mapped.</summary>
public sealed record GuideBranch(
    int Index,
    string Text,
    GuideBranchKind Kind,
    IReadOnlyList<GuideCandidate> Candidates,
    IReadOnlyList<string> Selected,
    IReadOnlyList<WantedMod> Picks,
    string? Note);

public sealed record GuideImportResult(
    ReconstructedWorkbench? Workbench,
    IReadOnlyList<GuideBranch> Branches,
    IReadOnlyList<string> Warnings,
    string? CompiledQuery)
{
    public bool Ok => Workbench?.Ok == true;
    public int Mapped => Branches.Count(b => b.Picks.Count > 0);
    public int Dropped => Branches.Count(b => b.Kind == GuideBranchKind.Dropped);
}

/// <summary>
/// Best-effort build-guide filter extraction. Branches are regex fragments
/// (the game's search semantics), not only exact table fragments. Exact names
/// and table fragments keep their pick identity; anything else is matched
/// against family display text and tier text, with multi-select reconciliation.
/// </summary>
public sealed class GuideImportService(
    EngineService engine,
    QueryReconstructionService reconstruction)
{
    public GuideImportResult Import(
        string input,
        IReadOnlyDictionary<int, IReadOnlyList<string>>? overrides = null)
    {
        var warnings = new List<string>();
        var (line, lineWarning) = ExtractFilter(input);
        if (lineWarning is not null) warnings.Add(lineWarning);

        if (line.Length == 0)
            return Empty(warnings, "No guide filter found.");

        if (line.StartsWith(": ", StringComparison.Ordinal) ||
            line.StartsWith("s: ", StringComparison.Ordinal))
        {
            // Explicit class anchors are a later slice; do not silently attach
            // the picks to the wrong scope.
            warnings.Add("Class anchors are not applied in guide import yet; importing globally.");
            var separator = line.IndexOf('|');
            line = separator >= 0 ? line[(separator + 1)..] : "";
            if (line.Length == 0)
                return Empty(warnings, "The guide filter only contained a class anchor.");
        }

        var branches = new List<GuideBranch>();
        var index = 0;
        foreach (var raw in SplitBranches(line))
        {
            var text = raw.Trim().Trim('`', '"', '\'').Trim();
            if (text.Length == 0) continue;
            branches.Add(ResolveBranch(index++, text, overrides));
        }

        var picks = new List<WantedMod>();
        foreach (var branch in branches)
            foreach (var pick in branch.Picks)
                if (!picks.Any(p => p.SamePick(pick)))
                    picks.Add(pick);

        if (picks.Count == 0)
        {
            warnings.Add("No guide branches mapped to a mod family.");
            return new GuideImportResult(null, branches, warnings, null);
        }

        var slot = new ReconstructedSlot("global", "", picks, false);
        var workbench = new ReconstructedWorkbench(
            true, "simple", [slot], [], [], warnings);
        return new GuideImportResult(
            workbench, branches, warnings, workbench.Recompile(engine));
    }

    private static GuideImportResult Empty(List<string> warnings, string reason)
    {
        warnings.Add(reason);
        return new GuideImportResult(null, [], warnings, null);
    }

    private GuideBranch ResolveBranch(
        int index, string text, IReadOnlyDictionary<int, IReadOnlyList<string>>? overrides)
    {
        if (text.StartsWith('!') || text.StartsWith('-'))
        {
            return new GuideBranch(index, text, GuideBranchKind.Dropped,
                [], [], [], "guide exclusions are not supported yet");
        }

        var exactNames = reconstruction.Families
            .SelectMany(f => f.Tiers.Select(t => (Family: f, Tier: t)))
            .Where(x => string.Equals(x.Tier.Name, text, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (exactNames.Count > 0)
        {
            var chosen = exactNames
                .OrderBy(x => ModFamilies.IsCommon(x.Family) ? 0 : 1)
                .ThenBy(x => x.Family.Key, StringComparer.Ordinal)
                .First();
            var pick = new WantedMod(chosen.Tier.Anchor.Id, Names: [chosen.Tier.Name]);
            return new GuideBranch(index, text, GuideBranchKind.AffixName,
                [], [chosen.Family.Key], [pick], null);
        }

        var scored = MatchFamilies(reconstruction.Families, text);
        if (scored.Count > 0)
        {
            var top = scored[0].Score;
            var candidates = scored
                .Where(x => x.Score >= top - 15)
                .Take(12)
                .Select(x => new GuideCandidate(x.Family.Key, x.Family.DisplayText))
                .ToList();

            var selected = ResolveSelection(index, candidates, scored, overrides);
            if (selected.Count == 0)
            {
                return new GuideBranch(index, text, GuideBranchKind.MultiFamily,
                    candidates, [], [], "no family selected");
            }

            var picks = selected
                .Select(key => reconstruction.Families.First(f => f.Key == key).Pick())
                .ToList();
            var kind = candidates.Count > 1
                ? GuideBranchKind.MultiFamily
                : GuideBranchKind.Family;
            var note = candidates.Count > 1
                ? $"regex matched {candidates.Count} families; select one or more"
                : null;
            return new GuideBranch(index, text, kind, candidates, selected, picks, note);
        }

        // A branch that does not match the display corpus can still be an
        // exact precomputed table fragment or name.
        if (reconstruction.TryResolveGuideTerm(text, out var fallbackPicks, out _) &&
            fallbackPicks.Count > 0)
        {
            var kind = fallbackPicks.All(p => p.IsNamePick)
                ? GuideBranchKind.AffixName
                : GuideBranchKind.Fragment;
            return new GuideBranch(index, text, kind, [], [], fallbackPicks, null);
        }

        return new GuideBranch(index, text, GuideBranchKind.Dropped,
            [], [], [], "no mod family matched (base/vendor names are not mapped yet)");
    }

    private static List<string> ResolveSelection(
        int index,
        IReadOnlyList<GuideCandidate> candidates,
        IReadOnlyList<(ModFamily Family, int Score)> scored,
        IReadOnlyDictionary<int, IReadOnlyList<string>>? overrides)
    {
        if (overrides is not null && overrides.TryGetValue(index, out var requested))
        {
            var valid = requested
                .Where(key => candidates.Any(c => string.Equals(c.Key, key, StringComparison.Ordinal)))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            return valid;
        }

        // Default to the common families near the top score. This makes a
        // branch like `i.+res` select the resistance families together, while
        // a clear winner like `i.+mov` selects one family.
        var top = scored[0].Score;
        var common = scored
            .Where(x => x.Score >= top - 10 && ModFamilies.IsCommon(x.Family))
            .Select(x => x.Family.Key)
            .Distinct(StringComparer.Ordinal)
            .Take(6)
            .ToList();
        return common.Count > 0 ? common : [scored[0].Family.Key];
    }

    private static List<(ModFamily Family, int Score)> MatchFamilies(
        IReadOnlyList<ModFamily> families, string query)
    {
        var matcher = new GuideMatcher(query);
        var scored = new List<(ModFamily Family, int Score)>();
        foreach (var family in families)
        {
            var score = 0;
            if (matcher.Matches(family.DisplayText)) score = Math.Max(score, 70);
            foreach (var tier in family.Tiers)
            {
                if (tier.Name.Length > 0 && matcher.Matches(tier.Name))
                    score = Math.Max(score, 90);
                foreach (var line in tier.DisplayLines)
                    if (line.Length > 0 && matcher.Matches(line))
                        score = Math.Max(score, 60);
                foreach (var line in tier.SearchText.Split('\n'))
                    if (line.Length > 0 && matcher.Matches(line))
                        score = Math.Max(score, 45);
            }
            if (score == 0) continue;
            if (ModFamilies.IsCommon(family)) score += 5;
            scored.Add((family, score));
        }
        return scored
            .OrderByDescending(x => x.Score)
            .ThenBy(x => ModFamilies.IsCommon(x.Family) ? 0 : 1)
            .ThenBy(x => x.Family.Key, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Regex matcher for guide semantics; falls back to a literal.</summary>
    private sealed class GuideMatcher
    {
        private readonly Regex? _regex;
        private readonly string _literal;

        public GuideMatcher(string query)
        {
            _literal = query;
            try
            {
                _regex = new Regex(
                    query,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                    TimeSpan.FromMilliseconds(100));
            }
            catch (ArgumentException)
            {
                _regex = null;
            }
        }

        public bool Matches(string text)
        {
            if (_regex is null)
                return text.Contains(_literal, StringComparison.OrdinalIgnoreCase);
            try
            {
                return _regex.IsMatch(text);
            }
            catch (RegexMatchTimeoutException)
            {
                return false;
            }
        }
    }

    private static (string Line, string? Warning) ExtractFilter(string input)
    {
        var lines = input.Replace("\r", "").Split('\n')
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .ToList();
        if (lines.Count == 0) return ("", null);
        var line = lines.Count == 1
            ? lines[0]
            : lines.OrderByDescending(x => x.Count(c => c == '|'))
                .ThenByDescending(x => x.Length)
                .First();
        var warning = lines.Count > 1
            ? $"Used one filter line from {lines.Count} lines: {line}"
            : null;

        line = line.Trim().Trim('`', '"', '\'').Trim();
        foreach (var prefix in new[]
                 {
                     "filter:", "search:", "regex:", "query:", "guide:",
                 })
        {
            if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                line = line[prefix.Length..].Trim();
                break;
            }
        }
        foreach (var bullet in new[] { "- ", "* ", "• " })
        {
            if (line.StartsWith(bullet, StringComparison.Ordinal))
            {
                line = line[bullet.Length..].Trim();
                break;
            }
        }
        return (line, warning);
    }

    /// <summary>Split on top-level | only; leave groups/classes/escapes intact.</summary>
    private static List<string> SplitBranches(string text)
    {
        var result = new List<string>();
        var start = 0;
        var depth = 0;
        var inClass = false;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch == '\\') { i++; continue; }
            if (inClass)
            {
                if (ch == ']') inClass = false;
                continue;
            }
            switch (ch)
            {
                case '[': inClass = true; break;
                case '(': depth++; break;
                case ')': if (depth > 0) depth--; break;
                case '|' when depth == 0:
                    result.Add(text[start..i]);
                    start = i + 1;
                    break;
            }
        }
        result.Add(text[start..]);
        return result.Where(x => x.Length > 0).ToList();
    }
}

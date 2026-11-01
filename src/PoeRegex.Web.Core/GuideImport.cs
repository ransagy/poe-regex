using System.Text;
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
    string? Selected,
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
/// Best-effort build-guide filter extraction. A guide branch is mapped to the
/// app's own picks; unsupported branches are dropped with a warning. No raw
/// guide text is retained as a chip.
/// </summary>
public sealed class GuideImportService(
    EngineService engine,
    QueryReconstructionService reconstruction)
{
    public GuideImportResult Import(
        string input, IReadOnlyDictionary<int, string>? overrides = null)
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
            branches.Add(ResolveBranch(index++, text, overrides, warnings));
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
        int index, string text, IReadOnlyDictionary<int, string>? overrides,
        List<string> warnings)
    {
        if (text.StartsWith('!') || text.StartsWith('-'))
        {
            var note = "guide exclusions are not supported yet";
            warnings.Add($"Dropped '{text}': {note}");
            return new GuideBranch(index, text, GuideBranchKind.Dropped,
                [], null, [], note);
        }

        if (!LooksLikeKeyword(text))
        {
            if (reconstruction.TryResolveGuideTerm(text, out var fragmentPicks, out _) &&
                fragmentPicks.Count > 0)
            {
                var kind = fragmentPicks.All(p => p.IsNamePick)
                    ? GuideBranchKind.AffixName
                    : GuideBranchKind.Fragment;
                return new GuideBranch(index, text, kind, [], null, fragmentPicks, null);
            }
            var note = "not an exact table fragment or affix name";
            warnings.Add($"Dropped '{text}': {note}");
            return new GuideBranch(index, text, GuideBranchKind.Dropped,
                [], null, [], note);
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
            var note = exactNames.Count > 1
                ? $"name exists in {exactNames.Count} families; picked '{chosen.Family.Key}'"
                : null;
            if (note is not null) warnings.Add($"'{text}': {note}");
            return new GuideBranch(index, text, GuideBranchKind.AffixName,
                [], chosen.Family.Key, [pick], note);
        }

        var scored = reconstruction.Families
            .Select(f => (Family: f, Score: Score(f, text)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => ModFamilies.IsCommon(x.Family) ? 0 : 1)
            .ThenBy(x => x.Family.Key, StringComparer.Ordinal)
            .ToList();
        var candidates = scored.Count == 0
            ? []
            : scored
                .Where(x => x.Score >= scored[0].Score - 15)
                .Take(10)
                .Select(x => new GuideCandidate(x.Family.Key, x.Family.DisplayText))
                .ToList();
        if (candidates.Count > 0)
        {
            var selected = candidates[0].Key;
            if (overrides is not null &&
                overrides.TryGetValue(index, out var requested) &&
                candidates.Any(c => string.Equals(c.Key, requested, StringComparison.Ordinal)))
                selected = requested;

            var family = reconstruction.Families.First(f => f.Key == selected);
            if (candidates.Count == 1)
                return new GuideBranch(index, text, GuideBranchKind.Family,
                    candidates, selected, [family.Pick()], null);

            var noteText =
                $"matched {candidates.Count} families; picked '{selected}' " +
                $"({string.Join(", ", candidates.Take(6).Select(c => c.Key))}" +
                (candidates.Count > 6 ? ", …" : "") + ")";
            warnings.Add($"'{text}': {noteText}");
            return new GuideBranch(index, text, GuideBranchKind.MultiFamily,
                candidates, selected, [family.Pick()], noteText);
        }

        // A word-like branch can still be an exact table fragment or a name
        // that the family search did not label (e.g. "d Ch").
        if (reconstruction.TryResolveGuideTerm(text, out var fallbackPicks, out _) &&
            fallbackPicks.Count > 0)
        {
            var kind = fallbackPicks.All(p => p.IsNamePick)
                ? GuideBranchKind.AffixName
                : GuideBranchKind.Fragment;
            return new GuideBranch(index, text, kind, [], null, fallbackPicks, null);
        }

        var dropped = "no mod family matched (base/vendor names are not mapped yet)";
        warnings.Add($"Dropped '{text}': {dropped}");
        return new GuideBranch(index, text, GuideBranchKind.Dropped,
            [], null, [], dropped);
    }

    private static int Score(ModFamily family, string query)
    {
        var q = Normalize(query);
        if (q.Length == 0) return 0;
        var display = Normalize(family.DisplayText);
        var score = string.Equals(display, q, StringComparison.Ordinal)
            ? 100
            : display.Contains(q, StringComparison.Ordinal) ? 60 : 0;
        if (Normalize(family.Key).Contains(q, StringComparison.Ordinal))
            score = Math.Max(score, 30);
        foreach (var tier in family.Tiers)
        {
            var name = Normalize(tier.Name);
            if (string.Equals(name, q, StringComparison.Ordinal))
                score = Math.Max(score, 95);
            else if (name.Contains(q, StringComparison.Ordinal))
                score = Math.Max(score, 55);
            if (Normalize(tier.SearchText).Contains(q, StringComparison.Ordinal))
                score = Math.Max(score, 35);
        }
        if (score > 0)
        {
            // Prefer families whose display text is close to the query (a
            // primary stat) over hybrids that merely mention it on a second line.
            score += Math.Max(0, 20 - (display.Length - q.Length));
            if (ModFamilies.IsCommon(family)) score += 5;
        }
        return score;
    }

    private static string Normalize(string value)
    {
        var sb = new StringBuilder(value.Length);
        var space = false;
        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            if (char.IsWhiteSpace(ch))
            {
                space = sb.Length > 0;
                continue;
            }
            if (space)
            {
                sb.Append(' ');
                space = false;
            }
            sb.Append(ch);
        }
        return sb.ToString();
    }

    private static bool LooksLikeKeyword(string text) =>
        text.Any(char.IsLetter) &&
        !text.Any(ch => @"^$*?()[]{}|\".Contains(ch));

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

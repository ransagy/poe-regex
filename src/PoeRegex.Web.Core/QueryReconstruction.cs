using System.Text.RegularExpressions;
using PoeRegex.Core;
using PoeRegex.Core.Compilation;
using PoeRegex.Core.Data;
using PoeRegex.Core.Explain;

namespace PoeRegex.Web.Core;

/// <summary>
/// One query reconstructed into canonical workbench criteria. Phase 1 covers
/// the simple scope and one advanced slot, with unfactored fragments and
/// affix-name picks; anything else fails closed with a diagnostic.
/// </summary>
public sealed record ReconstructedQuery(
    bool Ok,
    string Query,
    string Mode,
    string? ItemClass,
    IReadOnlyList<WantedMod> Includes,
    IReadOnlyList<string> Excludes,
    bool MatchAll,
    IReadOnlyList<string> Diagnostics)
{
    public static ReconstructedQuery Failed(string query, string reason) =>
        new(false, query, "simple", null, [], [], false, [reason]);

    public CompileResult? Recompile(EngineService engine) =>
        Ok ? engine.CompilePicks(Includes, Excludes, MatchAll, ItemClass) : null;
}

/// <summary>
/// Phase-1 reverse mapping: our own simple / single-slot advanced strings back
/// to canonical picks. It uses the shipped fragment table and catalog affix
/// names only; it does not run the shortest-form search or factor alternations.
/// </summary>
public sealed class QueryReconstructionService
{
    private readonly EngineService _engine;
    private readonly QueryImportService _anchors;
    private readonly Dictionary<string, List<(string Pattern, string? Class)>> _byFragment =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<ModEntry>> _byName =
        new(StringComparer.OrdinalIgnoreCase);

    public QueryReconstructionService(EngineService engine)
    {
        _engine = engine;
        _anchors = new QueryImportService(engine);

        foreach (var (pattern, fragment, itemClass) in engine.PrecomputedFragments())
        {
            if (fragment.Length == 0) continue;
            if (!_byFragment.TryGetValue(fragment, out var list))
            {
                list = [];
                _byFragment[fragment] = list;
            }
            list.Add((pattern, itemClass));
        }

        foreach (var mod in engine.ModsForClasses(engine.PlayableClasses().Select(c => c.Id))
                     .DistinctBy(m => m.Id, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(mod.Name)) continue;
            if (!_byName.TryGetValue(mod.Name, out var list))
            {
                list = [];
                _byName[mod.Name] = list;
            }
            list.Add(mod);
        }
    }

    public ReconstructedQuery Reconstruct(string query)
    {
        if (!_engine.DataReady)
            return ReconstructedQuery.Failed(query, "fragment table is not loaded");
        if (!string.Equals(_engine.TargetModel, FragmentTable.AdvancedRanges, StringComparison.Ordinal))
            return ReconstructedQuery.Failed(query, $"target model '{_engine.TargetModel}' is not supported");

        var shape = QueryShape.Validate(query);
        if (shape.Count > 0)
            return ReconstructedQuery.Failed(query, "invalid query shape: " + shape[0]);

        var anchored = _anchors.Import(query);
        if (anchored.Classes.Count > 1)
            return ReconstructedQuery.Failed(query, "multi-class paste reconstruction is not in Phase 1");
        var itemClass = anchored.Classes.Count == 1 ? anchored.Classes[0] : null;

        var includes = new List<WantedMod>();
        var excludes = new List<string>();

        foreach (var (phrase, excluded) in Explainer.Tokenize(query))
        {
            if (!excluded && phrase.StartsWith(": ", StringComparison.Ordinal))
                continue; // the class anchor is already resolved

            if (TryNamePick(phrase, itemClass, out var namePick))
            {
                if (excluded)
                    return ReconstructedQuery.Failed(query, "name exclusions are not in Phase 1");
                includes.Add(namePick!);
                continue;
            }

            var branches = SplitAlternatives(phrase);
            if (branches.Count == 0)
                return ReconstructedQuery.Failed(query, $"empty term '{phrase}'");

            var resolved = new List<WantedMod>();
            foreach (var branch in branches)
            {
                if (!TryFragmentPick(branch, itemClass, out var pick))
                    return ReconstructedQuery.Failed(query,
                        $"fragment '{branch}' is not an unfactored table fragment for this scope (term '{phrase}')");
                resolved.Add(pick!);
            }

            if (excluded)
            {
                if (resolved.Count != 1)
                    return ReconstructedQuery.Failed(query, "multi-branch exclusions are not in Phase 1");
                excludes.Add(resolved[0].ModId);
            }
            else
            {
                includes.AddRange(resolved);
            }
        }

        var any = _engine.CompilePicks(includes, excludes, false, itemClass);
        var all = _engine.CompilePicks(includes, excludes, true, itemClass);
        var anyMatch = any.Query == query;
        var allMatch = includes.Count > 1 && all.Query == query;
        if (!anyMatch && !allMatch)
            return ReconstructedQuery.Failed(query,
                $"recompiled query differs (any: '{any.Query}'; all: '{all.Query}')");

        return new ReconstructedQuery(
            true, query, itemClass is null ? "simple" : "advanced",
            itemClass, includes, excludes, !anyMatch && allMatch, []);
    }

    private bool TryNamePick(string phrase, string? itemClass, out WantedMod? pick)
    {
        pick = null;
        var branches = SplitAlternatives(phrase);
        if (branches.Count == 0) return false;

        var names = new List<string>();
        foreach (var branch in branches)
        {
            var name = UnescapeLiteral(branch.Trim());
            if (name.Length == 0 || IsRegexLike(name)) return false;
            names.Add(name);
        }

        var matches = new List<ModEntry>();
        foreach (var name in names)
        {
            var found = LookupName(name, itemClass);
            if (found.Count == 0) return false; // fall through to the fragment path
            matches.AddRange(found);
        }

        var families = matches
            .Select(m => m.Family)
            .Where(f => f.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (families.Count != 1) return false;

        var representative = matches.OrderBy(m => m.Id, StringComparer.Ordinal).First();
        pick = new WantedMod(representative.Id, Names: names);
        return true;
    }

    private List<ModEntry> LookupName(string name, string? itemClass)
    {
        if (!_byName.TryGetValue(name, out var mods)) return [];
        return itemClass is null
            ? mods
            : mods.Where(m => m.Classes.Contains(itemClass, StringComparer.Ordinal)).ToList();
    }

    private bool TryFragmentPick(string branch, string? itemClass, out WantedMod? pick)
    {
        pick = null;
        if (!_byFragment.TryGetValue(branch, out var matches)) return false;

        var scoped = itemClass is null
            ? matches
            : matches.Where(m => m.Class is null || string.Equals(m.Class, itemClass, StringComparison.Ordinal)).ToList();
        if (scoped.Count == 0) return false;

        var mods = _engine.ModsForPattern(scoped[0].Pattern)
            .OrderBy(m => m.Id, StringComparer.Ordinal)
            .ToList();
        if (mods.Count == 0) return false;

        var mod = itemClass is null
            ? mods[0]
            : mods.FirstOrDefault(m => m.Classes.Contains(itemClass, StringComparer.Ordinal)) ?? mods[0];
        pick = new WantedMod(mod.Id);
        return true;
    }

    private static bool IsRegexLike(string value) =>
        value.Any(ch => @"\.+*?()[]{}^$|".Contains(ch));

    private static string UnescapeLiteral(string value)
    {
        try
        {
            return Regex.Unescape(value);
        }
        catch (ArgumentException)
        {
            return value;
        }
    }

    /// <summary>Splits a quoted term on top-level | only (not inside groups or classes).</summary>
    private static List<string> SplitAlternatives(string phrase)
    {
        var result = new List<string>();
        var start = 0;
        var depth = 0;
        var inClass = false;
        for (var i = 0; i < phrase.Length; i++)
        {
            var ch = phrase[i];
            if (ch == '\\')
            {
                i++;
                continue;
            }
            if (inClass)
            {
                if (ch == ']') inClass = false;
                continue;
            }
            switch (ch)
            {
                case '[':
                    inClass = true;
                    break;
                case '(':
                    depth++;
                    break;
                case ')':
                    if (depth > 0) depth--;
                    break;
                case '|' when depth == 0:
                    result.Add(phrase[start..i]);
                    start = i + 1;
                    break;
            }
        }
        result.Add(phrase[start..]);
        return result.Where(x => x.Length > 0).ToList();
    }
}

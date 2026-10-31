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

/// <summary>One reconstructed slot/scope with canonical picks.</summary>
public sealed record ReconstructedSlot(
    string SlotId,
    string ClassId,
    IReadOnlyList<WantedMod> Includes,
    bool MatchAll);

/// <summary>Everything needed to apply a parsed query back to the workbench.</summary>
public sealed record ReconstructedWorkbench(
    bool Ok,
    string Mode,
    IReadOnlyList<ReconstructedSlot> Slots,
    IReadOnlyList<string> Excludes,
    IReadOnlyList<string> Diagnostics,
    IReadOnlyList<string> Warnings)
{
    public static ReconstructedWorkbench Failed(string reason) =>
        new(false, "simple", [], [], [reason], []);

    /// <summary>Replaces the workbench criteria with the recovered picks.</summary>
    public void ApplyTo(WorkbenchState state)
    {
        if (!Ok || Slots.Count == 0) return;
        state.WithSuppressedNotifications(() =>
        {
            foreach (var slot in GearSlot.All)
                state.ClearChips(slot.Id);
            state.ClearChips(null);
            state.SetMode(Mode);

            if (Mode == "simple")
            {
                state.AddIncludes(null, Slots[0].Includes);
                state.SetMatchAll(null, Slots[0].MatchAll);
                foreach (var id in Excludes) state.AddExclude(null, id);
                return;
            }

            foreach (var slot in Slots)
            {
                state.SetSlotClass(slot.SlotId, slot.ClassId);
                state.AddIncludes(slot.SlotId, slot.Includes);
                state.SetMatchAll(slot.SlotId, slot.MatchAll);
                if (Slots.Count == 1)
                    foreach (var id in Excludes)
                        state.AddExclude(slot.SlotId, id);
            }

            if (Slots.Count > 1)
                foreach (var id in Excludes)
                    state.AddExclude(null, id);
            state.FocusSlot(Slots[0].SlotId);
        });
    }

    /// <summary>Recompiles the recovered workbench and returns the query string.</summary>
    public string? Recompile(EngineService engine)
    {
        if (!Ok || Slots.Count == 0) return null;
        if (Mode == "simple")
            return engine.CompilePicks(Slots[0].Includes, Excludes, Slots[0].MatchAll, null).Query;
        if (Slots.Count == 1)
            return engine.CompilePicks(Slots[0].Includes, Excludes, Slots[0].MatchAll, Slots[0].ClassId).Query;
        return engine.Combine(
            Slots.Select(s => (s.SlotId, s.ClassId, (IEnumerable<WantedMod>)s.Includes, s.MatchAll)),
            Excludes, allowOverflow: true).Query;
    }
}

/// <summary>
/// Reverse mapping for our own strings. It uses the shipped fragment table and
/// catalog affix names only; it does not run the shortest-form search.
/// </summary>
public sealed class QueryReconstructionService
{
    private readonly EngineService _engine;
    private readonly QueryImportService _anchors;
    private readonly Dictionary<string, List<(string Pattern, string? Class)>> _byFragment =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<ModEntry>> _byName =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ModFamily> _familyByKey =
        new(StringComparer.Ordinal);

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

        var playableMods = engine.ModsForClasses(engine.PlayableClasses().Select(c => c.Id))
            .DistinctBy(m => m.Id, StringComparer.Ordinal)
            .ToList();
        foreach (var mod in playableMods)
        {
            if (string.IsNullOrWhiteSpace(mod.Name)) continue;
            if (!_byName.TryGetValue(mod.Name, out var list))
            {
                list = [];
                _byName[mod.Name] = list;
            }
            list.Add(mod);
        }
        foreach (var family in ModFamilies.Group(playableMods))
            _familyByKey[family.Key] = family;
    }

    /// <summary>
    /// Full workbench reconstruction: simple, one advanced slot, or a multi-class
    /// paste. The caller compares <see cref="ReconstructedWorkbench.Recompile"/>
    /// with the source string before applying.
    /// </summary>
    public ReconstructedWorkbench ReconstructWorkbench(string query)
    {
        if (!_engine.DataReady)
            return ReconstructedWorkbench.Failed("fragment table is not loaded");
        if (!string.Equals(_engine.TargetModel, FragmentTable.AdvancedRanges, StringComparison.Ordinal))
            return ReconstructedWorkbench.Failed($"target model '{_engine.TargetModel}' is not supported");

        var shape = QueryShape.Validate(query);
        if (shape.Count > 0)
            return ReconstructedWorkbench.Failed("invalid query shape: " + shape[0]);

        var anchored = _anchors.Import(query);
        if (anchored.Classes.Count > 1)
            return ReconstructPaste(query, anchored.Classes);

        var single = Reconstruct(query);
        if (!single.Ok)
            return ReconstructedWorkbench.Failed(single.Diagnostics.FirstOrDefault() ?? "reconstruction failed");
        if (single.ItemClass is not null && single.Includes.Count == 0)
            return ReconstructedWorkbench.Failed(
                "a class-only advanced string cannot be represented by a slot without picks");

        var slotId = single.ItemClass is null
            ? "global"
            : SlotForClass(single.ItemClass, new HashSet<string>(StringComparer.Ordinal)) ?? "weapon";
        var slot = new ReconstructedSlot(slotId, single.ItemClass ?? "", single.Includes, single.MatchAll);
        return new ReconstructedWorkbench(
            true, single.ItemClass is null ? "simple" : "advanced",
            [slot], single.Excludes, [], []);
    }

    private ReconstructedWorkbench ReconstructPaste(string query, IReadOnlyList<string> scope)
    {
        var classes = scope.Distinct(StringComparer.Ordinal).OrderBy(c => c, StringComparer.Ordinal).ToList();
        var builds = classes.ToDictionary(c => c, c => new SlotBuild(c), StringComparer.Ordinal);
        var excludes = new List<string>();
        var warnings = new List<string>();

        foreach (var (phrase, excluded) in Explainer.Tokenize(query))
        {
            if (excluded)
            {
                if (!TryFragmentPicks(phrase, null, out var excludedPicks, out var why))
                    return ReconstructedWorkbench.Failed(why);
                excludes.AddRange(excludedPicks.Select(p => p.ModId));
                continue;
            }

            if (phrase.StartsWith(": ", StringComparison.Ordinal))
            {
                var separator = TopLevelSeparator(phrase);
                if (separator < 0) continue; // anchor or class-only constraint

                var classPart = phrase[..separator];
                var fragPart = phrase[(separator + 1)..];
                var others = _anchors.Import(Poe2Syntax.Quote(classPart)).Classes;
                if (others.Count == 0)
                    return ReconstructedWorkbench.Failed($"group '{classPart}' does not resolve to a class");
                var targets = classes.Where(c => !others.Contains(c, StringComparer.Ordinal)).ToList();
                if (targets.Count == 0)
                    return ReconstructedWorkbench.Failed($"group '{classPart}' targets no selected class");

                if (!TryFragmentPicks(fragPart, targets[0], out var picks, out var why))
                    return ReconstructedWorkbench.Failed(why);
                foreach (var target in targets) builds[target].Add(picks);
                continue;
            }

            if (!TryFragmentPicks(phrase, classes[0], out var shared, out var sharedWhy))
                return ReconstructedWorkbench.Failed(sharedWhy);
            foreach (var target in classes) builds[target].Add(shared);
        }

        var empty = builds.Values.FirstOrDefault(b => b.Picks.Count == 0);
        if (empty is not null)
            return ReconstructedWorkbench.Failed(
                $"class '{empty.ClassId}' has no recoverable picks; a class-only anchor cannot be applied");

        var slots = new List<ReconstructedSlot>();
        var usedSlots = new HashSet<string>(StringComparer.Ordinal);
        var rings = builds.Values.Where(b => b.ClassId == "Ring").ToList();
        if (rings.Count > 0)
        {
            var ringPicks = rings.SelectMany(b => b.Picks).ToList();
            slots.Add(new ReconstructedSlot("ring1", "Ring", ringPicks,
                rings.Any(b => b.Tokens > 1)));
            usedSlots.Add("ring1");
            if (rings.Sum(b => b.Picks.Count) > 0)
                warnings.Add("ring1/ring2 merge into one Ring scope (accepted)");
        }

        foreach (var build in builds.Values.Where(b => b.ClassId != "Ring")
                     .OrderBy(b => b.ClassId, StringComparer.Ordinal))
        {
            var slotId = SlotForClass(build.ClassId, usedSlots);
            if (slotId is null)
                return ReconstructedWorkbench.Failed($"class '{build.ClassId}' has no free gear slot");
            usedSlots.Add(slotId);
            slots.Add(new ReconstructedSlot(slotId, build.ClassId, build.Picks, build.Tokens > 1));
        }

        var recompiled = new ReconstructedWorkbench(true, "advanced", slots, excludes, [], warnings).Recompile(_engine);
        if (recompiled == query)
            return new ReconstructedWorkbench(true, "advanced", slots, excludes, [], warnings);

        // Same-class all-of falls back to any-of in the shipped composer; try
        // the any-of form before failing.
        var anyOf = slots.Select(s => s with { MatchAll = false }).ToList();
        var fallback = new ReconstructedWorkbench(true, "advanced", anyOf, excludes, [], warnings).Recompile(_engine);
        if (fallback == query)
        {
            warnings.Add("same-class all-of falls back to any-of (accepted)");
            return new ReconstructedWorkbench(true, "advanced", anyOf, excludes, [], warnings);
        }

        return ReconstructedWorkbench.Failed(
            $"recompiled paste differs (got '{recompiled}'; any-of '{fallback}')");
    }

    private sealed class SlotBuild(string classId)
    {
        public string ClassId { get; } = classId;
        public List<WantedMod> Picks { get; } = [];
        public int Tokens { get; private set; }

        public void Add(IEnumerable<WantedMod> picks)
        {
            Tokens++;
            foreach (var pick in picks)
                if (!Picks.Any(p => p.SamePick(pick)))
                    Picks.Add(pick);
        }
    }

    private static readonly Dictionary<string, string> FixedSlots = new(StringComparer.Ordinal)
    {
        ["Helmet"] = "helmet",
        ["Body Armour"] = "body",
        ["Gloves"] = "gloves",
        ["Boots"] = "boots",
        ["Belt"] = "belt",
        ["Amulet"] = "amulet",
    };

    private static readonly HashSet<string> NonWeaponClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Helmet", "Body Armour", "Gloves", "Boots", "Belt", "Amulet", "Ring",
    };

    private static string? SlotForClass(string classId, HashSet<string> used)
    {
        if (FixedSlots.TryGetValue(classId, out var fixedSlot))
            return used.Contains(fixedSlot) ? null : fixedSlot;
        if (classId == "Ring") return used.Contains("ring1") ? null : "ring1";
        if (NonWeaponClasses.Contains(classId)) return null;
        if (!used.Contains("weapon")) return "weapon";
        if (!used.Contains("offhand")) return "offhand";
        return null;
    }

    /// <summary>Index of a top-level | in a class-prefixed group, or -1 for an anchor.</summary>
    private static int TopLevelSeparator(string phrase)
    {
        var depth = 0;
        var inClass = false;
        for (var i = 0; i < phrase.Length; i++)
        {
            var ch = phrase[i];
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
                case '|' when depth == 0: return i;
            }
        }
        return -1;
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

            if (TryNamePick(phrase, itemClass, out var namePick, out var nameFailure))
            {
                if (excluded)
                    return ReconstructedQuery.Failed(query, "name exclusions are not in Phase 1");
                includes.Add(namePick!);
                continue;
            }
            if (nameFailure is not null)
                return ReconstructedQuery.Failed(query, nameFailure);

            if (!TryFragmentPicks(phrase, itemClass, out var resolved, out var why))
                return ReconstructedQuery.Failed(query, why);

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

    private bool TryNamePick(string phrase, string? itemClass, out WantedMod? pick, out string? failure)
    {
        pick = null;
        failure = null;
        if (TryBuildNamePick(SplitAlternatives(phrase), itemClass, out pick, out failure))
            return true;
        if (failure is not null) return false;

        // Factored name sets, e.g. `of the (Bear|Brute|...)`.
        var expanded = ExpandFactored(phrase);
        return expanded.Count > 1 &&
               TryBuildNamePick(expanded, itemClass, out pick, out failure);
    }

    private bool TryBuildNamePick(
        IReadOnlyList<string> parts, string? itemClass, out WantedMod? pick, out string? failure)
    {
        pick = null;
        failure = null;
        var names = new List<string>();
        foreach (var part in parts)
        {
            var name = UnescapeLiteral(part.Trim());
            if (name.Length == 0 || IsRegexLike(name)) return false;
            names.Add(name);
        }

        var found = names.Select(name => (Name: name, Mods: LookupName(name, itemClass))).ToList();
        if (found.All(x => x.Mods.Count == 0)) return false; // not a name set; try fragments

        var missing = found.FirstOrDefault(x => x.Mods.Count == 0);
        if (missing.Name is not null)
        {
            failure = $"affix name '{missing.Name}' is not in the catalog for this scope";
            return false;
        }

        var matches = found.SelectMany(x => x.Mods).ToList();
        // The names are the query term; the family only supplies identity. If
        // the same names exist in several families, choose one deterministically.
        var representative = matches
            .OrderBy(m => m.Family, StringComparer.Ordinal)
            .ThenBy(m => m.Id, StringComparer.Ordinal)
            .First();
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

    private bool TryFragmentPicks(
        string term, string? itemClass, out List<WantedMod> picks, out string failure)
    {
        picks = [];
        failure = "";
        if (TryFragmentPick(term, itemClass, out var direct))
        {
            picks.Add(direct!);
            return true;
        }

        var branches = SplitAlternatives(term);
        if (branches.Count == 0)
        {
            failure = $"empty term '{term}'";
            return false;
        }

        foreach (var branch in branches)
        {
            var expanded = ExpandFactored(branch);
            foreach (var candidate in expanded)
            {
                if (!TryFragmentPick(candidate, itemClass, out var pick))
                {
                    failure = $"fragment '{candidate}' is not an unfactored table fragment for this scope";
                    return false;
                }
                picks.Add(pick!);
            }
        }
        return true;
    }

    private static List<string> ExpandFactored(string term)
    {
        var result = new List<string>();
        ExpandFactored(term, result, 0);
        return result;
    }

    private static void ExpandFactored(string term, List<string> output, int depth)
    {
        if (depth >= 4 || output.Count >= 128 || !term.EndsWith(')'))
        {
            output.Add(term);
            return;
        }

        var open = MatchingOpen(term);
        if (open <= 0)
        {
            output.Add(term);
            return;
        }

        var alternatives = SplitAlternatives(term[(open + 1)..^1]);
        if (alternatives.Count < 2)
        {
            output.Add(term);
            return;
        }

        var prefix = term[..open];
        if (prefix.Length == 0)
        {
            output.Add(term);
            return;
        }

        foreach (var alternative in alternatives)
            ExpandFactored(prefix + alternative, output, depth + 1);
    }

    private static int MatchingOpen(string term)
    {
        var depth = 0;
        var inClass = false;
        for (var i = term.Length - 1; i >= 0; i--)
        {
            var ch = term[i];
            if (inClass)
            {
                if (ch == '[') inClass = false;
                continue;
            }
            if (ch == ']') { inClass = true; continue; }
            if (ch == ')') depth++;
            else if (ch == '(')
            {
                depth--;
                if (depth == 0) return i;
            }
        }
        return -1;
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

        // Prefer the tierless family form: the query has no tier information.
        foreach (var familyKey in mods
                     .Select(m => m.Family)
                     .Where(f => f.Length > 0)
                     .Distinct(StringComparer.Ordinal)
                     .OrderBy(f => f, StringComparer.Ordinal))
        {
            if (!_familyByKey.TryGetValue(familyKey, out var family)) continue;
            var candidate = family.Pick();
            if (CompilesToTerm(candidate, branch, itemClass))
            {
                pick = candidate;
                return true;
            }
        }

        // Fallback: a family pick that would broaden the query is not used.
        var mod = itemClass is null
            ? mods[0]
            : mods.FirstOrDefault(m => m.Classes.Contains(itemClass, StringComparer.Ordinal)) ?? mods[0];
        pick = new WantedMod(mod.Id);
        return true;
    }

    private bool CompilesToTerm(WantedMod pick, string term, string? itemClass)
    {
        var compiled = _engine.CompilePicks([pick], [], false, itemClass);
        var terms = new List<string>();
        foreach (var (phrase, excluded) in Explainer.Tokenize(compiled.Query))
        {
            if (!excluded && phrase.StartsWith(": ", StringComparison.Ordinal)) continue;
            terms.Add(phrase);
        }
        return terms.Count == 1 && terms[0] == term;
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

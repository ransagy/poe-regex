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
                // Exclusions are global in the string, but a slot with only
                // exclusions still needs one to enroll (or it would vanish).
                foreach (var id in Excludes)
                    state.AddExclude(slot.SlotId, id);
            }
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
    private readonly Dictionary<string, List<ModEntry>> _byName =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ModFamily> _familyByKey =
        new(StringComparer.Ordinal);

    public QueryReconstructionService(EngineService engine)
    {
        _engine = engine;
        _anchors = new QueryImportService(engine);

        // Reuse the picker's caches: PlayableMods and its family grouping are
        // already warm by the time a dialog opens, and rebuilding them here
        // used to freeze the first Preview for seconds.
        var playableMods = engine.PlayableMods();
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
        foreach (var family in ModFamilies.CachedGroup(playableMods))
            _familyByKey[family.Key] = family;
    }

    /// <summary>
    /// Full workbench reconstruction: simple, one advanced slot, or a multi-class
    /// paste. The caller compares <see cref="ReconstructedWorkbench.Recompile"/>
    /// with the source string before applying.
    /// </summary>
    /// <summary>
    /// Resolves only the class-anchor term(s), never the whole query. Running
    /// QueryImportService on a large mixed group can scan the corpus through
    /// explain and freeze the UI.
    /// </summary>
    private IReadOnlyList<string> AnchorClasses(string query)
    {
        var classes = new List<string>();
        foreach (var (phrase, excluded) in Explainer.Tokenize(query))
        {
            if (excluded || !phrase.StartsWith(": ", StringComparison.Ordinal)) continue;
            if (TopLevelSeparator(phrase) >= 0) continue; // a paste group, not the anchor
            classes.AddRange(_anchors.Import(Poe2Syntax.Quote(phrase)).Classes);
            break;
        }
        return classes.Distinct(StringComparer.Ordinal).ToList();
    }

    public ReconstructedWorkbench ReconstructWorkbench(string query)
    {
        if (!_engine.DataReady)
            return ReconstructedWorkbench.Failed("fragment table is not loaded");
        if (!string.Equals(_engine.TargetModel, FragmentTable.AdvancedRanges, StringComparison.Ordinal))
            return ReconstructedWorkbench.Failed($"target model '{_engine.TargetModel}' is not supported");

        var shape = QueryShape.Validate(query);
        if (shape.Count > 0)
            return ReconstructedWorkbench.Failed("invalid query shape: " + shape[0]);

        var anchored = AnchorClasses(query);
        if (anchored.Count > 1)
            return ReconstructPaste(query, anchored);

        var single = Reconstruct(query);
        if (!single.Ok)
            return ReconstructedWorkbench.Failed(single.Diagnostics.FirstOrDefault() ?? "reconstruction failed");
        if (single.ItemClass is not null && single.Includes.Count == 0 && single.Excludes.Count == 0)
            return ReconstructedWorkbench.Failed(
                "a class-only advanced string cannot be represented by a slot without picks or exclusions");

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
        var used = new List<WantedMod>();

        foreach (var (phrase, excluded) in Explainer.Tokenize(query))
        {
            if (excluded)
            {
                if (!TryResolveSinglePick(phrase, null, used, out var excludedPick, out var why,
                        new HashSet<string>(excludes, StringComparer.Ordinal)))
                    return ReconstructedWorkbench.Failed(why);
                excludes.Add(excludedPick!.ModId);
                used.Add(excludedPick);
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

                if (!TryResolveGroup(fragPart, targets[0], used, out var picks, out var why))
                    return ReconstructedWorkbench.Failed(why);
                foreach (var target in targets) builds[target].Add(picks);
                used.AddRange(picks);
                continue;
            }

            if (!TryResolveGroup(phrase, classes[0], used, out var shared, out var sharedWhy))
                return ReconstructedWorkbench.Failed(sharedWhy);
            foreach (var target in classes) builds[target].Add(shared);
            used.AddRange(shared);
        }

        var multiPickGroup = builds.Values.FirstOrDefault(b => b.Tokens > 1 && b.HasMultiPick);
        if (multiPickGroup is not null)
            return ReconstructedWorkbench.Failed(
                $"class '{multiPickGroup.ClassId}' has an all-of group that is not a single pick");

        var empty = builds.Values.FirstOrDefault(b => b.Picks.Count == 0);
        if (empty is not null && excludes.Count == 0)
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

    private bool TryResolveGroup(
        string term, string itemClass, IReadOnlyList<WantedMod> used,
        out List<WantedMod> picks, out string failure)
    {
        if (TryResolveSinglePick(term, itemClass, used, out var single, out _))
        {
            picks = [single!];
            failure = "";
            return true;
        }
        return TryResolveTerm(term, itemClass, used, out picks, out failure);
    }

    private sealed class SlotBuild(string classId)
    {
        public string ClassId { get; } = classId;
        public List<WantedMod> Picks { get; } = [];
        public int Tokens { get; private set; }
        public bool HasMultiPick { get; private set; }

        public void Add(IReadOnlyList<WantedMod> picks)
        {
            Tokens++;
            if (picks.Count > 1) HasMultiPick = true;
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

        var anchored = AnchorClasses(query);
        if (anchored.Count > 1)
            return ReconstructedQuery.Failed(query, "multi-class paste reconstruction is not in Phase 1");
        var itemClass = anchored.Count == 1 ? anchored[0] : null;

        var includes = new List<WantedMod>();
        var excludes = new List<string>();
        // Occurrence-aware: a duplicate fragment must resolve to a distinct
        // pick, or applying it would collapse the duplicate.
        var used = new List<WantedMod>();
        var tokens = Explainer.Tokenize(query).ToList();
        var includeTerms = tokens
            .Where(t => !t.Excluded && !t.Phrase.StartsWith(": ", StringComparison.Ordinal))
            .ToList();
        // One group is any-of; one group per pick is all-of. Each all-of term
        // must resolve to one pick (a family's variants are ORed inside it).
        var allOf = includeTerms.Count > 1;

        foreach (var (phrase, excluded) in tokens)
        {
            if (!excluded && phrase.StartsWith(": ", StringComparison.Ordinal))
                continue; // the class anchor is already resolved

            if (excluded)
            {
                if (!TryResolveSinglePick(phrase, itemClass, used, out var excludedPick,
                        out var excludeWhy, new HashSet<string>(excludes, StringComparer.Ordinal)))
                    return ReconstructedQuery.Failed(query, excludeWhy);
                excludes.Add(excludedPick!.ModId);
                used.Add(excludedPick);
                continue;
            }

            if (allOf)
            {
                if (!TryResolveSinglePick(phrase, itemClass, used, out var single, out var singleWhy))
                    return ReconstructedQuery.Failed(query, singleWhy);
                includes.Add(single!);
                used.Add(single!);
            }
            else
            {
                if (!TryResolveTerm(phrase, itemClass, used, out var resolved, out var why))
                    return ReconstructedQuery.Failed(query, why);
                includes.AddRange(resolved);
                used.AddRange(resolved);
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

    /// <summary>Resolves an any-of term: every top-level branch may be a name or a fragment.</summary>
    private bool TryResolveTerm(
        string term, string? itemClass, IReadOnlyList<WantedMod> used,
        out List<WantedMod> picks, out string failure)
    {
        picks = [];
        failure = "";
        if (TryNamePick(term, itemClass, out var namePick, out var nameFailure))
        {
            picks.Add(namePick!);
            return true;
        }
        if (nameFailure is not null)
        {
            failure = nameFailure;
            return false;
        }

        // A group can mix names and fragments. Resolve the names as a set so
        // an app-built tier-or-better set becomes one pick, not one per name.
        var names = new List<string>();
        var fragments = new List<string>();
        foreach (var branch in SplitAlternatives(term))
        {
            foreach (var candidate in ExpandFactored(branch))
            {
                if (TryNamePick(candidate, itemClass, out var branchName, out var branchNameFailure))
                {
                    if (branchNameFailure is not null)
                    {
                        failure = branchNameFailure;
                        return false;
                    }
                    names.AddRange(branchName!.Names ?? []);
                }
                else
                {
                    fragments.Add(candidate);
                }
            }
        }

        if (names.Count > 0)
            foreach (var pick in ResolveNameAlternatives(names, itemClass))
                AddUnique(picks, pick);

        foreach (var fragment in fragments)
        {
            if (!TryFragmentPicks(fragment, itemClass, used, out var resolved, out var fragmentFailure))
            {
                failure = fragmentFailure;
                return false;
            }
            foreach (var pick in resolved) AddUnique(picks, pick);
        }

        if (picks.Count == 0)
        {
            failure = $"term '{term}' has no recoverable picks";
            return false;
        }
        return true;
    }

    /// <summary>
    /// Turns the name alternatives of a mixed group into the same picks the
    /// picker creates: one canonical tier-or-better pick per family suffix,
    /// and individual picks for names outside a suffix.
    /// </summary>
    private List<WantedMod> ResolveNameAlternatives(IReadOnlyList<string> names, string? itemClass)
    {
        var picks = new List<WantedMod>();
        var remaining = names.Distinct(StringComparer.Ordinal).ToList();
        while (remaining.Count > 1 && TryTakeLargestNameSuffix(remaining, itemClass, out var suffixPick))
            AddUnique(picks, suffixPick!);
        foreach (var name in remaining)
            if (TryBuildNamePick([name], itemClass, out var pick, out _))
                AddUnique(picks, pick!);
        return picks;
    }

    private bool TryTakeLargestNameSuffix(
        List<string> remaining, string? itemClass, out WantedMod? pick)
    {
        pick = null;
        var wanted = remaining.ToHashSet(StringComparer.Ordinal);
        ModFamily? bestFamily = null;
        ModTier? bestTier = null;
        var bestCount = 1;
        foreach (var family in _familyByKey.Values.OrderBy(f => f.Key, StringComparer.Ordinal))
        {
            foreach (var tier in family.Tiers)
            {
                if (tier.Name.Length == 0) continue;
                var suffix = family.NamesFrom(tier);
                if (suffix.Count <= bestCount || !suffix.All(wanted.Contains)) continue;
                if (itemClass is not null &&
                    !tier.Members.Any(m => m.Classes.Contains(itemClass, StringComparer.Ordinal)))
                    continue;
                bestFamily = family;
                bestTier = tier;
                bestCount = suffix.Count;
            }
        }

        if (bestFamily is null || bestTier is null) return false;
        pick = NamePickFor(bestFamily, bestTier, itemClass);
        foreach (var name in bestFamily.NamesFrom(bestTier)) remaining.Remove(name);
        return true;
    }

    /// <summary>
    /// A family pick whose anchor is legal in the slot scope. The family is
    /// global, but its lowest tier may exist only on another class (the melee
    /// skill-level family has Gloves and weapon tiers); Your picks labels the
    /// anchor from the slot's own mods.
    /// </summary>
    private static WantedMod FamilyPickFor(ModFamily family, string? itemClass)
    {
        var pick = family.Pick();
        if (itemClass is null) return pick;
        var anchor = family.Tiers
            .SelectMany(t => t.Members)
            .FirstOrDefault(m => m.Classes.Contains(itemClass, StringComparer.Ordinal));
        return anchor is null ? pick : pick with { ModId = anchor.Id };
    }

    /// <summary>One multi-name pick in the picker's canonical tier-or-better order.</summary>
    private static WantedMod NamePickFor(ModFamily family, ModTier tier, string? itemClass)
    {
        var anchor = itemClass is null
            ? tier.Anchor
            : tier.Members.FirstOrDefault(m => m.Classes.Contains(itemClass, StringComparer.Ordinal))
              ?? tier.Anchor;
        return new WantedMod(anchor.Id, Names: family.NamesFrom(tier));
    }

    private bool TryCanonicalNameSet(
        IReadOnlyList<string> names, string? itemClass, out WantedMod? pick)
    {
        pick = null;
        if (names.Count == 0) return false;
        var wanted = names.ToHashSet(StringComparer.Ordinal);
        if (wanted.Count != names.Count) return false;
        foreach (var family in _familyByKey.Values.OrderBy(f => f.Key, StringComparer.Ordinal))
        {
            foreach (var tier in family.Tiers)
            {
                if (tier.Name.Length == 0) continue;
                var suffix = family.NamesFrom(tier);
                if (suffix.Count != wanted.Count || !suffix.All(wanted.Contains)) continue;
                if (itemClass is not null &&
                    !tier.Members.Any(m => m.Classes.Contains(itemClass, StringComparer.Ordinal)))
                    continue;
                pick = NamePickFor(family, tier, itemClass);
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Resolves an all-of term to exactly one pick. A family pick is one pick
    /// even though its variants compile to several ORed fragments; splitting it
    /// into separate picks would turn the family OR into AND.
    /// </summary>
    private bool TryResolveSinglePick(
        string term, string? itemClass, IReadOnlyList<WantedMod> used,
        out WantedMod? pick, out string failure, ISet<string>? forbiddenIds = null)
    {
        pick = null;
        failure = "";
        if (TryNamePick(term, itemClass, out var namePick, out var nameFailure))
        {
            pick = namePick;
            return true;
        }
        if (nameFailure is not null)
        {
            failure = nameFailure;
            return false;
        }

        if (TryFragmentPick(term, itemClass, used, out var direct, forbiddenIds))
        {
            pick = direct;
            return true;
        }

        var candidates = new List<WantedMod>();
        foreach (var branch in SplitAlternatives(term))
        {
            foreach (var candidate in ExpandFactored(branch))
            {
                if (!TryFragmentPick(candidate, itemClass, used, out var branchPick, forbiddenIds))
                {
                    failure = $"fragment '{candidate}' is not an unfactored table fragment for this scope";
                    return false;
                }
                AddUnique(candidates, branchPick!);
            }
        }

        if (candidates.Count == 0)
        {
            failure = $"term '{term}' has no recoverable pick";
            return false;
        }
        if (candidates.Count == 1 || candidates.All(p => p.SamePick(candidates[0])))
        {
            pick = candidates[0];
            return true;
        }

        // All alternatives from one family are one tierless pick.
        var families = candidates
            .Select(p => _engine.FindMod(p.ModId)?.Family)
            .Where(f => !string.IsNullOrEmpty(f))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (families.Count == 1 && _familyByKey.TryGetValue(families[0]!, out var family))
        {
            var candidate = FamilyPickFor(family, itemClass);
            if (forbiddenIds?.Contains(candidate.ModId) != true &&
                !Conflicts(candidate, used) && CompilesToTerm(candidate, term, itemClass))
            {
                pick = candidate;
                return true;
            }
        }

        failure = "all-of term is not a single pick (mixed families/names)";
        return false;
    }

    /// <summary>
    /// True when adding the candidate would collapse into an existing pick:
    /// same identity, or an identical requirement list (which the paste
    /// composer merges across slots).
    /// </summary>
    private static bool Conflicts(WantedMod candidate, IReadOnlyList<WantedMod> used) =>
        used.Any(u => u.SamePick(candidate) ||
                      u.Requirements().SequenceEqual(candidate.Requirements()));

    private static void AddUnique(List<WantedMod> picks, WantedMod pick)
    {
        if (!picks.Any(p => p.SamePick(pick))) picks.Add(pick);
    }

    private bool TryNamePick(string phrase, string? itemClass, out WantedMod? pick, out string? failure)
    {
        pick = null;
        failure = null;

        // Collect names branch by branch so a term can mix plain names and
        // factored name groups: Mal(evolent|ignant)|Twisted|Vile.
        var names = new List<string>();
        foreach (var branch in SplitAlternatives(phrase))
        {
            foreach (var part in ExpandFactored(branch))
            {
                var name = UnescapeLiteral(part.Trim());
                if (name.Length == 0 || IsRegexLike(name)) return false;
                names.Add(name);
            }
        }

        if (names.Count == 0) return false;
        return TryBuildNamePick(names, itemClass, out pick, out failure);
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

        // Prefer the picker's canonical "tier or better" set when the input is
        // exactly that suffix; this is what the applied Your-picks row shows.
        if (TryCanonicalNameSet(names, itemClass, out pick))
        {
            failure = null;
            return true;
        }

        var found = names.Select(name => (Name: name, Mods: LookupName(name, itemClass))).ToList();
        if (found.All(x => x.Mods.Count == 0)) return false; // not a name set; try fragments

        if (found.Any(x => x.Mods.Count == 0))
        {
            // Mixed name/fragment groups are resolved branch by branch; a
            // missing name alone is not enough to reject the term as a name set.
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
        if (itemClass is null) return mods;
        var scoped = mods.Where(m => m.Classes.Contains(itemClass, StringComparer.Ordinal)).ToList();
        // Name literals are class-independent; a shared paste group may name a
        // class whose first target does not own the affix.
        return scoped.Count > 0 ? scoped : mods;
    }

    private bool TryFragmentPicks(
        string term, string? itemClass, IReadOnlyList<WantedMod> used,
        out List<WantedMod> picks, out string failure)
    {
        picks = [];
        failure = "";
        if (TryFragmentPick(term, itemClass, used, out var direct))
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
                if (!TryFragmentPick(candidate, itemClass, used, out var pick))
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

    private bool TryFragmentPick(
        string branch, string? itemClass, IReadOnlyList<WantedMod> used, out WantedMod? pick,
        ISet<string>? forbiddenIds = null)
    {
        pick = null;
        var matches = _anchors.FragmentsFor(branch);
        if (matches.Count == 0) return false;

        var scoped = itemClass is null
            ? matches
            : matches.Where(m => m.Class is null || string.Equals(m.Class, itemClass, StringComparison.Ordinal)).ToList();
        if (scoped.Count == 0) return false;

        // A fragment is class-specific in the table (`o Ev` vs `^\+.*on` for
        // the same line), so try the class-scoped pattern(s) before global.
        var mods = scoped
            .OrderBy(m => m.Class is null ? 1 : 0)
            .SelectMany(m => _engine.ModsForPattern(m.Pattern))
            .Where(m => itemClass is null || m.Classes.Contains(itemClass, StringComparer.Ordinal))
            .DistinctBy(m => m.Id, StringComparer.Ordinal)
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
            var candidate = FamilyPickFor(family, itemClass);
            if (forbiddenIds?.Contains(candidate.ModId) == true) continue;
            if (Conflicts(candidate, used)) continue;
            if (CompilesToTerm(candidate, branch, itemClass))
            {
                pick = candidate;
                return true;
            }
        }

        // Fallback: a family pick that would broaden the query is not used.
        foreach (var mod in mods)
        {
            if (forbiddenIds?.Contains(mod.Id) == true) continue;
            if (Conflicts(new WantedMod(mod.Id), used)) continue;
            var candidate = new WantedMod(mod.Id);
            if (!CompilesToTerm(candidate, branch, itemClass)) continue;
            pick = candidate;
            return true;
        }
        return false;
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

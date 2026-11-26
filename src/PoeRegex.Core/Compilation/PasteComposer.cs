using System.Text.RegularExpressions;
using PoeRegex.Core.Data;
using PoeRegex.Core.Explain;

namespace PoeRegex.Core.Compilation;

/// <summary>One slot's contribution to a combined paste: base class + wanted mods (any of Includes, or every RequiredGroups entry).</summary>
public sealed record PasteSlotCriterion(
    string ItemClass,
    IReadOnlyList<ModifierRequirement> Includes,
    string Label,
    IReadOnlyList<IReadOnlyList<ModifierRequirement>>? RequiredGroups = null)
{
    public bool MatchAll => RequiredGroups is { Count: > 0 };
}

/// <summary>One quoted group of a combined paste (anchor excluded — see result).</summary>
public sealed record PasteGroup(string Label, IReadOnlyList<string> TargetClasses, string Token);

/// <summary>Combined multi-slot paste: anchor + per-criterion exclusion groups, ANDed.</summary>
public sealed record MultiPasteResult
{
    public required string Query { get; init; }
    public required int Length { get; init; }
    public required int Limit { get; init; }
    public bool Exceeded => Length > Limit;
    public required string Anchor { get; init; }
    public required IReadOnlyList<PasteGroup> Groups { get; init; }
    public required IReadOnlyList<CompilerDiagnostic> Diagnostics { get; init; }
    /// <summary>Classes whose same-class all-of merge fell back to any-of.</summary>
    public IReadOnlyList<string> MergedToAnyClasses { get; init; } = [];
}

/// <summary>
/// Composes per-slot criteria into a single stash-paste string: an anchor
/// group over every selected slot's class alternation (see
/// <see cref="ClassAlternation"/>), plus one group per modded criterion
/// listing all selected classes EXCEPT its targets OR'd with the target's mod
/// fragments. Whole-text multiline matching, OR inside groups, AND across
/// groups. Deterministic output; enforces the game limit.
/// </summary>
public sealed class PasteComposer
{
    private readonly EngineCorpus _corpus;
    private readonly CompactionEngine _compaction;
    private readonly Explainer _explainer;
    private readonly bool _validateRecall;
    private readonly Dictionary<string, IReadOnlyList<StatConstraint>> _canonicalCache =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, GroupPlan?> _mergeCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> _unionSafetyCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<ValueInterval>> _classSpansCache =
        new(StringComparer.Ordinal);

    public PasteComposer(EngineCorpus corpus, bool validateRecall = true)
        : this(corpus, new CompactionEngine(corpus), validateRecall)
    {
    }

    public PasteComposer(
        EngineCorpus corpus, CompactionEngine compaction, bool validateRecall = true)
    {
        _corpus = corpus;
        _compaction = compaction;
        _explainer = new Explainer(corpus);
        _validateRecall = validateRecall;
    }

    /// <summary>
    /// Shortest class-name prefix unique across all catalog class names.
    /// See <see cref="EngineCorpus.ClassNamePrefix"/>.
    /// </summary>
    public string ClassPrefix(string itemClass) => _corpus.ClassNamePrefix(itemClass);

    public MultiPasteResult Compose(
        IReadOnlyList<PasteSlotCriterion> slots,
        IEnumerable<ModifierRequirement> excluded,
        bool allowOverflow = false,
        IEnumerable<string>? keywords = null)
    {
        var diagnostics = new List<CompilerDiagnostic>();
        var mergedToAnyClasses = new List<string>();

        // Resolve classes; unknown slots are skipped with a warning.
        var known = new List<PasteSlotCriterion>();
        foreach (var slot in slots)
        {
            if (_corpus.FindItemClass(slot.ItemClass) is null)
            {
                diagnostics.Add(new CompilerDiagnostic(DiagnosticLevel.Warning, "unresolved-class",
                    $"No fingerprint available for item class '{slot.ItemClass}'."));
                continue;
            }
            known.Add(slot);
        }

        // Merge same-class slots (union includes): separate groups would AND
        // together and hide everything, so one class gets one criterion.
        var merged = new List<PasteSlotCriterion>();
        foreach (var group in known.GroupBy(s => s.ItemClass, StringComparer.Ordinal))
        {
            var ordered = group.OrderBy(s => s.Label, StringComparer.Ordinal).ToList();
            var label = string.Join(", ", ordered.Select(s => s.Label).Distinct(StringComparer.Ordinal));
            var contributing = ordered.Where(s => s.Includes.Count > 0 || s.MatchAll).ToList();
            if (contributing.Count == 1 && contributing[0].MatchAll)
            {
                merged.Add(new PasteSlotCriterion(group.Key, [], label, contributing[0].RequiredGroups));
                continue;
            }
            // Two same-class slots cannot each require all of their own mods in one paste.
            if (contributing.Any(s => s.MatchAll))
            {
                mergedToAnyClasses.Add(group.Key);
                diagnostics.Add(new CompilerDiagnostic(DiagnosticLevel.Warning, "paste-merge-any",
                    $"Slots sharing item class '{group.Key}' combine as any-of; all-of needs one slot of that class."));
            }
            var includes = ordered
                .SelectMany(s => s.Includes.Concat(s.RequiredGroups?.SelectMany(g => g) ?? []))
                .Distinct().ToList();
            merged.Add(new PasteSlotCriterion(group.Key, includes, label));
        }
        merged.Sort((a, b) => string.CompareOrdinal(a.ItemClass, b.ItemClass));
        merged = merged.Select(NormalizeSlotRanges).ToList();
        merged = NormalizeRangeSlots(merged);

        var scope = merged.Select(s => s.ItemClass).Distinct(StringComparer.Ordinal).ToList();

        // A single class needs no grouping, so keep the same compact
        // `": <anchor>"` form as the single-slot compiler.
        var anchorInner = scope.Count == 1
            ? ": " + ClassAlternation.Build(_corpus, scope)
            : ": (" + ClassAlternation.Build(_corpus, scope) + ")";
        var anchorDiag = _corpus.Syntax.ValidateFragment(anchorInner);
        if (anchorDiag is not null) diagnostics.Add(anchorDiag);
        ValidateAnchor(anchorInner, scope, diagnostics);

        var plans = BuildGroupPlans(merged);
        var baseBuild = BuildPlan(
            plans, scope, anchorInner, excluded, keywords, _validateRecall);
        var best = baseBuild;
        IReadOnlyList<GroupPlan>? bestPlans = null;
        // Budget relief only: under-budget queries keep their exact
        // round-trippable form. Over-budget UI pastes may use a shorter
        // any-of union when the per-class proof accepts it.
        if (allowOverflow &&
            baseBuild.Query.Length > QuerySyntax.MaxCharacterLimit)
        {
            var candidates = new List<IReadOnlyList<GroupPlan>> { plans };
            candidates.AddRange(BuildUnionPlans(plans));
            foreach (var candidate in candidates.Skip(1))
            {
                var built = BuildPlan(
                    candidate, scope, anchorInner, excluded, keywords,
                    validateRecall: false);
                if (built.Unresolved <= best.Unresolved &&
                    (built.ShapeErrors < best.ShapeErrors ||
                     (built.ShapeErrors == best.ShapeErrors &&
                      built.Query.Length < best.Query.Length)))
                {
                    best = built;
                    bestPlans = candidate;
                }
            }
        }

        var result = bestPlans is null
            ? baseBuild
            : BuildPlan(
                bestPlans, scope, anchorInner, excluded, keywords, _validateRecall);
        diagnostics.AddRange(result.Diagnostics);
        var limit = QuerySyntax.MaxCharacterLimit;
        if (result.Query.Length > limit && !allowOverflow)
            throw new RegexLengthOverflowException(result.Query.Length, limit);
        return new MultiPasteResult
        {
            Query = result.Query,
            Length = result.Query.Length,
            Limit = limit,
            Anchor = anchorInner,
            Groups = result.Groups,
            Diagnostics = diagnostics,
            MergedToAnyClasses = mergedToAnyClasses,
        };
    }

    private sealed record GroupPlan(
        IReadOnlyList<string> Targets,
        IReadOnlyList<ModifierRequirement> Includes,
        string Label,
        IReadOnlyList<IReadOnlyList<ModifierRequirement>>? RequiredGroups = null)
    {
        public bool MatchAll => RequiredGroups is { Count: > 0 };
    }

    private sealed record PlanBuild(
        string Query,
        IReadOnlyList<PasteGroup> Groups,
        IReadOnlyList<CompilerDiagnostic> Diagnostics,
        int ShapeErrors,
        int Unresolved);

    private List<GroupPlan> BuildGroupPlans(IReadOnlyList<PasteSlotCriterion> merged)
    {
        var plans = new List<GroupPlan>();
        foreach (var bucket in merged.Where(s => s.Includes.Count > 0 || s.MatchAll)
                     .GroupBy(CriterionKey, StringComparer.Ordinal))
        {
            var first = bucket.First();
            plans.Add(new GroupPlan(
                bucket.Select(s => s.ItemClass).OrderBy(x => x, StringComparer.Ordinal).ToList(),
                first.Includes.Distinct().ToList(),
                string.Join(", ", bucket.Select(s => s.Label).OrderBy(x => x, StringComparer.Ordinal)),
                first.MatchAll ? first.RequiredGroups : null));
        }
        return plans;
    }

    private PlanBuild BuildPlan(
        IReadOnlyList<GroupPlan> plans,
        IReadOnlyList<string> scope,
        string anchorInner,
        IEnumerable<ModifierRequirement> excluded,
        IEnumerable<string>? keywords,
        bool validateRecall)
    {
        var diagnostics = new List<CompilerDiagnostic>();
        var groups = new List<PasteGroup>();
        var unresolved = 0;
        var blocks = new List<string> { _corpus.Syntax.Quote(anchorInner) };
        foreach (var plan in plans)
        {
            var required = plan.MatchAll
                ? plan.RequiredGroups!
                : [plan.Includes.Distinct().ToList()];
            foreach (var reqs in required)
            {
                var frags = new List<string>();
                var rawFrags = new List<string>();
                var sources = new List<string>();
                // A group that spans classes must use a fragment proven for
                // every target, not just targets[0]. The global scope is the
                // union corpus, so its fragment is safe for all of them.
                var fragmentScope = plan.Targets.Count == 1 ? plan.Targets[0] : null;
                foreach (var req in reqs.Distinct().ToList())
                {
                    var frag = FragmentFor(req, fragmentScope, diagnostics);
                    if (frag is null)
                    {
                        unresolved++;
                        diagnostics.Add(new CompilerDiagnostic(DiagnosticLevel.Warning, "unresolved-modifier",
                            $"Modifier '{req.Value}' could not be resolved; skipped."));
                        continue;
                    }
                    (req.IsRegex ? rawFrags : frags).Add(frag);
                    if (_corpus.FindMod(req.Value) is not null)
                    {
                        sources.Add(req.Value);
                        if (req.RangeModIds is { Count: > 0 } ids)
                            sources.AddRange(ids);
                    }
                }
                if (frags.Count == 0 && rawFrags.Count == 0) continue;
                var terms = CompactionEngine.FactorCommonPrefixes(frags).ToList();
                terms.AddRange(rawFrags);
                var others = scope.Where(c => !plan.Targets.Contains(c, StringComparer.Ordinal)).ToList();
                var token = others.Count > 0
                    ? ": (" + ClassAlternation.Build(_corpus, others) + ")|" + string.Join("|", terms)
                    : string.Join("|", terms);
                var tokenDiag = _corpus.Syntax.ValidateFragment(token);
                if (tokenDiag is not null) diagnostics.Add(tokenDiag);
                groups.Add(new PasteGroup(plan.Label, plan.Targets, token));
                blocks.Add(_corpus.Syntax.Quote(token));
                if (validateRecall)
                    ValidateGroupRecall(
                        token, plan.Targets,
                        sources.Distinct(StringComparer.Ordinal).ToList(),
                        plan.Label, diagnostics);
            }
        }

        // Exclusions union globally: block negation cannot be scoped per slot.
        var excludedTerms = new HashSet<string>(StringComparer.Ordinal);
        foreach (var req in excluded.Distinct().ToList())
        {
            var mod = req.IsRegex ? null : _corpus.FindMod(req.Value);
            var term = req.IsRegex
                ? req.Value
                : mod is not null
                    ? _compaction.SelectFragment(req, null) ?? req.Value
                    : _corpus.Syntax.EscapeLiteral(req.Value);
            if (!excludedTerms.Add(term)) continue;
            blocks.Add(_corpus.Syntax.FormatExclusion(term));
        }

        // Imported raw terms AND with the paste; they are not part of any
        // slot's OR group, so each becomes its own quoted block.
        foreach (var keyword in keywords ?? [])
        {
            if (keyword.Length == 0) continue;
            var diagnostic = _corpus.Syntax.ValidateFragment(keyword);
            if (diagnostic is not null) diagnostics.Add(diagnostic);
            blocks.Add(_corpus.Syntax.Quote(keyword));
        }

        var query = string.Join(" ", blocks);
        var shapeErrors = 0;
        foreach (var shapeError in QueryShape.Validate(query))
        {
            shapeErrors++;
            diagnostics.Add(new CompilerDiagnostic(
                DiagnosticLevel.Warning, "query-shape", shapeError));
        }
        return new PlanBuild(query, groups, diagnostics, shapeErrors, unresolved);
    }

    /// <summary>
    /// Partitions any-of groups into the fewest practical compatible buckets,
    /// then merges each bucket. A group joins a bucket only when the union
    /// adds no valid line to any target class. No query is built until the
    /// partition is known, so an over-budget compile does not compile every
    /// possible pair.
    /// </summary>
    private IEnumerable<IReadOnlyList<GroupPlan>> BuildUnionPlans(
        IReadOnlyList<GroupPlan> basePlans)
    {
        if (basePlans.Count < 2) yield break;
        // Special-source groups use their own scope rules; keep them as they
        // are. Ordinary affixes can still union around them.
        var mergeable = basePlans.Where(CanUnion).ToList();
        if (mergeable.Count < 2 || !HasSharedFamily(mergeable)) yield break;

        var buckets = new List<List<GroupPlan>>();
        foreach (var plan in mergeable)
        {
            var placed = false;
            foreach (var bucket in buckets)
            {
                var merged = MergePlans([.. bucket, plan]);
                if (merged is null || !IsUnionSafe([.. bucket, plan], merged)) continue;
                bucket.Add(plan);
                placed = true;
                break;
            }
            if (!placed) buckets.Add([plan]);
        }

        if (buckets.Count == mergeable.Count) yield break;
        var mergedPlans = new List<GroupPlan>(basePlans.Count);
        foreach (var bucket in buckets)
        {
            var merged = bucket.Count == 1 ? bucket[0] : MergePlans(bucket);
            if (merged is null || !IsUnionSafe(bucket, merged)) yield break;
            mergedPlans.Add(merged);
        }
        mergedPlans.AddRange(basePlans.Where(plan => !mergeable.Contains(plan)));
        mergedPlans.Sort((a, b) => string.CompareOrdinal(a.Targets[0], b.Targets[0]));
        yield return mergedPlans;
    }

    private bool CanUnion(GroupPlan plan) =>
        !plan.MatchAll &&
        plan.Includes.Count > 0 &&
        plan.Includes.All(req =>
            _corpus.FindMod(req.Value) is { } mod &&
            string.IsNullOrEmpty(mod.Source));

    /// <summary>
    /// A quick gate before any range proof: at least two groups must share a
    /// family/pattern. Disjoint class-exclusive sets can still union, but they
    /// are rare and not worth paying for on every over-budget keystroke.
    /// </summary>
    private bool HasSharedFamily(IReadOnlyList<GroupPlan> plans)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var plan in plans)
        foreach (var req in plan.Includes)
        {
            var mod = _corpus.FindMod(req.Value);
            if (mod is null) continue;
            var key = (mod.Family ?? "") + "\u0001" +
                      string.Join("\u0001", mod.StatLines
                          .Select(line => line.Pattern)
                          .Distinct(StringComparer.Ordinal)
                          .OrderBy(pattern => pattern, StringComparer.Ordinal));
            if (!seen.Add(key)) return true;
        }
        return false;
    }

    private IReadOnlyList<StatConstraint> Canonicalize(
        IReadOnlyList<ModifierRequirement> requirements, string? itemClass)
    {
        var key = RequirementsKey(requirements, itemClass);
        if (_canonicalCache.TryGetValue(key, out var cached)) return cached;
        var result = _compaction.Canonicalize(requirements, itemClass);
        _canonicalCache[key] = result;
        return result;
    }

    private GroupPlan? MergePlans(IReadOnlyList<GroupPlan> plans)
    {
        var key = PlanSetKey(plans);
        if (_mergeCache.TryGetValue(key, out var cached)) return cached;
        var result = MergePlansCore(plans);
        _mergeCache[key] = result;
        return result;
    }

    private bool IsUnionSafe(IReadOnlyList<GroupPlan> originalPlans, GroupPlan merged)
    {
        var key = PlanSetKey(originalPlans) + "\u0004" + PlanKey(merged);
        if (_unionSafetyCache.TryGetValue(key, out var cached)) return cached;
        var result = IsUnionSafeCore(originalPlans, merged);
        _unionSafetyCache[key] = result;
        return result;
    }

    private static string RequirementsKey(
        IReadOnlyList<ModifierRequirement> requirements, string? itemClass) =>
        (itemClass ?? "") + "\u0001" +
        string.Join("\u0002", requirements.Select(RequirementKeyForCache));

    private static string RequirementKeyForCache(ModifierRequirement requirement) =>
        (requirement.IsRegex ? "r" : "m") + (requirement.FullFamily ? "f" : "") + "\u0001" +
        requirement.Value + "\u0001" +
        (requirement.MinValue?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "") + "\u0001" +
        (requirement.MaxValue?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "") + "\u0001" +
        string.Join(",", (requirement.RangeModIds ?? [])
            .OrderBy(id => id, StringComparer.Ordinal));

    private static string PlanKey(GroupPlan plan) =>
        string.Join(",", plan.Targets) + "\u0001" + plan.Label + "\u0001" +
        string.Join("\u0002", plan.Includes.Select(RequirementKeyForCache));

    private static string PlanSetKey(IReadOnlyList<GroupPlan> plans) =>
        string.Join("\u0003", plans.Select(PlanKey).OrderBy(key => key, StringComparer.Ordinal));

    private GroupPlan? MergePlansCore(IReadOnlyList<GroupPlan> plans)
    {
        if (plans.Count < 2 || plans.Any(plan => plan.MatchAll)) return null;
        var requirements = plans.SelectMany(plan => plan.Includes).ToList();
        if (requirements.Count == 0 ||
            requirements.Any(req => req.IsRegex || _corpus.FindMod(req.Value) is null))
            return null;

        var constraints = Canonicalize(requirements, null);
        if (constraints.Count == 0) return null;
        var merged = new List<ModifierRequirement>(constraints.Count);
        foreach (var constraint in constraints)
        {
            // Full-family picks keep the family fragment (one anchor id).
            merged.Add(constraint.IsFullFamily
                ? new ModifierRequirement(
                    constraint.AnchorModId,
                    RangeModIds: constraint.MemberIds,
                    FullFamily: true)
                : new ModifierRequirement(
                    constraint.AnchorModId,
                    MinValue: constraint.Intervals.Count == 0 ? null : constraint.Min,
                    MaxValue: constraint.Intervals.Count == 0 ? null : constraint.Max,
                    RangeModIds: constraint.MemberIds));
        }

        var targets = plans.SelectMany(plan => plan.Targets)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(target => target, StringComparer.Ordinal)
            .ToList();
        return new GroupPlan(
            targets,
            merged,
            string.Join(", ", targets));
    }

    private bool IsUnionSafeCore(IReadOnlyList<GroupPlan> originalPlans, GroupPlan merged)
    {
        foreach (var plan in originalPlans)
        foreach (var target in plan.Targets)
        {
            var original = Canonicalize(plan.Includes, target);
            var union = Canonicalize(merged.Includes, target);
            if (!UnionCoversClass(target, original, union)) return false;
        }
        return true;
    }

    /// <summary>
    /// For one class, every union constraint that this class can actually roll
    /// must already be covered by the class's original constraint set. A term
    /// that is impossible on the class contributes nothing.
    /// </summary>
    private bool UnionCoversClass(
        string itemClass,
        IReadOnlyList<StatConstraint> original,
        IReadOnlyList<StatConstraint> union)
    {
        foreach (var candidate in union)
        {
            var classSpans = ClassSpans(candidate, itemClass);
            if (classSpans.Count == 0) continue;

            var covered = original.Any(constraint =>
                constraint.FamilyKey == candidate.FamilyKey &&
                constraint.Patterns.SequenceEqual(candidate.Patterns, StringComparer.Ordinal));
            if (candidate.Intervals.Count == 0)
            {
                if (covered) continue;
                return false;
            }

            var clamped = Intersect(candidate.Intervals, classSpans);
            if (clamped.Count == 0) continue;
            var own = original.FirstOrDefault(constraint =>
                constraint.FamilyKey == candidate.FamilyKey &&
                constraint.Patterns.SequenceEqual(candidate.Patterns, StringComparer.Ordinal));
            if (own is null) return false;
            foreach (var interval in clamped)
            {
                if (!own.Intervals.Any(ownInterval => ownInterval.Contains(interval)))
                    return false;
            }
        }
        return true;
    }

    private IReadOnlyList<ValueInterval> ClassSpans(StatConstraint constraint, string itemClass)
    {
        var key = constraint.FamilyKey + "\u0001" + itemClass;
        if (_classSpansCache.TryGetValue(key, out var cached)) return cached;
        var family = constraint.MemberIds
            .Select(id => _corpus.FindMod(id))
            .FirstOrDefault(mod => mod is not null)?.Family ?? "";
        var intervals = new List<ValueInterval>();
        foreach (var pattern in constraint.Patterns)
        foreach (var mod in _corpus.ModsWithPattern(pattern))
        {
            if (!mod.Classes.Contains(itemClass, StringComparer.Ordinal)) continue;
            if (!string.IsNullOrEmpty(family) &&
                !string.Equals(mod.Family, family, StringComparison.Ordinal))
                continue;
            foreach (var line in mod.StatLines)
            {
                if (!string.Equals(line.Pattern, pattern, StringComparison.Ordinal)) continue;
                if (line.MaxValue < line.MinValue) continue;
                if (line.MinValue == 0 && line.MaxValue == 0) continue;
                intervals.Add(new ValueInterval(line.MinValue, line.MaxValue));
            }
        }
        var result = ConstraintCanonicalizer.MergeIntervals(intervals);
        _classSpansCache[key] = result;
        return result;
    }

    private static IReadOnlyList<ValueInterval> Intersect(
        IReadOnlyList<ValueInterval> left,
        IReadOnlyList<ValueInterval> right)
    {
        var result = new List<ValueInterval>();
        foreach (var a in left)
        foreach (var b in right)
        {
            var lo = Math.Max(a.Lo, b.Lo);
            var hi = Math.Min(a.Hi, b.Hi);
            if (hi >= lo - 1e-9) result.Add(new ValueInterval(lo, hi));
        }
        return ConstraintCanonicalizer.MergeIntervals(result);
    }

    /// <summary>One slot whose only include is a tier range.</summary>
    private sealed record RangeSlot(
        int Index,
        PasteSlotCriterion Slot,
        ModifierRequirement Requirement,
        string Key,
        string Family,
        IReadOnlyList<(double Lo, double Hi)> Intervals,
        IReadOnlyList<string> Patterns);

    /// <summary>
    /// Canonicalize a slot's includes before grouping. Multiple exact tiers of
    /// the same family become one range requirement, so the composer sees the
    /// same semantic unit the single-item compiler does.
    /// </summary>
    private PasteSlotCriterion NormalizeSlotRanges(PasteSlotCriterion slot)
    {
        if (slot.MatchAll || slot.Includes.Count <= 1) return slot;
        if (slot.Includes.Any(req =>
                req.IsRegex || _corpus.FindMod(req.Value) is null))
            return slot;
        var constraints = Canonicalize(slot.Includes, slot.ItemClass);
        if (constraints.Count == 0) return slot;
        var requirements = new List<ModifierRequirement>(constraints.Count);
        foreach (var constraint in constraints)
        {
            // A full-family pick stays one requirement: the family fragment
            // covers every tier and groups with the same family on other
            // classes without enumerating each tier.
            requirements.Add(constraint.IsFullFamily
                ? new ModifierRequirement(
                    constraint.AnchorModId,
                    RangeModIds: constraint.MemberIds,
                    FullFamily: true)
                : new ModifierRequirement(
                    constraint.AnchorModId,
                    MinValue: constraint.Intervals.Count == 0 ? null : constraint.Min,
                    MaxValue: constraint.Intervals.Count == 0 ? null : constraint.Max,
                    RangeModIds: constraint.MemberIds));
        }
        return new PasteSlotCriterion(slot.ItemClass, requirements, slot.Label);
    }

    /// <summary>
    /// Merge the same anchor tier across slots when the union is safe for
    /// every class. A Gloves Athlete's exact range is a subset of a
    /// Body/Helmet Athlete's-or-better range; Gloves has no Fecund tier, so
    /// the union 120–174 is equivalent there and the three slots become one
    /// range group instead of repeating Athlete's.
    /// </summary>
    private List<PasteSlotCriterion> NormalizeRangeSlots(List<PasteSlotCriterion> merged)
    {
        var candidates = new List<RangeSlot>();
        for (var i = 0; i < merged.Count; i++)
        {
            var slot = merged[i];
            if (slot.MatchAll || slot.Includes.Count != 1) continue;
            var req = slot.Includes[0];
            if (req.RangeModIds is not { Count: > 0 }) continue;
            var mod = _corpus.FindMod(req.Value);
            if (mod is null) continue;
            var intervals = ClampedIntervals(req);
            if (intervals.Count == 0) continue;
            var patterns = mod.StatLines
                .Select(line => line.Pattern)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(pattern => pattern, StringComparer.Ordinal)
                .ToList();
            var key = (mod.Family ?? "") + "\u0001" + (mod.Name ?? "") + "\u0001" +
                      string.Join("\u0001", patterns);
            candidates.Add(new RangeSlot(i, slot, req, key, mod.Family ?? "", intervals, patterns));
        }
        if (candidates.Count < 2) return merged;

        var result = merged.ToList();
        foreach (var group in candidates.GroupBy(x => x.Key, StringComparer.Ordinal))
        {
            var items = group.ToList();
            if (items.Count < 2) continue;
            var anchor = _corpus.FindMod(items[0].Requirement.Value);
            if (anchor is null ||
                !items.All(item => anchor.Classes.Contains(item.Slot.ItemClass, StringComparer.Ordinal)))
                continue;

            var union = MergeIntervals(items.SelectMany(item => item.Intervals));
            if (union.Count == 0) continue;
            if (items.Any(item => !UnionSafeForSlot(item, union))) continue;

            var unionIds = items
                .SelectMany(item => item.Requirement.RangeModIds!)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList();
            var fullFamily = items.All(item => item.Requirement.FullFamily);
            var unionReq = fullFamily
                ? new ModifierRequirement(
                    items[0].Requirement.Value,
                    RangeModIds: unionIds,
                    FullFamily: true)
                : new ModifierRequirement(
                    items[0].Requirement.Value,
                    MinValue: union.Min(x => x.Lo),
                    MaxValue: union.Max(x => x.Hi),
                    RangeModIds: unionIds);
            // A union that cannot compile globally is not a safe merge. A
            // full-family union uses the table fragment, so it skips the
            // expensive range proof entirely.
            var fragment = fullFamily
                ? _compaction.PlanMany([unionReq], null).FirstOrDefault()
                : _compaction.SelectFragment(unionReq, null);
            if (fragment is null) continue;

            foreach (var item in items)
            {
                result[item.Index] = new PasteSlotCriterion(
                    item.Slot.ItemClass, [unionReq], item.Slot.Label);
            }
        }
        return result;
    }

    private IReadOnlyList<(double Lo, double Hi)> ClampedIntervals(ModifierRequirement req)
    {
        var intervals = new List<(double Lo, double Hi)>();
        foreach (var id in req.RangeModIds ?? [])
        {
            var mod = _corpus.FindMod(id);
            if (mod is null) continue;
            foreach (var line in mod.StatLines)
            {
                var lo = req.MinValue is { } min
                    ? Math.Max(line.MinValue, min)
                    : line.MinValue;
                var hi = req.MaxValue is { } max
                    ? Math.Min(line.MaxValue, max)
                    : line.MaxValue;
                if (hi >= lo && (lo != 0 || hi != 0))
                    intervals.Add((lo, hi));
            }
        }
        return MergeIntervals(intervals);
    }

    private static List<(double Lo, double Hi)> MergeIntervals(
        IEnumerable<(double Lo, double Hi)> intervals)
    {
        var ordered = intervals
            .Where(x => x.Hi >= x.Lo)
            .OrderBy(x => x.Lo)
            .ThenBy(x => x.Hi)
            .ToList();
        var result = new List<(double Lo, double Hi)>(ordered.Count);
        foreach (var current in ordered)
        {
            if (result.Count == 0)
            {
                result.Add(current);
                continue;
            }
            var last = result[^1];
            if (current.Lo <= last.Hi + 1e-9)
                result[^1] = (last.Lo, Math.Max(last.Hi, current.Hi));
            else
                result.Add(current);
        }
        return result;
    }

    /// <summary>
    /// True when the union adds no values the slot's class can actually
    /// roll; a narrowed slot keeps its own group otherwise.
    /// </summary>
    private bool UnionSafeForSlot(
        RangeSlot item, IReadOnlyList<(double Lo, double Hi)> union)
    {
        // The common case is a full-family pick: the union adds no span the
        // slot did not already allow, so no corpus scan is needed.
        if (union.All(span => ContainedIn(item.Intervals, span.Lo, span.Hi)))
            return true;

        foreach (var pattern in item.Patterns)
        {
            foreach (var mod in _corpus.ModsWithPattern(pattern))
            {
                if (!mod.Classes.Contains(item.Slot.ItemClass, StringComparer.Ordinal))
                    continue;
                if (!string.IsNullOrEmpty(item.Family) &&
                    !string.Equals(mod.Family, item.Family, StringComparison.Ordinal))
                    continue;
                foreach (var line in mod.StatLines)
                {
                    if (line.MaxValue < line.MinValue) continue;
                    if (line.MinValue == 0 && line.MaxValue == 0) continue;
                    if (!Overlaps(union, line.MinValue, line.MaxValue)) continue;
                    if (!ContainedIn(item.Intervals, line.MinValue, line.MaxValue))
                        return false;
                }
            }
        }
        return true;
    }

    private static bool Overlaps(
        IReadOnlyList<(double Lo, double Hi)> intervals, double lo, double hi) =>
        intervals.Any(x => lo <= x.Hi + 1e-9 && x.Lo <= hi + 1e-9);

    private static bool ContainedIn(
        IReadOnlyList<(double Lo, double Hi)> intervals, double lo, double hi) =>
        intervals.Any(x => lo >= x.Lo - 1e-9 && hi <= x.Hi + 1e-9);

    private string CriterionKey(PasteSlotCriterion s)
    {
        static string Join(IEnumerable<string> keys) => string.Join("\n",
            keys.Distinct(StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal));

        if (s.MatchAll)
        {
            var groups = s.RequiredGroups!
                .Select(g => Join(g.Select(RequirementKey)))
                .OrderBy(g => g, StringComparer.Ordinal);
            return "all\u0002" + string.Join("\u0002", groups);
        }
        return Join(s.Includes.Select(RequirementKey));
    }

    /// <summary>
    /// Semantic requirement identity: catalog mods are the same criterion
    /// when they share their distinct stat-line patterns and minimum. Family
    /// picks list different variant ids per class (Helmet has 10 tiers, Body
    /// Armour 13) even though every variant compiles to the same stat text.
    /// Raw regex and free-text terms keep their literal identity.
    /// </summary>
    private string RequirementKey(ModifierRequirement r)
    {
        var min = r.MinValue?.ToString("R", System.Globalization.CultureInfo.InvariantCulture) ?? "";
        var max = r.MaxValue?.ToString("R", System.Globalization.CultureInfo.InvariantCulture) ?? "";
        if (r.IsRegex)
            return "regex\u0001" + min + "\u0001" + max + "\u0001" + TextKey(r.Value);
        if (r.FullFamily)
        {
            var fullPatterns = (r.RangeModIds ?? [])
                .Select(_corpus.FindMod)
                .Where(mod => mod is not null)
                .SelectMany(mod => mod!.StatLines)
                .Select(line => line.Pattern)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(pattern => pattern, StringComparer.Ordinal)
                .Select(TextKey);
            return "full\u0001" + min + "\u0001" + max + "\u0001" +
                   string.Join("\u0001", fullPatterns);
        }
        if (r.RangeModIds is { Count: > 0 } rangeIds)
            return RangeRequirementKey(r, rangeIds, min, max);
        var mod = _corpus.FindMod(r.Value);
        if (mod is null)
            return "text\u0001" + min + "\u0001" + max + "\u0001" + TextKey(r.Value);
        var patterns = mod.StatLines
            .Select(line => line.Pattern)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(pattern => pattern, StringComparer.Ordinal)
            .Select(TextKey);
        return "mod\u0001" + min + "\u0001" + max + "\u0001" + string.Join("\u0001", patterns);
    }

    /// <summary>
    /// A tier range is the same criterion across classes when its stat
    /// pattern and covered value intervals match, even though each class uses
    /// different mod ids. The interval list also distinguishes a gapped tier
    /// set from a continuous one with the same min/max.
    /// </summary>
    private string RangeRequirementKey(
        ModifierRequirement r, IReadOnlyList<string> rangeIds, string min, string max)
    {
        var patterns = new HashSet<string>(StringComparer.Ordinal);
        var intervals = new List<(double Lo, double Hi)>();
        foreach (var id in rangeIds)
        {
            var mod = _corpus.FindMod(id);
            if (mod is null) continue;
            foreach (var line in mod.StatLines)
            {
                patterns.Add(line.Pattern);
                var lo = r.MinValue is { } minValue
                    ? Math.Max(line.MinValue, minValue)
                    : line.MinValue;
                var hi = r.MaxValue is { } maxValue
                    ? Math.Min(line.MaxValue, maxValue)
                    : line.MaxValue;
                if (hi >= lo) intervals.Add((lo, hi));
            }
        }
        if (patterns.Count == 0)
            return "range\u0001" + min + "\u0001" + max + "\u0001" + TextKey(r.Value);
        var intervalKey = string.Join(",", intervals
            .Distinct()
            .OrderBy(x => x.Lo)
            .ThenBy(x => x.Hi)
            .Select(x =>
                x.Lo.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "-" +
                x.Hi.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));
        return "range\u0001" + min + "\u0001" + max + "\u0001" +
               string.Join("\u0001", patterns.OrderBy(p => p, StringComparer.Ordinal)) +
               "\u0001" + intervalKey;
    }

    /// <summary>Length-prefixed text so a delimiter inside a value cannot
    /// collide with the key separators.</summary>
    private static string TextKey(string value) =>
        value.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + value;

    private string? FragmentFor(ModifierRequirement req, string? itemClass, List<CompilerDiagnostic> diagnostics)
    {
        if (req.IsRegex) return req.Value;
        var planned = _compaction.PlanMany([req], itemClass).FirstOrDefault();
        if (planned is not null) return planned;
        if (_corpus.FindMod(req.Value) is not null) return null;
        var diag = _corpus.Syntax.ValidateFragment(req.Value);
        if (diag is not null) diagnostics.Add(diag);
        return _corpus.Syntax.EscapeLiteral(req.Value);
    }

    private void ValidateAnchor(string anchorInner, IReadOnlyList<string> scope, List<CompilerDiagnostic> diagnostics)
    {
        // The common single-class anchor is a literal ("Boo"). A substring
        // check is much cheaper than running a regex against every class and
        // avoids a cold 118-match regex pass on the first paste.
        var literal = !anchorInner.Any(c => "\\^$.[]{}()*+?|".Contains(c, StringComparison.Ordinal));
        var regex = literal
            ? null
            : new Regex(anchorInner, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        foreach (var cls in _corpus.Catalog.ItemClasses)
        {
            var line = $"Item Class: {cls.Name}";
            var hit = literal
                ? line.Contains(anchorInner, StringComparison.OrdinalIgnoreCase)
                : regex!.IsMatch(line);
            var wanted = scope.Contains(cls.Id, StringComparer.Ordinal);
            if (wanted && !hit)
            {
                diagnostics.Add(new CompilerDiagnostic(DiagnosticLevel.Warning, "paste-anchor",
                    $"Anchor does not resolve class '{cls.Id}'."));
            }
            else if (!wanted && hit)
            {
                diagnostics.Add(new CompilerDiagnostic(DiagnosticLevel.Warning, "paste-anchor",
                    $"Anchor over-matches class '{cls.Id}'."));
            }
        }
    }

    private void ValidateGroupRecall(
        string token, IReadOnlyList<string> targets, IReadOnlyList<string> sources,
        string label, List<CompilerDiagnostic> diagnostics)
    {
        // Quote the group: spaces inside fragments must not split tokens.
        var quoted = _corpus.Syntax.Quote(token);
        foreach (var target in targets)
        {
            var candidates = _explainer.Explain(quoted, target).Mods.Select(m => m.Id)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var source in sources)
            {
                if (!candidates.Contains(source))
                {
                    diagnostics.Add(new CompilerDiagnostic(DiagnosticLevel.Warning, "paste-recall",
                        $"Group '{label}' does not recall mod '{source}' on '{target}'."));
                }
            }
        }
    }
}

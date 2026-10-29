using System.Text.RegularExpressions;
using PoeRegex.Core.Data;
using PoeRegex.Core.Explain;

namespace PoeRegex.Core.Compilation;

/// <summary>One slot's contribution to a combined paste: base class + wanted mods.</summary>
public sealed record PasteSlotCriterion(string ItemClass, IReadOnlyList<ModifierRequirement> Includes, string Label);

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

    public PasteComposer(EngineCorpus corpus, bool validateRecall = true)
    {
        _corpus = corpus;
        _compaction = new CompactionEngine(corpus);
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
        bool allowOverflow = false)
    {
        var diagnostics = new List<CompilerDiagnostic>();

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
            var includes = ordered.SelectMany(s => s.Includes).Distinct().ToList();
            var label = string.Join(", ", ordered.Select(s => s.Label).Distinct(StringComparer.Ordinal));
            merged.Add(new PasteSlotCriterion(group.Key, includes, label));
        }
        merged.Sort((a, b) => string.CompareOrdinal(a.ItemClass, b.ItemClass));

        var scope = merged.Select(s => s.ItemClass).Distinct(StringComparer.Ordinal).ToList();

        var anchorInner = ": (" + ClassAlternation.Build(_corpus, scope) + ")";
        var anchorDiag = Poe2Syntax.ValidateFragment(anchorInner);
        if (anchorDiag is not null) diagnostics.Add(anchorDiag);
        ValidateAnchor(anchorInner, scope, diagnostics);

        var groups = new List<PasteGroup>();
        var blocks = new List<string> { Poe2Syntax.Quote(anchorInner) };
        // Group identical criteria (different classes, same mods) into one line.
        foreach (var bucket in merged.Where(s => s.Includes.Count > 0)
                     .GroupBy(s => string.Join("\n", s.Includes.Select(i => i.Value + "\u0001" + i.MinValue)),
                         StringComparer.Ordinal))
        {
            var targets = bucket.Select(s => s.ItemClass).OrderBy(x => x, StringComparer.Ordinal).ToList();
            var label = string.Join(", ", bucket.Select(s => s.Label).OrderBy(x => x, StringComparer.Ordinal));
            var frags = new List<string>();
            var sources = new List<string>();
            foreach (var req in bucket.SelectMany(s => s.Includes).Distinct().ToList())
            {
                var frag = FragmentFor(req, targets[0], diagnostics);
                if (frag is null)
                {
                    diagnostics.Add(new CompilerDiagnostic(DiagnosticLevel.Warning, "unresolved-modifier",
                        $"Modifier '{req.Value}' could not be resolved; skipped."));
                    continue;
                }
                frags.Add(frag);
                if (_corpus.FindMod(req.Value) is not null) sources.Add(req.Value);
            }
            if (frags.Count == 0) continue;
            var others = scope.Where(c => !targets.Contains(c, StringComparer.Ordinal)).ToList();
            var factored = CompactionEngine.FactorCommonPrefixes(frags);
            var token = others.Count > 0
                ? ": (" + ClassAlternation.Build(_corpus, others) + ")|" + string.Join("|", factored)
                : string.Join("|", factored);
            var tokenDiag = Poe2Syntax.ValidateFragment(token);
            if (tokenDiag is not null) diagnostics.Add(tokenDiag);
            groups.Add(new PasteGroup(label, targets, token));
            blocks.Add(Poe2Syntax.Quote(token));
            if (_validateRecall)
                ValidateGroupRecall(token, targets, sources.Distinct(StringComparer.Ordinal).ToList(), label, diagnostics);
        }

        // Exclusions union globally: block negation cannot be scoped per slot.
        foreach (var req in excluded.Distinct().ToList())
        {
            var mod = _corpus.FindMod(req.Value);
            var term = mod is not null
                ? _compaction.SelectFragment(req, null) ?? req.Value
                : Poe2Syntax.EscapeLiteral(req.Value);
            blocks.Add(Poe2Syntax.FormatExclusion(term));
        }

        var query = string.Join(" ", blocks);
        foreach (var shapeError in QueryShape.Validate(query))
            diagnostics.Add(new CompilerDiagnostic(
                DiagnosticLevel.Warning, "query-shape", shapeError));
        var limit = Poe2Syntax.MaxCharacterLimit;
        if (query.Length > limit && !allowOverflow)
            throw new RegexLengthOverflowException(query.Length, limit);
        return new MultiPasteResult
        {
            Query = query,
            Length = query.Length,
            Limit = limit,
            Anchor = anchorInner,
            Groups = groups,
            Diagnostics = diagnostics,
        };
    }

    private string? FragmentFor(ModifierRequirement req, string itemClass, List<CompilerDiagnostic> diagnostics)
    {
        var mod = _corpus.FindMod(req.Value);
        if (mod is not null) return _compaction.SelectFragment(req, itemClass);
        var diag = Poe2Syntax.ValidateFragment(req.Value);
        if (diag is not null) diagnostics.Add(diag);
        return Poe2Syntax.EscapeLiteral(req.Value);
    }

    private void ValidateAnchor(string anchorInner, IReadOnlyList<string> scope, List<CompilerDiagnostic> diagnostics)
    {
        foreach (var cls in _corpus.Catalog.ItemClasses)
        {
            var line = $"Item Class: {cls.Name}";
            var hit = Regex.IsMatch(line, anchorInner, RegexOptions.IgnoreCase);
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
        var quoted = Poe2Syntax.Quote(token);
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

using PoeRegex.Core.Data;

namespace PoeRegex.Core.Compilation;

/// <summary>
/// Facade over corpus + strategy + compaction that produces the final query.
/// Deterministic output; enforces the game's character limit.
/// </summary>
public sealed class PoeRegexCompiler
{
    private readonly EngineCorpus _corpus;
    private readonly CompactionEngine _compaction;

    public PoeRegexCompiler(EngineCorpus corpus)
        : this(corpus, new CompactionEngine(corpus))
    {
    }

    public PoeRegexCompiler(EngineCorpus corpus, CompactionEngine compaction)
    {
        _corpus = corpus;
        _compaction = compaction;
    }

    public CompileResult Compile(QuerySpecification spec)
    {
        var diagnostics = new List<CompilerDiagnostic>();
        var blocks = new List<string>();

        // Class anchor first: single-slot Advanced queries read with the
        // class constraint up front. The anchor is an oracle-clean
        // `Item Class:` name prefix, kept as its own quoted block.
        // PasteComposer already emits its anchor first; this keeps the two
        // composition paths visually consistent.
        if (spec.ItemClass is not null)
        {
            if (_corpus.FindItemClass(spec.ItemClass) is null)
                diagnostics.Add(new CompilerDiagnostic(DiagnosticLevel.Warning, "unresolved-class",
                    $"No fingerprint available for item class '{spec.ItemClass}'."));
            else blocks.Add(_corpus.Syntax.Quote(": " + _corpus.ClassNameAnchor(spec.ItemClass)));
        }

        // Included modifiers → one quoted block with | alternation.
        var (included, includedRaw) = PlanRequirements(
            spec.IncludedModifiers, spec.ItemClass, diagnostics);
        if (included.Count > 0 || includedRaw.Count > 0)
        {
            // Factor/dedupe catalog fragments, but keep imported custom text
            // verbatim instead of factoring it with mod fragments.
            var terms = CompactionEngine.FactorCommonPrefixes(included).ToList();
            terms.AddRange(includedRaw);
            blocks.Add(_corpus.Syntax.Quote(string.Join("|", terms)));
        }

        foreach (var group in spec.RequiredGroups)
        {
            var (fragments, rawFragments) = PlanRequirements(
                group, spec.ItemClass, diagnostics);
            if (fragments.Count > 0 || rawFragments.Count > 0)
            {
                var terms = CompactionEngine.FactorCommonPrefixes(fragments).ToList();
                terms.AddRange(rawFragments);
                blocks.Add(_corpus.Syntax.Quote(string.Join("|", terms)));
            }
        }

        // Exclusions → separate quoted blocks. Identical fragments are one
        // exclusion; different mod ids can resolve to the same search term.
        var excludedTerms = new HashSet<string>(StringComparer.Ordinal);
        foreach (var req in spec.ExcludedModifiers)
        {
            // A raw exclusion is already a regex fragment (guide imports);
            // catalog exclusions still resolve to their shortest fragment.
            var mod = req.IsRegex ? null : _corpus.FindMod(req.Value);
            var term = req.IsRegex
                ? req.Value
                : mod is not null
                    ? SelectFragment(req, spec.ItemClass, diagnostics) ?? req.Value
                    : _corpus.Syntax.EscapeLiteral(req.Value);
            if (!excludedTerms.Add(term)) continue;
            if (included.Contains(term))
            {
                diagnostics.Add(new CompilerDiagnostic(
                    DiagnosticLevel.Warning, "contradictory-filter",
                    $"Included and excluded fragments are identical: \"{term}\"."));
            }
            blocks.Add(_corpus.Syntax.FormatExclusion(term));
        }

        // Custom keywords.
        foreach (var kw in spec.Keywords)
        {
            var diag = _corpus.Syntax.ValidateFragment(kw);
            if (diag is not null) diagnostics.Add(diag);
            blocks.Add(_corpus.Syntax.Quote(kw));
        }

        var query = string.Join(" ", blocks);
        foreach (var shapeError in QueryShape.Validate(query))
            diagnostics.Add(new CompilerDiagnostic(
                DiagnosticLevel.Warning, "query-shape", shapeError));
        var limit = QuerySyntax.MaxCharacterLimit;
        if (query.Length > limit && !spec.AllowOverflow)
        {
            throw new RegexLengthOverflowException(query.Length, limit);
        }
        return new CompileResult
        {
            Query = query,
            Length = query.Length,
            Limit = limit,
            Diagnostics = diagnostics,
        };
    }

    /// <summary>
    /// Split catalog requirements from raw/free text, diagnose unresolved
    /// mods, then plan all catalog requirements as canonical constraints.
    /// </summary>
    private (List<string> Fragments, List<string> Raw) PlanRequirements(
        IReadOnlyList<ModifierRequirement> requirements,
        string? itemClass,
        List<CompilerDiagnostic> diagnostics)
    {
        var catalog = new List<ModifierRequirement>();
        var literals = new List<string>();
        var raw = new List<string>();
        foreach (var req in requirements)
        {
            if (req.IsRegex)
            {
                raw.Add(req.Value);
                continue;
            }
            if (_corpus.FindMod(req.Value) is null)
            {
                var diag = _corpus.Syntax.ValidateFragment(req.Value);
                if (diag is not null) diagnostics.Add(diag);
                // Free-text affix names are literal fragments and may be
                // factored with other literals; imported regex stays raw.
                literals.Add(_corpus.Syntax.EscapeLiteral(req.Value));
                continue;
            }
            if (_corpus.FindMod(req.Value) is { StatLines.Count: 0 })
            {
                diagnostics.Add(new CompilerDiagnostic(DiagnosticLevel.Warning, "unresolved-modifier",
                    $"Modifier '{req.Value}' could not be resolved; skipped."));
                continue;
            }
            catalog.Add(req);
        }
        var planned = _compaction.PlanMany(catalog, itemClass);
        if (planned.Count == 0 && catalog.Count > 0)
        {
            diagnostics.Add(new CompilerDiagnostic(DiagnosticLevel.Warning, "unresolved-modifier",
                $"Modifier '{catalog[0].Value}' could not be resolved; skipped."));
        }
        var fragments = planned.ToList();
        fragments.AddRange(literals);
        return (fragments, raw);
    }

    private string? SelectFragment(ModifierRequirement req, string? itemClass, List<CompilerDiagnostic> diagnostics)
    {
        // Imported custom text is already a regex fragment; emit it verbatim.
        if (req.IsRegex) return req.Value;
        var mod = _corpus.FindMod(req.Value);
        if (mod is not null)
        {
            var fp = _compaction.SelectFragment(req, itemClass);
            return fp;
        }

        // Free text: validate then escape for literal matching.
        var diag = _corpus.Syntax.ValidateFragment(req.Value);
        if (diag is not null) diagnostics.Add(diag);
        return _corpus.Syntax.EscapeLiteral(req.Value);
    }
}

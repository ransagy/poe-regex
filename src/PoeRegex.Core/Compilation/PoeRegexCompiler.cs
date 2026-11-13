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
    {
        _corpus = corpus;
        _compaction = new CompactionEngine(corpus);
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
        var included = new List<string>();
        var includedRaw = new List<string>();
        foreach (var req in spec.IncludedModifiers)
        {
            var frag = SelectFragment(req, spec.ItemClass, diagnostics);
            if (frag is null)
            {
                diagnostics.Add(new CompilerDiagnostic(DiagnosticLevel.Warning, "unresolved-modifier",
                    $"Modifier '{req.Value}' could not be resolved; skipped."));
                continue;
            }
            (req.IsRegex ? includedRaw : included).Add(frag);
        }
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
            var fragments = new List<string>();
            var rawFragments = new List<string>();
            foreach (var req in group)
            {
                var frag = SelectFragment(req, spec.ItemClass, diagnostics);
                if (frag is null)
                {
                    diagnostics.Add(new CompilerDiagnostic(DiagnosticLevel.Warning, "unresolved-modifier",
                        $"Modifier '{req.Value}' could not be resolved; skipped."));
                    continue;
                }
                (req.IsRegex ? rawFragments : fragments).Add(frag);
            }
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

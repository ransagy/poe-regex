using PoeRegex.Core.Data;

namespace PoeRegex.Core.Compilation;

/// <summary>
/// Facade over corpus + strategy + compaction that produces the final query.
/// Deterministic output; enforces the game's character limit.
/// </summary>
public sealed class PoeRegexCompiler
{
    private readonly EngineCorpus _corpus;
    private readonly ICompilerStrategy _strategy;
    private readonly CompactionEngine _compaction;

    public PoeRegexCompiler(EngineCorpus corpus, ICompilerStrategy strategy, bool allowFallback = true)
    {
        _corpus = corpus;
        _strategy = strategy;
        _compaction = new CompactionEngine(corpus, strategy, allowFallback);
    }

    public static ICompilerStrategy StrategyFor(GameTarget target) =>
        target == GameTarget.PoE1 ? new PathOfExileOneStrategy() : new PathOfExileTwoStrategy();

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
            else blocks.Add(_strategy.Quote(": " + _strategy.EscapeLiteral(_corpus.ClassNamePrefix(spec.ItemClass))));
        }

        // Included modifiers → one quoted block with | alternation.
        var included = new List<string>();
        foreach (var req in spec.IncludedModifiers)
        {
            var frag = SelectFragment(req, spec.ItemClass, diagnostics);
            if (frag is not null) included.Add(frag);
            else diagnostics.Add(new CompilerDiagnostic(DiagnosticLevel.Warning, "unresolved-modifier",
                $"Modifier '{req.Value}' could not be resolved; skipped."));
        }
        if (included.Count > 0)
        {
            var factored = CompactionEngine.FactorCommonPrefixes(included);
            blocks.Add(_strategy.Quote(string.Join("|", factored)));
        }

        // Exclusions → separate quoted blocks.
        foreach (var req in spec.ExcludedModifiers)
        {
            var mod = _corpus.FindMod(req.Value);
            var term = mod is not null
                ? SelectFragment(req, spec.ItemClass, diagnostics) ?? req.Value
                : _strategy.EscapeLiteral(req.Value);
            blocks.Add(_strategy.FormatExclusion(term));
        }

        // Custom keywords.
        foreach (var kw in spec.Keywords)
        {
            var diag = _strategy.ValidateFragment(kw);
            if (diag is not null) diagnostics.Add(diag);
            blocks.Add(_strategy.Quote(kw));
        }

        var query = string.Join(" ", blocks);
        foreach (var shapeError in QueryShape.Validate(query))
            diagnostics.Add(new CompilerDiagnostic(
                DiagnosticLevel.Warning, "query-shape", shapeError));
        var limit = _strategy.GameRules.MaxCharacterLimit;
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
        var mod = _corpus.FindMod(req.Value);
        if (mod is not null)
        {
            var fp = _compaction.SelectFragment(req, itemClass);
            return fp;
        }

        // Free text: validate then escape for literal matching.
        var diag = _strategy.ValidateFragment(req.Value);
        if (diag is not null) diagnostics.Add(diag);
        return _strategy.EscapeLiteral(req.Value);
    }
}

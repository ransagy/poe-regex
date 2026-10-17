namespace PoeRegex.Web.Core;

/// <summary>
/// Shared advanced-description import pipeline: class resolution, per-line
/// catalog mapping, rule verdicts and <see cref="TestItem"/> assembly. Used by
/// the import dialog's single and batch paths; pure (no UI, no state), so it
/// is unit-testable without a browser.
/// </summary>
public static class ItemImport
{
    /// <summary>Class resolution plus per-(block,line) catalog mappings.</summary>
    public sealed record Mapped(
        string? ClassId,
        Dictionary<(int Block, int Line), EngineService.ModMapping> Lines);

    /// <summary>One item ready to import, with its verdicts.</summary>
    public sealed record Result(TestItem? Item, IReadOnlyList<RuleViolation> Violations, string? Error);

    /// <summary>Resolve the class and map every value line to catalog mod ids.</summary>
    public static Mapped Map(EngineService engine, ParsedItem parsed)
    {
        var classId = engine.ResolveClassId(parsed.ItemClass);
        var lines = new Dictionary<(int, int), EngineService.ModMapping>();
        foreach (var (block, bi) in parsed.Mods.Select((b, i) => (b, i)))
            foreach (var (line, li) in block.Lines.Select((l, i) => (l, i)))
                lines[(bi, li)] = engine.MapLineToMods(classId, line.CanonicalText, line.Rolls);
        return new Mapped(classId, lines);
    }

    /// <summary>
    /// Default picks per mapped line, mirroring what a user would pick in the
    /// candidate dropdown: the candidate bearing the block header's affix name
    /// wins (the line text alone is ambiguous across mods, e.g. `+37 to
    /// Accuracy Rating` is both `Reliable` and `of Light`), then generation
    /// kind agreement, then list order.
    /// </summary>
    public static Dictionary<(int, int), string> DefaultPicks(
        EngineService engine, ParsedItem parsed, Mapped mapped)
    {
        var picks = new Dictionary<(int, int), string>();
        foreach (var (block, bi) in parsed.Mods.Select((b, i) => (b, i)))
            foreach (var (_, li) in block.Lines.Select((l, i) => (l, i)))
            {
                if (!mapped.Lines.TryGetValue((bi, li), out var m) || m.ModIds.Count == 0) continue;
                picks[(bi, li)] = ChooseDefault(engine, block, m.ModIds);
            }
        return picks;
    }

    private static string ChooseDefault(
        EngineService engine, ParsedModBlock block, IReadOnlyList<string> candidates)
    {
        if (block.Name is { Length: > 0 } name)
        {
            var byName = candidates.FirstOrDefault(id =>
                string.Equals(engine.FindMod(id)?.Name, name, StringComparison.OrdinalIgnoreCase));
            if (byName is not null) return byName;
        }
        var byKind = candidates.FirstOrDefault(id =>
            string.Equals(engine.FindMod(id)?.GenerationType, block.Kind, StringComparison.OrdinalIgnoreCase));
        return byKind ?? candidates[0];
    }

    /// <summary>Gate message when the class cannot be imported; null when importable.</summary>
    public static string? Gate(EngineService engine, string? classId)
    {
        if (classId is null) return "no recognizable item class: import blocked";
        return engine.InGameClasses().Any(c => c.Id == classId)
            ? null
            : $"class '{classId}' has no catalog mods: import blocked";
    }

    /// <summary>Rule verdicts plus unmapped-block warnings for the current picks.</summary>
    public static IReadOnlyList<RuleViolation> Validate(
        EngineService engine, ParsedItem parsed, Mapped mapped,
        IReadOnlyDictionary<(int, int), string> picks)
    {
        var ruleLines = new List<RuleLine>();
        foreach (var (block, bi) in parsed.Mods.Select((b, i) => (b, i)))
        {
            // One entry per block (multi-line mods count once).
            string? sel = null;
            for (var li = 0; li < block.Lines.Count && sel is null; li++)
                if (mapped.Lines.TryGetValue((bi, li), out var m) && m.ModIds.Count > 0)
                    sel = picks.GetValueOrDefault((bi, li), m.ModIds[0]);
            ruleLines.Add(new RuleLine(block.Kind, sel));
        }
        var violations = ItemRules.Validate(engine, ruleLines, parsed.Rarity).ToList();
        foreach (var (block, bi) in parsed.Mods.Select((b, i) => (b, i)))
        {
            if (block.Kind == "implicit") continue;
            var mappedAny = Enumerable.Range(0, block.Lines.Count).Any(li =>
                mapped.Lines.TryGetValue((bi, li), out var m) && m.ModIds.Count > 0);
            if (!mappedAny)
                violations.Add(new RuleViolation("warning",
                    $"'{block.Name ?? block.Kind}' did not map to the catalog: counts may be off"));
        }
        return violations;
    }

    /// <summary>Assemble the test item from the parsed text, mappings and picks.</summary>
    public static TestItem Materialize(
        ParsedItem parsed, Mapped mapped, IReadOnlyDictionary<(int, int), string> picks,
        string fallbackName = "imported item")
    {
        var item = new TestItem
        {
            Name = parsed.Name ?? fallbackName,
            ClassId = mapped.ClassId,
            Parsed = parsed,
        };
        foreach (var (key, map) in mapped.Lines)
        {
            var (bi, li) = key;
            var line = parsed.Mods[bi].Lines[li];
            item.Mapped.Add(new MappedLine(
                line.Text, line.CanonicalText, line.Rolls, map.ModIds,
                map.ModIds.Count == 0 ? null : picks.GetValueOrDefault(key, map.ModIds[0])));
        }
        return item;
    }

    /// <summary>One-shot import with default picks — batch rows and quick loads.</summary>
    public static Result Import(EngineService engine, ParsedItem parsed, string fallbackName = "imported item")
    {
        var mapped = Map(engine, parsed);
        var gate = Gate(engine, mapped.ClassId);
        if (gate is not null) return new Result(null, [], gate);
        var picks = DefaultPicks(engine, parsed, mapped);
        var violations = Validate(engine, parsed, mapped, picks);
        var error = violations.FirstOrDefault(v => v.Level == "error");
        if (error is not null) return new Result(null, violations, error.Message);
        return new Result(Materialize(parsed, mapped, picks, fallbackName), violations, null);
    }
}

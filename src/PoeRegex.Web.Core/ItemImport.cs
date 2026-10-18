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

    /// <summary>
    /// Resolve the class and map every value line to catalog mod ids. The
    /// paste's bracketed ranges are evidence in the engine; a still-ambiguous
    /// line is narrowed by the block header's affix name, and whatever is
    /// left gets its default pick moved to the front — `ModIds[0]` is the
    /// resolved affix the annotations and the assembled item use.
    /// </summary>
    public static Mapped Map(EngineService engine, ParsedItem parsed)
    {
        var classId = engine.ResolveClassId(parsed.ItemClass);
        var lines = new Dictionary<(int, int), EngineService.ModMapping>();
        foreach (var (block, bi) in parsed.Mods.Select((b, i) => (b, i)))
            foreach (var (line, li) in block.Lines.Select((l, i) => (l, i)))
                lines[(bi, li)] = Resolve(engine, block, line, classId);
        return new Mapped(classId, lines);
    }

    private static EngineService.ModMapping Resolve(
        EngineService engine, ParsedModBlock block, ParsedValueLine line, string? classId)
    {
        var mapping = engine.MapLineToMods(classId, line.CanonicalText, line.Rolls, line.Ranges);
        if (mapping.ModIds.Count > 1 && block.Name is { Length: > 0 } name)
        {
            var byName = mapping.ModIds
                .Where(id => string.Equals(engine.FindMod(id)?.Name, name, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (byName.Count > 0) mapping = mapping with { ModIds = byName };
        }
        if (mapping.ModIds.Count > 1)
        {
            // The line text alone is ambiguous across mods (e.g. `+37 to
            // Accuracy Rating` is both `Reliable` and `of Light`); generation
            // kind agreement then list order decide. First so `ModIds[0]` is
            // the resolved affix everywhere.
            var chosen = ChooseDefault(engine, block, mapping.ModIds);
            mapping = mapping with { ModIds = [chosen, .. mapping.ModIds.Where(id => id != chosen)] };
        }
        return mapping;
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

    /// <summary>
    /// Rule verdicts plus mapping warnings for the resolved affixes. A block
    /// whose header names an affix the resolved pick does not carry warns
    /// instead of failing: the paste's ranges and text outrank the header.
    /// </summary>
    public static IReadOnlyList<RuleViolation> Validate(
        EngineService engine, ParsedItem parsed, Mapped mapped)
    {
        var ruleLines = new List<RuleLine>();
        var selected = new List<string?>();
        foreach (var (block, bi) in parsed.Mods.Select((b, i) => (b, i)))
        {
            // One entry per block (multi-line mods count once); ModIds[0] is
            // the resolved affix (Map puts the default pick first).
            string? sel = null;
            for (var li = 0; li < block.Lines.Count && sel is null; li++)
                if (mapped.Lines.TryGetValue((bi, li), out var m) && m.ModIds.Count > 0)
                    sel = m.ModIds[0];
            selected.Add(sel);
            ruleLines.Add(new RuleLine(block.Kind, sel));
        }
        var violations = ItemRules.Validate(engine, ruleLines, parsed.Rarity).ToList();
        foreach (var ((block, bi), sel) in parsed.Mods.Select((b, i) => (b, i)).Zip(selected))
        {
            if (block.Kind == "implicit") continue;
            var mappedAny = Enumerable.Range(0, block.Lines.Count).Any(li =>
                mapped.Lines.TryGetValue((bi, li), out var m) && m.ModIds.Count > 0);
            if (!mappedAny)
            {
                violations.Add(new RuleViolation("warning",
                    $"'{block.Name ?? block.Kind}' did not map to the catalog: counts may be off"));
                continue;
            }
            if (sel is not null && block.Name is { Length: > 0 } headerName)
            {
                var mod = engine.FindMod(sel);
                if (mod is not null &&
                    !string.Equals(mod.Name, headerName, StringComparison.OrdinalIgnoreCase))
                    violations.Add(new RuleViolation("warning",
                        $"line resolves to '{mod.Name ?? mod.Id}', but the header says '{headerName}'"));
            }
        }
        return violations;
    }

    /// <summary>
    /// Count of prefix/suffix lines that map to no catalog id. Implicit blocks
    /// are skipped: they never participate in rule counting, so an unmapped
    /// implicit is expected data (the catalog holds affixes only), not a
    /// defect worth surfacing.
    /// </summary>
    public static int UnmappedAffixCount(ParsedItem parsed, Mapped mapped)
    {
        var count = 0;
        foreach (var (block, bi) in parsed.Mods.Select((b, i) => (b, i)))
        {
            if (block.Kind == "implicit") continue;
            for (var li = 0; li < block.Lines.Count; li++)
                if (!mapped.Lines.TryGetValue((bi, li), out var m) || m.ModIds.Count == 0) count++;
        }
        return count;
    }

    /// <summary>Assemble the test item from the parsed text and mappings.</summary>
    public static TestItem Materialize(
        ParsedItem parsed, Mapped mapped, string fallbackName = "imported item")
    {
        var item = new TestItem
        {
            Name = parsed.Name ?? fallbackName,
            ClassId = mapped.ClassId,
            Parsed = parsed,
        };
        // Append in parsed order (dictionary order is not contractual) so the
        // preview card's running index lines up with the flattened mod lines.
        foreach (var (block, bi) in parsed.Mods.Select((b, i) => (b, i)))
            foreach (var (line, li) in block.Lines.Select((l, i) => (l, i)))
            {
                if (!mapped.Lines.TryGetValue((bi, li), out var map)) continue;
                item.Mapped.Add(new MappedLine(
                    line.Text, line.CanonicalText, line.Rolls, map.ModIds,
                    map.ModIds.Count == 0 ? null : map.ModIds[0]));
            }
        return item;
    }

    /// <summary>One-shot import — batch rows and quick loads.</summary>
    public static Result Import(EngineService engine, ParsedItem parsed, string fallbackName = "imported item")
    {
        var mapped = Map(engine, parsed);
        var gate = Gate(engine, mapped.ClassId);
        if (gate is not null) return new Result(null, [], gate);
        var violations = Validate(engine, parsed, mapped);
        var error = violations.FirstOrDefault(v => v.Level == "error");
        if (error is not null) return new Result(null, violations, error.Message);
        return new Result(Materialize(parsed, mapped, fallbackName), violations, null);
    }
}

using PoeRegex.Web.Core;

namespace PoeRegex.Data.Build;

/// <summary>
/// Validate the curated common shortlist against the shipped catalog. A
/// contributor entry can use either an explicit family key or the visible
/// family text; this check resolves it, confirms the pick compiles for every
/// scoped class, and keeps the shortlist small.
/// </summary>
internal static class CommonPicksAudit
{
    private const int MaxCommonFamilies = 30;

    public static int Run(string[] args)
    {
        var check = args.Contains("--check", StringComparer.Ordinal);
        var table = args.Contains("--table", StringComparer.Ordinal);
        var errors = new List<string>();
        var warnings = new List<string>();
        string? generated = null;

        try
        {
            var engine = new EngineService();
            var playable = ModFamilies.Group(engine.PlayableMods());
            var playableCommon = playable.Count(f => ModFamilies.IsCommon(f));
            if (playableCommon >= MaxCommonFamilies)
            {
                errors.Add(
                    $"PlayableMods has {playableCommon} common families; the cap is " +
                    $"less than {MaxCommonFamilies}.");
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in CommonPicks.Entries)
            {
                var label = string.IsNullOrWhiteSpace(entry.Label)
                    ? "(unnamed)"
                    : entry.Label.Trim();
                if (string.IsNullOrWhiteSpace(entry.Family) &&
                    string.IsNullOrWhiteSpace(entry.Match))
                {
                    errors.Add($"{label}: needs a family key or a visible match.");
                    continue;
                }

                var duplicateKey =
                    (entry.Family ?? entry.Match) + "\u0001" +
                    string.Join(",", entry.Classes.OrderBy(c => c, StringComparer.Ordinal));
                if (!seen.Add(duplicateKey))
                {
                    errors.Add($"{label}: duplicate family/match and class scope.");
                    continue;
                }

                if (entry.Classes.Count == 0)
                {
                    var resolved = Resolve(engine, entry, null);
                    if (resolved.Count == 0)
                    {
                        errors.Add($"{label}: no family matches the catalog.");
                        continue;
                    }
                    if (resolved.Count > 1)
                    {
                        warnings.Add(
                            $"{label}: matches {resolved.Count} families " +
                            $"({string.Join(", ", resolved.Select(f => f.Key))}); " +
                            "add a family key to disambiguate.");
                    }
                    foreach (var family in resolved)
                        ValidateFamily(engine, entry, family, null, label, errors);
                    continue;
                }

                foreach (var cls in entry.Classes.Distinct(StringComparer.Ordinal))
                {
                    var resolved = Resolve(engine, entry, cls);
                    if (resolved.Count == 0)
                    {
                        errors.Add($"{label}: no family matches for class '{cls}'.");
                        continue;
                    }
                    if (resolved.Count > 1)
                    {
                        warnings.Add(
                            $"{label} ({cls}): matches {resolved.Count} families " +
                            $"({string.Join(", ", resolved.Select(f => f.Key))}); " +
                            "add a family key to disambiguate.");
                    }
                    foreach (var family in resolved)
                        ValidateFamily(engine, entry, family, cls, label, errors);
                }
            }

            foreach (var cls in engine.PlayableClasses().Select(c => c.Id))
            {
                var count = ModFamilies.Group(engine.ModsForClass(cls))
                    .Count(f => ModFamilies.IsCommon(f, cls));
                if (count >= MaxCommonFamilies)
                {
                    errors.Add(
                        $"Class '{cls}' has {count} common families; the cap is " +
                        $"less than {MaxCommonFamilies}.");
                }
            }

            Console.WriteLine(
                $"Common picks: {CommonPicks.Entries.Count} entries " +
                $"({CommonPicks.FamilyKeys.Count} family keys, " +
                $"{CommonPicks.MatchTexts.Count} visible matches).");
            Console.WriteLine(
                $"PlayableMods: {playableCommon} common families " +
                $"(cap < {MaxCommonFamilies}).");

            generated = CommonPicksTable.Render(engine);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"common-picks: {Unwrap(ex).Message}");
            return 1;
        }

        if (generated is not null)
        {
            if (table)
            {
                if (errors.Count == 0) UpdateTable(generated, errors);
            }
            else if (check)
            {
                CheckTable(generated, errors);
            }
        }

        foreach (var warning in warnings)
            Console.WriteLine($"note: {warning}");
        foreach (var error in errors)
            Console.Error.WriteLine($"error: {error}");

        if (errors.Count > 0)
        {
            if (check || table) return 1;
            Console.WriteLine("Run with --check to fail on these errors.");
        }
        return 0;
    }

    internal static List<ModFamily> Resolve(
        EngineService engine, CommonPick entry, string? cls)
    {
        var mods = cls is null ? engine.PlayableMods() : engine.ModsForClass(cls);
        var families = ModFamilies.Group(mods);
        if (!string.IsNullOrWhiteSpace(entry.Family))
        {
            var key = entry.Family.Trim();
            return families
                .Where(f => string.Equals(f.Key, key, StringComparison.Ordinal) ||
                            f.Members.Any(m => string.Equals(
                                ModFamilies.KeyOf(m), key, StringComparison.Ordinal)))
                .ToList();
        }
        var match = entry.Match!.Trim();
        return families
            .Where(f => string.Equals(f.DisplayText, match, StringComparison.Ordinal))
            .ToList();
    }

    private static void ValidateFamily(
        EngineService engine, CommonPick entry, ModFamily family, string? cls,
        string label, List<string> errors)
    {
        if (!ModFamilies.IsCommon(family, cls))
        {
            errors.Add($"{label}: resolved family '{family.Key}' is not common at runtime.");
        }
        var fragment = engine.PickFragment(family.Pick(), cls);
        if (string.IsNullOrWhiteSpace(fragment))
        {
            var scope = cls is null ? "the global scope" : $"class '{cls}'";
            errors.Add($"{label}: family '{family.Key}' compiles no fragment for {scope}.");
        }
    }

    private static void CheckTable(string generated, List<string> errors)
    {
        try
        {
            var path = CommonPicksTable.RelativePath;
            var document = File.ReadAllText(path);
            if (!string.Equals(
                    CommonPicksTable.ExtractBlock(document), generated, StringComparison.Ordinal))
            {
                errors.Add(
                    $"{path} is out of date; run `common-picks --table` and commit the result.");
            }
        }
        catch (Exception ex)
        {
            errors.Add($"{CommonPicksTable.RelativePath}: {Unwrap(ex).Message}");
        }
    }

    private static void UpdateTable(string generated, List<string> errors)
    {
        try
        {
            var path = CommonPicksTable.RelativePath;
            var document = File.ReadAllText(path);
            var updated = CommonPicksTable.ReplaceBlock(document, generated);
            if (string.Equals(updated, document, StringComparison.Ordinal))
            {
                Console.WriteLine($"Common picks table is up to date: {path}");
                return;
            }
            File.WriteAllText(path, updated);
            Console.WriteLine($"Updated common picks table: {path}");
        }
        catch (Exception ex)
        {
            errors.Add($"cannot update {CommonPicksTable.RelativePath}: {Unwrap(ex).Message}");
        }
    }

    private static Exception Unwrap(Exception ex) =>
        ex is TypeInitializationException { InnerException: { } inner } ? inner : ex;
}

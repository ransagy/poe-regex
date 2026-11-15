using System.Text;
using System.Text.Json;
using PoeRegex.Core.Compilation;
using PoeRegex.Core.Data;

namespace PoeRegex.Data.Build;

/// <summary>
/// Compare two PoE1 catalogs (and optionally their vendor corpora and exact
/// tables) without caring which upstream export produced them. This is the
/// release gate for moving from the secondary ExileData source back to the
/// primary RePoE source once RePoE catches up to the live patch.
/// </summary>
public static class Poe1SourceCompare
{
    private const int MaxExamplesPerSection = 200;

    private sealed record Diff(bool HasDifferences, string Summary, string Markdown);

    public static int Run(string[] args)
    {
        var opts = ParseOptions(args);
        var leftPath = GetValue(opts, "--left", "");
        var rightPath = GetValue(opts, "--right", "");
        var outPath = GetValue(opts, "--out", "out/poe1-source-diff.md");
        var failOnDiff = opts.ContainsKey("--fail-on-diff");

        if (leftPath.Length == 0 || rightPath.Length == 0)
        {
            Console.Error.WriteLine(
                "poe1-compare needs --left <catalog> and --right <catalog>");
            return 2;
        }

        try
        {
            var left = new FileCatalogSource(leftPath).Load();
            var right = new FileCatalogSource(rightPath).Load();
            var diff = CompareCatalogs(left, right, leftPath, rightPath, opts);
            Directory.CreateDirectory(Path.GetDirectoryName(outPath) ?? ".");
            File.WriteAllText(outPath, diff.Markdown);
            Console.WriteLine($"poe1-compare: {diff.Summary}");
            Console.WriteLine($"  wrote {outPath}");
            return diff.HasDifferences && failOnDiff ? 2 : 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"poe1-compare: {ex.Message}");
            return 1;
        }
    }

    private static Diff CompareCatalogs(
        Catalog left,
        Catalog right,
        string leftPath,
        string rightPath,
        Dictionary<string, string> opts)
    {
        var md = new StringBuilder();
        var differences = 0;

        md.AppendLine("# PoE1 source comparison");
        md.AppendLine();
        md.AppendLine($"- Left: `{leftPath}`");
        md.AppendLine($"- Right: `{rightPath}`");
        md.AppendLine($"- Left data version: `{left.DataVersion}`");
        md.AppendLine($"- Right data version: `{right.DataVersion}`");
        md.AppendLine();

        var leftClasses = left.ItemClasses.ToDictionary(c => c.Id, StringComparer.Ordinal);
        var rightClasses = right.ItemClasses.ToDictionary(c => c.Id, StringComparer.Ordinal);
        var classAdded = rightClasses.Keys.Except(leftClasses.Keys, StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal).ToList();
        var classRemoved = leftClasses.Keys.Except(rightClasses.Keys, StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal).ToList();
        var classChanged = leftClasses.Keys.Intersect(rightClasses.Keys, StringComparer.Ordinal)
            .Where(id =>
                !string.Equals(leftClasses[id].Name, rightClasses[id].Name, StringComparison.Ordinal) ||
                !string.Equals(leftClasses[id].BaseFingerprint, rightClasses[id].BaseFingerprint,
                    StringComparison.Ordinal))
            .OrderBy(x => x, StringComparer.Ordinal).ToList();

        var leftMods = left.Mods.ToDictionary(m => m.Id, StringComparer.Ordinal);
        var rightMods = right.Mods.ToDictionary(m => m.Id, StringComparer.Ordinal);
        var modAdded = rightMods.Keys.Except(leftMods.Keys, StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal).ToList();
        var modRemoved = leftMods.Keys.Except(rightMods.Keys, StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal).ToList();
        var modChanged = leftMods.Keys.Intersect(rightMods.Keys, StringComparer.Ordinal)
            .Where(id => !ModsEqual(leftMods[id], rightMods[id]))
            .OrderBy(x => x, StringComparer.Ordinal).ToList();

        var classDiff = classAdded.Count + classRemoved.Count + classChanged.Count;
        var modDiff = modAdded.Count + modRemoved.Count + modChanged.Count;
        differences += classDiff + modDiff;

        md.AppendLine("## Summary");
        md.AppendLine();
        md.AppendLine($"- Item classes: {leftClasses.Count} / {rightClasses.Count}; " +
                      $"added {classAdded.Count}, removed {classRemoved.Count}, " +
                      $"changed {classChanged.Count}");
        md.AppendLine($"- Mods: {leftMods.Count} / {rightMods.Count}; " +
                      $"added {modAdded.Count}, removed {modRemoved.Count}, " +
                      $"changed {modChanged.Count}");
        md.AppendLine();

        md.AppendLine("## Item classes");
        md.AppendLine();
        if (classDiff == 0)
        {
            md.AppendLine("No differences.");
        }
        else
        {
            foreach (var id in classAdded.Take(MaxExamplesPerSection))
                md.AppendLine($"- Added `{id}` ({rightClasses[id].Name})");
            foreach (var id in classRemoved.Take(MaxExamplesPerSection))
                md.AppendLine($"- Removed `{id}` ({leftClasses[id].Name})");
            foreach (var id in classChanged.Take(MaxExamplesPerSection))
            {
                md.AppendLine($"- Changed `{id}`");
                if (!string.Equals(leftClasses[id].Name, rightClasses[id].Name,
                        StringComparison.Ordinal))
                    md.AppendLine($"  - name: `{leftClasses[id].Name}` -> `{rightClasses[id].Name}`");
                if (!string.Equals(leftClasses[id].BaseFingerprint,
                        rightClasses[id].BaseFingerprint, StringComparison.Ordinal))
                    md.AppendLine(
                        $"  - baseFingerprint: `{leftClasses[id].BaseFingerprint}` -> " +
                        $"`{rightClasses[id].BaseFingerprint}`");
            }
        }
        md.AppendLine();

        AppendModList(md, "Added mods", modAdded, rightMods, added: true);
        AppendModList(md, "Removed mods", modRemoved, leftMods, added: false);

        md.AppendLine("## Changed mods");
        md.AppendLine();
        if (modChanged.Count == 0)
        {
            md.AppendLine("No differences.");
        }
        else
        {
            foreach (var id in modChanged.Take(MaxExamplesPerSection))
            {
                md.AppendLine($"- `{id}`");
                foreach (var field in ModFieldDiff(leftMods[id], rightMods[id]))
                    md.AppendLine($"  - {field}");
            }
            if (modChanged.Count > MaxExamplesPerSection)
                md.AppendLine($"- …and {modChanged.Count - MaxExamplesPerSection} more");
        }
        md.AppendLine();

        differences += AppendVendorDiff(md, opts);
        differences += AppendTableDiff(md, opts);

        var summary =
            $"classes {leftClasses.Count}/{rightClasses.Count} " +
            $"(+{classAdded.Count}/-{classRemoved.Count}/~{classChanged.Count}); " +
            $"mods {leftMods.Count}/{rightMods.Count} " +
            $"(+{modAdded.Count}/-{modRemoved.Count}/~{modChanged.Count}); " +
            $"differences {differences}";
        return new Diff(differences > 0, summary, md.ToString());
    }

    private static void AppendModList(
        StringBuilder md,
        string title,
        IReadOnlyList<string> ids,
        IReadOnlyDictionary<string, ModEntry> mods,
        bool added)
    {
        md.AppendLine($"## {title}");
        md.AppendLine();
        if (ids.Count == 0)
        {
            md.AppendLine("No differences.");
        }
        else
        {
            foreach (var id in ids.Take(MaxExamplesPerSection))
            {
                var mod = mods[id];
                var text = mod.StatLines.Count == 0
                    ? ""
                    : " — " + string.Join(" / ", mod.StatLines.Select(l => l.Line));
                md.AppendLine(
                    $"- `{id}` [{mod.Source}/{mod.GenerationType}] " +
                    $"{string.Join(", ", mod.Classes)}{text}");
            }
            if (ids.Count > MaxExamplesPerSection)
                md.AppendLine($"- …and {ids.Count - MaxExamplesPerSection} more");
        }
        md.AppendLine();
    }

    private static bool ModsEqual(ModEntry left, ModEntry right) =>
        ModFieldDiff(left, right).Count == 0;

    private static List<string> ModFieldDiff(ModEntry left, ModEntry right)
    {
        var fields = new List<string>();
        Compare("name", left.Name, right.Name);
        Compare("generationType", left.GenerationType, right.GenerationType);
        Compare("source", left.Source, right.Source);
        Compare("family", left.Family, right.Family);
        Compare("requiredLevel", left.RequiredLevel.ToString(), right.RequiredLevel.ToString());

        var leftClasses = string.Join(",", left.Classes.OrderBy(x => x, StringComparer.Ordinal));
        var rightClasses = string.Join(",", right.Classes.OrderBy(x => x, StringComparer.Ordinal));
        Compare("classes", leftClasses, rightClasses);

        var leftGroups = string.Join(",", left.Groups.OrderBy(x => x, StringComparer.Ordinal));
        var rightGroups = string.Join(",", right.Groups.OrderBy(x => x, StringComparer.Ordinal));
        Compare("groups", leftGroups, rightGroups);

        if (left.StatLines.Count != right.StatLines.Count)
        {
            fields.Add($"statLines: {left.StatLines.Count} -> {right.StatLines.Count}");
        }
        else
        {
            for (var i = 0; i < left.StatLines.Count; i++)
            {
                var a = left.StatLines[i];
                var b = right.StatLines[i];
                Compare($"statLines[{i}].line", a.Line, b.Line);
                Compare($"statLines[{i}].rangeLine", a.RangeLine, b.RangeLine);
                Compare($"statLines[{i}].pattern", a.Pattern, b.Pattern);
                Compare($"statLines[{i}].minValue", a.MinValue.ToString("G9"), b.MinValue.ToString("G9"));
                Compare($"statLines[{i}].maxValue", a.MaxValue.ToString("G9"), b.MaxValue.ToString("G9"));
            }
        }

        return fields;

        void Compare(string field, string? a, string? b)
        {
            if (!string.Equals(a, b, StringComparison.Ordinal))
                fields.Add($"{field}: `{a}` -> `{b}`");
        }
    }

    private static int AppendVendorDiff(StringBuilder md, Dictionary<string, string> opts)
    {
        var leftPath = GetValue(opts, "--left-vendor", "");
        var rightPath = GetValue(opts, "--right-vendor", "");
        if (leftPath.Length == 0 || rightPath.Length == 0) return 0;

        var left = LoadVendor(leftPath);
        var right = LoadVendor(rightPath);
        var leftKeys = left.Lines.Select(Key).ToHashSet(StringComparer.Ordinal);
        var rightKeys = right.Lines.Select(Key).ToHashSet(StringComparer.Ordinal);
        var added = rightKeys.Except(leftKeys, StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal).ToList();
        var removed = leftKeys.Except(rightKeys, StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal).ToList();

        md.AppendLine("## Vendor corpus");
        md.AppendLine();
        md.AppendLine($"- Lines: {left.Lines.Count} / {right.Lines.Count}");
        md.AppendLine($"- Added: {added.Count}; removed: {removed.Count}");
        md.AppendLine();
        if (added.Count == 0 && removed.Count == 0)
        {
            md.AppendLine("No differences.");
        }
        else
        {
            foreach (var key in added.Take(MaxExamplesPerSection))
                md.AppendLine($"- Added `{key}`");
            foreach (var key in removed.Take(MaxExamplesPerSection))
                md.AppendLine($"- Removed `{key}`");
            if (added.Count + removed.Count > MaxExamplesPerSection)
                md.AppendLine(
                    $"- …and {added.Count + removed.Count - MaxExamplesPerSection} more");
        }
        md.AppendLine();
        return added.Count + removed.Count;
    }

    private static int AppendTableDiff(StringBuilder md, Dictionary<string, string> opts)
    {
        var leftPath = GetValue(opts, "--left-table", "");
        var rightPath = GetValue(opts, "--right-table", "");
        if (leftPath.Length == 0 || rightPath.Length == 0) return 0;

        var left = LoadTable(leftPath);
        var right = LoadTable(rightPath);
        var added = 0;
        var removed = 0;
        var changed = 0;
        var examples = new List<string>();

        CompareScope(null, left.Global, right.Global);
        foreach (var scope in left.ByClass.Keys.Union(right.ByClass.Keys)
                     .OrderBy(x => x, StringComparer.Ordinal))
        {
            left.ByClass.TryGetValue(scope, out var a);
            right.ByClass.TryGetValue(scope, out var b);
            CompareScope(scope, a ?? new Dictionary<string, string>(), b ?? new Dictionary<string, string>());
        }

        md.AppendLine("## Exact table");
        md.AppendLine();
        md.AppendLine($"- Scopes: {ScopeCount(left)} / {ScopeCount(right)}");
        md.AppendLine($"- Added: {added}; removed: {removed}; changed: {changed}");
        md.AppendLine();
        if (added + removed + changed == 0)
        {
            md.AppendLine("No differences.");
        }
        else
        {
            foreach (var example in examples.Take(MaxExamplesPerSection))
                md.AppendLine($"- {example}");
            if (added + removed + changed > MaxExamplesPerSection)
                md.AppendLine(
                    $"- …and {added + removed + changed - MaxExamplesPerSection} more");
        }
        md.AppendLine();
        return added + removed + changed;

        void CompareScope(
            string? scope,
            IReadOnlyDictionary<string, string> a,
            IReadOnlyDictionary<string, string> b)
        {
            foreach (var pattern in a.Keys.Union(b.Keys).OrderBy(x => x, StringComparer.Ordinal))
            {
                var hasA = a.TryGetValue(pattern, out var fa);
                var hasB = b.TryGetValue(pattern, out var fb);
                var label = (scope ?? "global") + "/" + pattern.Replace('\u0001', '#');
                if (!hasA)
                {
                    added++;
                    examples.Add($"Added `{label}` = `{fb}`");
                }
                else if (!hasB)
                {
                    removed++;
                    examples.Add($"Removed `{label}` (was `{fa}`)");
                }
                else if (!string.Equals(fa, fb, StringComparison.Ordinal))
                {
                    changed++;
                    examples.Add($"Changed `{label}`: `{fa}` -> `{fb}`");
                }
            }
        }
    }

    private static string Key(VendorLine line) =>
        $"{line.ClassId ?? "global"}|{line.Kind ?? ""}|{line.Pattern ?? ""}|{line.Line}";

    private static VendorCorpusFile LoadVendor(string path) =>
        JsonSerializer.Deserialize<VendorCorpusFile>(
            File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        ?? throw new InvalidDataException($"empty vendor corpus: {path}");

    private static FragmentTable LoadTable(string path) =>
        JsonSerializer.Deserialize<FragmentTable>(
            File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        ?? throw new InvalidDataException($"empty fragment table: {path}");

    private static int ScopeCount(FragmentTable table) =>
        table.Global.Count + table.ByClass.Values.Sum(x => x.Count);

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal)) continue;
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                dict[args[i]] = args[i + 1];
                i++;
            }
            else
            {
                dict[args[i]] = "1";
            }
        }
        return dict;
    }

    private static string GetValue(
        Dictionary<string, string> opts, string key, string fallback) =>
        opts.TryGetValue(key, out var value) ? value : fallback;
}

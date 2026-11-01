using System.Text.RegularExpressions;
using PoeRegex.Core;
using PoeRegex.Core.Compilation;
using PoeRegex.Core.Data;
using PoeRegex.Core.Explain;

namespace PoeRegex.Web.Core;

/// <summary>One parsed query token and its best-effort mapping.</summary>
public sealed record ImportedToken(
    string Phrase,
    bool Excluded,
    string Kind,
    string? Pattern,
    string? ModId,
    IReadOnlyList<string> Classes,
    string Confidence);

/// <summary>
/// Best-effort import of an already-compiled query into the UI model. It uses
/// only the shipped fragment table as a reverse index; it does not run the
/// shortest-form search or the explainer.
/// </summary>
public sealed class QueryImportService
{
    private readonly EngineService _engine;
    private readonly Dictionary<string, List<(string Pattern, string? Class)>> _byFragment =
        new(StringComparer.Ordinal);
    private Dictionary<string, List<string>>? _classByAnchor;

    public QueryImportService(EngineService engine)
    {
        _engine = engine;
        foreach (var (pattern, fragment, cls) in engine.PrecomputedFragments())
        {
            if (fragment.Length == 0) continue;
            if (!_byFragment.TryGetValue(fragment, out var list))
            {
                list = [];
                _byFragment[fragment] = list;
            }
            list.Add((pattern, cls));
        }
    }

    /// <summary>The precomputed fragment reverse-index, shared with reconstruction.</summary>
    internal IReadOnlyList<(string Pattern, string? Class)> FragmentsFor(string fragment) =>
        _byFragment.TryGetValue(fragment, out var list) ? list : [];

    /// <summary>
    /// Built on first anchor lookup: the picker only ever emits in-game
    /// classes, and indexing every catalog class made the first preview pay
    /// for metadata classes nobody can search.
    /// </summary>
    private Dictionary<string, List<string>> ClassByAnchor =>
        _classByAnchor ??= BuildClassByAnchor();

    private Dictionary<string, List<string>> BuildClassByAnchor()
    {
        var map = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var itemClass in _engine.InGameClasses())
        {
            // The optimized anchor is what this build emits; also accept the
            // literal prefix so queries compiled before the anchor change
            // still import cleanly.
            foreach (var anchor in new[]
                     {
                         _engine.ClassNameAnchor(itemClass.Id),
                         Poe2Syntax.EscapeLiteral(_engine.ClassNamePrefix(itemClass.Id)),
                     }.Distinct(StringComparer.Ordinal))
            {
                if (anchor.Length == 0) continue;
                var phrase = ": " + anchor;
                if (!map.TryGetValue(phrase, out var classes))
                {
                    classes = [];
                    map[phrase] = classes;
                }
                if (!classes.Contains(itemClass.Id, StringComparer.Ordinal))
                    classes.Add(itemClass.Id);
            }
        }
        return map;
    }

    public QueryImportResult Import(string query, string? itemClass = null)
    {
        var diagnostics = new List<string>();
        var shapeErrors = QueryShape.Validate(query);
        diagnostics.AddRange(shapeErrors.Select(x => "shape: " + x));

        var classes = new HashSet<string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(itemClass)) classes.Add(itemClass);

        var tokens = new List<ImportedToken>();
        var includes = new List<ModifierRequirement>();
        var includeGroups = new List<IReadOnlyList<ModifierRequirement>>();
        var excludes = new List<ModifierRequirement>();
        var keywords = new List<string>();

        foreach (var (phrase, excluded) in Explainer.Tokenize(query))
        {
            var anchorClasses = ResolveClassAnchor(phrase);
            if (!excluded && anchorClasses.Count > 0)
            {
                classes.UnionWith(anchorClasses);
                tokens.Add(new ImportedToken(
                    phrase, false, "class", null, null,
                    anchorClasses.OrderBy(x => x, StringComparer.Ordinal).ToList(),
                    "exact"));
                continue;
            }

            var matches = Lookup(phrase, classes);
            if (matches.Count == 0 && LooksSynthetic(phrase))
            {
                var recovered = RecoverViaExplain(phrase, classes, excluded);
                if (recovered.Count > 0)
                {
                    foreach (var (recoveredPattern, recoveredMod) in recovered)
                    {
                        var recoveredRequirement = new ModifierRequirement(recoveredMod);
                        if (excluded) excludes.Add(recoveredRequirement);
                        else includes.Add(recoveredRequirement);
                    }
                    if (!excluded) includeGroups.Add(recovered.Select(r => new ModifierRequirement(r.Mod)).ToList());
                    var first = recovered[0];
                    var firstMod = _engine.FindMod(first.Mod);
                    tokens.Add(new ImportedToken(
                        phrase, excluded, excluded ? "exclusion" : "mod",
                        first.Pattern, first.Mod,
                        firstMod?.Classes ?? [], "recovered"));
                    diagnostics.Add(
                        $"fragment '{phrase}' recovered {recovered.Count} catalog mod(s) via explain");
                    continue;
                }
            }
            if (matches.Count == 0)
            {
                if (excluded) keywords.Add("!" + phrase);
                else keywords.Add(phrase);
                tokens.Add(new ImportedToken(
                    phrase, excluded, "keyword", null, null, [], "unknown"));
                diagnostics.Add($"unmatched {(excluded ? "exclusion" : "token")} '{phrase}' preserved as keyword");
                continue;
            }

            var grouped = matches
                .GroupBy(x => x.Pattern, StringComparer.Ordinal)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .ToList();
            if (grouped.Count > 1)
                diagnostics.Add(
                    $"fragment '{phrase}' maps to {grouped.Count} patterns; using '{grouped[0].Key}'");

            var pattern = grouped[0].Key;
            var mods = _engine.ModsForPattern(pattern)
                .OrderBy(m => m.Id, StringComparer.Ordinal)
                .ToList();
            if (mods.Count == 0)
            {
                if (excluded) keywords.Add("!" + phrase);
                else keywords.Add(phrase);
                tokens.Add(new ImportedToken(
                    phrase, excluded, "keyword", pattern, null, [], "unresolved"));
                diagnostics.Add($"fragment '{phrase}' resolved to pattern '{pattern}' with no mod");
                continue;
            }

            var mod = classes.Count > 0
                ? mods.FirstOrDefault(m => m.Classes.Any(classes.Contains)) ?? mods[0]
                : mods[0];
            if (mods.Count > 1)
                diagnostics.Add(
                    $"fragment '{phrase}' maps to {mods.Count} mods; using '{mod.Id}'");
            var requirement = new ModifierRequirement(mod.Id);
            if (excluded) excludes.Add(requirement);
            else
            {
                includes.Add(requirement);
                includeGroups.Add([requirement]);
            }
            tokens.Add(new ImportedToken(
                phrase, excluded, excluded ? "exclusion" : "mod",
                pattern, mod.Id, mod.Classes, grouped.Count > 1 || mods.Count > 1 ? "ambiguous" : "exact"));
        }

        var orderedClasses = classes.OrderBy(x => x, StringComparer.Ordinal).ToList();
        var mode = orderedClasses.Count switch
        {
            0 => "simple",
            1 => "advanced",
            _ => "multi-class",
        };
        if (shapeErrors.Count > 0)
            diagnostics.Add("the query is not a valid quoted query; imported tokens are best-effort");

        return new QueryImportResult(
            query, mode, orderedClasses, includes, excludes, keywords, tokens, diagnostics, includeGroups);
    }

    private static bool LooksSynthetic(string phrase) =>
        phrase.Any(c => c is '|' or '(' or ')' or '[' or ']' or '{' or '}' or
            '\\' or '^' or '$' or '*' or '+' or '?' or '.');

    private List<(string Pattern, string Mod)> RecoverViaExplain(
        string phrase, IReadOnlySet<string> classes, bool excluded)
    {
        try
        {
            var classId = classes.Count == 1 ? classes.First() : null;
            var explanation = _engine.Explain(Poe2Syntax.Quote(phrase), classId);
            var result = new List<(string Pattern, string Mod)>();
            foreach (var match in explanation.Tokens.SelectMany(t => t.Matches))
            {
                foreach (var modId in match.ModIds)
                {
                    var mod = _engine.FindMod(modId);
                    if (mod is null) continue;
                    if (classes.Count > 0 &&
                        !mod.Classes.Any(classes.Contains)) continue;
                    if (result.Any(x => x.Mod == modId)) continue;
                    result.Add((match.Pattern, modId));
                }
            }
            return result;
        }
        catch (ArgumentException)
        {
            return [];
        }
    }

    private List<(string Pattern, string? Class)> Lookup(
        string phrase, IReadOnlySet<string> classes)
    {
        if (!_byFragment.TryGetValue(phrase, out var matches)) return [];
        if (classes.Count == 0) return matches;
        var scoped = matches
            .Where(x => x.Class is null || classes.Contains(x.Class))
            .ToList();
        return scoped.Count > 0 ? scoped : matches;
    }

    private List<string> ResolveClassAnchor(string phrase)
    {
        if (!phrase.StartsWith(": ", StringComparison.Ordinal)) return [];
        if (ClassByAnchor.TryGetValue(phrase, out var exact))
            return exact;

        // Paste anchors are alternations: ": (Am|Ri)" and merged forms such
        // as ": (.*Hand M)". Match them against the catalog class lines.
        var inner = phrase[2..];
        if (!inner.StartsWith('(') || !inner.EndsWith(')')) return [];
        var result = new List<string>();
        foreach (var cls in _engine.Classes)
        {
            var line = $"Item Class: {cls.Name}";
            try
            {
                if (Regex.IsMatch(line, phrase,
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                        TimeSpan.FromMilliseconds(50)))
                    result.Add(cls.Id);
            }
            catch (ArgumentException)
            {
                return [];
            }
        }
        return result;
    }
}

public sealed record QueryImportResult(
    string Original,
    string Mode,
    IReadOnlyList<string> Classes,
    IReadOnlyList<ModifierRequirement> Includes,
    IReadOnlyList<ModifierRequirement> Excludes,
    IReadOnlyList<string> Keywords,
    IReadOnlyList<ImportedToken> Tokens,
    IReadOnlyList<string> Diagnostics,
    IReadOnlyList<IReadOnlyList<ModifierRequirement>>? IncludeGroups = null)
{
    /// <summary>True when the string required several picks at once (one include block each).</summary>
    public bool RequiresAll => IncludeGroups is { Count: > 1 } groups && groups.All(g => g.Count == 1);
}

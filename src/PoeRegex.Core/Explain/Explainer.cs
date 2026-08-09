using PoeRegex.Core.Compilation;
using PoeRegex.Core.Data;
using PoeRegex.Core.Search;

namespace PoeRegex.Core.Explain;

/// <summary>
/// Reverses a trade-search query back into the classes, tokens, and candidate
/// mods it could match. Matching is case-insensitive and treats the digit
/// placeholder as a digit-run wildcard. Candidate mods are the AND-intersection
/// over included tokens (a mod must be matched by every included phrase),
/// minus any mod matched by an excluded phrase. Ambiguity is surfaced as the
/// full candidate list rather than a single answer.
/// </summary>
public sealed class Explainer
{
    private readonly EngineCorpus _corpus;

    public Explainer(EngineCorpus corpus) => _corpus = corpus;

    public ExplainResult Explain(string query, string? itemClass = null)
    {
        var raw = Tokenize(query);

        // Scope: an explicit class is authoritative; otherwise every class
        // participates (a generated class anchor is a class-corpus fingerprint
        // and is typically ambiguous, so it cannot narrow the scope on its own).
        var scope = itemClass is not null
            ? new List<string> { itemClass }
            : _corpus.Catalog.ItemClasses.Select(c => c.Id).OrderBy(x => x, StringComparer.Ordinal).ToList();

        // Pass 1: classify each token. A token is a class anchor only when it
        // matches a class line AND no mod line in scope — mod fragments such as
        // "ex" can coincidentally appear in a class name, so matching any mod
        // line takes precedence.
        var kinds = new string[raw.Count];
        var matchesByIndex = new List<ExplainMatch>[raw.Count];
        for (var i = 0; i < raw.Count; i++)
        {
            var (phrase, excluded) = raw[i];
            var matches = MatchLines(phrase, scope);
            matchesByIndex[i] = matches;
            if (excluded)
            {
                kinds[i] = "excluded";
                continue;
            }
            if (matches.Count == 0 && MatchedClasses(phrase).Count > 0)
            {
                kinds[i] = "class";
            }
            else
            {
                kinds[i] = matches.Count > 0 ? "match" : "keyword";
            }
        }

        // Pass 2: build tokens.
        var tokens = new List<ExplainToken>(raw.Count);
        for (var i = 0; i < raw.Count; i++)
        {
            var (phrase, excluded) = raw[i];
            if (kinds[i] == "class")
            {
                var clsList = MatchedClasses(phrase).OrderBy(x => x, StringComparer.Ordinal).ToList();
                tokens.Add(new ExplainToken
                {
                    Index = i,
                    Phrase = phrase,
                    Excluded = excluded,
                    Kind = "class",
                    Classes = clsList,
                    Matches = clsList.Select(MakeClassMatch).ToList(),
                });
                continue;
            }

            var matches = matchesByIndex[i]!;
            var kind = kinds[i] == "excluded"
                ? "excluded"
                : matches.Count > 0 ? "included" : "keyword";
            var tokenClasses = matches.Select(m => m.Class).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
            tokens.Add(new ExplainToken
            {
                Index = i,
                Phrase = phrase,
                Excluded = excluded,
                Kind = kind,
                Classes = tokenClasses,
                Matches = matches,
            });
        }

        var mods = CandidateMods(tokens);

        var anchored = itemClass is not null || tokens.Any(t => t.Kind == "class");
        var classes = anchored
            ? scope.OrderBy(x => x, StringComparer.Ordinal)
                .Select(c => ToExplainClass(c)!)
                .Where(c => c is not null)
                .ToList()
            : [];

        return new ExplainResult
        {
            Query = query,
            Game = _corpus.Catalog.Game,
            Classes = classes,
            Tokens = tokens,
            Mods = mods,
        };
    }

    private ExplainClass? ToExplainClass(string classId)
    {
        var entry = _corpus.FindItemClass(classId);
        return entry is null ? null : new ExplainClass { Id = entry.Id, Name = entry.Name };
    }

    private ExplainMatch MakeClassMatch(string classId)
    {
        var entry = _corpus.FindItemClass(classId)!;
        var line = $"Item Class: {entry.Name}";
        return new ExplainMatch
        {
            Class = classId,
            Line = line,
            Pattern = CatalogLineNormalizer.Normalize(line),
            ModIds = [],
        };
    }

    private List<ExplainMatch> MatchLines(string phrase, IReadOnlyList<string> scope)
    {
        var regex = PhraseToRegex(phrase);
        var matches = new List<ExplainMatch>();
        foreach (var cls in scope)
        {
            foreach (var (pattern, real) in _corpus.RealLinesForClass(cls))
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch(real, regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                {
                    continue;
                }
                var modIds = _corpus.ModsWithPattern(pattern)
                    .Where(m => m.Classes.Contains(cls))
                    .Select(m => m.Id)
                    .Distinct()
                    .OrderBy(x => x, StringComparer.Ordinal)
                    .ToList();
                matches.Add(new ExplainMatch { Class = cls, Line = real, Pattern = pattern, ModIds = modIds });
            }
        }
        matches.Sort(static (a, b) =>
        {
            var c = string.CompareOrdinal(a.Class, b.Class);
            if (c != 0) return c;
            c = string.CompareOrdinal(a.Pattern, b.Pattern);
            return c != 0 ? c : string.CompareOrdinal(a.Line, b.Line);
        });
        return matches;
    }

    private HashSet<string> MatchedClasses(string phrase)
    {
        var regex = PhraseToRegex(phrase);
        var result = new HashSet<string>();
        foreach (var cls in _corpus.Catalog.ItemClasses)
        {
            var line = $"Item Class: {cls.Name}";
            if (System.Text.RegularExpressions.Regex.IsMatch(line, regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                result.Add(cls.Id);
            }
        }
        return result;
    }

    /// <summary>
    /// Candidate mods = the AND-intersection of the mods matched by every included
    /// token, minus any mod matched by an excluded token.
    /// </summary>
    private List<ExplainMod> CandidateMods(IReadOnlyList<ExplainToken> tokens)
    {
        var included = tokens.Where(t => t.Kind == "included").ToList();
        if (included.Count == 0) return [];

        HashSet<string>? candidates = null;
        foreach (var token in included)
        {
            var matched = token.Matches.SelectMany(m => m.ModIds).ToHashSet();
            candidates = candidates is null ? matched : candidates.Intersect(matched).ToHashSet();
        }
        if (candidates is null || candidates.Count == 0) return [];

        foreach (var token in tokens.Where(t => t.Kind == "excluded"))
        {
            var excluded = token.Matches.SelectMany(m => m.ModIds).ToHashSet();
            candidates.ExceptWith(excluded);
        }

        var result = new List<ExplainMod>(candidates.Count);
        foreach (var modId in candidates.OrderBy(x => x, StringComparer.Ordinal))
        {
            var mod = _corpus.FindMod(modId);
            if (mod is null) continue;

            var phrases = included
                .Where(t => t.Matches.Any(m => m.ModIds.Contains(modId)))
                .Select(t => t.Phrase)
                .ToList();
            var lines = included
                .SelectMany(t => t.Matches.Where(m => m.ModIds.Contains(modId)))
                .Select(m => new ExplainLine { Class = m.Class, Line = m.Line })
                .Distinct()
                .OrderBy(l => l.Class, StringComparer.Ordinal)
                .ThenBy(l => l.Line, StringComparer.Ordinal)
                .ToList();

            result.Add(new ExplainMod
            {
                Id = mod.Id,
                Name = mod.Name,
                Classes = mod.Classes.OrderBy(x => x, StringComparer.Ordinal).ToList(),
                Phrases = phrases,
                Lines = lines,
            });
        }
        return result;
    }

    /// <summary>
    /// Splits a query into its quoted blocks (and any bare text), preserving the
    /// leading <c>!</c> exclusion flag. Quotes may contain spaces; unquoted spaces
    /// separate tokens.
    /// </summary>
    public static List<(string Phrase, bool Excluded)> Tokenize(string query)
    {
        var tokens = new List<(string, bool)>();
        var i = 0;
        while (i < query.Length)
        {
            if (char.IsWhiteSpace(query[i]))
            {
                i++;
                continue;
            }

            var excluded = false;
            if (query[i] == '!')
            {
                excluded = true;
                i++;
            }

            if (i < query.Length && query[i] == '"')
            {
                var end = query.IndexOf('"', i + 1);
                if (end < 0) end = query.Length;
                tokens.Add((query.Substring(i + 1, end - i - 1), excluded));
                i = end + 1;
            }
            else
            {
                var start = i;
                while (i < query.Length && !char.IsWhiteSpace(query[i])) i++;
                tokens.Add((query.Substring(start, i - start), excluded));
            }
        }
        return tokens;
    }

    /// <summary>Treats the digit placeholder as a digit-run wildcard in a phrase.</summary>
    public static string PhraseToRegex(string phrase)
    {
        var placeholder = new string(SuffixIndex.DigitPlaceholder, 1);
        return phrase.IndexOf(SuffixIndex.DigitPlaceholder) < 0 ? phrase : phrase.Replace(placeholder, @"\d+");
    }
}

public sealed class ExplainResult
{
    public required string Query { get; init; }
    public required string Game { get; init; }
    public required IReadOnlyList<ExplainClass> Classes { get; init; }
    public required IReadOnlyList<ExplainToken> Tokens { get; init; }
    public required IReadOnlyList<ExplainMod> Mods { get; init; }
}

public sealed class ExplainClass
{
    public required string Id { get; init; }
    public required string Name { get; init; }
}

public sealed class ExplainToken
{
    public required int Index { get; init; }
    public required string Phrase { get; init; }
    public required bool Excluded { get; init; }
    /// <summary>included | excluded | class | keyword</summary>
    public required string Kind { get; init; }
    public required IReadOnlyList<string> Classes { get; init; }
    public required IReadOnlyList<ExplainMatch> Matches { get; init; }
}

public sealed class ExplainMatch
{
    public required string Class { get; init; }
    public required string Line { get; init; }
    public required string Pattern { get; init; }
    public required IReadOnlyList<string> ModIds { get; init; }
}

public sealed class ExplainMod
{
    public required string Id { get; init; }
    public string? Name { get; init; }
    public required IReadOnlyList<string> Classes { get; init; }
    /// <summary>Included phrases that matched this mod.</summary>
    public required IReadOnlyList<string> Phrases { get; init; }
    public required IReadOnlyList<ExplainLine> Lines { get; init; }
}

public sealed class ExplainLine
{
    public required string Class { get; init; }
    public required string Line { get; init; }
}

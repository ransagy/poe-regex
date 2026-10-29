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

        // Scope: an explicit class is authoritative; otherwise explain over
        // the distinct global union instead of scanning every class twice.
        var scope = itemClass is not null
            ? new List<string> { itemClass }
            : _corpus.InGameClasses.Select(c => c.Id).OrderBy(x => x, StringComparer.Ordinal).ToList();

        // Pass 1: classify each token. A token is a class anchor only when it
        // matches a class line AND no mod line in scope — mod fragments such as
        // "ex" can coincidentally appear in a class name, so matching any mod
        // line takes precedence.
        var kinds = new string[raw.Count];
        var matchesByIndex = new List<ExplainMatch>[raw.Count];
        for (var i = 0; i < raw.Count; i++)
        {
            var (phrase, excluded) = raw[i];
            var matches = MatchLines(phrase, itemClass);
            matchesByIndex[i] = matches;
            if (excluded)
            {
                kinds[i] = "excluded";
                continue;
            }
            if (matches.All(m => m.ModIds.Count == 0) && MatchedClasses(phrase).Count > 0)
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

    private List<ExplainMatch> MatchLines(string phrase, string? itemClass)
    {
        var pattern = PhraseToRegex(phrase);
        System.Text.RegularExpressions.Regex regex;
        try
        {
            regex = new System.Text.RegularExpressions.Regex(
                pattern,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase |
                System.Text.RegularExpressions.RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1));
        }
        catch (ArgumentException)
        {
            return [];
        }

        var hint = LiteralHint(phrase);
        var lines = _corpus.ExplainLines(itemClass);
        var matches = new List<ExplainMatch>();
        foreach (var (linePattern, real) in lines)
        {
            if (hint is not null &&
                real.IndexOf(hint, StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            if (!regex.IsMatch(real)) continue;
            var modIds = _corpus.ModsWithPattern(linePattern)
                .Where(m => itemClass is null || m.Classes.Contains(itemClass))
                .Select(m => m.Id)
                .Distinct()
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToList();
            matches.Add(new ExplainMatch
            {
                Class = itemClass ?? "",
                Line = real,
                Pattern = linePattern,
                ModIds = modIds,
            });
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

    /// <summary>
    /// Longest literal run in a query phrase. Used as a safe pre-filter before
    /// the regex scan; a line that does not contain the run cannot match.
    /// </summary>
    private static string? LiteralHint(string phrase)
    {
        // These constructs can make a literal optional or alternative; a
        // single-substring pre-filter would then reject valid lines.
        for (var i = 0; i < phrase.Length; i++)
        {
            if (phrase[i] == '\\' && i + 1 < phrase.Length) { i++; continue; }
            if (phrase[i] is '?' or '{' or '[' or '(' or ')' or '|') return null;
        }
        var best = "";
        var current = new System.Text.StringBuilder();
        void Flush()
        {
            if (current.Length > best.Length &&
                current.ToString().Any(char.IsLetterOrDigit))
                best = current.ToString();
            current.Clear();
        }
        for (var i = 0; i < phrase.Length; i++)
        {
            var c = phrase[i];
            if (c == '\\' && i + 1 < phrase.Length)
            {
                var n = phrase[i + 1];
                if (n is 'd' or 'D' or 'w' or 'W' or 's' or 'S')
                {
                    Flush();
                    i++;
                    continue;
                }
                current.Append(n);
                i++;
                continue;
            }
            if (c is '.' or '^' or '$' or '*' or '+' or '?' or '(' or ')' or
                '[' or ']' or '{' or '}' or '|')
            {
                Flush();
                continue;
            }
            current.Append(c);
        }
        Flush();
        return best.Length >= 2 ? best.Trim() : null;
    }

    private HashSet<string> MatchedClasses(string phrase)
    {
        var regex = PhraseToRegex(phrase);
        var result = new HashSet<string>();
        foreach (var cls in _corpus.InGameClasses)
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

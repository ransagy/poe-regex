using PoeRegex.Core;
using PoeRegex.Core.Compilation;
using PoeRegex.Core.Data;

namespace PoeRegex.Cli;

internal static class Program
{
    private const int ExitOk = 0;
    private const int ExitError = 1;
    private const int ExitUsage = 2;

    private static EngineCorpus LoadCorpus(GameTarget game, string? catalogPath)
    {
        ICatalogSource source = catalogPath is null
            ? new EmbeddedCatalogSource()
            : new FileCatalogSource(catalogPath);
        try
        {
            return new EngineCorpus(source.Load(game));
        }
        catch (FileNotFoundException ex)
        {
            Console.Error.WriteLine($"catalog not found: {ex.FileName} (use --catalog)");
            Environment.Exit(ExitError);
            throw;
        }
        catch (InvalidOperationException ex) when (catalogPath is null)
        {
            Console.Error.WriteLine(ex.Message);
            Environment.Exit(ExitError);
            throw;
        }
    }

    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
    };

    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            return RunInteractive();
        }

        return args[0] switch
        {
            "generate" => RunGenerate(args[1..]),
            "list-classes" => RunListClasses(args[1..]),
            "list-mods" => RunListMods(args[1..]),
            "validate" => RunValidate(args[1..]),
            "-h" or "--help" or "help" => PrintHelp(),
            _ => Usage($"unknown command '{args[0]}'"),
        };
    }

    private static int PrintHelp()
    {
        Console.WriteLine("""
            Poe Regex Compiler

            usage:
              poe-regex                          interactive
              poe-regex generate --game <poe1|poe2> [--catalog <path>] [--class <id>]
                  [--include <mod-or-text>]... [--min <text:value>]...
                  [--exclude <text>]... [--ilvl <n>] [--keyword <kw>]...
                  [--clipboard] [--json] [--allow-overflow]
              poe-regex list-classes --game <poe1|poe2> [--catalog <path>] [--json]
              poe-regex list-mods --game <poe1|poe2> [--catalog <path>] [--class <id>]
                  [--search <text>] [--limit <n>] [--json]
              poe-regex validate --game <poe1|poe2> [--catalog <path>] --query "<regex>" [--json]

            --json: emit machine-readable JSON on stdout (suitable for other
            programs and scripts); human text otherwise.

            exit codes: 0 ok, 1 error, 2 usage
            """);
        return ExitOk;
    }

    private static int Usage(string message)
    {
        Console.Error.WriteLine(message);
        PrintHelp();
        return ExitUsage;
    }

    // ---- generate ----

    private static int RunGenerate(string[] args)
    {
        var opts = new Dictionary<string, List<string>>();
        var minValues = new List<(string Text, double Value)>();
        var game = GameTarget.PoE1;
        string? catalogPath = null;
        string? itemClass = null;
        var includes = new List<ModifierRequirement>();
        var excludes = new List<ModifierRequirement>();
        var keywords = new List<string>();
        var clipboard = false;
        var json = false;
        var allowOverflow = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--game":
                    game = ParseGame(GetValue(args, ref i, "--game"));
                    break;
                case "--catalog":
                    catalogPath = GetValue(args, ref i, "--catalog");
                    break;
                case "--class":
                    itemClass = GetValue(args, ref i, "--class");
                    break;
                case "--include":
                    includes.Add(new ModifierRequirement(GetValue(args, ref i, "--include")));
                    break;
                case "--min":
                {
                    var raw = GetValue(args, ref i, "--min");
                    var sep = raw.LastIndexOf(':');
                    if (sep < 0) return Usage($"--min expects 'text:value', got '{raw}'");
                    var text = raw[..sep];
                    if (!double.TryParse(raw[(sep + 1)..], System.Globalization.CultureInfo.InvariantCulture, out var v))
                        return Usage($"--min value must be numeric, got '{raw[(sep + 1)..]}'");
                    includes.Add(new ModifierRequirement(text, v));
                    break;
                }
                case "--exclude":
                    excludes.Add(new ModifierRequirement(GetValue(args, ref i, "--exclude")));
                    break;
                case "--ilvl":
                    keywords.Add($"ilvl:{GetValue(args, ref i, "--ilvl")}");
                    break;
                case "--keyword":
                    keywords.Add(GetValue(args, ref i, "--keyword"));
                    break;
                case "--clipboard":
                    clipboard = true;
                    break;
                case "--json":
                    json = true;
                    break;
                case "--allow-overflow":
                    allowOverflow = true;
                    break;
                default:
                    return Usage($"unknown option '{args[i]}'");
            }
        }

        if (includes.Count == 0 && keywords.Count == 0)
        {
            return Usage("generate requires at least one --include or --keyword");
        }

        var corpus = LoadCorpus(game, catalogPath);
        var compiler = new PoeRegexCompiler(corpus, PoeRegexCompiler.StrategyFor(game));
        var spec = QuerySpecification.For(game) with
        {
            ItemClass = itemClass,
            IncludedModifiers = includes,
            ExcludedModifiers = excludes,
            Keywords = keywords,
            AllowOverflow = allowOverflow,
        };

        try
        {
            var result = compiler.Compile(spec);
            if (clipboard)
            {
                try
                {
                    TextCopy.ClipboardService.SetText(result.Query);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"warning: clipboard unavailable: {ex.Message}");
                }
            }
            if (json) EmitJson(result);
            else EmitText(result);
            return result.Exceeded ? ExitError : ExitOk;
        }
        catch (RegexLengthOverflowException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return ExitError;
        }
    }

    private static void EmitText(CompileResult result)
    {
        Console.WriteLine(result.Query);
        Console.WriteLine($"# {result.Length}/{result.Limit} characters{(result.Exceeded ? " (OVER LIMIT)" : "")}");
        foreach (var d in result.Diagnostics) Console.WriteLine($"# [{d.Level}] {d.Code}: {d.Message}");
    }

    private static void EmitJson(CompileResult result)
    {
        var payload = new JsonEmitResult
        {
            Query = result.Query,
            Length = result.Length,
            Limit = result.Limit,
            Exceeded = result.Exceeded,
            Diagnostics = result.Diagnostics.Select(d => new JsonEmitDiagnostic
            {
                Level = d.Level.ToString().ToLowerInvariant(),
                Code = d.Code,
                Message = d.Message,
            }).ToList(),
        };
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(payload, new CliJsonContext(JsonOptions).JsonEmitResult));
    }

    // ---- list-classes ----

    private static int RunListClasses(string[] args)
    {
        var game = GameTarget.PoE1;
        string? catalogPath = null;
        var json = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--game": game = ParseGame(GetValue(args, ref i, "--game")); break;
                case "--catalog": catalogPath = GetValue(args, ref i, "--catalog"); break;
                case "--json": json = true; break;
                default: return Usage($"unknown option '{args[i]}'");
            }
        }
        var corpus = LoadCorpus(game, catalogPath);
        var classes = corpus.Catalog.ItemClasses.OrderBy(c => c.Id, StringComparer.Ordinal).ToList();
        if (json)
        {
            var payload = classes.Select(c => new JsonClassEntry { Id = c.Id, Name = c.Name }).ToList();
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(payload, new CliJsonContext(JsonOptions).ListJsonClassEntry));
        }
        else
        {
            foreach (var cls in classes) Console.WriteLine($"{cls.Id}\t{cls.Name}");
        }
        return ExitOk;
    }

    // ---- list-mods ----

    private static int RunListMods(string[] args)
    {
        var game = GameTarget.PoE1;
        string? catalogPath = null;
        string? itemClass = null;
        string? search = null;
        var limit = 200;
        var json = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--game": game = ParseGame(GetValue(args, ref i, "--game")); break;
                case "--catalog": catalogPath = GetValue(args, ref i, "--catalog"); break;
                case "--class": itemClass = GetValue(args, ref i, "--class"); break;
                case "--search": search = GetValue(args, ref i, "--search"); break;
                case "--json": json = true; break;
                case "--limit":
                    if (!int.TryParse(GetValue(args, ref i, "--limit"), out limit)) return Usage("--limit must be numeric");
                    break;
                default: return Usage($"unknown option '{args[i]}'");
            }
        }

        var corpus = LoadCorpus(game, catalogPath);
        var mods = corpus.Mods.AsEnumerable();
        if (itemClass is not null) mods = mods.Where(m => m.Classes.Contains(itemClass));
        var list = mods.Where(m => search is null || Matches(m, search))
                       .OrderBy(m => m.Name ?? m.Id, StringComparer.Ordinal)
                       .Take(limit)
                       .ToList();
        if (json)
        {
            var payload = list.Select(m => new JsonModEntry
            {
                Id = m.Id,
                Name = m.Name,
                Line = m.StatLines.FirstOrDefault()?.Line ?? "",
            }).ToList();
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(payload, new CliJsonContext(JsonOptions).ListJsonModEntry));
        }
        else
        {
            foreach (var m in list)
            {
                var first = m.StatLines.FirstOrDefault()?.Line ?? "";
                Console.WriteLine($"{m.Id}\t{m.Name ?? ""}\t{first}");
            }
        }
        return ExitOk;
    }

    private static bool Matches(ModEntry mod, string search)
    {
        if ((mod.Name ?? "").Contains(search, StringComparison.OrdinalIgnoreCase)) return true;
        if (mod.Id.Contains(search, StringComparison.OrdinalIgnoreCase)) return true;
        return mod.StatLines.Any(sl => sl.Line.Contains(search, StringComparison.OrdinalIgnoreCase));
    }

    // ---- validate ----

    private static int RunValidate(string[] args)
    {
        var game = GameTarget.PoE1;
        string? catalogPath = null;
        string? query = null;
        var json = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--game": game = ParseGame(GetValue(args, ref i, "--game")); break;
                case "--catalog": catalogPath = GetValue(args, ref i, "--catalog"); break;
                case "--query": query = GetValue(args, ref i, "--query"); break;
                case "--json": json = true; break;
                default: return Usage($"unknown option '{args[i]}'");
            }
        }
        if (query is null) return Usage("validate requires --query");

        var strategy = PoeRegexCompiler.StrategyFor(game);
        var diag = strategy.ValidateFragment(query);
        if (json)
        {
            var payload = new JsonValidateResult
            {
                Ok = true,
                Game = game.ToRuleKey(),
                Warning = diag is null ? null : new JsonEmitDiagnostic
                {
                    Level = diag.Level.ToString().ToLowerInvariant(),
                    Code = diag.Code,
                    Message = diag.Message,
                },
            };
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(payload, new CliJsonContext(JsonOptions).JsonValidateResult));
        }
        else
        {
            if (diag is not null)
            {
                Console.WriteLine($"warning: {diag.Code}: {diag.Message}");
            }
            Console.WriteLine($"ok: query is syntactically acceptable for {game.ToRuleKey()}");
        }
        return ExitOk;
    }

    // ---- interactive ----

    private static int RunInteractive()
    {
        Console.WriteLine("Poe Regex Compiler (interactive)");
        Console.WriteLine("Pick a game:");
        Console.WriteLine("  1) Path of Exile 1");
        Console.WriteLine("  2) Path of Exile 2");
        var gameChoice = Console.ReadLine()?.Trim();
        var game = gameChoice == "2" ? GameTarget.PoE2 : GameTarget.PoE1;
        var corpus = LoadCorpus(game, null);

        Console.WriteLine("Item class (enter to skip, '?' to list):");
        var classLine = Console.ReadLine()?.Trim();
        string? itemClass = null;
        if (classLine == "?")
        {
            foreach (var cls in corpus.Catalog.ItemClasses.OrderBy(c => c.Id, StringComparer.Ordinal))
                Console.WriteLine($"  {cls.Id}\t{cls.Name}");
            Console.WriteLine("Item class:");
            classLine = Console.ReadLine()?.Trim();
        }
        if (!string.IsNullOrEmpty(classLine)) itemClass = classLine;

        var includes = new List<ModifierRequirement>();
        Console.WriteLine("Modifiers to include (free text or mod id; empty to finish):");
        while (true)
        {
            Console.Write("  include> ");
            var line = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(line)) break;
            var sep = line.LastIndexOf(':');
            if (sep > 0 && double.TryParse(line[(sep + 1)..], System.Globalization.CultureInfo.InvariantCulture, out var v))
            {
                includes.Add(new ModifierRequirement(line[..sep], v));
            }
            else
            {
                includes.Add(new ModifierRequirement(line));
            }
        }

        var excludes = new List<ModifierRequirement>();
        Console.WriteLine("Modifiers to exclude (empty to finish):");
        while (true)
        {
            Console.Write("  exclude> ");
            var line = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(line)) break;
            excludes.Add(new ModifierRequirement(line));
        }

        var compiler = new PoeRegexCompiler(corpus, PoeRegexCompiler.StrategyFor(game));
        var spec = QuerySpecification.For(game) with
        {
            ItemClass = itemClass,
            IncludedModifiers = includes,
            ExcludedModifiers = excludes,
        };
        try
        {
            var result = compiler.Compile(spec);
            Console.WriteLine();
            Console.WriteLine(result.Query);
            Console.WriteLine($"# {result.Length}/{result.Limit} characters");
            foreach (var d in result.Diagnostics) Console.WriteLine($"# [{d.Level}] {d.Code}: {d.Message}");
            return ExitOk;
        }
        catch (RegexLengthOverflowException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return ExitError;
        }
    }

    // ---- helpers ----

    private static GameTarget ParseGame(string value)
    {
        try { return GameTargetExtensions.Parse(value); }
        catch { Console.Error.WriteLine($"invalid game '{value}'"); Environment.Exit(ExitUsage); throw; }
    }

    private static string GetValue(string[] args, ref int i, string option)
    {
        if (i + 1 >= args.Length)
        {
            Console.Error.WriteLine($"missing value for {option}");
            Environment.Exit(ExitUsage);
        }
        return args[++i];
    }
}

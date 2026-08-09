# Using the Engine in Your Own Project

`PoeRegex.Core` is a self-contained .NET class library: give it a catalog and a
description of the trade-filter you want, and it returns a short, in-game-safe
regex query (PoE 1 and PoE 2). This guide covers everything from adding the
reference to extending the engine.

## What the engine does

- Resolves modifier requirements (catalog mod ids or free text) to the shortest
  unique fingerprint per item class.
- Applies value floors (`--min`) as digit-range patterns.
- ANDs quoted blocks, merges included mods into one `|` alternation, factors
  common prefixes, and emits `!"..."` exclusions.
- Enforces the game's character limit (250 for both games by default) and
  validates fragments against the per-game grammar.

Everything is deterministic: the same input always yields the same output.

## 1. Reference it

The library targets `net10.0` and ships the catalogs as embedded resources, so
referencing the project is all you need:

```xml
<ItemGroup>
  <ProjectReference Include="..\PoeRegex.Core\PoeRegex.Core.csproj" />
</ItemGroup>
```

It has no external package dependencies (only the BCL). You can also take the
project's source or build it into your own NuGet package; the assembly is
marked `IsAotCompatible`, so it works inside Native AOT consumers too.

## 2. The five-step pipeline

The engine is a small facade around three types: a **corpus** (in-memory index
over a catalog), a **strategy** (per-game grammar), and the **compiler** that
combines them.

```csharp
using PoeRegex.Core;
using PoeRegex.Core.Compilation;
using PoeRegex.Core.Data;

// 1. Load a catalog (embedded = zero config).
var catalog = new EmbeddedCatalogSource().Load(GameTarget.PoE1);

// 2. Build the in-memory index. This is the expensive step; build once.
var corpus = new EngineCorpus(catalog);

// 3. Pick the strategy for the game. This also carries the game's rules.
var compiler = new PoeRegexCompiler(corpus, PoeRegexCompiler.StrategyFor(GameTarget.PoE1));

// 4. Describe what you want.
var spec = QuerySpecification.For(GameTarget.PoE1) with
{
    ItemClass = "Ring",
    IncludedModifiers = [new ModifierRequirement("IncreasedLife1", 80)],
    ExcludedModifiers = [new ModifierRequirement("reflect")],
    Keywords = ["ilvl:60"],
};

// 5. Compile.
var result = compiler.Compile(spec);
Console.WriteLine(result.Query);
```

The demo above produces (deterministically):

```text
"\+(\d{3,}|[9-9]\d|8[0-9]) to maximum Life" !"reflect" ":" "ilvl:60"
```

Read the query: a life mod at least `+80`, no "reflect" text, on the Ring class
(`":"` is Ring's fingerprint), requiring item level 60.

## 3. Building a `QuerySpecification`

`QuerySpecification.For(target)` gives a blank spec (all collection props
default to empty) that you fill in via `with` expressions or object-initializer
style.

### `ModifierRequirement(Value, MinValue?)`

Each include/exclude is either a **catalog mod id** or **free text**; the
compiler decides by looking the value up in the corpus:

- **Mod id** (e.g. `"IncreasedLife1"`) — resolved against the catalog and
  emitted as the shortest unique fingerprint. With a `MinValue` (e.g. `80`), the
  fingerprint becomes a digit-range pattern that matches every roll ≥ 80.
  Use `list-mods` (or the `EngineCorpus` API below) to discover ids.
- **Free text** (e.g. `"reflect"`) — validated against the game's grammar, then
  regex-escaped so it matches literally. Unresolved text is never silently
  changed; it becomes a literal block.

`MinValue` is a `double` (mod values like `1.5%` exist), and the emitted pattern
is verified against every real roll of that line in the corpus.

### `ItemClass`

Set to a class id from the catalog (e.g. `"Ring"`, `"Body Armour"`) to anchor
the query to that item base. `null` (the default) omits the class anchor. An
unknown class id produces a `unresolved-class` warning diagnostic and is skipped.

### `ExcludedModifiers`

Each becomes its own `!"..."` block. Exclusion supports the same mod-id / free
text resolution as includes (minus the value floor).

### `Keywords`

Custom keyword prefixes the game supports (e.g. `"ilvl:60"`, `"str:20"`). Each
is validated then emitted as a quoted block. `GameRules.CustomKeywords` lists
what the strategy knows about.

### `AllowOverflow`

By default a query over the game's character limit throws
`RegexLengthOverflowException`. Set `AllowOverflow = true` to receive a
best-effort result instead (check `CompileResult.Exceeded`).

## 4. Reading `CompileResult`

```csharp
result.Query          // the regex string, ready for the trade site
result.Length         // query.Length
result.Limit          // the game's character limit (GameRules.MaxCharacterLimit)
result.Exceeded       // Length > Limit (only reachable with AllowOverflow)
result.Diagnostics    // non-fatal messages
```

Diagnostics are `CompilerDiagnostic(Level, Code, Message)` with `Level` one of
`Info | Warning | Error`. Codes are stable and prefixed by cause:

- `unresolved-modifier` — a mod id (or free text) could not be resolved
- `unresolved-class` — item class id not in the catalog
- grammar warnings from the strategy (e.g. unsupported lookahead on PoE 1)

Warnings do not fail the compile; they describe what was skipped or relaxed.
Fatal problems throw:

- `RegexLengthOverflowException(RequestedLength, Limit)` — query too long and
  `AllowOverflow` is false. Catch it and tell the user to add exclusions or drop
  mods.

## 5. Catalog sources

Catalogs are loaded through `ICatalogSource`:

```csharp
public interface ICatalogSource
{
    Catalog Load(GameTarget game);
}
```

- **`EmbeddedCatalogSource`** — reads the catalog baked into the assembly.
  Zero config, works in AOT, recommended for apps that ship the standard
  catalogs.
- **`FileCatalogSource(path)`** — reads a JSON file. `{game}` in the path is
  substituted with the game's key (`poe1`/`poe2`), so one source can serve both:
  `new FileCatalogSource("data/{game}.catalog.json")`.
- **Your own** — implement `ICatalogSource` to load from a database, an API,
  or a custom file format. `Catalog` is a plain POCO (`Game`, `DataVersion`,
  `ItemClasses`, `Mods`) and is serializable via
  `CatalogJsonContext.Default.Catalog` (source-generated, AOT-safe).

## 6. Discovering ids and classes

`EngineCorpus` doubles as the data-access API:

```csharp
corpus.Catalog.ItemClasses           // all classes (Id, Name, BaseFingerprint)
corpus.Mods                          // all mods (Id, Name, GenerationType, Classes, StatLines)
corpus.FindItemClass("Ring")         // ItemClassEntry?
corpus.FindMod("IncreasedLife1")     // ModEntry?
corpus.ClassesForMod("IncreasedLife1") // classes the mod can appear on

// Fingerprints (what the compiler uses):
corpus.ClassFingerprint("Ring")                       // e.g. ":"
corpus.Fingerprint("IncreasedLife1", "<pattern>", "Ring")

// False-positive oracle — is this fragment safe on this class?
corpus.FragmentSafeOnClass("Ring", "+9 to maximum Life", [linePattern])
corpus.RegexSafeOnClass("Ring", regexFragment, [allowedPatterns])
```

A `ModEntry` exposes its `StatLines` (`Line`, `Pattern`, `MinValue`,
`MaxValue`, `Fingerprints`) — useful for building your own UI, e.g. showing the
mod text a fingerprint was derived from.

## 7. Custom strategies

`ICompilerStrategy` encapsulates the per-game grammar:

```csharp
public interface ICompilerStrategy
{
    GameRules GameRules { get; }
    CompilerDiagnostic? ValidateFragment(string fragment);
    string EscapeLiteral(string text);
    string FormatExclusion(string term);
    string Quote(string term);
}
```

The shipped strategies are `PathOfExileOneStrategy` and
`PathOfExileTwoStrategy` (both subclass `CompilerStrategyBase`). To tailor
grammar, subclass the base and override what differs:

```csharp
public sealed class MyLeagueStrategy : CompilerStrategyBase
{
    public override GameRules GameRules { get; } = GameRules.For(GameTarget.PoE2) with
    {
        MaxCharacterLimit = 300,
    };

    // Required: the base does not provide a default exclusion wrapper.
    public override string FormatExclusion(string term) => $"!{term}";
}
```

`GameRules` also supports an on-disk override at load:
`GameRules.Load(target, "rules.json")` — a partial JSON of `GameRules` fields is
merged on top of the defaults. This lets operators tune limits without
recompiling.

## 8. Regenerating catalogs

Catalogs are derived from RePoE exports by `tools/PoeRegex.Data.Build`
(dev-time only; the tool is not part of the engine). After a league patch:

```sh
dotnet run --project tools/PoeRegex.Data.Build -- fetch --game poe1 --out data/raw/poe1
dotnet run --project tools/PoeRegex.Data.Build -- build --game poe1 --raw data/raw/poe1 --catalog data/catalogs/poe1.catalog.json
```

Run `build` for each game, then rebuild/re-embed the `PoeRegex.Core` assembly so
`EmbeddedCatalogSource` picks up the new data. For details on the pipeline,
inputs, and the fingerprinting algorithm, see [`DESIGN.md`](DESIGN.md).

## 9. Performance notes

- `EngineCorpus` builds per-class suffix indexes at construction — the only
  heavy step. **Construct once and reuse.** It is read-only after construction
  and safe to share across threads; `PoeRegexCompiler` is stateless, so one
  instance can compile concurrently.
- Compilation is string-building over precomputed indexes: microseconds per
  query, independent of catalog size.
- Catalog JSON is ~0.5–1.2 MB per game; the embedded copy adds roughly that to
  the assembly (and the AOT binary).

## 10. Native AOT consumers

The library is `IsAotCompatible` and uses source-generated JSON
(`CatalogJsonContext`), so it AOT-compiles cleanly:

```sh
dotnet publish src/YourApp -c Release -r linux-x64 -p:PublishAot=true
```

No reflection-based serialization paths exist in the engine. (The CLI tool also
ships as AOT binaries — see [`BUILDING.md`](BUILDING.md).)

## 11. Licensing & data

The engine and catalogs ship under MIT. The catalog content is community
extraction of Grinding Gear Games-owned game data (via RePoE); see `NOTICE` for
the exact terms, and [`DESIGN.md`](DESIGN.md) §1 for the sourcing policy. If you
redistribute catalogs, keep the attribution in `NOTICE` intact.

## Cookbook

A life-on-rings query with a floor and an ilvl keyword (PoE 1):

```csharp
var spec = QuerySpecification.For(GameTarget.PoE1) with
{
    ItemClass = "Ring",
    IncludedModifiers = [new ModifierRequirement("IncreasedLife1", 80)],
    Keywords = ["ilvl:60"],
};
```

PoE 2, free-text only, no class anchor, never throw on overflow:

```csharp
var compiler = new PoeRegexCompiler(
    new EngineCorpus(new EmbeddedCatalogSource().Load(GameTarget.PoE2)),
    PoeRegexCompiler.StrategyFor(GameTarget.PoE2));

var result = compiler.Compile(QuerySpecification.For(GameTarget.PoE2) with
{
    IncludedModifiers = [new ModifierRequirement("nearby enemies"),
                         new ModifierRequirement("fire damage")],
    AllowOverflow = true,
});
if (result.Exceeded) Console.WriteLine("over budget");
```

Interactively discover a mod id, then build a query from it:

```csharp
var mod = corpus.Mods.FirstOrDefault(m =>
    m.Name != null && m.Name.Contains("Sanguine", StringComparison.OrdinalIgnoreCase));
if (mod is not null)
{
    var result = compiler.Compile(QuerySpecification.For(GameTarget.PoE1) with
    {
        ItemClass = "Ring",
        IncludedModifiers = [new ModifierRequirement(mod.Id)],
    });
    Console.WriteLine(result.Query);
}
```

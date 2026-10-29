# Using the Engine in Your Own Project

`PoeRegex.Core` is a self-contained .NET class library for PoE2. Give it the
embedded catalog and exact fragment table, describe the search you want, and
it returns a short, in-game-safe quoted regex query.

There are two ways to consume it:

| Approach | Audience | What you get |
|---|---|---|
| **Library** (`PoeRegex.Core`) | .NET apps | In-process compile and data access |
| **CLI subprocess** (`poe-regex ... --json`) | Any language | Same compiler over stdin/stdout |

## What the engine does

- Resolves modifier requirements (catalog mod ids or free text) to exact
  precomputed fragments.
- Applies value floors (`--min`) as digit-range patterns.
- ANDs quoted blocks, merges included mods into one `|` alternation, factors
  common prefixes, and emits `!"..."` exclusions.
- Enforces the 250-character PoE2 limit.
- Targets the advanced item-search text: ranged stats show `roll(min-max)`;
  normal display is presentation only. The shipped table declares
  `targetModel: advanced-ranges-v1`.
- Never searches for fragments at runtime. If the table is missing or stale,
  the affected modifier is reported as unresolved instead of falling back to a
  heuristic.

## 1. Reference it

```xml
<ItemGroup>
  <ProjectReference Include="..\PoeRegex.Core\PoeRegex.Core.csproj" />
</ItemGroup>
```

The library targets `net10.0`, has only BCL dependencies, and is
`IsAotCompatible`.

## 2. The pipeline

```csharp
using PoeRegex.Core;
using PoeRegex.Core.Compilation;
using PoeRegex.Core.Data;

// 1. Load the embedded PoE2 catalog and exact table.
var corpus = new EngineCorpus(new EmbeddedCatalogSource().Load());

// 2. Build the compiler once.
var compiler = new PoeRegexCompiler(corpus);

// 3. Describe what you want.
var spec = new QuerySpecification
{
    ItemClass = "Ring",
    IncludedModifiers = [new ModifierRequirement("IncreasedLife6", 80)],
    ExcludedModifiers = [new ModifierRequirement("reflect")],
    Keywords = ["ilvl:60"],
};

// 4. Compile.
var result = compiler.Compile(spec);
Console.WriteLine(result.Query);
```

Output is deterministic for the same input and catalog.

## 3. `QuerySpecification`

```csharp
public sealed record QuerySpecification
{
    public string? ItemClass { get; init; }
    public IReadOnlyList<ModifierRequirement> IncludedModifiers { get; init; }
    public IReadOnlyList<ModifierRequirement> ExcludedModifiers { get; init; }
    public IReadOnlyList<string> Keywords { get; init; }
    public bool AllowOverflow { get; init; }
}
```

`ModifierRequirement(Value, MinValue?)` accepts a catalog mod id or free text.

- **Mod id** — resolves to the exact precomputed fragment. With `MinValue`,
  the fragment becomes a digit-range pattern that matches every roll at or
  above the value.
- **Free text** — validated, escaped, and emitted as a literal quoted block.

`ItemClass` anchors the query to an item class. Unknown classes produce an
`unresolved-class` warning and are skipped.

`Keywords` are custom search prefixes such as `ilvl:60`, `str:20`, `dex:20`,
and `int:20`.

`AllowOverflow = true` returns a best-effort result instead of throwing when
the query exceeds 250 characters.

## 4. `CompileResult`

```csharp
result.Query          // ready for the in-game search box
result.Length         // query.Length
result.Limit          // 250
result.Exceeded       // Length > Limit (only with AllowOverflow)
result.Diagnostics    // warnings and unresolved diagnostics
```

Diagnostics use `CompilerDiagnostic(Level, Code, Message)` with levels
`Info | Warning | Error`. Stable codes include:

- `unresolved-modifier`
- `unresolved-class`
- `word-boundary`
- `query-shape`

Fatal overflow throws `RegexLengthOverflowException(RequestedLength, Limit)`
unless `AllowOverflow` is true.

## 5. Catalog sources

```csharp
public interface ICatalogSource
{
    Catalog Load();
}
```

- `EmbeddedCatalogSource` — the shipped PoE2 catalog, recommended.
- `FileCatalogSource(path)` — reads a catalog JSON file.
- Custom sources can load from a database, API, or another format. `Catalog`
  is a plain POCO and source-generated JSON is available through
  `CatalogJsonContext`.

The exact table is embedded separately. A catalog with a different
`DataVersion` disables the embedded table; the engine then reports unresolved
modifiers rather than guessing.

## 6. Discovering ids and classes

```csharp
corpus.Catalog.ItemClasses
corpus.Mods
corpus.FindItemClass("Ring")
corpus.FindMod("IncreasedLife6")
corpus.ClassesForMod("IncreasedLife6")
corpus.ClassNamePrefix("Ring")
```

Table access:

```csharp
corpus.HasPrecomputedFragments
corpus.PrecomputedDataVersion
corpus.PrecomputedGlobal
corpus.PrecomputedForClass("Ring")
corpus.TryPrecomputedFragment(pattern, "Ring", out var fragment)
corpus.PrecomputedFragments()
```

`PrecomputedFragments()` is the reverse index used by the web importer.

## 7. Threshold patterns

A `MinValue` produces a digit-range fragment from the catalog value span and
validates it against the real display shapes in the scope:

```csharp
new ModifierRequirement("IncreasedLife6", 80)
```

The resulting term matches every roll at or above 80, including the advanced
`roll(min-max)` display shape.

## 8. Paste composition

`PasteComposer` builds a multi-slot stash/vendor paste:

```csharp
var composer = new PasteComposer(corpus);
var paste = composer.Compose(
[
    new PasteSlotCriterion("Ring", [new ModifierRequirement("IncreasedLife6")], "ring"),
    new PasteSlotCriterion("Amulet", [], "amulet"),
], []);
```

The anchor lists every selected class; modded criteria become exclusion
groups. `validateRecall` is on by default and reports `paste-recall` /
`paste-anchor` diagnostics.

## 9. Explain

```csharp
var explainer = new Explainer(corpus);
var explanation = explainer.Explain(result.Query, "Ring");
```

`ExplainResult` reports token kinds, matching lines, candidate mods, and
per-token class sets. Explain is the one intentionally expensive operation;
the web app caches it and warms it after idle.

## 10. AOT

The library is `IsAotCompatible`, uses source-generated JSON, and has no
reflection-based serialization. Native AOT consumers work without extra
configuration.

## 11. CLI

```sh
poe-regex generate --class Ring --include "to maximum Life" \
  --min "increased rarity:30" --exclude reflect --ilvl 60 --clipboard
poe-regex list-classes
poe-regex list-mods --class Ring --search life
poe-regex validate --query '"to maximum Life"'
poe-regex explain --class Ring --query '"life" "I"'
```

Every command supports `--json`. The CLI emits exactly one JSON document on
stdout; diagnostics and errors go to stderr.

Example:

```sh
poe-regex generate --class Ring --include "to maximum Life" --json
```

```json
{
  "query": "\u0022: Ri\u0022 \u0022to maximum Life\u0022",
  "length": 24,
  "limit": 250,
  "exceeded": false,
  "diagnostics": []
}
```

## 12. Data rebuilds

```sh
dotnet run --project tools/PoeRegex.Data.Build -- fetch
dotnet run --project tools/PoeRegex.Data.Build -- build
dotnet run --project tools/PoeRegex.Data.Build -- corpus
dotnet run --project tools/PoeRegex.Data.Build -- matrix-solve --from-scratch
```

`matrix-solve` writes the exact table, matrix, proof, and status and fails if
a shorter legal form exists.

## 13. PoE1

PoE1 is not part of the current product. If it returns, it will be a separate
profile with its own catalog, table, proof, and query composer. The fragment
solver is game-agnostic; the old heuristic selector is gone.

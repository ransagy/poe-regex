# Poe2 Regex Compiler Engine — Design Specification

**Target stack:** .NET 10 (LTS), pure-BCL core (WASM-safe), CLI-first.
**License:** MIT (project); third-party components are all MIT or Apache-2.0
(see `NOTICE`). Catalog data is a derived subset of GGG-owned game data
extracted by RePoE — see `PROVENANCE.md`.
**Scope:** English client, PoE2 only. The web reference implementation
("Renly's Recipes") lives in `web/`, documented in `WEB-APP-PLAN.md` and
`DEPLOYING-WEB.md`.

---

## 1. Data & Licensing Policy

- **Upstream:** `repoe-fork/repoe` PoE2 exports under `/poe2`. The exports
  carry no version field; the catalog records the fetch date as `DataVersion`.
  Re-fetching is a repeatable build step, designed so league patches re-source
  cleanly. Full lineage and refresh procedure: `PROVENANCE.md`.
- **Distribution:** only **derived catalogs** ship (human-readable mod-text
  lines + precomputed fingerprints). Raw RePoE JSON is dev-only and
  gitignored. RePoE's license states that generated data files are owned by
  GGG and must be used per GGG's terms; the repo ships attribution and
  trademark/affiliation disclaimers in `NOTICE` and records the exact subset
  in `PROVENANCE.md`.
- **Grammar/validation sources:** the search grammar is treated as a
  functional fact of the game and described in our own words. No prose,
  example query strings, or snippets are reproduced from community wikis,
  forums, subreddits, or guide sites.
- **Why self-authored fixtures are sufficient:** the shortest unique fragment
  for a line is dictated by the catalog data, not creative authorship, so any
  correct implementation converges on the same functional output.

## 2. Repository Layout

```text
poe-regex/
  PoeRegex.slnx
  src/
    PoeRegex.Core/                 # engine, exact table lookup, assembly
    PoeRegex.Cli/                  # interactive + batch
    PoeRegex.Web.Core/             # browser-facing services
  tools/
    PoeRegex.Data.Build/           # ingestion + exact solver + artifacts
  tests/
    PoeRegex.Core.Tests/
    PoeRegex.Data.Build.Tests/
    PoeRegex.Web.Tests/
  data/
    raw/                           # gitignored RePoE downloads
    catalogs/                      # checked-in PoE2 catalog, vendor corpus,
                                   # safety fixtures, exact fragment table
  web/                             # "Renly's Recipes" Blazor WASM
  docs/
```

## 3. Data Pipeline (`tools/PoeRegex.Data.Build`, dev-time)

1. **Fetch** the PoE2 `.min.json` exports: `mods`, `base_items`,
   `item_classes`, `mods_by_base`, `stat_value_handlers`, and the
   `stat_translations/*` descriptions.
2. **Render mod text:** resolve each stat translation, apply index handlers
   and numeric formatting, and emit one copy-text line per stat. Multi-stat
   mods render multiple lines.
3. **Corpus construction:** map mods to item classes, build the catalog, and
   build the vendor/stash line corpus (regular, corrupted, essence, crafted,
   implicit, base properties, captions, requirements, sockets, usage).
4. **Exact solve:** for every pattern/scope, search the G1 grammar over the
   target and foreign lines and return the shortest legal quoted term.
5. **Emit artifacts:** catalog, vendor corpus, exact fragment table, review
   matrix, exact proof, and status.
6. **Ship only derived data:** no raw RePoE JSON is committed.

The exact solver is a product-state NFA search with a counterexample-guided
outer loop. It runs at data-build time, never in the browser.

## 4. Core Domain (`src/PoeRegex.Core`)

- `Catalog`, `ModEntry`, `StatLineEntry`, `ItemClassEntry`.
- `EngineCorpus` loads the embedded catalog and exact `FragmentTable`.
  It exposes table lookup, class anchors, explain/matching lines, and
  threshold validation. It does not search for fragments.
- `Poe2Syntax` owns the query dialect: quoted terms, `!` exclusions, the
  250-character limit, custom keywords, escaping, and `\b` validation.
- `QuerySpecification { ItemClass?, IncludedModifiers, ExcludedModifiers,
  Keywords, AllowOverflow }`; each modifier is a catalog mod id or free text,
  optionally with `MinValue`.
- `PoeRegexCompiler(corpus)` composes a query from table lookups and
  threshold patterns. `PasteComposer(corpus)` composes multi-slot pastes.
- `CompileResult { Query, Length, Limit, Diagnostics, Exceeded }`.
  `RegexLengthOverflowException` is thrown unless `AllowOverflow = true`.
- `ICatalogSource` abstracts the catalog source; `EmbeddedCatalogSource` is
  the shipped path, `FileCatalogSource` is for tools/tests.
- Missing or stale table entries produce `unresolved-modifier` diagnostics;
  there is no live heuristic fallback.

## 5. Grammar & Assembly

- **G1 fragment grammar:** literal pieces at any position, zero to three
  wildcards (`.`, `.*`, `.+`), optional `^`/`$`, and optional leading
  `\d`/`\d+`. No lookahead, character classes, alternation, or backreferences.
- **Semantics:** one quoted term is line-local; matching is case-insensitive.
- **Target text:** the in-game search matches the advanced item text.
  Ranged stats show `roll(min-max)`, fixed stats show the plain value; normal
  display is presentation only. The table declares
  `targetModel: advanced-ranges-v1`, and the engine rejects a mismatched table.
- **Assembly:** class anchor first (when present), one quoted block with `|`
  alternation over included fragments, separate `!"term"` exclusion blocks,
  and quoted keyword blocks, joined by spaces. Spaces inside a quote are
  literal; unquoted whitespace separates ANDed blocks.
- **Exactness:** the table is the only source of stat fragments. Each entry
  was proven shortest for its scope under G1 against the modeled corpus and
  safety fixtures.

## 6. Compaction Engine

- Table lookup per stat line; choose the shortest table form across a hybrid
  mod's lines and between class/global scopes.
- `MinValue` builds a digit-range alternation from the catalog value span and
  re-validates it against real display shapes.
- Common-prefix factorization via a trie reduces multi-mod alternations.
- Output is deterministic.
- The proof boundary: exact table forms are line-local and strict; unmodeled
  unique/socketable/base text remains outside the safety corpus.

## 7. CLI (`src/PoeRegex.Cli`)

- Bare run → interactive PoE2 flow: class → mods → thresholds → exclusions →
  query + `n/250` meter.
- Batch:
  `generate --class Helmet --include "to maximum life" --min "increased rarity:30" --exclude reflect --ilvl 60 [--clipboard] [--json]`
- Support: `list-classes`, `list-mods --class X`, `validate --query "..."`,
  `explain --class X --query "..."`.
- All commands accept `--json`: one JSON document on stdout, diagnostics on
  stderr. JSON is source-generated and AOT-safe.
- Clipboard via TextCopy. Exit codes 0/1/2.

## 8. Test Suite

- Grammar/query-shape validation.
- Length enforcement and overflow behavior.
- Exact table coverage, recall, zero foreign matches, and safety fixtures.
- Exact solver known-form and differential tests.
- Compiler/paste determinism, thresholds, exclusions, and explain
  reversibility.
- Web service and item-import/matching tests.
- CI also re-runs the exact matrix proof.

## 9. Future Profiles

PoE1 is out of scope for now. If it returns, it is a separate grammar/query
profile with its own catalog, table, proof, and status. The fragment solver
stays game-agnostic; only the profile and query composer change. PoE1 does not
reuse the old heuristic selector.

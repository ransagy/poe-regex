# Poe Regex Compiler Engine — Design Specification

**Target stack:** .NET 10 (LTS), pure-BCL core (WASM-safe), CLI-first.
**License:** MIT (project); third-party components are all MIT or Apache-2.0
(see `NOTICE`). Catalog data is a derived subset of GGG-owned game data
extracted by RePoE — see `PROVENANCE.md`.
**Scope:** English client, PoE2 and PoE1 normal equipment. PoE2 is the original
full profile; PoE1 adds a separate catalog, target model, corpus, exact table,
and query dialect while sharing the engine. The web reference implementation
("Renly's Tools") lives in `web/`, documented in `web/README.md` and
`DEPLOYING-WEB.md`; the CLI remains PoE2-only.

---

## 1. Data & Licensing Policy

- **Upstream:** `repoe-fork/repoe` PoE2 exports under `/poe2` and the PoE1 root
  export (index `poe1.html`). The exports carry no version field; each catalog
  records its snapshot date as `DataVersion`. Re-fetching is a repeatable build
  step, designed so league patches re-source cleanly. Full lineage and refresh
  procedure: `PROVENANCE.md`.
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
    catalogs/                      # checked-in catalogs, vendor corpora,
                                   # safety fixtures, exact fragment table
  web/                             # "Renly's Tools" Blazor WASM
  docs/
```

## 3. Data Pipeline (`tools/PoeRegex.Data.Build`, dev-time)

1. **Fetch** the PoE2 and PoE1 `.min.json` exports: `mods`, `base_items`,
   `item_classes`, `mods_by_base`, `stat_value_handlers`, and the
   `stat_translations/*` descriptions.
2. **Render mod text:** resolve each stat translation, apply index handlers
   and numeric formatting, and emit one copy-text line per stat. Multi-stat
   mods render multiple lines.
3. **Corpus construction:** map mods to item classes, build the catalog, and
   build the vendor/stash line corpus (regular, corrupted, essence, implicit,
   desecrated, base properties, captions, requirements, sockets, usage).
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
- `QuerySyntax` owns the per-game dialect: quoted terms, the exclusion shape
  (`"!term"` for both games), the 250-character limit, custom
  keywords, escaping, tokenization, and `\b` validation. `Poe2Syntax` is the
  PoE2-named facade; `EngineCorpus.Syntax` selects the loaded game.
- `QuerySpecification { ItemClass?, IncludedModifiers, RequiredGroups, ExcludedModifiers,
  Keywords, AllowOverflow }`; each modifier is a catalog mod id or free text,
  optionally with `MinValue`/`MaxValue`, or `RangeModIds` for one combined
  tier-range fragment.
- `StatConstraint` + `ConstraintCanonicalizer` normalize exact tiers,
  “or better”, family picks, and min/max into one lossless interval set.
  Discrete tiers and gaps stay separate; only overlapping spans merge.
- `CompactionEngine.PlanMany`/`PlanConstraint` is the single representation
  planner: full-family table fragment, proven range, or single-value form.
  Exact/up/family selections of the same tiers produce the same query,
  independent of selection order.
- `EngineService.PickIntervals` exposes the canonical discrete intervals
  for a UI pick. Labels and tier marks use it, so a non-consecutive set
  keeps its gaps instead of collapsing to min–max. Exact tier picks merge
  only within consecutive family runs. Exact toggles one tier: on a covered
  tier it splits/trims the range/up/family around it; on an excluded tier it
  clears the exclusion; on an unselected tier it adds exact. Exclude is the
  only tier action that creates exclusions. The up button is anchor-only:
  the anchor removes its up pick, a covered up tier replaces that up pick
  with this tier as the new anchor (the old lower part drops unless it is
  separately picked), a partial suffix is replaced by one up pick, and a
  family/range-covered suffix is disabled with a hint. A single min/max
  filter on the old up pick is copied to the replacement. Exact/up never
  create suffix exclusions. Every selected tier shows `✓` — explicit exact,
  range-covered, and up-covered alike; `+` is only for an unselected tier,
  and `⇄` only for a genuine covered claim where clicking switches to that
  tier. `−` belongs to exclude only. Range tiers keep the plain `range` note,
  up-covered tiers keep `≥ covered`, and the range `≥` button is a normal add
  action with a range title. Hover/focus tints the affected row and family
  status and shows a plain inline action hint (`+ add`, `− remove`,
  `⊘ exclude`, `⇄ switch`/`replace`) on the row with the action. Add is mod,
  remove/change is ember, exclude is no. No badges, rings, or underlines.
  All-tier selections stay as the player built them; the
  family row is the explicit conversion to the family pick. Natural bounds render as empty, and a family pick converts
  to a range pick on the first min/max entry.
- `EngineService` caches whole `Compile` and `Combine` results by semantic
  key and shares one `CompactionEngine` across the compiler, service, and
  paste composer, so a re-render or a paste does not re-plan or re-prove
  unchanged constraints. Range proofs use unsorted per-scope proof
  lines, a clean literal tail narrows the proof scan to lines containing
  that tail, and matched proof lines and safe roll hits are cached across
  interval unions. Global display shapes come from catalog forms instead of
  grouping the whole vendor corpus, and the pattern index is hand-built.
  Exact tier range fragments are precomputed into the shipped table
  (`RangeFragments`) by the data build, so the browser skips the first-use
  range proof and falls back to the runtime planner only for missing keys.
  The picker's action refresh suppresses the parent-driven duplicate render,
  and single-class anchor validation uses a literal fast path. These are
  representation-neutral performance changes, not alternate matching paths.
- `PoeRegexCompiler(corpus)` composes a query from table lookups and
  threshold/range patterns. `PasteComposer(corpus)` composes multi-slot pastes.
- `CompileResult { Query, Length, Limit, Diagnostics, Exceeded }`.
  `RegexLengthOverflowException` is thrown unless `AllowOverflow = true`.
- `ICatalogSource` abstracts the catalog source; `EmbeddedCatalogSource` is
  the shipped path, `FileCatalogSource` is for tools/tests.
- Missing or stale table entries produce `unresolved-modifier` diagnostics;
  there is no live heuristic fallback.

## 5. Grammar & Assembly

- **G1 fragment grammar (exact table):** literal pieces at any position, zero
  to three wildcards (`.`, `.*`, `.+`), optional `^`/`$`, and optional leading
  `\d`/`\d+`. No lookahead, character classes, alternation, or backreferences.
  Runtime range fragments are a separate, catalog-proven form: they may use
  character classes/ranges and alternation, and the value-aware proof rejects
  any candidate that matches a disallowed pattern or an out-of-span roll.
- **Semantics:** one quoted term is line-local; matching is case-insensitive.
  A family+range constraint stays in one quoted term: separate terms are
  ANDed across the whole item, so a bare range term would match another line.
- **Target text:** the in-game search matches the advanced item text.
  Ranged stats show `roll(min-max)`, fixed stats show the plain value; normal
  display is presentation only. The table declares
  `targetModel: advanced-ranges-v1`, and the engine rejects a mismatched table.
- **Assembly:** class anchor first (when present), one quoted block with `|`
  alternation over included fragments, separate `"!term"` exclusion blocks,
  and quoted keyword blocks, joined by spaces. Spaces inside a quote are
  literal; unquoted whitespace separates ANDed blocks. A tier range is one
  combined fragment (family text plus value class), never a separate term.
- **Exactness:** the table is the only source of stat fragments. Each entry
  was proven shortest for its scope under G1 against the modeled corpus and
  safety fixtures.

## 6. Compaction Engine

- Table lookup per stat line; choose the shortest table form across a hybrid
  mod's lines and between class/global scopes.
- `MinValue` builds a bounded range from the floor to the pattern's catalog
  maximum and proves it with the value-aware range oracle. `MaxValue` (with
  or without a floor) builds a bounded range; a floor alone keeps the
  shipped "this or higher" behavior without matching a longer line such as
  "25% increased Movement Speed while Sprinting".
- Tier ranges use one combined fragment: the first roll placeholder in the
  advanced display shape is replaced with the shortest safe value class, and
  the candidate is proven against the catalog by pattern and by every
  displayed roll in an allowed span, not only the representative high roll.
  The family text tail is anchored so a range endpoint inside
  `roll(low-high)` cannot stand in for the roll. Tier picks in a multi-value
  family are range-first for exact and up tiers; a single-value family keeps
  the shorter affix-name form. Full-family coverage uses the table fragment,
  so the first tier’s “or better” and the family pick are the same query.
- Factoring is token-aware: fragments containing classes or groups are left
  unfactored rather than risk a malformed split such as `1[(234]...`; literal
  prefixes still factor. Value classes compress consecutive digits into
  ranges and only add a group when the value alternates, so `1[234567]\d`
  becomes `1[2-7]\d` and a pure concatenation is not wrapped.
- Multi-slot paste criteria group by semantic key. Each slot is
  canonicalized first, so multiple exact tiers of one family become one range
  requirement. A tier range keys by stat pattern, clamped covered intervals,
  and min/max; cross-slot union merges the same anchor range only when the
  extra span does not exist for the narrower class. Different or gapped
  ranges stay separate.
- A finite integer roll set is synthesized through a digit trie (identical
  subtrees merged, consecutive digits compressed). For a fixed-tier family
  this turns 15–35 into `15|[23][05]`; the catalog proof still rejects any
  candidate that matches a roll outside the requested intervals.
- Candidate generation tries exact values, digit classes, fixed-width ranges,
  alternations, and decimal forms; the shortest proven candidate wins.
- Common-prefix factorization via a trie reduces multi-mod alternations.
- Output is deterministic; range candidates and catalog proofs are cached per
  pattern/span/scope.
- The proof boundary: exact table forms are line-local and strict; unmodeled
  unique/socketable/base text remains outside the safety corpus.

## 7. CLI (`src/PoeRegex.Cli`)

- The CLI is PoE2-only. Bare run → interactive flow: class → mods →
  thresholds → exclusions → query + `n/250` meter. The web workbench supports
  PoE2 and PoE1.
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

PoE1 is a separate game profile with its own catalog, target model, corpus,
table, proof, status, and query dialect. The normal-equipment MVP is shipping
with the current release; see [`POE1-SCOPE.md`](POE1-SCOPE.md). The
fragment solver stays game-agnostic; `GameData`, `EngineCorpus.Syntax`,
`GameDataLoader`, and `GameSession` provide the profile seam. PoE1 does not
reuse the old heuristic selector.

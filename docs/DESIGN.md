# Path of Exile Regex Compiler Engine — Design Specification

**Target stack:** .NET 10 (LTS), pure-BCL core (WASM-safe for a later web UI), CLI-first.
**License:** MIT (project); deps: xUnit (Apache 2.0), System.Text.Json (MIT), Awesome Assertions (Apache 2.0), TextCopy (MIT).
**Scope:** English client only. v0.1 = engine + CLI + test suite; web UI deferred.

---

## 1. Data & Licensing Policy

- **Upstream:** `repoe-fork/repoe` gh-pages exports — PoE 1 at root, PoE 2 under `/poe2`. Each export has a pinned game version recorded in the catalog (`DataVersion`). Re-fetching is a repeatable build step (`PoeRegex.Data.Build`), designed so league patches re-source cleanly.
- **Distribution:** only **derived catalogs** ship (human-readable mod-text lines + precomputed fingerprints). Raw RePoE JSON is dev-only and gitignored. This is tolerated community use, not clean compliance — the repo ships a `NOTICE` with RePoE attribution and GGG trademark/affiliation disclaimers.
- RePoE data files are GGG-owned per RePoE's `LICENSE.md`; the project deliberately distributes the minimal derived subset (short factual text + fingerprints) rather than raw extracted data.
- **Grammar/validation sources:** the trade-search regex grammar is treated as a *functional fact* of GGG's trade sites and is described in our own words only. No content (prose, example query strings, or snippets) is reproduced from community wikis, forums, subreddits, or guide sites. In particular: poewiki/poe2wiki content is **CC BY-NC-SA 3.0** (non-commercial + share-alike, incompatible with this MIT repo) and poe.re/poe2.re publish without a license — both are consulted at most for factual understanding by humans and never copied. Reddit threads and Maxroll/Mobalytics are treated as all-rights-reserved and unused. All query→expected-string test cases are self-authored, modeled on functional behavior.
- **Why self-authored fixtures are sufficient:** trade-search query strings are *functional optima*. The shortest unique fragment for a given mod line is dictated by the catalog data, not by creative authorship, so any correct implementation converges on the same output. Self-authored fixtures therefore naturally match what community tooling produces (independent creation, not reproduction), without copying anyone's expression.
- **Community contributions are welcome and covered:** direct contributions — new test cases, corrected cases, or bug reports of wrong output — come in via pull requests and are licensed in-bound under this project's MIT license. This is a permissive, standard inbound-licensing arrangement; contributors do not need a separate license grant. See `CONTRIBUTING.md` for how to add cases.

## 2. Repository Layout

```
poe-regex/
  PoeRegex.sln
  src/
    PoeRegex.Core/                 # engine, grammar, compaction, catalogs (net10.0, BCL-only)
    PoeRegex.Cli/                  # interactive + batch (net10.0)
  tools/
    PoeRegex.Data.Build/           # dev-time ingestion + fingerprint builder (net10.0)
  tests/
    PoeRegex.Core.Tests/
    PoeRegex.Data.Build.Tests/
  data/
    raw/                           # gitignored RePoE downloads
    catalogs/                      # checked-in, versioned: poe1.catalog.json, poe2.catalog.json
  docs/
    DESIGN.md
    BUILDING.md
    USING-THE-ENGINE.md
  NOTICE
  LICENSE
  README.md
```

For consuming `PoeRegex.Core` as a library in your own project, see
[`USING-THE-ENGINE.md`](USING-THE-ENGINE.md); for publishing the CLI, see
[`BUILDING.md`](BUILDING.md).

## 3. Data Pipeline (`tools/PoeRegex.Data.Build`, dev-time)

1. **Fetch** pinned `.min.json` files per game: `mods.json`, `stat_translations.json`, `stat_value_handlers.json`, `stats.json`, `base_items.json`, `item_classes.json`, `mods_by_base.json`. Deterministic, versioned; skips download if already cached at pinned version.
2. **Render mod text:** for each mod (explicit/prefix/suffix generation types), for each stat entry `(id, min, max)`: resolve the stat's translation template by index shape, apply `index_handlers` (e.g. MultiplyByHundred) and numeric formatting → one copy-text line per stat. Multi-stat mods render multiple lines.
3. **Corpus construction:** via `mods_by_base.json` + `base_items.json`, map mods → item class → their renderable lines. Class corpus = distinct mod lines for that class (digit-runs normalized) + the `Item Class: X` line. PoE 1 and PoE 2 corpora are never mixed.
4. **Fingerprinting:** shortest-unique-substring per line within its class corpus; class-line fingerprints computed over the union of class lines to avoid cross-class collisions. Digit-runs are treated as a single value token so fingerprints stay value-agnostic across tiers.
5. **Emit catalog:** compact JSON with `game`, `dataVersion`, `itemClasses`, `mods`, `statLines`. No raw game data. The catalog is the only game-data artifact the engine consumes, so upstream schema changes affect only the build tool, not the engine.

## 4. Core Domain (`src/PoeRegex.Core`)

- `GameTarget { PoE1, PoE2 }`
- `GameRules` (per game, from an embedded `rules.json` with an **optional on-disk override** so regex/limit rules can be updated without a recompile): `MaxCharacterLimit` (default 250 both), `SupportsLookahead` (PoE2 true, PoE1 false), `RequireQuotedBlocks` (true both), `SupportsModNameAnchors` (feature flag, off until in-game verified), `CustomKeywords` (`ilvl:`, `str:`, `dex:`, `int:` with spacing rules), `SocketSyntax`.
- `QuerySpecification { GameTarget, ItemClass?, IncludedModifiers, ExcludedModifiers, Keywords, AllowOverflow }` where each modifier requirement is either a catalog `ModId` or free text, optionally with `MinValue`.
- `ICompilerStrategy`: `Compile(spec, corpus)`, `ValidateFragment`, `FormatExclusion`, `GameRules`. Implemented by `PathOfExileOneStrategy` / `PathOfExileTwoStrategy`.
- `PoeRegexCompiler` facade → `CompileResult { Query, Length, Limit, Diagnostics, Exceeded }`. Throws `RegexLengthOverflowException` unless `AllowOverflow = true` (then best-effort with a diagnostic).
- Engine depends only on an `ICatalogSource` abstraction (file path in CLI; embedded resource in a future WASM build). No platform APIs, no filesystem access inside the engine beyond the injected source.

## 5. Grammar & Assembly

- **Shared base (both games):** literals, `|`, `+ * ? {m,n}`, `[a-z]`/`[0-9]`, `.`, `^`/`$` (per-line anchors), `\d \w \s`, `( )`, case-insensitive per-line matching.
- **PoE 2 (functional behavior of the trade site):** quoted-block AND `"A" "B"`, `!` negation as its own quoted block, lookaheads `(?=...)`/`(?!...)` supported, custom keywords.
- **Validation:** per-game tokenizer accepts the supported set; unsupported constructs (lookahead in PoE 1, `\b` everywhere) produce a diagnostic warning — the engine never emits them and never claims they "crash" the game.
- **Assembly rules:** one quoted block with `|` alternation over included-mod patterns; separate quoted blocks for exclusions (`!"term"`), the class fingerprint, and keywords, joined by spaces (quoted-blocks-separated-by-spaces = AND). Deterministic ordering everywhere.
- Unquoted spaces inside a quote are literal; they are NOT implicit AND separators.

## 6. Compaction Engine (corpus-backed)

- Select the shortest unique fingerprint per selected mod from the catalog; `MinValue` → digit-range pattern from the mod's value span, re-validated for uniqueness.
- Common-prefix factorization via trie (`m(ana|ax)` style) to reduce length.
- Vowel-stripping heuristic gated by the uniqueness oracle, **off by default** (correctness first).
- **False-positive oracle:** every emitted fragment is asserted to match no other line in the class corpus (suffix index built at load). Property tests enforce this over all catalogs.
- Output is fully deterministic (stable for tests and reproducible builds).

## 7. CLI (`src/PoeRegex.Cli`)

- Bare run → interactive: pick game → class → searchable mod list → thresholds → exclusions → output query + `n/250` meter.
- Batch: `generate --game poe2 --catalog <path> --class Helmets --include "to maximum life" --min "increased rarity:30" --exclude "reflect" --ilvl 60 [--clipboard] [--json]`
- Support: `list-classes`, `list-mods --class X`, `validate --game X --query "..."`.
- Clipboard via TextCopy (cross-platform). Exit codes 0/1/2.

## 8. Test Suite (xUnit + Awesome Assertions)

- **Grammar/validation:** per-game accept/reject matrices; assembly determinism.
- **Length enforcement:** injected `GameRules` (limit 50 → overflow; 250 → passes).
- **Compaction:** no-false-positive property tests over catalog fixtures; threshold pattern correctness; deterministic output.
- **Data build:** fixture RePoE JSONs → expected rendered lines (number formatting, handlers), fingerprint correctness.
- **Integration:** self-authored query→expected-string cases modeling functional trade-site behavior (no third-party example text).
- Golden-corpus validation against real in-game copy-text is a post-v0.1 item (v0.1 uses synthetic text synthesized from RePoE data).

## 9. Non-blocking "verify during implementation" items

- PoE 1 advanced copy including mod names on mod lines (gates `SupportsModNameAnchors`).
- PoE 1 multi-quoted-block AND behavior (to be verified against the game itself).
- Exact `stat_value_handlers` formatting edge cases (percent, fractions, rounding) — covered by fixture tests.
- PoE 2 `ilvl:`/`str:` spacing rules (to be verified against the game itself).

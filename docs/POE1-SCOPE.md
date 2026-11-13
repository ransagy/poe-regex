# PoE1 introduction — scope and sizing

_Status: normal-equipment MVP shipping with the current release. Current data
is RePoE PoE1 3.29.3.6 (2026-11-13); the 3.30 refresh is pending the upstream
RePoE export. Last updated 2026-11-13._

This document sizes the introduction of Path of Exile 1 as a second game
profile. It is an engineering scope, not a delivery promise; the estimates
assume one experienced developer and exclude upstream league churn.

## Recommendation

Add PoE1 as a **separate game profile** with its own catalog, target model,
corpus, exact table, proof, class/slot model, and import rules. Do not retrofit
PoE2 records with a game flag. Keep PoE2 as the default game and load PoE1
data lazily so the current app and PWA stay within their size budgets.

Ship normal equipment first (prefixes/suffixes, base implicits, corrupted
enchantments), then add influences, crafts/veils, flasks, and jewels. This
matches the current PoE2 coverage order and gets a usable PoE1 picker before
the high-variance sources.

## 1. Goal and first-release scope

**Goal:** a PoE1 workbench that can pick a slot/class, select mods, compile the
in-game search string, import copied item text, save/share picks, and explain
matches, using PoE1 data and grammar.

**First release (recommended):**

- Normal equipment: prefix/suffix families and tiers
- Base implicits
- Corrupted enchantments
- Class anchors and the PoE1 equipment slot model
- Import of normal/magic/rare copied item text
- Per-game saved picks, share links, and workbench persistence

**Later phases:** influenced mods, crafted/veiled mods, flasks, jewels
(normal/abyss/cluster), timeless jewels, synthesis/scourge/crucible/eldritch
and other league sources.

**Out of scope for the first release:** unique-only mods, gems, maps,
currency, passive tree, trade-site queries, accounts, and exact base-type
fidelity beyond the item class.

**MVP+1:** vendorable skill and support gems. Their copied text includes
explanatory lines that can win fragment selection, so they need their own
corpus and target-model review before they can join the normal-equipment
table without regressing it.

### MVP boundary

**Included**

- 23 equipment classes: Helmet, Body Armour, Gloves, Boots, Belt, Amulet,
  Ring, Shield, Quiver, Claw, Dagger, Wand, One Hand Sword, Thrusting One Hand
  Sword, One Hand Axe, One Hand Mace, Sceptre, Bow, Staff, Two Hand Sword,
  Two Hand Axe, Two Hand Mace, Warstaff
- Normal/magic/rare prefix and suffix families: **1,200 mods, 182 families,
  and 135 normalized patterns** that map through the current PoE1
  `mods_by_base` equipment pools
- Base implicits at class scope: **178 mods**
- Item-domain corrupted implicits: **233 mapped mods** out of 372 exported
  (the rest have no equipment class in `mods_by_base`)
- **Catalog total: 1,611 mods across 23 classes**
- **Exact table: 1,584 scopes, 0 unresolved, all verified strict** against the
  generated PoE1 foreign-line corpus
- Ten equipment slots, All-gear union, per-slot pickers
- Query compile, min values, exclusions, copy, explain, saved picks/share
- Import of normal/magic/rare copied item text, including fractured headers
  that carry normal affixes
- Exact table/proof/status for those scopes

**Not in the MVP**

- Influenced mods (1,113 affixes)
- Crafted/veiled mods, lab/bench enchantments
- Flasks, jewels, cluster/abyss/timeless jewels
- Synthesis, eldritch, scourge, crucible, tattoos, and other league sources
- Unique-only text, gems (MVP+1), maps, currency
- Exact base-type implicit fidelity beyond the item class

## 2. What is reusable

The core is mostly game-agnostic:

- exact fragment table lookup, suffix index, and strict oracle
- exact solver, matrix, proof, and status pipeline
- compiler assembly, exclusions, paste composition, length limits
- pick claims, mod families/tiers, common-picks validation pattern
- import parser structure and item matcher
- picker, saved picks, share links, PWA shell, and release gates

What is currently PoE2-hardcoded and must become profile data:

- `Poe2Syntax` and query shape validation
- `GameItemClasses` / in-game class filtering
- `EmbeddedCatalogSource`, `EngineCorpus`, `FragmentTable`, `VendorCorpus`
- `CatalogBuilder`, `Program` paths, matrix/proof/status game ids
- web `EngineService` construction and data loading
- slot/class lists, import headers, persistence keys, and share snapshots

## 3. Data source and survey

The PoE1 export is the **root** RePoE export
(`https://repoe-fork.github.io/`, linked from `poe1.html`), not
`/poe1`. The local `data/raw/poe1` snapshot is the 2026-11-13 live fetch and
is pinned by size and SHA-256 in `data/repoe-poe1.snapshot.json`.
`data/repoe.manifest.json` now carries and classifies the root export's 90
entries; `data-audit --check` passes.

Live export survey (2026-11-12):

| Surface | Count |
|---|---:|
| Mods | 40,355 |
| Base items | 5,461 |
| Item classes | 103 |
| `item` domain mods | 25,052 |
| Unique-only mods | 15,886 |
| Item prefix/suffix mods | 4,366 |
| Distinct normalized affix texts | 1,013 |
| Mapped normal equipment mods | 1,200 |
| Mapped normal patterns | 135 |
| Mapped base implicits | 178 |
| Mapped corrupted mods | 233 |
| Catalog entries (all sources) | 1,611 |
| Exact-table scopes | 1,584 |
| Essence-only affixes | 458 |
| Influence-tagged affixes | 1,933 |
| Crafted mods | 1,637 |
| Unveiled mods | 258 |
| Flask mods | 439 |
| Abyss jewel mods | 548 |
| Item-domain corrupted mods | 372 (233 mapped) |
| Enchantments | 1,635 |
| Eldritch implicits (`eater`/`searing`) | 4,086 |
| Crucible tree mods | 2,604 |

Relevant equipment/flask/jewel bases total **1,086** across 29 classes,
including 124 Body Armour, 99 Amulet, 98 Shield, 96 Helmet, 83 Boots, 80
Gloves, 49 Ring, 18 Utility Flask, and 8 Jewel bases. Cluster jewels use a
separate `cluster_jewels.json` export rather than base items.

Raw file sizes: `mods.min.json` 22 MB, `mods_by_base.min.json` 15 MB,
`base_items.min.json` 3 MB, `stat_translations.min.json` 4.4 MB.

### Data quirks

- PoE1 mods already carry a rendered `text` field, unlike PoE2's stat
  translation pipeline. Normal affix rendering is therefore simpler; the
  range template still needs normalization to the copied item form.
- `mods_by_base` is keyed by comma-separated **tag sets**, not item class
  names. PoE2's mapper cannot be reused unchanged.
- PoE1 has duplicate class categories (for example Thrusting One Hand Sword
  and One Hand Sword share a category id), several weapon classes, quivers,
  and flask/jewel classes.
- Sources are much broader: influences, fractured, synthesised, crafted,
  veiled, enchanted, corrupted, eldritch, scourge, crucible, essence, and
  league-specific records.
- Base items carry `inherits_from`, tags, implicits, properties, and
  requirements; class mapping must prove a positive equipment mapping, not
  infer from a bare domain.
- Jewels split into normal, abyss, cluster, and timeless data; cluster jewels
  are not in the normal base-item list.
- Flasks have their own prefix/suffix pool and enchantments.
- A single mod can have multiple stat lines and local/global variants.

## 4. Grammar and copy-text spikes

Validated in-game on 2026-11-12/13. Outcomes:

1. **In-game grammar parity.** Confirmed: quoted phrases, `|`, case folding,
   whitespace, wildcards, anchors, `\d`, escaped punctuation, and the
   250-character limit match PoE2. One difference: exclusions use `"!term"`
   (inside the quotes), not `!"term"`; `QuerySyntax.Poe1` owns that shape.
2. **Copied item text.** Confirmed: copy text carries both final values and
   advanced `roll(min-max)` forms plus `{ Prefix/Suffix/Implicit Modifier … }`
   headers. The PoE2 `advanced-ranges-v1` target model applies unchanged.
3. **Search corpus.** Confirmed line-local, case-insensitive, substring
   matching over headers, annotations, properties, names/base types, and
   class/sockets/quality/item level.
4. **Import shapes.** Confirmed normal/magic/rare names, fractured headers,
   corruption implicits, annotations, socket lines, and item-level lines.
   Influenced, eldritch, crafted, and veiled sources are rejected.
5. **Length/budget.** Confirmed 250 characters, the same budget as PoE2.

Deliverable: a grammar/copy-text spike note with a fixture matrix; no builder
work starts until these are signed off. The results above are now encoded in
`Poe1SyntaxTests`, `Poe1Tests`, and the shipped table.

## 5. Shipped artifacts and reproducible build

- Manifest: `data/repoe.manifest.json` has a `poe1` export rooted at
  <https://repoe-fork.github.io>, audited through the generated
  <https://repoe-fork.github.io/poe1.html> index. All 90 entries are
  classified; the seven catalog inputs are `used`.
- Snapshot: `data/repoe-poe1.snapshot.json` pins the seven raw files by size
  and SHA-256, with `dataVersion` `2026-11-13` (RePoE PoE1 3.29.3.6,
  pre-3.30; the 3.30 refresh is pending the upstream export).
- Catalog: `data/catalogs/poe1.catalog.json` — 23 item classes, 1,611 mods,
  with source, family, tier order, classes, groups, and stat lines.
- Foreign corpus: `data/catalogs/poe1.vendor-corpus.json` — deduplicated base
  names, properties, requirements, base implicits, affix names,
  advanced-description captions, class lines, and every plain/advanced mod
  form (85,668 lines).
- Exact data: `data/catalogs/poe1.fragments.json` — 1,584 scopes, 0
  unresolved. The web copies live under `web/wwwroot/data/poe1/` and load
  lazily.
- Safety fixtures: `data/catalogs/poe1.safety-items.txt` — captured item text
  used as foreign lines by the proof, including the reported `Sockets: G`
  boots item.
- Proof pipeline: `docs/CANDIDATE-MATRIX-poe1.html`,
  `docs/CANDIDATE-PROOF-poe1.json`, and `docs/CANDIDATE-STATUS-poe1.json`
  carry the `poe1` game id and prove the shipped table. Strictness is proven
  over the generated foreign corpus (all catalog mod lines, base
  names/properties, class, socket, and fixed property lines) plus the captured
  safety item.
- Code: per-game `GameData`/`EngineCorpus`/`EngineService`, `GameSession`,
  `GameDataLoader`, `QuerySyntax.Poe1`, per-game persistence, and PoE1
  parser/import rules. Class anchors are at least two characters and are
  checked against socket and property lines, so `G` cannot satisfy a Gloves
  guard via `Sockets: G`.
- Tests: normal, fractured/corrupted, influenced, crafted, veiled, and
  eldritch fixtures in `Poe1Tests`; dialect tests in `Poe1SyntaxTests`.

Rebuild from the pinned raw snapshot:

```sh
dotnet run --project tools/PoeRegex.Data.Build -- poe1-build
dotnet run --project tools/PoeRegex.Data.Build -- poe1-matrix --from-scratch
dotnet run --project tools/PoeRegex.Data.Build -- poe1-status --check
```

`poe1-build` reads `data/raw/poe1` (gitignored) and writes the catalog and
foreign corpus. `poe1-matrix` regenerates the matrix, table, proof, and status;
`poe1-status --check --check-shortest` fails if the table is stale or the proof is missing.

### Import boundary

| Source | MVP | Behavior |
|---|---|---|
| Normal / magic / rare equipment | Yes | Parsed and mapped |
| Fractured headers with normal affixes | Yes | Parsed; affix maps normally |
| Corruption implicits | Yes | Mapped to the corrupted pool |
| Plain and advanced roll forms | Yes | `roll` and `roll(min-max)` both match |
| `(Tier: n)` / `(Rank: n)` headers | Yes | Header is searchable text |
| `— Unscalable Value` line suffix | Yes | Stripped before catalog mapping |
| Shaper / Elder / Crusader / Redeemer / Hunter / Warlord | No | Rejected with an influence message |
| Searing Exarch / Eater of Worlds eldritch | No | Rejected with an eldritch message |
| Crafted / Master Crafted | No | Rejected with a crafted message |
| Veiled | No | Rejected with a veiled message |
| Synthesised items | No | Rejected with a synthesised message |
| Lab/bench enchantments | No | Rejected as an unsupported modifier header |
| Unique rarity | No | Rejected; unique-only text is outside the MVP |
| Gems (skill/support) | MVP+1 | Excluded; own text model needed |
| Flasks, jewels, maps, currency, uniques | No | Outside normal-equipment scope |

PoE1 exclusion blocks use `"!term"` (inside the quotes), unlike PoE2's
`!"term"`. `QuerySyntax.Poe1` owns that shape; compiler, paste composition,
explain, query import/reconstruction, item matching, and the block ruler all
route through the corpus syntax.

### Known text-search limits

- The exact table is strict over the generated foreign corpus plus the captured
  `poe1.safety-items.txt` item. Rare/unique names, unmodeled league text, and
  other item text can still satisfy a short fragment; this is the same
  inherent text-search limitation as PoE2.
- The common-picks shortlist is shared with PoE2 and resolves by family key or
  display text where a close equivalent exists. There is no dedicated PoE1
  common-picks curation yet.
- Old copied strings containing one-character class anchors (for example `G`
  for Gloves) can still false-highlight. Regenerate the string in the current
  build; the new anchors are at least two characters.

## 6. Catalog and source model

Suggested first-release filter:

- `domain = item`
- `generation_type in (prefix, suffix)`
- exclude `unique` and `is_essence_only`
- exclude influence-tagged affixes in the first pass (separate source later)
- exclude lab/bench enchantments and league-only generations (`crucible`,
  `scourge`, `synthesis`, eldritch implicits, etc.)
- map to equipment classes through base tags/spawn weights, not the PoE2
  `mods_by_base` class-name shape

Then add separate sources in this order:

1. base implicits
2. corrupted enchantments
3. influenced mods
4. crafted/veiled
5. flasks
6. jewels (normal → abyss → cluster)
7. timeless/league mechanics

Families/tiers use the PoE1 mod `type`/`groups`, name, and required level.
Duplicate policy should stay class-scoped and effect-first, as in PoE2, but
the source tags and duplicate rules need their own acceptance tests.

## 7. Architecture changes

Implemented in the MVP: `GameData` carries a game's catalog/table/corpus;
`EngineCorpus.Syntax` selects the query dialect; `EngineService.LoadGame`
switches in place; `GameDataLoader`/`GameSession` own lazy loading and
per-game state. The original plan is kept below as the design record.

### Profile foundation

- `GameProfile`: id (`poe1`/`poe2`), display name, catalog/table/corpus
  resource names, syntax, class/slot model, import rules, common picks.
- Replace hard-coded `Poe2Syntax`, `GameItemClasses`, and resource names with
  profile injection.
- Add a game id to catalog, table, proof/status, snapshots, saved picks, and
  share links.

### Data pipeline

- Add a `poe1` manifest export with `used` entries and audit classifications
  for the root export's many unrelated files.
- Add PoE1 schema loaders for the list-shaped `stat_translations`, tag-shaped
  `mods_by_base`, and `text` field.
- Add `Poe1CatalogBuilder`, `Poe1CorpusBuilder`, and PoE1 safety-item fixtures
  (implemented: `Poe1CatalogBuilder`, `poe1.vendor-corpus.json`,
  `poe1.safety-items.txt`).
- Parameterize matrix/proof/status output paths and game ids.
- Keep the exact solver game-agnostic; only scopes, target forms, and class
  anchors differ.

### Web/app

- Lazy-load PoE1 data; do not embed both full datasets in the initial WASM
  payload.
- Add a game switch in Options/header, with PoE2 as default and last-used
  remembered.
- Per-game slot model, class lists, common picks, picker tags, and import.
- Per-game saved picks/share links; decide whether to migrate or namespace
  existing PoE2 data.
- PWA cache both shells/data packs with versioned files.

### Data size risk

PoE2's vendor corpus is already about 18 MB and the publish is about 25 MB.
PoE1 adds 22 MB of mods plus a larger base/class surface. Shipping both
embedded is likely to exceed the Cloudflare Pages per-file limit and the
tolerable initial download. Plan for split/lazy data files, or a separate
PoE1 deployment if lazy loading is not clean.

## 8. Workstreams and sizing

### Basis

The first draft used a top-down 6–9 week estimate. That was too conservative:
it assumed a cold codebase, a full profile rewrite, and no reuse. It was not
calibrated to this project's measured velocity.

The repo has 436 commits; the main October–November run spans 31 days. The P2
augment pass (catalog, 3,734-scope exact table, and picker/union work) landed
in about two days. The offline PoE1 spike mapped the normal-equipment surface
to **1,204 normal mods / 135 normal patterns / 764 class-pattern rows**, and
the shipped MVP catalog now carries **1,611 mods across 1,584 table scopes**,
versus 1,050 patterns / 2,480 class rows for the PoE2 P2 table. PoE1 is smaller and simpler
to render because its mods already carry a `text` field. The real new cost is
the profile/web refactor and the grammar/copy-text unknowns, not the stat
volume.

Estimates below are focused engineering days for one developer, not calendar
time. Add review, release, and upstream league churn separately.

| Workstream | Focused days |
|---|---:|
| P0 live grammar/copy-text validation | 0.5–1 |
| Profile foundation and game id plumbing | 1–2 |
| PoE1 manifest/fetch/audit + catalog builder | 1–2 |
| Corpus/class anchors/exact table/proof | 1–3 |
| Import fixtures and parser | 0.5–1 |
| Web game switch, lazy data, slots/UI | 2–3 |
| Tests, docs, PWA, perf, release | 1–2 |
| **Normal-equipment MVP** | **7–14 (~1.5–2.5 focused weeks)** |

The offline spike (1,204 normal mods / 135 patterns) and the normal-equipment
MVP catalog/table (1,611 mods / 1,584 scopes) are complete. Engine/data/
table only, without the web switch, is **4–7 focused days**. Add
influences/crafts/veils/flasks: **+5–9 days**. Add normal/abyss/cluster
jewels: **+8–12 days**. Full normal gear + crafts + flasks + jewels is
realistically **4–6 focused weeks**; league-specific sources remain separate
releases.

## 9. Suggested delivery phases

1. **P0 — spikes:** grammar, copy text, search corpus, current-data fetch,
   bundle-size experiment.
2. **P1 — internal PoE1 normal equipment:** catalog, corpus, exact table,
   proof, CLI/engine tests.
3. **P2 — web profile switch:** lazy load, slots, picker, import, saved/share.
4. **P3 — normal-equipment release.**
5. **P4 — influences/crafts/veils/flasks.**
6. **P5 — jewels and league mechanics.**

## 10. Risks and open decisions

- **Copy-text model unknown:** the largest correctness risk; resolve before
  building the table.
- **Data mapping:** PoE1 `mods_by_base`/spawn tags are not the PoE2 shape.
- **Bundle size:** both games may not fit comfortably in one WASM/PWA shell.
- **UI surface:** game switch, per-game saves, and import differences touch
  most of the web layer.
- **League churn:** PoE1 adds many legacy/league sources; keep them out of the
  first release.
- **Default game:** PoE2 default vs last-used.
- **Deployment:** one app with a game switch vs separate PoE1 host.
- **First-release scope:** normal equipment only, or include flasks/jewels
  from day one.

## 11. Release gates for the PoE1 MVP

- Data audit passes with every PoE1 root entry classified.
- Catalog counts and source coverage match the scope table.
- Grammar/copy-text fixtures pass against captured item text.
- Exact table: no stale entries, `shortestProven: true`, `failed: 0`.
- Web: game switch, profile-scoped saves/share, import round-trip.
- PWA: offline boot for both games; cache and file-size budgets pass.
- Perf: boot, mode switch, picker, import, and paste within the existing
  acceptance budgets (or documented exceptions).

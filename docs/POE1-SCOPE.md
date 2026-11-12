# PoE1 introduction — scope and sizing

_Status: scoped, not started. Last updated 2026-11-12._

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
`/poe1`. The local `data/raw/poe1` snapshot is from 2026-08-09 and is already
stale; the live root export has 40,355 mods / 5,461 bases / 103 classes
versus 40,348 / 5,173 / 102 locally.
PoE1 is not yet in `data/repoe.manifest.json`, so the first data task is to add
and classify it before any builder work.

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
| Essence-only affixes | 458 |
| Influence-tagged affixes | 1,113 |
| Crafted mods | 1,637 |
| Unveiled mods | 258 |
| Flask mods | 439 |
| Abyss jewel mods | 548 |
| Item-domain corrupted mods | 372 |
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

These must be resolved before committing to an exact table:

1. **In-game grammar parity.** Verify PoE1 search behavior for quoted phrases,
   `|`, `!` exclusions, case folding, whitespace, wildcards, anchors, and the
   maximum query length. Reuse the PoE2 grammar only if the client behavior
   matches; otherwise add a separate `Poe1Syntax` profile.
2. **Copied item text.** Capture representative PoE1 items and confirm whether
   copy text uses final values only or PoE2-style `roll(min-max)` advanced
   forms, and whether `{ Prefix Modifier … }` headers are copied. This decides
   whether the target model is `poe1-plain-v1` or an advanced variant.
3. **Search corpus.** Confirm what the in-game search actually matches:
   visible mod lines only, or also advanced headers/names/tags. The exact
   table and safety corpus depend on this.
4. **Import shapes.** Verify magic/rare item names, influenced/fractured/
   synthesised headers, flask/jewel text, socket lines, and item-level lines.
5. **Length/budget.** Measure the PoE1 search limit and whether it matches the
   current 250-character budget.

Deliverable: a grammar/copy-text spike note with a fixture matrix; no builder
work starts until these are signed off.

## 5. Base artifacts to establish

- Manifest: a `poe1` export entry, current raw snapshot, audit classifications
  for the root export's unrelated files, and a version stamp.
- Catalog: `poe1.catalog.json` with PoE1 item classes, sources, family keys,
  tier order, and fingerprints.
- Exact data: `poe1.fragments.json`, `poe1.vendor-corpus.json`, and
  `poe1.safety-items.txt`.
- Proof pipeline: PoE1 candidate matrix, proof, and status files with the
  game id in their payloads.
- Code: `GameProfile`, `Poe1Syntax` (or a proven shared syntax), PoE1 class/slot
  model, common-picks data, import rules, and profile-scoped persistence.
- Test fixtures: captured normal/magic/rare item text, influenced/fractured/
  synthesised examples, flask/jewel examples, and negative fixtures
  (gems/maps/currency) for foreign-line checks.

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
- Add `Poe1CatalogBuilder`, `Poe1CorpusBuilder`, and PoE1 safety-item fixtures.
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

Rough engineering estimate, one developer, excluding upstream league churn:

| Workstream | Estimate | Confidence |
|---|---:|---|
| P0 grammar/copy-text/data spikes | 3–5 days | Medium |
| Profile foundation and game id plumbing | 3–5 days | High |
| PoE1 manifest/fetch/audit + schema loaders | 5–8 days | Medium |
| Normal catalog/corpus/class mapping | 10–15 days | Medium |
| Grammar/import fixtures and parser | 5–8 days | Medium |
| Exact table/proof/status | 5–8 days | High |
| Web game switch, lazy data, slots/UI | 8–12 days | Medium |
| Tests, docs, PWA, perf, release | 4–6 days | High |
| **Normal-equipment MVP** | **~6–9 weeks** | Medium |

Phased coverage after the MVP:

| Phase | Estimate |
|---|---:|
| Influenced mods | 3–5 days |
| Crafted/veiled | 4–6 days |
| Flasks | 3–4 days |
| Jewels (normal/abyss/cluster) | 8–12 days |
| Timeless/cluster special data | 5–10 days |
| Synthesis, lab/bench enchant, eldritch, scourge, crucible, other leagues | 8–12 days |

Full normal-gear + crafts + flasks + jewels is realistically **10–14 weeks**;
league-specific sources add more and should be treated as separate releases.

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

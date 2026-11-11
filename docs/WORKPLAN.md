# Workplan

_Last updated: 2026-11-11._

Current planned and parked work. Behavior is documented in
[`DESIGN.md`](DESIGN.md), [`EXACT-TABLE.md`](EXACT-TABLE.md), and
[`../web/README.md`](../web/README.md); acceptance is in
[`ACCEPTANCE.md`](ACCEPTANCE.md).

## Current

- Production: <https://renlys.tools>. The live commit is in
  `/build-info.json`; the current release is `c870666` (PR #76, 2026-11-11).
  The Pages project is the internal `renlys-recipes` project.
- App: Blazor WebAssembly workbench with saved picks, import, and test items.
- Engine: exact PoE2 table/proof is current: 1737/1737 scopes proven, not
  stale ([`CANDIDATE-STATUS-poe2.json`](CANDIDATE-STATUS-poe2.json)).
- Catalog: explicit prefixes/suffixes, Abyss desecrated item affixes, base
  implicits, and corrupted enchantments. Implicits render in a dedicated
  **Implicits** section above the normal Prefixes/Suffixes columns with a
  muted-blue `<Implicit>` tag; only implicit-only patterns are included, so a
  line already covered by a normal prefix/suffix is not duplicated. Implicit
  scope is class-level. Corrupted enchantments render in a dedicated
  **Corrupted** section below Implicits with a muted-red `<Corrupted>` tag;
  only class scopes not already covered by a normal/desecrated/implicit family
  are included. The builder allows at most two. The PoE2 export also carries
  legacy PoE1 `crafted` prefix/suffix records (Navali/Helena master crafts);
  they are excluded because they have no positive PoE2 base mapping. PoE2's
  actual crafted mods are jewel-only (`CraftedJewel*`, domain `misc`) and
  remain parked with jewel support. Basic tiered runes use the same
  unique-only policy as implicits/corrupted: a rune family is kept only where
  at least one effect pattern is new, leaving 14 unique effect families
  (55 target rows). They render in a dedicated **Augments** section with a
  Rune source tag and an Armour / Martial Weapon / Wand or Staff target tag;
  the export's target category controls which item classes can carry each
  effect. In the All-gear union, a rune family is omitted when one
  non-augment family covers every one of its effect patterns; per-slot views
  keep it where it is the only source, so the union shows only the two
  globally unique effects (Stone Rune Wand/Staff and Vision Rune Armour).
  Attribute runes (the `All Equipment` target), special/transformative
  augments, Soul Cores, Idols, Abyssal Eyes, bonded stats, and a socket model
  remain parked.
- Picker: selection is claim-covered by the compiled positive query. A mod/tier
  row with the same query as the active selection stays visible as `covered`;
  its `⇄` button switches the selection instead of adding a second chip. An
  always-visible color-coded glyph legend under the scope line (`+` Add,
  `✓` Selected, `⇄` Switch, `−` Exclude, `⊘` Excluded, `≥` Better, `covered`
  by an existing pick) keeps the key fixed while typing; the always-present
  scope line shows `Common picks`, `All N mods`, or `N matches`, and the `(?)`
  there explains multi-effect searches, covered rows, duplicate merging, and
  exclusions. Includes and exclusions are mutually exclusive per search;
  duplicate exclusion fragments collapse in the compiler.
- Common picks: the pre-typing shortlist is data-driven from
  `data/common-picks.json`. `classes` is a runtime scope, so Helmet minion
  levels and Gloves attack-only added damage stay out of All gear while the
  normal weapon-local physical/fire/cold/lightning variants remain global.
  `common-picks --check` validates the entries and the generated contributor
  table, and runs in CI.
- Tests: 111 core, 415 web, 39 data-build; local suites green.
- Deployment: Cloudflare Pages with branded previews, a Pages-host guard, and
  daily deployment cleanup. Production deploys are manual via
  `deploy-production`; previews remain automatic.
- PWA: installable offline shell with a build-versioned service-worker
  registration; a normal reload adopts a new build without a hard refresh,
  and installability, offline reload, and cache replacement pass locally.
- Import: advanced item lines are range-first; rounded display rolls,
  reversed ranges, and decimal patterns map exactly. Fuzzy fallback remains
  only for unmatched wording, and genuine ambiguity keeps a deterministic
  candidate rank. Multi-line stat descriptions still map line by line.
- Accessibility: light-theme source tags and dark secondary text meet 4.5:1;
  game rarity colors are unchanged and the unique name keeps its exact hex
  with a dark edge. Compact layout reviewed at 390×844 and 768×1024;
  color-vision simulation passed with text/icon labels preserved.
- Data sync: `data/repoe.manifest.json` maps the public RePoE and PoB exports;
  `data-audit --check` fails on new/unmapped files. `fetch` is manifest-driven,
  writes `data/repoe.snapshot.json` (313 SoulCore bases, 16,911 mods,
  `dataVersion` 2026-11-08), and the manual **data-audit** workflow checks the
  published inventory weekly.
- Perf: a manual `web-perf` workflow builds the app, runs the 1920×1080
  harness, publishes the JSON/log artifact, and writes a step summary. It is
  observational; the local release-prep run remains the blocking gate.

## Next

### Missing mod-type scope

Scope every remaining missing mod type before implementation: jewels, attribute
runes, special/transformative runes, Soul Cores, Idols, Abyssal Eyes, bonded
stats, and the socket model. This is a scoping pass first; the coverage table
below remains the reference.

## Recently completed

- UI follow-up PR: picked/excluded rows stay visible under search, common-only,
  and scope filters; the compact layout pins the search header/query/ruler while
  the tools scroll; Reset workbench is a visible danger button; and
  `common-picks --table` generates the contributor table with a CI drift gate.
- PR #76 merged and deployed at `c870666`: RePoE manifest/audit/snapshot,
  basic rune augments, fail-closed exact solver timeout, claim-covered picker,
  data-driven common picks, and common-picks CI validation.
- Picker row metadata, touch hover/focus cleanup, and the common-picks
  contributor flow documented.

## Parked

### Distribution and platform

- Browser AOT / smaller publish size (currently the unoptimized interpreter
  build, about 25 MB).
- Short-link / URL-share store. Current `#r=` snapshot links work; a compact
  or server-backed share layer is deferred.
- Accounts / OIDC. Design-only; would add a backend and the largest operational
  surface.

### Catalog coverage

Coverage order is driven by how much of the normal-equipment model each type
needs. The shared work for any catalog addition is the same: provenance check,
class mapping, source/kind handling, import behavior,
catalog/matrix/table/proof rebuild, stability pass, fragment report, and tests.

| Type | Raw surface | New patterns vs catalog | Effort | Impact | Status |
|---|---:|---:|---|---|---|
| PoE1 crafted leftovers | 9 | 0 for PoE2 | — | — | Excluded |
| Base implicits | 168 ids / 348 base entries | 85 (94 mods) | Medium | High | Done |
| Corrupted mods | 108 records / 103 mapped | 43 (44 mods) | Medium | Medium | Done |
| Jewels | 320 normal + 44 desecrated + 13 crafted | ~196 + 34 | High | High for jewel users | Parked |
| Augments / Soul Cores | 313 released bases | 14 unique rune effect families / 55 target rows | Medium | High for socketed items | Unique basic runes done; specials parked |

- **PoE1 crafted leftovers** are not PoE2 coverage. The nine `crafted`
  prefix/suffix records in the PoE2 export are verbatim PoE1 Navali/Helena
  master crafts (`Prefixes Cannot Be Changed`, `of Blurring`, etc.); they have
  no positive PoE2 base mapping and are excluded. A new category must have a
  positive PoE2 reference (current base mapping, positive spawn tags, or an
  implicit/base link), not just a domain label. PoE2's actual crafted mods are
  the jewel-only `CraftedJewel*` records in domain `misc`, which belong to the
  parked jewel work.
- **Base implicits** are now modeled at class level: 94 catalog mods across 85
  implicit-only patterns and 13 classes. All 168 implicit ids are PoE2-specific
  by exact-id check. Coverage is per class: a line already covered by a normal
  prefix/suffix on one class can still ship on another class where it is
  missing. Import keeps implicit lines quiet. The limitation remains that a
  class family does not prove a specific base type; full base-type fidelity
  needs a base selector and is parked with that work.
- **Corrupted mods** are modeled as a separate non-affix kind: 44 catalog mods
  across 43 patterns and 17 classes. All 108 raw ids are PoE2-specific by
  exact-id check; 103 map through `mods_by_base` and 5 have no positive
  base/spawn reference and are excluded. The builder allows at most two.
  Game copy presents corruption enchantments as implicit modifiers, so import
  keeps those lines quiet; an explicit `{ Corrupted Modifier }` header maps.
  Full base-type import mapping is parked with the base selector.
- **Jewels** need a separate item class/slot and `misc` domain mapping. Normal
  jewel mods, desecrated jewel mods, and the actual PoE2 crafted jewel mods
  are all outside the current normal-equipment scope. Parked until the
  item-class/slot work is justified.
- **Augments / Soul Cores** are now sourced from RePoE `augments.json`, which
  maps each base to its target category and rendered effect lines. The first
  pass covers the 63 basic tiered runes, filtered to 14 unique effect families
  (55 target rows); they are treated as
  a separate non-affix source and do not need a socket system for search.
  Attribute runes (`All Equipment`), the 36 bonded-only category rows, special
  and transformative runes, Soul Cores, Idols, Abyssal Eyes, bonded stats, and
  any socket-count model remain parked. The RePoE inventory and PoB
  cross-check are documented in [`REPOE-DATA.md`](REPOE-DATA.md).
- **Unique-item mods** are out of normal-rarity scope. **Essence/veiled**
  records are placeholders with no usable rendered text in the current export.

### Query compiler

- **Combined-slot fragment middle ground.** A shared criterion across
  multiple classes now uses the global fragment, which is proven safe across
  the class union, rather than one class's shortest fragment. That is
  intentionally conservative and can be slightly longer than a class-specific
  minimal form. Parked: revisit only if query-budget pressure returns, and
  look for a shorter shared-safe form (for example, intersecting the
  class-safe forms or factoring per-class alternatives). Single-class queries
  already use the minimal class fragment.

### Ops decisions

- Keep the current Pages middleware or move the Pages-host block to Cloudflare
  Access. Access adds identity policies and audit logs but needs Access API
  permissions, a Worker service token, and a custom-domain bypass app. The
  middleware also provides the `pages.dev` → apex redirect, which Access does
  not.
- Remove the legacy `poe-regex.recipes.v1` saved-picks key after the next
  release.

## Acceptance gates

- Release prep checklist: [`RELEASE-PREP.md`](RELEASE-PREP.md).
- Web acceptance: [`ACCEPTANCE.md`](ACCEPTANCE.md).
- Engine exact table/proof: [`EXACT-TABLE.md`](EXACT-TABLE.md) and
  [`CANDIDATE-STATUS-poe2.json`](CANDIDATE-STATUS-poe2.json).
- Import behavior and limitations: [`IMPORT.md`](IMPORT.md).

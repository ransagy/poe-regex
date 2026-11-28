# Workplan

_Last updated: 2026-11-28._

Current planned and parked work. Behavior is documented in
[`DESIGN.md`](DESIGN.md), [`EXACT-TABLE.md`](EXACT-TABLE.md), and
[`../web/README.md`](../web/README.md); acceptance is in
[`ACCEPTANCE.md`](ACCEPTANCE.md).

## Current

- Production: <https://renlys.tools>. The live commit is in
  `/build-info.json`. The Pages project is the internal `renlys-recipes`
  project.
- App: Blazor WebAssembly workbench with saved picks, import, and test items.
  PoE2 remains the default; the PoE1 normal-equipment MVP is shipping with the
  current release and is scoped in [`POE1-SCOPE.md`](POE1-SCOPE.md).
- Engine: exact PoE2 table/proof status is tracked in
  [`CANDIDATE-STATUS-poe2.json`](CANDIDATE-STATUS-poe2.json); PoE1 in
  [`CANDIDATE-STATUS-poe1.json`](CANDIDATE-STATUS-poe1.json).
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
  remain parked with jewel support. All runes (including
  special/transformative and Legacy), Soul Cores, Idols, Abyssal Eyes, and
  bonded effects use the same unique-only policy as implicits/corrupted: a
  family is kept only where at least one effect pattern is new, leaving 613
  target rows across 497 families after all target mapping. They render in a
  dedicated **Augments** section with Rune / Soul Core / Idol / Abyssal Eye
  source tags; the export's target filters eligible classes but is not shown
  in the tag in either view. Bonded effects carry a separate
  Bonded tag. The export's target category controls which item classes can carry each
  effect. In the All-gear union, a family is omitted when one non-augment
  family covers every one of its effect patterns; per-slot views keep it where
  it is the only source. Congealed Mist, jewels, and a socket/import/builder
  model remain parked.
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
- Tier ranges: a canonical `StatConstraint`/planner normalizes exact tiers,
  “or better”, family picks, and min/max into one lossless interval set.
  Multi-value families use one combined family+range fragment for exact and
  up tiers; a single-value family (`+1 Skills`) keeps the shorter affix-name
  form. Four exact tiers, Stallion’s or better, and the same tier set compile
  identically and independent of pick order; first-tier “or better” equals
  the family pick. The pick row shows the
  explicit range (for example `20–35% increased Movement Speed`); a legacy
  name-up row shows the compact equivalent range
  (`= 20–35% increased Movement Speed`). The range is proven against the
  catalog: every matched line must be an allowed pattern and its first roll
  must sit inside the requested intervals. Min/max boxes appear when the
  pick covers more than one numeric value; a single fixed value (one fixed
  tier or `+1 Skills`) hides both, and a legacy exact ranged name pick
  converts to its range pick on the first filter. Integer stats use whole
  numbers, decimal stats keep the game's displayed precision (including
  extra digits such as 2.03 in a 1.5–2.5 stat), and the range stays in the
  same quoted fragment as the family text. Cross-slot tier ranges group by
  covered intervals and min/max, including exact+up unions when the extra
  span does not exist for the narrower class; otherwise the groups stay
  separate. Factoring never splits character classes/groups. PoE2 exclusions
  use `"!term"` like PoE1.
- Common picks: the pre-typing shortlist is data-driven from
  `data/common-picks.json`. `classes` is a runtime scope, so Helmet minion
  levels and Gloves attack-only added damage stay out of All gear while the
  normal weapon-local physical/fire/cold/lightning variants remain global.
  `common-picks --check` validates the entries and the generated contributor
  table, and runs in CI.
- Tests: the full Release suite and CI are the release gate.
- Deployment: Cloudflare Pages with branded previews, a Pages-host guard, and
  daily deployment cleanup. Production deploys are manual via
  `deploy-production`; previews remain automatic.
- PWA: installable offline shell with a build-versioned service-worker
  registration. A new worker waits instead of taking over; a non-blocking
  toast offers Update now/Later, Options has a single Check/Update button and
  an Ask/Automatic setting, the row says **An update is ready** when a worker
  is pending, automatic mode waits for 60s idle and a 5s countdown, and a
  shared link finishes applying before an update may reload. Installability,
  offline reload, cache replacement, manual update, automatic update, and
  shared-link update pass locally.
- Import: advanced item lines are range-first; rounded display rolls,
  reversed ranges, and decimal patterns map exactly. Fuzzy fallback remains
  only for unmatched wording, and genuine ambiguity keeps a deterministic
  candidate rank. Multi-line stat descriptions still map line by line.
- Accessibility: light-theme source tags and dark secondary text meet 4.5:1;
  game rarity colors are unchanged and the unique name keeps its exact hex
  with a dark edge. Compact layout reviewed at 390×844 and 768×1024;
  color-vision simulation passed with text/icon labels preserved.
- Data sync: `data/repoe.manifest.json` maps the public RePoE and PoB exports;
  `data-audit --check` fails on new/unmapped files. `fetch` is manifest-driven
  and writes `data/repoe.snapshot.json`; the manual **data-audit** workflow
  checks the published inventory weekly.
- Perf: a manual `web-perf` workflow builds the app, runs the 1920×1080
  harness, publishes the JSON/log artifact, and writes a step summary. It is
  observational; the local release-prep run remains the blocking gate.

## Open work index

This is the canonical list. Design details live in the linked docs; this table
is the status source of truth.

| ID | Item | Status | Notes |
|---|---|---|---|
| STR-1 | Any-of union for over-budget pastes | Done | Merge compatible slot groups with a per-class proof; 253→183 and 325→186 on the PoE2 fixtures, 261→211 on the real PoE1 state |
| STR-2 | Shared-safe fragment selection | Parked | Current multi-class criteria use the global fragment; revisit shorter class-intersection forms |
| STR-3 | Share payload compaction | Deferred | Token is 828 chars for a typical 10-slot state; not a current constraint |
| STR-4 | Safe cross-term regex factoring | Done | Token-aware prefix and value-suffix factoring; 31–45 Cold/Fire/Lightning resistance 85→45; representative four-family added-damage exact query 96→88. Name fragments stay unfactored for round-trip |
| STR-5 | Category threshold picks | Parked | Broad hand forms such as `[3-9]\d.*resi` or `.*att` intentionally match more than exact picks; needs an explicit category/threshold pick |
| DATA-1 | PoE2 jewels and socket/import/builder model | Parked | Normal/desecrated/crafted jewels; separate item slot/class |
| DATA-2 | PoE2 Congealed Mist | Parked | After jewel/item-class work |
| DATA-3 | PoE1 gems | MVP+1 | Copied gem text needs its own corpus/target-model review |
| DATA-4 | PoE1 flasks/jewels/influenced/crafts/veils | Later phases | See `POE1-SCOPE.md` |
| PERF-1 | Runtime proof for arbitrary min/max and cross-slot intervals | Parked | Only if observed; release smoke currently clean |
| UX-1 | Reset workbench scope | Parked | Decide whether UI-local filters/panels reset too |
| UX-2 | Smoother PWA update flow | Done | Waiting worker, update toast/Options check, Ask/Automatic idle, shared-link pause; covered by `tools/pwa-update-check/run.mjs` |
| DIST-1 | Browser AOT / smaller publish | Parked | Current interpreter publish is ~25 MB |
| DIST-2 | Compact/server share links | Parked | Current `#r=` snapshots work |
| DIST-3 | Accounts / OIDC | Parked | Requires a backend |
| OPS-1 | Pages middleware vs Cloudflare Access | Parked | Access login/audit versus current simple middleware |
| OPS-2 | Retire legacy storage keys | Deferred | Keep until a deliberate deprecation window |
| OPS-3 | Build-version non-fingerprinted shell assets | Done | Cloudflare can override `_headers` for CSS/JS; `?v=<commit>` on CSS/JS in index and precache fixes stale-cache transitions |
| COMPAT-1 | Older clients and range-aware links | Documented | v7 snapshots reject safely; v6 range links can broaden until re-shared |

## Next

### String budget

- **STR-1 (done).** Over-budget multi-slot pastes may union compatible
  any-of slot groups. A merged group is accepted only when every target
  class's valid catalog span set is unchanged. Under-budget queries keep their
  existing exact, round-trippable form. PoE2 fixtures cover the given 253-char
  Boots/Gloves state and the 325-char variant with the same four damage
  families on the ring. The real PoE1 10-slot state now compiles in 211/250
  (was 261), and its Boots MatchAll movement + life variant in 234/250.
  Full-family picks become one family requirement, so classes with different
  legal tier counts (PoE1 Gloves cold 1–6, Ring cold 1–9) share one family
  fragment without enumerating every tier. A narrowed Ring cold tier stays
  exact while the other groups shorten: 238/250.
- **STR-2 (parked).** Once the grouping pass settles, revisit shorter
  shared-safe fragment selection for criteria that span classes. Current
  behavior uses the global fragment by design.
- **STR-4 (done).** Prefix and value-suffix forms factor at safe token
  boundaries, including group/range prefixes. The 31–45 Cold/Fire/Lightning
  resistance state compiles in 45/250 (was 85) with the same range and
  families. A representative four-family added-damage exact selection drops
  96→88; the suffix pass cannot beat the prefix form because the enclosing
  parentheses cost more than the repeated `ks` saves. Leading anchors are
  preserved, so already-factored `^\d.+...` pairs do not regress. Tier range
  plans cover every tier span and store compact proven union values, so common
  “or better” picks compose without a runtime proof; the first scoped 31–45%
  resistance pick is ~58 ms instead of ~1.5 s. Runtime range branches
  reconstruct from matched catalog lines, so both factored and pre-factoring
  range strings import to the same range picks. The remaining
  difference to the hand-written `.*att` snippet is a broader category
  filter. Suffix factoring is limited to fragments containing digits so
  affix-name lists stay round-trippable.
- **STR-5 (parked).** The broad `[3-9]\d.*resi` and `.*att` forms trade exact
  pick semantics for length. If users want them, add an explicit category
  threshold pick with its own match-set proof instead of silently widening
  normal picks.

### Remaining mod-type scope

P2 coverage is complete and live in production: all target strings are mapped,
and all runes (including special/transformative and Legacy), Soul Cores, Idols,
Abyssal Eyes, and bonded effects are in the catalog. Remaining: jewels
(data/simple mode, then advanced UX), Congealed Mist, and the
socket/import/builder model. The coverage table below is the reference.

### PoE1 introduction

Scoped in [`POE1-SCOPE.md`](POE1-SCOPE.md) and shipping with the current
release. PoE1 is a separate game profile, not a retrofit of the PoE2 catalog.

- Catalog: 23 equipment classes, 1,611 mods (1,200 normal, 178 implicit,
  233 corrupted).
- Exact table: 1,584 scopes, 0 unresolved; every entry verified strict over
  the generated PoE1 foreign-line corpus.
- Data: `data/repoe.manifest.json` classifies all 90 root-export entries;
  `data/poe1.snapshot.json` pins the seven used inputs. The current 3.30.0.4
  snapshot uses the root RePoE export; see
  [`POE1-DATA-SOURCES.md`](POE1-DATA-SOURCES.md).
  `poe1-build`, `poe1-matrix`, and `poe1-status` regenerate catalog, table,
  proof, and status.
- Web: game switch, lazy PoE1 data load/warm, per-game workbench and saved
  picks, share-link game id, and legacy PoE2 storage fallback.
- Import: normal/magic/rare equipment, fractured headers, corruption
  implicits, annotations, and `— Unscalable Value`; influenced, eldritch,
  crafted, and veiled sources are rejected with clear messages.
- Syntax: exclusions are `"!term"`; the per-game `QuerySyntax` profile
  is used by compile, paste, explain, import/reconstruction, and matching.
- Gems are MVP+1: copied gem text includes explanatory lines that can skew
  fragment selection. Flasks, jewels, influenced mods, crafts, and league
  mechanics remain phased afterward.
- PoE2 remains the default; PoE1 data loads lazily so the current app and PWA
  stay within size budgets.

## Recently completed

- Cross-term prefix/suffix factoring (STR-4): structured regex prefixes and
  value-bearing suffixes now factor at token boundaries. The 31–45 resistance
  state drops from 85 to 45 characters without changing its range or
  families; the representative added-damage exact query drops from 96 to 88.
  The suffix form does not beat the prefix form there because the enclosing
  parentheses cost more than the repeated `ks`. The broad category forms are
  parked as STR-5.
- Shell asset versioning (OPS-3): Cloudflare serves CSS/JS with a four-hour
  cache despite `_headers`; an old worker could pair new HTML with old CSS and
  leave the update toast unstyled. Non-fingerprinted CSS/JS now carry
  `?v=<commit>` in `index.html` and the precache manifest, and the update
  harness asserts the versioned URLs and the styled toast after switching
  builds.
- PWA update flow (UX-2): the new service worker waits instead of claiming or
  reloading open tabs. A non-blocking toast and a single Options Check/Update
  button offer Update now/Later or Check; the row says **An update is ready**
  once a worker is pending, and **Up to date**/**Retry** hold for 5 seconds.
  Automatic waits for 60s idle with a cancelable countdown. Share links suspend
  auto-update until the handoff is persisted and the hash is cleared. `sw.js`
  keeps cache-first navigation for the active shell, and
  `tools/pwa-update-check/run.mjs` covers manual, automatic, and shared-link
  updates plus the label/button state.
- Release docs gate (#96): the pre-merge checklist now requires WORKPLAN,
  ACCEPTANCE, and DESIGN/scope docs to move in the same PR as the change.
- PoE1 budget parity (#95): full-family pick markers and the full-family union
  path bring the real PoE1 10-slot state to 211/250, the MatchAll Boots variant
  to 234/250, and the narrowed Ring case to 238/250 without widening the Ring.
- STR-1 (#94): over-budget any-of groups merge with a per-class proof; PoE2
  253→183 and 325→186.
- Both-game data refresh: PoE1 moved back to the root RePoE export at
  3.30.0.4; PoE2 refreshed to the 2026-11-25 snapshot. Catalogs, corpora,
  exact tables, proofs, status, and the lazy PoE1 web payload were regenerated
  together. The release adds PoE1 tier-range plans, drops retired saved-pick
  ids with a notice, versions the range-aware snapshot schema to v7, and adds
  a compatibility gate to [`RELEASE-PREP.md`](RELEASE-PREP.md).
- P2 deployment: all augment target strings mapped; all runes, Soul Cores,
  Idols, Abyssal Eyes, and bonded effects added with cross-source dedup; the
  exact table regenerated with a stability pass. Production serves the merged
  P2 build.
- UI follow-ups: the Options popover sits above the compact result block,
  narrow family rows get a readable two-line layout, and augment targets are
  hidden from source tags while remaining in the data for filtering.
- RePoE audit refresh: the upstream PoB PoE2 tree gained PoE1-only files and
  dropped minified duplicates; the manifest now classifies the new entries and
  the audit passes again.
- Earlier UI follow-up PR: picked/excluded rows stay visible under search, common-only,
  and scope filters; the compact layout pins the search header/query/ruler while
  the tools scroll; Reset workbench is a visible danger button; and
  `common-picks --table` generates the contributor table with a CI drift gate.
- PR #76: RePoE manifest/audit/snapshot,
  basic rune augments, fail-closed exact solver timeout, claim-covered picker,
  data-driven common picks, and common-picks CI validation.
- Picker row metadata, touch hover/focus cleanup, and the common-picks
  contributor flow documented.

## Parked

### UI follow-ups

- **Reset workbench scope.** `#workbench-reset` clears persisted work and
  `State` (mode, slots, picks, test items) but not UI-local state such as the
  ModPicker search filter, show-all/expanded families, or open panels. Decide
  whether Reset should clear those too or be renamed/scoped.

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
| Augments / Idols | 313 released bases | 613 target rows / 497 families | Medium | High for socketed items | All runes, Soul Cores, Idols, Abyssal Eyes, bonded done; jewels/sockets parked |

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
- **Augments / Soul Cores** are sourced from RePoE `augments.json`, which maps
  each base to its target category and rendered effect lines. This pass covers
  all runes (including special/transformative and Legacy), 52 Soul Cores, 35
  Idols, and 4 Abyssal Eyes. All 29 target strings are mapped, including
  `All Equipment`, `Weapon`, shields/foci, specific slots, and compound
  targets. After target mapping and duplicate removal, 613 target rows across
  497 families remain. Bonded effects are a separate `bonded` source and are
  searchable when the copied item contains `Bonded: … (rune)`; they are
  Shaman-only in game. Source tags distinguish Rune, Soul Core, Idol, Abyssal
  Eye, and Bonded. Jewels, Congealed Mist, and the socket-count model remain
  parked. The RePoE inventory and PoB export classification are documented in
  [`REPOE-DATA.md`](REPOE-DATA.md).
- **Unique-item mods** are out of normal-rarity scope. **Essence/veiled**
  records are placeholders with no usable rendered text in the current export.

### Query compiler

- **Combined-slot fragment middle ground (STR-2).** A shared criterion across
  multiple classes now uses the global fragment, which is proven safe across
  the class union, rather than one class's shortest fragment. That is
  intentionally conservative and can be slightly longer than a class-specific
  minimal form. Revisit shorter shared-safe forms (for example, intersecting
  the class-safe forms or factoring per-class alternatives) after STR-1.
  Single-class queries already use the minimal class fragment.

### Ops decisions

- Keep the current Pages middleware or move the Pages-host block to Cloudflare
  Access. Access adds identity policies and audit logs but needs Access API
  permissions, a Worker service token, and a custom-domain bypass app. The
  middleware also provides the `pages.dev` → apex redirect, which Access does
  not.
- Retire the legacy PoE2 storage keys (`workbench.v1`, `saved-picks.v1`, and
  the pre-rename `recipes.v1`). They are still read as a compatibility
  fallback; removing them would drop work for anyone who has not opened the
  app since the per-game keys shipped. Keep them until a deliberate
  deprecation window is announced.

## Acceptance gates

- Release prep checklist: [`RELEASE-PREP.md`](RELEASE-PREP.md).
- Web acceptance: [`ACCEPTANCE.md`](ACCEPTANCE.md).
- Engine exact table/proof: [`EXACT-TABLE.md`](EXACT-TABLE.md) and
  [`CANDIDATE-STATUS-poe2.json`](CANDIDATE-STATUS-poe2.json).
- Import behavior and limitations: [`IMPORT.md`](IMPORT.md).

# Workplan

_Last updated: 2026-11-07._

Current planned and parked work. Behavior is documented in
[`DESIGN.md`](DESIGN.md), [`EXACT-TABLE.md`](EXACT-TABLE.md), and
[`../web/README.md`](../web/README.md); acceptance is in
[`ACCEPTANCE.md`](ACCEPTANCE.md).

## Current

- Production: <https://renlys.tools>. The live commit is in
  `/build-info.json`; the Pages project is the internal `renlys-recipes`
  project.
- App: Blazor WebAssembly workbench with saved picks, import, and test items.
- Engine: exact PoE2 table/proof is current: 1672/1672 scopes proven, not
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
  remain parked with jewel support. Augments, bonded modifiers, and jewel
  affixes remain unmodeled.
- Tests: 111 core, 375 web, 31 data-build; local suites green.
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
- Perf: a manual `web-perf` workflow builds the app, runs the 1920×1080
  harness, publishes the JSON/log artifact, and writes a step summary. It is
  observational; the local release-prep run remains the blocking gate.

## Next

No active next items. Catalog coverage and platform work are parked below.

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
| Augments / Soul Cores | 295 released bases | Unknown | High / blocked | High for socketed items | Parked |

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
- **Augments / Soul Cores** need a socket/effect model. The current raw export
  has 295 released Augment bases but no linked effect lines in `mods`, so this
  is not just another source. Parked and blocked on data.
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

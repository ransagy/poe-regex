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
- Engine: exact PoE2 table/proof is current: 1559/1559 scopes proven, not
  stale ([`CANDIDATE-STATUS-poe2.json`](CANDIDATE-STATUS-poe2.json)).
- Catalog: explicit prefixes/suffixes, Abyss desecrated item affixes, and base
  implicits. Implicits render in a dedicated **Implicits** section above the
  normal Prefixes/Suffixes columns with a muted-blue `<Implicit>` tag; only
  implicit-only patterns are included, so a line already covered by a normal
  prefix/suffix is not duplicated. Implicit scope is class-level. The PoE2
  export also carries legacy PoE1 `crafted` prefix/suffix records
  (Navali/Helena master crafts); they are excluded because they have no
  positive PoE2 base mapping. PoE2's actual crafted mods are jewel-only
  (`CraftedJewel*`, domain `misc`) and remain parked with jewel support.
  Corrupted mods, augments, bonded modifiers, and jewel affixes remain
  unmodeled.
- Tests: 111 core, 357 web, 27 data-build; local suites green.
- Deployment: Cloudflare Pages with branded previews, a Pages-host guard, and
  daily deployment cleanup. Production deploys are manual via
  `deploy-production`; previews remain automatic.
- PWA: installable offline shell with a build-versioned service-worker
  registration; a normal reload adopts a new build without a hard refresh,
  and installability, offline reload, and cache replacement pass locally.

## Next

1. **Implicits UX review:** check the dedicated Implicits section above the
   Prefixes/Suffixes columns, the muted-blue tag, and whether the section or a
   filter/tab reads better in the compact layout.
2. **Corrupted mods:** 108 raw records, 103 already mapped through
   `mods_by_base`; settle the header/rule semantics and add the ~29 new
   patterns. Corrupted mods are not normal prefixes/suffixes, so they need a
   separate kind rather than a source-only tag.
3. **Catalog/import shorthand:** close remaining shorthand gaps and make
   ambiguous import lines more deterministic.
4. **Web a11y follow-up:** color-vision emulation pass and compact-layout
   review.
5. **Web-perf smoke:** add the 1920×1080 harness to CI, or keep it documented
   as a manual gate.

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
| Base implicits | 168 ids / 348 base entries | 64 (70 mods) | Medium | High | Done |
| Corrupted mods | 108 records / 103 mapped | ~29 | Medium | Medium | Next |
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
- **Base implicits** are now modeled at class level: 70 catalog mods across 64
  implicit-only patterns and 13 classes. All 168 implicit ids are PoE2-specific
  by exact-id check. Lines already covered by a normal prefix/suffix were not
  duplicated; import keeps implicit lines quiet. The limitation remains that a
  class family does not prove a specific base type; full base-type fidelity
  needs a base selector and is parked with that work.
- **Corrupted mods** already have class mapping through `mods_by_base`; all
  108 ids are PoE2-specific by exact-id check. They need a separate corrupted
  kind, item-rule treatment, and their ~29 new patterns; do them next so the
  source/header plumbing is proven.
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

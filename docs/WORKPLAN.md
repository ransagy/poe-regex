# Workplan

_Last updated: 2026-11-06._

Current planned and parked work. Behavior is documented in
[`DESIGN.md`](DESIGN.md), [`EXACT-TABLE.md`](EXACT-TABLE.md), and
[`../web/README.md`](../web/README.md); acceptance is in
[`ACCEPTANCE.md`](ACCEPTANCE.md).

## Current

- Production: <https://renlys.tools>. The live commit is in
  `/build-info.json`; the Pages project is the internal `renlys-recipes`
  project.
- App: Blazor WebAssembly workbench with saved picks, import, and test items.
- Engine: exact PoE2 table/proof is current: 1565/1565 scopes proven, not
  stale ([`CANDIDATE-STATUS-poe2.json`](CANDIDATE-STATUS-poe2.json)).
- Catalog: explicit prefixes/suffixes, Abyss desecrated item affixes, and
  crafted prefixes/suffixes. Desecrated rows render with a green `<faction>`
  tag; crafted rows render with a muted-cyan `<Crafted>` tag. Crafted class
  scope is inferred at class level: defence crafts use the released base tags
  named by the raw `implicit_tags`, and generic meta-crafts apply to every
  normal equipment class. Implicits, corrupted mods, augments/Soul Cores, and
  jewel affixes remain unmodeled.
- Tests: 110 core, 355 web, 27 data-build; local suites green.
- Deployment: Cloudflare Pages with branded previews, a Pages-host guard, and
  daily deployment cleanup. Production deploys are manual via
  `deploy-production`; previews remain automatic.
- PWA: installable offline shell with a build-versioned service-worker
  registration; a normal reload adopts a new build without a hard refresh,
  and installability, offline reload, and cache replacement pass locally.

## Next

1. **Base implicits at class level:** add the implicit-only patterns (about 70
   new lines across 168 implicit ids) without duplicating the ~75 lines that
   already overlap normal explicit families. Keep the existing class-level
   limitation: the picker can only say the line exists on some base in the
   class, not on a specific base type.
2. **Corrupted mods:** 108 raw records, 103 already mapped through
   `mods_by_base`; settle the header/rule semantics and add the ~29 new
   patterns.
3. **Catalog/import shorthand:** close remaining implicit/shorthand gaps and
   make ambiguous import lines more deterministic.
4. **Web a11y follow-up:** color-vision emulation pass and compact-layout
   review.
5. **Web-perf smoke:** add the 1920×1080 harness to CI, or keep it documented
   as a manual gate.

## Parked

### Catalog coverage

Coverage order is driven by how much of the normal-equipment model each type
needs. The shared work for any catalog addition is the same: class mapping,
source-aware families, import behavior, catalog/matrix/table/proof rebuild,
stability pass, fragment report, and tests.

| Type | Raw surface | New patterns vs catalog | Effort | Impact | Status |
|---|---:|---:|---|---|---|
| Crafted bench mods | 9 records (3 stat, 6 meta) | 6 | Low–medium | Low | Done |
| Base implicits | 168 ids / 348 base entries | ~70 | Medium | High | Next |
| Corrupted mods | 108 records / 103 mapped | ~29 | Medium | Medium | After implicits |
| Jewels | 320 normal + 44 desecrated | ~196 + 34 | High | High for jewel users | Parked |
| Augments / Soul Cores | 295 released bases | Unknown | High / blocked | High for socketed items | Parked |

- **Crafted** is complete. The raw records have `default: 0` and no positive
  spawn tags, so class applicability is inferred rather than read: defence
  crafts match released base defence tags, and generic meta-crafts apply to
  every normal equipment class. This is deliberately class-level. The final
  crafted tag shade is still pending; the CSS uses a provisional muted cyan
  (`--crafted`) in the same tone family as the desecrated green.
- **Base implicits** are high impact because implicit lines are common on
  imported items. Most of the work is the ~70 implicit-only patterns; the
  ~75 overlapping lines already compile through the normal family. Add these
  at class level first and keep the documented limitation that a class family
  does not prove a specific base type. Full base-type fidelity needs a base
  selector and is parked with that work.
- **Corrupted mods** already have class mapping through `mods_by_base`. They
  need source-aware headers, item-rule treatment, and their ~29 new patterns;
  do them after implicits so the source/header plumbing is proven.
- **Jewels** need a separate item class/slot and `misc` domain mapping. Normal
  jewel mods and desecrated jewel mods are both outside the current scope.
  Parked until the item-class/slot work is justified.
- **Augments / Soul Cores** need a socket/effect model. The current raw export
  has 295 released Augment bases but no linked effect lines in `mods`, so this
  is not just another source. Parked and blocked on data.
- **Unique-item mods** are out of normal-rarity scope. **Essence/veiled**
  records are placeholders with no usable rendered text in the current export.

### Distribution and platform

- Browser AOT / smaller publish size (currently the unoptimized interpreter
  build, about 25 MB).
- Short-link / URL-share store. Current `#r=` snapshot links work; a compact
  or server-backed share layer is deferred.
- Accounts / OIDC. Design-only; would add a backend and the largest operational
  surface.

### Ops decisions

- Keep the current Pages middleware or move the Pages-host block to Cloudflare
  Access. Access adds identity policies and audit logs but needs Access API
  permissions, a Worker service token, and a custom-domain bypass app. The
  middleware also provides the `pages.dev` → apex redirect, which Access does
  not.
- Remove the legacy `poe-regex.recipes.v1` saved-picks key after the next
  release.

## Acceptance gates

- Web acceptance: [`ACCEPTANCE.md`](ACCEPTANCE.md).
- Engine exact table/proof: [`EXACT-TABLE.md`](EXACT-TABLE.md) and
  [`CANDIDATE-STATUS-poe2.json`](CANDIDATE-STATUS-poe2.json).
- Import behavior and limitations: [`IMPORT.md`](IMPORT.md).

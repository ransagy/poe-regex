# Project status

_Last updated: 2026-11-03._

This is the current-state ledger. Historical design/work logs and detailed
subsystem notes remain in the linked documents below.

## Current

- **Production:** <https://renlys.tools>. The live commit is in
  `/build-info.json`; the Pages project is the internal `renlys-recipes`
  project.
- **Web app:** "Renly's Tools", with **Saved picks** as the saved-build
  feature. Saved picks migrate from `poe-regex.recipes.v1` to
  `poe-regex.saved-picks.v1`.
- **Previews:** branded at `https://preview-<branch>.renlys.tools`; direct
  `*.renlys-recipes.pages.dev` hosts redirect (root) or return 404 (branch and
  hash hosts) unless the preview router secret is present.
- **Deployment:** Cloudflare Pages builds on watch-path changes; PR comments
  are branded; the daily `cleanup-deployments` workflow removes old
  production deployments, stale previews, and orphan `contrib-pr-*` branches.
- **Engine:** exact shortest-form table/proof is current: 892/892 scopes
  proven, not stale (`docs/CANDIDATE-STATUS-poe2.json`).
- **Tests:** 110 core, 324 web, 25 data-build; main CI green.

## Parked / planned

### Distribution and platform

- **PWA:** service worker + manifest for offline/installable use.
- **Browser AOT:** install the `wasm-tools` workload and ship an optimized
  publish instead of the unoptimized interpreter build (about 25 MB currently).
- **Short-link / URL-share store:** the current `#r=` snapshot links work;
  a compact or server-backed share layer is still deferred.
- **Accounts / OIDC:** design-only; would add a backend and the largest
  operational surface.

### Web app and UX

- Final viewport/a11y pass, especially compact laptop widths and
  color-vision-safe include/exclude states.
- Remaining picker/min-value polish from real use.
- Tie canonicalization and the optional web-perf CI smoke.

### QA and hygiene

- Manual in-game acceptance: slot footprints/backpack dimensions, rare-name
  and unique checks, and the `.`/`.*` newline behavior.
- Catalog fidelity follow-ups: implicits are not in the derived catalog and a
  few internal shorthand forms (`LightRadius`, `ManaLeech`) need mapping or
  documentation.
- Ambiguous import lines still surface as candidates rather than a guaranteed
  resolution.
- Remove the legacy `poe-regex.recipes.v1` saved-picks key after the next
  release.

### Ops decisions

- Decide whether to keep the current Pages middleware or move the Pages-host
  block to Cloudflare Access. Access adds identity policies and audit logs but
  needs Access API permissions, a Worker service token, and a custom-domain
  bypass app. The current middleware also provides the `pages.dev` → apex
  redirect, which Access does not.

## Accepted limitations

- Hybrid affixes that share a stat line can match either affix; quoted AND
  terms and range pinning were reviewed and parked.
- Two slots of the same class fall back from all-of to any-of with a visible
  warning.
- Rare-name/unique verification is manual.
- Implicits are intentionally unmapped.
- Approximate shorthand matches stay flagged.
- Minimum values remain expensive because they compile as near-whole-line
  text.

## Ledgers

- **Ops and deployment:** [`DEPLOYING-WEB.md`](DEPLOYING-WEB.md)
- **Historical web design/work log:** [`WEB-APP-PLAN.md`](WEB-APP-PLAN.md)
- **Engine exact table/proof:** [`CANDIDATE-STATUS-poe2.json`](CANDIDATE-STATUS-poe2.json),
  [`CANDIDATE-REPORT-poe2.md`](CANDIDATE-REPORT-poe2.md)
- **Architecture:** [`DESIGN.md`](DESIGN.md)
- **Web app details and known limitations:** [`../web/README.md`](../web/README.md)
- **Active engine plan:** maintained in the local OpenCode plan workspace
  (`poe2-shortest-legal-form-plan.md`); the repo-visible status above and
  `CANDIDATE-STATUS-poe2.json` are authoritative for shipped state.

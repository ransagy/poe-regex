# Workplan

_Last updated: 2026-11-05._

Current planned and parked work. Behavior is documented in
[`DESIGN.md`](DESIGN.md), [`EXACT-TABLE.md`](EXACT-TABLE.md), and
[`../web/README.md`](../web/README.md); acceptance is in
[`ACCEPTANCE.md`](ACCEPTANCE.md).

## Current

- Production: <https://renlys.tools>. The live commit is in
  `/build-info.json`; the Pages project is the internal `renlys-recipes`
  project.
- App: Blazor WebAssembly workbench with saved picks, import, and test items.
- Engine: exact PoE2 table/proof is current: 892/892 scopes proven, not stale
  ([`CANDIDATE-STATUS-poe2.json`](CANDIDATE-STATUS-poe2.json)).
- Tests: 110 core, 324 web, 25 data-build; main CI green.
- Deployment: Cloudflare Pages with branded previews, a Pages-host guard, and
  daily deployment cleanup. Production deploys are manual via
  `deploy-production`; previews remain automatic.
- PWA: installable offline shell with a build-versioned service worker;
  installability, offline reload, and cache replacement pass locally.

## Next

1. **Catalog/import fidelity:** close remaining implicit/shorthand gaps and
   make ambiguous import lines more deterministic.
2. **Web a11y follow-up:** color-vision emulation pass and compact-layout
   review.
3. **Web-perf smoke:** add the 1920×1080 harness to CI, or keep it documented
   as a manual gate.

## Parked

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

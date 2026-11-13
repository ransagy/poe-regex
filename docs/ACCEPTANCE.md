# Acceptance

_Last measured: 2026-11-13._

Acceptance criteria and current evidence for the web workbench. Engine proof is
covered separately in [`EXACT-TABLE.md`](EXACT-TABLE.md). The end-to-end gate
sequence is in [`RELEASE-PREP.md`](RELEASE-PREP.md).

## PoE1 MVP evidence

Measured on `feat/poe1-mvp` (2026-11-13), not yet deployed:

- Catalog: 23 equipment classes, 1,652 mods (1,204 normal, 215 implicit, 233
  corrupted).
- Exact table: 1,626 scopes (382 global, 1,244 class), 0 unresolved. The
  matrix proof checks 1,966 expanded scopes with 0 failures; `poe1-status
  --check` reports a non-stale table and a proven shortest form.
- Independent audit: `Poe1ArtifactTests` re-checks every shipped fragment
  against the generated foreign corpus plus the captured safety item.
- Regression: the captured boots item has `Sockets: G` and no movement speed;
  the multi-slot paste with a Boots movement-speed block must not match it.
  Class anchors are at least two characters, and `Gloves` uses `Gl`.
- Tests: 118 core + 439 web + 46 data-build pass.
- Browser smoke: the top-bar switch shows full game names on desktop and compact labels on phones; the title stays visible (wrapped on small screens); a
  dimmed loading overlay appears while PoE1 data loads; switching shows 23
  PoE1 classes; PoE1 exclusions compile as `"!term"`; PoE2 keeps `!"term"`;
  picks round-trip per game; a reload with PoE1 as the last game restores the
  overlay, engine, state, and query. PoE1 data is fetched lazily on first
  switch and is not in the install-time precache.
- PWA: service worker activates, manifest/installability checks pass, and the
  shell boots offline (68 precached entries; PoE1 data is cached on first
  switch).
- Perf at 1920×1080: boot p50 1265 ms / p95 1333 ms (budget 2500 ms); long
  tasks 14 over 50 ms, 0 over 200 ms, max 91 ms.

## Performance

Harness: [`../tools/web-perf/run.mjs`](../tools/web-perf/run.mjs). Published
Release wwwroot, headless Chromium, cache disabled.

### 1920×1080

| Action | p50 | p95 | Budget |
|---|---:|---:|---:|
| boot to interactive | 1245 ms | 1281 ms | < 2500 ms |
| bulk add `++` | 43 ms | 45 ms | < 50 ms |
| mode: advanced | 37 ms | 43 ms | < 50 ms |
| builder open | 44 ms | 46 ms | < 50 ms |
| 10-slot paste update | 70 ms | 71 ms | < 50 ms |
| explain cold | 93 ms | 100 ms | < 500 ms |
| import parse 5 items | 36 ms | 44 ms | < 100 ms |

Advanced per-slot picks: **35–82 ms**. Long tasks: **22 over 50 ms, 0 over
200 ms, max 88 ms**.

The larger augment catalog and exact table raised boot to about 1.25 s, still
under the 2.5 s budget. The All-gear union now warms in sliced chunks, so it
no longer produces a 200 ms+ task while the picker is opening; mode advanced
and bulk add are inside the 50 ms target. The 10-slot paste update and some
per-slot picks remain slightly over; the budget is a target, not a hard
failure.

### Narrow and low-end

```text
900×1200: boot 1249 ms · bulk add 42 ms · advanced picks 40–88 ms
          explain 97 ms · import 39 ms · max long task 90 ms

4× CPU:   boot 5.6 s · bulk add 244 ms · advanced picks 129–329 ms
          explain 422 ms · import 166 ms · max long task 541 ms
```

The 4× CPU sample is one run on the current host and is informational; rerun
it on a quiet host before treating it as a regression signal.

## Accessibility

- Lighthouse accessibility on the initial page after the color pass: **100**.
- axe-core WCAG 2.1 A/AA: **0 violations** on the initial page and on the
  import string, saved picks, options, test items, import item, builder, and
  "what it matches" states.
- Focus enters the first control, Tab wraps inside the import-string,
  import-item, and builder dialogs, and Escape closes and restores the opener
  for those dialogs plus saved picks, options, and help.
- Include/exclude states use icon/text (`✓` / `⊘`, `picked` / `excluded`) in
  addition to color.
- Light-theme source tags meet 4.5:1 on raised/surface; dark secondary text
  meets 4.5:1. The unique rarity hex is unchanged and carries a dark edge
  shadow; the subline also spells out the rarity.
- Color-vision simulation (protanopia/deuteranopia/tritanopia) completed;
  source tags and state cues remain text/icon labeled.
- Compact layout reviewed at 390×844 and 768×1024: search/clear/Add all,
  dialogs, paper doll, and picks remain reachable without horizontal overflow.

## PWA

- Manifest parses with no errors; Chrome reports no installability errors.
- Service worker activates and controls the page.
- The cache is keyed by the build commit and contains the app shell plus
  `_framework` assets.
- After one online load, a reload with the network offline boots the app and
  restores the workbench.
- A new build changes the versioned `/sw.js?v=<commit>` registration URL,
  installs a new cache, and deletes the previous one on activation.
- A controlled page reloads once after the new worker claims it, so a normal
  reload picks up new CSS/JS without a hard refresh. The same build does not
  reload again, and registration is skipped on `localhost`/`127.0.0.1`.
- `build.sh` generates `precache-manifest.json` and substitutes the commit
  into the registration; `_headers` marks the manifest, precache manifest,
  build info, CSS, JS, and `web.styles.css` no-cache, and `_framework/*`
  immutable.
- Icon fallbacks: `favicon.ico`, SVG favicon, 180×180 Apple touch icon, and
  manifest 192/512/maskable icons. Firefox desktop has no PWA install UI; it
  uses the normal site icon. Firefox Android uses the manifest icons for
  “Add to home screen.”

Check with [`tools/pwa-check/run.mjs`](../tools/pwa-check/run.mjs):

```sh
node tools/pwa-check/run.mjs --root /tmp/webpwa/wwwroot
```

## Out of scope

The exact-table proof is complete for the modeled corpus plus the captured
safety fixtures. Desecrated item affixes, implicit-only base families,
corrupted enchantments, all runes (including special/transformative and
Legacy), Soul Cores, Idols, Abyssal Eyes, and bonded effects are modeled; PoE1
`crafted` leftovers are excluded, and PoE2's actual crafted jewel mods are not
modeled. Unmodeled unique/rare names, unique-only text, flavour, jewel text,
and some socketable/base text can still make an item highlight. This is accepted and
out of scope; therefore there are **no manual in-game acceptance checks**.

## Accepted limitations

- Hybrid affixes that share a stat line can match either affix; quoted AND
  terms and range pinning are parked.
- Two slots of the same class fall back from all-of to any-of with a visible
  warning.
- Implicit-only base families are class-level and their import lines stay
  quiet; approximate shorthand matches stay flagged.
- Corrupted enchantments are picker/builder-only on import: game copy uses
  implicit headers, so those lines stay quiet. The builder allows at most two;
  five raw records without a positive reference are excluded.
- Normal-gear crafted mods are not modeled: the export's `crafted` prefix/
  suffix records are PoE1 legacy, and actual PoE2 crafted mods are jewel-only.
- Ambiguous import lines surface as candidates rather than a guaranteed
  resolution.
- Minimum values compile as near-whole-line text and remain expensive.
- Publish is the unoptimized interpreter build (about 25 MB).

## Reproduce

```sh
dotnet publish web/web.csproj -c Release -o /tmp/webperf \
  -p:BlazorEnableCompression=false

node tools/web-perf/run.mjs --root /tmp/webperf/wwwroot --runs 3 \
  --width 1920 --height 1080 --out /tmp/perf-1920.json

node tools/web-perf/run.mjs --root /tmp/webperf/wwwroot --runs 3 \
  --width 900 --height 1200 --out /tmp/perf-900.json

node tools/web-perf/run.mjs --root /tmp/webperf/wwwroot --runs 1 \
  --width 1920 --height 1080 --throttle 4 --out /tmp/perf-4x.json
```

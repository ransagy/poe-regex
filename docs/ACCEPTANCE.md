# Acceptance

_Last measured: 2026-11-13; 3.30 catalog counts updated 2026-11-15._

Acceptance criteria and current evidence for the web workbench. Engine proof is
covered separately in [`EXACT-TABLE.md`](EXACT-TABLE.md). The end-to-end gate
sequence is in [`RELEASE-PREP.md`](RELEASE-PREP.md).

## PoE1 MVP evidence

Measured on the PoE1 MVP branch (2026-11-13), shipping with the PoE1 3.30
update; the 3.30 catalog is sourced from ExileData 3.30.0.2:

- Catalog: 23 equipment classes, 1,612 mods (1,200 normal, 179 implicit, 233
  corrupted).
- Exact table: 1,585 scopes (367 global, 1,218 class), 0 unresolved. The
  matrix proof checks 1,918 expanded scopes with 0 failures; `poe1-status
  --check` reports a non-stale table and a proven shortest form.
- Independent audit: `Poe1ArtifactTests` re-checks every shipped fragment
  against the generated foreign corpus plus the captured safety item.
- Regression: the captured boots item has `Sockets: G` and no movement speed;
  the multi-slot paste with a Boots movement-speed block must not match it.
  Class anchors are at least two characters, and `Gloves` uses `Gl`.
- Import audit: every PoE1 catalog mod/class/display form maps back to its
  source mod (18,666 forms, 0 failures).
- Tests: 118 core + 444 web + 52 data-build pass.
- Browser smoke: the top-bar switch shows full game names on desktop and compact labels on phones; the title stays visible (wrapped on small screens); a
  dimmed loading overlay appears while PoE1 data loads; switching shows 23
  PoE1 classes; both games compile exclusions as `"!term"`;
  picks round-trip per game; a reload with PoE1 as the last game restores the
  overlay, engine, state, and query. PoE1 data is fetched lazily on first
  switch and is not in the install-time precache.
- PWA: service worker activates, manifest/installability checks pass, and the
  shell boots offline (68 precached entries; PoE1 data is cached on first
  switch).
- Perf at 1920×1080: boot p50 1265 ms / p95 1333 ms (budget 2500 ms); long
  tasks 14 over 50 ms, 0 over 200 ms, max 91 ms.

## PoE2 tier ranges (prototype)

- Tier picks in a multi-value family (movement speed, maximum life) use one
  combined family+range fragment for both exact and up tiers. A single-value
  family (`+1 Skills`) keeps the shorter affix-name form. The pick row shows
  the explicit range, a legacy name-up row shows the compact equivalent range
  (`= 20–35% increased Movement Speed`), and the breakdown shows the compiled
  term.
- Min/max boxes appear when the pick covers more than one numeric value.
  A single fixed value (one fixed tier or `+1 Skills`) hides both; an exact
  ranged tier in a multi-value family uses the range form, and a legacy exact
  name pick converts on the first filter. Integer stats use whole numbers and
  decimal stats keep the displayed precision.
- The range fragment is proven against the catalog: every matched line must
  be an allowed pattern and its first roll must sit inside the requested
  intervals. A range endpoint in `roll(low-high)` cannot satisfy the roll.
- Cross-slot tier ranges group by covered intervals and min/max, so the same
  tier range on different classes shares one group; different ranges stay
  separate and do not cross-match.
- Tests cover boots movement speed (fixed tiers), body/helmet maximum life
  (integer ranges), body armour life regeneration (decimal advanced range),
  cross-width ranges, the Athlete’s/Fecund slot matrix, and both exclusion
  shapes.
- Current Release suite: 126 core + 467 web + 53 data-build; the published
  PWA shell boots and reloads offline with the new picker.
- Perf smoke (published Release, one run): boot 1459 ms; picker expand 23 ms;
  advanced family pick 88 ms; longest task 100 ms, none over 200 ms.

## Performance

Harness: [`../tools/web-perf/run.mjs`](../tools/web-perf/run.mjs). Published
Release wwwroot, headless Chromium, cache disabled.

### 1920×1080

| Action | p50 | p95 | Budget |
|---|---:|---:|---:|
| boot to interactive | 1408 ms | 1428 ms | < 2500 ms |
| bulk add `++` | 46 ms | 48 ms | < 50 ms |
| mode: advanced | 53 ms | 87 ms | < 50 ms |
| builder open | 49 ms | 53 ms | < 50 ms |
| 10-slot paste update | 80 ms | 82 ms | < 50 ms |
| explain cold | 99 ms | 114 ms | < 500 ms |
| import parse 5 items | 41 ms | 48 ms | < 100 ms |

Advanced per-slot picks: **35–82 ms**. Long tasks: **29 over 50 ms, 0 over
200 ms, max 103 ms**.

The PoE1 profile, lazy data loader, and game rail raised boot to about 1.4 s,
still under the 2.5 s budget. Bulk add, builder open, explain, and import are
within their targets; mode advanced, the 10-slot paste update, and some
per-slot picks remain slightly over the 50 ms target, consistent with the
existing per-slot variance. The budget is a target, not a hard failure.

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

- Hybrid affixes that share a stat line can match either affix. Range pinning
  is implemented for single-line numeric tiers and family picks through one
  combined family+range fragment; multi-line hybrid range pinning remains
  parked.
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

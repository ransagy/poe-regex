# Acceptance

_Last measured: 2026-11-04._

Acceptance criteria and current evidence for the web workbench. Engine proof is
covered separately in [`EXACT-TABLE.md`](EXACT-TABLE.md). The end-to-end gate
sequence is in [`RELEASE-PREP.md`](RELEASE-PREP.md).

## Performance

Harness: [`../tools/web-perf/run.mjs`](../tools/web-perf/run.mjs). Published
Release wwwroot, headless Chromium, cache disabled.

### 1920×1080

| Action | p50 | p95 | Budget |
|---|---:|---:|---:|
| boot to interactive | 938 ms | 960 ms | < 2500 ms |
| bulk add `++` | 60 ms | 60 ms | < 50 ms |
| mode: advanced | 47 ms | 51 ms | < 50 ms |
| builder open | 43 ms | 46 ms | < 50 ms |
| 10-slot paste update | 46 ms | 46 ms | < 50 ms |
| explain cold | 94 ms | 94 ms | < 500 ms |
| import parse 5 items | 62 ms | 66 ms | < 100 ms |

Advanced per-slot picks: **51–65 ms**. Long tasks: **6 over 50 ms, 0 over
200 ms, max 87 ms**.

Bulk add and advanced picks are slightly over the 50 ms target; they do not
produce long-task spikes. The budget is a target, not a hard failure.

### Narrow and low-end

```text
900×1200: boot 950 ms · bulk add 59 ms · advanced picks 53–65 ms
          explain 94 ms · import 64 ms · max long task 87 ms

4× CPU:   boot 3.96 s · bulk add 267 ms · advanced picks 108–228 ms
          explain 389 ms · import 273 ms · max long task 355 ms
```

## Accessibility

- Lighthouse accessibility on the initial page: **100**.
- axe-core WCAG 2.1 A/AA: **0 violations** on the initial page and on the
  import string, saved picks, options, test items, import item, builder, and
  "what it matches" states.
- Focus enters the first control, Tab wraps inside the import-string,
  import-item, and builder dialogs, and Escape closes and restores the opener
  for those dialogs plus saved picks, options, and help.
- Include/exclude states use icon/text (`✓` / `⊘`, `picked` / `excluded`) in
  addition to color.
- Color-vision emulation is still a manual browser pass.

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
safety fixtures. Desecrated item affixes are modeled; PoE1 `crafted`
leftovers are excluded, and PoE2's actual crafted jewel mods are not modeled.
Unmodeled unique/rare names, unique-only text, flavour, augments/bonded text,
and some socketable/base text can still make an item highlight. This is
accepted and out of scope; therefore there are **no manual in-game acceptance
checks**.

## Accepted limitations

- Hybrid affixes that share a stat line can match either affix; quoted AND
  terms and range pinning are parked.
- Two slots of the same class fall back from all-of to any-of with a visible
  warning.
- Implicits are intentionally unmapped; approximate shorthand matches stay
  flagged.
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

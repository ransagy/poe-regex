# Web performance report — PoE2 workbench

Measured with `tools/web-perf/run.mjs` against a locally served Release
publish. The harness drives headless Chromium over CDP, disables cache, waits
for DOM stability after each action, and reports p50/p95 over repeated runs.

## Environment

```text
viewport:       1920×1080 (primary), 900×1200 (narrow)
runs:           3 normal, 1 at 4× CPU throttle
chromium:       headless, --disable-gpu, cache disabled
artifact:       published Release wwwroot
```

## Primary results — 1920×1080

| Action | p50 | p95 | Budget |
|---|---:|---:|---|
| boot to interactive (test area collapsed) | 931 ms | 943 ms | < 2500 ms |
| mode: advanced | 36 ms | 39 ms | < 50 ms |
| slot focus | 38 ms | 38 ms | < 50 ms |
| mode: simple | 11 ms | 12 ms | < 50 ms |
| picker expand | 10 ms | 11 ms | < 50 ms |
| picker collapse | 9 ms | 11 ms | < 50 ms |
| picker filter | 34 ms | 35 ms | < 50 ms |
| family pick | 27 ms | 30 ms | < 50 ms |
| exclusion pick | 30 ms | 31 ms | < 50 ms |
| copy result | 7 ms | 20 ms | < 50 ms |
| details open | 24 ms | 24 ms | < 50 ms |
| explain cold | 148 ms | 151 ms | < 500 ms |
| explain close | 2 ms | 2 ms | < 50 ms |
| explain warm | 2 ms | 2 ms | < 50 ms |
| details close | 20 ms | 20 ms | < 50 ms |
| clear picks | 17 ms | 18 ms | < 50 ms |
| bulk add `++` | 45 ms | 46 ms | < 50 ms |
| query import open | 17 ms | 18 ms | < 50 ms |
| query import close | 16 ms | 16 ms | < 50 ms |
| import open | 18 ms | 18 ms | < 50 ms |
| import parse 5 items | 63 ms | 65 ms | < 100 ms |
| import cancel | 18 ms | 18 ms | < 50 ms |
| builder open | 42 ms | 42 ms | < 50 ms |
| builder cancel | 20 ms | 21 ms | < 50 ms |
| test area open | 23 ms | 23 ms | < 50 ms |
| test area close | 22 ms | 22 ms | < 50 ms |

### Advanced mode, 1–10 slots

| Action | p50 | p95 |
|---|---:|---:|
| advanced switch | 11 ms | 11 ms |
| focus helmet | 7 ms | 9 ms |
| pick helmet | 50 ms | 51 ms |
| pick body | 27 ms | 28 ms |
| pick gloves | 26 ms | 32 ms |
| pick boots | 19 ms | 20 ms |
| pick weapon (10-slot state) | 58 ms | 59 ms |
| pick offhand / belt / amulet / ring | 58–60 ms | 59–64 ms |
| details open (advanced) | 17 ms | 19 ms |
| explain helmet cold | 47 ms | 49 ms |
| details close (advanced) | 14 ms | 15 ms |
| 10-slot paste update | 36 ms | 37 ms |
| class change (10 slots) | 40 ms | 40 ms |

Long tasks: 6 over 50 ms, max 142 ms, **0 over 200 ms** after boot.

## Narrow viewport — 900×1200

The same action set is effectively unchanged:

```text
boot 919 ms · filter 35 ms · family pick 33 ms · explain cold 148 ms
details open 22 ms · import parse 5 items 65 ms · builder open 38 ms
no long task > 200 ms
```

## 4× CPU throttle — 1920×1080

This is a deliberately harsh low-end pass:

```text
boot 3.9 s · picker filter 147 ms · family pick 155 ms
explain cold 634 ms · import parse 5 items 272 ms
builder open 171 ms · advanced helmet pick 237 ms
10-slot paste update 154 ms · class change 177 ms
max long task 586 ms (two over 200 ms)
```

Normal actions remain usable; cold explain and import are the expected
low-end costs. The 4× pass is a diagnostic, not the primary budget.

## What changed

- Stable `EngineService` mod-list caches avoid rebuilding lists on every
  render.
- Shared `ModFamilies` grouping cache avoids regrouping the same scope on
  mode/slot switches.
- Tier search text is cached once per grouping; filter keystrokes no longer
  rescan every tier's members.
- Explain now uses the catalog-backed explain corpus and literal prefilters;
  the cold simple-mode explain dropped from ~15.7 s to ~141 ms.
- The global union's suffix indexes are lazy; explain only builds the line
  list it needs.
- Class alternation results are cached, and the runtime combines
  precomputed class groups without re-validating them.
- Literal class prefixes and verified multi-class groups ship in the table,
  so paste composition never rebuilds the vendor-derived class corpus at
  runtime.
- The post-#24 layout keeps boot and normal actions within budget. The harness
  now targets the current search bar, pick breakdown, drawers, query cards,
  and class selector, and adds details open/close and query-import actions.
- Chip/modal rendering benefits from the same state/grouping caches.

## Reproduce

```sh
dotnet publish web/web.csproj -c Release -o /tmp/webperf -p:BlazorEnableCompression=false
node tools/web-perf/run.mjs --root /tmp/webperf/wwwroot --runs 5 \
  --width 1920 --height 1080 --out /tmp/poe-web-perf.json
```

Optional low-end pass:

```sh
node tools/web-perf/run.mjs --root /tmp/webperf/wwwroot --runs 1 \
  --width 1920 --height 1080 --throttle 4 --out /tmp/poe-web-perf-4x.json
```

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
| boot to interactive (test area collapsed) | 931 ms | — | < 2500 ms |
| mode: advanced | 41 ms | 43 ms | < 50 ms |
| slot focus | 40 ms | 42 ms | < 50 ms |
| mode: simple | 11 ms | 11 ms | < 50 ms |
| picker expand | 10 ms | 10 ms | < 50 ms |
| picker collapse | 9 ms | 9 ms | < 50 ms |
| picker filter | 35 ms | 35 ms | < 50 ms |
| family pick | 24 ms | 25 ms | < 50 ms |
| exclusion pick | 20 ms | 23 ms | < 50 ms |
| copy result | 32 ms | 33 ms | < 50 ms |
| clear picks | 14 ms | 14 ms | < 50 ms |
| bulk add `++` | 37 ms | 39 ms | < 50 ms |
| explain cold | 144 ms | 147 ms | < 500 ms |
| explain warm | 1 ms | 1 ms | < 50 ms |
| import open | 15 ms | 16 ms | < 50 ms |
| import parse 5 items | 64 ms | 65 ms | < 100 ms |
| import cancel | 13 ms | 14 ms | < 50 ms |
| builder open | 38 ms | 39 ms | < 50 ms |
| builder cancel | 14 ms | 15 ms | < 50 ms |
| test area open | 18 ms | 19 ms | < 50 ms |
| test area close | 17 ms | 17 ms | < 50 ms |

### Advanced mode, 1–10 slots

| Action | p50 | p95 |
|---|---:|---:|
| advanced switch | 12 ms | 12 ms |
| focus helmet | 6 ms | 7 ms |
| pick helmet | 45 ms | 47 ms |
| pick body | 23 ms | 27 ms |
| pick gloves | 25 ms | 27 ms |
| pick boots | 19 ms | 19 ms |
| pick weapon (10-slot state) | 57 ms | 58 ms |
| pick offhand / belt / amulet / ring | 56–63 ms | 56–66 ms |
| 10-slot paste update | 27 ms | 28 ms |
| class change (10 slots) | 39 ms | 42 ms |
| explain helmet cold | 47 ms | 47 ms |

Long tasks: 6 over 50 ms, max 144 ms, **0 over 200 ms** after boot.

## Narrow viewport — 900×1200

The same action set is effectively unchanged:

```text
boot 961 ms · filter 38 ms · family pick 24 ms · explain cold 153 ms
import parse 5 items 80 ms · builder open 36 ms · no long task > 200 ms
```

## 4× CPU throttle — 1920×1080

This is a deliberately harsh low-end pass:

```text
boot 4.0 s · picker filter 143 ms · family pick 95 ms
explain cold 609 ms · import parse 5 items 279 ms
builder open 154 ms · advanced helmet pick 195 ms
10-slot paste update 127 ms · class change 162 ms
max long task 596 ms
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

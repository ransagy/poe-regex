# QA and performance — PoE2 workbench

_Last run: 2026-11-04. Baseline captured before the prewarm debounce; the
post-fix numbers are below._

This is the quality-gate ledger for the web workbench. It records automated
browser measurements, accessibility/viewport checks, and the manual in-game
checks that still need a maintainer.

## Environment

```text
artifact:   published Release wwwroot
browser:    headless Chromium, --disable-gpu, cache disabled
viewport:   1920x1080 (primary), 900x1200 (narrow)
runs:       3 normal at each viewport, 1 at 4x CPU throttle
harness:    tools/web-perf/run.mjs
```

## Baseline before the prewarm debounce

### 1920×1080

| Action | p50 | p95 | Budget | Status |
|---|---:|---:|---:|---|
| boot to interactive | 981 ms | 990 ms | < 2500 ms | pass |
| mode: advanced | 53 ms | 59 ms | < 50 ms | **over** |
| slot focus | 46 ms | 49 ms | < 50 ms | pass |
| picker filter | 37 ms | 37 ms | < 50 ms | pass |
| family pick | 36 ms | 49 ms | < 50 ms | pass |
| explain cold | 94 ms | 95 ms | < 500 ms | pass |
| import parse 5 items | 68 ms | 70 ms | < 100 ms | pass |
| builder open | 52 ms | 92 ms | < 50 ms | **over p95** |
| bulk add `++` | **386 ms** | 388 ms | < 50 ms | **regression** |
| 10-slot paste update | 53 ms | 53 ms | < 50 ms | slightly over |
| class change | 48 ms | 52 ms | < 50 ms | pass p50 |

Advanced per-slot picks:

| Slot | p50 |
|---|---:|
| helmet | 62 ms |
| body | 32 ms |
| gloves | 39 ms |
| boots | 27 ms |
| weapon | 72 ms |
| offhand | 77 ms |
| belt | 73 ms |
| amulet | 79 ms |
| ring 1 | 84 ms |
| ring 2 | 70 ms |

Long tasks: **9 over 50 ms, 3 over 200 ms, max 317 ms**.

### 900×1200

Same shape:

```text
boot 978 ms · bulk add 360 ms · advanced picks 59–84 ms
explain cold 98 ms · import 67 ms · long tasks 8 over 50, 3 over 200, max 304 ms
```

### 4× CPU throttle

```text
boot 4.15 s · bulk add 1.50 s · advanced pick 138–262 ms
explain cold 415 ms · import 323 ms · max long task 1.24 s
```

## After the prewarm debounce

The prewarm now waits for 250 ms of quiet and is cancelled by newer state
changes. This keeps the corpus warm-up from competing with the visible update.

### 1920×1080

| Action | p50 | p95 |
|---|---:|---:|
| boot | 938 ms | 960 ms |
| bulk add `++` | **60 ms** | 60 ms |
| mode: advanced | 47 ms | 51 ms |
| builder open | 43 ms | 46 ms |
| 10-slot paste update | 46 ms | 46 ms |
| explain cold | 94 ms | 94 ms |
| import parse 5 items | 62 ms | 66 ms |

Advanced picks: 51–65 ms. Long tasks: **6 over 50 ms, 0 over 200 ms, max 87 ms**.

### 900×1200

```text
boot 950 ms · bulk add 59 ms · advanced picks 53–65 ms
explain cold 94 ms · import 64 ms · long tasks 6 over 50, 0 over 200, max 87 ms
```

### 4× CPU throttle

```text
boot 3.96 s · bulk add 267 ms · advanced pick 108–228 ms
explain cold 389 ms · import 273 ms · max long task 355 ms
```

## Regression against the 2026-10-30 report

| Metric | Old | Before fix | After fix |
|---|---:|---:|---:|
| bulk add `++` | 50 ms | 386 ms | 60 ms |
| explain cold | 149 ms | 94 ms | 94 ms |
| advanced weapon pick | 58 ms | 72 ms | 55 ms |
| long tasks over 200 ms | 0 | 3 | 0 |
| 4× max long task | 584 ms | 1241 ms | 355 ms |

The bulk-add regression was caused by prewarm work competing with the visible
update. Debouncing the warm-up restored the action budget and removed the
long-task spike. Advanced picks are still a little over 50 ms but are much
closer; the 4× numbers are diagnostic.

## Accessibility and viewport

- Lighthouse accessibility score on the initial page: **100**.
- axe-core WCAG 2.1 A/AA: **0 violations** on the initial page and on the
  import string, saved picks, options, test items, import item, builder, and
  "what it matches" states.
- 1920×1080 and 900×1200 screenshots show no horizontal overflow; the narrow
  layout stacks the intro/search/picker cleanly.
- Include/exclude states use icon/text (`✓` / `⊘`, `picked` / `excluded`) in
  addition to color, but a color-vision-emulation pass is still needed.
- Keyboard checks: focus enters the first control, Tab wraps inside the
  import-string, import-item, and builder dialogs, and Escape closes and
  restores the opener for those dialogs plus saved picks, options, and help.
- Fixed in this pass: backpack `role="group"`, options-note contrast,
  `aria-modal` on the three import/builder dialogs, Escape handling, focus
  traps, and opener restoration.

## In-game acceptance plan

**Goal:** verify only the assumptions the catalog, table audit, and test suite
cannot prove inside the game client.

**Before starting**

- English PoE2 client; record the client patch and the app data version
  (`2026-08-09` currently).
- Use <https://renlys.tools> in a fresh browser profile.
- Have the five fixtures in `docs/samples/real-items.txt`, plus one live rare
  and one live unique item.

### Already covered — no in-game re-check needed

| Area | Evidence |
|---|---|
| Backpack dimensions and footprints | Maintainer-confirmed table in `WEB-APP-PLAN` §9.2; `FootprintTests` and placement tests |
| Unknown footprint fallback | Intentional `1x3` fallback with `Unknown` confidence |
| `.` / `.*` line behavior | In-game `Charm.+Thorns` on the Wide Belt did not match, so `.` is line-scoped; the shipped table is line-local and the .NET audits cover the rest |
| Import fidelity | Five real fixtures plus the >1,000-form catalog round-trip audit |
| Saved picks and share links | Persistence, component, and codec tests |
| Auto-copy | Preference and UI tests; it is browser behavior, not in-game behavior |
| Compact viewport and color vision | Browser QA, tracked as a separate a11y follow-up |

### Remaining manual in-game checks

| ID | Check | How | Pass criteria |
|---|---|---|---|
| IG-01 | Rare and unique names | Import one live rare and one live unique item; search a name fragment from each | Card/tooltip name, class, rarity, and mods agree; the intended item matches and unrelated items do not |
| IG-02a | Simple/global fragment | Build an all-gear query, copy, paste in the Highlight box | Expected items highlight; no false positives |
| IG-02b | Advanced class anchor | Build a per-slot query with a class anchor and paste it | Expected class and mods match |
| IG-02c | Exclusion + all-of | Build a query with an exclusion and an all-of pair | Excluded items stay dark; all-of requires both picks |
| IG-02d | Minimum value | Add a min value and paste the query | Threshold matches the intended roll range |
| IG-03 | Optional: visual footprint spot-check | Import one item per unusual footprint (buckler, armour shield, staff/spear, wand) | Grid placement matches the game inventory |
| IG-04 | Optional: new live item | Import a live item outside the five fixtures | Mapping is correct or explicitly flagged |

**Record**

| ID | Client patch | Expected | Actual | Result | Notes |
|---|---|---|---|---|---|
|  |  |  |  |  |  |

**Exit criteria**

- IG-01, IG-02a, IG-02b, IG-02c, and IG-02d pass.
- Any mismatch has a filed issue and a documented workaround.
- If a new wildcard case contradicts the line-local assumption, stop and
  update the grammar profile before calling the release complete.
- Record the final results here and update [`STATUS.md`](STATUS.md) if the
  shipped model changes.

## Follow-ups

- [ ] Add the 1920×1080 perf run to CI as a smoke check, or document the
      manual command if the runtime cost is too high.
- [ ] Keep an eye on advanced picks (51–65 ms) and 4× CPU numbers after
      future UI changes.
- [ ] Run the color-vision-emulation pass.
- [ ] Record the manual in-game results above.

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

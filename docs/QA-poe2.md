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

**Goal:** verify the assumptions the app cannot prove from the catalog: slot
footprints, rare/unique naming, line-local matching, and the final copy/paste
round trip.

**Before starting**

- English PoE2 client; record the client patch and the app data version
  (`2026-08-09` currently).
- Use <https://renlys.tools> in a fresh browser profile.
- Have `docs/samples/real-items.txt` available, plus 2–3 live items per tested
  class.
- Capture the item copy text, app screenshot, in-game search, and result.

**Test matrix**

| ID | Check | How | Pass criteria |
|---|---|---|---|
| IG-01 | Backpack dimensions | Open the test-items drawer; compare to the in-game inventory | 12×5 cells, same orientation |
| IG-02 | Footprints | Import belt, buckler, armour shield, 1H mace, wand, staff/spear, armour, accessory | Each item occupies the expected cells |
| IG-03 | Unknown footprint fallback | Import a class/item without a confirmed footprint | Flagged fallback is visible and not silently wrong |
| IG-04 | Rare name/card | Import a rare item | Name, class, rarity, mods, and tooltip agree |
| IG-05 | Unique name/card | Import a unique item | Name/caption and tooltip agree |
| IG-06 | `.` wildcard | Search a generated query containing `.` | Matches only the intended line, no cross-line hit |
| IG-07 | `.*` wildcard | Search a generated query containing `.*` | Matches only the intended line, no cross-line hit |
| IG-08 | Simple copy/paste | Build an all-gear query, copy, paste in game | Expected items highlight; others do not |
| IG-09 | Advanced anchors | Build a per-slot query with class anchors | Expected class and mods match |
| IG-10 | Exclusions/all-of | Build a query with exclusions and all-of | Excluded items stay dark; all-of requires every pick |
| IG-11 | Minimum value | Add a min value and search | Threshold matches the intended roll range |
| IG-12 | Import fidelity | Import items with implicit, socket, corrupted, and granted-skill lines | Mapping, tooltip, and highlight agree; ambiguous lines are flagged |
| IG-13 | Saved picks | Save, load, delete, undo | State returns exactly; no stray picks |
| IG-14 | Share link | Open a share link in a second browser | Picks restore; backpack is preserved |
| IG-15 | Compact viewport | 1280×720 and 900×1200; color-vision emulation | No horizontal overflow; include/exclude states distinguishable |
| IG-16 | Auto-copy | Enable “Copy on every change” and edit picks | Clipboard updates only after a user gesture |

**Record**

| ID | Client patch | Expected | Actual | Result | Notes |
|---|---|---|---|---|---|
|  |  |  |  |  |  |

**Exit criteria**

- IG-01, IG-02, IG-04, IG-05, IG-06, IG-07, IG-08, IG-09, and IG-12 pass.
- Any mismatch has a filed issue and a documented workaround.
- If `.`/`.*` semantics differ, update the grammar profile and the active
  engine plan before calling the release complete.
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

# QA and performance — PoE2 workbench

_Last run: 2026-11-04. App code unchanged since `aa609f1`; measured from the
current `main` publish._

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

## Automated performance

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

## Regression against the 2026-10-30 report

| Metric | Old | Current |
|---|---:|---:|
| bulk add `++` | 50 ms | 386 ms |
| explain cold | 149 ms | 94 ms |
| advanced weapon pick | 58 ms | 72 ms |
| long tasks over 200 ms | 0 | 3 |
| 4× max long task | 584 ms | 1241 ms |

Boot and import remain healthy; explain improved. The bulk-add path is the
clear regression and the first profiling target. Deferring the value-prewarm
work until after render was tried locally and did not move the bulk-add number
(366 ms), so the cost is in the add/recompute/render path, not prewarm alone.
The advanced pick path and long tasks likely share that work.

## Accessibility and viewport

- Lighthouse accessibility score on the initial page: **100**.
- 1920×1080 and 900×1200 screenshots show no horizontal overflow; the narrow
  layout stacks the intro/search/picker cleanly.
- Include/exclude states use icon/text (`✓` / `⊘`, `picked` / `excluded`) in
  addition to color, but a color-vision-emulation pass is still needed.
- Lighthouse covers the initial page only. The picker, import, builder,
  saved-picks, and settings dialogs still need a keyboard/focus audit.

## Manual in-game checklist

- [ ] Confirm slot footprints/arrangement and backpack dimensions against a
      real screenshot/client.
- [ ] Confirm rare-name and unique-name checks.
- [ ] Confirm `.` / `.*` newline behavior with the current grammar.
- [ ] Import a representative real item; verify mapping, tooltip, highlight.
- [ ] Copy a query into the game and verify the highlight result.
- [ ] Save/load/undo a pick and open a share link in a second browser.
- [ ] Check compact laptop width and color-vision emulation.

## Follow-ups

- [ ] Profile the bulk-add path (`State.AddIncludes` → `Recompute` → render →
      value prewarm) and restore the <50 ms budget.
- [ ] Re-check advanced picks and long tasks after that fix.
- [ ] Add the 1920×1080 perf run to CI as a smoke check, or document the
      manual command if the runtime cost is too high.
- [ ] Run the keyboard/focus audit across every dialog.
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

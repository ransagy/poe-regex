# Web performance harness

Repeatable browser timing for the PoE2 and PoE1 workbench. It serves a
published `wwwroot`, launches headless Chromium, drives the main UX actions
over CDP, and writes p50/p95 results plus long-task and memory metrics. The
current action set starts from the PoE2 default; PoE1 switching is covered by
the browser smoke checks.

## Usage

```sh
dotnet publish web/web.csproj -c Release -o /tmp/webperf \
  -p:BlazorEnableCompression=false

node tools/web-perf/run.mjs \
  --root /tmp/webperf/wwwroot \
  --runs 5 \
  --width 1920 --height 1080 \
  --out /tmp/poe-web-perf.json
```

Options:

```text
--root <dir>          published wwwroot (required)
--runs <n>            repetitions, default 5
--width/--height      Chromium window size, default 1920x1080
--throttle <n>        CDP CPU throttling rate, default 1
--out <file>          JSON output, default /tmp/poe-web-perf.json
--screenshot <file>   capture the first boot (with intro) on run 0
--screenshotAfter     capture the final state on run 0
```

The harness starts its own static server on port 8099 and Chromium debugging
on port 9222. It clears localStorage before each run so the intro and workbench
start clean.

The action set covers boot, mode/slot switching, picker filter/expand, family
and exclusion picks, the first cold advanced class-scoped resistance `≥` pick,
copy, explain cold/warm, clear, bulk add, import open/parse, builder
open/cancel, and long-task/memory metrics.

## Tier matrix

`tier-matrix.mjs` is the breadth check: it walks every class/family, picks the
first, middle, and last tier both exactly and as `≥`, clears between actions,
and reports click-to-query latency plus the slowest entries.

```sh
node tools/web-perf/tier-matrix.mjs --root out/web/wwwroot \
  --out /tmp/opencode/tier-matrix.json --max-ms 500
```

A full pass is about 2–3 minutes and runs ~1,700 actions. The exit code is
non-zero for any failed action or any action above `--max-ms` (default 500).

For the latest results and interpretation, see
[`docs/ACCEPTANCE.md`](../../docs/ACCEPTANCE.md).

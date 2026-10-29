# Web performance harness

Repeatable browser timing for the PoE2 workbench. It serves a published
`wwwroot`, launches headless Chromium, drives the main UX actions over CDP, and
writes p50/p95 results plus long-task and memory metrics.

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
and exclusion picks, copy, explain cold/warm, clear, bulk add, import open/parse,
builder open/cancel, and long-task/memory metrics.

For the latest results and interpretation, see
[`docs/WEB-PERF-poe2.md`](../../docs/WEB-PERF-poe2.md).

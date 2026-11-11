# Acceptance

_Last measured: 2026-11-11._

Acceptance criteria and current evidence for the web workbench. Engine proof is
covered separately in [`EXACT-TABLE.md`](EXACT-TABLE.md). The end-to-end gate
sequence is in [`RELEASE-PREP.md`](RELEASE-PREP.md).

## Performance

Harness: [`../tools/web-perf/run.mjs`](../tools/web-perf/run.mjs). Published
Release wwwroot, headless Chromium, cache disabled.

### 1920×1080

| Action | p50 | p95 | Budget |
|---|---:|---:|---:|
| boot to interactive | 1537 ms | 1639 ms | < 2500 ms |
| bulk add `++` | 68 ms | 73 ms | < 50 ms |
| mode: advanced | 56 ms | 170 ms | < 50 ms |
| builder open | 57 ms | 61 ms | < 50 ms |
| 10-slot paste update | 74 ms | 85 ms | < 50 ms |
| explain cold | 105 ms | 127 ms | < 500 ms |
| import parse 5 items | 51 ms | 54 ms | < 100 ms |

Advanced per-slot picks: **57–83 ms**. Long tasks: **42 over 50 ms, 0 over
200 ms, max 158 ms**.

The larger augment catalog and exact table raised boot to about 1.5 s, still
under the 2.5 s budget. Bulk add, advanced mode, and the 10-slot paste update
are slightly over the 50 ms target; they do not produce long tasks over
200 ms. The budget is a target, not a hard failure.

### Narrow and low-end

```text
900×1200: boot 1761 ms · bulk add 75 ms · advanced picks 85 ms
          explain 115 ms · import 49 ms · max long task 155 ms

4× CPU:   boot 8.2–9.6 s · bulk add 375–440 ms · advanced picks 428–508 ms
          explain 480–697 ms · import 247–314 ms · max long task 1.45–1.51 s
```

The 4× CPU sample ran on a loaded host and is informational; rerun it on a
quiet host before treating it as a regression signal.

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
corrupted enchantments, basic runes, Soul Cores, Abyssal Eyes, and bonded
effects are modeled; PoE1 `crafted` leftovers are excluded, and PoE2's actual
crafted jewel mods are not modeled. Unmodeled unique/rare names, unique-only
text, flavour, special/transformative rune text, idol and jewel text, and some
socketable/base text can still make an item highlight. This is accepted and
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

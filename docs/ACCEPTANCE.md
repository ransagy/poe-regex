# Acceptance

_Last measured: 2026-11-25; data refreshed from RePoE 3.30.0.4._

Acceptance criteria and current evidence for the web workbench. Engine proof is
covered separately in [`EXACT-TABLE.md`](EXACT-TABLE.md). The end-to-end gate
sequence is in [`RELEASE-PREP.md`](RELEASE-PREP.md).

## PoE1 MVP evidence

Measured on the PoE1 MVP branch (2026-11-13), shipping with the PoE1 3.30
update; the current catalog is sourced from RePoE 3.30.0.4 (2026-11-25):

- Catalog: 23 equipment classes, 1,611 mods (1,200 normal, 178 implicit, 233
  corrupted).
- Exact table: 1,584 scopes (367 global, 1,217 class), 0 unresolved. The
  matrix proof checks 1,917 expanded scopes with 0 failures; `poe1-status
  --check` reports a non-stale table and a proven shortest form.
- Independent audit: `Poe1ArtifactTests` re-checks every shipped fragment
  against the generated foreign corpus plus the captured safety item.
- Regression: the captured boots item has `Sockets: G` and no movement speed;
  the multi-slot paste with a Boots movement-speed block must not match it.
  Class anchors are at least two characters, and `Gloves` uses `Gl`.
- Import audit: every PoE1 catalog mod/class/display form maps back to its
  source mod (18,666 forms, 0 failures).
- Tests: 139 core + 497 web + 53 data-build pass.
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

- A canonical `StatConstraint` layer normalizes exact tiers, “or better”,
  family picks, and min/max into one lossless interval set. Four exact
  movement-speed tiers, Stallion’s or better, and the same tier set compile
  identically and independent of pick order. First-tier “or better” equals
  the family pick.
- Tier picks in a multi-value family (movement speed, maximum life) use one
  combined family+range fragment for both exact and up tiers. A single-value
  family (`+1 Skills`) keeps the shorter affix-name form. The pick row shows
  the explicit range, a full-family range uses the family label, a legacy
  name-up row shows the compact equivalent range
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
- Cross-slot tier ranges group by canonical covered intervals and min/max.
  Same-anchor ranges union only when the extra span does not exist for the
  narrower class; otherwise they stay separate and do not cross-match.
  Multiple exact tiers in one slot coalesce to one valid range before grouping.
- Token-aware factoring never cuts inside character classes/groups; a
  malformed split such as `1[(234]...` cannot be emitted.
- Value classes compress runs of consecutive digits (`[234567]` becomes
  `[2-7]`) and omit parentheses when there is no alternation, so a combined
  life range is `\+1[2-7]\d.*fe` rather than `\+(1[234567]\d).*fe$`.
- Adjacent tiers stay exact: Athlete's+Fecund (120–174) does not match
  Vigorous rolls 175–179. The proof checks every displayed roll in an
  allowed catalog span, not only the representative high-roll line.
- Non-consecutive exact tiers stay discrete: 15+25 is two picks, not a
  15–25 range, and the gap tier stays unselected. Consecutive runs still
  coalesce, and a bounded range marks every tier it covers.
- The highest tier is exact, not an “or better” anchor: its exact button
  toggles off normally. A full-family range shows ⇄ on the family row and
  converts to the family pick when clicked; first-tier “or better” stays an
  up range until the family row is used. Family and first-tier “or better”
  show the same natural bounds (empty, not a filter).
- Entering a min/max on a family pick converts it to the canonical range
  pick, so its label, tier marks, and actions stay range-aware.
- Exact and exclude are never locked by the representation. Exact toggles
  one tier’s inclusion: on a range/up/family-covered tier it splits/trims
  that pick around the tier; on an exact tier it removes it; on an excluded
  tier it clears the exclusion; on an unselected tier it adds an exact tier.
  Exclude is the only tier action that adds an exclusion. The up button is
  active only on its actual anchor and undoes that up pick. A covered up
  tier replaces that up pick with this tier as the anchor, dropping the old
  lower part unless it is separately picked; a partial suffix is replaced
  by one up pick; a suffix already covered by a family/range shows a disabled
  hint instead of being rewritten. Exact/up never create exclusions.
  Existing min/max filters are copied across splits/trims and to a replaced
  up pick. Every selected tier shows `✓` — explicit exact, range-covered,
  and up-covered alike; `+` is only for an unselected tier, and `⇄` only for
  a genuine covered claim where clicking switches to that tier. `−` belongs
  to exclude only. Range tiers keep the plain `range` note, up-covered tiers
  keep `≥ covered`, and the range `≥` button is a normal add action with a
  range title. Hover or keyboard focus tints the whole affected row plus the
  family status ("x picked", "covered", "excluded") and shows a plain inline
  action hint (`+ add`, `− remove`, `⊘ exclude`, `⇄ switch`/`replace`) on the
  row with the action. No badges, rings, or underlines.
- All tiers picked individually stay a range; only the family row converts
  them to the family pick. A family pick with tier exclusions is partial and
  the family row selects all (clears the exclusions). Every tier in a range
  shows `✓` and the `range` note; the up button is a plain add with a range
  title. After unpicking a middle tier from an up pick, both resulting runs
  show their selected tiers with `✓` (the lower range and the higher up/range
  anchor).
- Range labels use the discrete value set when tiers have a gap
  (`15 or 25% increased Movement Speed`); a dash is used only for a
  consecutive tier run. Min/max controls are hidden for a gapped tier set,
  since a min filter would fill the gap and drop the lower tier.
- Fixed-value ranges compile from the exact catalog roll set: 15–35 becomes
  `15|[23][05]`, not `(15|20|25|30|35)`. The loose `[1-3][05]` is rejected
  because it would include 10.
- A range narrowed inside one tier still compiles: 120–130 in Athlete's
  120–149 matches 120–130 and rejects 131–149. The proof checks synthesized
  in-range rolls, not only the representative high-roll line.
- Tests cover boots movement speed (fixed tiers), body/helmet maximum life
  (integer ranges), body armour life regeneration (decimal advanced range),
  cross-width ranges, the Athlete’s/Fecund slot matrix, and both exclusion
  shapes.
- Current Release suite: 139 core + 497 web + 53 data-build; the published
  PWA shell boots and reloads offline with the new picker.
- Perf smoke (published Release, two runs): boot ~1.35 s (p95 ~1.42 s);
  picker expand ~25 ms; advanced family pick ~71 ms; first cross-class range
  pick ~125 ms (subsequent picks 33–96 ms); first ring pick ~209 ms because
  it builds that slot's cross-slot union; 10-slot paste update ~69 ms; class
  change ~82 ms; longest task ~164 ms with no task above 200 ms. Range
  proofs cache matched catalog lines and safe roll hits, so adding another
  slot to a cross-slot union re-checks the proof instead of rescanning it.

## Performance

Harness: [`../tools/web-perf/run.mjs`](../tools/web-perf/run.mjs). Published
Release wwwroot, headless Chromium, cache disabled.

### 1920×1080

| Action | p50 | p95 | Budget |
|---|---:|---:|---:|
| boot to interactive | 1427 ms | 1472 ms | < 2500 ms |
| bulk add `++` | 37 ms | 43 ms | < 50 ms |
| mode: advanced | 74 ms | 78 ms | < 50 ms |
| builder open | 49 ms | 55 ms | < 50 ms |
| 10-slot paste update | 64 ms | 78 ms | < 50 ms |
| explain cold | 83 ms | 95 ms | < 500 ms |
| import parse 5 items | 38 ms | 42 ms | < 100 ms |

Advanced per-slot picks: **33–133 ms** after the first cross-class range;
the first cross-class range is **~130 ms** because it builds the class
safety corpus, and the first ring pick is **~189 ms** because it adds that
slot to a new cross-slot union. Long tasks across two runs: **22 over 50 ms,
0 over 200 ms, max ~144 ms**.

Fresh-bench first pick (both `Runner's or better` and the exact `+` on
Sprinter's) measures **~65–68 ms p50** at 1× CPU with a **~55 ms** max long
task; a second exact tier composes at **~24 ms** with no long task. At 4×
the first pick is **~275–300 ms** and the second is **~110–115 ms**. Exact
tier ranges are precomputed in the shipped table (`RangeFragments`), and
per-tier value/template plans (`TierRangePlans`) compose any selected tier
union without another range proof. The click still pays the combined-paste
plan and the picker refresh; the action render is no longer duplicated by
the parent's state-change render, and single-class anchor validation uses a
literal fast path.

The PoE1 profile, lazy data loader, and game rail raised boot to about
1.4 s, still under the 2.5 s budget. Bulk add, builder open, explain, and import are
within their targets; mode advanced and some per-slot picks remain slightly
over the 50 ms target, consistent with the existing per-slot variance. The
budget is a target, not a hard failure. Compile and combine results are
cached by semantic key, one `CompactionEngine` and `PasteComposer` are
reused per session, global display shapes come from catalog forms, the range
proof uses unsorted proof lines, a clean literal tail narrows the proof scan,
and matched proof lines and safe roll hits are cached across interval unions;
those changes removed the 1.9 s first-pick and 0.7 s class-change long tasks.

### Narrow and low-end

```text
900×1200: boot 1373 ms · bulk add 48 ms · advanced picks 39–128 ms
          first ring pick 211 ms · explain 95 ms · import 37 ms
          max long task 169 ms

4× CPU:   boot 6.0 s · bulk add 247 ms · advanced picks 141–382 ms
          first ring pick 733 ms · explain 720 ms · import 147 ms
          max long task 689 ms
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

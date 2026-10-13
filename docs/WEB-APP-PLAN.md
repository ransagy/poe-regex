# poe-regex Web App Plan (locked)

Local-only Blazor WebAssembly workbench with the PoE2 inventory look-and-feel,
editable gear slots, per-slot and global regex compilation in-browser, a
backpack-grid test area with match highlighting, and English advanced-item
import. The OpenTUI demo is frozen; the CLI contract is untouched.

## 1. Locked decisions

- **Runtime**: Blazor WebAssembly standalone (`net10.0`) running
  `src/PoeRegex.Core` in-process. Core is BCL-only, `IsAotCompatible`,
  source-generated JSON (`CatalogJsonContext`), no file/threading use, and the
  poe2 catalog ships embedded (520 KB).
- **Game scope**: PoE2 only (multi-quoted-group AND, `!` negation, lookahead).
  PoE1 stays in the catalog but is not loaded.
- **Gear slots (9)**: weapon (single concept; swaps irrelevant) / helmet / body
  armour / gloves / boots / belt / amulet / ring x2. Out of scope: charms,
  flasks, skill bar, currency, stash panel, weapon-swap distinction.
- **Test area**: the inventory backpack grid (fixed in-game size, not
  scrollable, game-sized multi-cell items supported). No separate stash view.
- **Highlight semantics**: mirrors the in-game Highlight box. In a slot context
  an item highlights when it matches that slot's query within its class scope;
  in global mode it highlights on the classless query.
- **Persistence**: localStorage first, behind an `IShareStore` seam. URL-share
  is measured later (spec size TBD); accounts/OIDC are design-only (they imply
  a future backend; the WASM app stays serverless).
- **Parser fixtures (5, English)**: normal Utility Belt (implicits + charm
  slot), magic Talisman (prefix T4 + suffix T2, ranges, `(augmented)`,
  `(unmet)`), rare Crossbow (dual damage with `(lightning)` tag, `Reload
  Time`, 1 prefix + 3 suffixes, decimal leech), magic Staff (`Sockets: S S`,
  `Grants Skill:`), corrupted rare Mace (multi-element `(fire)/(lightning)`,
  two-line mod, `Corrupted` footer).

## 2. Reference sources (confirmed with maintainer)

- **Screenshot (1)**: full-screen PoE2 image (STASH left / world center /
  INVENTORY right, `Highlight Items / Type keywords here...` search row).
  Layout facts only: slot arrangement, backpack grid below the paper-doll,
  search-row behavior. No game art, fonts, or text copied.
- **Item texts (5)**: the pastes in the decision thread. Format facts:
  `Item Class:` header, `Rarity:`, name lines, `--------` separators, base-stat
  lines, `Requires:`, `Item Level:`, `{ Implicit / Prefix "N" (Tier: N) — tags
  / Suffix ... }` headers, ranged values like `26(19-29)`, `(augmented)` /
  `(unmet)` markers, `Sockets:`, `Grants Skill:`, `Corrupted`.
- **Engine facts**: `QuerySpecification` (class?, includes as mod-id-or-text +
  `MinValue`, excludes, keywords, `AllowOverflow`) compiles deterministically
  to `CompileResult{query,length,limit(250),exceeded,diagnostics}`; `explain`
  reverses query to tokens to candidate mods; TUI logic to port
  (`playableClasses`, `availableMods`, `filterMods`, `mergeMods`,
  `generateTasks`, capacity meter).

## 3. Architecture

- `web/` Blazor WASM app with a project reference to `src/PoeRegex.Core`.
  `EngineCorpus` is built once at startup from the embedded poe2 catalog; one
  shared `PoeRegexCompiler` uses the PoE2 strategy.
- No backend, no CLI subprocess, no network calls (offline-friendly by
  construction; PWA service-worker manifest deferred to Phase 7).
- Persistence behind `IShareStore`: `LocalStorageStore` now; `UrlShare`
  later; `AccountStore` design-only.
- Styling: hand-rolled CSS + inline SVG slots; desktop-first responsive;
  keyboard accessible. Recreation only — no copied assets.

## 4. UX spec

- Right-panel layout: paper-doll (9 slots in PoE2 arrangement) + backpack
  grid below + per-slot (or global) query output with capacity meter
  (green -> orange -> red) + copy button + `explain` panel.
- Slot editing: base-class picker (only classes with explicit mods), wanted-mod
  typeahead (search name/id/line; include/exclude; min-value floors;
  select-all; clear-all), per-slot query list in multi mode, single query in
  single/global (global hides the equipment row).
- Test lab: each backpack item holds parsed item text; matching cells highlight
  per the active slot query or the global query; the tester shows per-token
  hit/miss via `explain` plus `validate` status per query.
- Item builder: assemble test items freeform from mod lines first;
  rules-enforced building (prefix/suffix caps, group exclusivity) only if the
  raw RePoE fields support it (Phase 1 investigation — the derived catalog only
  carries `classes[]` + `generationType`).

## 5. Parser spec (advanced description to app model)

- Grammar: header (`Item Class`, `Rarity`, name), separators, base stats,
  `Requires`, `Item Level`, `Sockets`, `Grants Skill`, mod blocks (header with
  tier/tags + 1-2 value lines), footers (`Corrupted`).
- Values: keep the displayed roll plus the `(min-max)` range; strip
  `(augmented)` / `(unmet)` into flags, not text.
- Mapping: each mod block maps to `(modId, statLine)` via corpus lookup
  (exact, then pattern, then fuzzy confirm list); unmatched lines become
  free-text includes; ambiguous lines open a candidate picker (mirrors the
  engine's reversibility candidate semantics).
- Golden tests from all 5 fixtures.

## 6. Phases and exit criteria

- **P0 Inputs** — DONE (locked above). Exit: this plan signed off.
- **P1 WASM spike** — Scaffold `web/`, boot Core in the browser, build the
  poe2 corpus, compile one sample spec; report load/index/compile time and
  publish size (interpreter vs AOT); check raw RePoE fields for mod-group /
  affix-cap data. Exit: go/no-go numbers recorded.
- **P2 Data service** — Classes/mods/typeahead parity with the TUI logic;
  unit tests; determinism check. Exit: tests green.
- **P3 Shell UI** — Paper-doll + backpack grid per the confirmed layout;
  visual sign-off against the screenshot. Exit: maintainer approval.
- **P4 Slot editing + compile** — Pickers, chips, minimum values,
  per-slot/global queries, meter, copy, explain/validate. Exit: live regen and
  the overflow path work in-browser.
- **P5 Import + lab** — Paste, parse, map to slot/test-item; highlight engine;
  freeform item builder (+ rules if feasible). Exit: all 5 fixtures import at
  the agreed rate; tester agrees with `explain`.
- **P6 Hardening** — Core suite green, UI tests, perf budgets (virtualize
  long mod lists), keyboard/a11y, empty/error states. Exit: full suite +
  smoke green.
- **P7 Docs/run** — `web/README.md`, architecture note, one-command local run,
  PWA/account seams documented. Exit: reproducible local run, no deploy.

## 7. Risks

- WASM index-build latency on low-end machines (build once, measure in P1,
  consider a worker/AOT).
- URL-share size for multi-slot specs (measure before promising).
- Coexistence rules may not exist in the data (freeform builder is the
  fallback).
- Screenshot fidelity vs copyright (recreation only, no copied art/text).

## 8. Open decisions

### 9.1 Multi-slot combine (no combined string yet — by analysis, not omission)

Current output is one verified query per slot. A single combined string was
requested (per-slot groups ANDed, scoping each group with the `Item Class:`
line plus negations of the other slots' classes). It is **not implemented**
because it is not expressible under the engine's verified matching model:

- The trade grammar ANDs space-separated quoted blocks, and matching is
  **per line** (every engine oracle, fingerprint, and `explain` result is
  line-scoped; see `docs/DESIGN.md` §5). An item matches the whole query only
  if it matches **every** block.
- A slot's class anchor (`Item Class: Rings`) and its mod fragments live on
  **different lines**, so they cannot be ANDed inside one quoted block
  (spaces inside quotes are literal). Any ANDed class anchor therefore kills
  every other slot's items.
- `!` negates a whole quoted block (`FormatExclusion` emits `!"..."` as its
  own block); there is no per-branch negation inside an alternation. Global
  `!"classB"` blocks would exclude slot B's items entirely.
- `|` alternation inside one block is same-line only, so per-slot
  alternatives cannot span the class line + mod lines either. Anchorless
  alternation (`"(mA|mB)"`) would cross-match shared mod text across slots.

What would make a combine possible: whole-text (rather than per-line) matching,
e.g. `(Item Class: Rings[\s\S]*...)|(Item Class: ...)`. That semantic is
**unverified** — the decisive in-game experiment is a one-block two-word query
like `"Life Mana"` on an item whose Life and Mana texts sit on different
lines: per-line predicts no match, whole-text predicts a match. Also open:
where would the combined string be pasted — trade site (one class per search)
or the inventory Highlight box? Until that lands, per-slot strings stand.

### 9.2 Backpack footprint gaps (fallback 1x3 Unknown until filled)

| Class | Footprint | Status |
|---|---|---|
| Helmet / Gloves / Boots | 2x2 | Confirmed |
| Body Armour | 2x3 | Confirmed |
| Belt | 2x1 | Confirmed |
| Amulet / Ring | 1x1 | Confirmed |
| Shield | 2x4 | Confirmed (armour shields) |
| Buckler | 2x2 | Confirmed |
| Focus | 2x3 | Inferred from "others" — verify |
| One Hand Mace | 2x3 | Confirmed |
| Wand | 1x3 | Confirmed |
| Staff / Spear | 1x4 | Confirmed |
| Warstaff | 1x4 | Inferred (stave family) — verify |
| Bow, Crossbow, Dagger, Claw, One Hand Sword, One Hand Axe, Sceptre, Flail, Two Hand Sword, Two Hand Axe, Two Hand Mace, FishingRod, TrapTool, Quiver, Talisman | 1x3 | Unknown fallback — need numbers |

## 9. Work log

- Plan locked with maintainer (9 slots, backpack test grid, highlight
  semantics, 5 fixtures, localStorage-first). Written to
  `docs/WEB-APP-PLAN.md`.
- **P1 WASM spike — GO (interpreter).** `web/` Blazor WASM standalone
  references `PoeRegex.Core` and boots the real engine in headless Chromium
  (verified via CDP DOM read with a real-time wait — note: the stored
  Chromium DevTools accepts a non-RFC WS accept, frames work anyway):
  - poe2 `EngineCorpus` build in-browser: **166 ms**; sample compile
    (Ring + `IncreasedLife6` min 80, exclude `reflect`): **17 ms**,
    deterministic (`True`), `58/250`, 0 diagnostics.
  - Query **byte-identical** to desktop `PoeRegex.Cli` for the equivalent
    spec: `"+(\d{3,}|[9-9]\d|8[0-9]) to maximum Life" !"reflect" ":"`.
    (Careful: CLI `--include X --min X:80` creates TWO requirements; the
    equivalent single-requirement CLI call is `--min "X:80"` alone.)
  - Publish size (interpreter, unoptimized): **29 MB** `wwwroot`.
    Browser-AOT optimization needs the `wasm-tools` workload, which requires
    sudo (unavailable) — deferred; interpreter is the shipping path.
  - Coexistence data: raw RePoE mod records carry `groups` (e.g.
    `["IncreasedLife"]`) and `generation_type`, so one-per-group exclusivity
    is decidable; affix-count caps are game rules not in the data (ask
    maintainer in Phase 5). Derived catalogs do NOT ship groups — a
    rules-enforced builder may need a catalog extension (Phase 5 decision).
- **P2 Data service — DONE.** `src/PoeRegex.Web.Core`: `EngineService`
  (singleton: embedded poe2 catalog, corpus built once, classes / playable
  classes / per-class + union mods / compile / explain / validate) and
  `ModSearch` (pure TUI-parity helpers: filter, available, merge, meter).
  `tests/PoeRegex.Web.Tests` (8 tests: playable-hides-empty, filter
  name/id/line case-insensitive, available, merge-union, meter thresholds,
  compile determinism + byte-parity with the CLI, explain recall, validate).
  Full `dotnet test PoeRegex.slnx`: 8 + 6 + 60 green. `web/` consumes
  `EngineService` via DI; CDP-verified in headless Chromium with the same
  byte-identical query (corpus warm 5 ms, compile 16 ms). `PoeRegex.slnx`
  gains `/src/PoeRegex.Web.Core`, `/tests/PoeRegex.Web.Tests`, `/web/web`.
- **P3 Shell UI — implemented, visual QA pending.** Template chrome stripped
  (Counter/Weather/NavMenu/bootstrap removed); hand-drawn dark CSS;
  `Components/ModeSwitch`, `PaperDoll` (9 slots on a 6x5 unit grid,
  data-driven placement), `BackpackGrid` (12x5). `WorkbenchState` mirrors TUI
  mode semantics + 7 unit tests. CDP-verified in headless Chromium: 9 slots,
  60 cells, single default weapon, multi toggle (`body, helmet`), global dims
  the doll and yields no active slots, engine strip still byte-identical.
    Screenshot self-reviewed (layout coherent). OPEN for maintainer QA:
    slot footprints/arrangement, backpack dims, overall look.
- **P4 Slot editing + compile — DONE.** `ClassPicker` (playable-class select
  per slot; defaults: Crossbow/Helmet/Body Armour/Gloves/Boots/Belt/Amulet/
  Ring/Ring — all ids verified against the poe2 catalog),
  `ModPicker` (search + min field + include/exclude + select-all + clear +
  chips), `QueryCard` (live compile, meter, copy via clipboard.js + fallback,
  diagnostics, validate status, overflow + best-effort, explain panel with
  tokens + candidates). Shared chips across slots (TUI parity) + per-include
  min values (new). CDP-verified: search `life` → 16 Crossbow rows, include →
  chip + `"feL" ":"` (byte-identical to CLI), explain shows token + 4
  candidates incl. `LifeLeechLocal2`.
- **P5 Import + lab — DONE.** `ItemTextParser` (pure; golden tests on all 5
  fixtures) + `EngineService.ResolveClassId` (plural display names) +
  `MapLineToMods` (exact → spaceless → word-stem fallbacks, approximate
  flagged; implicits map nowhere — catalog ships prefix/suffix only).
  Fixture coverage test: every prefix/suffix line of all 5 fixtures maps to
  its header-name mod (Belt 0+2 implicit-empty, Talisman 2, Crossbow 4+1,
  Staff 1, Mace 5). Found + documented two catalog-vs-game gaps: `LightRadius`
  vs `Light Radius`, `ManaLeech 6%` vs `Leeches 5.1%` (renderer keeps internal
  first-alternative forms; pipeline fix deferred, lab degrades gracefully).
  `ItemMatcher` (engine token semantics over item lines) + `TestItem` +
  `ImportPanel` (preview with candidate picker + freeform builder) +
  `TestItems` badges + grid markers + per-view highlight. CDP-verified:
  talisman import → save → Talisman query `"o I" ":"` (byte-identical to CLI)
  → badge `match` + highlighted cell. Screenshot self-reviewed.
  35/35 web tests; full slnx 6+35+60 green. OPEN for maintainer QA:
  item dimension table (grid is 1x1 markers until then), affix-cap rules,
  visual look.
- **P6 Hardening + P7 Docs — DONE.** localStorage persistence
  (`WorkbenchSnapshot` + source-gen `WebJsonContext`, `PersistenceService`
  seam, load-on-boot, save-on-change, reset button; corrupt storage never
  blocks boot) with round-trip + unknown-dropping tests; CDP-verified across a
  real page reload (chip + query restored). `web/README.md` (run/test/layout/
  limitations), main README link. Full slnx 6+37+60 green. Final screenshot
  self-reviewed (fixed `clear (r)` TUI-ism label + unstyled reset button).
- **Follow-up round 1 (branch `feature/web-workbench`, commits `91b6cec`,
  `835a7e6`).** Layout per feedback (editor left, doll right sized up,
  slot-query bar + copy under the doll, collapsed Advanced section);
  per-slot criteria (multi compiles differ per slot); catalog `groups`
  extension + regen of both catalogs (purely additive, suite green);
  game-sized backpack (placement engine, confirmed table, 1x3 Unknown
  fallback); enforced builder (caps Normal 0/0, Magic 1/1, Rare 3/3;
  group-clash errors; import blocking + assembly UI with live violations).
  Full slnx 6+61+60 green.

# poe-regex Web App Plan (locked)

Blazor WebAssembly workbench — "Renly's Recipes" — with the PoE2 inventory
look-and-feel, editable gear slots, per-slot and global regex compilation
in-browser, a backpack-grid test area with match highlighting, and English
advanced-item import. The OpenTUI demo is frozen; the CLI contract is
untouched. The deployed static build runs on Cloudflare Pages (see
[`DEPLOYING-WEB.md`](DEPLOYING-WEB.md)).

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

### 9.1 Multi-slot combine — RESOLVED and BUILT (maintainer-confirmed)

Maintainer confirmed the mechanism: the paste is the anchor group (all
selected slots) plus one exclusion group per modded slot/criterion,
space-separated (AND), whole-text multiline matching, OR inside groups, no
negation operator. `": "` matches the `Item Class:` line prefix; mod parts
are plain words. The class lists exclude each group's targets so targets
must earn the group through mods; never-targeted slots (rings/amulets) match
every group via class and always show.

Locked decisions:
- Multi mode ends in ONE combined paste string once slots are defined.
- Class branches: oracle-validated shortest unique name-prefixes
  (behavior-equivalent to the community abbreviations, extended until
  false-positive-free — e.g. `Am`, since bare `a` also matches Abyss Jewels).
- Mod fragments: engine fingerprints/threshold patterns (same alternation
  shape as the community words, provably unique — the style was for mod
  uniqueness).
- Anchor lists ALL selected slots (criteria-less slots match all their items).
- Slots sharing identical criteria share one explicit group (the Q4 pattern);
  same-class slots merge includes (union — separate groups would AND together
  and hide everything; flagged as a judgment call).
- Exclusions union into global `!"..."` blocks (block negation cannot be
  scoped; surfaced in UI).
- Overflow: hard error by default (the game limit is real), best-effort
  preview behind toggle.
- Validation: per-group `explain` scoped to target classes — source mods must
  be recalled (`paste-recall` warnings); anchor must resolve to all selected
  classes (`paste-anchor` warnings). Full-paste candidate recall does NOT
  apply by design; verification is item-level (the lab matcher ANDs
  quoted-group tokens, which is exactly paste semantics).

The analysis below is superseded but kept for the record:

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
lines: per-line predicts no match, whole-text predicts a match.

**Paste target — resolved (maintainer, round 9)**: the strings go into the
in-game Highlight box (stash/vendor search for campaign/endgame play), not
the trade site — trade browsing is out of scope (dozens of filters, unknown
regex support). In-game test evidence: the box matches advanced-description
mod names for rares (a `"Snowy"` fragment lit a rare focus), and tier
filters can be expressed as name fragments (`"Snowy"`) or alternations of
names. The grouped family/tier pickers (round 10) compile exactly that:
family picks keep the stat-text fragment, tier picks compile the quoted
name, `↑` compiles the tier's name and every better tier by required level.

### 9.2 Backpack footprints — FINAL (maintainer-confirmed, all existing classes)

| Classes | Size |
|---|---|
| Helmet / Gloves / Boots | 2x2 |
| Body Armour | 2x3 |
| Belt | 2x1 |
| Amulet / Ring | 1x1 |
| Shield (armour) | 2x4 |
| Other shields, Focus | 2x3 |
| Buckler | 2x2 |
| One Hand Mace | 2x3 |
| Two Hand Mace | 2x4 |
| Wand | 1x3 |
| Staff / Spear | 1x4 |
| Bow / Crossbow | 2x4 |
| Sceptre / Quiver | 2x3 |
| Talisman | 2x2 |
| Everything else (currency, gems, not-yet-in-game bases…) | 1x3 fallback (irrelevant) |

Import rules (locked): import requires a resolvable, playable (has-mods)
class or the save is blocked with a message; freeform raw-text builder
removed; import is a modal dialog (dimmed overlay, paste, preview with
mappings, enforcement + class gate, Accept adds to backpack and closes).

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
- **Follow-up round 10 — family/tier grouped picks + the affix-name truth.**
  The min-roll box is retired (separate commit): `WantedMod` carries no
  `MinValue`, the picker has no min field, and stored snapshots with
  `minValue` load normalized (unknown JSON fields are dropped) — roll
  limiting is the tier picks below, not a text floor. Both catalogs now
  carry the raw RePoE family key (`family` — the `type` field, which merges
  scopes: one-hand/two-hand share a family) and `requiredLevel`; the new
  `ModFamilies` helper groups a scope's mods into families, with tiers
  grouped by affix name (1H/2H variants merge) ordered by required level —
  the game's own tier numbers are never shown or guessed (the crossbow
  `Snapping` is "(Tier: 8)" in-game but 3rd by required level). `ModPicker`
  and `ItemBuilder` show one block per family: `+` adds the plain stat
  (stat-text fragment — matches every tier), expanding lists tier rows
  whose `+` compiles the quoted affix name (the only text that separates
  tiers in-game), `↑` compiles the picked tier's name plus every better
  tier's in one alternation, `−` excludes the family (fragment) or a tier
  (name literal, compiled as `!"Name"`); chips label exactly what compiles
  (`"Reliable"`, `"Snowy" +4`), plain and name picks on the same anchor
  coexist, and exact duplicates are ignored (the round-trip now keeps
  structural list equality). The lab's matchable text became the full
  copied advanced description: `ParsedItem.AllLines()` adds the
  reconstructed `{ … Modifier "Name" (Tier: n) — tags }` captions (where
  the in-game name search actually matches), builder-assembled items
  materialize captions from the catalog (`MappedLine.IsHeader`, skipped as
  stat lines by the card), and `Reheal` is now idempotent — it used to
  duplicate mapped lines on every boot (headers first, later copies for the
  same mod dropped).
  **43.1 (maintainer review round, separate commit):** family rows are
  plain-text toggles again — the shared `.suggest-row button` chrome
  (uppercase etc.) is neutralized and a `▸`/`▾` caret plus `aria-expanded`
  makes expand/collapse obvious; the family text drops the numbers instead
  of unioning ranges (the union span read oddly): `+ to maximum Life`,
  `% increased Physical Damage`, `Adds to Lightning Damage` — the
  union-math code and its tests were deleted in favor of the shape text
  (tier rows keep real ranges, chips follow the family shape); the assemble
  dialog has no family-level `+` (tiers only) and is single-column again —
  the two-column container rule now scopes to `.modpicker .suggest` (the
  dialog inherited `.panel.inventory` as its container at ≥1500px). The
  hybrid-fragment question (`% phys + accuracy` compiling a fragment that
  also matches the pure accuracy affix) was reviewed and left as is: a
  single quoted term is evaluated per line, so it cannot require two lines;
  lookahead pairs would need whole-text matching plus game verification the
  lab can't model, and quoted AND terms / range pinning were parked.
  Full slnx 7+116+66 green; CDP-verified: family shape rows
  (`+ to Accuracy Rating`, `Adds to Lightning Damage`) with caret toggles,
  tier / `↑` / name-exclusion chips and compiled bytes,
  `+` disabled after the pick lands, imported crossbow highlights on the
  `"Snapping"` name pick, built ring highlights on `"Hale"`, its card shows
  the caption not the header line, mapped lines stable across reloads,
  900px keeps the page unscrolled, and name picks survive a real reload.
  **43.2 (review follow-up, separate commit):** the assemble dialog's mod
  list showed what looked like two columns of families plus a horizontal
  scrollbar. Root cause: the list had `columns: 1` — any `columns`
  declaration (even `1`) makes the element a multicol container, and a
  height-constrained multicol (`max-height: 46vh` + `overflow-y: auto`)
  overflows into extra column boxes to the right instead of scrolling down.
  The declaration is gone (the list is a plain single-column vertical
  scroll; the main picker keeps its two columns because its list is not
  height-constrained — `.mod-list` is the scroll container), with a comment
  so it doesn't come back. Also fixed: tier tooltips in the picker and
  builder rendered literal `&quot;` (Blazor sets attributes through DOM
  APIs, so entity references are not decoded) — the titles are now built in
  C# with straight quotes. CDP-verified: dialog list `scrollWidth ==
  clientWidth`, rows all at the same x, vertical scroll bound intact
  (`scrollHeight 771 > clientHeight 457`), main picker still two columns
  with no horizontal overflow, tooltips read `include "Squire's" — …`.
  **43.3 (review follow-up, separate commit):** tooltips are facts only —
  `include "Snowy"`, `include "Snowy" and better: …`, `expand 7 tier(s)`,
  `remove (any tier)`; the wave-43 phrasing that explained engine behaviour
  (`compiles the stat-text fragment`, `the game matches …`) is gone. Hybrid
  families render their stat shapes on separate lines in both pickers and
  join with `/` in chips (`% increased Physical Damage / + to Accuracy
  Rating`). Both pickers tag rows `PREFIX`/`SUFFIX` (the main view dropped
  its `P`/`S` shorthand; the builder's lowercase tag got uppercased). And
  stray text before an item's `Item Class:` line — the sample blob's
  lowercase `sockets` blurb — is dropped by the parser instead of rendering
  as a base-stat line; texts with no class header keep the old single-item
  fallback. Full slnx 7+118+66 green. (43.4 follow-up: the `↑` tooltip
  lists only the better tiers — the picked one is already in the caption.)
  **43.5 (review follow-up, separate commit):** the editor keeps one 0.5rem
  rhythm — mode bar, `BASE CLASS` selector and picker are evenly spaced
  (the chips row renders only when it has picks; its double margins made
  the gap below the selector 19px vs 9px above) and global mode renders an
  invisible spacer with the selector's height (29px) so switching modes
  never shifts the picker (measured: search top at y=177 in both modes,
  gaps 8px/8px). The assemble dialog now disables what can't be built:
  `ItemRules.CapsFor` extracted (Normal 0/0, Magic 1/1, Rare 3/3) and a
  new `ItemRules.CanAdd` (same rules as `Validate`: caps, family lock — no
  second tier of a stat — and catalog group clashes) drives every family
  and tier `+`; Normal also disables the search and the list silently;
  chosen mods stay listed with their `+` grayed so families never shift
  while assembling. CDP-verified: family's 8 tier buttons all disable
  after one pick, cross-family group clash (belt flask life vs mana
  recovery) disables the sibling family, Magic 1+1 grays every `+` with
  Save still enabled, Rare keeps both generations open, no new note text.
  Full slnx 7+123+66 green.
  **43.6 (review follow-up, separate commit):** the global-mode spacer now
  *mirrors* the field (a hidden disabled select with the same global
  styles) instead of hardcoding 1.8rem — all three modes put the search
  box at exactly y=176.75 (the hardcode left a 0.2px wobble that landed on
  a device pixel at some zooms). Normal items are assemblable: Save
  accepts zero mods at Normal rarity (guard fixed in both the button and
  `Save()`; the list dims and stays inert). Item cards everywhere follow
  the game's order — implicit, prefix, suffix, stable within a kind
  (`ItemDisplay`); built items no longer render in add order, so a
  suffix-first assembly shows the prefix on top in the preview card and
  the saved tooltip alike. Full slnx 7+126+66 green.
  **43.7 (public-readiness):** licensing and provenance pass. The stat-text
  renderer was rewritten as an independent implementation of RePoE's
  documented translation format (behaviour pinned by rebuilding both catalogs
  byte-for-byte from the local raw exports); PyPoE's GPL-3.0 status is stated
  correctly (behavioural prior art, no code incorporated), `PROVENANCE.md`
  records the full data and asset lineage, and the docs no longer call the
  workbench local-only. Deployment (Cloudflare Pages; `main` → production) is
  documented in `DEPLOYING-WEB.md`. Full slnx 10+126+66 green.
- **Follow-up round 9 — import truth (ranges + resolved names) + carousel.**
  Per-line candidate pickers are gone: the paste already carries the
  identifying evidence, so the importer resolves it — bracketed `(lo-hi)`
  pairs are parsed (`ParsedValueLine.Ranges`) and `MapLineToMods` keeps only
  candidates whose catalog range line repeats the same pairs (fallback to the
  loose list when nothing survives, so renderer quirks never blank a line);
  a still-ambiguous line narrows by the block header's affix name, and the
  default pick moves to the front (`ModIds[0]` is *the* resolved affix for
  annotations, rules and materialization alike). Annotations are read-only
  dimmed affix names (`Reliable`, `of Light`) with `id · kind · range` in the
  tooltip, `+N` when a line truly stays ambiguous, `~approx` on fallback
  matches; a header that contradicts the resolved affix warns
  (`line resolves to 'of Light', but the header says '…'`) instead of being
  silently rewritten. The carousel is now `‹ card ›` with the `k / N`
  counter centered above the card and the status centered under the counter;
  ArrowLeft/ArrowRight step it while focus is anywhere in the dialog (the
  paste box stops propagation so caret keys keep working, modifiers are
  ignored). The min-roll box no longer lies about multi-value lines: a mod
  whose every stat line has one rolled number keeps the real threshold,
  anything else is added without the floor and says so (`min ignored for
  "Snapping" — a multi-value line takes no single threshold`); the compiled
  output is byte-identical to no-min, which is what it always silently was.
  Full slnx 7+101+66 green; CDP-verified: belt card quiet (implicits),
  talisman/crossbow/staff/mace cards resolve every header name (the crossbow
  implicit's exact-text suffix maps too), approximate leech lines flag,
  arrow keys step and the textarea keeps its caret, add-all lands all five,
  single flow unchanged, min ignored-vs-kept pair byte-checked, 900px keeps
  the page unscrolled with the card between the arrows.
- **Follow-up round 8 — import carousel + quiet implicits.** Multi-item
  pastes preview as a card carousel: one `ItemPreview` card at a time with
  `‹ k / N ›` navigation, so the dialog stays single-card sized at any item
  count. Every batch card carries the same mapping annotations as the single
  flow plus per-item candidate pickers; picks are remembered per row (they
  survive navigation) and `add all (N)` materializes each unblocked item with
  its own picks — a pick edit that breaks the rules recomputes that row's
  verdict live and drops it from the add-all count. Partial `add all` keeps
  the dialog open and parks the carousel on the first item that still needs
  attention; backpack-full is a transient add error kept separate from the
  verdict, so the retry (`add all` again) stays possible. Unmapped
  *implicit* lines are quiet everywhere (`ItemImport.UnmappedAffixCount`
  skips implicit blocks — the catalog ships affixes only and implicits never
  count in the rules), so the sample belt no longer reads `2 line(s) not in
  catalog`; only unmapped prefix/suffix lines still flag.
  Full slnx 7+96+66 green; CDP-verified: 5-card cycle with disabled ends,
  belt card clean, pick edit honored through add-all and localStorage, 15-item
  overfill parks at the first failure with per-card reasons and a live retry
  count, single-item flow unchanged, 1100px keeps the page unscrolled.
- **Follow-up round 7 — one item card everywhere.** New `ItemPreview`
  component: the single game-style card the backpack tooltip, the import
  preview and the assemble dialog all draw — rarity-coloured name,
  class · rarity line, base stats, requires / item level / sockets / granted
  skill, reconstructed mod headers (name, tier, tags), blue mod lines, a
  corrupted footer, and an optional per-line annotation slot. The tooltip is
  now a thin pinned wrapper over it (full card instead of mapped lines only;
  the card is the tooltip's scroll region, so its border stays put). The
  import dialog's single-item preview is the card with its mapping status
  inline (candidate picker for ambiguous lines, `not in catalog` otherwise).
  The assemble dialog is two columns (controls / suggestions / violations
  left; name input, live card and removal chips right) and its Add button
  saves exactly the card shown. `ItemImport.Materialize` appends mapped
  lines in parse order so the card's running index always lines up.
  Full slnx 7+93+66 green.
- **Follow-up round 6 — multi-item paste + name-aware default picks.**
  `ItemTextParser.SplitItems` (a new item starts at every `Item Class:` line;
  a pre-header stray line — the staff's `sockets` — follows the blank-line
  boundary to its own item instead of corrupting the previous one).
  `ItemImport` extracted from the import dialog as the pure shared pipeline
  (resolve → map → default picks → gate → rules → materialize), used by the
  single and batch paths; ImportPanel batch mode lists every detected item
  (rarity, class, unmapped count) with `add all` and per-row
  blocked/rule/backpack-full verdicts. Default picks now prefer the candidate
  bearing the block header's affix name, then generation-kind agreement —
  which fixes the mace's ambiguous accuracy line (`Reliable` vs `of Light`)
  and lands `Arcing`/`Snapping`/`of the Jaguar` on the header names.
  `docs/samples/real-items.txt` = the five fixtures as one paste blob.
  Full slnx 7+91+66 green.
- **Follow-up round 5 — soundness (no-false-positive rule).**
  Universal non-mod lines in every corpus (Rarity x4, Requires template,
  Item Level, Corrupted, Sockets) + class lines in the union; explain matches
  content lines only; Data.Build shares the list and both catalogs were
  regenerated. Single-compile anchors are now oracle-clean name prefixes
  (`: Ri`, `: Cr` — the vacuous `:` is gone by construction); global is the
  first and default mode. Full slnx 6+75+65 green; CLI republished.
- **Follow-up round 4 — friendly range display + editor reshape.**
  Pipeline `RangeLine` per stat line (per-number tier spans, purely additive;
  both catalogs regenerated with zero non-additive diffs); CLI `list-mods`
  gains `range` (+ text output + search); UI shows range form in suggestions,
  chips, builder, and test items with ids removed (matching still runs on
  representative text); editor slimmed to criteria only; import + enforced
  assembly both modal dialogs launched from the backpack area. Full slnx
  6+72+65 green; CLI binary republished.
- **Follow-up round 3 — final footprints + import dialog.**
  Full confirmed table (§9.2 final, incl. Sceptre 2x3); import gated on
  resolvable playable classes; freeform raw-text builder removed; import is a
  modal dialog (backdrop dismiss, preview, enforcement, accept-to-backpack).
  Full slnx 70+6+65 green.
- **Follow-up round 2 — multi combine (`PasteComposer` in Core).**
  Anchor + per-criterion exclusion groups from per-slot criteria, oracle-clean
  class prefixes (`Am`, `Hel`, `Ri`…), engine fingerprint fragments, unioned
  global exclusions, per-group explain recall validation (`paste-recall`) +
  anchor resolution checks (`paste-anchor`), 250-budget hard error with
  best-effort toggle. UI: combined paste + copy in the slot-query bar,
  per-slot cards + group breakdown in Advanced, lab badges test the paste.
  Core suite gains `MultiPasteTests` (5 tests). Full slnx 6+63+60 green.

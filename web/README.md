# Renly's Recipes — the poe-regex web workbench

Blazor WebAssembly app that runs `PoeRegex.Core` **in the browser**: a
PoE2-inventory-style workbench with editable gear slots, live-compiled regex
queries, a backpack test-items lab, and English advanced-description import.
No backend and no app-level network calls; no game assets (all panels are
hand-drawn CSS/SVG). The deployed static build is at
<https://renlys-recipes.pages.dev> (link-only; noindex) and includes
Cloudflare Web Analytics — cookieless page-view counts injected by the host,
not part of the app source ([Cloudflare's description](https://developers.cloudflare.com/web-analytics/)).

Full product plan and work log: [`docs/WEB-APP-PLAN.md`](../docs/WEB-APP-PLAN.md).
Deployment: [`docs/DEPLOYING-WEB.md`](../docs/DEPLOYING-WEB.md).
Performance: [`docs/WEB-PERF-poe2.md`](../docs/WEB-PERF-poe2.md).
Import audit: [`docs/IMPORT-AUDIT-poe2.md`](../docs/IMPORT-AUDIT-poe2.md).
Data/asset provenance: [`docs/PROVENANCE.md`](../docs/PROVENANCE.md).

## Prerequisites

- .NET 10 SDK (same as the repo root).
- A headless Chromium/Firefox only for the scripted browser checks below.

## Run

```sh
dotnet run --project web/web.csproj
# open the printed http://localhost:PORT/ URL
```

The app loads the embedded PoE2 catalog and exact fragment table and compiles
locally by table lookup. Suffix indexes for explain are built lazily on first
use (~5 ms warm, ~165 ms cold in the interpreter build).

## Build for deployment

```sh
./build.sh
# static site in out/web/wwwroot
```

`build.sh` installs the .NET 10 SDK into `out/` (gitignored) for the
Cloudflare Pages image, publishes the app, and stamps the commit into
`build-info.json`. See [`docs/DEPLOYING-WEB.md`](../docs/DEPLOYING-WEB.md).

The Pages project has Cloudflare Web Analytics enabled, so Pages injects its
cookieless beacon into the served HTML. It is not part of the app source; if
you host your own copy, disable it in the project settings or leave it out.
The build also ships `license.txt`, `notice.txt` and `provenance.md`, linked
from the page footer.

## Load the five real sample items

`docs/samples/real-items.txt` holds the maintainer-provided real item
descriptions (the same five locked fixtures the parser tests use: belt,
talisman, crossbow, socketed staff, corrupted mace). Open **import item** and
paste the whole file — the dialog detects five items and shows them as a card
carousel (`‹ 2 / 5 ›`, one card at a time, so any paste size costs one card of
space). Each card carries the same mapping annotations as the single-item
flow, with per-item candidate pickers; edits are remembered per item and
**add all** imports every unblocked item using its picks (class gate, rule
check and backpack-full are reported per card). Unmapped implicit lines stay
quiet: the catalog holds affixes only and implicits never count in the rules.

## Import a search string or guide filter

**Import string** has two modes:

- **App string** — paste a query this app produced. The preview reconstructs
  the picks and renders the real Your-picks view; Apply replaces the workbench.
  This is the byte-identical round-trip path.
- **Guide filter** — paste a build-guide filter line. Top-level `|` branches
  become picks through exact names, exact fragments, or case-insensitive regex
  matching against family/tier text. Multi-family branches use a multi-select
  (common families selected by default); unknown/base/vendor branches are
  dropped with a warning. Guide apply is simple/global only.

Current guide limits: base/vendor names, guide exclusions, and explicit class
anchors are follow-ups. App-string import covers multi-class pastes, exclusions,
and all-of.

## Test

```sh
dotnet test tests/PoeRegex.Web.Tests/PoeRegex.Web.Tests.csproj  # 165 unit tests
dotnet test PoeRegex.slnx                                       # whole repo
```

Scripted browser verification (publish + serve + headless Chromium driven
over CDP; `publish` output goes to a temp dir, never into the repo):

```sh
dotnet publish web/web.csproj -c Release -o /tmp/webpub
cd /tmp/webpub/wwwroot && python3 -m http.server 8080
# in another shell: chromium --headless=new --remote-debugging-port=9222 http://127.0.0.1:8080/
```

## Layout (`web/`)

- `Pages/Home.razor` — workbench wiring (modes, tasks, highlight, persistence,
  share links). Top to bottom: the search string bar (the product, shaped
  like the in-game Highlight box, with Copy beside it); the two check panels
  it opens (test items, what it matches); the mode switch (`All gear` =
  simple, `Per slot` = advanced); three columns: doll (advanced only) | mod
  picker | your picks; a one-line footer with the engine and data stamp,
  build, legal links, and `Reset workbench`. Advanced starts unfocused
  ("Choose a gear slot to see the mods it can roll"); the focused slot's name
  heads the picker, and weapon/offhand put their base-class select there.
- `Components/` — `PaperDoll` (advanced only: 10 gear slots; click a slot to
  edit it, slots with picks enroll, fill, and show a pick count; clearing stays
  in Your picks so every slot follows the same rule; weapon/offhand name their
  base class under the slot label),
  `SearchBar` and `PickBreakdown` (the string and what it is made of — see
  below; they share a `SearchModel` for blocks and hover), `RecipeMenu`
  (saved recipes and share links — see below),
  `BackpackGrid` (12×5 test area; integer-snapped square
  cells in a bounded-width grid; the bar toggle reports `k lit`;
  matching items highlight and non-matching ones dim; clicking
  an item pins its tooltip),
  `ItemTooltip` (pins the shared item card at the click point; dismissed via
  x, same tile, empty pack space, or removal; clamped to the viewport by
  `poeRegexPlaceTooltip`), `ItemPreview` (the one game-style card — rarity
  name, class · rarity line, base stats, requires / item level / sockets /
  granted skill, rebuilt mod headers with tier and tags, blue mod lines, a
  corrupted footer, and an optional per-line annotation slot; the card is
  the tooltip's scroll region so its border stays put), `ModeSwitch` (a
  two-option segmented control with the active mode's meaning beside it),
  `HelpTip` (the test-items `(?)` card — click to open, backdrop or Escape to
  close), `ModPicker` (searchable mods
  grouped into stat families: one row per family with the stat text with its
  numbers dropped
  (`+ to maximum Life`, `% increased Physical Damage`; multi-line families
  stack their lines) — the row is a plain-text toggle (`▸`/`▾`
  caret) with a dim tier count, a quiet `+` for the plain stat (any tier,
  compiled across every tier variant) and
  a dim `−` for the whole family; expanding shows the tiers, each with its
  real range first and the affix name after it, where `+` compiles the affix-name literal
  (specific tier), `↑` compiles that name and every better tier
  (`… or better`, by required level), `−` excludes by name; chips join a
  multi-line family with `/`; rows sit under sticky `Prefixes` / `Suffixes`
  headers (two columns when the editor is wide, stacked when narrow; no
  per-row tags) and the family tooltip names the content (`Show 6 tiers of
  % increased Movement Speed`); a picked family tints and its `+` becomes `✓`,
  and a family with a tier picked gets an underline; with a filter typed, an
  `Add all N` button beside the search box adds every matching family in one
  batch (disabled, with the prospective k/N in its tooltip, when that would
  exceed the game's 250-character limit; the preview handles up to 64 matches
  and larger sets stay visible but disabled with the budget message; one
  match keeps the row's own `+`); before typing,
  only the curated common shortlist shows under a `Common picks · N shown`
  line with `Show all N mods` (the filter placeholder is `Search mods…`;
  typing searches every family; classes with no curated picks list
  everything); a one-letter filter keeps matching families
  collapsed, two-plus letters auto-expand at most 8 families, and the bulk
  preview is capped at 64 matches so a broad first letter never compiles the
  whole catalog; the list stretches/scrolls to fill the editor's height;
  typing filters tiers, so `reliable` narrows to the family(ies) carrying
  that affix), `QueryCard`
  (per-slot string with its length, copy, and the mods it matches), `ImportPanel`
  (paste one advanced description, or several at once — a multi-item paste
  previews as `‹ card ›` with the `k / N` counter centered above the card
  and the per-item status centered under it; arrow keys step too; the paste's
  bracketed `(lo-hi)` ranges and the block header names resolve each line, so
  there is no candidate picker: annotations are read-only dimmed affix names
  with `id · kind · range` in the tooltip, `+N` when a line stays ambiguous
  and `~approx` on fallback matches, and a header name contradicting the
  resolved affix warns; `add all (N)` imports each unblocked item; class
  gate, rule check and backpack-full are reported per card; every
  preview — single or batch — is the shared card with mapping status per
  line), `ItemBuilder` (assemble from
  catalog mods: the same family/tier grouping as the picker on the left
  (expanding is the only family-level action — tiers carry the `+`), a name
  input, live card and removal chips right; the dialog's mod list is a plain
  single-column vertical scroll — deliberately no `columns` declaration,
  because a height-constrained multicol spills phantom columns sideways
  instead of scrolling down; Save adds exactly the card shown; both modals
  reject when the backpack is full). Test items open from `Test on items` in
  the search string bar (the toggle shows the item count and `k lit`) as a
  panel joined to the bar: the `(?)` help, import, assemble, remove (acts on
  the pinned item), and the backpack. `What it matches` opens the other
  panel: how the combined string is built, and each slot's own string with
  the catalog lines every block matches. One panel is open at a time;
  closing test items dismisses a pinned tooltip.
- The page uses a persisted display scale (100 / 115 / 130%, default 115%
  on desktop and 100% on phone-width screens) applied as body zoom. Below
  1100 × 640 CSS px after zoom the page scrolls and the columns stack (the
  search string drops under doll | picker); phone-width screens stack every
  column. The main columns never shrink below 26rem (the grid uses size
  containment so the long mod list never sets the page height): opening a
  check panel on a short window scrolls the page and pushes the footer down
  instead (1280x800 at 115% fits with both panels closed). In the stacked
  layouts a bar pinned to the bottom keeps the length and a copy button in
  reach. Display scale, theme, and `Copy on every change` live in the header's
  **Options** popover between `What is this?` and the GitHub link.
- The Options popover carries a persisted appearance picker. **Forge Black**
  is the dark default; **Bench Amber** is the light theme (tan workbench page, dark
  bronze header/footer, gold CTA with dark ink, game-dark item cards);
  **Match system** follows `prefers-color-scheme` (dark → Forge Black, light →
  Bench Amber) and switches live when the OS/browser mode changes. The choice
  sets `data-theme` on `<html>`, is stored as `forge`, `bench-amber`, or
  `system` under `poe-regex.theme`, is resolved before the stylesheet on boot
  (no flash of the default), and other open tabs follow it. Forge Black primary
  actions use Bronze (`#c78a3c`) while accent text and focus rings keep
  `#f0a541`. Item cards (tooltip, import preview, assemble dialog) stay the
  game-dark card in every theme, tinted to the page, so rarity and modifier
  colors keep their exact game values. The forge loading screen keeps its
  game-like palette with a theme-aware label.
- There is no separate base-class row: simple is classless, and in advanced
  only weapon/offhand have a class choice, made in the picker header while
  that slot is focused (every other slot is 1:1 with its class).
- Fragments come from the **exact PoE2 table** (`data/catalogs/poe2.fragments.json`):
  every entry was proven shortest for its scope under G1 by the data-build
  solver. The target text is the advanced item-search text (ranged stats show
  `roll(min-max)`); normal display is presentation only. The runtime performs
  table lookup only; there is no live fragment search and no heuristic
  fallback. The oracle is line-scoped over the derived
  vendor/stash corpus, and the table is strict: zero foreign modeled or
  captured-safety lines. Unique-only text, flavour, and rare names remain
  unmodeled and are reported separately. The exact table has 892 expanded
  scopes (136 global patterns, 21 classes, 525 class fragments, 0 unresolved).
  Regenerate with `matrix-solve`; a version mismatch fails the audit. Examples:
  `#% increased maximum Life` → `^\d.+um.+e$` (9), `#% increased Critical
  Damage Bonus` → `^\d.*al d.+u.$` (14), `Gain #% of Damage as Extra Fire
  Damage` → `^g.*-.*ir.+e$` (16), `#% of Damage taken Recouped as Life` →
  `f d.*n r.*e$` (12), and `#% increased Armour, Evasion and Energy Shield` →
  `-.*d a.+,` (9). The catalog/table audits check all scope entries with .NET
  Regex, captured-item modeled lines, simple/slot compiles, family flows, and
  class-pair pastes; the in-game rare-name/unique check remains manual.
- `SearchBar` and `PickBreakdown` replace the old picks rail and result bar.
  The bar shows the string with each quoted block underlined by role (class
  anchor grey, included mods blue, exclusions red), Copy with an optional
  `Copy on every change` in the header Options popover (stored in
  `poe-regex.auto-copy`; it only writes the clipboard after a user gesture, so
  loading the page never copies), and a
  budget bar where each block is a segment of its exact character cost
  (spaces included, so the segments sum to the `k / 250` count). Your picks
  lists them per slot in advanced (or `All gear` in simple), each with the
  fragment it compiles to on its own and its character count. With two or
  more picks an `Any of / All of` switch sets how they combine: any of is one
  `|` alternation (an item needs one); all of gives every pick its own block
  (an item needs each; per slot, each block is `": (other classes)|frag"`).
  Two slots of the same class still share one block, so all of falls back to
  any of there and the section says so. "None of" lists exclusions (each its
  own `!"…"` block; in advanced they apply to every slot, and `All gear`
  exclusions carry over). Plain single-line picks with a number take a
  `min` value (floors the first number, compiled by the engine's threshold
  fragment against the advanced `roll(min-max)` text, so it costs far more
  than the plain fragment; when the engine cannot carry the floor safely it
  falls back and the input turns red). Hovering a block, segment, pick, or
  section highlights the matching parts. A tier pick shows the quoted affix
  name, `or better` when it carries higher tiers, and the tier's range.
  Clicking a slot name focuses it. Over budget, the full string stays, the
  count turns red, the bar marks the 250 limit, and copy stays enabled.
- `RecipeMenu`: `Recipes` saves the current picks under a name (modes,
  classes, picks, match mode and minimums; never test items) in
  `poe-regex.recipes.v1`, loads one back, or deletes one, each with a
  one-step undo. `Copy share link` copies `/#r=<token>`, the picks as
  deflated, base64url JSON (`RecipeCodec`; a few hundred characters).
  Opening a link replaces the picks, keeps the backpack, saves any earlier
  picks as a "Before shared link" recipe first, clears the fragment, and says
  so in a dismissible notice. A same-tab `#r=` fragment applies without a
  reload. Any external workbench write freezes the other tabs; a shared link
  gets a specific handoff dialog, ordinary edits get a generic one. The frozen
  tab saves its own picks as a recipe, blocks further workbench writes, and
  offers a hard reload so it cannot overwrite the newer state. Automatic
  backups are skipped when an identical recipe already exists. Links and saved
  recipes are untrusted:
  `SnapshotValidator` rejects missing or blank strings, null list entries,
  out-of-range minimums, unknown slots, unknown versions and implausible
  counts, and decoding also rejects tokens past the length cap or more than
  64 KB of JSON. Saved recipes that fail the check are skipped on load. The
  recipe dialog traps focus and restores it to the opener; share URLs longer
  than 8,000 characters are refused with a “save a recipe instead” message.
- The assemble dialog disables what can't be built instead of only warning
  after the fact (`ItemRules.CanAdd`, same rules as `Validate`): Normal
  disables the whole mod list and the search (the list dims; Save still
  works — a modless Normal item is assemblable), Magic caps each
  generation at 1, Rare at 3, and a chosen family or catalog group (e.g.
  belt flask life vs mana recovery) locks its siblings; chosen mods stay
  listed with their `+` grayed so the list never shifts while assembling.
- Item cards (tooltip, import preview, assemble preview) show mods in the
  game's order: implicits, then prefixes, then suffixes — stable within a
  kind (`ItemDisplay`).
- Mod picking compiles what it says: a plain family pick keeps the stat-text
  fragment (matches every tier); a tier pick compiles the quoted affix name
  (`"Snowy"`), because the name is the only text that distinguishes tiers
  in-game; `↑` compiles the whole name set from that tier up in one quoted
  alternation. Tier order is RePoE `required_level` — the UI never shows
  game tier numbers (the game's own numbering is not derivable from data).
  One limitation is inherent to text matching: when two affixes share a
  stat line's text (e.g. the hybrid phys%+accuracy prefix and the pure
  accuracy prefix share the accuracy line), any fragment of that line
  recalls both — a single quoted term cannot require two different item
  lines, so hybrid picks match either affix. Leaving it as is was a
  maintainer call (the alternatives — quoted AND terms or range pinning —
  were reviewed and parked).
- Item matching for highlight/dim and tooltip verdicts runs on the item's
  full copied advanced description: the `Item Class:` line, parsed/mapped
  lines, and the `{ … Modifier "Name" (Tier: n) — tags }` captions — the
  captions are where the game matches affix-name queries, so the lab agrees
  with an in-game `snowy` search; builder-assembled items get reconstructed
  captions too (without tier/tags, which the catalog does not carry). It
  engages only once the active scope has mods picked.
- `Services/PersistenceService.cs` — localStorage for the workbench and saved
  recipes, behind an `IShareStore`-style seam. Workbench loads and saved
  recipes are checked by `SnapshotValidator`; share links are `RecipeCodec`
  fragments; short links and accounts are future work (see the plan doc).
- `wwwroot/js/clipboard.js` — `navigator.clipboard` with textarea fallback.
- `wwwroot/js/focus.js` — keeps Tab inside the Recipes dialog.
- `wwwroot/js/tab-sync.js` — cross-tab workbench freeze and share-link handoff.
- `wwwroot/js/tooltip.js` — flips/clamps the pinned item tooltip inside the
  viewport after each render.

Engine-facing logic lives in `src/PoeRegex.Web.Core` (`EngineService`,
`ModSearch`, `ModFamilies` (family/tier grouping + union display),
`WorkbenchState`, `ItemTextParser`, `ItemMatcher`, `TestItem`,
`Persistence`) so it is unit-testable without a browser.

## Known limitations (all flagged for maintainer QA)

- Slot footprints/arrangement and backpack dims are the author's reading of
  the provided screenshot — confirm visually.
- Backpack footprints: confirmed table in `ItemFootprints` (belts 2×1,
  buckler 2×2, armour shields 2×4, 1H mace 2×3, wands 1×3, staves/spears 1×4,
  standard armour/accessories); everything else uses a flagged 1×3 Unknown
  fallback — gap list in the plan doc.
- Multi mode ends in ONE combined stash paste (anchor + per-slot exclusion
  groups, whole-text OR-in-group / AND-across-groups); per-slot queries stay
  in Advanced. Same-class slots merge (union); exclusions apply globally.
  Membership is derived: a slot joins the group as soon as it has any
  wanted/unwanted chips; clicking a slot only moves the editing focus.
- The derived catalog ships prefix/suffix generations only (no implicits)
  and renders some internal shorthand forms (`LightRadius`, `ManaLeech`);
  the importer maps those via flagged approximate fallbacks. Imports resolve
  by bracket ranges + header names, so a line with neither (and no plain-text
  match) shows as ambiguous (`+N`) rather than asking — the grouped
  family/tier pickers cover that surface for the builder and picker.
- Text before an item's `Item Class:` line is paste noise (the game's copy
  can open with a stray blurb such as a lowercase `sockets`): the parser
  drops it instead of showing it as a base-stat line. Text with no
  `Item Class:` at all keeps the old single-item behavior (all lines are
  base lines).
- Minimums are back as a per-pick `min` input in Your picks. A numeric floor
  still compiles as near-whole-line text (often 10x the plain fragment), so
  the input sits next to the pick's cost to make the trade visible; tier
  picks remain the cheaper way to limit rolls. Snapshots older than v4 drop
  their stored floors on load: they came from the retired min-roll box and
  were never visible.
- Publish is the unoptimized interpreter build (25 MB `wwwroot`); browser AOT
  needs the `wasm-tools` workload (requires sudo to install).

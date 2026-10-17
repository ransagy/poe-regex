# poe-regex workbench (web)

Local-only Blazor WebAssembly app that runs `PoeRegex.Core` **in the
browser**: a PoE2-inventory-style workbench with editable gear slots,
live-compiled regex queries, a backpack test-items lab, and English
advanced-description import. No backend, no network calls, no game assets
(all panels are hand-drawn CSS/SVG).

Full product plan and work log: [`docs/WEB-APP-PLAN.md`](../docs/WEB-APP-PLAN.md).

## Prerequisites

- .NET 10 SDK (same as the repo root).
- A headless Chromium/Firefox only for the scripted browser checks below.

## Run

```sh
dotnet run --project web/web.csproj
# open the printed http://localhost:PORT/ URL
```

The app loads the embedded poe2 catalog, builds the suffix-index corpus once
(~5 ms warm, ~165 ms cold in the interpreter build), and compiles locally.

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

## Test

```sh
dotnet test tests/PoeRegex.Web.Tests/PoeRegex.Web.Tests.csproj  # 102 unit tests
dotnet test PoeRegex.slnx                                       # whole repo
```

Scripted browser verification (publish + serve + headless Chromium driven
over CDP; `publish` output goes to a temp dir, never into the repo):

```sh
dotnet publish web/web.csproj -c Release -o /tmp/opencode/webpub
cd /tmp/opencode/webpub/wwwroot && python3 -m http.server 8080
# in another shell: chromium --headless=new --remote-debugging-port=9222 http://127.0.0.1:8080/
```

## Layout (`web/`)

- `Pages/Home.razor` — workbench wiring (modes, tasks, highlight, persistence).
  The mode strip holds the mode buttons on the left and the compiled-query
  box filling the rest of the row (one-line ellipsized regex with the full
  text in its title, length meter, copy); per-slot queries for Advanced
  live below.
- `Components/` — `PaperDoll` (10 gear slots; multi = click to focus, chips
  enroll, X clears), `BackpackGrid` (12×5 test area; integer-snapped square
  cells span the full width in the stacked layout and a bounded column under
  the paper doll from 1500px up — when the
  window is too short the pack area scrolls internally, never the page;
  matching items highlight and non-matching ones dim; clicking an item pins
  its tooltip),
  `ItemTooltip` (pins the shared item card at the click point; dismissed via
  x, same tile, empty pack space, or removal; clamped to the viewport by
  `poeRegexPlaceTooltip`), `ItemPreview` (the one game-style card — rarity
  name, class · rarity line, base stats, requires / item level / sockets /
  granted skill, rebuilt mod headers with tier and tags, blue mod lines, a
  corrupted footer, and an optional per-line annotation slot; the card is
  the tooltip's scroll region so its border stays put), `ModeSwitch`,
  `ClassPicker`,
  `ModPicker` (searchable mods, one flat list with a P/S prefix/suffix tag
  per line — one column, or two when the editor column is wide enough — and
  the list stretches/scrolls to fill the editor's height), `QueryCard` (meter,
  copy, explain, overflow), `ImportPanel`
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
  catalog mods in two columns — searchable suggestions left, name input,
  live card and removal chips right; Save adds exactly the card shown; both
  modals reject when the backpack is full). Backpack tools row: import,
  assemble, and remove (acts on the pinned item).
- Item matching for highlight/dim and tooltip verdicts runs on the item's
  `Item Class:` line plus parsed and mapped catalog lines (builder-assembled
  items match too); it engages only once the active scope has mods picked.
- `Services/PersistenceService.cs` — localStorage behind an `IShareStore`-style
  seam (URL-share and accounts are future work; see the plan doc).
- `wwwroot/js/clipboard.js` — `navigator.clipboard` with textarea fallback.
- `wwwroot/js/tooltip.js` — flips/clamps the pinned item tooltip inside the
  viewport after each render.

Engine-facing logic lives in `src/PoeRegex.Web.Core` (`EngineService`,
`ModSearch`, `WorkbenchState`, `ItemTextParser`, `ItemMatcher`, `TestItem`,
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
  match) shows as ambiguous (`+N`) rather than asking — grouped family/tier
  pickers (next round) replace that surface for the builder and picker.
- The min-roll box is retired: a numeric floor only compiles soundly as
  whole-line text (~4-5x the length of the normal fingerprint fragment) and
  the click-time interaction never made it live or editable. Roll limiting
  comes from tier picks instead (a tier's affix name implies its range).
- Publish is the unoptimized interpreter build (29 MB `wwwroot`); browser AOT
  needs the `wasm-tools` workload (requires sudo to install).

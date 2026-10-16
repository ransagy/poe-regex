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

## Test

```sh
dotnet test tests/PoeRegex.Web.Tests/PoeRegex.Web.Tests.csproj  # 83 unit tests
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
  enroll, X clears), `BackpackGrid` (12×5 test area; cells fit the box they
  are given — integer-snapped squares, centered — so the pack grows with the
  window without scrolling the bench; matching items highlight and
  non-matching ones dim; clicking an item pins its tooltip),
  `ItemTooltip` (game-style overlay at the click point — rarity-coloured
  name, class/rarity line, mapped lines; dismissed via x, same tile, empty
  pack space, or removal; clamped to the viewport by `poeRegexPlaceTooltip`),
  `ModeSwitch`, `ClassPicker`,
  `ModPicker` (searchable mods split into prefix/suffix lists — side by side
  once the editor column is wide enough), `QueryCard` (meter, copy, explain,
  overflow), `ImportPanel`
  (paste + freeform), `ItemBuilder` (assemble from catalog mods; both modals
  reject when the backpack is full). Backpack tools row: import, assemble,
  and remove (acts on the pinned item).
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
  the importer maps those via flagged approximate fallbacks.
- Publish is the unoptimized interpreter build (29 MB `wwwroot`); browser AOT
  needs the `wasm-tools` workload (requires sudo to install).

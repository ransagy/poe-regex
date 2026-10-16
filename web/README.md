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
dotnet test tests/PoeRegex.Web.Tests/PoeRegex.Web.Tests.csproj  # 37 unit tests
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
- `Components/` — `PaperDoll` (9 gear slots; multi = click to focus, chips
  enroll, X clears), `BackpackGrid` (12×5 test area; click an item to select),
  `ItemDetail` (selected item's verdicts + mapped lines under the doll),
  `ModeSwitch`, `ClassPicker`, `ModPicker`, `QueryCard` (meter, copy, explain,
  overflow), `ImportPanel` (paste + freeform), `TestItems` (compact rows with
  match dot).
- `Services/PersistenceService.cs` — localStorage behind an `IShareStore`-style
  seam (URL-share and accounts are future work; see the plan doc).
- `wwwroot/js/clipboard.js` — `navigator.clipboard` with textarea fallback.

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

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
Data/asset provenance: [`docs/PROVENANCE.md`](../docs/PROVENANCE.md).

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
from the Advanced panel.

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
dotnet test tests/PoeRegex.Web.Tests/PoeRegex.Web.Tests.csproj  # 126 unit tests
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
  `ModPicker` (searchable mods grouped into stat families: one row per
  family with a PREFIX/SUFFIX tag and the stat text with its numbers dropped
  (`+ to maximum Life`, `% increased Physical Damage`; multi-line families
  stack their lines) — the row is a plain-text toggle (`▸`/`▾`
  caret) with a tier-count badge, a `+` for the plain stat (any tier) and a
  `−` for the whole family; expanding shows the tiers, each with the affix
  name and its real range, where `+` compiles the affix-name literal
  (specific tier), `↑` compiles that name and every better tier
  (`… or better`, by required level), `−` excludes by name; chips join a
  multi-line family with `/`; one column, or
  two when the editor column is wide enough — and the list
  stretches/scrolls to fill the editor's height; typing filters tiers,
  so `reliable` narrows to the family(ies) carrying that affix), `QueryCard`
  (meter, copy, explain, overflow), `ImportPanel`
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
  reject when the backpack is full). Backpack
  tools row: import, assemble, and remove (acts on the pinned item).
- The editor keeps one rhythm: the mode bar, the `BASE CLASS` selector and
  the mod picker are spaced with the same 0.5rem gap (the chips row renders
  only when it has picks); in global mode a hidden mirror of the field
  (same select styles, `visibility: hidden`) keeps the geometry
  pixel-identical, so switching modes never shifts the picker.
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
- `Services/PersistenceService.cs` — localStorage behind an `IShareStore`-style
  seam (URL-share and accounts are future work; see the plan doc).
- `wwwroot/js/clipboard.js` — `navigator.clipboard` with textarea fallback.
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
- The min-roll box is retired: a numeric floor only compiles soundly as
  whole-line text (~4-5x the length of the normal fingerprint fragment) and
  the click-time interaction never made it live or editable. Roll limiting
  comes from tier picks instead (a tier's affix name implies its range).
- Publish is the unoptimized interpreter build (29 MB `wwwroot`); browser AOT
  needs the `wasm-tools` workload (requires sudo to install).

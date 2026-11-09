# Data & asset provenance

Where every piece of data and every asset in this repository comes from, under
which terms it is used, and what is deliberately **not** included. `NOTICE`
holds the attribution text; this page records the lineage and the legal
reasoning.

## At a glance

| What ships | Where | Source | Terms |
| --- | --- | --- | --- |
| Engine, CLI, web workbench | `src/`, `tools/`, `web/` | original work for this project | MIT (`LICENSE`) |
| Derived catalogs + vendor corpora | `data/catalogs/*.json` | RePoE exports of GGG game data, including `augments.json` | tooling MIT; data GGG-owned (below) |
| Icon artwork | `web/wwwroot/favicon.svg`, inline shell/boot SVGs | original artwork for this project | MIT |
| Sample item texts | `docs/samples/real-items.txt`, `FixtureTexts.cs` | in-game copy output | GGG-owned text, minimal test data |
| Third-party libraries | see `NOTICE` | upstream packages | all MIT or Apache-2.0 |

Not in the repository: game binaries, game art, audio, fonts, raw RePoE
exports, wiki/forum/guide text, or any content under a non-permissive license.

## Data lineage

1. **Game files** — Path of Exile 2 data is © Grinding Gear Games Limited.
2. **Extraction tooling** — RePoE extracts the game files with PyPoE and the
   poe-tool-dev data schema. Neither is redistributed here.
3. **RePoE exports** — `tools/PoeRegex.Data.Build fetch` downloads a
   manifest-backed set of RePoE's hosted PoE2 JSON from
   <https://repoe-fork.github.io/poe2>, from
   <https://github.com/repoe-fork/repoe>, a fork of
   <https://github.com/brather1ng/RePoE>. This includes `augments.json`,
   generated from `SoulCores.dat64`, `SoulCoreStats.dat64`, and stat
   descriptions. Both repositories are MIT-licensed; their `LICENSE.md` adds
   that the contents of generated `data` files are owned by GGG and must be
   used according to GGG's terms.
4. **Our build tool** — renders modifier text, maps mods to item classes,
   computes fingerprints, and writes the catalogs. Raw exports stay in
   `data/raw/` (gitignored) and are never committed.
5. **Shipped catalog** — `data/catalogs/poe2.catalog.json`, embedded into the
   CLI and web assemblies at build time.
   They contain item class names, mod ids and affix names, generation type,
   source (`desecrated`, `implicit`, `corrupted`, `augment`, or empty for normal
   explicit affixes), augment type/target, family, required level,
   class/group membership, rendered stat text, and precomputed catalog
   fingerprints. The first augment pass covers the basic tiered runes; special
   and bonded socketables remain deferred. PoE1 `crafted` prefix/suffix leftovers
   are excluded; PoE2's actual crafted mods are jewel-only in domain `misc`.
   Corrupted enchantments are modeled as a separate source; five raw records
   without a positive base/spawn reference are excluded.
   `poe2.fragments.json` is the exact shortest-form table derived from the
   same catalog plus the safety corpus.
6. **Vendor/stash safety corpus** — `poe2.vendor-corpus.json` is derived from
   the same raw exports. It adds the searchable line text that the selectable
   catalog omits (regular, corrupted, essence, and implicit item mods; base
   item names, properties, requirements, implicits, and granted skills;
   regular affix captions/tags; basic-rune augment effect lines; and
   universal usage). Modeled desecrated lines, implicit-only base lines, and
   corrupted enchantment lines are also included. PoE1 `crafted`
   leftovers are excluded; PoE2 crafted jewel lines are not modeled yet.
   Unique-only mods, unique names, captions, flavour, and rare item names are
   deliberately excluded and are reported as unmodeled in the audit. No game
   binaries, memory, client logs, or runtime extraction are involved.
7. **Reference-only PoB export** — RePoE also hosts a Path of Building data
   export at <https://repoe-fork.github.io/pob-data/poe2>. Its source project,
   Path of Building Community, is MIT-licensed and its generated item data is
   GGG-owned. This repository uses `ModRunes.min.json` only as a development
   cross-check for basic rune effects; no PoB file is committed, embedded, or
   shipped. The manifest records the rest of that export for future work.
8. **Consumers** — the CLI, the library, and the web workbench read only the
   derived catalog, exact fragment table, vendor corpus, and safety fixtures;
   nothing downloads game data at runtime.

### `dataVersion`

RePoE's hosted exports carry no version field. `fetch` writes the export files
and a `data/repoe.snapshot.json` with the download date, sizes and SHA-256
hashes; `build` records the download date of the snapshot as the catalog's
`dataVersion` (currently `2026-11-08`). The manifest-backed audit checks the
published file list before a refresh. This identifies the raw export, not
the derived fragment table or solver revision; the candidate status records
the table-generation time and matrix hash separately. To refresh after a
league patch, re-run `fetch` and `build` and commit the regenerated catalogs:

```sh
dotnet run --project tools/PoeRegex.Data.Build -- fetch
dotnet run --project tools/PoeRegex.Data.Build -- build
dotnet run --project tools/PoeRegex.Data.Build -- corpus
dotnet run --project tools/PoeRegex.Data.Build -- matrix-solve --from-scratch
```

## Stat-text rendering

`tools/PoeRegex.Data.Build/Rendering/StatTextRenderer.cs` is an independent
implementation of the translation format documented by RePoE
(`RePoE/docs/stat_translations.md`): per-id variants and conditions, `formats`,
`index_handlers` (numeric fields supplied by RePoE's
`stat_value_handlers.json`), and `{i}` substitution. Its output is pinned by
unit tests and by rebuilding the catalog byte-for-byte from the raw exports.

PyPoE is the best-known prior renderer of the same data and is **GPL-3.0**
(maintained fork: <https://github.com/Project-Path-of-Exile-Wiki/PyPoE>). It
informed the expected behaviour only; no PyPoE code is incorporated in the
current sources.

### Sourcing correction (October 2026)

Early revisions of this repository described PyPoE as MIT-licensed and the
renderer as a port of its `TranslationString.format_string`. Both statements
were wrong: PyPoE is GPL-3.0, and the renderer has since been reimplemented
from the documented RePoE format. The incorrect claims remain visible in older
commits; the current tree contains no PyPoE code. This does not affect the MIT
license of the current code.

## Item text fixtures

`docs/samples/real-items.txt` and the fixtures in
`tests/PoeRegex.Web.Tests/FixtureTexts.cs` are English advanced item
descriptions copied from the game by the maintainer: a normal belt, magic
talisman, rare crossbow, magic staff, and corrupted rare mace. They are
GGG-owned text used as minimal parser/test data, not wiki or guide content.

## Artwork and fonts

- `web/wwwroot/favicon.svg` and the inline header/boot SVGs are original
  artwork drawn for this project; there are no game icons or sprites.
- The two Blazor-template placeholder PNGs (`favicon.png`, `icon-192.png`)
  were removed; the app references only the project's own SVG.
- No webfonts are shipped; the workbench uses system serif/sans stacks.

## Third-party libraries

The complete list with licenses is in `NOTICE` (all MIT or Apache-2.0):
TextCopy, .NET/Blazor, System.Text.Json, xUnit and its Visual Studio runner,
Awesome Assertions, Microsoft.NET.Test.Sdk, and coverlet.

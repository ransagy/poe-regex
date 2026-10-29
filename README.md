# Poe Regex Compiler

[![ci](https://github.com/ransagy/poe-regex/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/ransagy/poe-regex/actions/workflows/ci.yml)
[![license: MIT](https://img.shields.io/badge/license-MIT-c9a227.svg)](LICENSE)

A PoE2 engine that turns modifier requirements into short, in-game-safe regex
queries. PoE2 search boxes cap regex queries at 250 characters; the project
precomputes the exact shortest line-safe fragment for every mod line over the
vendor/stash corpus and assembles deterministic, AND-ed quoted blocks.

The repository contains the engine, its data pipeline, the CLI, and the web
reference implementation — **"Renly's Recipes"**, a Blazor WebAssembly
workbench that compiles queries in-browser for the in-game Highlight search.
The deployed build is at <https://renlys-recipes.pages.dev> (link-only;
excluded from search engines).

## Design

- **Data pipeline** (`tools/PoeRegex.Data.Build`): fetches RePoE's hosted
  exports (the fetch date is recorded as the catalog's `dataVersion`), renders
  each mod's stat text, builds the vendor/stash corpus, solves every scope
  exactly, and emits the derived catalog plus the exact fragment table
  (`data/catalogs/*.json`). Raw RePoE JSON is dev-only and gitignored; only
  derived data ships. See [`docs/PROVENANCE.md`](docs/PROVENANCE.md).
- **Engine** (`src/PoeRegex.Core`): consumes the PoE2 catalog and its exact
  fragment table through `ICatalogSource`, performs table lookup only (no
  runtime fragment search), composes value-threshold digit-range patterns,
  factors common prefixes across included mods, and enforces the 250-character
  limit. Output is deterministic.
- **CLI** (`src/PoeRegex.Cli`): interactive prompt or batch commands.
- **Web workbench** (`web/`, "Renly's Recipes"): Blazor WebAssembly running
  `PoeRegex.Core` in the browser — gear slots, live queries for the in-game
  Highlight box, an item-import lab, and a test-item backpack. Static site,
  no backend; the app makes no network calls of its own (the Cloudflare-hosted
  build includes cookieless Cloudflare Web Analytics — see
  [`web/README.md`](web/README.md)).
- **Tests** (`tests/`): engine unit tests, exact-table audits, and web
  no-false-positive property tests over the shipped PoE2 catalog.

See `docs/DESIGN.md` for the full specification. Integrating `PoeRegex.Core`
into your own app (references, pipeline, custom catalog sources, AOT)?
See [`docs/USING-THE-ENGINE.md`](docs/USING-THE-ENGINE.md). Contributions are
welcome — see [`CONTRIBUTING.md`](CONTRIBUTING.md) for adding or correcting test
cases.

## Build & test

```sh
dotnet build PoeRegex.slnx
dotnet test PoeRegex.slnx
```

See [`docs/BUILDING.md`](docs/BUILDING.md) for publishing self-contained or
Native AOT single-file binaries (including native toolchain prerequisites and
the `--clipboard` dependency on `xclip`/`xsel` on Linux), plus how the
GitHub Actions release pipeline works.

## Usage

```sh
# interactive
dotnet run --project src/PoeRegex.Cli

# batch: life on rings with a floor, exclude reflect, require item level 60+
dotnet run --project src/PoeRegex.Cli -- generate \
  --class Ring \
  --include "to maximum Life" --min "increased rarity:30" \
  --exclude reflect --ilvl 60 --clipboard

# list classes / mods
dotnet run --project src/PoeRegex.Cli -- list-classes
dotnet run --project src/PoeRegex.Cli -- list-mods --class Ring --search life

# validate a query
dotnet run --project src/PoeRegex.Cli -- validate --query '"to maximum Life"'

# explain a query: decompose it back into the catalog mods it matches
dotnet run --project src/PoeRegex.Cli -- explain --class Ring --query '"life" "I"'
```

Every command also supports `--json` for machine-readable stdout — that's the
cross-language integration surface (Python, Node, shell, etc.). See
[`docs/USING-THE-ENGINE.md`](docs/USING-THE-ENGINE.md#12-using-the-engine-from-any-language-the-cli-subprocess-contract).

The web reference implementation, "Renly's Recipes", runs the engine
in-browser (Blazor WASM): see [`web/`](web/README.md). The deployed build is
at <https://renlys-recipes.pages.dev>; deployment notes are in
[`docs/DEPLOYING-WEB.md`](docs/DEPLOYING-WEB.md).

Exit codes: `0` ok, `1` error (including a query over the character limit),
`2` usage error.

## Data pipeline (reproducing the catalog)

```sh
dotnet run --project tools/PoeRegex.Data.Build -- fetch
dotnet run --project tools/PoeRegex.Data.Build -- build
dotnet run --project tools/PoeRegex.Data.Build -- corpus
dotnet run --project tools/PoeRegex.Data.Build -- matrix-solve --from-scratch
```

`matrix-solve` runs the exact solver, writes the matrix/table/proof/status,
and verifies that no shorter legal form exists. Re-running after a league
patch re-sources the latest exports. The catalog records the fetch date and
contains only the derived subset described in `docs/PROVENANCE.md`.

## Acknowledgements

Sources and prior art from the Path of Exile community this project builds on:

- **RePoE** — extracted game data for the data pipeline.
  - `repoe-fork/repoe` (https://github.com/repoe-fork/repoe) — fork whose gh-pages
    exports this project fetches.
  - Original project `brather1ng/RePoE` (https://github.com/brather1ng/RePoE).
  - The underlying game data is owned by Grinding Gear Games; see `NOTICE`.
- **PyPoE** (https://github.com/OmegaK2/PyPoE; maintained fork
  https://github.com/Project-Path-of-Exile-Wiki/PyPoE) — GPL-3.0. Behavioural
  prior art for stat-text rendering: our renderer (`StatTextRenderer`) is an
  independent implementation of RePoE's documented translation format, and no
  PyPoE code is incorporated. See [`docs/PROVENANCE.md`](docs/PROVENANCE.md).
- **Community grammar knowledge** — the trade-search regex conventions this tool
  targets (quoted-block AND `"A" "B"`, `!` negation, lookaheads and custom
  keywords on PoE 2, the 250-character query limit) are treated as functional
  behavior of the in-game trade sites and described here in our own words.
  No example text, query strings, or prose are reproduced from community wikis
  (which use CC BY-NC-SA), forums, subreddits, or guide sites. No unlicensed
  source was used.

Nods and kudos to the projects that made this possible:

- [PyPoE](https://github.com/OmegaK2/PyPoE) — GPL-3.0 — behavioural prior art for rendering (no code incorporated)
- [RePoE](https://github.com/repoe-fork/repoe) — MIT — game-data extraction
- [RePoE (original)](https://github.com/brather1ng/RePoE) — MIT — upstream project
- [xUnit](https://github.com/xunit/xunit) — Apache 2.0 — test framework
- [Awesome Assertions](https://github.com/AwesomeAssertions/AwesomeAssertions) — Apache 2.0 — test assertions
- [TextCopy](https://github.com/CopyText/TextCopy) — MIT — cross-platform clipboard
- [.NET](https://dot.net) / [System.Text.Json](https://github.com/dotnet/runtime) — MIT — runtime and JSON

## License

MIT. The repository also contains the web reference implementation
("Renly's Recipes"). This project is not affiliated with Grinding Gear Games;
Path of Exile is a registered trademark of GGG. Derived catalog data is
community extraction from RePoE (GGG-owned data) — see `NOTICE` and
[`docs/PROVENANCE.md`](docs/PROVENANCE.md).

## AI disclosure

AI assistance was used to generate and validate portions of this project. All
generated code and content adhere to the project's licensing and sourcing policy
(no reproduction of non-permissively licensed community content), and have been
reviewed and verified by the project maintainer.

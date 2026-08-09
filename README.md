# Poe Regex Compiler

A compiler engine that turns Path of Exile 1 & 2 trade-filter modifier requirements
into short, in-game-safe regex queries. PoE 2 trade search caps regex queries at
250 characters; this tool targets that budget by precomputing the shortest unique
fragment for every mod line and assembling deterministic, AND-ed quoted blocks.

## Design

- **Data pipeline** (`tools/PoeRegex.Data.Build`): fetches pinned RePoE exports,
  renders each mod's stat text, fingerprints every line via a suffix-array index,
  and emits a compact derived catalog (`data/catalogs/*.json`). Raw RePoE JSON is
  dev-only and gitignored; only derived catalogs ship.
- **Engine** (`src/PoeRegex.Core`): consumes a catalog through `ICatalogSource`,
  validates fragments against per-game grammar, selects the shortest safe
  fingerprint (or a value-threshold digit-range pattern), factors common prefixes
  across included mods, and enforces the game's character limit. Output is
  deterministic.
- **CLI** (`src/PoeRegex.Cli`): interactive prompt or batch commands.
- **Tests** (`tests/`): engine unit tests plus no-false-positive property tests
  over both shipped catalogs.

See `docs/DESIGN.md` for the full specification. Integrating `PoeRegex.Core`
into your own app (references, pipeline, custom strategies/catalogs, AOT)?
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
  --game poe1 --class Ring \
  --include "to maximum Life" --min "increased rarity:30" \
  --exclude reflect --ilvl 60 --clipboard

# list classes / mods
dotnet run --project src/PoeRegex.Cli -- list-classes --game poe2
dotnet run --project src/PoeRegex.Cli -- list-mods --game poe2 --class Ring --search life

# validate a query
dotnet run --project src/PoeRegex.Cli -- validate --game poe1 --query '"to maximum Life"'

# explain a query: decompose it back into the catalog mods it matches
dotnet run --project src/PoeRegex.Cli -- explain --game poe1 --class Ring --query '"life" "I"'
```

Every command also supports `--json` for machine-readable stdout — that's the
cross-language integration surface (Python, Node, shell, etc.). See
[`docs/USING-THE-ENGINE.md`](docs/USING-THE-ENGINE.md#12-using-the-engine-from-any-language-the-cli-subprocess-contract).

Exit codes: `0` ok, `1` error (including a query over the character limit),
`2` usage error.

## Data pipeline (reproducing the catalogs)

```sh
dotnet run --project tools/PoeRegex.Data.Build -- fetch --game poe1 --out data/raw/poe1
dotnet run --project tools/PoeRegex.Data.Build -- build --game poe1 --raw data/raw/poe1 --catalog data/catalogs/poe1.catalog.json
```

Re-running after a league patch re-sources the latest exports.

## Acknowledgements

Sources and prior art from the Path of Exile community this project builds on:

- **RePoE** — extracted game data for the data pipeline.
  - `repoe-fork/repoe` (https://github.com/repoe-fork/repoe) — fork whose gh-pages
    exports this project fetches.
  - Original project `brather1ng/RePoE` (https://github.com/brather1ng/RePoE).
  - The underlying game data is owned by Grinding Gear Games; see `NOTICE`.
- **PyPoE** (https://github.com/OmegaK2/PyPoE) — MIT-licensed. The stat-text
  renderer (`StatTextRenderer`) is an algorithm port of PyPoE's
  `TranslationString.format_string` (variant selection, `index_handlers`, and
  value substitution), which itself mirrors the in-game advanced mod description.
- **Community grammar knowledge** — the trade-search regex conventions this tool
  targets (quoted-block AND `"A" "B"`, `!` negation, lookaheads and custom
  keywords on PoE 2, the 250-character query limit) are treated as functional
  behavior of the in-game trade sites and described here in our own words.
  No example text, query strings, or prose are reproduced from community wikis
  (which use CC BY-NC-SA), forums, subreddits, or guide sites. No unlicensed
  source was used.

Nods and kudos to the projects that made this possible:

- [PyPoE](https://github.com/OmegaK2/PyPoE) — MIT — translation/rendering prior art
- [RePoE](https://github.com/repoe-fork/repoe) — MIT — game-data extraction
- [RePoE (original)](https://github.com/brather1ng/RePoE) — MIT — upstream project
- [xUnit](https://github.com/xunit/xunit) — Apache 2.0 — test framework
- [Awesome Assertions](https://github.com/AwesomeAssertions/AwesomeAssertions) — Apache 2.0 — test assertions
- [TextCopy](https://github.com/CopyText/TextCopy) — MIT — cross-platform clipboard
- [.NET](https://dot.net) / [System.Text.Json](https://github.com/dotnet/runtime) — MIT — runtime and JSON

## License

MIT. This project is not affiliated with Grinding Gear Games; Path of Exile is a
registered trademark of GGG. Derived catalog data is community extraction from
RePoE (GGG-owned data) — see `NOTICE`.

## AI disclosure

AI assistance was used to generate and validate portions of this project. All
generated code and content adhere to the project's licensing and sourcing policy
(no reproduction of non-permissively licensed community content), and have been
reviewed and verified by the project maintainer.

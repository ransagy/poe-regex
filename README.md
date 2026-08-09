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

See `docs/DESIGN.md` for the full specification.

## Build & test

```sh
dotnet build PoeRegex.slnx
dotnet test PoeRegex.slnx
```

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
```

Exit codes: `0` ok, `1` error (including a query over the character limit),
`2` usage error.

## Data pipeline (reproducing the catalogs)

```sh
dotnet run --project tools/PoeRegex.Data.Build -- fetch --game poe1 --out data/raw/poe1
dotnet run --project tools/PoeRegex.Data.Build -- build --game poe1 --raw data/raw/poe1 --catalog data/catalogs/poe1.catalog.json
```

Re-running after a league patch re-sources the latest exports.

## License

MIT. This project is not affiliated with Grinding Gear Games; Path of Exile is a
registered trademark of GGG. Derived catalog data is community extraction from
RePoE (GGG-owned data) — see `NOTICE`.

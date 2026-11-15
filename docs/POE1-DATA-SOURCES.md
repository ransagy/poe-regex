# PoE1 data sources

PoE1 data comes from the game client's own exported files. The pipeline has a
primary source and a secondary compatible source so a patch launch does not
have to wait for the primary to catch up.

## Current state

- **Primary:** [RePoE](https://repoe-fork.github.io) (`poe1.html`).
  - Last checked: RePoE **3.29.3.6**; the 3.30 export had not been published.
- **Secondary:** [ExileData](https://exiledata.dev), documentation at
  <https://docs.exiledata.dev>.
  - Current dataset: **poe1 3.30.0.2**, generated 2026-11-15T08:20:46Z.
  - Used for the shipped 3.30 catalog while RePoE is behind.
  - Shipped counts: 1,612 mods (1,200 normal, 179 implicit, 233 corrupted),
    1,585 exact-table scopes, 85,784 foreign-corpus lines.
- **Snapshot:** `data/poe1.snapshot.json` records the actual source:
  - `sourceKind`: `repoe` or `exiledata`
  - `source`, `sourceVersion`, `generatedAt`, `downloadedAt`
  - `dataVersion` (the game patch, e.g. `3.30.0.2`)
  - the used files with sizes and SHA-256 checksums.

The PoE1 builder reads four raw files: `base_items.min.json`,
`mods.min.json`, `mods_by_base.min.json`, and `item_classes.min.json`.
RePoE's manifest classifies seven inputs; the other three are stat/translation
files used by other pipelines, not by the PoE1 normal-equipment builder.

## Why ExileData is compatible

ExileData publishes the same game-client exports in the same shapes and units
as RePoE:

- `critical_strike_chance` is in hundredths of a percent.
- `attack_time` is in milliseconds.
- `range` is in tenths of a metre.
- `mods_by_base` is keyed by plural item-class names and tag sets.
- `base_items` carries class, tags, implicits, properties, requirements, and
  release state.

The builder also tolerates ExileData's extra fields (`implicit_text`,
`stats`, `generation_weights`, and so on) and ignores extra non-equipment
records. ExileData is in preview; dataset addresses are immutable per patch,
so the snapshot pins `3.30.0.2` and verifies every checksum.

## Compatibility shims

ExileData's raw `mod.text` differs from RePoE in two ways that the builder
normalizes:

1. **Keyword markup.** ExileData keeps markup such as
   `[PlayerItemRarity|Item Rarity]` and `[Onslaught]`. The copied item text
   shows the display half only. `Poe1CatalogBuilder.CleanText` strips the
   markup, so the shipped catalog contains `+10% to Item Rarity` and
   `Onslaught`.
2. **Null or incomplete text.** ExileData leaves `mod.text` null for the
   granted-skill suffix and the corrupted support-gem implicits, and omits the
   `-15% additional Physical Damage Reduction` line from the crushed
   chaos-resistance implicits. `data/catalogs/poe1.mod-text-fallback.json`
   supplies the RePoE 3.29 wording for those records. The fallback is used only
   when the snapshot's `sourceKind` is `exiledata`; a RePoE build ignores it.

`stat_translations` is a folder in ExileData rather than one file, but the
PoE1 builder does not use it because PoE1 mods already carry rendered text.

## Refresh

### ExileData

```sh
dotnet run --project tools/PoeRegex.Data.Build -- poe1-fetch \
  --source exiledata --version 3.30.0.2 --refresh
dotnet run --project tools/PoeRegex.Data.Build -- poe1-build
dotnet run --project tools/PoeRegex.Data.Build -- poe1-matrix --from-scratch
dotnet run --project tools/PoeRegex.Data.Build -- poe1-status --check --check-shortest
```

`poe1-fetch` downloads the four used files from the immutable patch address,
checks each SHA-256 against ExileData's `manifest.json`, and writes
`data/poe1.snapshot.json`.

### RePoE

```sh
dotnet run --project tools/PoeRegex.Data.Build -- poe1-fetch --source repoe --refresh
dotnet run --project tools/PoeRegex.Data.Build -- poe1-build
dotnet run --project tools/PoeRegex.Data.Build -- poe1-matrix --from-scratch
dotnet run --project tools/PoeRegex.Data.Build -- poe1-status --check --check-shortest
```

The RePoE fetch records the version string from `poe1.html` as
`sourceVersion` and `dataVersion`.

## Compare two sources

```sh
dotnet run --project tools/PoeRegex.Data.Build -- poe1-compare \
  --left data/catalogs/poe1.catalog.json \
  --right /tmp/opencode/repoe/poe1.catalog.json \
  --left-vendor data/catalogs/poe1.vendor-corpus.json \
  --right-vendor /tmp/opencode/repoe/poe1.vendor-corpus.json \
  --left-table data/catalogs/poe1.fragments.json \
  --right-table /tmp/opencode/repoe/poe1.fragments.json \
  --out /tmp/opencode/poe1-source-diff.md
```

The report compares item classes, mods (id, source, generation type, family,
level, classes, groups, and every stat line), vendor-corpus lines, and exact
table scopes/fragments. `--fail-on-diff` makes the command exit 2 when any
difference is found.

## Moving back to RePoE

1. Confirm the RePoE `poe1.html` version matches the live patch.
2. Fetch RePoE: `poe1-fetch --source repoe --refresh`.
3. Build a temporary RePoE catalog without overwriting the shipped ExileData
   artifacts:

   ```sh
   dotnet run --project tools/PoeRegex.Data.Build -- poe1-build \
     --catalog /tmp/opencode/repoe/poe1.catalog.json \
     --vendor /tmp/opencode/repoe/poe1.vendor-corpus.json
   ```

4. Compare the ExileData and RePoE catalogs with `poe1-compare`.
5. If the only differences are expected patch changes (or the sources agree),
   switch the snapshot to RePoE by fetching it into the normal raw directory
   and rebuilding the shipped artifacts.
6. Rebuild `poe1-matrix --from-scratch` and verify
   `poe1-status --check --check-shortest`.
7. Run the full tests, data audit, PWA check, and preview/production smoke.
8. Update counts and the source note in `POE1-SCOPE.md`, `PROVENANCE.md`, and
   this document.
9. Deploy.

The builder is source-agnostic. Moving back should only change the snapshot
source metadata and the rendered text source. Keep the fallback file for
future ExileData refreshes; it is ignored for RePoE builds. Remove it only if
RePoE is confirmed to render every used mod completely.

## Ownership

Path of Exile game data, names, and assets belong to Grinding Gear Games.
RePoE and ExileData are independent community projects; ExileData is not
affiliated with or endorsed by Grinding Gear Games. See `NOTICE` and
`PROVENANCE.md` for the full lineage.

# Import audit — PoE2 workbench

Two import surfaces are covered:

1. **Item import** — advanced item description → parsed blocks → catalog mods.
2. **Search-string import** — a compiled quoted query → catalog mods/classes.

## Item import

### Catalog round-trip

`CatalogLines_AllMapBackToTheirSourceMod` walks every playable catalog mod,
every class it can roll on, and every display form:

- representative plain line
- `RangeLine`
- advanced `roll(min-max)` form

For each form, `MapLineToMods(class, line)` must return a candidate set that
contains the source mod. The audit currently checks more than 1,000 forms and
passes.

### Fixtures and policy

The five real item fixtures continue to pass their existing mapping tests:

- belt
- talisman
- crossbow
- staff
- corrupted mace

Expected exceptions remain explicit:

- implicits are quiet and do not participate in affix rules
- unknown text maps nowhere
- approximate renderer-shorthand matches stay flagged as `approximate`
- ambiguous text keeps a ranked candidate list; the header name/range is used
  to choose the default

## Search-string import

`QueryImportService` now handles:

- exact table fragments
- class anchors, including alternation anchors
- threshold/digit-range fragments
- alternation/compacted blocks, via an explain-based recovery fallback
- exclusions
- unknown/custom tokens preserved for review

The fallback is only used for synthetic/regex-shaped phrases, so plain unknown
text is still treated as a keyword rather than expanded into unrelated mods.

Regression tests:

- `QueryImport_ReconstructsAdvancedModsAndClass`
- `QueryImport_ResolvesAClassAnchor`
- `QueryImport_PreservesUnknownTokens`
- `QueryImport_HandlesAlternationAndThreshold`
- `QueryImport_RecoversThresholdOnly`

## Guide filter import

The Import string panel has an explicit **Guide filter** mode. It extracts one
filter line (or the most branch-like line from a paste), splits top-level `|`
branches, and maps each branch through the same name/fragment/family indexes
as search-string reconstruction:

- exact affix names become name picks;
- exact table fragments become fragment picks;
- every other branch is matched as a case-insensitive regex against family
  display text and tier text; multi-family branches get a multi-select with
  the common families selected by default;
- negative groups (`!parry|bow|...`) are applied as raw regex exclusions;
- unknown/base/vendor branches are dropped with a warning;
- the preview and apply path reuse the read-only Your-picks view.

Current guide limitations: no base-type list yet, and explicit class anchors
warn and import globally.

## Remaining limitations

- The catalog ships prefix/suffix generations only; implicits are intentionally
  unmapped unless their exact text overlaps a catalog affix.
- Approximate shorthand (`LightRadius`, `ManaLeech`) remains flagged, not silent.
- A line with neither a bracketed range nor a header/name match can remain
  ambiguous (`+N`) in the preview.
- Multi-class search-string import is applied by the reconstruction path;
  guide-filter apply is simple/global only.
- Guide filters do not yet support base/vendor names or class anchors;
  exclusions are applied as raw regex groups.
- Unmodeled unique/socketable/base text is outside the safety corpus.

## Reproduce

```sh
dotnet test tests/PoeRegex.Web.Tests --filter "FullyQualifiedName~ItemMappingTests"
dotnet test tests/PoeRegex.Web.Tests --filter "FullyQualifiedName~QueryImport"
```

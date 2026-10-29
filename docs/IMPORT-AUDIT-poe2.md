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

## Remaining limitations

- The catalog ships prefix/suffix generations only; implicits are intentionally
  unmapped unless their exact text overlaps a catalog affix.
- Approximate shorthand (`LightRadius`, `ManaLeech`) remains flagged, not silent.
- A line with neither a bracketed range nor a header/name match can remain
  ambiguous (`+N`) in the preview.
- Multi-class search-string import is preview-only; apply is wired for one
  simple/advanced scope.
- Unmodeled unique/socketable/base text is outside the safety corpus.

## Reproduce

```sh
dotnet test tests/PoeRegex.Web.Tests --filter "FullyQualifiedName~ItemMappingTests"
dotnet test tests/PoeRegex.Web.Tests --filter "FullyQualifiedName~QueryImport"
```

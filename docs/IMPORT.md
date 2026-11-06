# Import behavior and coverage — PoE2 workbench

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

A synthetic crafted helmet fixture covers the `{ Crafted Suffix Modifier ... }`
header path, crafted-source selection, and suffix rule counting. Expected
exceptions remain explicit:

- implicits are quiet and do not participate in affix rules
- unknown text maps nowhere
- approximate renderer-shorthand matches stay flagged as `approximate`
- ambiguous text keeps a ranked candidate list; the header name/range is used
  to choose the default

## Search-string import

`QueryImportService` handles:

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
  display text and tier text; multi-family branches get a picker dropdown
  with the common families selected by default;
- negative groups (`!parry|bow|...`) are applied as raw regex exclusions;
- class/rarity/base filters (`s: (...)`, `Item Class:`, `Rarity:`) and other
  known non-mod terms are preserved as custom text, grouped under one Custom
  text section with Any of / Must match / None of roles; custom text in the
  same quoted group as mods is ORed with them;
- unknown text that matches no known item line is dropped with a
  verify-in-game diagnostic;
- the preview and apply path reuse the read-only Your-picks view.

Guide limitations: no base-type list yet, and explicit `:`/`s:`
anchors are not mapped to slots; `s:` class filters are preserved as raw
`Item Class:` terms.

## Limitations

- The catalog ships explicit prefix/suffix affixes, Abyss desecrated
  prefixes/suffixes, and crafted prefixes/suffixes. Implicits are intentionally
  unmapped unless their exact text overlaps a catalog affix.
- `{ Desecrated Prefix/Suffix Modifier ... }` headers are parsed and mapped.
  A desecrated match is preferred, but a desecrated reveal can carry a normal
  explicit affix; when no desecrated match exists, the normal match is used.
- `{ Crafted Prefix/Suffix Modifier ... }` and `{ Master Crafted ... }`
  headers are parsed and mapped. A crafted match is preferred; when the line
  has no crafted catalog entry, the normal explicit match is used. Crafted
  stat lines are class-scoped from the raw crafted record, so a class that
  cannot carry the craft does not offer that family.
- Crafted class scope is class-level, not base-level: defence crafts are
  inferred from released base defence tags (`armour`, `evasion`,
  `energy_shield`), and generic meta-crafts apply to every normal equipment
  class. A craft that exists on only some bases in a class can still appear
  for the whole class.
- Desecrated tier labels show the roll range, but the exact table is
  pattern-based: ranges that share one stat pattern (for example `2–4%` and
  `4–7%`) compile to the same fragment, so the search string does not pin the
  chosen range. The picker collapses those tiers into one row while range
  strings remain pattern-based. Range-specific strings need range-aware proof
  scopes.
- Desecrated tier exclusions are family-wide for the same reason: excluding
  one range excludes every range that shares its fragment.
- Augments, bonded modifiers, corrupted mods, and jewel affixes are not
  modeled yet; crafted stat lines map to crafted entries where available.
  Jewel lines remain unmapped.
- Base implicits remain class-level when added: the catalog can say a line
  exists on some base in a class, but not which base type.
- Approximate shorthand (`LightRadius`, `ManaLeech`) remains flagged, not silent.
- A line with neither a bracketed range nor a header/name match can remain
  ambiguous (`+N`) in the preview.
- Multi-class search-string import is applied by the reconstruction path;
  guide-filter apply is simple/global only.
- Guide filters preserve class/rarity/base filters as imported raw terms but
  do not map them to slots yet. Exclusions are applied as raw regex groups.
- Unmodeled unique/socketable/base text is outside the safety corpus.
- Fragment choices can change after a catalog/corpus rebuild. An old raw
  search string that uses a retired fragment may fail reconstruction; re-copy
  it from the current build. The table build keeps existing fragments when
  they still pass and remain shortest, so only forced changes retire old
  strings. Saved picks and share links store picks and recompile against the
  current table.

## Reproduce

```sh
dotnet test tests/PoeRegex.Web.Tests --filter "FullyQualifiedName~ItemMappingTests"
dotnet test tests/PoeRegex.Web.Tests --filter "FullyQualifiedName~Crafted"
dotnet test tests/PoeRegex.Web.Tests --filter "FullyQualifiedName~QueryImport"
```

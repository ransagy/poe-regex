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
passes. A second pass builds realistic advanced forms (ranges and header names)
and checks catalog forms map exactly; all non-implicit advanced forms pass.

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
- the fuzzy word-stem fallback remains only for unmatched renderer wording and
  stays flagged as `approximate`
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
The coverage tool now defaults to the reconstruction report; `--legacy` keeps
the older `QueryImportService` spike for comparison.

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
  prefixes/suffixes, and implicit-only base families. Import keeps implicit
  lines quiet; a line that also exists as a normal affix still maps to that
  normal affix (the crossbow bolt implicit).
- `{ Desecrated Prefix/Suffix Modifier ... }` headers are parsed and mapped.
  A desecrated match is preferred, but a desecrated reveal can carry a normal
  explicit affix; when no desecrated match exists, the normal match is used.
- `{ Crafted Prefix/Suffix Modifier ... }` and `{ Master Crafted ... }`
  headers are parsed, but no crafted normal-equipment catalog source is
  shipped; those lines fall back to normal explicit matches when the text
  matches one.
- Desecrated tier labels show the roll range, but the exact table is
  pattern-based: ranges that share one stat pattern (for example `2–4%` and
  `4–7%`) compile to the same fragment, so the search string does not pin the
  chosen range. The picker collapses those tiers into one row while range
  strings remain pattern-based. Range-specific strings need range-aware proof
  scopes.
- Desecrated tier exclusions are family-wide for the same reason: excluding
  one range excludes every range that shares its fragment. The compiler
  canonicalizes identical exclusion fragments to one block; a source string
  that still contains duplicate blocks is rejected as non-canonical rather
  than replayed.
- Basic runes, Soul Cores, Abyssal Eyes, and bonded effects are modeled in the
  picker but import mapping is deferred; special/transformative runes, idols,
  jewel affixes, and the socket model are not modeled yet. The PoE2
  export's `crafted` prefix/suffix records are PoE1 master-craft leftovers
  (no positive PoE2 base mapping) and are excluded; crafted stat lines can
  still map to a normal affix with the same text. Actual PoE2 crafted mods are
  jewel-only (`CraftedJewel*`, domain `misc`) and remain unmodeled with
  jewels.
- Corrupted enchantments are modeled as a separate non-affix kind. Game copy
  presents them as implicit modifiers, so import keeps those lines quiet; an
  explicit `{ Corrupted Modifier }` header (the app's built-item text) maps.
  The builder allows at most two. Five raw corrupted records without a
  positive base/spawn reference are excluded.
- Base implicits are modeled at class level: the catalog can say a line exists
  on some base in a class, but not which base type.
- Implicit-only and corrupted catalog entries are class-level picker/builder
  families; import never annotates plain game implicit lines, because the same
  header is used for base implicits and corruption enchantments.
- Advanced range forms are range-first: rounded displayed rolls get half a
  display unit of tolerance, reversed ranges (`10(15-10)`) are accepted, and
  decimal stats use the catalog's two-placeholder pattern shape (`0.17` ->
  `\x01.\x01`).
- Multi-line stat descriptions are mapped line by line; a wrapped first line
  can remain approximate (the Focus infusion line).
- Genuine ambiguity, such as two desecrated Cost Efficiency suffixes sharing
  one text, keeps a ranked candidate list with a deterministic default.
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
dotnet test tests/PoeRegex.Web.Tests --filter "FullyQualifiedName~CraftedLegacyTests"
dotnet test tests/PoeRegex.Web.Tests --filter "FullyQualifiedName~QueryImport"
```

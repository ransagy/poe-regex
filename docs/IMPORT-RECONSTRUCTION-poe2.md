# PoE2 query reconstruction — Phase 1 results

_Generated 2026-11-02 00:04 UTC._

- Data: `2026-08-09`
- Target model: `advanced-ranges-v1`
- Fragment table: `2026-08-09`

## Method

The same representative strings as the coverage spike are reconstructed with
`QueryReconstructionService`, then the recovered criteria are recompiled and
compared byte-for-byte with the original. A case passes only when it is both
reconstructed and recompiles to the identical string. Tierless fragments prefer
the family pick; name literals stay name picks; factored and multi-class forms
are expanded. The Import string panel applies the recovered slots; the same
comparison is exercised by the UI tests. Free text, legacy anchors, class-only
strings, and minimum values fail closed or are accepted losses.

## Summary

| Scope | Cases | reconstructed | recompiled identically | fail-closed |
|---|---:|---:|---:|---:|
| advanced | 5 | 5 | 5 | 0 |
| edge | 5 | 0 | 0 | 5 |
| paste | 5 | 5 | 5 | 0 |
| simple | 7 | 6 | 6 | 1 |

**Overall:** 16/22 cases reconstruct and recompile identically.

## Matrix

| Case | Scope | Result | Mode | Class | Picks | MatchAll | Notes |
|---|---|---|---|---|---:|---|---|
| `simple-plain-1` | simple | **pass** | simple | — | 1 | no | identical recompile |
| `simple-any-2` | simple | **pass** | simple | — | 2 | no | identical recompile |
| `simple-all-2` | simple | **pass** | simple | — | 2 | yes | identical recompile |
| `simple-exclusion` | simple | **pass** | simple | — | 1 | no | identical recompile |
| `simple-name` | simple | **pass** | simple | — | 1 | no | identical recompile |
| `simple-family` | simple | **pass** | simple | — | 1 | no | identical recompile |
| `simple-min` | simple | fail-closed | simple | — | 0 | no | fragment '\+(\d{3,}\|[2-9]\d\|1[0-9])\(\d+-\d+\) to maximum Life' is not an unfactored table fragment for thi... |
| `advanced-plain` | advanced | **pass** | advanced | Ring | 1 | no | identical recompile |
| `advanced-name` | advanced | **pass** | advanced | Ring | 1 | no | identical recompile |
| `advanced-family` | advanced | **pass** | advanced | Ring | 1 | no | identical recompile |
| `advanced-any-2` | advanced | **pass** | advanced | Ring | 2 | no | identical recompile |
| `advanced-all-2` | advanced | **pass** | advanced | Ring | 2 | yes | identical recompile |
| `paste-rings` | paste | **pass** | advanced | Ring | 2 | no | identical; rings merge into one Ring scope (accepted) |
| `paste-weapon-offhand` | paste | **pass** | advanced | Crossbow, Shield | 2 | no | identical recompile |
| `paste-armour` | paste | **pass** | advanced | Boots, Gloves, Helmet | 3 | no | identical recompile |
| `paste-exclusion` | paste | **pass** | advanced | Boots, Helmet | 2 | no | identical recompile |
| `paste-same-class-allof` | paste | **pass** | advanced | Ring | 2 | no | identical; same-class all-of falls back to any-of (accepted) |
| `edge-class-anchor` | edge | fail-closed | simple | — | 0 | no | a class-only advanced string cannot be represented by a slot without picks or exclusions |
| `edge-old-anchor` | edge | fail-closed | simple | — | 0 | no | recompiled query differs (any: '": On.+M"'; all: '": On.+M"') |
| `edge-unknown-token` | edge | fail-closed | simple | — | 0 | no | fragment 'zzzz' is not an unfactored table fragment for this scope |
| `edge-shape` | edge | fail-closed | simple | — | 0 | no | invalid query shape: unterminated quoted block |
| `edge-free-text-and-mod` | edge | fail-closed | simple | — | 0 | no | fragment 'life' is not an unfactored table fragment for this scope |

## Passing examples

### `simple-plain-1` — one plain pick
- query: `"\+.*ife"`
- mode: simple
- global — any of: `family:IncreasedLife1`
- recompile: identical

### `simple-any-2` — two plain picks, any-of
- query: `"\+.*ife\|o Str"`
- mode: simple
- global — any of: `family:IncreasedLife1`, `family:Strength1`
- recompile: identical

### `simple-all-2` — two plain picks, all-of
- query: `"\+.*ife" "o Str"`
- mode: simple
- global — all of: `family:IncreasedLife1`, `family:Strength1`
- recompile: identical

### `simple-exclusion` — plain pick + exclusion
- query: `"\+.*ife" !"o Str"`
- mode: simple
- global — any of: `family:IncreasedLife1`
- exclusions: `Strength1`
- recompile: identical

### `simple-name` — one affix-name pick
- query: `"Clouded\|Darkened\|Impure\|Malignant\|Tainted\|Vile"`
- mode: simple
- global — any of: `name:Impure\|Tainted\|Clouded\|Darkened\|Malignant\|Vile`
- recompile: identical

### `simple-family` — one family pick
- query: `"d cha.*ge$"`
- mode: simple
- global — any of: `family:ChaosDamagePrefixOnTwoHandWeapon1`
- recompile: identical

### `advanced-plain` — Ring plain pick
- query: `": Ri" "\+.*fe"`
- mode: advanced
- ring1 · Ring — any of: `family:IncreasedLife1`
- recompile: identical

### `advanced-name` — Ring affix-name pick
- query: `": Ri" "Clouded\|Darkened\|Impure\|Malignant\|Tainted\|Vile"`
- mode: advanced
- ring1 · Ring — any of: `name:Impure\|Tainted\|Clouded\|Darkened\|Malignant\|Vile`
- recompile: identical

### `advanced-family` — Ring family pick
- query: `": Ri" "d Ch"`
- mode: advanced
- ring1 · Ring — any of: `family:ChaosDamagePercent1`
- recompile: identical

### `advanced-any-2` — Ring two picks, any-of
- query: `": Ri" "d Ch\|ed Co"`
- mode: advanced
- ring1 · Ring — any of: `family:ChaosDamagePercent1`, `family:ColdDamagePercent1`
- recompile: identical

### `advanced-all-2` — Ring two picks, all-of
- query: `": Ri" "d Ch" "ed Co"`
- mode: advanced
- ring1 · Ring — all of: `family:ChaosDamagePercent1`, `family:ColdDamagePercent1`
- recompile: identical

### `paste-rings` — ring1 + ring2
- query: `": Ri" "d Ch\|ed Co"`
- mode: advanced
- ring1 · Ring — any of: `family:ChaosDamagePercent1`, `family:ColdDamagePercent1`
- recompile: identical

### `paste-weapon-offhand` — weapon + offhand
- query: `": (Cro\|Shi.+s)" ": (Shi.+s)\|ith" ": (Cro)\|i.*ur$"`
- mode: advanced
- weapon · Crossbow — any of: `family:WeaponElementalDamageOnTwohandWeapon1`
- offhand · Shield — any of: `family:LocalIncreasedPhysicalDamageReductionRatingPercent1`
- recompile: identical

### `paste-armour` — helmet + boots + gloves
- query: `": (Boo\|Gl\|Hel)" "i.*ur$"`
- mode: advanced
- boots · Boots — any of: `family:LocalIncreasedPhysicalDamageReductionRatingPercent1`
- gloves · Gloves — any of: `family:LocalIncreasedPhysicalDamageReductionRatingPercent1`
- helmet · Helmet — any of: `family:LocalIncreasedPhysicalDamageReductionRatingPercent1`
- recompile: identical

### `paste-exclusion` — two slots + exclusion
- query: `": (Boo\|Hel)" "i.*ur$" !"o Str"`
- mode: advanced
- boots · Boots — any of: `family:LocalIncreasedPhysicalDamageReductionRatingPercent1`
- helmet · Helmet — any of: `family:LocalIncreasedPhysicalDamageReductionRatingPercent1`
- exclusions: `Strength1`
- recompile: identical

### `paste-same-class-allof` — ring1 + ring2 all-of (accepted fallback)
- query: `": Ri" "d Ch\|ed Co"`
- mode: advanced
- ring1 · Ring — any of: `family:ChaosDamagePercent1`, `family:ColdDamagePercent1`
- recompile: identical

## Fail-closed examples

- `simple-min` (simple) — fragment '\+(\d{3,}\|[2-9]\d\|1[0-9])\(\d+-\d+\) to maximum Life' is not an unfactored table fragment for thi...
- `edge-class-anchor` (edge) — a class-only advanced string cannot be represented by a slot without picks or exclusions
- `edge-old-anchor` (edge) — recompiled query differs (any: '": On.+M"'; all: '": On.+M"')
- `edge-unknown-token` (edge) — fragment 'zzzz' is not an unfactored table fragment for this scope
- `edge-shape` (edge) — invalid query shape: unterminated quoted block
- `edge-free-text-and-mod` (edge) — fragment 'life' is not an unfactored table fragment for this scope

## Review notes

- **Simple affix-name picks choose one owning family when the names exist on
  multiple classes.** The names are the query term; the family only supplies
  identity, so the string is unchanged. An advanced anchor scopes it explicitly.
- **Legacy literal class anchors** (e.g. `": One Hand M"`) reconstruct the class
  but recompile to the current wildcard anchor, so they fail the same-string check.
- **Tierless fragments reconstruct as family picks**, not representative tiers.
  When a fragment or name set spans families, the chosen family is deterministic
  but arbitrary; the query is unchanged.
- **Ring1 + ring2 paste** reconstructs to one Ring scope; exact ring split is a
  non-goal and the string is unchanged.
- **Same-class all-of** falls back to any-of in the compiler; the reconstruction
  reports the any-of form (accepted).
- **Minimum values are genuinely lost**; the query encodes a threshold that the
  pick model cannot recover (accepted limitation).
- Factored fragments/name sets and multi-class pastes are supported by this
  path. The Import string panel applies the recovered slots.
- Guide-filter extraction ships as a separate mode: exact names/fragments and
  regex matches against family/tier text map; multi-family branches use a
  multi-select. Base/vendor names, exclusions, and explicit class anchors are
  follow-ups.

Tests: `dotnet test tests/PoeRegex.Web.Tests/PoeRegex.Web.Tests.csproj -c Release --filter "FullyQualifiedName~QueryReconstructionTests"`.

# PoE2 query reconstruction — Phase 1 results

_Generated 2026-10-31 19:47 UTC._

- Data: `2026-08-09`
- Target model: `advanced-ranges-v1`
- Fragment table: `2026-08-09`

## Method

The same representative strings as the coverage spike are reconstructed with
`QueryReconstructionService`, then the recovered criteria are recompiled and
compared byte-for-byte with the original. A case passes only when it is both
reconstructed and recompiles to the identical string. Factored alternations,
multi-class pastes, free text, and legacy anchors intentionally fail closed.

## Summary

| Scope | Cases | reconstructed | recompiled identically | fail-closed |
|---|---:|---:|---:|---:|
| advanced | 5 | 5 | 5 | 0 |
| edge | 5 | 1 | 1 | 4 |
| paste | 5 | 2 | 2 | 3 |
| simple | 7 | 5 | 5 | 2 |

**Overall:** 13/22 cases reconstruct and recompile identically.

## Matrix

| Case | Scope | Result | Mode | Class | Picks | MatchAll | Notes |
|---|---|---|---|---|---:|---|---|
| `simple-plain-1` | simple | **pass** | simple | — | 1 | no | identical recompile |
| `simple-any-2` | simple | **pass** | simple | — | 2 | no | identical recompile |
| `simple-all-2` | simple | **pass** | simple | — | 2 | yes | identical recompile |
| `simple-exclusion` | simple | **pass** | simple | — | 1 | no | identical recompile |
| `simple-name` | simple | fail-closed | simple | — | 0 | no | affix-name set spans multiple families; add a class anchor or choose one |
| `simple-family` | simple | **pass** | simple | — | 1 | no | identical recompile |
| `simple-min` | simple | fail-closed | simple | — | 0 | no | fragment '\+(\d{3,}\|[2-9]\d\|1[0-9])\(\d+-\d+\) to maximum Life' is not an unfactored table fragment for thi... |
| `advanced-plain` | advanced | **pass** | advanced | Ring | 1 | no | identical recompile |
| `advanced-name` | advanced | **pass** | advanced | Ring | 1 | no | identical recompile |
| `advanced-family` | advanced | **pass** | advanced | Ring | 1 | no | identical recompile |
| `advanced-any-2` | advanced | **pass** | advanced | Ring | 2 | no | identical recompile |
| `advanced-all-2` | advanced | **pass** | advanced | Ring | 2 | yes | identical recompile |
| `paste-rings` | paste | **pass** | advanced | Ring | 2 | no | identical; rings merge into one Ring scope (accepted) |
| `paste-weapon-offhand` | paste | fail-closed | simple | — | 0 | no | multi-class paste reconstruction is not in Phase 1 |
| `paste-armour` | paste | fail-closed | simple | — | 0 | no | multi-class paste reconstruction is not in Phase 1 |
| `paste-exclusion` | paste | fail-closed | simple | — | 0 | no | multi-class paste reconstruction is not in Phase 1 |
| `paste-same-class-allof` | paste | **pass** | advanced | Ring | 2 | no | identical; same-class all-of falls back to any-of (accepted) |
| `edge-class-anchor` | edge | **pass** | advanced | Ring | 0 | no | identical recompile |
| `edge-old-anchor` | edge | fail-closed | simple | — | 0 | no | recompiled query differs (any: '": On.+M"'; all: '": On.+M"') |
| `edge-unknown-token` | edge | fail-closed | simple | — | 0 | no | fragment 'zzzz' is not an unfactored table fragment for this scope (term 'zzzz') |
| `edge-shape` | edge | fail-closed | simple | — | 0 | no | invalid query shape: unterminated quoted block |
| `edge-free-text-and-mod` | edge | fail-closed | simple | — | 0 | no | fragment 'life' is not an unfactored table fragment for this scope (term 'life') |

## Passing examples

### `simple-plain-1` — one plain pick
- query: `"\+.*ife"`
- mode: simple
- picks: `IncreasedLife1`
- match-all: no
- recompile: identical

### `simple-any-2` — two plain picks, any-of
- query: `"\+.*ife\|o Str"`
- mode: simple
- picks: `IncreasedLife1`, `Strength1`
- match-all: no
- recompile: identical

### `simple-all-2` — two plain picks, all-of
- query: `"\+.*ife" "o Str"`
- mode: simple
- picks: `IncreasedLife1`, `Strength1`
- match-all: yes
- recompile: identical

### `simple-exclusion` — plain pick + exclusion
- query: `"\+.*ife" !"o Str"`
- mode: simple
- picks: `IncreasedLife1`
- exclusions: `Strength1`
- match-all: no
- recompile: identical

### `simple-family` — one family pick
- query: `"d cha.*ge$"`
- mode: simple
- picks: `ChaosDamagePercent1`
- match-all: no
- recompile: identical

### `advanced-plain` — Ring plain pick
- query: `": Ri" "\+.*fe"`
- mode: advanced · Ring
- picks: `IncreasedLife1`
- match-all: no
- recompile: identical

### `advanced-name` — Ring affix-name pick
- query: `": Ri" "Clouded\|Darkened\|Impure\|Malignant\|Tainted\|Vile"`
- mode: advanced · Ring
- picks: `name:Clouded\|Darkened\|Impure\|Malignant\|Tainted\|Vile`
- match-all: no
- recompile: identical

### `advanced-family` — Ring family pick
- query: `": Ri" "d Ch"`
- mode: advanced · Ring
- picks: `ChaosDamagePercent1`
- match-all: no
- recompile: identical

### `advanced-any-2` — Ring two picks, any-of
- query: `": Ri" "d Ch\|ed Co"`
- mode: advanced · Ring
- picks: `ChaosDamagePercent1`, `ColdDamagePercent1`
- match-all: no
- recompile: identical

### `advanced-all-2` — Ring two picks, all-of
- query: `": Ri" "d Ch" "ed Co"`
- mode: advanced · Ring
- picks: `ChaosDamagePercent1`, `ColdDamagePercent1`
- match-all: yes
- recompile: identical

### `paste-rings` — ring1 + ring2
- query: `": Ri" "d Ch\|ed Co"`
- mode: advanced · Ring
- picks: `ChaosDamagePercent1`, `ColdDamagePercent1`
- match-all: no
- recompile: identical

### `paste-same-class-allof` — ring1 + ring2 all-of (accepted fallback)
- query: `": Ri" "d Ch\|ed Co"`
- mode: advanced · Ring
- picks: `ChaosDamagePercent1`, `ColdDamagePercent1`
- match-all: no
- recompile: identical

### `edge-class-anchor` — class anchor only
- query: `": Ri"`
- mode: advanced · Ring
- picks: —
- match-all: no
- recompile: identical

## Fail-closed examples

- `simple-name` (simple) — affix-name set spans multiple families; add a class anchor or choose one
- `simple-min` (simple) — fragment '\+(\d{3,}\|[2-9]\d\|1[0-9])\(\d+-\d+\) to maximum Life' is not an unfactored table fragment for thi...
- `paste-weapon-offhand` (paste) — multi-class paste reconstruction is not in Phase 1
- `paste-armour` (paste) — multi-class paste reconstruction is not in Phase 1
- `paste-exclusion` (paste) — multi-class paste reconstruction is not in Phase 1
- `edge-old-anchor` (edge) — recompiled query differs (any: '": On.+M"'; all: '": On.+M"')
- `edge-unknown-token` (edge) — fragment 'zzzz' is not an unfactored table fragment for this scope (term 'zzzz')
- `edge-shape` (edge) — invalid query shape: unterminated quoted block
- `edge-free-text-and-mod` (edge) — fragment 'life' is not an unfactored table fragment for this scope (term 'life')

## Review notes

- **Simple affix-name picks are ambiguous when the name exists on multiple
  classes.** Phase 1 refuses to guess; the advanced anchor supplies the scope.
- **Legacy literal class anchors** (e.g. `": One Hand M"`) reconstruct the class
  but recompile to the current wildcard anchor, so they fail the same-string check.
- **Shared-fragment mod identity is deterministic but arbitrary.** The query is
  equivalent even when several catalog mods share the selected fragment.
- **Ring1 + ring2 paste** reconstructs to one Ring scope; exact ring split is a
  non-goal and the string is unchanged.
- **Same-class all-of** falls back to any-of in the compiler; the reconstruction
  reports the any-of form (accepted).
- **Family picks** recover one representative mod; the query is equivalent, but
  the pick identity is arbitrary among mods that share the fragment.
- Factored fragments/name sets and multi-class pastes are Phase 2; there is no
  UI wiring yet.

Tests: `dotnet test tests/PoeRegex.Web.Tests/PoeRegex.Web.Tests.csproj -c Release --filter "FullyQualifiedName~QueryReconstructionTests"`.

# PoE2 import coverage (spike)

_Generated 2026-10-31 17:15 UTC._

- Data: `2026-08-09`
- Target model: `advanced-ranges-v1`
- Fragment table: `2026-08-09`

## Method

Each representative app string is compiled by the shipped compiler, imported with the
current best-effort `QueryImportService`, then the recovered criteria are recompiled
(any-of and, when available, all-of). Classification:

- **exact** — the recompiled string is identical with no recovery diagnostics.
- **factored** — identical after the importer's explain/factoring recovery.
- **ambiguous** — every token resolved, but the recompiled string differs.
- **unrecoverable** — free text, unresolved/shape errors, lost minimum values, or paste structure loss.

## App strings

| Scope | Cases | exact | factored | ambiguous | unrecoverable |
|---|---:|---:|---:|---:|---:|
| advanced | 5 | 0 | 4 | 0 | 1 |
| edge | 5 | 1 | 0 | 1 | 3 |
| paste | 5 | 0 | 2 | 0 | 3 |
| simple | 7 | 0 | 4 | 1 | 2 |

### Cases

| Case | Scope | Classification | Import mode | Why |
|---|---|---|---|---|
| `simple-plain-1` | simple | **factored** | simple | query-equivalent via an arbitrary catalog mod choice |
| `simple-any-2` | simple | **ambiguous** | simple | recovered criteria recompile to a different query |
| `simple-all-2` | simple | **factored** | simple | query-equivalent via an arbitrary catalog mod choice |
| `simple-exclusion` | simple | **factored** | simple | query-equivalent via an arbitrary catalog mod choice |
| `simple-name` | simple | **unrecoverable** | simple | affix-name pick is preserved as free text (needs the affix-name index) |
| `simple-family` | simple | **factored** | simple | query-equivalent via an arbitrary catalog mod choice |
| `simple-min` | simple | **unrecoverable** | simple | minimum value is not encoded in the string |
| `advanced-plain` | advanced | **factored** | advanced | query-equivalent via an arbitrary catalog mod choice |
| `advanced-name` | advanced | **unrecoverable** | advanced | affix-name pick is preserved as free text (needs the affix-name index) |
| `advanced-family` | advanced | **factored** | advanced | query-equivalent via an arbitrary catalog mod choice |
| `advanced-any-2` | advanced | **factored** | advanced | query-equivalent via explain recovery |
| `advanced-all-2` | advanced | **factored** | advanced | query-equivalent via an arbitrary catalog mod choice |
| `paste-rings` | paste | **factored** | advanced | query-equivalent via explain recovery |
| `paste-weapon-offhand` | paste | **unrecoverable** | multi-class | multi-class paste: per-slot mapping is lost |
| `paste-armour` | paste | **unrecoverable** | multi-class | multi-class paste: per-slot mapping is lost |
| `paste-exclusion` | paste | **unrecoverable** | multi-class | multi-class paste: per-slot mapping is lost |
| `paste-same-class-allof` | paste | **factored** | advanced | query-equivalent via explain recovery |
| `edge-class-anchor` | edge | **exact** | advanced | identical recompile |
| `edge-old-anchor` | edge | **ambiguous** | advanced | legacy literal class anchor recompiles to the current wildcard |
| `edge-unknown-token` | edge | **unrecoverable** | simple | free text / unknown token is preserved as a keyword |
| `edge-shape` | edge | **unrecoverable** | simple | invalid query shape |
| `edge-free-text-and-mod` | edge | **unrecoverable** | simple | free text / unknown token is preserved as a keyword |

### Failure reasons

| Reason | Count | Cases |
|---|---:|---|
| multi-class paste: per-slot mapping is lost | 3 | `paste-weapon-offhand`, `paste-armour`, `paste-exclusion` |
| affix-name pick is preserved as free text (needs the affix-name index) | 2 | `simple-name`, `advanced-name` |
| free text / unknown token is preserved as a keyword | 2 | `edge-unknown-token`, `edge-free-text-and-mod` |
| recovered criteria recompile to a different query | 1 | `simple-any-2` |
| minimum value is not encoded in the string | 1 | `simple-min` |
| legacy literal class anchor recompiles to the current wildcard | 1 | `edge-old-anchor` |
| invalid query shape | 1 | `edge-shape` |

### Representative queries

- `simple-plain-1` — one plain pick
  - compiled: `"\+.*ife"`
  - recompiled: `"\+.*ife"`
  - diagnostics: fragment '\+.*ife' maps to 49 mods; using 'IncreasedLife1'
- `simple-any-2` — two plain picks, any-of
  - compiled: `"\+.*ife\|o Str"`
  - recompiled: `"\+.*ife\|nd Ev\|o Str\|r a.*ld"`
  - diagnostics: fragment '\+.*ife\|o Str' recovered 58 catalog mod(s) via explain
- `simple-all-2` — two plain picks, all-of
  - compiled: `"\+.*ife" "o Str"`
  - recompiled: `"\+.*ife" "o Str"`
  - diagnostics: fragment '\+.*ife' maps to 49 mods; using 'IncreasedLife1'; fragment 'o Str' maps to 9 mods; using 'Strength1'
- `simple-exclusion` — plain pick + exclusion
  - compiled: `"\+.*ife" !"o Str"`
  - recompiled: `"\+.*ife" !"o Str"`
  - diagnostics: fragment '\+.*ife' maps to 49 mods; using 'IncreasedLife1'; fragment 'o Str' maps to 9 mods; using 'Strength1'
- `simple-name` — one affix-name pick
  - compiled: `"Clouded\|Darkened\|Impure\|Malignant\|Tainted\|Vile"`
  - recompiled: `"Clouded\|Darkened\|Impure\|Malignant\|Tainted\|Vile"`
  - diagnostics: unmatched token 'Clouded\|Darkened\|Impure\|Malignant\|Tainted\|Vile' preserved as keyword
- `simple-family` — one family pick
  - compiled: `"d cha.*ge$"`
  - recompiled: `"d cha.*ge$"`
  - diagnostics: fragment 'd cha.*ge$' maps to 22 mods; using 'ChaosDamagePercent1'
- `simple-min` — plain pick with minimum
  - compiled: `"\+(\d{3,}\|[2-9]\d\|1[0-9])\(\d+-\d+\) to maximum Life"`
  - recompiled: `"\+.*ife\|nd Ev\|r a.*ld"`
  - diagnostics: fragment '\+(\d{3,}\|[2-9]\d\|1[0-9])\(\d+-\d+\) to maximum Life' recovered 49 catalog mo...
- `advanced-plain` — Ring plain pick
  - compiled: `": Ri" "\+.*fe"`
  - recompiled: `": Ri" "\+.*fe"`
  - diagnostics: fragment '\+.*fe' maps to 49 mods; using 'IncreasedLife1'
- `advanced-name` — Ring affix-name pick
  - compiled: `": Ri" "Clouded\|Darkened\|Impure\|Malignant\|Tainted\|Vile"`
  - recompiled: `": Ri" "Clouded\|Darkened\|Impure\|Malignant\|Tainted\|Vile"`
  - diagnostics: unmatched token 'Clouded\|Darkened\|Impure\|Malignant\|Tainted\|Vile' preserved as keyword
- `advanced-family` — Ring family pick
  - compiled: `": Ri" "d Ch"`
  - recompiled: `": Ri" "d Ch"`
  - diagnostics: fragment 'd Ch' maps to 22 mods; using 'ChaosDamagePercent1'
- `advanced-any-2` — Ring two picks, any-of
  - compiled: `": Ri" "d Ch\|ed Co"`
  - recompiled: `": Ri" "d Ch\|ed Co"`
  - diagnostics: fragment 'd Ch\|ed Co' recovered 12 catalog mod(s) via explain
- `advanced-all-2` — Ring two picks, all-of
  - compiled: `": Ri" "d Ch" "ed Co"`
  - recompiled: `": Ri" "d Ch" "ed Co"`
  - diagnostics: fragment 'd Ch' maps to 22 mods; using 'ChaosDamagePercent1'; fragment 'ed Co' maps to 22 mods; using 'ColdDamagePercent1'
- `paste-rings` — ring1 + ring2
  - compiled: `": Ri" "d Ch\|ed Co"`
  - recompiled: `": Ri" "d Ch\|ed Co"`
  - diagnostics: fragment 'd Ch\|ed Co' recovered 12 catalog mod(s) via explain
- `paste-weapon-offhand` — weapon + offhand
  - compiled: `": (Cro\|Shi.+s)" ": (Shi.+s)\|ith" ": (Cro)\|i.*ur$"`
  - diagnostics: fragment ': (Shi.+s)\|ith' recovered 6 catalog mod(s) via explain; fragment ': (Cro)\|i.*ur$' recovered 14 catalog mod(s) via explain
- `paste-armour` — helmet + boots + gloves
  - compiled: `": (Boo\|Gl\|Hel)" "i.*ur$"`
  - diagnostics: fragment 'i.*ur$' maps to 39 mods; using 'LocalArmourAndStunThreshold1'
- `paste-exclusion` — two slots + exclusion
  - compiled: `": (Boo\|Hel)" "i.*ur$" !"o Str"`
  - diagnostics: fragment 'i.*ur$' maps to 39 mods; using 'LocalArmourAndStunThreshold1'; fragment 'o Str' maps to 9 mods; using 'Strength1'
- `paste-same-class-allof` — ring1 + ring2 all-of (accepted fallback)
  - compiled: `": Ri" "d Ch\|ed Co"`
  - recompiled: `": Ri" "d Ch\|ed Co"`
  - diagnostics: fragment 'd Ch\|ed Co' recovered 12 catalog mod(s) via explain

## Guide branches

Guide input was supplied locally and is not committed (provenance); only
classifications and counts appear here.

| Guide | Branches | one family | multiple families | affix name/tier | base/vendor | unknown/free | class anchor |
|---|---:|---:|---:|---:|---:|---:|---|
| FGkorbyn Act 1 | 7 | 1 | 4 | 2 | 0 | 0 | no |
| FGkorbyn Act 2 | 7 | 1 | 3 | 2 | 1 | 0 | no |
| FGkorbyn Act 3 | 7 | 1 | 3 | 3 | 0 | 0 | no |
| FGkorbyn Act 4 | 9 | 1 | 5 | 3 | 0 | 0 | no |
| GuyThatDies huntress | 4 | 1 | 1 | 0 | 1 | 1 | no |
| Lundburgerr Warrior | 6 | 0 | 5 | 0 | 1 | 0 | no |

**Total:** 40 branches — 5 one-family, 21 multi-family, 10 affix-name, 3 base/vendor, 1 unknown/free text.
0 of 6 guide filters carry a class anchor; the rest are global-simple candidates.

## Reading this

- App strings reproduced query-equivalently: **11 / 22**.
- 9 of those relied on an arbitrary catalog mod for a shared fragment; the
  query matches, but the imported pick identity is not guaranteed.
- Ambiguous: **2**; unrecoverable: **9**.
- Affix-name picks currently survive only as free-text keywords; they need the
  affix-name index. Minimum values and modded multi-class pastes also need the
  shared reverse-mapping layer; the per-case table shows exactly where.

## Recommendation

- **Start Phase 1.** 11/22 app strings are query-equivalent today, but 9 depend on an arbitrary mod for a shared fragment and 2 affix-name picks survive only as free text. The anchor/group parser, affix-name index, and single-slot reconstruction make those deterministic.
- **Budget Phase 2 for paste.** All 3 multi-class paste cases lose their per-slot mapping; this is the largest structural gap.
- **Factoring is not the first blocker.** Ten cases resolve through explain/recovery and only one any-of arrangement recompiles differently in this sample; keep the factored resolver on the Phase 2 list rather than starting there.
- **Guides are promising but not next.** The sample has 40 branches, mostly multi-family (21) and affix names (10), with no class anchors. Family/affix extraction is worth prototyping, but multi-family reconciliation and a base-type list are prerequisites.

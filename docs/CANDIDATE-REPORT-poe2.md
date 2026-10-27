# PoE2 candidate report — proof-assisted deep pass

Artifact: `docs/CANDIDATE-MATRIX-poe2.html`  
Status: `docs/CANDIDATE-STATUS-poe2.json`  
Table: `data/catalogs/poe2.fragments.json`

This is the current reviewed snapshot. The deep pass shortened every long
`current-only` scope, and a new `matrix-proof` pass verifies that the repaired
families have no shorter candidate in the current grammar.

## Model

- Every emitted fragment is a **single quoted block**.
- Each quoted block is a **line-local term**.
- Unquoted whitespace would split the query into ANDed terms; the compiler
  never emits a bare fragment.
- `.` does not cross lines.
- `Strict` = zero same-line foreign lines.
- `Minion` = only minion / allies / presence foreign lines.
- Simple mode is global-only.
- Advanced mode uses the slot class; class anchors are separate quoted blocks.
- Hierarchy: shortest Strict → shortest Minion-only → bound-complete current.
- Purely reserved regex fragments (`.*`, `\d+`, `^$`, `\+`, `|`) are rejected.

## Artifact coverage

```text
194 families · 710 scopes · 0 unresolved
search bounds: 10 (374 scopes) · 14 (323) · 24 (3) · 48 (10)
```

| Scope | Strict | Minion | Current-only |
|---|---:|---:|---:|
| Global | 179 | 25 | 30 |
| Class | 288 | 4 | 57 |
| Per-class | 100 | 11 | 16 |

`current-only` means the chosen form equals the current form and is within its
search bound. **There are 0 bound-limited scopes.**

## Applied table

```text
147 global patterns · 30 classes · 0 unresolved
```

The table was generated from the matrix with `matrix-table`. It re-verifies
every applied choice with the indexed term oracle and refuses to write a table
when a choice fails. All matrix choices passed.

## Representative forms

Quoted length includes the two block quotes; inner length excludes them.

| Family | Scope | Chosen | Quoted | Inner | Profile |
|---|---|---|---:|---:|---|
| Maximum Life | global | `\+.*ife` | 9 | 7 | Strict |
| Maximum Life | Amulet/Ring | `\+.*fe` | 8 | 6 | Strict |
| Maximum Life | Belt/Helmet/Buckler/Boots | `fe$` | 5 | 3 | Strict |
| Maximum Life | Gloves/Shield | `m.Life` | 8 | 6 | Strict |
| Attack Speed | global | `k Speed$` | 10 | 8 | Minion |
| Movement Speed | global | `Mov.*eed$` | 11 | 9 | Minion |
| Movement Speed | Boots | `Mov` | 5 | 3 | Strict |
| Cast Speed | global | `ed Cast Speed$` | 16 | 14 | Minion |
| Cast Speed | Amulet | `peed$` | 7 | 5 | Strict |
| Cast Speed | Focus/Staff/Wand | `eed$` | 6 | 4 | Strict |
| Cast Speed | Ring | `Cast S` | 8 | 6 | Strict |
| Critical Damage Bonus | global | `^\d.*al Damage.*us$` | 21 | 19 | Strict |
| Critical Damage Bonus | Amulet | `nus$` | 6 | 4 | Strict |
| Critical Damage Bonus | Gloves | `Bon` | 5 | 3 | Strict |
| Damage Gained Fire | global | `^Ga.*of Da.*e Damage$` | 23 | 21 | Strict |
| Damage Gained Cold | global | `^Ga.*of Da.*d Damage$` | 23 | 21 | Strict |
| Damage Gained Fire | Staff/Wand | `a F` / `a.f` | 5 | 3 | Strict |
| Damage Gained Cold | Staff/Wand | `a C` / `ra C` | 5 / 6 | 3 / 4 | Strict |
| Damage Recouped as Life | global | `of Da.*n Recouped as Life$` | 28 | 26 | Strict |
| Life per Enemy Hit | global | `per En.*ks$` | 13 | 11 | Strict |
| Increased Armour | global | `^\d.*d Armour$` | 16 | 14 | Strict |
| Armour, Evasion and ES | global | `d Armour,` | 11 | 9 | Strict |
| Increased Energy Shield | global/class | `ed Energy.*ld$` | 16 | 14 | Strict |
| Stun Threshold | Shield | `hr` | 4 | 2 | Strict |
| Stun Threshold | Boots/global | `o Stu` | 7 | 5 | Strict |
| Maximum Mana | Focus | `m M` | 5 | 3 | Strict |
| Additional Arrow | global | `Su.*w$` | 8 | 6 | Strict |
| Loads additional bolts | global | `L.bolts` | 9 | 7 | Strict |
| Loads an additional bolt | global | `s an` | 6 | 4 | Strict |
| Allies in Presence damage | global | `in y` | 6 | 4 | Minion |
| Critical Hit Chance | global | `in.*Hit.*e$` | 13 | 11 | Minion |
| Fire Resistance | class | `to F` | 6 | 4 | Strict |
| Lightning Resistance | class | `to L` | 6 | 4 | Strict |
| Cold Resistance | class | `to..o` | 7 | 5 | Strict |
| Chaos Resistance | global | `to Chaos.*ce$` | 15 | 13 | Strict |
| Strength | class | `gth` | 5 | 3 | Strict |
| Dexterity | class | `Dext` | 6 | 4 | Strict |
| Intelligence | class | `to I` / `Inte` | 6 | 4 | Strict |
| Local added Fire | global | `Adds.*F.*e$` | 13 | 11 | Strict |
| Local added Cold | global | `^Ad.*Co.*e$` | 13 | 11 | Strict |
| Local added Lightning | global | `^Ad.*Li.*e$` | 13 | 11 | Strict |
| Local added Physical | global | `Adds.*P.*e$` | 13 | 11 | Strict |
| Local Evasion | global | `^\+.*asion` | 12 | 10 | Strict |

Class anchors remain a separate table:

```text
": One Hand M"   12 inner / 14 quoted
": Two Hand M"   12 inner / 14 quoted
```

They are the shortest literal prefixes that isolate the class. A regex anchor
such as `": .ne M"` / `": .wo M"` could be shorter, but the compiler currently
escapes anchors as literals; that requires an anchor schema/compiler change.

## Proof status

`matrix-proof` re-runs the grammar at `chosen.Length - 1` and reports any
shorter Strict/Minion-only candidate. The full sharded proof checked every
expanded scope:

```text
1129/1129 scopes checked
542 shorter candidates in 143 families
shortestProven: false
```

The 49 scopes in the repaired deep families are clean, but the full catalog is
not yet proof-shortest. The 542 proof candidates are the next data pass; they
include many 2–4 character forms and need the same in-game spot-check gate
before shipping. The proof is grammar-relative, not a claim about arbitrary
regexes outside the grammar.

## In-game spot checks

Use the compiled quoted query, not the bare inner fragment. For class checks
use Advanced mode so the class anchor is present.

### New deep forms — check these first

1. **Critical Damage Bonus, global** — `"^\d.*al Damage.*us$"`.
   Test plain, reduced, attack, spell, trap, and local `+#% to Critical
   Damage Bonus` lines.
2. **Damage Gained Fire/Cold** — `"^Ga.*of Da.*e Damage$"` /
   `"^Ga.*of Da.*d Damage$"`; test both elements plus `Attacks Gain`,
   `Physical Damage`, `with Spells`, and `Runic Ward`.
3. **Damage Recouped as Life** — `"of Da.*n Recouped as Life$"`; compare
   Recouped Mana.
4. **Life per Enemy Hit** — `"per En.*ks$"`; compare Mana per Enemy Hit.
5. **Increased Armour** — `"^\d.*d Armour$"`; test `Break ... increased
   Armour`, hybrid armour, and `Armour Break Duration`.
6. **Hybrid Armour/Evasion/ES** — `"d Armour,"`; test all hybrid
   combinations.
7. **Energy Shield** — `"ed Energy.*ld$"`; test recharge, hybrid, and
   maximum Energy Shield.
8. **Very short class forms** — `"hr"` (Shield Stun Threshold), `"a C"` /
   `"ra C"` (Staff/Wand Cold), `"a F"` / `"a.f"` (Fire), `"m M"` (Focus
   Mana), `"o Ar"` (Armour), `"o Stu"` (Stun Threshold). These are the
   highest-risk forms despite passing the catalog oracle.

### Existing high-risk forms

9. **Maximum Life** — `"\+.*ife"`, `"fe$"`, `"m.Life"`.
10. **Movement Speed** — `"Mov.*eed$"`, `"Mov"`.
11. **Attack Speed** — `"k Speed$"`, `"eed$"`.
12. **Cast Speed** — `"ed Cast Speed$"`, `"peed$"`, `"eed$"`, `"Cast S"`.
13. **Critical Hit Chance** — `"in.*Hit.*e$"`, `"hance$"`.
14. **Additional Arrow / Bolts** — `"Su.*w$"`, `"L.bolts"`, `"s an"`.
15. **Allies in Presence** — `"in y"`, `"yo"`.
16. **Local added damage** — `"Adds.*F.*e$"`, `"^Ad.*Co.*e$"`,
    `"^Ad.*Li.*e$"`, `"Adds.*P.*e$"`.
17. **Evasion / ES local** — `"^\+.*gy"`, `"^\+.*asion"`.
18. **Resistances/attributes** — `"to F"`, `"to L"`, `"to..o"`, `"to Ch"`,
    `"gth"`, `"Dext"`, `"to I"`, `"nus$"`, `"Bon"`.
19. **Mace anchors** — `": One Hand M"` / `": Two Hand M"`; verify class
    isolation against One/Two Hand Axe/Sword/Mace.

### Structure

20. **Quoted vs unquoted control** — the compiled quoted query must not match
    an item whose lines would false-positive after unquoted splitting.
21. **Simple vs Advanced** — Simple uses the global form; Advanced adds the
    class anchor.
22. **Multi-slot** — shared class anchor plus one quoted group per slot.
23. **Threshold** — compile a minimum roll and test below/above values.

## Resource note

Deep searches were run as bounded family batches, one process at a time with
`--jobs 4–8`. No 24-process run was used.

```bash
# deep current-relative search for a family or batch
candidate-matrix --game poe2 --family "FamilyA,FamilyB" \
  --max-length 48 --deep-phrases --jobs 4 --out partial.html

# targeted family replacement (first-wins is intentional here)
candidate-matrix --game poe2 --merge "partial.html|current.html" \
  --max-length 14 --out matrix.html

# proof the chosen forms have no shorter candidate in the grammar
matrix-proof --game poe2 --matrix matrix.html --jobs 6 \
  --family "FamilyA,FamilyB" --out proof.json --check

# table from the reviewed matrix
matrix-table --game poe2 --matrix matrix.html --out data/catalogs/poe2.fragments.json
```

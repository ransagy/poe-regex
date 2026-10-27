# PoE2 candidate report — deep current-only pass

Artifact: `docs/CANDIDATE-MATRIX-poe2.html`  
Status: `docs/CANDIDATE-STATUS-poe2.json`  
Table: `data/catalogs/poe2.fragments.json`

This is the current reviewed snapshot. It is stronger than the earlier 10/14
bound pass: the long `current-only` scopes have been searched with the deeper
phrase grammar, and all applied choices pass the indexed term oracle.

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
194 families · 709 scopes · 0 unresolved
search bounds: 10 (374 scopes) · 14 (323 scopes) · 48 (12 scopes)
```

| Scope | Strict | Minion | Current-only |
|---|---:|---:|---:|
| Global | 186 | 26 | 22 |
| Class | 286 | 4 | 57 |
| Per-class | 100 | 11 | 17 |

`current-only` now means the shipped form is the shortest form found within
the searched grammar and equals the current form. **There are 0 bound-limited
scopes**: every current-only length is at or below its search bound.

The 48-bound scopes are the deep repairs for Critical Damage Bonus, Damage
Gained as Fire/Cold, and related class scopes. They are not long current
forms; the chosen forms are 21–26 characters.

## Applied table

```text
147 global patterns · 30 classes · 0 unresolved
8 global and 7 class entries shortened in the deep repair
0 entries longer than before
```

The table was generated from the matrix with `matrix-table`. It re-verifies
every applied choice with the indexed term oracle and now refuses to write a
table when a choice fails. All matrix choices passed.

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
| Critical Damage Bonus | global | `sed Critical Damage Bonus$` | 28 | 26 | Minion |
| Critical Damage Bonus | Amulet | `nus$` | 6 | 4 | Strict |
| Critical Damage Bonus | Gloves | `Bon` | 5 | 3 | Strict |
| Damage Gained as Fire | global | `^Ga.*of Da.*e Damage$` | 23 | 21 | Strict |
| Damage Gained as Cold | global | `^Ga.*of Da.*d Damage$` | 23 | 21 | Strict |
| Damage Recouped as Life | global | `of Da.*n Recouped as Life$` | 28 | 26 | Strict |
| Life per Enemy Hit | global | `per En.*ks$` | 13 | 11 | Strict |
| Increased Armour | global | `^\d.*d Armour$` | 16 | 14 | Strict |
| Armour, Evasion and ES | global | `d Armour,` | 11 | 9 | Strict |
| Increased Energy Shield | global/class | `ed Energy.*ld$` | 16 | 14 | Strict |
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

Class anchors remain a separate table. The mace anchors are still:

```text
": One Hand M"   12 inner / 14 quoted
": Two Hand M"   12 inner / 14 quoted
```

They are the shortest **literal prefixes** that isolate the class; a regex
anchor such as `": .ne M"` / `": .wo M"` could be shorter, but the compiler
currently escapes anchors as literals. That requires an anchor schema/compiler
change and is not part of this data pass.

## What is not proven

- The deep pass is a targeted grammar extension, not the final exhaustive
  `TermGrammar` proof. `matrix-status` records `shortestProven: false`.
- The 96 `current-only` scopes are bound-complete within the current grammar;
  they are not a claim that no exotic regex outside that grammar is shorter.
- In-game validation is still pending.

## In-game spot checks

Use the compiled quoted query, not the bare inner fragment. For class checks
use Advanced mode so the class anchor is present.

### New deep forms — check these first

1. **Critical Damage Bonus, global** — `"sed Critical Damage Bonus$"`.
   Test plain `% increased Critical Damage Bonus`, minion/allies versions,
   `reduced Critical Damage Bonus`, `for Attack Damage`, `Critical Spell
   Damage Bonus`, and local `+#% to Critical Damage Bonus`.
2. **Damage Gained as Fire** — `"^Ga.*of Da.*e Damage$"`.
   Test Fire, Cold, Lightning, `Attacks Gain...`, `Physical Damage...`,
   `with Spells`, and `while missing Runic Ward`.
3. **Damage Gained as Cold** — `"^Ga.*of Da.*d Damage$"`.
   Same negative checks; verify it does not match Fire.
4. **Damage Recouped as Life** — `"of Da.*n Recouped as Life$"`.
   Test Recouped Life, Recouped Mana, and other Damage-taken lines.
5. **Life per Enemy Hit** — `"per En.*ks$"`.
   Test Life/Mana per Enemy Hit and `per Enemy Hit with Attacks`.
6. **Increased Armour** — `"^\d.*d Armour$"`.
   Test plain Armour, `Break ... increased Armour`, `Armour and Evasion`,
   `Armour, Evasion and Energy Shield`, and `Armour Break Duration`.
7. **Hybrid Armour/Evasion/ES** — `"d Armour,"`.
   Test all hybrid armour combinations and comma-containing lines.
8. **Energy Shield** — `"ed Energy.*ld$"`.
   Test Energy Shield, Energy Shield Recharge Rate, hybrid Armour/Evasion/ES,
   and `maximum Energy Shield` lines.

### Existing high-risk forms

9. **Maximum Life** — `"\+.*ife"`, `"fe$"`, `"m.Life"`; test flat life vs
   recouped/max-life lines.
10. **Movement Speed** — `"Mov.*eed$"`, `"Mov"`; player vs minion and
    non-Boots items.
11. **Attack Speed** — `"k Speed$"`, `"eed$"`; player vs minion.
12. **Cast Speed** — `"ed Cast Speed$"`, `"peed$"`, `"eed$"`, `"Cast S"`;
    test `Attack and Cast Speed`, flask-effect cast speed, and allies/minions.
13. **Critical Hit Chance** — `"in.*Hit.*e$"`, `"hance$"`; player vs minion.
14. **Additional Arrow / Bolts** — `"Su.*w$"`, `"L.bolts"`, `"s an"`.
15. **Allies in Presence** — `"in y"`, `"yo"`.
16. **Local added damage** — `"Adds.*F.*e$"`, `"^Ad.*Co.*e$"`,
    `"^Ad.*Li.*e$"`, `"Adds.*P.*e$"`.
17. **Evasion / ES local** — `"^\+.*gy"`, `"^\+.*asion"`.
18. **Resistances/attributes** — `"to F"`, `"to L"`, `"to..o"`, `"to Ch"`,
    `"gth"`, `"Dext"`, `"to I"`, `"nus$"`, `"Bon"`.
19. **Mace anchors** — `": One Hand M"` / `": Two Hand M"`; verify exact class
    isolation against One/Two Hand Axe/Sword/Mace.

### Structure

20. **Quoted vs unquoted control** — the compiled quoted query must not match
    an item whose lines would false-positive after unquoted splitting.
21. **Simple vs Advanced** — verify Simple uses the global form and Advanced
    adds the class anchor.
22. **Multi-slot** — shared class anchor plus one quoted group per slot.
23. **Threshold** — compile a minimum roll and test below/above values.

## Resource note

The deep repair was run as bounded family batches, one process at a time with
`--jobs 4–8`. No 24-process run was used.

```bash
# deep current-relative search for a family or batch
candidate-matrix --game poe2 --family "FamilyA,FamilyB" \
  --max-length 48 --deep-phrases --jobs 4 --out partial.html

# combine partials without losing shorter entries
candidate-matrix --game poe2 --merge "partial.html|current.html" \
  --merge-best --max-length 14 --out matrix.html

# table from the reviewed matrix
matrix-table --game poe2 --matrix matrix.html --out data/catalogs/poe2.fragments.json
```

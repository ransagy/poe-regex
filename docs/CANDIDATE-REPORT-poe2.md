# PoE2 candidate report — final review snapshot

Commit: `6c0f139`  
Artifact: `docs/CANDIDATE-MATRIX-poe2.html`  
Table: `data/catalogs/poe2.fragments.json`

This is the reviewed shortest-form snapshot under the corrected in-game model.

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
- Hierarchy: Strict → Minion-only → class-scoped Strict/Minion → long current.
- No arbitrary minion length threshold.
- Purely reserved regex fragments (`.*`, `\d+`, `^$`, `\+`, `|`) are rejected.

## Artifact coverage

```text
194 families · 709 scopes · 0 unresolved
search bounds: 10 (374 scopes) · 14 (335 scopes)
```

| Scope | Strict | Minion | Current-only |
|---|---:|---:|---:|
| Global | 175 | 25 | 34 |
| Class | 301 | 4 | 42 |
| Per-class | 109 | 11 | 8 |

`current-only` means no shorter form was found within the searched bound; it
is not unresolved.

A 24-bound pass over the 45 current-only families was attempted. It exceeded
60 minutes in one process and was not merged. The 10/14-bound artifact and
table are the current final resource-feasible result.

## Applied table

```text
147 global patterns · 30 classes · 0 unresolved
128 global patterns shortened · 19 unchanged · 0 longer
```

The table was generated from the matrix with `matrix-table`, which re-verifies
every applied choice with the indexed term oracle and falls back to the old
entry if a choice is not clean. All matrix choices passed.

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
| Additional Arrow | global | `Su.*w$` | 8 | 6 | Strict |
| Loads additional bolts | global | `L.bolts` | 9 | 7 | Strict |
| Loads an additional bolt | global | `s an` | 6 | 4 | Strict |
| Allies in Presence damage | global | `in y` | 6 | 4 | Minion |
| Critical Hit Chance | global | `in.*Hit.*e$` | 13 | 11 | Minion |
| Critical Damage Bonus | Amulet | `nus$` | 6 | 4 | Strict |
| Critical Damage Bonus | Gloves | `Bon` | 5 | 3 | Strict |
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
| Local Energy Shield | global | `^\+.*gy` | 9 | 7 | Strict |
| Local Evasion | global | `^\+.*asion` | 12 | 10 | Strict |

## Remaining current-only areas

The 84 current-only scopes are mostly long same-core/armour/critical forms
and short forms that are already optimal in class scope. They are visible in
the artifact with `coverage: current-only` and `save 0`. They are the next
target if a 24-bound search is made practical.

## In-game spot checks

Use the **compiled quoted query**, not the bare inner fragment. For
class-scoped checks use Advanced mode so the class anchor is present.

### Highest risk — 2–4 character class guards

1. **Strength, class scope** — `"gth"` on an item with Strength; should not
   match Dexterity/Intelligence. Also test a non-attribute line containing
   "gth" if one exists.
2. **Life, Belt/Helmet/Buckler/Boots** — `"fe$"` on flat life; verify it does
   not match `% of Damage taken Recouped as Life` alone.
3. **Critical Damage Bonus, Amulet** — `"nus$"`; verify it matches only
   "Critical Damage Bonus", not other "Bonus" lines.
4. **Critical Damage Bonus, Gloves** — `"Bon"`; same negative check.
5. **Fire Resistance, class** — `"to F"` matches only Fire Resistance, not
   Cold/Lightning/Chaos.
6. **Lightning Resistance, class** — `"to L"` only Lightning.
7. **Cold Resistance, class** — `"to..o"` only Cold.
8. **Chaos Resistance, class** — `"to Ch"` only Chaos.
9. **Dexterity / Intelligence** — `"Dext"`, `"to I"`/`"Inte"`; verify
   attribute isolation.
10. **Evasion / Energy Shield class forms** — `"inc.*g$"`, `"incr.*g$"`,
    `"Eva.*g$"`, `"increased Energy Shield$"`; test hybrid Armour+Evasion+ES
    items.

### High risk — global short forms

11. **Maximum Life** — `"\+.*ife"` on the good flat-life amulet; must not
    match the Str + `% increased maximum Life` amulet without flat life.
12. **Movement Speed** — `"Mov.*eed$"`; test a player-movement item and a
    minion-movement line. Minion-only is allowed; verify the item without
    movement does not match.
13. **Attack Speed** — `"k Speed$"`; test player and minion attack speed.
14. **Critical Hit Chance** — `"in.*Hit.*e$"`; test player crit and minion
    crit lines.
15. **Additional Arrow** — `"Su.*w$"`; test a bow with an additional arrow
    and a bow with an unrelated "Surpassing" line.
16. **Loads additional bolts** — `"L.bolts"`; test "Loads 2 additional
    bolts". Test **Loads an additional bolt** — `"s an"`; ensure it does not
    match unrelated "Adds an ..." text.
17. **Allies in Presence** — `"in y"`; test a sceptre with Allies in your
    Presence and a non-allies item containing "in your..." text.
18. **Local added damage** — `"Adds.*F.*e$"`, `"^Ad.*Co.*e$"`,
    `"^Ad.*Li.*e$"`, `"Adds.*P.*e$"`; test added Fire/Cold/Lightning/Physical
    weapons and hybrid added-damage lines.
19. **Local Energy Shield / Evasion** — `"^\+.*gy"`, `"^\+.*asion"`; test
    +ES and +Evasion items, including hybrid armour pieces.
20. **Chaos Resistance** — `"to Chaos.*ce$"`; test chaos resistance and
    "Chaos Damage" without resistance.

### Structure and quoting

21. **Quoted vs unquoted control** — on the Str + `% max life` amulet, the
    compiled quoted query must not match; the same inner fragment unquoted
    may split and false-positive. This confirms the term model.
22. **Simple vs Advanced** — Simple Movement uses `"Mov.*eed$"`; Advanced
    Boots uses `"Mov"` plus the class anchor. Verify the Advanced query does
    not match a non-Boots item and Simple behaves as expected.
23. **Multi-slot** — select two slots/mods; verify the query has the shared
    class anchor plus one quoted group per slot, and that items need both
    groups.
24. **Ranged/advanced items** — test the long current-only forms for
    Critical Damage Bonus, Critical Hit Chance, Damage Gained, and Armour on
    advanced items with `roll(min-max)` parentheses; they must still match.
25. **Threshold** — compile a minimum roll (e.g., life ≥80); test one roll
    below and one above the threshold.

## Resource note

The 24-process matrix run exhausted the machine; each process rebuilt the full
scope indexes. The sustainable process is:

```bash
# one process, bounded workers
candidate-matrix --jobs 12 --max-length 14 --out artifact.html

# or a few chunks
candidate-matrix --chunk 1/4 --jobs 3 --out partial-1.html
...
candidate-matrix --merge 'partial-1.html|...|partial-4.html' --out artifact.html

# table from reviewed matrix
matrix-table --matrix artifact.html --out data/catalogs/poe2.fragments.json
```

The 12-family repair took 7.4 minutes in one process. The 24-bound pass over
45 families exceeded 60 minutes and was not merged.

## Verification

- 111 Core, 136 Web, 24 Data.Build tests pass.
- All matrix choices pass the term oracle.
- Shipped table fragments and matrix fragments pass shape validation.
- CI and Cloudflare Pages are green at `6c0f139`.

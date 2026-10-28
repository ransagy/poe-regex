# PoE2 candidate report — proof-complete snapshot

Artifact: `docs/CANDIDATE-MATRIX-poe2.html`  
Status: `docs/CANDIDATE-STATUS-poe2.json`  
Proof: `docs/CANDIDATE-PROOF-poe2.json`  
Table: `data/catalogs/poe2.fragments.json`

This snapshot applies the policy-eligible proof candidates and re-proves the
result to a fixed point.

## Model and policy

- Every emitted fragment is a **single quoted block**.
- Each quoted block is a **line-local term**.
- Unquoted whitespace would split the query into ANDed terms; the compiler
  never emits a bare fragment.
- `.` does not cross lines.
- `Strict` = zero same-line foreign lines.
- `Minion` = only minion / allies / presence foreign lines.
- Simple mode is global-only.
- Advanced mode uses the slot class; the class anchor is the **first** quoted
  block.
- Shipping policy: global fragments have at least 3 characters; class-scoped
  fragments may have 2 because the class anchor constrains them.
- In-game safety fixtures are part of the oracle; unique, socketable, base,
  and skill text is still not a complete game-text model.

## Artifact coverage

```text
182 families · 769 scopes · 0 unresolved
bounds: 10 (350) · 14 (406) · 24 (3) · 48 (10)
```

| Scope | Strict | Minion | Current-only |
|---|---:|---:|---:|
| Global | 177 | 22 | 22 |
| Class | 405 | 4 | 36 |
| Per-class | 77 | 11 | 15 |

There are **0 bound-limited scopes**. The 73 `current-only` scopes are within
their search bounds.

## Applied table

```text
136 global patterns · 21 classes · 0 unresolved
```

The pruned-scope build removed 11 non-game global patterns and 190 class
fragments, shortened five released global patterns, and shortened three
Buckler scope choices. Relative to the previous table:

```text
5 released global patterns shortened
3 Buckler scope choices shortened
0 released class entries lost
```

## Non-game class pruning

Nine catalog classes with no released bases (`Dagger`, `Claw`, `Flail`,
`One Hand Sword`, `One Hand Axe`, `Two Hand Sword`, `Two Hand Axe`,
`FishingRod`, `TrapTool`) are excluded from the in-game matrix, shipped table,
global union, and proof. Their catalog entries remain for data fidelity. The
pruned artifacts drop 12 fishing/trap families, 11 global patterns, and 190
class fragments; released classes keep their fragments. The global union,
safety lines, and cross-line sections now contain released classes only.

## Class-mapping correction

RePoE includes unique bases in `mods_by_base`, and the class mapper was
treating a unique-only base as evidence that a regular prefix/suffix can roll
on the whole class. Filtering those bases removed 40 false Buckler mappings
across six families (Strength, Armour/Evasion, Armour/Evasion/Stun Threshold,
Base Armour/Evasion, Armour Applies to Elemental Damage, and Reduced Critical
Damage). Reduced Critical Damage Bonus is now Shield/Targe only; max elemental
resistance remains on both Shields and Bucklers because both have the regular
`shield` tag.

## Proof status

```text
892 / 892 expanded scopes checked
0 shorter candidates
shortestProven: true
```

The proof is relative to the current grammar and safety corpus. It is not a
claim about arbitrary regexes outside that grammar, nor about unmodeled
unique/socketable/base text.

## Representative forms

Quoted length includes the two block quotes; inner length excludes them.

| Family | Scope | Chosen | Quoted | Inner |
|---|---|---|---:|---:|
| Maximum Life | global | `\+.*ife` | 9 | 7 |
| Maximum Life | Amulet/Ring | `\+.*fe` | 8 | 6 |
| Maximum Life | Boots | `fe$` | 5 | 3 |
| Maximum Life | Shield | `m Lif` | 7 | 5 |
| Attack Speed | global | `k Speed$` | 10 | 8 |
| Attack Speed | Gloves | `k S` | 5 | 3 |
| Movement Speed | global | `Mov.*eed$` | 11 | 9 |
| Movement Speed | Boots | `Mov` | 5 | 3 |
| Cast Speed | global | `ed Cast.*ed$` | 14 | 12 |
| Cast Speed | Amulet/Ring | `t S` | 5 | 3 |
| Cast Speed | Focus/Staff/Wand | `eed$` | 6 | 4 |
| Critical Damage Bonus | global | `^\d.*al Damage.*us$` | 21 | 19 |
| Critical Damage Bonus | Amulet | `nus$` | 6 | 4 |
| Critical Damage Bonus | Gloves | `Bon` | 5 | 3 |
| Reduced Critical Damage | Shield/Targe | `Bon` | 5 | 3 |
| Damage Gained Fire | global | `^Ga.*of Da.*e Damage$` | 23 | 21 |
| Damage Gained Cold | global | `^Ga.*of Da.*d Damage$` | 23 | 21 |
| Damage Gained Fire | Staff/Wand | `a F` / `a.f` | 5 | 3 |
| Damage Gained Cold | Staff/Wand | `a C` / `ra C` | 5 / 6 | 3 / 4 |
| Damage Recouped as Life | global | `of Da.*n Recouped as Life$` | 28 | 26 |
| Life per Enemy Hit | global | `per En.*ks$` | 13 | 11 |
| Increased Armour | global | `^\d.*d Armour$` | 16 | 14 |
| Increased Armour | Shield/Boots | `i.*ur$` | 8 | 6 |
| Armour, Evasion and ES | global | `d Armour,` | 11 | 9 |
| Increased Energy Shield | global/class | `ed Energy.*ld$` | 16 | 14 |
| Physical Damage % | global | `sed Ph` | 8 | 6 |
| Max Elemental Resistance | global | `um El` | 7 | 5 |
| Max Elemental Resistance | Shield | `l M` | 5 | 3 |
| Dexterity | class | `xt` | 4 | 2 |
| Stun Threshold | Shield | `hr` | 4 | 2 |
| Stun Threshold | global | `o Stu` | 7 | 5 |
| Armour | class | `o Ar` | 6 | 4 |
| Maximum Mana | Focus | `m M` | 5 | 3 |
| All Elemental Resistance | class | `l E` | 5 | 3 |
| Additional Arrow | global | `Su.*w$` | 8 | 6 |
| Loads additional bolts | global | `L.bolts` | 9 | 7 |
| Loads an additional bolt | global | `s an` | 6 | 4 |
| Allies in Presence damage | global | `in y` | 6 | 4 |
| Critical Hit Chance | global | `in.*Hit.*e$` | 13 | 11 |
| Fire Resistance | class | `to F` | 6 | 4 |
| Lightning Resistance | class | `to L` | 6 | 4 |
| Cold Resistance | class | `to..o` | 7 | 5 |
| Chaos Resistance | global | `to Chaos.*ce$` | 15 | 13 |
| Strength | class | `gth` | 5 | 3 |
| Dexterity | global | `o De` | 6 | 4 |
| Intelligence | class | `to I` / `Inte` | 6 | 4 |
| Local added Fire | global | `Adds.*F.*e$` | 13 | 11 |
| Local added Cold | global | `^Ad.*Co.*e$` | 13 | 11 |
| Local added Lightning | global | `^Ad.*Li.*e$` | 13 | 11 |
| Local added Physical | global | `Adds.*P.*e$` | 13 | 11 |
| Local Evasion | global | `^\+.*asion` | 12 | 10 |
| Evasion Rating | Buckler | `o E` | 5 | 3 |
| Increased Evasion Rating | Buckler | `d Ev` | 6 | 4 |
| Accuracy | global | `o Ac` | 6 | 4 |
| Melee Skills | global | `l Me` | 6 | 4 |
| Stun Buildup | global | `Ca.+p$` | 7 | 5 |
| Allies added Cold | global | `k Co` | 6 | 4 |
| Allies added Physical | global | `k Ph` | 6 | 4 |

Class anchors remain a separate table. The mace anchors are still literal
prefixes and are correct for One/Two Hand Mace isolation. Shorter anchor forms
such as `": .ne M"` / `": .wo M"` require the anchor regex/plain-flag pipeline
and remain a later optimization.

## Class anchors

These are the actual anchors emitted in Advanced mode. The anchor is always
the first quoted block; colloquial class names such as "Staff" or "Focus" are
not the class-line text. Non-game classes are pruned from the in-game
matrix, table, and proof; their catalog entries remain for data fidelity.

| Class | Anchor |
|---|---|
| Amulet | `": Am"` |
| Belt | `": Bel"` |
| Body Armour | `": Bod"` |
| Boots | `": Boo"` |
| Bow | `": Bow"` |
| Buckler | `": Bu"` |
| Crossbow | `": Cro"` |
| Focus | `": Fo"` |
| Gloves | `": Gl"` |
| Helmet | `": Hel"` |
| One Hand Mace | `": One Hand M"` |
| Quiver | `": Qui"` |
| Ring | `": Ri"` |
| Sceptre | `": Sce"` |
| Shield | `": Shields"` |
| Spear | `": Spea"` |
| Staff | `": Stav"` |
| Talisman | `": Tal"` |
| Two Hand Mace | `": Two Hand M"` |
| Wand | `": Wan"` |
| Warstaff | `": Qua"` |

For query examples use the anchor column verbatim:

```text
": Stav" "a C"
": Fo" "m M"
```

## Class-scoped two-character forms

`xt`, `hr`, and the other two-character class forms are valid only with their
class anchor first:

```text
": Am" "xt"
": Shields" "hr"
```

The global forms fail because they can hit extra-damage lines, `Shrine
Sceptre`, `Grants Skill: Spear Throw`, and other unmodeled text. The class
anchor is not optional for these forms.

## Security and data coverage

The raw data includes unique-only, jewel/socketable, base, and skill lines
that the selectable catalog and vendor corpus deliberately exclude. Observed
field lines are now in the safety fixtures (`Idol of the Sycophant`,
`Shrine Sceptre`, `Grants Skill: Spear Throw`). A complete global safety
corpus remains the next coverage improvement; the current proof is complete
for the modeled corpus plus captured fixtures.

### Accepted soft collisions

The global table deliberately accepts two foreign-line classes outside the
selectable item scope:

- Minion/allies/presence variants of the same displayed stat, under the
  existing minion policy.
- The jewel-only `... from Equipped Shield` suffix for `d Armour,`. A jewel
  prefix can render `#% increased Armour, Evasion and Energy Shield from
  Equipped Shield`, and the shortest global form for the armour hybrid
  prefix matches it. The class-scoped form (`Ar.*A.+nd`) is unaffected.

If strict jewel exclusion is ever required, the known end-anchored fallback is
`d Armour, Evasion and Energy Shield$` (36 characters). It avoids both the
jewel suffix and the `#% increased Global Armour, Evasion and Energy Shield`
lines. No shorter jewel-safe anchored form is retained in the current grammar.

## Recommended in-game checks

1. `": Am" "xt"` on an amulet vs weapons with extra cold/fire/lightning
   damage. The class anchor must exclude the weapons.
2. `": Shields" "hr"` on a shield vs `Shrine Sceptre`, `Grants Skill: Spear
   Throw`, and ailment-threshold items.
3. `"^\d.*al Damage.*us$"` on plain, reduced, attack, spell, trap, and local
   critical damage lines.
4. `"^Ga.*of Da.*e Damage$"` / cold on Fire/Cold/Lightning/Physical,
   `Attacks Gain`, `with Spells`, and Runic Ward.
5. `"^\d.*d Armour$"` / `"d Armour,"` on plain, hybrid, Break, and
   Armour Break Duration lines. The jewel-only `from equipped Shield` variant
   is an accepted soft collision; see “Accepted soft collisions” above.
6. `"ed Energy.*ld$"` on ES, recharge, and hybrid armour pieces.
7. `"sed Ph"` on local physical damage vs elemental and global physical lines.
8. `"um El"` on all-maximum elemental resistance vs individual max and normal
   all-resistance lines.
9. `": One Hand M"` / `": Two Hand M"` against the opposite mace class and
   other in-game weapon class lines. Axes and swords have no released bases and
   are no longer part of the in-game scope.

## Import search

The current importer still expects catalog-generated fragments. Existing
build-guide regexes use unquoted terms, alternations, affix names, thresholds,
and broad stat words, so importer semantics remain a separate follow-up.

## Resource note

The full proof was run as four shards with `--jobs 4`, then merged:

```text
4 shards · 892 expanded scopes · ~2.5 minutes
```

The full applier is `matrix-apply`; `matrix-proof` supports `--shard` and
`--merge`. No 24-process run was used.

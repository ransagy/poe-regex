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

## Target display

The in-game Highlight search always matches the **advanced item text**.
Ranged stats show `roll(min-max)` (for example,
`24(19-29)% increased Armour, Evasion and Energy Shield`); fixed stats show
the plain value. Normal display is presentation only and is not a separate
target model.

The table records this basis as `targetModel: advanced-ranges-v1`. The engine
rejects a table whose target model does not match, so a table generated under
a different display basis cannot be used silently. Fragments may therefore
use the range hyphen or parentheses as guards, and the web importer already
works from advanced descriptions.

## Artifact coverage

```text
182 families · 600 scopes · 0 unresolved
bounds: 250 (600)
```

| Scope | Strict | Minion | Current-only |
|---|---:|---:|---:|
| Global | 0 | 0 | 221 |
| Class | 0 | 0 | 253 |
| Per-class | 0 | 0 | 126 |

There are **0 bound-limited scopes**. All 600 rows are the exact current
table; the seeded matrix keeps an already-proven equal-length form, so every
row is marked `current-only`. The proof independently checks every expanded
scope for a shorter candidate.

## Applied table

```text
136 global patterns · 21 classes · 0 unresolved
```

The exact strict pass replaced the heuristic candidate grammar. It shortened
51 global and 82 class fragments and lengthened 15 global and 13 class
fragments where the old form collided with a same-stat source or cross-stat
line. Net inner-character savings: **206**.

Examples:

| Pattern | Scope | Exact form |
|---|---|---|
| `#% increased maximum Life` | global | `^\d.+um.+e$` |
| `#% increased Critical Damage Bonus` | global | `^\d.*al d.+u.$` |
| `Gain #% of Damage as Extra Fire Damage` | global | `^g.*-.*ir.+e$` |
| `Gain #% of Damage as Extra Cold Damage` | global | `^g.*f d.*co.+e$` |
| `#% of Damage taken Recouped as Life` | global | `f d.*n r.*e$` |
| `#% increased Armour, Evasion and Energy Shield` | global | `-.*d a.+,` |
| `#% increased Evasion Rating` | Amulet | `-.*va` |
| `+#% to Critical Damage Bonus` | Bow | `nus$` |

The strict pass is exact for the declared grammar and target display model
(advanced item text, including ranged `roll(min-max)` forms).

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

## From-scratch reproducibility

The post-patch independence run used the exact solver with no table seed:

```bash
dotnet ... matrix-solve --from-scratch --jobs 4
```

```text
runtime: 95s
matrix:  182 families · 607 rows · 892 expanded scopes
table:   136 global · 21 classes · 525 class fragments
proof:   892 / 892 · 0 failures
```

Compared with the shipped artifacts:

- Expanded scopes: 892 vs 892, identical keys.
- Chosen length: identical on all 892 scopes.
- Table: 136/21/525 in both.
- 376 table entries differ only at equal length: 201 case-only and 175
  same-length shape ties.
- The seeded matrix has 600 rows; the from-scratch matrix has 607. The
  difference is row grouping; both expand to the same 892 scopes.

The differences are tie-breaks, not coverage or length regressions. The
seeded run keeps an already-proven equal-length form; from-scratch returns
the solver's preferred equal-length form. The shortest-length guarantee is
seed-independent. The exact string is not canonical across equal-length
solutions by design; the table remains a validated cache and
`--from-scratch` remains the authority when the cache is stale.

## Solver validation

`ExactSolverDifferentialTests` compares the exact product-state solver with an
exhaustive token search on synthetic target/foreign corpora. The cases cover
literals, wildcards, anchors, `\d`, `\d+`, and a no-form input. All cases agree
on length and strictness.

## Representative exact forms

Quoted length includes the two block quotes; inner length excludes them.

| Pattern | Scope | Form | Inner |
|---|---|---|---:|
| `#% increased maximum Life` | global | `^\d.+um.+e$` | 9 |
| `#% increased maximum Life` | Amulet/Ring | `cr.*fe` | 6 |
| `#% increased Movement Speed` | global | `^\d.+ov.+e.$` | 10 |
| `#% increased Critical Damage Bonus` | global | `^\d.*al d.+u.$` | 14 |
| `#% increased Critical Damage Bonus` | Gloves | `nus$` | 4 |
| `Gain #% of Damage as Extra Fire Damage` | global | `^g.*-.*ir.+e$` | 16 |
| `Gain #% of Damage as Extra Cold Damage` | global | `^g.*f d.*co.+e$` | 16 |
| `#% of Damage taken Recouped as Life` | global | `f d.*n r.*e$` | 12 |
| `#% increased Armour, Evasion and Energy Shield` | global | `-.*d a.+,` | 9 |
| `#% increased Armour` | global | `^\d.*d Armour$` | 14 |
| `#% increased Energy Shield` | global | `ed en.+d$` | 9 |
| `+# to maximum Energy Shield` | global | `^\+.*gy` | 6 |
| `#% increased Evasion Rating` | Amulet | `-.*va` | 5 |
| `#% increased Armour` | Amulet | `-.*rm` | 6 |
| `+#% to Critical Damage Bonus` | Bow | `nus$` | 4 |

All forms are strict: zero foreign lines in their scope. Some forms use the
range hyphen from advanced item display; the target model includes those
display shapes.

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
`Shrine Sceptre`, `Grants Skill: Spear Throw`, plus the cross-stat
`from Equipped Body Armour` and `from Equipped Quiver` lines). A complete
global safety corpus remains the next coverage improvement; the current proof
is complete for the modeled corpus plus captured fixtures.

### Strict target membership

The table no longer allows soft collisions. A line is a target only when its
normalized pattern is the target pattern for the scope. Minion/allies lines,
jewel `from Equipped Shield` lines, and desecrated `from Equipped` lines are
foreign unless the target pattern itself includes that subject/source.

This is why the previous accepted forms are gone:

- `d Armour,` is replaced by `-.*d a.+,` (the latter excludes the fixed-value
  jewel line without matching it).
- Amulet Evasion is `-.*va`, not `d Ev`.
- Amulet Armour is `-.*rm`, not `d Ar`.

Cross-stat and same-stat source lines are both in the foreign corpus. No
policy exception can reintroduce them without changing the basis.

## Optional changed-form checks

The data and exact grammar proof are authoritative. No random in-game item
sampling is required for the guarantee. A future grammar profile that adds a
new regex construct needs a one-time conformance test for that construct.

## Import search

The current importer still expects catalog-generated fragments. Existing
build-guide regexes use unquoted terms, alternations, affix names, thresholds,
and broad stat words, so importer semantics remain a separate follow-up.

## Resource note

The current pipeline is one command with `--jobs 4`:

```text
matrix-solve (seeded):        ~66s
matrix-solve (from scratch):  ~95s
exact proof:                  ~34s
```

`matrix-solve` is the one pipeline; `matrix-proof` re-checks the committed
matrix without writing the table.

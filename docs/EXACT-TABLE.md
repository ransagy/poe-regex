# PoE2 exact table — proof and coverage

Artifact: `docs/CANDIDATE-MATRIX-poe2.html`  
Status: `docs/CANDIDATE-STATUS-poe2.json`  
Proof: `docs/CANDIDATE-PROOF-poe2.json`  
Table: `data/catalogs/poe2.fragments.json`

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
works from advanced descriptions. The oracle corpus includes both the plain
copy-text line and its advanced `roll(min-max)` form, so a fragment must be
safe for both.

## Artifact coverage

```text
456 families · 519 patterns · 1360 expanded scopes · 0 unresolved
bound-limited: 0
```

| Scope | Rows |
|---|---:|
| Global | 519 |
| Class | 518 |
| Per-class | 323 |

Each expanded scope is independently checked for a shorter candidate.

## Applied table

```text
381 global patterns · 21 classes · 1081 class fragments · 0 unresolved
```

Examples:

| Pattern | Scope | Exact form |
|---|---|---|
| `#% increased maximum Life` | global | `^\d.+d m.*fe$` |
| `#% increased Critical Damage Bonus` | global | `^\d.*al d.+u.$` |
| `Gain #% of Damage as Extra Fire Damage` | global | `^g.*-.*ir.+e$` |
| `Gain #% of Damage as Extra Cold Damage` | global | `^g.*f d.*co.+e$` |
| `#% of Damage taken Recouped as Life` | global | `f d.*n r.*e$` |
| `#% increased Armour, Evasion and Energy Shield` | global | `d a.+v.+y shield$` |
| `#% increased Evasion Rating` | Amulet | ` e.+g$` |
| `Always Hits` | One Hand Mace | `ay` |
| `Crushes Enemies on Hit` | global | `shes` |
| `+#% to Critical Damage Bonus` | Bow | `nus$` |
| `Bow Attacks fire an additional Arrow` | Bow | `^bo` |

The strict pass is exact for the declared grammar and target display model
(advanced item text, including ranged `roll(min-max)` forms).

## Non-game class pruning

Ten catalog classes with no released bases (`Dagger`, `Claw`, `Flail`,
`One Hand Sword`, `One Hand Axe`, `Two Hand Sword`, `Two Hand Axe`,
`FishingRod`, `TrapTool`, `HiddenItem`) are excluded from the in-game matrix,
shipped table, global union, and proof. Their catalog entries remain for data fidelity. The
pruned artifacts exclude those classes from the in-game matrix, table, and
proof; released classes keep their fragments. The global union,
safety lines, and cross-line sections contain released classes only.

## Proof status

```text
1894 / 1894 expanded scopes checked
0 shorter candidates
shortestProven: true
```

The proof is relative to the current grammar and safety corpus. It is not a
claim about arbitrary regexes outside that grammar.

## From-scratch reproducibility

The post-patch independence run used the exact solver with no table seed:

```bash
dotnet ... matrix-solve --from-scratch --jobs 4
```

```text
runtime: local from-scratch run
matrix:  456 families · 519 patterns · 1360 expanded scopes
table:   381 global · 21 classes · 1081 class fragments
proof:   1894 / 1894 · 0 failures
```

This from-scratch run is the basis for the shipped table. The shipped table
also applies a stability pass: an existing fragment is kept when it still
passes the exact oracle and matches the new shortest length, so equal-length
tie churn does not retire working strings. The diff report therefore lists
only forced and new fragments. Use `matrix-solve --prefer <old-fragments>` to
reproduce that stability pass.

- Expanded scopes: 1894 / 1894, all proven.
- Chosen length: proven on every scope.
- Table: 381 global patterns, 21 classes, 1081 class fragments, 0 unresolved.
- Equal-length ties are resolved deterministically by the current run.

The exact string is not canonical across equal-length solutions by design;
the table remains a validated cache and `--from-scratch` remains the
authority when the cache is stale.

## Fragment-change review

Any change that adds catalog lines, changes the vendor/safety corpus, or changes
the solver must regenerate the table from scratch and review a before/after
fragment report before merge. The report covers every changed existing scope
with its old/new fragment, both lengths, the length delta, and the collision
reason, plus every added/removed scope. Keep the report outside the repository;
the shipped table and proof remain the source of truth.

Generate it with [`../tools/fragment-diff/run.py`](../tools/fragment-diff/run.py)
after saving the target branch's table. The Markdown and JSON outputs list the
same signed deltas. See [`RELEASE-PREP.md`](RELEASE-PREP.md) for the full gate
sequence.

## Solver validation

`ExactSolverDifferentialTests` compares the exact product-state solver with an
exhaustive token search on synthetic target/foreign corpora. The cases cover
literals, wildcards, anchors, `\d`, `\d+`, and a no-form input. All cases agree
on length and strictness.

## Representative exact forms

Quoted length includes the two block quotes; inner length excludes them.

| Pattern | Scope | Form | Inner |
|---|---|---|---:|
| `#% increased maximum Life` | global | `^\d.+d m.*fe$` | 13 |
| `#% increased maximum Life` | Amulet | `d.*m l` | 6 |
| `#% increased Movement Speed` | global | `^\d.+ov.+e.$` | 12 |
| `#% increased Critical Damage Bonus` | global | `^\d.*al d.+u.$` | 14 |
| `#% increased Critical Damage Bonus` | Gloves | `bon` | 3 |
| `Gain #% of Damage as Extra Fire Damage` | global | `^g.*-.*ir.+e$` | 13 |
| `Gain #% of Damage as Extra Cold Damage` | global | `^g.*f d.*co.+e$` | 15 |
| `#% of Damage taken Recouped as Life` | global | `f d.*n r.*e$` | 12 |
| `#% increased Armour, Evasion and Energy Shield` | global | `d a.+v.+y shield$` | 18 |
| `#% increased Armour` | global | `^\d.*d armour$` | 14 |
| `#% increased Energy Shield` | global | `ed en.*d$` | 9 |
| `+# to maximum Energy Shield` | global | `^\+.*gy` | 7 |
| `#% increased Evasion Rating` | Amulet | ` e.+g$` | 6 |
| `#% increased Armour` | Amulet | `d..rmour$` | 9 |
| `+#% to Critical Damage Bonus` | Bow | `nus$` | 4 |

All forms are strict: zero foreign lines in their scope. Some forms use the
range hyphen from advanced item display; the target model includes those
display shapes.

## Class anchors

These are the actual anchors emitted in Advanced mode. The anchor is always
the first quoted block; colloquial class names such as "Staff" or "Focus" are
not the class-line text. Each anchor is the shortest legal selector found by
a data-time search over literal substrings and `.` / `.+` wildcards, verified
against every class line and every modeled non-class line. Wildcard anchors
are emitted as regex, not escaped as literals. The table also carries each
class's literal prefix for importing older queries and every verified
multi-class paste term, so the browser never rebuilds the vendor-derived
class corpus to compose a multi-slot query.

Non-game classes are pruned from the in-game matrix, table, and proof; their
catalog entries remain for data fidelity.

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
| One Hand Mace | `": On.+M"` |
| Quiver | `": Qui"` |
| Ring | `": Ri"` |
| Sceptre | `": Sce"` |
| Shield | `": Shi.+s"` |
| Spear | `": Spea"` |
| Staff | `": Stav"` |
| Talisman | `": Tal"` |
| Two Hand Mace | `": Tw.+M"` |
| Wand | `": Wan"` |
| Warstaff | `": Qua"` |

For query examples use the anchor column verbatim:

```text
": Stav" "a C"
": Fo" "m M"
```

Paste class groups use the same data-time search. The shipped table includes
the shortest verified multi-class terms, so composition never runs the
oracle in the browser:

| Classes | Term |
|---|---|
| Bow, Crossbow | `.*Bow` |
| Gloves, Staff, Warstaff | `.*ves` |
| One Hand Mace, Two Hand Mace | `.*Mac` |
| Staff, Warstaff | `.*tav` |

## Class-scoped two-character forms

`xt`, `hr`, and the other two-character class forms are valid only with their
class anchor first:

```text
": Am" "xt"
": Shi.+s" "hr"
```

The global forms fail because they can hit extra-damage lines, `Shrine
Sceptre`, `Grants Skill: Spear Throw`, and other unmodeled text. The class
anchor is not optional for these forms.

## Security and data coverage

The raw data includes unique-only, jewel/socketable, base, and skill lines
that the selectable catalog and vendor corpus deliberately exclude. Observed
field lines are in the safety fixtures (`Idol of the Sycophant`,
`Shrine Sceptre`, `Grants Skill: Spear Throw`, plus the cross-stat
`from Equipped Body Armour` and `from Equipped Quiver` lines). Abyss
desecrated item affixes are now part of the modeled catalog and safety
corpus, with their classes derived from spawn-weight tags. Base implicits are
modeled as implicit-only families at class level, with classes taken from the
released base items' implicit lists. Corrupted enchantments are modeled as a
separate non-affix source, with classes taken from `mods_by_base`; the five
records without a positive base/spawn reference are excluded. The PoE2 export's
`crafted` prefix/suffix records are PoE1 master-craft leftovers; they have no
positive PoE2 base mapping and are excluded. PoE2's actual crafted mods are
the jewel-only `CraftedJewel*` records in domain `misc`, which are not
modeled. Normal and desecrated jewel affixes are not modeled and are outside
the proof. Family
tiers are split by roll range for display, but the table is pattern-based:
tiers that share one stat pattern share a fragment, so the search string
cannot pin one numeric range; the picker collapses them into one row while
range strings remain pattern-based. Range-specific strings need range-aware
proof scopes. The oracle corpus includes both plain and advanced
`roll(min-max)` forms, so shipped fragments are safe for the game's Highlight
target as well as normal copy text. The proof is complete for the modeled
corpus plus captured fixtures. Unmodeled
unique, rare, flavour, socketable, and bonded text can still highlight; this
is accepted and out of scope.

### Strict target membership

The table does not allow soft collisions. A line is a target only when its
normalized pattern is the target pattern for the scope. Minion/allies lines,
jewel `from Equipped Shield` lines, and desecrated `from Equipped` lines are
foreign unless the target pattern itself includes that subject/source.

Examples of strict forms:

- `-.*d a.+,` excludes the fixed-value jewel line without matching it.
- Amulet Evasion is ` e.+g$`, not `d Ev`.
- Amulet Armour is `d..rmour$`, not `d Ar`.

Cross-stat and same-stat source lines are both in the foreign corpus. No
policy exception can reintroduce them without changing the basis.

## Grammar scope

The data and exact grammar proof are authoritative. No random in-game item
sampling is required for the guarantee. A future grammar profile that adds a
new regex construct needs a one-time conformance test for that construct.

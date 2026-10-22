# Candidate grammar report — poe2

Generated from the real catalog + vendor corpus + captured safety items.
This report is diagnostic only; it does not change the shipped fragment table.
Class scopes are the multi-slot scopes. Vendor/base-context filtering is not modeled in this pass; a class scope includes all of that class's vendor lines.

Grammar:
- literal substrings, including partial words; no full-word requirement
- word-start prefixes and word-end suffixes
- one or two middle wildcards (`.`, `.*`, `.+`, fixed-length dot runs)
- optional `^`/`$` anchors; no adjacency allowance
- max candidate length 24

`Strict` = zero foreign lines and no cross-line match. `Line-only` = zero foreign lines but the wildcard may cross a line break. `Shortest observed` = shortest plausible candidates regardless of collisions.
`Plausible` means the candidate starts at a word start or ends at a word end and contains at least three letters; arbitrary letter runs are excluded from the decision tables.

## Common-family strict summary

| family | class | strict candidate | len | line-only candidate | current |
|---|---|---|---:|---|---|
| IncreasedLife | Belt | `ife$` | 4 | `ife$` | `maximum Life$` |
| IncreasedMana | Ring | `to.*M.+ana` | 10 | `to.*a$` | `\) to maximum Mana$` |
| LocalPhysicalDamagePercent | Bow | `D....sical` | 10 | `inc.*e$` | `increased Physical Damage$` |
| LocalFireDamage | Bow | `Fire D` | 6 | `Fi.*e$` | `Fire Damage$` |
| LocalColdDamage | Bow | `Col..d` | 6 | `Ad.*ld` | `Cold Damage$` |
| LocalLightningDamage | Bow | `Adds.*ing` | 9 | `Li.*ge$` | `Lightning Damage$` |
| LocalEnergyShield | Body Armour | `m Energy` | 8 | `max.*y` | `maximum Energy Shield$` |
| LocalPhysicalDamageReductionRating | Body Armour | `to Ar` | 5 | `to Ar` | `\) to Armour$` |
| LocalEvasionRating | Body Armour | `to Ev` | 5 | `to Ev` | `\) to Evasion Rating$` |
| BaseSpirit | Amulet | `rit$` | 4 | `rit$` | `\) to Spirit$` |
| FireResistance | Amulet | `to F` | 4 | `to F` | `Fire Resistance$` |
| ColdResistance | Amulet | `to Co` | 5 | `to Co` | `% to Cold Resistance$` |
| LightningResistance | Amulet | `to Li` | 5 | `to Li` | `Lightning Resistance$` |
| ChaosResistance | Amulet | `to Ch` | 5 | `to Ch` | `Chaos Resistance$` |
| Strength | Amulet | `gth` | 3 | `gth` | `\) to Strength$` |
| Dexterity | Amulet | `Dext` | 4 | `Dext` | `\) to Dexterity$` |
| Intelligence | Amulet | `Inte` | 4 | `Inte` | `\) to Intelligence$` |
| MovementVelocity | Boots | `Mov` | 3 | `Mov` | `Movement Speed$` |
| LocalIncreasedAttackSpeed | Bow | `eed$` | 4 | `eed$` | `Attack Speed$` |
| LocalBaseCriticalStrikeChance | Bow | `H.nce$` | 6 | `C.*ce$` | `Hit Chance$` |
| LocalAccuracyRating | Bow | `acy` | 3 | `acy` | `Accuracy Rating$` |
| ManaRegeneration | Ring | `Mana R` | 6 | `Mana R` | `Mana Regeneration Rate$` |

Strict class-scope average: **5.4** characters across 22 families (max 10).
## IncreasedLife — Belt

Targets: 22; foreign lines: 2319; current: `maximum Life$`

### Strict (zero foreign, no cross-line)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 4 | `ife$` | 0 | 0 | no | no |  |
| 5 | `Life$` | 0 | 0 | no | no |  |
| 6 | `m Life` | 0 | 0 | no | no |  |
| 6 | `m..ife` | 0 | 0 | no | no |  |
| 6 | `m.Life` | 0 | 0 | no | no |  |

### Line-only (zero foreign; cross-line possible)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 4 | `ife$` | 0 | 0 | no | no |  |
| 5 | `L.fe$` | 0 | 0 | yes | no |  |
| 5 | `Li.e$` | 0 | 0 | yes | no |  |
| 5 | `Life$` | 0 | 0 | no | no |  |
| 6 | `L.*fe$` | 0 | 0 | yes | no |  |

### Shortest observed (collisions included)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 3 | `max` | 5 | 24 | no | no | `15(15-25)% increased maximum Energy Shield · 25(15-25)% increased maximum Energy Shield` |
| 3 | `mum` | 5 | 24 | no | no | `15(15-25)% increased maximum Energy Shield · 25(15-25)% increased maximum Energy Shield` |
| 3 | `Lif` | 34 | 352 | no | no | `1(1-2) Life Regeneration per second · 13(9.1-13) Life Regeneration per second` |
| 3 | `ife` | 34 | 352 | no | no | `1(1-2) Life Regeneration per second · 13(9.1-13) Life Regeneration per second` |
| 4 | `ife$` | 0 | 0 | no | no |  |
| 4 | `to m` | 3 | 21 | no | no | `+1 to Maximum Endurance Charges · +10(10-14) to maximum Mana` |
| 4 | `imum` | 5 | 24 | no | no | `15(15-25)% increased maximum Energy Shield · 25(15-25)% increased maximum Energy Shield` |
| 4 | `maxi` | 5 | 24 | no | no | `15(15-25)% increased maximum Energy Shield · 25(15-25)% increased maximum Energy Shield` |

## IncreasedLife — global

Targets: 47; foreign lines: 40981; current: `\) to maximum Life$`

### Strict (zero foreign, no cross-line)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 14 | `o maximum Life` | 0 | 0 | no | no |  |
| 14 | `to maximum Lif` | 0 | 0 | no | no |  |
| 15 | `o maximum Life$` | 0 | 0 | no | no |  |
| 15 | `t..maximum Life` | 0 | 0 | no | no |  |
| 15 | `to maximum Life` | 0 | 0 | no | no |  |

### Line-only (zero foreign; cross-line possible)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 14 | `o maximum Life` | 0 | 0 | no | no |  |
| 14 | `to maximum Lif` | 0 | 0 | no | no |  |
| 14 | `to maximum.*fe` | 0 | 0 | yes | yes |  |
| 14 | `to maximum.+fe` | 0 | 0 | yes | yes |  |
| 15 | `o maximum Life$` | 0 | 0 | no | no |  |

### Shortest observed (collisions included)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 3 | `max` | 49 | 363 | no | no | `+1 to maximum number of Elemental Infusions · +1% to Maximum Chaos Resistance` |
| 3 | `mum` | 49 | 363 | no | no | `+1 to maximum number of Elemental Infusions · +1% to Maximum Chaos Resistance` |
| 3 | `Lif` | 223 | 2263 | no | no | `Item Class: Life Flasks · 1(1-20)% increased maximum Life` |
| 3 | `ife` | 269 | 2298 | no | no | `Item Class: Life Flasks · 1(1-20)% increased maximum Life` |
| 4 | `ife$` | 31 | 145 | no | no | `1(1-20)% increased maximum Life · 10% less damage taken while on Low Life` |
| 4 | `to m` | 24 | 259 | no | no | `+1 to maximum number of Elemental Infusions · +1% to Maximum Chaos Resistance` |
| 4 | `imum` | 49 | 363 | no | no | `+1 to maximum number of Elemental Infusions · +1% to Maximum Chaos Resistance` |
| 4 | `maxi` | 49 | 363 | no | no | `+1 to maximum number of Elemental Infusions · +1% to Maximum Chaos Resistance` |

## IncreasedMana — Ring

Targets: 28; foreign lines: 3404; current: `\) to maximum Mana$`

### Strict (zero foreign, no cross-line)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 10 | `to.*M.+ana` | 0 | 0 | no | no |  |
| 10 | `to.*M.+na$` | 0 | 0 | no | no |  |
| 10 | `to.*Ma.*a$` | 0 | 0 | no | no |  |
| 10 | `to.*Ma.*na` | 0 | 0 | no | no |  |
| 10 | `to.*Ma.+a$` | 0 | 0 | no | no |  |

### Line-only (zero foreign; cross-line possible)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 6 | `to.*a$` | 0 | 0 | yes | no |  |
| 6 | `to.*na` | 0 | 0 | yes | no |  |
| 6 | `to.+a$` | 0 | 0 | yes | no |  |
| 6 | `to.+na` | 0 | 0 | yes | no |  |
| 7 | `to.*ana` | 0 | 0 | yes | no |  |

### Shortest observed (collisions included)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 3 | `max` | 5 | 23 | no | no | `4(4-6)% increased maximum Life · 6(4-6)% increased maximum Life` |
| 3 | `mum` | 5 | 23 | no | no | `4(4-6)% increased maximum Life · 6(4-6)% increased maximum Life` |
| 3 | `ana` | 36 | 400 | no | no | `10(10-19)% increased Mana Regeneration Rate · 12(8-12)% increased Mana Regeneration Rate` |
| 3 | `Man` | 37 | 412 | no | no | `10(10-19)% increased Mana Regeneration Rate · 12(8-12)% increased Mana Regeneration Rate` |
| 4 | `ana$` | 3 | 7 | no | no | `4(4-6)% increased maximum Mana · 6(4-6)% increased maximum Mana` |
| 4 | `to m` | 2 | 18 | no | no | `+10(10-19) to maximum Life · +100(100-119) to maximum Life` |
| 4 | `imum` | 5 | 23 | no | no | `4(4-6)% increased maximum Life · 6(4-6)% increased maximum Life` |
| 4 | `maxi` | 5 | 23 | no | no | `4(4-6)% increased maximum Life · 6(4-6)% increased maximum Life` |

## IncreasedMana — global

Targets: 102; foreign lines: 40926; current: `\) to maximum Mana$`

### Strict (zero foreign, no cross-line)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 12 | `to maximum M` | 0 | 0 | no | no |  |
| 12 | `to maximum.m` | 0 | 0 | no | no |  |
| 13 | `to maximum Ma` | 0 | 0 | no | no |  |
| 14 | `o maximum Mana` | 0 | 0 | no | no |  |
| 14 | `to maximum Man` | 0 | 0 | no | no |  |

### Line-only (zero foreign; cross-line possible)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 12 | `to maximum M` | 0 | 0 | no | no |  |
| 12 | `to maximum.m` | 0 | 0 | no | no |  |
| 13 | `to maximum Ma` | 0 | 0 | no | no |  |
| 14 | `o maximum Mana` | 0 | 0 | no | no |  |
| 14 | `to maximum Man` | 0 | 0 | no | no |  |

### Shortest observed (collisions included)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 3 | `max` | 49 | 308 | no | no | `+1 to maximum number of Elemental Infusions · +1% to Maximum Chaos Resistance` |
| 3 | `mum` | 49 | 308 | no | no | `+1 to maximum number of Elemental Infusions · +1% to Maximum Chaos Resistance` |
| 3 | `ana` | 213 | 2105 | no | no | `Item Class: Mana Flasks · 1(1-20)% increased maximum Mana` |
| 3 | `Man` | 672 | 2722 | no | no | `Item Class: Mana Flasks · Item Class: Talismans` |
| 4 | `ana$` | 8 | 77 | no | no | `1(1-20)% increased maximum Mana · 10(10-11)% more Attack damage while on Low Mana` |
| 4 | `to m` | 24 | 204 | no | no | `+1 to maximum number of Elemental Infusions · +1% to Maximum Chaos Resistance` |
| 4 | `imum` | 49 | 308 | no | no | `+1 to maximum number of Elemental Infusions · +1% to Maximum Chaos Resistance` |
| 4 | `maxi` | 49 | 308 | no | no | `+1 to maximum number of Elemental Infusions · +1% to Maximum Chaos Resistance` |

## LocalPhysicalDamagePercent — Bow

Targets: 34; foreign lines: 2548; current: `increased Physical Damage$`

### Strict (zero foreign, no cross-line)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 10 | `D....sical` | 0 | 0 | no | no |  |
| 10 | `D...ysical` | 0 | 0 | no | no |  |
| 10 | `D..hysical` | 0 | 0 | no | no |  |
| 10 | `D.Physical` | 0 | 0 | no | no |  |
| 10 | `d Physical` | 0 | 0 | no | no |  |

### Line-only (zero foreign; cross-line possible)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 7 | `inc.*e$` | 0 | 0 | yes | no |  |
| 7 | `inc.+e$` | 0 | 0 | yes | no |  |
| 8 | `inc.*cal` | 0 | 0 | yes | no |  |
| 8 | `inc.*ge$` | 0 | 0 | yes | no |  |
| 8 | `inc.+cal` | 0 | 0 | yes | no |  |

### Shortest observed (collisions included)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 3 | `inc` | 5 | 42 | no | no | `10(8-10)% increased Attack Speed · 11(11-13)% increased Attack Speed` |
| 3 | `sed` | 5 | 42 | no | no | `10(8-10)% increased Attack Speed · 11(11-13)% increased Attack Speed` |
| 3 | `Phy` | 39 | 475 | no | no | `Physical Damage: 12-2 · Physical Damage: 15-31` |
| 3 | `cal` | 58 | 661 | no | no | `Critical Hit Chance: 10% · Critical Hit Chance: 5%` |
| 3 | `Dam` | 87 | 1053 | no | no | `100(87-100)% increased Elemental Damage with Attacks · 19(19-35)% increased Elemental Damage with Attacks` |
| 3 | `age` | 88 | 1054 | no | no | `100(87-100)% increased Elemental Damage with Attacks · 19(19-35)% increased Elemental Damage with Attacks` |
| 4 | `ased` | 3 | 29 | no | no | `10(8-10)% increased Attack Speed · 11(11-13)% increased Attack Speed` |
| 4 | `incr` | 3 | 29 | no | no | `10(8-10)% increased Attack Speed · 11(11-13)% increased Attack Speed` |

## LocalPhysicalDamagePercent — global

Targets: 36; foreign lines: 40992; current: `increased Physical Damage$`

### Strict (zero foreign, no cross-line)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 12 | `increased Ph` | 0 | 0 | no | no |  |
| 12 | `sed Physical` | 0 | 0 | no | no |  |
| 13 | `ased Physical` | 0 | 0 | no | no |  |
| 13 | `increased Phy` | 0 | 0 | no | no |  |
| 14 | `eased Physical` | 0 | 0 | no | no |  |

### Line-only (zero foreign; cross-line possible)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 12 | `increased Ph` | 0 | 0 | no | no |  |
| 12 | `sed Physical` | 0 | 0 | no | no |  |
| 13 | `ased Physical` | 0 | 0 | no | no |  |
| 13 | `increased Phy` | 0 | 0 | no | no |  |
| 14 | `eased Physical` | 0 | 0 | no | no |  |

### Shortest observed (collisions included)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 3 | `Phy` | 152 | 1874 | no | no | `Physical Damage: 35-59 · Physical Damage: 93-159 (augmented)` |
| 3 | `inc` | 249 | 2124 | no | no | `Item Class: Incursion Items · Item Class: Incubators` |
| 3 | `sed` | 257 | 2128 | no | no | `1% increased Area of Effect for Attacks per 10 Intelligence · 1(1-2)% increased Projectile Damage per Power Charge` |
| 3 | `cal` | 443 | 3265 | no | no | `Physical Damage: 35-59 · Physical Damage: 93-159 (augmented)` |
| 3 | `Dam` | 501 | 6004 | no | no | `Physical Damage: 35-59 · Physical Damage: 93-159 (augmented)` |
| 3 | `age` | 611 | 6247 | no | no | `Physical Damage: 35-59 · Physical Damage: 93-159 (augmented)` |
| 4 | `age$` | 73 | 1002 | no | no | `+1(1-2)% chance to Block Projectile Attack Damage · +10(9-10)% chance to Block Projectile Attack Damage` |
| 4 | `Phys` | 146 | 1868 | no | no | `Physical Damage: 35-59 · Physical Damage: 93-159 (augmented)` |

## LocalFireDamage — Bow

Targets: 22; foreign lines: 2560; current: `Fire Damage$`

### Strict (zero foreign, no cross-line)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 6 | `Fire D` | 0 | 0 | no | no |  |
| 7 | `Fire Da` | 0 | 0 | no | no |  |
| 8 | `Fire Dam` | 0 | 0 | no | no |  |
| 8 | `Fire.+e$` | 0 | 0 | no | no |  |
| 8 | `Fire.+ge` | 0 | 0 | no | no |  |

### Line-only (zero foreign; cross-line possible)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 6 | `Fi.*e$` | 0 | 0 | yes | no |  |
| 6 | `Fi.+e$` | 0 | 0 | yes | no |  |
| 6 | `Fir.+e` | 0 | 0 | yes | no |  |
| 6 | `Fire D` | 0 | 0 | no | no |  |
| 7 | `Ad.*ire` | 0 | 0 | yes | no |  |

### Shortest observed (collisions included)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 3 | `dds` | 4 | 64 | no | no | `Adds 11(7-11) to 18(12-18) Chaos damage · Adds 7(7-11) to 12(12-18) Chaos damage` |
| 3 | `Add` | 8 | 77 | no | no | `25(25-35)% chance to Chain an additional time · 35(25-35)% chance to Chain an additional time` |
| 3 | `Fir` | 21 | 227 | no | no | `{ Corrupted Modifier (Tier: 1) — Elemental Damage, Damage, Elemental, Fire, Attack } · { Corrupted Modifier (Tier: 10) — Elemental Damage, Damage, Elemental, Fire, Attack }` |
| 3 | `ire` | 40 | 331 | no | no | `10(20-10)% reduced Attribute Requirements · 15% reduced Attribute Requirements` |
| 3 | `Dam` | 87 | 1065 | no | no | `100(87-100)% increased Elemental Damage with Attacks · 19(19-35)% increased Elemental Damage with Attacks` |
| 3 | `age` | 88 | 1066 | no | no | `100(87-100)% increased Elemental Damage with Attacks · 19(19-35)% increased Elemental Damage with Attacks` |
| 4 | `Ad.s` | 4 | 64 | yes | no | `Adds 11(7-11) to 18(12-18) Chaos damage · Adds 7(7-11) to 12(12-18) Chaos damage` |
| 4 | `Adds` | 4 | 64 | no | no | `Adds 11(7-11) to 18(12-18) Chaos damage · Adds 7(7-11) to 12(12-18) Chaos damage` |

## LocalFireDamage — global

Targets: 44; foreign lines: 40984; current: `\) Fire Damage$`

### Strict (zero foreign, no cross-line)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 12 | `^Add.+F.*ge$` | 0 | 0 | no | no |  |
| 12 | `^Add.+F.+ge$` | 0 | 0 | no | no |  |
| 12 | `^Add.+Fi.*e$` | 0 | 0 | no | no |  |
| 12 | `^Add.+Fi.+e$` | 0 | 0 | no | no |  |
| 12 | `^Adds.*F.*e$` | 0 | 0 | no | no |  |

### Line-only (zero foreign; cross-line possible)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 11 | `Adds.*F.*e$` | 0 | 0 | yes | yes |  |
| 11 | `Adds.*F.+e$` | 0 | 0 | yes | yes |  |
| 11 | `Adds.+F.*e$` | 0 | 0 | yes | yes |  |
| 11 | `Adds.+F.+e$` | 0 | 0 | yes | yes |  |
| 11 | `^Ad.*F.*ge$` | 0 | 0 | yes | no |  |

### Shortest observed (collisions included)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 3 | `dds` | 13 | 393 | no | no | `Adds 1 to 2(2-3) Cold Damage to Spells · Adds 1 to 3(2-3) Cold Damage to Spells` |
| 3 | `Add` | 68 | 629 | no | no | `+15(15-18)% Surpassing chance to fire an additional Projectile · +18(15-18)% Surpassing chance to fire an additional Projectile` |
| 3 | `Fir` | 224 | 2033 | no | no | `Elemental Damage: 35-59 (fire), 4-64 (lightning) · +1 to Level of all Fire Spell Skills` |
| 3 | `ire` | 294 | 4137 | no | no | `Requires: Level 55 · Requires: Level 36, 46 Int` |
| 3 | `Dam` | 501 | 5996 | no | no | `Physical Damage: 35-59 · Physical Damage: 93-159 (augmented)` |
| 3 | `age` | 611 | 6239 | no | no | `Physical Damage: 35-59 · Physical Damage: 93-159 (augmented)` |
| 4 | `Adds` | 13 | 393 | no | no | `Adds 1 to 2(2-3) Cold Damage to Spells · Adds 1 to 3(2-3) Cold Damage to Spells` |
| 4 | `^Add` | 13 | 393 | no | no | `Adds 1 to 2(2-3) Cold Damage to Spells · Adds 1 to 3(2-3) Cold Damage to Spells` |

## LocalColdDamage — Bow

Targets: 22; foreign lines: 2560; current: `Cold Damage$`

### Strict (zero foreign, no cross-line)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 6 | `Col..d` | 0 | 0 | no | no |  |
| 6 | `Cold D` | 0 | 0 | no | no |  |
| 6 | `Cold.d` | 0 | 0 | no | no |  |
| 7 | `Cold Da` | 0 | 0 | no | no |  |
| 8 | `Col.*age` | 0 | 0 | no | no |  |

### Line-only (zero foreign; cross-line possible)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 6 | `Ad.*ld` | 0 | 0 | yes | no |  |
| 6 | `Ad.+ld` | 0 | 0 | yes | no |  |
| 6 | `Co...d` | 0 | 0 | yes | no |  |
| 6 | `Col.*e` | 0 | 0 | yes | no |  |
| 6 | `Col.+d` | 0 | 0 | yes | no |  |

### Shortest observed (collisions included)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 3 | `dds` | 4 | 64 | no | no | `Adds 11(7-11) to 18(12-18) Chaos damage · Adds 7(7-11) to 12(12-18) Chaos damage` |
| 3 | `Add` | 8 | 77 | no | no | `25(25-35)% chance to Chain an additional time · 35(25-35)% chance to Chain an additional time` |
| 3 | `Col` | 18 | 216 | no | no | `{ Corrupted Modifier (Tier: 1) — Elemental Damage, Damage, Elemental, Cold, Attack } · { Corrupted Modifier (Tier: 10) — Elemental Damage, Damage, Elemental, Cold, Attack }` |
| 3 | `old` | 18 | 216 | no | no | `{ Corrupted Modifier (Tier: 1) — Elemental Damage, Damage, Elemental, Cold, Attack } · { Corrupted Modifier (Tier: 10) — Elemental Damage, Damage, Elemental, Cold, Attack }` |
| 3 | `Dam` | 87 | 1065 | no | no | `100(87-100)% increased Elemental Damage with Attacks · 19(19-35)% increased Elemental Damage with Attacks` |
| 3 | `age` | 88 | 1066 | no | no | `100(87-100)% increased Elemental Damage with Attacks · 19(19-35)% increased Elemental Damage with Attacks` |
| 4 | `Ad.s` | 4 | 64 | yes | no | `Adds 11(7-11) to 18(12-18) Chaos damage · Adds 7(7-11) to 12(12-18) Chaos damage` |
| 4 | `Adds` | 4 | 64 | no | no | `Adds 11(7-11) to 18(12-18) Chaos damage · Adds 7(7-11) to 12(12-18) Chaos damage` |

## LocalColdDamage — global

Targets: 45; foreign lines: 40983; current: `\) Cold Damage$`

### Strict (zero foreign, no cross-line)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 13 | `^Add.*Co.*ge$` | 0 | 0 | no | no |  |
| 13 | `^Add.*Co.+ge$` | 0 | 0 | no | no |  |
| 13 | `^Add.*Col.*e$` | 0 | 0 | no | no |  |
| 13 | `^Add.*Col.+e$` | 0 | 0 | no | no |  |
| 13 | `^Add.+Co.*ge$` | 0 | 0 | no | no |  |

### Line-only (zero foreign; cross-line possible)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 11 | `^Ad.*Co.*e$` | 0 | 0 | yes | no |  |
| 11 | `^Ad.*Co.+e$` | 0 | 0 | yes | no |  |
| 11 | `^Ad.+Co.*e$` | 0 | 0 | yes | no |  |
| 11 | `^Ad.+Co.+e$` | 0 | 0 | yes | no |  |
| 12 | `Adds.*Co.*e$` | 0 | 0 | yes | yes |  |

### Shortest observed (collisions included)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 3 | `dds` | 13 | 392 | no | no | `Adds 1 to 2(2-3) Cold Damage to Spells · Adds 1 to 3(2-3) Cold Damage to Spells` |
| 3 | `Add` | 68 | 628 | no | no | `+15(15-18)% Surpassing chance to fire an additional Projectile · +18(15-18)% Surpassing chance to fire an additional Projectile` |
| 3 | `Col` | 199 | 2017 | no | no | `+1 to Level of all Cold Spell Skills · +1% to Maximum Cold Resistance` |
| 3 | `old` | 399 | 2289 | no | no | `+1 to Level of all Cold Spell Skills · +1% to Maximum Cold Resistance` |
| 3 | `Dam` | 501 | 5995 | no | no | `Physical Damage: 35-59 · Physical Damage: 93-159 (augmented)` |
| 3 | `age` | 611 | 6238 | no | no | `Physical Damage: 35-59 · Physical Damage: 93-159 (augmented)` |
| 4 | `Adds` | 13 | 392 | no | no | `Adds 1 to 2(2-3) Cold Damage to Spells · Adds 1 to 3(2-3) Cold Damage to Spells` |
| 4 | `^Add` | 13 | 392 | no | no | `Adds 1 to 2(2-3) Cold Damage to Spells · Adds 1 to 3(2-3) Cold Damage to Spells` |

## LocalLightningDamage — Bow

Targets: 22; foreign lines: 2560; current: `Lightning Damage$`

### Strict (zero foreign, no cross-line)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 9 | `Adds.*ing` | 0 | 0 | no | no |  |
| 9 | `Adds.+ing` | 0 | 0 | no | no |  |
| 9 | `L.*t.*ge$` | 0 | 0 | no | no |  |
| 9 | `L.*t.+ge$` | 0 | 0 | no | no |  |
| 9 | `L.+t.*ge$` | 0 | 0 | no | no |  |

### Line-only (zero foreign; cross-line possible)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 7 | `Li.*ge$` | 0 | 0 | yes | no |  |
| 7 | `Li.+ge$` | 0 | 0 | yes | no |  |
| 7 | `Lig.*e$` | 0 | 0 | yes | no |  |
| 7 | `Lig.+e$` | 0 | 0 | yes | no |  |
| 8 | `Ad.*ning` | 0 | 0 | yes | no |  |

### Shortest observed (collisions included)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 3 | `dds` | 4 | 64 | no | no | `Adds 11(7-11) to 18(12-18) Chaos damage · Adds 7(7-11) to 12(12-18) Chaos damage` |
| 3 | `Add` | 8 | 77 | no | no | `25(25-35)% chance to Chain an additional time · 35(25-35)% chance to Chain an additional time` |
| 3 | `Lig` | 23 | 234 | no | no | `10% increased Light Radius · 15% increased Light Radius` |
| 3 | `ing` | 192 | 681 | no | no | `Arcing · Blasting` |
| 3 | `Dam` | 87 | 1065 | no | no | `100(87-100)% increased Elemental Damage with Attacks · 19(19-35)% increased Elemental Damage with Attacks` |
| 3 | `age` | 88 | 1066 | no | no | `100(87-100)% increased Elemental Damage with Attacks · 19(19-35)% increased Elemental Damage with Attacks` |
| 4 | `Ad.s` | 4 | 64 | yes | no | `Adds 11(7-11) to 18(12-18) Chaos damage · Adds 7(7-11) to 12(12-18) Chaos damage` |
| 4 | `Adds` | 4 | 64 | no | no | `Adds 11(7-11) to 18(12-18) Chaos damage · Adds 7(7-11) to 12(12-18) Chaos damage` |

## LocalLightningDamage — global

Targets: 44; foreign lines: 40984; current: `\) Lightning Damage$`

### Strict (zero foreign, no cross-line)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 13 | `^Add.*Li.*ge$` | 0 | 0 | no | no |  |
| 13 | `^Add.*Li.+ge$` | 0 | 0 | no | no |  |
| 13 | `^Add.*Lig.*e$` | 0 | 0 | no | no |  |
| 13 | `^Add.*Lig.+e$` | 0 | 0 | no | no |  |
| 13 | `^Add.+Li.*ge$` | 0 | 0 | no | no |  |

### Line-only (zero foreign; cross-line possible)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 11 | `^Ad.*Li.*e$` | 0 | 0 | yes | no |  |
| 11 | `^Ad.*Li.+e$` | 0 | 0 | yes | no |  |
| 11 | `^Ad.+Li.*e$` | 0 | 0 | yes | no |  |
| 11 | `^Ad.+Li.+e$` | 0 | 0 | yes | no |  |
| 12 | `Adds.*Li.*e$` | 0 | 0 | yes | yes |  |

### Shortest observed (collisions included)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 3 | `dds` | 13 | 393 | no | no | `Adds 1 to 2(2-3) Cold Damage to Spells · Adds 1 to 3(2-3) Cold Damage to Spells` |
| 3 | `Add` | 68 | 629 | no | no | `+15(15-18)% Surpassing chance to fire an additional Projectile · +18(15-18)% Surpassing chance to fire an additional Projectile` |
| 3 | `Lig` | 241 | 2042 | no | no | `Elemental Damage: 35-59 (fire), 4-64 (lightning) · Lightning Damage: 2-43 (lightning)` |
| 3 | `Dam` | 501 | 5996 | no | no | `Physical Damage: 35-59 · Physical Damage: 93-159 (augmented)` |
| 3 | `age` | 611 | 6239 | no | no | `Physical Damage: 35-59 · Physical Damage: 93-159 (augmented)` |
| 3 | `ing` | 2805 | 8087 | no | no | `Evasion Rating: 117 · Evasion Rating: 117 (augmented)` |
| 4 | `Adds` | 13 | 393 | no | no | `Adds 1 to 2(2-3) Cold Damage to Spells · Adds 1 to 3(2-3) Cold Damage to Spells` |
| 4 | `^Add` | 13 | 393 | no | no | `Adds 1 to 2(2-3) Cold Damage to Spells · Adds 1 to 3(2-3) Cold Damage to Spells` |

## LocalEnergyShield — Body Armour

Targets: 62; foreign lines: 8011; current: `maximum Energy Shield$`

### Strict (zero foreign, no cross-line)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 8 | `m Energy` | 0 | 0 | no | no |  |
| 8 | `m....rgy` | 0 | 0 | no | no |  |
| 8 | `m...ergy` | 0 | 0 | no | no |  |
| 8 | `m..nergy` | 0 | 0 | no | no |  |
| 8 | `m.Energy` | 0 | 0 | no | no |  |

### Line-only (zero foreign; cross-line possible)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 6 | `max.*y` | 0 | 0 | yes | no |  |
| 6 | `max.+y` | 0 | 0 | yes | no |  |
| 7 | `ma.*ld$` | 0 | 0 | yes | no |  |
| 7 | `ma.+ld$` | 0 | 0 | yes | no |  |
| 7 | `max.*gy` | 0 | 0 | yes | no |  |

### Shortest observed (collisions included)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 3 | `max` | 5 | 48 | no | no | `1% increased Maximum Life for each Corrupted Item Equipped · +10(10-19) to maximum Life` |
| 3 | `mum` | 5 | 48 | no | no | `1% increased Maximum Life for each Corrupted Item Equipped · +10(10-19) to maximum Life` |
| 3 | `rgy` | 111 | 1463 | no | no | `100(92-100)% increased Armour and Energy Shield · 101(101-110)% increased Armour and Energy Shield` |
| 3 | `eld` | 112 | 1464 | no | no | `100(92-100)% increased Armour and Energy Shield · 101(101-110)% increased Armour and Energy Shield` |
| 3 | `Shi` | 115 | 1478 | no | no | `100(92-100)% increased Armour and Energy Shield · 101(101-110)% increased Armour and Energy Shield` |
| 3 | `Ene` | 151 | 1560 | no | no | `1(1-2) Life Regeneration per second · 13(9.1-13) Life Regeneration per second` |
| 4 | `to m` | 2 | 44 | no | no | `+10(10-19) to maximum Life · +10(7-10) to maximum Life` |
| 4 | `imum` | 5 | 48 | no | no | `1% increased Maximum Life for each Corrupted Item Equipped · +10(10-19) to maximum Life` |

## LocalEnergyShield — global

Targets: 84; foreign lines: 40944; current: `\) to maximum Energy Shield$`

### Strict (zero foreign, no cross-line)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 12 | `to.+Ener.*d$` | 0 | 0 | no | no |  |
| 12 | `to.+Ener.+d$` | 0 | 0 | no | no |  |
| 12 | `to.+m.*ield$` | 0 | 0 | no | no |  |
| 12 | `to.+m.+ield$` | 0 | 0 | no | no |  |
| 12 | `to.+ma.*eld$` | 0 | 0 | no | no |  |

### Line-only (zero foreign; cross-line possible)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 11 | `t.*max.*ld$` | 0 | 0 | yes | no |  |
| 11 | `t.*max.+ld$` | 0 | 0 | yes | no |  |
| 11 | `t.+max.*ld$` | 0 | 0 | yes | no |  |
| 11 | `t.+max.+ld$` | 0 | 0 | yes | no |  |
| 11 | `to.*Ene.*d$` | 0 | 0 | yes | yes |  |

### Shortest observed (collisions included)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 3 | `max` | 49 | 326 | no | no | `+1 to maximum number of Elemental Infusions · +1% to Maximum Chaos Resistance` |
| 3 | `mum` | 49 | 326 | no | no | `+1 to maximum number of Elemental Infusions · +1% to Maximum Chaos Resistance` |
| 3 | `rgy` | 196 | 2454 | no | no | `Energy Shield: 49 · Energy Shield: 86 (augmented)` |
| 3 | `Ene` | 367 | 3080 | no | no | `Energy Shield: 49 · Energy Shield: 86 (augmented)` |
| 3 | `eld` | 810 | 3093 | no | no | `Energy Shield: 49 · Energy Shield: 86 (augmented)` |
| 3 | `Shi` | 848 | 3222 | no | no | `Energy Shield: 49 · Energy Shield: 86 (augmented)` |
| 4 | `to m` | 24 | 222 | no | no | `+1 to maximum number of Elemental Infusions · +1% to Maximum Chaos Resistance` |
| 4 | `imum` | 49 | 326 | no | no | `+1 to maximum number of Elemental Infusions · +1% to Maximum Chaos Resistance` |

## LocalPhysicalDamageReductionRating — Body Armour

Targets: 62; foreign lines: 8011; current: `\) to Armour$`

### Strict (zero foreign, no cross-line)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 5 | `to Ar` | 0 | 0 | no | no |  |
| 6 | `to Arm` | 0 | 0 | no | no |  |
| 7 | `to Armo` | 0 | 0 | no | no |  |
| 8 | `o Armour` | 0 | 0 | no | no |  |
| 8 | `to Armou` | 0 | 0 | no | no |  |

### Line-only (zero foreign; cross-line possible)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 5 | `to Ar` | 0 | 0 | no | no |  |
| 5 | `to..r` | 0 | 0 | yes | no |  |
| 6 | `to Arm` | 0 | 0 | no | no |  |
| 7 | `to Armo` | 0 | 0 | no | no |  |
| 8 | `o Armour` | 0 | 0 | no | no |  |

### Shortest observed (collisions included)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 3 | `Arm` | 300 | 1719 | no | no | `100(92-100)% increased Armour · 101(101-110)% increased Armour` |
| 3 | `our` | 345 | 2259 | no | no | `100(92-100)% increased Armour · 101(101-110)% increased Armour` |
| 4 | `to A` | 2 | 2 | no | no | `+1% to all Maximum Elemental Resistances · +1% to all Resistances for each Corrupted Item Equipped` |
| 4 | `our$` | 52 | 81 | no | no | `100(92-100)% increased Armour · 101(101-110)% increased Armour` |
| 4 | `Armo` | 282 | 1701 | no | no | `100(92-100)% increased Armour · 101(101-110)% increased Armour` |
| 4 | `mour` | 282 | 1701 | no | no | `100(92-100)% increased Armour · 101(101-110)% increased Armour` |
| 4 | `Ar.o` | 296 | 1726 | yes | no | `100(92-100)% increased Armour · 101(101-110)% increased Armour` |
| 5 | `to Ar` | 0 | 0 | no | no |  |

## LocalPhysicalDamageReductionRating — global

Targets: 84; foreign lines: 40944; current: `\) to Armour$`

### Strict (zero foreign, no cross-line)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 5 | `to Ar` | 0 | 0 | no | no |  |
| 6 | `to Arm` | 0 | 0 | no | no |  |
| 7 | `to Armo` | 0 | 0 | no | no |  |
| 8 | `o Armour` | 0 | 0 | no | no |  |
| 8 | `to Armou` | 0 | 0 | no | no |  |

### Line-only (zero foreign; cross-line possible)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 5 | `to Ar` | 0 | 0 | no | no |  |
| 6 | `to Arm` | 0 | 0 | no | no |  |
| 7 | `to Armo` | 0 | 0 | no | no |  |
| 8 | `o Armour` | 0 | 0 | no | no |  |
| 8 | `to Armou` | 0 | 0 | no | no |  |

### Shortest observed (collisions included)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 3 | `Arm` | 431 | 2709 | no | no | `Armour: 115 · Armour: 138 (augmented)` |
| 3 | `our` | 685 | 5657 | no | no | `Armour: 115 · Armour: 138 (augmented)` |
| 4 | `our$` | 58 | 107 | no | no | `15(15-24)% increased effect of Fully Broken Armour · 24(15-24)% increased effect of Fully Broken Armour` |
| 4 | `to A` | 24 | 308 | no | no | `+10(10-12)% to all Elemental Resistances · +10(10-25) to Ailment Threshold` |
| 4 | `mour` | 340 | 2300 | no | no | `Armour: 115 · Armour: 138 (augmented)` |
| 4 | `Armo` | 360 | 2331 | no | no | `Armour: 115 · Armour: 138 (augmented)` |
| 4 | `Ar.o` | 564 | 2594 | yes | no | `Armour: 115 · Armour: 138 (augmented)` |
| 5 | `to Ar` | 0 | 0 | no | no |  |

## LocalEvasionRating — Body Armour

Targets: 62; foreign lines: 8011; current: `\) to Evasion Rating$`

### Strict (zero foreign, no cross-line)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 5 | `to Ev` | 0 | 0 | no | no |  |
| 6 | `to Eva` | 0 | 0 | no | no |  |
| 7 | `to Evas` | 0 | 0 | no | no |  |
| 8 | `to Evasi` | 0 | 0 | no | no |  |
| 9 | `o Evasion` | 0 | 0 | no | no |  |

### Line-only (zero foreign; cross-line possible)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 5 | `to Ev` | 0 | 0 | no | no |  |
| 6 | `to Eva` | 0 | 0 | no | no |  |
| 7 | `to Evas` | 0 | 0 | no | no |  |
| 8 | `to Evasi` | 0 | 0 | no | no |  |
| 9 | `o Evasion` | 0 | 0 | no | no |  |

### Shortest observed (collisions included)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 3 | `Rat` | 31 | 314 | no | no | `1(1-2) Life Regeneration per second · 13(9.1-13) Life Regeneration per second` |
| 3 | `ing` | 172 | 850 | no | no | `100(92-100)% increased Evasion Rating · 101(101-110)% increased Evasion Rating` |
| 3 | `Eva` | 120 | 1537 | no | no | `100(92-100)% increased Armour and Evasion · 101(101-110)% increased Armour and Evasion` |
| 3 | `ion` | 154 | 1711 | no | no | `1(1-2) Life Regeneration per second · 13(9.1-13) Life Regeneration per second` |
| 4 | `to E` | 1 | 14 | no | no | `+14(14-19)% of Armour also applies to Elemental Damage · +15(15-25)% of Armour also applies to Elemental Damage` |
| 4 | `ing$` | 34 | 74 | no | no | `100(92-100)% increased Evasion Rating · 101(101-110)% increased Evasion Rating` |
| 4 | `ting` | 20 | 264 | no | no | `100(92-100)% increased Evasion Rating · 101(101-110)% increased Evasion Rating` |
| 4 | `t.ng` | 32 | 276 | yes | no | `100(92-100)% increased Evasion Rating · 101(101-110)% increased Evasion Rating` |

## LocalEvasionRating — global

Targets: 80; foreign lines: 40948; current: `\) to Evasion Rating$`

### Strict (zero foreign, no cross-line)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 17 | `o Evasion Rating$` | 0 | 0 | no | no |  |
| 18 | `to Evasion Rating$` | 0 | 0 | no | no |  |
| 18 | `to Evasion.....ng$` | 0 | 0 | no | no |  |
| 18 | `to Evasion....ing$` | 0 | 0 | no | no |  |
| 18 | `to Evasion...ting$` | 0 | 0 | no | no |  |

### Line-only (zero foreign; cross-line possible)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 14 | `to Evasion.*g$` | 0 | 0 | yes | no |  |
| 14 | `to Evasion.+g$` | 0 | 0 | yes | no |  |
| 15 | `to Evasion.*ng$` | 0 | 0 | yes | no |  |
| 15 | `to Evasion.+ng$` | 0 | 0 | yes | no |  |
| 16 | `to Evasion.*ing$` | 0 | 0 | yes | no |  |

### Shortest observed (collisions included)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 3 | `Rat` | 221 | 1472 | no | no | `Evasion Rating: 117 · Evasion Rating: 117 (augmented)` |
| 3 | `Eva` | 195 | 2565 | no | no | `Evasion Rating: 117 · Evasion Rating: 117 (augmented)` |
| 3 | `ion` | 666 | 5445 | no | no | `Evasion Rating: 117 · Evasion Rating: 117 (augmented)` |
| 3 | `ing` | 2805 | 8051 | no | no | `Evasion Rating: 117 · Evasion Rating: 117 (augmented)` |
| 4 | `to E` | 6 | 31 | no | no | `Burning Enemies you kill have a 10(10-30)% chance to Explode, dealing a
tenth of their maximum Life as Fire Damage · Burning Enemies you kill have a 30(10-30)% chance to Explode, dealing a
tenth of their maximum Life as Fire Damage` |
| 4 | `ing$` | 298 | 432 | no | no | `+327(327-427) to Accuracy Rating · +427(327-427) to Accuracy Rating` |
| 4 | `Rati` | 105 | 1046 | no | no | `Evasion Rating: 117 · Evasion Rating: 117 (augmented)` |
| 4 | `ting` | 1372 | 2503 | no | no | `Evasion Rating: 117 · Evasion Rating: 117 (augmented)` |

## BaseSpirit — Amulet

Targets: 12; foreign lines: 3645; current: `\) to Spirit$`

### Strict (zero foreign, no cross-line)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 4 | `rit$` | 0 | 0 | no | no |  |
| 5 | `irit$` | 0 | 0 | no | no |  |
| 5 | `to Sp` | 0 | 0 | no | no |  |
| 6 | `pirit$` | 0 | 0 | no | no |  |
| 6 | `to Spi` | 0 | 0 | no | no |  |

### Line-only (zero foreign; cross-line possible)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 4 | `rit$` | 0 | 0 | no | no |  |
| 5 | `irit$` | 0 | 0 | no | no |  |
| 5 | `to Sp` | 0 | 0 | no | no |  |
| 6 | `S.*it$` | 0 | 0 | yes | no |  |
| 6 | `S.+it$` | 0 | 0 | yes | no |  |

### Shortest observed (collisions included)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 3 | `Spi` | 2 | 4 | no | no | `Grants Skill: Level 1 Raging Spirits · Grants Skill: Level 10 Raging Spirits` |
| 3 | `rit` | 27 | 219 | no | no | `10(10-14)% increased Critical Damage Bonus · 14(10-14)% increased Critical Damage Bonus` |
| 4 | `rit$` | 0 | 0 | no | no |  |
| 4 | `Spir` | 2 | 4 | no | no | `Grants Skill: Level 1 Raging Spirits · Grants Skill: Level 10 Raging Spirits` |
| 4 | `irit` | 2 | 4 | no | no | `Grants Skill: Level 1 Raging Spirits · Grants Skill: Level 10 Raging Spirits` |
| 4 | `to S` | 1 | 18 | no | no | `+10(10-15) to Strength · +12(9-12) to Strength` |
| 5 | `irit$` | 0 | 0 | no | no |  |
| 5 | `to Sp` | 0 | 0 | no | no |  |

## BaseSpirit — global

Targets: 22; foreign lines: 41006; current: `\) to Spirit$`

### Strict (zero foreign, no cross-line)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 6 | `to Spi` | 0 | 0 | no | no |  |
| 7 | `to Spir` | 0 | 0 | no | no |  |
| 8 | `o Spirit` | 0 | 0 | no | no |  |
| 8 | `to Spiri` | 0 | 0 | no | no |  |
| 9 | `o Spirit$` | 0 | 0 | no | no |  |

### Line-only (zero foreign; cross-line possible)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 6 | `to Spi` | 0 | 0 | no | no |  |
| 7 | `t.*rit$` | 0 | 0 | yes | no |  |
| 7 | `t.+rit$` | 0 | 0 | yes | no |  |
| 7 | `to Spir` | 0 | 0 | no | no |  |
| 8 | `o Spirit` | 0 | 0 | no | no |  |

### Shortest observed (collisions included)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 3 | `Spi` | 213 | 362 | no | no | `Item Class: Uncut Spirit Gems · 1(1-20)% increased Spirit` |
| 3 | `rit` | 201 | 1461 | no | no | `Rarity: Normal · Rarity: Magic` |
| 4 | `rit$` | 1 | 36 | no | no | `1(1-20)% increased Spirit · 12(8-12)% increased Spirit` |
| 4 | `irit` | 27 | 86 | no | no | `Item Class: Uncut Spirit Gems · 1(1-20)% increased Spirit` |
| 4 | `Spir` | 74 | 190 | no | no | `Item Class: Uncut Spirit Gems · 1(1-20)% increased Spirit` |
| 4 | `to S` | 7 | 194 | no | no | `Adds 1 to 2(2-3) Cold Damage to Spells · Adds 1 to 3(2-3) Cold Damage to Spells` |
| 5 | `irit$` | 1 | 36 | no | no | `1(1-20)% increased Spirit · 12(8-12)% increased Spirit` |
| 5 | `pirit` | 27 | 86 | no | no | `Item Class: Uncut Spirit Gems · 1(1-20)% increased Spirit` |

## FireResistance — Amulet

Targets: 16; foreign lines: 3641; current: `Fire Resistance$`

### Strict (zero foreign, no cross-line)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 4 | `to F` | 0 | 0 | no | no |  |
| 5 | `to Fi` | 0 | 0 | no | no |  |
| 6 | `o Fire` | 0 | 0 | no | no |  |
| 6 | `to Fir` | 0 | 0 | no | no |  |
| 7 | `t...ire` | 0 | 0 | no | no |  |

### Line-only (zero foreign; cross-line possible)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 4 | `to F` | 0 | 0 | no | no |  |
| 5 | `to Fi` | 0 | 0 | no | no |  |
| 6 | `o Fire` | 0 | 0 | no | no |  |
| 6 | `to Fir` | 0 | 0 | no | no |  |
| 7 | `Fi.+ce$` | 0 | 0 | yes | no |  |

### Shortest observed (collisions included)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 3 | `Fir` | 17 | 182 | no | no | `{ Corrupted Modifier (Tier: 1) — Cold Resistance, Elemental Resistance, Fire Resistance, Lightning Resistance, Elemental, Fire, Cold, Lightning, Resistance } · { Corrupted Modifier (Tier: 10) — Cold Resistance, Elemental Resistance, Fire Resistance, Lightning Resistance, Elemental, Fire, Cold, Lightning, Resistance }` |
| 3 | `ire` | 25 | 201 | no | no | `of Ire · { Corrupted Modifier (Tier: 1) — Cold Resistance, Elemental Resistance, Fire Resistance, Lightning Resistance, Elemental, Fire, Cold, Lightning, Resistance }` |
| 3 | `nce` | 98 | 965 | no | no | `10(10-14)% increased Critical Hit Chance · 14(10-14)% increased Critical Hit Chance` |
| 3 | `Res` | 146 | 1274 | no | no | `Buttressed · Grants Skill: Level 1 Overwhelming Presence` |
| 4 | `to F` | 0 | 0 | no | no |  |
| 4 | `nce$` | 16 | 94 | no | no | `10(10-14)% increased Critical Hit Chance · 14(10-14)% increased Critical Hit Chance` |
| 4 | `Fire` | 17 | 182 | no | no | `{ Corrupted Modifier (Tier: 1) — Cold Resistance, Elemental Resistance, Fire Resistance, Lightning Resistance, Elemental, Fire, Cold, Lightning, Resistance } · { Corrupted Modifier (Tier: 10) — Cold Resistance, Elemental Resistance, Fire Resistance, Lightning Resistance, Elemental, Fire, Cold, Lightning, Resistance }` |
| 4 | `Fi.e` | 21 | 199 | yes | no | `Confident · Grants Skill: Level 1 Sacrifice` |

## FireResistance — global

Targets: 20; foreign lines: 41008; current: `% to Fire Resistance$`

### Strict (zero foreign, no cross-line)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 9 | `to Fire R` | 0 | 0 | no | no |  |
| 10 | `to Fire Re` | 0 | 0 | no | no |  |
| 10 | `to Fire..e` | 0 | 0 | no | no |  |
| 10 | `to Fire.re` | 0 | 0 | no | no |  |
| 11 | `to Fire Res` | 0 | 0 | no | no |  |

### Line-only (zero foreign; cross-line possible)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 9 | `to Fire R` | 0 | 0 | no | no |  |
| 10 | `to Fire Re` | 0 | 0 | no | no |  |
| 10 | `to Fire..e` | 0 | 0 | no | no |  |
| 10 | `to Fire.re` | 0 | 0 | no | no |  |
| 11 | `to Fire Res` | 0 | 0 | no | no |  |

### Shortest observed (collisions included)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 3 | `Fir` | 224 | 2057 | no | no | `Elemental Damage: 35-59 (fire), 4-64 (lightning) · +1 to Level of all Fire Spell Skills` |
| 3 | `ire` | 294 | 4161 | no | no | `Requires: Level 55 · Requires: Level 36, 46 Int` |
| 3 | `nce` | 698 | 6714 | no | no | `Block chance: 32% · Block chance: 32% (augmented)` |
| 3 | `Res` | 2372 | 8944 | no | no | `Requires: Level 55 · Requires: Level 36, 46 Int` |
| 4 | `to F` | 13 | 63 | no | no | `+11(11-15)% to Fire Damage over Time Multiplier · +15(11-15)% to Fire Damage over Time Multiplier` |
| 4 | `nce$` | 53 | 305 | no | no | `+0(0-1)% to Critical Hit Chance · +0.3(0.3-0.4) metres to Dodge Roll distance` |
| 4 | `Resi` | 126 | 1249 | no | no | `+1% to Maximum Chaos Resistance · +1% to Maximum Cold Resistance` |
| 4 | `ance` | 278 | 1982 | no | no | `Block chance: 32% · Block chance: 32% (augmented)` |

## ColdResistance — Amulet

Targets: 16; foreign lines: 3641; current: `% to Cold Resistance$`

### Strict (zero foreign, no cross-line)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 5 | `to Co` | 0 | 0 | no | no |  |
| 6 | `o Cold` | 0 | 0 | no | no |  |
| 6 | `to Col` | 0 | 0 | no | no |  |
| 7 | `t..Cold` | 0 | 0 | no | no |  |
| 7 | `to Cold` | 0 | 0 | no | no |  |

### Line-only (zero foreign; cross-line possible)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 5 | `to Co` | 0 | 0 | no | no |  |
| 5 | `to..o` | 0 | 0 | yes | no |  |
| 6 | `o Cold` | 0 | 0 | no | no |  |
| 6 | `to Col` | 0 | 0 | no | no |  |
| 7 | `t....ld` | 0 | 0 | yes | no |  |

### Shortest observed (collisions included)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 3 | `old` | 26 | 193 | no | no | `Gold Amulet · Gold Amulet of the Brute` |
| 3 | `Col` | 22 | 200 | no | no | `Collector's · { Corrupted Modifier (Tier: 1) — Cold Resistance, Elemental Resistance, Fire Resistance, Lightning Resistance, Elemental, Fire, Cold, Lightning, Resistance }` |
| 3 | `nce` | 98 | 965 | no | no | `10(10-14)% increased Critical Hit Chance · 14(10-14)% increased Critical Hit Chance` |
| 3 | `Res` | 146 | 1274 | no | no | `Buttressed · Grants Skill: Level 1 Overwhelming Presence` |
| 4 | `to C` | 1 | 12 | no | no | `+11(8-11)% to Chaos Resistance · +12(12-15)% to Chaos Resistance` |
| 4 | `nce$` | 16 | 94 | no | no | `10(10-14)% increased Critical Hit Chance · 14(10-14)% increased Critical Hit Chance` |
| 4 | `C.ld` | 20 | 187 | yes | no | `{ Corrupted Modifier (Tier: 1) — Cold Resistance, Elemental Resistance, Fire Resistance, Lightning Resistance, Elemental, Fire, Cold, Lightning, Resistance } · { Corrupted Modifier (Tier: 10) — Cold Resistance, Elemental Resistance, Fire Resistance, Lightning Resistance, Elemental, Fire, Cold, Lightning, Resistance }` |
| 4 | `Cold` | 20 | 187 | no | no | `{ Corrupted Modifier (Tier: 1) — Cold Resistance, Elemental Resistance, Fire Resistance, Lightning Resistance, Elemental, Fire, Cold, Lightning, Resistance } · { Corrupted Modifier (Tier: 10) — Cold Resistance, Elemental Resistance, Fire Resistance, Lightning Resistance, Elemental, Fire, Cold, Lightning, Resistance }` |

## ColdResistance — global

Targets: 21; foreign lines: 41007; current: `% to Cold Resistance$`

### Strict (zero foreign, no cross-line)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 9 | `to Cold R` | 0 | 0 | no | no |  |
| 10 | `to Cold Re` | 0 | 0 | no | no |  |
| 10 | `to Cold..e` | 0 | 0 | no | no |  |
| 11 | `to Cold Res` | 0 | 0 | no | no |  |
| 12 | `to Cold Resi` | 0 | 0 | no | no |  |

### Line-only (zero foreign; cross-line possible)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 9 | `to Cold R` | 0 | 0 | no | no |  |
| 10 | `to Cold Re` | 0 | 0 | no | no |  |
| 10 | `to Cold..e` | 0 | 0 | no | no |  |
| 11 | `to Cold Res` | 0 | 0 | no | no |  |
| 12 | `to Cold Resi` | 0 | 0 | no | no |  |

### Shortest observed (collisions included)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 3 | `Col` | 199 | 2041 | no | no | `+1 to Level of all Cold Spell Skills · +1% to Maximum Cold Resistance` |
| 3 | `old` | 399 | 2313 | no | no | `+1 to Level of all Cold Spell Skills · +1% to Maximum Cold Resistance` |
| 3 | `nce` | 698 | 6713 | no | no | `Block chance: 32% · Block chance: 32% (augmented)` |
| 3 | `Res` | 2372 | 8943 | no | no | `Requires: Level 55 · Requires: Level 36, 46 Int` |
| 4 | `to C` | 20 | 121 | no | no | `+0(0-1)% to Critical Hit Chance · +1(0-1)% to Critical Hit Chance` |
| 4 | `nce$` | 53 | 304 | no | no | `+0(0-1)% to Critical Hit Chance · +0.3(0.3-0.4) metres to Dodge Roll distance` |
| 4 | `Resi` | 126 | 1248 | no | no | `+1% to Maximum Chaos Resistance · +1% to Maximum Cold Resistance` |
| 4 | `C.ld` | 178 | 1974 | yes | no | `+1 to Level of all Cold Spell Skills · +1% to Maximum Cold Resistance` |

## LightningResistance — Amulet

Targets: 16; foreign lines: 3641; current: `Lightning Resistance$`

### Strict (zero foreign, no cross-line)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 5 | `to Li` | 0 | 0 | no | no |  |
| 6 | `to Lig` | 0 | 0 | no | no |  |
| 7 | `to Ligh` | 0 | 0 | no | no |  |
| 8 | `to Light` | 0 | 0 | no | no |  |
| 9 | `to Lightn` | 0 | 0 | no | no |  |

### Line-only (zero foreign; cross-line possible)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 5 | `to Li` | 0 | 0 | no | no |  |
| 6 | `to Lig` | 0 | 0 | no | no |  |
| 6 | `to...g` | 0 | 0 | yes | no |  |
| 7 | `to Ligh` | 0 | 0 | no | no |  |
| 8 | `Ligh.*e$` | 0 | 0 | yes | no |  |

### Shortest observed (collisions included)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 3 | `Lig` | 20 | 202 | no | no | `of the Lightning · { Corrupted Modifier (Tier: 1) — Cold Resistance, Elemental Resistance, Fire Resistance, Lightning Resistance, Elemental, Fire, Cold, Lightning, Resistance }` |
| 3 | `ing` | 69 | 470 | no | no | `10(10-14)% increased Evasion Rating · 14(10-14)% increased Evasion Rating` |
| 3 | `nce` | 98 | 965 | no | no | `10(10-14)% increased Critical Hit Chance · 14(10-14)% increased Critical Hit Chance` |
| 3 | `Res` | 146 | 1274 | no | no | `Buttressed · Grants Skill: Level 1 Overwhelming Presence` |
| 4 | `to L` | 6 | 15 | no | no | `+1(1-2) to Level of all Cold Skills · +2(1-2) to Level of all Cold Skills` |
| 4 | `nce$` | 16 | 94 | no | no | `10(10-14)% increased Critical Hit Chance · 14(10-14)% increased Critical Hit Chance` |
| 4 | `Ligh` | 19 | 184 | no | no | `of the Lightning · { Corrupted Modifier (Tier: 1) — Cold Resistance, Elemental Resistance, Fire Resistance, Lightning Resistance, Elemental, Fire, Cold, Lightning, Resistance }` |
| 4 | `ning` | 21 | 197 | no | no | `Shining · of the Lightning` |

## LightningResistance — global

Targets: 21; foreign lines: 41007; current: `% to Lightning Resistance$`

### Strict (zero foreign, no cross-line)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 14 | `to Lightning R` | 0 | 0 | no | no |  |
| 15 | `to Lightning Re` | 0 | 0 | no | no |  |
| 15 | `to Lightning..e` | 0 | 0 | no | no |  |
| 16 | `to Lightning Res` | 0 | 0 | no | no |  |
| 17 | `to Lightning Resi` | 0 | 0 | no | no |  |

### Line-only (zero foreign; cross-line possible)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 14 | `to Lightning R` | 0 | 0 | no | no |  |
| 15 | `to Lightning Re` | 0 | 0 | no | no |  |
| 15 | `to Lightning..e` | 0 | 0 | no | no |  |
| 16 | `to Lightning Res` | 0 | 0 | no | no |  |
| 16 | `to Lightning.*e$` | 0 | 0 | yes | no |  |

### Shortest observed (collisions included)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 3 | `Lig` | 241 | 2065 | no | no | `Elemental Damage: 35-59 (fire), 4-64 (lightning) · Lightning Damage: 2-43 (lightning)` |
| 3 | `nce` | 698 | 6713 | no | no | `Block chance: 32% · Block chance: 32% (augmented)` |
| 3 | `ing` | 2805 | 8110 | no | no | `Evasion Rating: 117 · Evasion Rating: 117 (augmented)` |
| 3 | `Res` | 2372 | 8943 | no | no | `Requires: Level 55 · Requires: Level 36, 46 Int` |
| 4 | `to L` | 21 | 96 | no | no | `+1 to Level of Socketed Support Gems · +1 to Level of all Chaos Spell Skills` |
| 4 | `nce$` | 53 | 304 | no | no | `+0(0-1)% to Critical Hit Chance · +0.3(0.3-0.4) metres to Dodge Roll distance` |
| 4 | `Resi` | 126 | 1248 | no | no | `+1% to Maximum Chaos Resistance · +1% to Maximum Cold Resistance` |
| 4 | `Ligh` | 227 | 1963 | no | no | `Elemental Damage: 35-59 (fire), 4-64 (lightning) · Lightning Damage: 2-43 (lightning)` |

## ChaosResistance — Amulet

Targets: 12; foreign lines: 3645; current: `Chaos Resistance$`

### Strict (zero foreign, no cross-line)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 5 | `to Ch` | 0 | 0 | no | no |  |
| 6 | `to Cha` | 0 | 0 | no | no |  |
| 7 | `o Chaos` | 0 | 0 | no | no |  |
| 7 | `to Chao` | 0 | 0 | no | no |  |
| 8 | `t....aos` | 0 | 0 | no | no |  |

### Line-only (zero foreign; cross-line possible)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 5 | `to Ch` | 0 | 0 | no | no |  |
| 6 | `to Cha` | 0 | 0 | no | no |  |
| 6 | `to.*os` | 0 | 0 | yes | no |  |
| 6 | `to.+os` | 0 | 0 | yes | no |  |
| 7 | `o Chaos` | 0 | 0 | no | no |  |

### Shortest observed (collisions included)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 3 | `aos` | 6 | 72 | no | no | `{ Suffix Modifier "of Bameth" (Tier: 1) — Chaos Resistance, Chaos, Resistance } · { Suffix Modifier "of Bameth" (Tier: 10) — Chaos Resistance, Chaos, Resistance }` |
| 3 | `Cha` | 34 | 160 | no | no | `10(10-14)% increased Critical Hit Chance · 14(10-14)% increased Critical Hit Chance` |
| 3 | `nce` | 98 | 969 | no | no | `10(10-14)% increased Critical Hit Chance · 14(10-14)% increased Critical Hit Chance` |
| 3 | `Res` | 146 | 1278 | no | no | `Buttressed · Grants Skill: Level 1 Overwhelming Presence` |
| 4 | `to C` | 1 | 16 | no | no | `+10(6-10)% to Cold Resistance · +11(11-15)% to Cold Resistance` |
| 4 | `Chao` | 6 | 72 | no | no | `{ Suffix Modifier "of Bameth" (Tier: 1) — Chaos Resistance, Chaos, Resistance } · { Suffix Modifier "of Bameth" (Tier: 10) — Chaos Resistance, Chaos, Resistance }` |
| 4 | `haos` | 6 | 72 | no | no | `{ Suffix Modifier "of Bameth" (Tier: 1) — Chaos Resistance, Chaos, Resistance } · { Suffix Modifier "of Bameth" (Tier: 10) — Chaos Resistance, Chaos, Resistance }` |
| 4 | `Ch.o` | 8 | 85 | yes | no | `Marchioness' · { Prefix Modifier "Marchioness'" (Tier: 1) }` |

## ChaosResistance — global

Targets: 32; foreign lines: 40996; current: `% to Chaos Resistance$`

### Strict (zero foreign, no cross-line)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 17 | `to Chaos.+R.+nce$` | 0 | 0 | no | no |  |
| 17 | `to Chaos.+Re.*ce$` | 0 | 0 | no | no |  |
| 17 | `to Chaos.+Re.+ce$` | 0 | 0 | no | no |  |
| 17 | `to Chaos.+Res.*e$` | 0 | 0 | no | no |  |
| 17 | `to Chaos.+Res.+e$` | 0 | 0 | no | no |  |

### Line-only (zero foreign; cross-line possible)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 13 | `to Chaos.*ce$` | 0 | 0 | yes | no |  |
| 13 | `to Chaos.+ce$` | 0 | 0 | yes | no |  |
| 14 | `to Chaos.*nce$` | 0 | 0 | yes | no |  |
| 14 | `to Chaos.+nce$` | 0 | 0 | yes | no |  |
| 15 | `to Chaos.*C.*e$` | 0 | 0 | yes | no |  |

### Shortest observed (collisions included)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 3 | `aos` | 107 | 1146 | no | no | `+1 to Level of all Chaos Spell Skills · +1% to Maximum Chaos Resistance` |
| 3 | `Cha` | 380 | 2387 | no | no | `Block chance: 32% · Block chance: 32% (augmented)` |
| 3 | `nce` | 698 | 6702 | no | no | `Block chance: 32% · Block chance: 32% (augmented)` |
| 3 | `Res` | 2372 | 8932 | no | no | `Requires: Level 55 · Requires: Level 36, 46 Int` |
| 4 | `to C` | 20 | 110 | no | no | `+0(0-1)% to Critical Hit Chance · +1(0-1)% to Critical Hit Chance` |
| 4 | `nce$` | 53 | 293 | no | no | `+0(0-1)% to Critical Hit Chance · +0.3(0.3-0.4) metres to Dodge Roll distance` |
| 4 | `Chao` | 107 | 1146 | no | no | `+1 to Level of all Chaos Spell Skills · +1% to Maximum Chaos Resistance` |
| 4 | `haos` | 107 | 1146 | no | no | `+1 to Level of all Chaos Spell Skills · +1% to Maximum Chaos Resistance` |

## Strength — Amulet

Targets: 18; foreign lines: 3639; current: `\) to Strength$`

### Strict (zero foreign, no cross-line)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 3 | `gth` | 0 | 0 | no | no |  |
| 4 | `Stre` | 0 | 0 | no | no |  |
| 4 | `gth$` | 0 | 0 | no | no |  |
| 4 | `ngth` | 0 | 0 | no | no |  |
| 5 | `Stren` | 0 | 0 | no | no |  |

### Line-only (zero foreign; cross-line possible)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 3 | `gth` | 0 | 0 | no | no |  |
| 4 | `Stre` | 0 | 0 | no | no |  |
| 4 | `gth$` | 0 | 0 | no | no |  |
| 4 | `ngth` | 0 | 0 | no | no |  |
| 5 | `Stren` | 0 | 0 | no | no |  |

### Shortest observed (collisions included)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 3 | `gth` | 0 | 0 | no | no |  |
| 3 | `Str` | 10 | 43 | no | no | `Strong-Willed · of Destruction` |
| 4 | `Stre` | 0 | 0 | no | no |  |
| 4 | `gth$` | 0 | 0 | no | no |  |
| 4 | `ngth` | 0 | 0 | no | no |  |
| 4 | `to S` | 1 | 12 | no | no | `+10(10-15) to Spirit · +15(10-15) to Spirit` |
| 5 | `Stren` | 0 | 0 | no | no |  |
| 5 | `ength` | 0 | 0 | no | no |  |

## Strength — global

Targets: 22; foreign lines: 41006; current: `\) to Strength$`

### Strict (zero foreign, no cross-line)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 6 | `to Str` | 0 | 0 | no | no |  |
| 7 | `to Stre` | 0 | 0 | no | no |  |
| 8 | `to Stren` | 0 | 0 | no | no |  |
| 9 | `to Streng` | 0 | 0 | no | no |  |
| 10 | `o Strength` | 0 | 0 | no | no |  |

### Line-only (zero foreign; cross-line possible)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 6 | `to Str` | 0 | 0 | no | no |  |
| 6 | `to.*h$` | 0 | 0 | yes | no |  |
| 6 | `to.+h$` | 0 | 0 | yes | no |  |
| 7 | `to Stre` | 0 | 0 | no | no |  |
| 7 | `to.*gth` | 0 | 0 | yes | no |  |

### Shortest observed (collisions included)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 3 | `gth` | 6 | 32 | no | no | `10(7-10)% increased Strength · 7(7-10)% increased Strength` |
| 3 | `Str` | 206 | 1625 | no | no | `Requires: Level 77, 163 (unmet) Str · Requires: Level 70, 98 (unmet) Str, 72 Int` |
| 4 | `gth$` | 2 | 4 | no | no | `10(7-10)% increased Strength · 7(7-10)% increased Strength` |
| 4 | `ngth` | 6 | 32 | no | no | `10(7-10)% increased Strength · 7(7-10)% increased Strength` |
| 4 | `Stre` | 9 | 57 | no | no | `10(7-10)% increased Strength · 7(7-10)% increased Strength` |
| 4 | `to S` | 7 | 194 | no | no | `+1(1-24) to Spirit · +10(10-15) to Spirit` |
| 5 | `ngth$` | 2 | 4 | no | no | `10(7-10)% increased Strength · 7(7-10)% increased Strength` |
| 5 | `Stren` | 6 | 32 | no | no | `10(7-10)% increased Strength · 7(7-10)% increased Strength` |

## Dexterity — Amulet

Targets: 18; foreign lines: 3639; current: `\) to Dexterity$`

### Strict (zero foreign, no cross-line)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 4 | `Dext` | 0 | 0 | no | no |  |
| 4 | `to D` | 0 | 0 | no | no |  |
| 5 | `Dexte` | 0 | 0 | no | no |  |
| 5 | `erity` | 0 | 0 | no | no |  |
| 5 | `rity$` | 0 | 0 | no | no |  |

### Line-only (zero foreign; cross-line possible)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 4 | `Dext` | 0 | 0 | no | no |  |
| 4 | `to D` | 0 | 0 | no | no |  |
| 5 | `Dexte` | 0 | 0 | no | no |  |
| 5 | `erity` | 0 | 0 | no | no |  |
| 5 | `rity$` | 0 | 0 | no | no |  |

### Shortest observed (collisions included)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 3 | `Dex` | 1 | 1 | no | no | `Requires: Level 59, 44 Str, 44 Dex` |
| 3 | `ity` | 17 | 89 | no | no | `10(10-15)% increased Rarity of Items found · 10(6-10)% increased Rarity of Items found` |
| 4 | `Dext` | 0 | 0 | no | no |  |
| 4 | `to D` | 0 | 0 | no | no |  |
| 4 | `ity$` | 7 | 9 | no | no | `Grants Skill: Level 1 Trinity · Grants Skill: Level 10 Trinity` |
| 4 | `rity` | 5 | 20 | no | no | `10(10-15)% increased Rarity of Items found · 10(6-10)% increased Rarity of Items found` |
| 5 | `Dexte` | 0 | 0 | no | no |  |
| 5 | `erity` | 0 | 0 | no | no |  |

## Dexterity — global

Targets: 20; foreign lines: 41008; current: `\) to Dexterity$`

### Strict (zero foreign, no cross-line)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 5 | `to De` | 0 | 0 | no | no |  |
| 6 | `to Dex` | 0 | 0 | no | no |  |
| 7 | `to Dext` | 0 | 0 | no | no |  |
| 8 | `to Dexte` | 0 | 0 | no | no |  |
| 9 | `to Dexter` | 0 | 0 | no | no |  |

### Line-only (zero foreign; cross-line possible)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 5 | `to De` | 0 | 0 | no | no |  |
| 6 | `to Dex` | 0 | 0 | no | no |  |
| 7 | `to Dext` | 0 | 0 | no | no |  |
| 8 | `to Dexte` | 0 | 0 | no | no |  |
| 8 | `to.*rity` | 0 | 0 | yes | yes |  |

### Shortest observed (collisions included)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 3 | `ity` | 92 | 444 | no | no | `Rarity: Normal · Rarity: Magic` |
| 3 | `Dex` | 18 | 1116 | no | no | `Requires: Level 59, 44 Str, 44 Dex · 10(7-10)% increased Dexterity` |
| 4 | `to D` | 2 | 7 | no | no | `+0.3(0.3-0.4) metres to Dodge Roll distance · +0.4(0.3-0.4) metres to Dodge Roll distance` |
| 4 | `Dext` | 5 | 30 | no | no | `10(7-10)% increased Dexterity · 7(7-10)% increased Dexterity` |
| 4 | `ity$` | 23 | 30 | no | no | `+20% to Maximum Quality · 10(7-10)% increased Dexterity` |
| 4 | `rity` | 25 | 118 | no | no | `Rarity: Normal · Rarity: Magic` |
| 5 | `to De` | 0 | 0 | no | no |  |
| 5 | `rity$` | 7 | 11 | no | no | `10(7-10)% increased Dexterity · 7(7-10)% increased Dexterity` |

## Intelligence — Amulet

Targets: 18; foreign lines: 3639; current: `\) to Intelligence$`

### Strict (zero foreign, no cross-line)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 4 | `Inte` | 0 | 0 | no | no |  |
| 4 | `to I` | 0 | 0 | no | no |  |
| 5 | `Intel` | 0 | 0 | no | no |  |
| 5 | `to In` | 0 | 0 | no | no |  |
| 6 | `I..nce` | 0 | 0 | no | no |  |

### Line-only (zero foreign; cross-line possible)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 4 | `Inte` | 0 | 0 | no | no |  |
| 4 | `to I` | 0 | 0 | no | no |  |
| 5 | `Intel` | 0 | 0 | no | no |  |
| 5 | `to In` | 0 | 0 | no | no |  |
| 6 | `I...ce` | 0 | 0 | yes | no |  |

### Shortest observed (collisions included)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 3 | `Int` | 5 | 16 | no | no | `Scintillating · { Prefix Modifier "Scintillating" (Tier: 1) — Defences, Energy Shield }` |
| 3 | `nce` | 98 | 963 | no | no | `10(10-14)% increased Critical Hit Chance · 14(10-14)% increased Critical Hit Chance` |
| 4 | `Inte` | 0 | 0 | no | no |  |
| 4 | `to I` | 0 | 0 | no | no |  |
| 4 | `In.e` | 2 | 4 | yes | no | `Grants Skill: Level 1 Lingering Illusion · Grants Skill: Level 10 Lingering Illusion` |
| 4 | `nce$` | 16 | 92 | no | no | `10(10-14)% increased Critical Hit Chance · 14(10-14)% increased Critical Hit Chance` |
| 4 | `ence` | 42 | 411 | no | no | `Grants Skill: Convalescence · Grants Skill: Level 1 Convalescence` |
| 5 | `Intel` | 0 | 0 | no | no |  |

## Intelligence — global

Targets: 20; foreign lines: 41008; current: `\) to Intelligence$`

### Strict (zero foreign, no cross-line)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 6 | `to Int` | 0 | 0 | no | no |  |
| 7 | `to Inte` | 0 | 0 | no | no |  |
| 8 | `to Intel` | 0 | 0 | no | no |  |
| 9 | `to Intell` | 0 | 0 | no | no |  |
| 10 | `to Intelli` | 0 | 0 | no | no |  |

### Line-only (zero foreign; cross-line possible)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 6 | `to Int` | 0 | 0 | no | no |  |
| 7 | `to Inte` | 0 | 0 | no | no |  |
| 8 | `to Intel` | 0 | 0 | no | no |  |
| 9 | `to Intell` | 0 | 0 | no | no |  |
| 9 | `to.*ence$` | 0 | 0 | yes | no |  |

### Shortest observed (collisions included)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 3 | `Int` | 1397 | 2531 | no | no | `Requires: Level 36, 46 Int · Requires: Level 70, 98 (unmet) Str, 72 Int` |
| 3 | `nce` | 698 | 6714 | no | no | `Block chance: 32% · Block chance: 32% (augmented)` |
| 4 | `to I` | 3 | 3 | no | no | `Chance to Poison is calculated from your base chance to inflict Bleeding instead · Chance to inflict Bleeding is calculated from your base chance to Poison instead` |
| 4 | `Inte` | 105 | 251 | no | no | `1% increased Area of Effect for Attacks per 10 Intelligence · 10(7-10)% increased Intelligence` |
| 4 | `nce$` | 53 | 305 | no | no | `+0(0-1)% to Critical Hit Chance · +0.3(0.3-0.4) metres to Dodge Roll distance` |
| 4 | `In.e` | 186 | 458 | yes | no | `Item Class: Trinkets · 1% increased Area of Effect for Attacks per 10 Intelligence` |
| 4 | `ence` | 416 | 4764 | no | no | `1% increased Area of Effect for Attacks per 10 Intelligence · 10(7-10)% increased Intelligence` |
| 5 | `to In` | 3 | 3 | no | no | `Chance to Poison is calculated from your base chance to inflict Bleeding instead · Chance to inflict Bleeding is calculated from your base chance to Poison instead` |

## MovementVelocity — Boots

Targets: 9; foreign lines: 5584; current: `Movement Speed$`

### Strict (zero foreign, no cross-line)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 3 | `Mov` | 0 | 0 | no | no |  |
| 4 | `Move` | 0 | 0 | no | no |  |
| 4 | `eed$` | 0 | 0 | no | no |  |
| 5 | `Movem` | 0 | 0 | no | no |  |
| 5 | `peed$` | 0 | 0 | no | no |  |

### Line-only (zero foreign; cross-line possible)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 3 | `Mov` | 0 | 0 | no | no |  |
| 4 | `Move` | 0 | 0 | no | no |  |
| 4 | `eed$` | 0 | 0 | no | no |  |
| 5 | `Movem` | 0 | 0 | no | no |  |
| 5 | `peed$` | 0 | 0 | no | no |  |

### Shortest observed (collisions included)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 3 | `Mov` | 0 | 0 | no | no |  |
| 3 | `eed` | 8 | 85 | no | no | `{ Corrupted Modifier (Tier: 1) — Speed } · { Corrupted Modifier (Tier: 10) — Speed }` |
| 3 | `Spe` | 10 | 98 | no | no | `Spectre's · { Corrupted Modifier (Tier: 1) — Speed }` |
| 3 | `inc` | 33 | 240 | no | no | `100(92-100)% increased Armour · 13(6-13)% increased Armour` |
| 3 | `sed` | 39 | 279 | no | no | `100(92-100)% increased Armour · 13(6-13)% increased Armour` |
| 3 | `ent` | 121 | 911 | no | no | `10(20-10)% reduced Attribute Requirements · 15% reduced Attribute Requirements` |
| 4 | `Move` | 0 | 0 | no | no |  |
| 4 | `eed$` | 0 | 0 | no | no |  |

## MovementVelocity — global

Targets: 15; foreign lines: 41013; current: `(unresolved)`

### Strict (zero foreign, no cross-line)

None.

### Line-only (zero foreign; cross-line possible)

None.

### Shortest observed (collisions included)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 3 | `Mov` | 12 | 41 | no | no | `10% increased Movement Speed during any Flask Effect · 10(10-14)% increased Movement Speed while Sprinting` |
| 3 | `eed` | 124 | 1140 | no | no | `10% increased Attack and Cast Speed during any Flask Effect · 10% increased Movement Speed during any Flask Effect` |
| 3 | `Spe` | 376 | 1737 | no | no | `Item Class: Spears · +1 to Level of all Chaos Spell Skills` |
| 3 | `inc` | 249 | 2145 | no | no | `Item Class: Incursion Items · Item Class: Incubators` |
| 3 | `sed` | 257 | 2149 | no | no | `1% increased Area of Effect for Attacks per 10 Intelligence · 1(1-2)% increased Projectile Damage per Power Charge` |
| 3 | `ent` | 1302 | 6559 | no | no | `Requires: Level 62, 38 (augmented) Str, 38 (augmented) Int · Armour: 138 (augmented)` |
| 4 | `Move` | 10 | 28 | no | no | `10% increased Movement Speed during any Flask Effect · 10(10-14)% increased Movement Speed while Sprinting` |
| 4 | `eed$` | 19 | 163 | no | no | `11(11-20)% increased Projectile Speed · 12(9-12)% increased Cast Speed` |

## LocalIncreasedAttackSpeed — Bow

Targets: 12; foreign lines: 2570; current: `Attack Speed$`

### Strict (zero foreign, no cross-line)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 4 | `eed$` | 0 | 0 | no | no |  |
| 5 | `peed$` | 0 | 0 | no | no |  |
| 6 | `S.eed$` | 0 | 0 | no | no |  |
| 6 | `Spe.d$` | 0 | 0 | no | no |  |
| 6 | `Speed$` | 0 | 0 | no | no |  |

### Line-only (zero foreign; cross-line possible)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 4 | `eed$` | 0 | 0 | no | no |  |
| 5 | `peed$` | 0 | 0 | no | no |  |
| 6 | `S..ed$` | 0 | 0 | yes | no |  |
| 6 | `S.eed$` | 0 | 0 | no | no |  |
| 6 | `Sp.*d$` | 0 | 0 | yes | no |  |

### Shortest observed (collisions included)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 3 | `inc` | 5 | 64 | no | no | `100(87-100)% increased Elemental Damage with Attacks · 19(19-35)% increased Elemental Damage with Attacks` |
| 3 | `sed` | 5 | 64 | no | no | `100(87-100)% increased Elemental Damage with Attacks · 19(19-35)% increased Elemental Damage with Attacks` |
| 3 | `eed` | 6 | 72 | no | no | `{ Corrupted Modifier (Tier: 1) — Attack, Speed } · { Corrupted Modifier (Tier: 10) — Attack, Speed }` |
| 3 | `Spe` | 8 | 85 | no | no | `of the Desperate · { Corrupted Modifier (Tier: 1) — Attack, Speed }` |
| 3 | `ack` | 122 | 1415 | no | no | `100(87-100)% increased Elemental Damage with Attacks · 19(19-35)% increased Elemental Damage with Attacks` |
| 3 | `Att` | 130 | 1517 | no | no | `100(87-100)% increased Elemental Damage with Attacks · 19(19-35)% increased Elemental Damage with Attacks` |
| 4 | `eed$` | 0 | 0 | no | no |  |
| 4 | `ased` | 3 | 51 | no | no | `100(87-100)% increased Elemental Damage with Attacks · 19(19-35)% increased Elemental Damage with Attacks` |

## LocalIncreasedAttackSpeed — global

Targets: 24; foreign lines: 41004; current: `^\d+\(\d+-\d+\)% increased Attack Speed$`

### Strict (zero foreign, no cross-line)

None.

### Line-only (zero foreign; cross-line possible)

None.

### Shortest observed (collisions included)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 3 | `eed` | 124 | 1131 | no | no | `10% increased Attack and Cast Speed during any Flask Effect · 10% increased Movement Speed during any Flask Effect` |
| 3 | `Spe` | 376 | 1728 | no | no | `Item Class: Spears · +1 to Level of all Chaos Spell Skills` |
| 3 | `inc` | 249 | 2136 | no | no | `Item Class: Incursion Items · Item Class: Incubators` |
| 3 | `sed` | 257 | 2140 | no | no | `1% increased Area of Effect for Attacks per 10 Intelligence · 1(1-2)% increased Projectile Damage per Power Charge` |
| 3 | `ack` | 571 | 3553 | no | no | `Attacks per Second: 1.45 · Item Class: Stackable Currency` |
| 3 | `Att` | 429 | 3939 | no | no | `Attacks per Second: 1.45 · +1(1-2)% chance to Block Projectile Attack Damage` |
| 4 | `eed$` | 19 | 154 | no | no | `11(11-20)% increased Projectile Speed · 12(9-12)% increased Cast Speed` |
| 4 | `Spee` | 98 | 944 | no | no | `10% increased Attack and Cast Speed during any Flask Effect · 10% increased Movement Speed during any Flask Effect` |

## LocalBaseCriticalStrikeChance — Bow

Targets: 10; foreign lines: 2572; current: `Hit Chance$`

### Strict (zero foreign, no cross-line)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 6 | `H.nce$` | 0 | 0 | no | no |  |
| 6 | `hance$` | 0 | 0 | no | no |  |
| 7 | `C..nce$` | 0 | 0 | no | no |  |
| 7 | `C.ance$` | 0 | 0 | no | no |  |
| 7 | `Ch.nce$` | 0 | 0 | no | no |  |

### Line-only (zero foreign; cross-line possible)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 6 | `C.*ce$` | 0 | 0 | yes | no |  |
| 6 | `C.+ce$` | 0 | 0 | yes | no |  |
| 6 | `H.*ce$` | 0 | 0 | yes | no |  |
| 6 | `H.+ce$` | 0 | 0 | yes | no |  |
| 6 | `H..ce$` | 0 | 0 | yes | no |  |

### Shortest observed (collisions included)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 3 | `Hit` | 5 | 12 | no | no | `10(5-10)% chance to Blind Enemies on hit · 5(5-10)% chance to Blind Enemies on hit` |
| 3 | `nce` | 11 | 35 | no | no | `10(5-10)% chance to Blind Enemies on hit · 5(5-10)% chance to Blind Enemies on hit` |
| 3 | `Cha` | 15 | 62 | no | no | `10(5-10)% chance to Blind Enemies on hit · 5(5-10)% chance to Blind Enemies on hit` |
| 3 | `Cri` | 16 | 174 | no | no | `Critical Hit Chance: 10% · Critical Hit Chance: 5%` |
| 3 | `cal` | 58 | 685 | no | no | `109(85-109)% increased Physical Damage · 110(110-134)% increased Physical Damage` |
| 4 | `nce$` | 1 | 1 | no | no | `of Radiance` |
| 4 | `to C` | 2 | 16 | no | no | `25(25-35)% chance to Chain an additional time · 35(25-35)% chance to Chain an additional time` |
| 4 | `Chan` | 9 | 22 | no | no | `10(5-10)% chance to Blind Enemies on hit · 5(5-10)% chance to Blind Enemies on hit` |

## LocalBaseCriticalStrikeChance — global

Targets: 12; foreign lines: 41016; current: `% to Critical Hit Chance$`

### Strict (zero foreign, no cross-line)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 13 | `to Critical H` | 0 | 0 | no | no |  |
| 14 | `o Critical Hit` | 0 | 0 | no | no |  |
| 14 | `to Critical Hi` | 0 | 0 | no | no |  |
| 15 | `to Critical Hit` | 0 | 0 | no | no |  |
| 15 | `to Critical...t` | 0 | 0 | no | no |  |

### Line-only (zero foreign; cross-line possible)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 13 | `to Critical H` | 0 | 0 | no | no |  |
| 14 | `o Critical Hit` | 0 | 0 | no | no |  |
| 14 | `to Critical Hi` | 0 | 0 | no | no |  |
| 15 | `to Critical Hit` | 0 | 0 | no | no |  |
| 15 | `to Critical.*ce` | 0 | 0 | yes | no |  |

### Shortest observed (collisions included)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 3 | `Hit` | 61 | 290 | no | no | `Critical Hit Chance: 5.00% · +4(4-5)% to Fire Spell Critical Hit Chance` |
| 3 | `Cri` | 179 | 1287 | no | no | `Critical Hit Chance: 5.00% · Item Class: Inscribed Ultimatum` |
| 3 | `Cha` | 380 | 2407 | no | no | `Block chance: 32% · Block chance: 32% (augmented)` |
| 3 | `cal` | 443 | 3289 | no | no | `Physical Damage: 35-59 · Physical Damage: 93-159 (augmented)` |
| 3 | `nce` | 698 | 6722 | no | no | `Block chance: 32% · Block chance: 32% (augmented)` |
| 4 | `to C` | 20 | 130 | no | no | `+10(10-13)% to Chaos Resistance · +11(11-15)% to Chaos Damage over Time Multiplier` |
| 4 | `nce$` | 53 | 313 | no | no | `+0.3(0.3-0.4) metres to Dodge Roll distance · +0.4(0.3-0.4) metres to Dodge Roll distance` |
| 4 | `Chan` | 91 | 510 | no | no | `Block chance: 32% · Block chance: 32% (augmented)` |

## LocalAccuracyRating — Bow

Targets: 42; foreign lines: 2540; current: `Accuracy Rating$`

### Strict (zero foreign, no cross-line)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 3 | `acy` | 0 | 0 | no | no |  |
| 4 | `Accu` | 0 | 0 | no | no |  |
| 4 | `racy` | 0 | 0 | no | no |  |
| 4 | `to A` | 0 | 0 | no | no |  |
| 5 | `Accur` | 0 | 0 | no | no |  |

### Line-only (zero foreign; cross-line possible)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 3 | `acy` | 0 | 0 | no | no |  |
| 4 | `Accu` | 0 | 0 | no | no |  |
| 4 | `R.cy` | 0 | 0 | yes | no |  |
| 4 | `Ra.y` | 0 | 0 | yes | no |  |
| 4 | `racy` | 0 | 0 | no | no |  |

### Shortest observed (collisions included)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 3 | `acy` | 0 | 0 | no | no |  |
| 3 | `Acc` | 2 | 13 | no | no | `of Acclaim · { Suffix Modifier "of Acclaim" (Tier: 1) — Attack, Speed }` |
| 3 | `Rat` | 18 | 73 | no | no | `Burnished Obliterator Bow of the Mongoose · Deliberate` |
| 3 | `ing` | 192 | 661 | no | no | `Arcing · Blasting` |
| 4 | `Accu` | 0 | 0 | no | no |  |
| 4 | `R.cy` | 0 | 0 | yes | no |  |
| 4 | `Ra.y` | 0 | 0 | yes | no |  |
| 4 | `racy` | 0 | 0 | no | no |  |

## LocalAccuracyRating — global

Targets: 50; foreign lines: 40978; current: `^\+\d+\(\d+-\d+\) to Accuracy`

### Strict (zero foreign, no cross-line)

None.

### Line-only (zero foreign; cross-line possible)

None.

### Shortest observed (collisions included)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 3 | `acy` | 5 | 23 | no | no | `Allies in your Presence have +11(11-32) to Accuracy Rating · Allies in your Presence have +123(85-123) to Accuracy Rating` |
| 3 | `Acc` | 12 | 83 | no | no | `Allies in your Presence have +11(11-32) to Accuracy Rating · Allies in your Presence have +123(85-123) to Accuracy Rating` |
| 3 | `Rat` | 221 | 1502 | no | no | `Evasion Rating: 117 · Evasion Rating: 117 (augmented)` |
| 3 | `ing` | 2805 | 8081 | no | no | `Evasion Rating: 117 · Evasion Rating: 117 (augmented)` |
| 4 | `R.cy` | 5 | 23 | yes | no | `Allies in your Presence have +11(11-32) to Accuracy Rating · Allies in your Presence have +123(85-123) to Accuracy Rating` |
| 4 | `racy` | 5 | 23 | no | no | `Allies in your Presence have +11(11-32) to Accuracy Rating · Allies in your Presence have +123(85-123) to Accuracy Rating` |
| 4 | `Accu` | 5 | 32 | no | no | `Allies in your Presence have +11(11-32) to Accuracy Rating · Allies in your Presence have +123(85-123) to Accuracy Rating` |
| 4 | `Ra.y` | 12 | 32 | yes | no | `Allies in your Presence have +11(11-32) to Accuracy Rating · Allies in your Presence have +123(85-123) to Accuracy Rating` |

## ManaRegeneration — Ring

Targets: 20; foreign lines: 3412; current: `Mana Regeneration Rate$`

### Strict (zero foreign, no cross-line)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 6 | `Mana R` | 0 | 0 | no | no |  |
| 6 | `d Mana` | 0 | 0 | no | no |  |
| 7 | `Mana Re` | 0 | 0 | no | no |  |
| 7 | `ed Mana` | 0 | 0 | no | no |  |
| 8 | `Mana Reg` | 0 | 0 | no | no |  |

### Line-only (zero foreign; cross-line possible)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 6 | `Mana R` | 0 | 0 | no | no |  |
| 6 | `d Mana` | 0 | 0 | no | no |  |
| 7 | `M.*ate$` | 0 | 0 | yes | no |  |
| 7 | `M.+ate$` | 0 | 0 | yes | no |  |
| 7 | `Ma.*ate` | 0 | 0 | yes | no |  |

### Shortest observed (collisions included)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 3 | `ate` | 7 | 41 | no | no | `15(15-25)% increased Life Regeneration rate · 25(15-25)% increased Life Regeneration rate` |
| 3 | `Rat` | 10 | 80 | no | no | `1(1-2) Life Regeneration per second · 13(9.1-13) Life Regeneration per second` |
| 3 | `Reg` | 9 | 100 | no | no | `1(1-2) Life Regeneration per second · 13(9.1-13) Life Regeneration per second` |
| 3 | `inc` | 15 | 106 | no | no | `10(7-10)% increased Cast Speed · 12(9-12)% increased Cast Speed` |
| 3 | `sed` | 17 | 119 | no | no | `10(7-10)% increased Cast Speed · 12(9-12)% increased Cast Speed` |
| 3 | `ion` | 31 | 249 | no | no | `1(1-2) Life Regeneration per second · 13(9.1-13) Life Regeneration per second` |
| 3 | `ana` | 36 | 408 | no | no | `4(4-6)% increased maximum Mana · 6(4-6)% increased maximum Mana` |
| 3 | `Man` | 37 | 420 | no | no | `4(4-6)% increased maximum Mana · 6(4-6)% increased maximum Mana` |

## ManaRegeneration — global

Targets: 36; foreign lines: 40992; current: `Mana Regeneration Rate$`

### Strict (zero foreign, no cross-line)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 12 | `Man.+Reg.*e$` | 0 | 0 | no | no |  |
| 12 | `Man.+Reg.+e$` | 0 | 0 | no | no |  |
| 12 | `Man.+i.*ate$` | 0 | 0 | no | no |  |
| 12 | `Man.+i.+ate$` | 0 | 0 | no | no |  |
| 12 | `Mana.*i.*te$` | 0 | 0 | no | no |  |

### Line-only (zero foreign; cross-line possible)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 11 | `M.*Rege.*e$` | 0 | 0 | yes | yes |  |
| 11 | `M.*Rege.+e$` | 0 | 0 | yes | yes |  |
| 11 | `M.+Rege.*e$` | 0 | 0 | yes | yes |  |
| 11 | `M.+Rege.+e$` | 0 | 0 | yes | yes |  |
| 12 | `M.*Ra.*Rate$` | 0 | 0 | yes | no |  |

### Shortest observed (collisions included)

| len | fragment | foreign patterns | foreign lines | cross synthetic | cross captured | examples |
|---:|---|---:|---:|---|---|---|
| 3 | `Reg` | 74 | 326 | no | no | `15(15-20)% increased Runic Ward Regeneration Rate · 20(15-20)% increased Runic Ward Regeneration Rate` |
| 3 | `ate` | 556 | 1014 | no | no | `11(11-15)% increased Flask Mana Recovery rate · 11(8-11)% increased Flask Life Recovery rate` |
| 3 | `Rat` | 221 | 1516 | no | no | `Evasion Rating: 117 · Evasion Rating: 117 (augmented)` |
| 3 | `inc` | 249 | 2124 | no | no | `Item Class: Incursion Items · Item Class: Incubators` |
| 3 | `sed` | 257 | 2128 | no | no | `1% increased Area of Effect for Attacks per 10 Intelligence · 1(1-2)% increased Projectile Damage per Power Charge` |
| 3 | `ana` | 213 | 2171 | no | no | `Item Class: Mana Flasks · +110(110-114) to maximum Mana` |
| 3 | `Man` | 672 | 2788 | no | no | `Item Class: Mana Flasks · Item Class: Talismans` |
| 3 | `ion` | 666 | 5489 | no | no | `Evasion Rating: 117 · Evasion Rating: 117 (augmented)` |



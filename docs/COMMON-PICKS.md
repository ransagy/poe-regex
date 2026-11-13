# Common picks

The pre-typing picker shortlist lives in
[`data/common-picks.json`](../data/common-picks.json). The same file is shared
by both game profiles: entries resolve by family key or visible display text,
so a PoE2 pick with a close PoE1 equivalent also appears in the PoE1 picker.
Add or change entries there instead of editing a hardcoded family-key list.

## Entry shape

```json
{
  "label": "Minion skill levels",
  "family": "GlobalIncreaseMinionSpellSkillGemLevel",
  "classes": ["Helmet"]
}
```

| Field | Meaning |
| --- | --- |
| `label` | Human-readable name used in reports and contributor docs. Required. |
| `family` | Exact RePoE family key. Use this when a visible line is shared by more than one family. |
| `match` | Exact family display text, with numbers omitted (for example `Adds to Fire damage to Attacks`). A contributor-friendly alternative to `family`. |
| `classes` | Optional runtime scope. When present, the pick applies only in those class views and is omitted from All gear/global. Entries without `classes` are global. |
| `note` | Optional contributor note. |

At least one of `family` or `match` is required. When both are present, `family`
wins at runtime. A class-scoped entry is absent from the All-gear shortlist, so
global picks should use the normal variants (for weapons, the local
`LocalPhysicalDamage` / `LocalFireDamage` / `LocalColdDamage` /
`LocalLightningDamage` families).

## Current shortlist

<!-- common-picks:start -->
_Generated from [`data/common-picks.json`](../data/common-picks.json) by `common-picks --table`. Do not edit by hand._

| Pick | Data file | Scope | Resolved family |
| --- | --- | --- | --- |
| + to maximum Life | `IncreasedLife` | All gear | `IncreasedLife` |
| + to maximum Mana | `IncreasedMana` | All gear | `IncreasedMana` |
| % increased Physical Damage | `LocalPhysicalDamagePercent` | All gear | `LocalPhysicalDamagePercent` |
| Adds Physical Damage | `LocalPhysicalDamage` | All gear | `LocalPhysicalDamage` |
| Adds Fire Damage | `LocalFireDamage` | All gear | `LocalFireDamage` |
| Adds Cold Damage | `LocalColdDamage` | All gear | `LocalColdDamage` |
| Adds Lightning Damage | `LocalLightningDamage` | All gear | `LocalLightningDamage` |
| + to maximum Energy Shield | `LocalEnergyShield` | All gear | `LocalEnergyShield` |
| + to Armour | `LocalPhysicalDamageReductionRating` | All gear | `LocalPhysicalDamageReductionRating` |
| + to Evasion Rating | `LocalEvasionRating` | All gear | `LocalEvasionRating` |
| + to Spirit | `BaseSpirit` | All gear | `BaseSpirit` |
| +% to Fire Resistance | `FireResistance` | All gear | `FireResistance` |
| +% to Cold Resistance | `ColdResistance` | All gear | `ColdResistance` |
| +% to Lightning Resistance | `LightningResistance` | All gear | `LightningResistance` |
| +% to Chaos Resistance | `ChaosResistance` | All gear | `ChaosResistance` |
| + to Strength | `Strength` | All gear | `Strength` |
| + to Dexterity | `Dexterity` | All gear | `Dexterity` |
| + to Intelligence | `Intelligence` | All gear | `Intelligence` |
| % increased Movement Speed | `MovementVelocity` | All gear | `MovementVelocity` |
| % increased Attack Speed | `LocalIncreasedAttackSpeed` | All gear | `LocalIncreasedAttackSpeed` |
| +% to Critical Hit Chance | `LocalBaseCriticalStrikeChance` | All gear | `LocalBaseCriticalStrikeChance` |
| + to Accuracy Rating | `LocalAccuracyRating` | All gear | `LocalAccuracyRating` |
| % increased Mana Regeneration Rate | `ManaRegeneration` | All gear | `ManaRegeneration` |
| Life Regeneration | `LifeRegeneration` | All gear | `LifeRegeneration` |
| % increased Cast Speed | `IncreasedCastSpeed` | All gear | `IncreasedCastSpeed` |
| % increased Spell Damage | `SpellDamage` | All gear | `SpellDamage` |
| Minion skill levels | `GlobalIncreaseMinionSpellSkillGemLevel` | Helmet | `GlobalIncreaseMinionSpellSkillGemLevel` |
| Added physical damage | match: "Adds to Physical Damage to Attacks" | Gloves | `PhysicalDamage` |
| Added fire damage | match: "Adds to Fire damage to Attacks" | Gloves | `FireDamage` |
| Added cold damage | match: "Adds to Cold damage to Attacks" | Gloves | `ColdDamage` |
| Added lightning damage | match: "Adds to Lightning damage to Attacks" | Gloves | `LightningDamage` |
<!-- common-picks:end -->

## Validate before opening a PR

```sh
dotnet run --project tools/PoeRegex.Data.Build --configuration Release -- \
  common-picks --check
```

The check resolves every entry, verifies the family exists and compiles for each
listed class, keeps the shortlist under its size cap, and reports ambiguous
matches. It also fails when the generated table above drifts from the data
file. The `ci` workflow runs the same command on every pull request.

Regenerate the table after editing the JSON, then commit both files:

```sh
dotnet run --project tools/PoeRegex.Data.Build --configuration Release -- \
  common-picks --table
```

## Current examples

- `Minion skill levels` for Helmet (class-scoped).
- `Added physical/fire/cold/lightning damage` for Gloves (attack-only,
  class-scoped, so absent from All gear).
- Normal weapon-local added damage for physical/fire/cold/lightning in the
  global/All-gear shortlist.
- The original life, mana, resistance, attribute, movement, accuracy, and
  critical-chance baseline entries.

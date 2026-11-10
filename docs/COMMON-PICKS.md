# Common picks

The pre-typing picker shortlist lives in
[`data/common-picks.json`](../data/common-picks.json). Add or change entries
there instead of editing a hardcoded family-key list.

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

## Validate before opening a PR

```sh
dotnet run --project tools/PoeRegex.Data.Build --configuration Release -- \
  common-picks --check
```

The check resolves every entry, verifies the family exists and compiles for each
listed class, keeps the shortlist under its size cap, and reports ambiguous
matches. The `ci` workflow runs the same command on every pull request.

## Current examples

- `Minion skill levels` for Helmet (class-scoped).
- `Added physical/fire/cold/lightning damage` for Gloves (attack-only,
  class-scoped, so absent from All gear).
- Normal weapon-local added damage for physical/fire/cold/lightning in the
  global/All-gear shortlist.
- The original life, mana, resistance, attribute, movement, accuracy, and
  critical-chance baseline entries.

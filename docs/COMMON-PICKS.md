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
| `classes` | Optional classes used by validation and documentation. The runtime list still applies wherever the family exists. |
| `note` | Optional contributor note. |

At least one of `family` or `match` is required. When both are present, `family`
wins at runtime.

## Validate before opening a PR

```sh
dotnet run --project tools/PoeRegex.Data.Build --configuration Release -- \
  common-picks --check
```

The check resolves every entry, verifies the family exists and compiles for each
listed class, keeps the shortlist under its size cap, and reports ambiguous
matches. The `ci` workflow runs the same command on every pull request.

## Current examples

- `Minion skill levels` for Helmet.
- `Added physical/fire/cold/lightning damage` for Gloves.
- The original life, mana, resistance, attribute, movement, accuracy, and
  critical-chance baseline entries.

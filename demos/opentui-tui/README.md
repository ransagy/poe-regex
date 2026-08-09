# OpenTUI demo for poe-regex

An interactive terminal UI that drives the poe-regex engine through its JSON CLI
subprocess contract (see `docs/USING-THE-ENGINE.md` §12). It mirrors the UI
blueprint in the original spec (§7): game toggle, base-equipment select,
explicit modifier include/exclude, compiled output strings, copy-to-clipboard,
and a live capacity meter.

Built with [OpenTUI](https://github.com/anomalyco/opentui) (Zig core + TS
bindings) running under Node with the experimental FFI flag.

## Prerequisites

- **Node.js ≥ 26.4** (the OpenTUI native module requires `--experimental-ffi`).
- **A poe-regex CLI binary.** Either:
  - publish one with `npm run build:cli` (needs a .NET SDK + the Native AOT
    toolchain, see `docs/BUILDING.md`), which writes `PoeRegex.Cli` into the
    repo's `out/` directory, or
  - set `POE_REGEX_BIN` to an existing binary (e.g. a downloaded release
    binary). This takes priority over `out/PoeRegex.Cli`.
- **python3** — only needed for the `test:smoke` integration check.

## Setup

```sh
cd demos/opentui-tui
npm install
npm run build:cli   # or: export POE_REGEX_BIN=/path/to/PoeRegex.Cli
```

## Run

```sh
npm run dev
```

The layout:

- **Left column** — `SYSTEM ENGINE MODE` (PoE1/PoE2), `MODE`
  (single/global/multi), `BASE EQUIPMENT`, the mod typeahead (`MOD` input +
  suggestions), and the `INCLUDED` / `EXCLUDED` chip lists.
- **Right column** — `COMPILED OUTPUT STRINGS` (one query per active slot),
  the payload capacity meter `[████░░░░░░] 26/250` (green → orange → red), a
  status line, and the `explain` panel.

### Controls

| Key | Action |
|---|---|
| `Tab` / `Shift+Tab` | cycle focus between widgets |
| `Up` / `Down` | move the highlight within the focused list |
| type in `MOD` | filter the mod suggestions (client-side, case-insensitive); the filter is kept as you add mods |
| `Enter` (in `MOD` / suggestions) | include the first matching mod (or the highlighted one); if none matches, the typed text is included as a free-text pattern |
| `x` (in `MOD` / suggestions) | exclude the first matching mod (or the highlighted one) |
| `a`, or select the `[select all N]` row | include every currently-filtered, not-yet-chosen mod and clear the filter |
| `Enter` on an included/excluded chip | remove that chip |
| `Enter` / `Space` on a class (multi mode) | toggle that base-equipment slot |
| `c` | copy the compiled query/query-list (system clipboard tool, then OSC52) |
| `e` | explain the current query in the panel (tokens + candidate mods) |
| `g` | toggle PoE1 ↔ PoE2 |
| `q` / `Ctrl+C` | quit |

The footer bar shows these controls. The letter shortcuts (`x`/`a`/`c`/`e`/`g`)
are typed as text instead while you're in the `MOD` field — move focus with
`Tab` first.

### Modes

- **single** — one base-equipment slot; the `BASE EQUIPMENT` highlight picks
  the slot and regenerates live.
- **global** — no class anchor; the `BASE EQUIPMENT` row is hidden and the
  typeahead searches the whole game corpus; the output is a single classless
  query.
- **multi** — each selected base-equipment becomes a slot (`Enter`/`Space`
  toggles `[x]`); the typeahead searches the union of mods across selected
  slots and the output pane lists one query per slot.

The `BASE EQUIPMENT` list shows only classes that actually have explicit mods
in the catalog (flasks, currency, gems, relics, etc. are omitted; the default
slot is `Ring`).

## Tests

```sh
npm test            # unit tests (engine wrapper, app state, catalog)
npm run test:smoke  # PTY integration check: renders, filters, includes/excludes,
                    # select-all, explains, mode/global/multi, clean exit
```

`test:smoke` skips with a message when no CLI binary is available; run it after
`npm run build:cli` (or with `POE_REGEX_BIN` set).

## Troubleshooting

- **"poe-regex CLI binary not found"** at startup — run `npm run build:cli`
  first, or set `POE_REGEX_BIN`.
- **Clipboard says "clipboard unavailable"** — no `wl-copy`/`xclip`/`xsel`/
  `pbcopy`/`clip` on PATH and the terminal does not support OSC52; copying is
  best-effort, the output pane still shows the query.
- **Empty typeahead for a class** — some base equipments have no explicit mods
  in the catalog; the `BASE EQUIPMENT` list hides them (only classes with mods
  are shown, defaulting to `Ring`).

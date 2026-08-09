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
| type in `MOD` | filter the mod suggestions (client-side, case-insensitive) |
| `Enter` | include the highlighted suggestion; if no suggestion matches, the typed text is included as a free-text pattern |
| `x` | exclude the highlighted suggestion (or typed free text) instead |
| `Enter` on an included/excluded chip | remove that chip |
| `Enter` on a class (multi mode) | toggle that base-equipment slot |
| `Up` / `Down` | move the selection highlight inside a list |
| `c` | copy the compiled query/query-list (system clipboard tool, then OSC52) |
| `e` | explain the current query in the panel (tokens + candidate mods) |
| `g` | toggle PoE1 ↔ PoE2 |
| `q` / `Ctrl+C` | quit |

The footer bar shows these controls. `c`/`e`/`g`/`x` are ignored while you are
typing in the `MOD` field (they are typed as text instead) — move focus with
`Tab` first.

### Modes

- **single** — one base-equipment slot; the `BASE EQUIPMENT` highlight picks
  the slot and regenerates live.
- **global** — no class anchor; the typeahead searches the whole game corpus
  and the output is a single classless query.
- **multi** — each selected base-equipment becomes a slot; the output pane
  lists one query per slot.

## Tests

```sh
npm test            # 19 unit tests (engine wrapper + app state)
npm run test:smoke  # PTY integration check: boots the TUI, types, includes,
                    # excludes, explains, toggles game, verifies clean exit
```

`test:smoke` skips with a message when no CLI binary is available; run it after
`npm run build:cli` (or with `POE_REGEX_BIN` set).

## Troubleshooting

- **"poe-regex CLI binary not found"** at startup — run `npm run build:cli`
  first, or set `POE_REGEX_BIN`.
- **Clipboard says "clipboard unavailable"** — no `wl-copy`/`xclip`/`xsel`/
  `pbcopy`/`clip` on PATH and the terminal does not support OSC52; copying is
  best-effort, the output pane still shows the query.
- **Empty typeahead for a class** — some base equipments (e.g. `AbyssJewel`)
  have no explicit mods in the catalog; pick another base equipment (the demo
  defaults to `Ring`, which has a full mod list).

# Project Plan & Work Log

Persistent working plan for the PoE regex compiler project. Update this document
as work progresses so it can be resumed after interruptions.

## Status

Repo: `https://github.com/ransagy/poe-regex` (private), branch `main`.
Baseline: commit `91a147a` (CLI subprocess API). All prior phases (AOT releases,
release workflow, docs) are complete and pushed.

## Current objective

Reversibility guarantee + spec-concept TUI demo.

- **Phase 1 — Engine + CLI:** every query the engine produces must decompose back
  to a candidate set that includes the mods it was built from (recall = 1),
  ambiguity surfaced as candidate options. Includes a real bug fix (digit
  placeholder leaking into queries) and a new global (no-class) mode.
- **Phase 2 — TUI demo:** OpenTUI (Zig core, TS bindings) driving the CLI as a
  subprocess, mirroring the original spec §7 layout plus slot modes
  (single/global/multi) and a mod typeahead with autocomplete.
- **Phase 3 — Docs + CI:** document `explain`, extend smoke-test JSON assertions.

## Decisions (confirmed by maintainer)

- Fix the digit-placeholder bug in this pass.
- Explain matching is case-insensitive; candidate semantics = AND-intersection.
- TUI lives in-repo under `demos/opentui-tui/`.
- Multi-slot selection emits a per-slot query list (no experimental class
  alternation).
- Global (no slot) mode resolves catalog mods via union-corpus fingerprints.
- Implementation order: engine+CLI first, TUI second.
- TUI runtime: Node 26.4 + `--experimental-ffi` (already installed; no Bun).
- De-risk OpenTUI with a hello-world spike before the full layout.
- PoE2 50-char limit / lookahead policy divergences: OUT OF SCOPE (in-game
  verify items per DESIGN §9).

## Phase 1 — Engine + CLI (in progress)

- [x] README: AI-disclosure note (bottom).
- [x] This plan doc.
- [x] 1.1 `SuffixIndex`: shortest unique substring that avoids the digit
      placeholder (prefer placeholder-free even if longer).
- [x] 1.1 `CompactionEngine.SelectFragment`: placeholder-free candidate first,
      else placeholder->digit-regex + oracle validation, else
      `unresolved-modifier` warning. Regression test + catalog sweep test
      (no control char in any generated query). ALSO fixed a second latent bug
      the sweep caught: plain fingerprints could leak unescaped regex
      metacharacters (e.g. PoE2 `" +"`); plain fragments and the class anchor
      are now escaped. (commit: Phase-1 branch, not yet pushed)
- [x] 1.2 `EngineCorpus`: per-game union suffix index over all class patterns
      (runtime, no catalog regen); `SelectFragment` null-class path uses it
      (global mode resolves catalog mods).
- [x] 1.3 `Explainer` (`src/PoeRegex.Core/Explain/Explainer.cs`): quote-aware
      tokenizer, placeholder wildcard, case-insensitive matching, token kinds
      (included/excluded/class/keyword); candidate mods = matched-class mods
      matched by EVERY included token minus excluded-token matches; reverse
      pattern->mods index in `EngineCorpus`. Class anchors are tokens that match
      a class line but no mod line in scope (mod text takes precedence, so a
      mod phrase like "ex" that also appears in a class name is not mistaken for
      an anchor).
- [x] 1.4 CLI `explain --game <g> [--catalog <p>] [--class <id>] --query
      "<regex>" [--json]` -> `{query, game, classes[], tokens[], mods[]}`;
      source-gen records; same stdout/stderr/exit-code contract; help text.
- [x] 1.5 Reversibility test suite: exhaustive class x mod (recall=1 for both
      games), global sample, seeded multi-mod, thresholds, exclusions,
      determinism. 66 tests green (was 45).
- [x] 1.6 Full build + test run (Debug + Release, 66 tests green, 0 warnings);
      AOT/JIT parity verified for `explain`/`generate`/`validate`/`list-mods`/
      `list-classes` --json on both games (identical stdout + exit codes).
- [x] 1.7 Commit + push Phase 1 (commit `851d4ec`, main).

## Phase 1 — additional discovered fix (in this pass)

- [x] 1.8 Case-insensitive fingerprints: generated fragments were unique only
      under case-SENSITIVE corpus matching, but the game matches
      case-insensitively. Class anchors like PoE2 Belt's `"I"` collided with mod
      text and broke reversibility for 342 PoE2 pairs. Fix: `SuffixIndex.Build`
      gained a case-insensitive mode (folded corpus, fingerprints returned in
      original case); `EngineCorpus` builds folded per-class + union indexes and
      `Fingerprint`/`GlobalFingerprint`/`ClassFingerprint` gained a
      `caseInsensitive` path used by the compiler; the regex oracle
      (`RegexSafeOnClass`/`GlobalRegexSafe`) is now case-insensitive. Recall = 1
      across all pairs for both games (was 342 fails on PoE2).

## Phase 2 — OpenTUI demo (`demos/opentui-tui/`)

- [x] 2.1 Spike: hello-world `createCliRenderer` under Node 26.4 +
      `--experimental-ffi`; validated @opentui/core@0.5.1 API (Text/Box/Input/
      Select render + clean exit); pinned version; `demos/opentui-tui/` scaffold
      (package.json scripts, .gitignore, spike.mjs).
- [x] 2.2 Subprocess wrapper implementing the CLI contract (child_process +
      JSON parse + exit codes): `src/engine.mjs` (`resolveBinary`, `run`,
      `parseJson`, `listClasses`/`listMods`/`generate`/`explain`/`validate`);
      `build:cli` publishes the AOT binary into `out/`.
- [x] 2.3 Layout per spec §7: game toggle; slot modes (Single dropdown /
      Global / Multi checkbox -> per-slot query list); mod typeahead (Input +
      suggestions, client-side filter, Enter->Included / Tab->Excluded chips);
      live generate preview; clipboard (system tools + OSC52 fallback);
      capacity meter (green->orange->red); explain panel (tokens + candidate
      mods). Renderables are constructed directly (`new XRenderable(renderer,
      ...)`) because the VNode factory proxies only replay queued calls at
      materialization time. Enter is reported as `key.name === "return"` by the
      keypress parser. Default base equipment is Ring (the alphabetically-first
      class, AbyssJewel, has no explicit mods -> empty typeahead).
- [x] 2.4 Logic tests with `node --test` (18 green: 9 engine wrapper incl.
      `--limit`, 9 app state incl. `selectClass`/`generateTasks`).
- [x] 2.5 Manual terminal verification: `test/smoke-pty.py` (python3 PTY driver
      — boot labels, typeahead filter, Enter regenerates output + meter, Tab/E
      explain panel, game toggle, clean exit; skips when no CLI binary).
      `npm run test:smoke`.

## Phase 3 — Docs + CI

- [x] 3.1 USING-THE-ENGINE §12 + README: `explain` command/JSON shape
      (tokens/mods semantics, case-insensitive matching, reversibility note)
      + README usage example.
- [x] 3.2 CI smoke: `explain --json` added to the python3 JSON-parse assertions
      (asserts `tokens`/`mods` non-empty) and the non-python fallback loop;
      non-JSON `explain` grep check in the human-readable smoke block.

## Reference

- Original spec: `/home/ransagy/Downloads/poe-regex.md` (UI blueprint in §7).
- Engine design: `docs/DESIGN.md`; integration: `docs/USING-THE-ENGINE.md`.
- Build/publish: `docs/BUILDING.md`; policy: `CONTRIBUTING.md`, `NOTICE`.

## Work log

- 2026-08-10: Phase 1.1 done. Fixed two real bugs (placeholder leak, unescaped
  metachar leak) via catalog sweep; 45 tests green (was 41). Not yet pushed.
- 2026-08-10: Phase 1.2-1.5 done. Explainer + CLI `explain` + reversibility
  suite (66 tests green). Found + fixed a third real bug during the suite:
  fingerprints were case-sensitive-only, breaking recall under the game's
  case-insensitive matching (342 PoE2 pairs). Fingerprints are now unique under
  case-folded matching (SuffixIndex case-insensitive mode + folded per-class/
  union indexes + case-insensitive oracle). Not yet pushed.
- 2026-08-10: Phase 1.6-1.7 done. Debug+Release 66 green, 0 warnings;
  AOT/JIT parity verified; pushed `851d4ec` (Phase 1) + `c148e27` (plan mark).
- 2026-08-10: Phase 2.2-2.5 done. `src/engine.mjs` subprocess wrapper,
  `src/app.mjs` pure state, `src/clipboard.mjs`, `src/index.mjs` OpenTUI layout
  (spec §7 + modes + typeahead + meter + explain + copy with OSC52 fallback),
  18 `node --test` tests, `test/smoke-pty.py` PTY integration check. Key
  findings: VNode factory proxies don't forward post-materialization mutations
  (construct renderables directly); Enter parses as `key.name === "return"`;
  AbyssJewel (alphabetically first class) has no explicit mods -> default to
  Ring. `npm test` + `npm run test:smoke` green.
- 2026-08-10: Phase 3.1-3.2 done. `explain` documented in USING-THE-ENGINE §12
  (commands table row + "Explaining a query" subsection with JSON shape and
  semantics) and README usage; CI release smoke now asserts `explain --json`
  (tokens/mods present, mods non-empty) in the python3 and fallback branches.

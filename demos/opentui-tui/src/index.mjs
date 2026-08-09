// poe-regex TUI demo (spec §7 blueprint). Runs under Node 26.4 + --experimental-ffi.
//
// Renderables are constructed directly with `new XRenderable(renderer, {...})`
// rather than the VNode factory functions (`Box(...)`, `Text(...)`, ...): the
// factories return proxies that only replay queued calls at materialization
// time, so post-render mutations (options, content, focus, events) need real
// renderable handles.
import {
  createCliRenderer,
  BoxRenderable,
  TextRenderable,
  InputRenderable,
  SelectRenderable,
  RenderableEvents,
  InputRenderableEvents,
  SelectRenderableEvents,
} from "@opentui/core";
import * as engine from "./engine.mjs";
import * as app from "./app.mjs";
import { copyToClipboard } from "./clipboard.mjs";

const renderer = await createCliRenderer({ exitOnCtrlC: true });
const state = app.createState();

// ---- widgets ---------------------------------------------------------------

const gameSelect = new SelectRenderable(renderer, {
  width: 24,
  options: app.GAMES.map((g) => ({ name: app.GAME_LABELS[g], value: g, description: app.GAME_LABELS[g] })),
  showDescription: false,
});

const modeSelect = new SelectRenderable(renderer, {
  width: 14,
  options: [
    { name: "single", value: "single", description: "one base equipment slot" },
    { name: "global", value: "global", description: "no class anchor" },
    { name: "multi", value: "multi", description: "per-slot query list" },
  ],
});

const classSelect = new SelectRenderable(renderer, {
  width: 30,
  height: 8,
  options: [],
  showDescription: false,
});

const modInput = new InputRenderable(renderer, {
  width: 32,
  placeholder: "type to filter mods...",
  backgroundColor: "#1a1a1a",
  focusedBackgroundColor: "#222233",
});

const suggestSelect = new SelectRenderable(renderer, {
  width: 32,
  height: 8,
  options: [],
});

const includedSelect = new SelectRenderable(renderer, { width: 32, height: 5, options: [], showDescription: false });
const excludedSelect = new SelectRenderable(renderer, { width: 32, height: 5, options: [], showDescription: false });

const outputText = new TextRenderable(renderer, { content: "", fg: "#E0E0E0" });
const meterText = new TextRenderable(renderer, { content: "" });
const statusText = new TextRenderable(renderer, { content: "loading...", fg: "#AAAAAA" });
const explainText = new TextRenderable(renderer, { content: "", fg: "#9CDCFE" });

function label(content) {
  return new TextRenderable(renderer, { content, fg: "#888888" });
}

// ---- layout ----------------------------------------------------------------

const rootCol = new BoxRenderable(renderer, { flexDirection: "column", flexGrow: 1 });
const mainRow = new BoxRenderable(renderer, { flexDirection: "row", gap: 2, padding: 1 });
const leftCol = new BoxRenderable(renderer, { flexDirection: "column", gap: 1, flexGrow: 1 });
const rightCol = new BoxRenderable(renderer, { flexDirection: "column", gap: 1, width: 52 });

const footerBox = new BoxRenderable(renderer, { flexDirection: "row", paddingX: 1, borderStyle: "rounded", borderColor: "#444444" });
footerBox.add(
  new TextRenderable(renderer, {
    content: "Tab focus  ·  Enter include  ·  x exclude  ·  c copy  ·  e explain  ·  g game  ·  q quit",
    fg: "#888888",
  }),
);

function row(...children) {
  const r = new BoxRenderable(renderer, { flexDirection: "row", gap: 1 });
  for (const child of children) r.add(child);
  return r;
}

leftCol.add(row(label("SYSTEM ENGINE MODE:"), gameSelect));
leftCol.add(row(label("MODE:"), modeSelect));
leftCol.add(row(label("BASE EQUIPMENT:"), classSelect));
leftCol.add(row(label("MOD:"), modInput));
leftCol.add(suggestSelect);
leftCol.add(new TextRenderable(renderer, { content: "INCLUDED (enter)", fg: "#00C853" }));
leftCol.add(includedSelect);
leftCol.add(new TextRenderable(renderer, { content: "EXCLUDED (x)", fg: "#FF9800" }));
leftCol.add(excludedSelect);

const outputBox = new BoxRenderable(renderer, { borderStyle: "rounded", padding: 1, flexGrow: 1 });
outputBox.add(outputText);
rightCol.add(label("COMPILED OUTPUT STRINGS"));
rightCol.add(outputBox);
rightCol.add(meterText);
rightCol.add(statusText);
rightCol.add(explainText);

mainRow.add(leftCol);
mainRow.add(rightCol);
rootCol.add(mainRow);
rootCol.add(footerBox);
renderer.root.add(rootCol);

// ---- focus cycle -----------------------------------------------------------

const FOCUSABLE = [modInput, suggestSelect, includedSelect, excludedSelect, classSelect, modeSelect, gameSelect];
let focusIndex = 0;

// Keep focusIndex in sync even when focus changes via mouse or autoFocus.
for (let i = 0; i < FOCUSABLE.length; i++) {
  FOCUSABLE[i].on(RenderableEvents.FOCUSED, () => {
    focusIndex = i;
  });
}

function focusWidget(index) {
  const next = (index + FOCUSABLE.length) % FOCUSABLE.length;
  if (focusIndex !== next) FOCUSABLE[focusIndex].blur();
  focusIndex = next;
  FOCUSABLE[next].focus();
}

function cycleFocus(delta) {
  focusWidget(focusIndex + delta);
}

// ---- async engine flows ----------------------------------------------------

let refreshToken = 0;

async function loadClasses() {
  try {
    const classes = await engine.listClasses(state.game, { binary: engine.resolveBinary() });
    app.setClasses(state, classes);
    // Prefer a base equipment with explicit mods (the alphabetically-first
    // class, AbyssJewel, has none) so the typeahead works on first boot.
    if (state.mode !== "global") {
      const preferred = state.classes.find((c) => c.id === "Ring") ?? state.classes[0];
      if (preferred && state.selectedClasses[0] !== preferred.id) {
        app.selectClass(state, preferred.id);
      }
    }
    updateClassSelect();
    await refreshSuggestions();
    await regenerate();
  } catch (err) {
    statusText.content = `error: ${err.message}`;
  }
}

async function loadMods() {
  const bin = engine.resolveBinary();
  const limit = 1000;
  const cls = app.activeClasses(state)[0];
  if (state.mode === "global" || cls === undefined) {
    return engine.listMods(state.game, { limit, binary: bin });
  }
  return engine.listMods(state.game, { itemClass: cls, limit, binary: bin });
}

async function refreshSuggestions() {
  try {
    const mods = await loadMods();
    app.setMods(state, mods);
    updateSuggestions();
    if (state.mods.length === 0 && state.mode !== "global") {
      const cls = app.activeClasses(state)[0];
      statusText.content = `no explicit mods for ${cls} — pick another base equipment`;
    }
  } catch (err) {
    statusText.content = `error loading mods: ${err.message}`;
  }
}

async function regenerate() {
  const token = ++refreshToken;
  const tasks = app.generateTasks(state);
  const bin = engine.resolveBinary();
  try {
    if (state.mode === "global") {
      const task = tasks[0];
      const result = await engine.generate(state.game, { includes: task.includes, excludes: task.excludes, binary: bin });
      if (token !== refreshToken) return;
      state.results = [{ cls: null, ...result }];
    } else {
      const perClass = await Promise.all(
        tasks.map(async (task) => {
          const result = await engine.generate(state.game, { itemClass: task.itemClass, includes: task.includes, excludes: task.excludes, binary: bin });
          return { cls: task.cls, ...result };
        }),
      );
      if (token !== refreshToken) return;
      state.results = perClass;
    }
    state.explain = null;
    renderOutput();
  } catch (err) {
    if (token !== refreshToken) return;
    state.results = [];
    statusText.content = `error: ${err.message}`;
    renderOutput();
  }
}

async function explainCurrent() {
  const result = state.results[0];
  if (!result) return;
  try {
    const ex = await engine.explain(state.game, result.query, { itemClass: result.cls, binary: engine.resolveBinary() });
    state.explain = ex;
    renderExplain();
  } catch (err) {
    statusText.content = `explain error: ${err.message}`;
  }
}

async function copyQueries() {
  if (state.results.length === 0) return;
  const text = state.results.map((r) => r.query).join("\n");
  let ok = await copyToClipboard(text);
  if (!ok && renderer.isOsc52Supported()) {
    ok = renderer.copyToClipboardOSC52(text);
  }
  statusText.content = ok
    ? `copied ${state.results.length} query(ies) (${text.length} chars)`
    : "clipboard unavailable (install wl-copy/xclip/xsel/pbcopy/clip)";
}

// ---- rendering -------------------------------------------------------------

function updateClassSelect() {
  if (state.mode === "global") {
    classSelect.options = [];
    return;
  }
  classSelect.options = state.classes.map((c) => {
    const active = state.selectedClasses.includes(c.id);
    const marker = state.mode === "multi" ? (active ? "[x] " : "[ ] ") : "";
    return { name: `${marker}${c.name}`, description: c.id, value: c.id };
  });
  const idx = state.classes.findIndex((c) => c.id === state.selectedClasses[0]);
  if (idx >= 0) classSelect.setSelectedIndex(idx);
}

function updateSuggestions() {
  const matches = app.filterMods(state.mods, state.search);
  suggestSelect.options = matches.slice(0, 100).map((m) => ({
    name: m.line || m.id,
    description: `${m.name ?? ""} (${m.id})`,
    value: m,
  }));
  suggestSelect.setSelectedIndex(0);
}

function updateChips() {
  includedSelect.options = state.included.map((m) => ({ name: m.line || m.id, description: m.id, value: m.id }));
  excludedSelect.options = state.excluded.map((m) => ({ name: m.line || m.id, description: m.id, value: m.id }));
  includedSelect.setSelectedIndex(0);
  excludedSelect.setSelectedIndex(0);
}

function renderOutput() {
  const lines = state.results.map((r) => {
    const head = r.cls ? `[${r.cls}] ` : "[global] ";
    return `${head}${r.query}\n        ${r.length}/${r.limit} chars${r.exceeded ? " (OVER LIMIT)" : ""}`;
  });
  outputText.content = lines.join("\n\n") || "(no output)";
  const first = state.results[0];
  if (first) {
    meterText.content = `PAYLOAD CAPACITY: ${app.meterBar(first.length, first.limit)} ${first.length}/${first.limit}`;
    meterText.fg = app.METER_HEX[app.meterColor(first.length, first.limit)];
  } else {
    meterText.content = "";
  }
  const diag = state.results.flatMap((r) => r.diagnostics ?? []);
  statusText.content = diag.length > 0 ? `warnings: ${diag.map((d) => d.message).join("; ")}` : "ready";
}

function renderExplain() {
  const ex = state.explain;
  if (!ex) {
    explainText.content = "";
    return;
  }
  const lines = [];
  for (const token of ex.tokens) {
    lines.push(`[${token.kind}] ${token.phrase}${token.excluded ? " (excluded)" : ""} -> ${token.matches.length} match(es)`);
  }
  lines.push(`candidate mods: ${ex.mods.length}`);
  for (const mod of ex.mods.slice(0, 20)) {
    const phrases = mod.phrases.length > 0 ? ` <= ${mod.phrases.join(", ")}` : "";
    lines.push(`  ${mod.id} ${mod.name ?? ""}${phrases}`);
  }
  if (ex.mods.length > 20) lines.push(`  ... and ${ex.mods.length - 20} more`);
  explainText.content = lines.join("\n");
}

// ---- actions ---------------------------------------------------------------

function highlightedSuggestion() {
  const opt = suggestSelect.getSelectedOption();
  return opt?.value ?? null;
}

function rawSearchInclude() {
  const text = state.search.trim();
  if (!text) return null;
  return { id: text, line: text };
}

function onEnter() {
  const focused = FOCUSABLE[focusIndex];
  if (focused === modInput || focused === suggestSelect) {
    const mod = highlightedSuggestion() ?? rawSearchInclude();
    if (mod) {
      app.addIncluded(state, mod);
      modInput.value = "";
      updateChips();
      regenerate();
    }
  } else if (focused === includedSelect) {
    const opt = includedSelect.getSelectedOption();
    if (opt) {
      app.removeMod(state, state.included, opt.value);
      updateChips();
      regenerate();
    }
  } else if (focused === excludedSelect) {
    const opt = excludedSelect.getSelectedOption();
    if (opt) {
      app.removeMod(state, state.excluded, opt.value);
      updateChips();
      regenerate();
    }
  } else if (focused === classSelect && state.mode === "multi") {
    const opt = classSelect.getSelectedOption();
    if (opt) {
      app.toggleClass(state, opt.value);
      updateClassSelect();
      refreshSuggestions();
      regenerate();
    }
  }
}

/** Add the highlighted suggestion (or typed free text) to the EXCLUDED chips. */
function onExclude() {
  const focused = FOCUSABLE[focusIndex];
  if (focused === modInput || focused === suggestSelect) {
    const mod = highlightedSuggestion() ?? rawSearchInclude();
    if (mod) {
      app.addExcluded(state, mod);
      modInput.value = "";
      updateChips();
      regenerate();
    }
  }
}

// ---- events ----------------------------------------------------------------

modInput.on(InputRenderableEvents.INPUT, (value) => {
  app.setSearch(state, value);
  updateSuggestions();
});

gameSelect.on(SelectRenderableEvents.SELECTION_CHANGED, (idx, opt) => {
  if (opt?.value && opt.value !== state.game) {
    app.setGame(state, opt.value);
    loadClasses();
  }
});

modeSelect.on(SelectRenderableEvents.SELECTION_CHANGED, (idx, opt) => {
  if (opt?.value && opt.value !== state.mode) {
    app.setMode(state, opt.value);
    updateClassSelect();
    refreshSuggestions();
    regenerate();
  }
});

classSelect.on(SelectRenderableEvents.SELECTION_CHANGED, (idx, opt) => {
  if (state.mode === "single" && opt?.value && state.selectedClasses[0] !== opt.value) {
    app.selectClass(state, opt.value);
    refreshSuggestions();
    regenerate();
  }
});

renderer.keyInput.on("keypress", (key) => {
  if (key.name === "tab") {
    key.preventDefault();
    cycleFocus(key.shift ? -1 : 1);
    return;
  }
  if (key.name === "enter" || key.name === "return" || key.name === "linefeed") {
    key.preventDefault();
    onEnter();
    return;
  }
  const typing = focusIndex === 0; // modInput is index 0
  if (typing) return; // let the input receive characters
  if (key.name === "x") {
    key.preventDefault();
    onExclude();
  } else if (key.name === "c") {
    key.preventDefault();
    copyQueries();
  } else if (key.name === "e") {
    key.preventDefault();
    explainCurrent();
  } else if (key.name === "q") {
    key.preventDefault();
    renderer.destroy();
    process.exit(0);
  } else if (key.name === "g") {
    key.preventDefault();
    const next = state.game === "poe1" ? "poe2" : "poe1";
    app.setGame(state, next);
    gameSelect.setSelectedIndex(app.GAMES.indexOf(next));
    loadClasses();
  }
});

// ---- boot ------------------------------------------------------------------

focusWidget(0);
loadClasses();

// Pure application state + derivation for the TUI. No OpenTUI / FFI imports so
// this module is unit-testable with `node --test`.

export const GAMES = ["poe1", "poe2"];
export const MODES = ["single", "global", "multi"];
export const GAME_LABELS = { poe1: "Path of Exile 1", poe2: "Path of Exile 2" };

export function createState() {
  return {
    game: "poe1",
    mode: "single",
    classes: [], // [{id, name}] for the current game
    selectedClasses: [], // class ids: 1 for single, 0 for global, many for multi
    included: [], // [{id, line}]
    excluded: [], // [{id, line}]
    mods: [], // mod catalog for the current class (client-side typeahead source)
    search: "",
    results: [], // [{cls, query, length, limit, exceeded, diagnostics}]
    explain: null, // explain result for the last previewed query
    status: "",
  };
}

export function setGame(state, game) {
  state.game = game;
  state.classes = [];
  state.selectedClasses = [];
  state.included = [];
  state.excluded = [];
  state.mods = [];
  state.search = "";
  state.results = [];
  state.explain = null;
  state.status = "";
}

export function setMode(state, mode) {
  if (!MODES.includes(mode)) throw new Error(`unknown mode: ${mode}`);
  state.mode = mode;
  if (mode === "global") {
    state.selectedClasses = [];
    state.mods = [];
  } else if (mode === "single") {
    state.selectedClasses = state.selectedClasses.length >= 1 ? [state.selectedClasses[0]] : [];
  }
  state.results = [];
  state.explain = null;
}

export function setClasses(state, classes) {
  state.classes = classes;
  if (state.mode !== "global" && state.selectedClasses.length === 0 && classes.length > 0) {
    state.selectedClasses = [classes[0].id];
  }
}

export function toggleClass(state, clsId) {
  const i = state.selectedClasses.indexOf(clsId);
  if (i >= 0) state.selectedClasses.splice(i, 1);
  else state.selectedClasses.push(clsId);
  state.results = [];
  state.explain = null;
}

/** Replace the single selection (single mode). */
export function selectClass(state, clsId) {
  state.selectedClasses = [clsId];
  state.results = [];
  state.explain = null;
}

/** Active class ids for the current mode (global -> empty). */
export function activeClasses(state) {
  if (state.mode === "global") return [];
  if (state.mode === "single") return state.selectedClasses.slice(0, 1);
  return state.selectedClasses.slice();
}

export function addIncluded(state, mod) {
  if (!state.included.some((m) => m.id === mod.id)) state.included.push({ id: mod.id, line: mod.line });
  state.results = [];
  state.explain = null;
}

export function addExcluded(state, mod) {
  if (!state.excluded.some((m) => m.id === mod.id)) state.excluded.push({ id: mod.id, line: mod.line });
  state.results = [];
  state.explain = null;
}

export function removeMod(state, list, id) {
  const i = list.findIndex((m) => m.id === id);
  if (i >= 0) list.splice(i, 1);
  state.results = [];
  state.explain = null;
}

/** Clear the included/excluded chips and the mod search (keeps game/mode/slots). */
export function clearChips(state) {
  state.included = [];
  state.excluded = [];
  state.search = "";
  state.results = [];
  state.explain = null;
}

export function setMods(state, mods) {
  state.mods = mods;
}

export function setSearch(state, text) {
  state.search = text;
}

/** Keep only base-equipment classes that have explicit mods (null set = keep all). */
export function playableClasses(classes, classIdsWithMods) {
  if (!classIdsWithMods) return classes;
  return classes.filter((c) => classIdsWithMods.has(c.id));
}

/** Mods not already added to included or excluded (by id). */
export function availableMods(state) {
  const taken = new Set([...state.included, ...state.excluded].map((m) => m.id));
  return state.mods.filter((m) => !taken.has(m.id));
}

/** Mods offered by the typeahead: search over the available mods. */
export function suggestionMods(state) {
  return filterMods(availableMods(state), state.search);
}

/** Add many mods at once (select-all), preserving first-wins id order. */
export function addManyIncluded(state, mods) {
  const seen = new Set(state.included.map((m) => m.id));
  for (const mod of mods) {
    if (!seen.has(mod.id)) {
      seen.add(mod.id);
      state.included.push({ id: mod.id, line: mod.line });
    }
  }
  state.results = [];
  state.explain = null;
}

/** Merge per-slot mod lists into one, deduped by id (first occurrence wins). */
export function mergeMods(lists) {
  const seen = new Set();
  const out = [];
  for (const list of lists) {
    for (const m of list) {
      if (!seen.has(m.id)) {
        seen.add(m.id);
        out.push(m);
      }
    }
  }
  return out;
}

/** Client-side substring filter over the loaded mod catalog (name/id/line). */
export function filterMods(mods, text) {
  const t = text.trim().toLowerCase();
  if (!t) return mods;
  return mods.filter(
    (m) => (m.name ?? "").toLowerCase().includes(t) || m.id.toLowerCase().includes(t) || m.line.toLowerCase().includes(t),
  );
}

/** True when a query must be regenerated for the given state. */
export function queryInputs(state) {
  return {
    game: state.game,
    classes: activeClasses(state),
    includes: state.included.map((m) => m.id),
    excludes: state.excluded.map((m) => m.id),
    keywords: [],
  };
}

/** Per-slot generate tasks: global -> one task, otherwise one task per active class. */
export function generateTasks(state) {
  const { includes, excludes } = queryInputs(state);
  if (state.mode === "global") return [{ cls: null, itemClass: undefined, includes, excludes }];
  return activeClasses(state).map((cls) => ({ cls, itemClass: cls, includes, excludes }));
}

/** Capacity meter fill (0..1) and the block bar. */
export function meterRatio(length, limit) {
  return limit <= 0 ? 0 : Math.min(1, length / limit);
}

export function meterBar(length, limit, cells = 20) {
  const filled = Math.round(meterRatio(length, limit) * cells);
  return "█".repeat(filled) + "░".repeat(cells - filled);
}

/** Green -> orange -> red based on capacity consumed. */
export function meterColor(length, limit) {
  const ratio = meterRatio(length, limit);
  if (ratio >= 0.9) return "red";
  if (ratio >= 0.6) return "orange";
  return "green";
}

/** Color name -> hex used by the TUI. */
export const METER_HEX = { green: "#00C853", orange: "#FF9800", red: "#F44336" };

export function resultLabel(result) {
  return `${result.query}  [${result.length}/${result.limit}${result.exceeded ? " OVER" : ""}]`;
}

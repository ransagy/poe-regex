import { test } from "node:test";
import assert from "node:assert/strict";
import {
  createState,
  setGame,
  setMode,
  setClasses,
  toggleClass,
  selectClass,
  addIncluded,
  addExcluded,
  addManyIncluded,
  removeMod,
  clearChips,
  setMods,
  setSearch,
  filterMods,
  playableClasses,
  availableMods,
  suggestionMods,
  mergeMods,
  activeClasses,
  queryInputs,
  generateTasks,
  meterRatio,
  meterBar,
  meterColor,
} from "../src/app.mjs";

test("setGame flushes game-specific state", () => {
  const s = createState();
  setClasses(s, [{ id: "Ring", name: "Rings" }]);
  addIncluded(s, { id: "IncreasedLife1", line: "+# to maximum Life" });
  setMode(s, "multi");
  toggleClass(s, "Amulet");
  assert.equal(s.selectedClasses.length, 2);

  setGame(s, "poe2");
  assert.equal(s.game, "poe2");
  assert.equal(s.classes.length, 0);
  assert.equal(s.selectedClasses.length, 0);
  assert.equal(s.included.length, 0);
});

test("setMode global clears classes and single keeps one", () => {
  const s = createState();
  setClasses(s, [{ id: "A", name: "a" }, { id: "B", name: "b" }]);
  toggleClass(s, "B");
  setMode(s, "global");
  assert.deepEqual(activeClasses(s), []);

  setMode(s, "multi");
  toggleClass(s, "A");
  toggleClass(s, "B");
  assert.deepEqual(activeClasses(s).sort(), ["A", "B"]);

  setMode(s, "single");
  assert.deepEqual(activeClasses(s), ["A"]);
});

test("toggleClass adds and removes", () => {
  const s = createState();
  toggleClass(s, "Ring");
  assert.deepEqual(s.selectedClasses, ["Ring"]);
  toggleClass(s, "Ring");
  assert.deepEqual(s.selectedClasses, []);
});

test("selectClass replaces the selection in single mode", () => {
  const s = createState();
  selectClass(s, "Ring");
  assert.deepEqual(s.selectedClasses, ["Ring"]);
  selectClass(s, "Amulet");
  assert.deepEqual(s.selectedClasses, ["Amulet"]);
  assert.equal(s.results.length, 0);
});

test("included/excluded add once and remove by id", () => {
  const s = createState();
  addIncluded(s, { id: "X", line: "l" });
  addIncluded(s, { id: "X", line: "l" });
  addExcluded(s, { id: "Y", line: "l2" });
  assert.deepEqual(s.included, [{ id: "X", line: "l" }]);
  assert.deepEqual(s.excluded, [{ id: "Y", line: "l2" }]);

  removeMod(s, s.included, "X");
  assert.deepEqual(s.included, []);
});

test("clearChips clears chips, search, and results but keeps game/mode/slots", () => {
  const s = createState();
  setGame(s, "poe2");
  setClasses(s, [{ id: "Ring", name: "Rings" }, { id: "Amulet", name: "Amulets" }]);
  setMode(s, "multi");
  toggleClass(s, "Amulet");
  setSearch(s, "dexter");
  addIncluded(s, { id: "A", line: "l" });
  addExcluded(s, { id: "B", line: "l2" });
  s.results = [{ query: "x" }];

  clearChips(s);
  assert.equal(s.included.length, 0);
  assert.equal(s.excluded.length, 0);
  assert.equal(s.search, "");
  assert.equal(s.results.length, 0);
  assert.equal(s.explain, null);
  assert.equal(s.game, "poe2");
  assert.equal(s.mode, "multi");
  assert.deepEqual(s.selectedClasses, ["Ring", "Amulet"]);
});

test("filterMods matches name, id, and line case-insensitively", () => {
  const mods = [
    { id: "IncreasedLife1", name: "Healthy", line: "+# to maximum Life" },
    { id: "Strength1", name: "Brute", line: "+# to Strength" },
  ];
  assert.equal(filterMods(mods, "life").length, 1);
  assert.equal(filterMods(mods, "STR").length, 1);
  assert.equal(filterMods(mods, "brute").length, 1);
  assert.equal(filterMods(mods, "maximum").length, 1);
  assert.equal(filterMods(mods, "zzz").length, 0);
  assert.equal(filterMods(mods, "  ").length, 2);
});

test("queryInputs reflects active classes and chips", () => {
  const s = createState();
  setClasses(s, [{ id: "Ring", name: "Rings" }, { id: "Amulet", name: "Amulets" }]);
  addIncluded(s, { id: "A", line: "l" });
  addExcluded(s, { id: "B", line: "l2" });
  setMode(s, "multi");
  toggleClass(s, "Amulet");
  assert.deepEqual(queryInputs(s), {
    game: "poe1",
    classes: ["Ring", "Amulet"],
    includes: ["A"],
    excludes: ["B"],
    keywords: [],
  });
});

test("generateTasks maps state to per-slot generate calls", () => {
  const s = createState();
  setClasses(s, [{ id: "Ring", name: "Rings" }, { id: "Amulet", name: "Amulets" }]);
  addIncluded(s, { id: "A", line: "l" });
  addExcluded(s, { id: "B", line: "l2" });

  assert.deepEqual(generateTasks(s), [
    { cls: "Ring", itemClass: "Ring", includes: ["A"], excludes: ["B"] },
  ]);

  setMode(s, "multi");
  toggleClass(s, "Amulet");
  assert.deepEqual(generateTasks(s), [
    { cls: "Ring", itemClass: "Ring", includes: ["A"], excludes: ["B"] },
    { cls: "Amulet", itemClass: "Amulet", includes: ["A"], excludes: ["B"] },
  ]);

  setMode(s, "global");
  assert.deepEqual(generateTasks(s), [
    { cls: null, itemClass: undefined, includes: ["A"], excludes: ["B"] },
  ]);
});

test("meter helpers", () => {
  assert.equal(meterRatio(26, 250), 0.104);
  assert.equal(meterRatio(300, 250), 1);
  assert.equal(meterBar(26, 250), "██░░░░░░░░░░░░░░░░░░");
  assert.equal(meterColor(26, 250), "green");
  assert.equal(meterColor(170, 250), "orange");
  assert.equal(meterColor(240, 250), "red");
});

test("playableClasses keeps only classes with explicit mods", () => {
  const classes = [{ id: "Ring", name: "R" }, { id: "AbyssJewel", name: "A" }, { id: "Amulet", name: "Am" }];
  const ids = new Set(["Ring", "Amulet"]);
  assert.deepEqual(playableClasses(classes, ids).map((c) => c.id), ["Ring", "Amulet"]);
  assert.equal(playableClasses(classes, null).length, 3, "null set keeps all");
  assert.equal(playableClasses([], ids).length, 0);
});

test("availableMods/suggestionMods exclude already-chosen mods", () => {
  const s = createState();
  const mods = [
    { id: "X", name: "n", line: "l" },
    { id: "Y", name: "dex", line: "dexterity +#" },
    { id: "Z", name: "dex2", line: "dex +#" },
  ];
  setMods(s, mods);
  addIncluded(s, { id: "X", line: "l" });
  addExcluded(s, { id: "Y", line: "dexterity +#" });
  assert.deepEqual(availableMods(s).map((m) => m.id), ["Z"]);

  setSearch(s, "dex");
  assert.deepEqual(suggestionMods(s).map((m) => m.id), ["Z"]);
  setSearch(s, "");
  assert.deepEqual(suggestionMods(s).map((m) => m.id), ["Z"]);
});

test("addManyIncluded merges and dedupes by id", () => {
  const s = createState();
  addIncluded(s, { id: "A", line: "a" });
  addManyIncluded(s, [
    { id: "A", line: "a" },
    { id: "B", line: "b" },
    { id: "B", line: "b2" },
  ]);
  assert.deepEqual(s.included.map((m) => m.id), ["A", "B"]);
  assert.equal(s.included[1].line, "b");
});

test("mergeMods dedupes per-slot mod lists by id", () => {
  const lists = [
    [{ id: "A" }, { id: "B" }],
    [{ id: "B" }, { id: "C" }],
  ];
  assert.deepEqual(mergeMods(lists).map((m) => m.id), ["A", "B", "C"]);
  assert.equal(mergeMods([[]]).length, 0);
});

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
  removeMod,
  setMods,
  setSearch,
  filterMods,
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

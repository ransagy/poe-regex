import { test } from "node:test";
import assert from "node:assert/strict";
import { parseClassIdsWithMods, loadClassIdsWithMods } from "../src/catalog.mjs";

test("parseClassIdsWithMods collects class ids referenced by any mod", () => {
  const catalogJson = {
    mods: [
      { id: "m1", classes: ["Ring", "Amulet"] },
      { id: "m2", classes: ["Ring"] },
      { id: "m3", classes: [] },
    ],
  };
  const ids = parseClassIdsWithMods(catalogJson);
  assert.equal(ids.has("Ring"), true);
  assert.equal(ids.has("Amulet"), true);
  assert.equal(ids.has("AbyssJewel"), false);
  assert.equal(parseClassIdsWithMods({ mods: [] }).size, 0);
  assert.equal(parseClassIdsWithMods(null).size, 0);
});

test("loadClassIdsWithMods reads the shipped poe1 catalog", () => {
  const ids = loadClassIdsWithMods("poe1");
  assert.ok(ids instanceof Set, "expected a Set for the shipped catalog");
  assert.equal(ids.size, 25, "poe1 has 25 classes with explicit mods");
  assert.equal(ids.has("Ring"), true);
  assert.equal(ids.has("AbyssJewel"), false);
  assert.equal(ids.has("LifeFlask"), false);
});

test("loadClassIdsWithMods is null-safe for unknown games", () => {
  assert.equal(loadClassIdsWithMods("poe999"), null);
});

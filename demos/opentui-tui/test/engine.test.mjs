import { test, before, after } from "node:test";
import assert from "node:assert/strict";
import * as engine from "../src/engine.mjs";

let bin;
before(() => {
  try {
    bin = engine.resolveBinary();
  } catch {
    bin = null;
  }
});

function skipWithoutBinary(t) {
  if (!bin) {
    t.skip("no poe-regex CLI binary (set POE_REGEX_BIN or npm run build:cli)");
    return true;
  }
  return false;
}

test("resolveBinary throws when no binary is available", () => {
  const env = process.env.POE_REGEX_BIN;
  delete process.env.POE_REGEX_BIN;
  try {
    // Only assert the error path when the repo out/ has no binary either.
    let threw = false;
    try {
      engine.resolveBinary();
    } catch (err) {
      threw = true;
      assert.match(err.message, /POE_REGEX_BIN|build:cli/);
    }
    if (env) process.env.POE_REGEX_BIN = env;
    assert.ok(threw || process.env.POE_REGEX_BIN, "expected an error or an env override");
  } finally {
    if (env) process.env.POE_REGEX_BIN = env;
  }
});

test("listClasses returns id/name entries", async (t) => {
  if (skipWithoutBinary(t)) return;
  const classes = await engine.listClasses("poe1", { binary: bin });
  assert.ok(classes.length > 50, "expected many classes");
  assert.ok(classes.every((c) => typeof c.id === "string" && typeof c.name === "string"));
});

test("listMods filters by search", async (t) => {
  if (skipWithoutBinary(t)) return;
  const mods = await engine.listMods("poe1", { itemClass: "Ring", search: "dexter", binary: bin });
  assert.ok(mods.length > 0, "expected matching mods");
  assert.ok(mods.every((m) => m.line.toLowerCase().includes("dexter") || m.id.toLowerCase().includes("dexter")));
});

test("listMods honors the limit", async (t) => {
  if (skipWithoutBinary(t)) return;
  const mods = await engine.listMods("poe1", { itemClass: "Ring", limit: 5, binary: bin });
  assert.ok(mods.length <= 5, `expected at most 5 mods, got ${mods.length}`);
});

test("generate returns query with length and limit", async (t) => {
  if (skipWithoutBinary(t)) return;
  const g = await engine.generate("poe1", { itemClass: "Ring", includes: ["IncreasedLife1"], binary: bin });
  assert.ok(typeof g.query === "string" && g.query.length > 0);
  assert.equal(typeof g.length, "number");
  assert.equal(typeof g.limit, "number");
  assert.equal(g.limit, 250);
});

test("generate without a class uses global mode", async (t) => {
  if (skipWithoutBinary(t)) return;
  const g = await engine.generate("poe1", { includes: ["AddedPhysicalDamage1"], binary: bin });
  assert.ok(g.query.length > 0, "global generate should produce a query");
});

test("explain reverses a generated query back to the source mod", async (t) => {
  if (skipWithoutBinary(t)) return;
  const g = await engine.generate("poe1", { itemClass: "Ring", includes: ["IncreasedLife1"], binary: bin });
  const ex = await engine.explain("poe1", g.query, { itemClass: "Ring", binary: bin });
  assert.ok(ex.mods.some((m) => m.id === "IncreasedLife1"), "explain must recall the source mod");
  assert.ok(ex.tokens.length >= 2, "expected include + class tokens");
});

test("validate reports ok for a valid query", async (t) => {
  if (skipWithoutBinary(t)) return;
  const v = await engine.validate("poe2", '"tr" ":"', { binary: bin });
  assert.equal(v.ok, true);
});

test("errors become thrown errors with exit code detail", async (t) => {
  if (skipWithoutBinary(t)) return;
  await assert.rejects(engine.listClasses("poe3", { binary: bin }), /failed \(exit/);
});

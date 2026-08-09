// Reads the derived catalogs shipped in the repo (data/catalogs/*.catalog.json)
// to answer "which base-equipment classes have explicit mods?" without extra
// CLI round-trips. The TUI filters the BASE EQUIPMENT list to these classes so
// flasks, currency, gems, relics, etc. don't appear as unusable slots.
import { readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const DEMO_ROOT = path.dirname(fileURLToPath(import.meta.url));
const REPO_ROOT = path.resolve(DEMO_ROOT, "..", "..", "..");

const cache = new Map();

/** Class ids referenced by at least one mod's `classes` array. Pure. */
export function parseClassIdsWithMods(catalogJson) {
  const ids = new Set();
  for (const mod of catalogJson?.mods ?? []) {
    for (const cls of mod.classes ?? []) ids.add(cls);
  }
  return ids;
}

/** Load the set for a game from the shipped catalog; null when unavailable. */
export function loadClassIdsWithMods(game) {
  if (cache.has(game)) return cache.get(game);
  const file = path.join(REPO_ROOT, "data", "catalogs", `${game}.catalog.json`);
  let ids = null;
  try {
    ids = parseClassIdsWithMods(JSON.parse(readFileSync(file, "utf8")));
  } catch {
    ids = null;
  }
  cache.set(game, ids);
  return ids;
}

// Subprocess wrapper for the poe-regex CLI.
//
// Every call runs the CLI with --json and applies the subprocess contract:
// exactly one JSON document on stdout, stderr clean on success, exit 0 on
// success. Exit 1 (error) and exit 2 (usage) become thrown errors.
//
// The CLI binary is resolved from (highest priority):
//   1. the POE_REGEX_BIN environment variable, or
//   2. out/PoeRegex.Cli (.exe on Windows) — the published binary produced by
//      `npm run build:cli` (native AOT publish into the repo out/ dir).
import { execFile } from "node:child_process";
import { fileURLToPath } from "node:url";
import { existsSync } from "node:fs";
import path from "node:path";

const DEMO_ROOT = path.dirname(fileURLToPath(import.meta.url));
const REPO_ROOT = path.resolve(DEMO_ROOT, "..", "..", "..");

export function resolveBinary({ repoRoot = REPO_ROOT } = {}) {
  const envBin = process.env.POE_REGEX_BIN;
  if (envBin) return envBin;
  const candidates = [path.join(repoRoot, "out", "PoeRegex.Cli"), path.join(repoRoot, "out", "PoeRegex.Cli.exe")];
  for (const candidate of candidates) {
    if (existsSync(candidate)) return candidate;
  }
  throw new Error(
    "poe-regex CLI binary not found. Build it with `npm run build:cli` " +
      "(dotnet publish -p:PublishAot=true into out/) or set POE_REGEX_BIN to an existing binary.",
  );
}

function run(args, { binary = resolveBinary(), timeoutMs = 30_000 } = {}) {
  return new Promise((resolve, reject) => {
    execFile(
      binary,
      args,
      { maxBuffer: 16 * 1024 * 1024, timeout: timeoutMs },
      (error, stdout, stderr) => {
        const code = error && typeof error.code === "number" ? error.code : error ? 1 : 0;
        if (code !== 0) {
          const message = (stderr || error?.message || "").trim();
          reject(new Error(`poe-regex ${args[0] ?? ""} failed (exit ${code}): ${message || "(no stderr)"}`));
          return;
        }
        resolve({ stdout, stderr });
      },
    );
  });
}

function parseJson(args, raw) {
  let parsed;
  try {
    parsed = JSON.parse(raw.stdout);
  } catch (err) {
    throw new Error(`poe-regex ${args[0]} returned invalid JSON: ${err.message}`);
  }
  if (raw.stderr.trim().length > 0) {
    // The contract says stderr is clean on success; surface anything unexpected.
    console.warn(`poe-regex ${args[0]} wrote to stderr: ${raw.stderr.trim()}`);
  }
  return parsed;
}

/** list-classes --game <game> --json -> [{id, name}] */
export async function listClasses(game, { binary } = {}) {
  const args = ["list-classes", "--game", game, "--json"];
  return parseJson(args, await run(args, { binary }));
}

/** list-mods --game <game> [--class <id>] [--search <text>] [--limit <n>] --json -> [{id, name, line}] */
export async function listMods(game, { itemClass, search, limit, binary } = {}) {
  const args = ["list-mods", "--game", game, "--json"];
  if (itemClass) args.push("--class", itemClass);
  if (search) args.push("--search", search);
  if (limit !== undefined) args.push("--limit", String(limit));
  return parseJson(args, await run(args, { binary }));
}

/** generate --game <game> [--class <id>] --include ... --exclude ... --keyword ... --json */
export async function generate(game, { itemClass, includes = [], excludes = [], keywords = [], allowOverflow = false, binary } = {}) {
  const args = ["generate", "--game", game, "--json"];
  if (itemClass) args.push("--class", itemClass);
  for (const include of includes) args.push("--include", include);
  for (const exclude of excludes) args.push("--exclude", exclude);
  for (const keyword of keywords) args.push("--keyword", keyword);
  if (allowOverflow) args.push("--allow-overflow");
  return parseJson(args, await run(args, { binary }));
}

/** explain --game <game> [--class <id>] --query <query> --json */
export async function explain(game, query, { itemClass, binary } = {}) {
  const args = ["explain", "--game", game, "--json"];
  if (itemClass) args.push("--class", itemClass);
  args.push("--query", query);
  return parseJson(args, await run(args, { binary }));
}

/** validate --game <game> --query <query> --json -> {ok, game, warning} */
export async function validate(game, query, { binary } = {}) {
  const args = ["validate", "--game", game, "--query", query, "--json"];
  return parseJson(args, await run(args, { binary }));
}

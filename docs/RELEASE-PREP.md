# Release prep checklist

Use this for every catalog, corpus, solver, or web release. The goal is one
reviewable path from a branch to production, with the proof and performance
evidence regenerated from the branch being merged.

This is an internal maintainer/CI gate, not a PR artifact. PRs contain only
the code/docs change and its tests; local reports, proof/status outputs, perf
logs, and PWA output are never committed, attached, linked, or pasted into a
PR.

Do not merge until every applicable gate is green. Do not run
`deploy-production` without explicit approval for that deployment; merging to
`main` runs CI and previews only.

## 1. Provenance and scope

- [ ] Confirm every new PoE2 mod family is PoE2 data.
  - Check exact ids against PoE1 RePoE when an id looks like a legacy/master
    craft.
  - A mod needs a positive PoE2 reference: `mods_by_base`, positive spawn
    tags, a base `implicits` link, or another explicit PoE2-only source.
  - Do not infer a normal-equipment category from a bare domain label
    (`crafted` is PoE1 legacy in the current export).
- [ ] Run the RePoE inventory audit and classify any new export file before
  using it:

  ```sh
  dotnet run --project tools/PoeRegex.Data.Build -- data-audit --check \
    --out /tmp/opencode/repoe-audit.md
  ```

- [ ] Record the raw `dataVersion` and the affected item classes.
- [ ] Update the coverage backlog and any changed limitations in
  [`WORKPLAN.md`](WORKPLAN.md), [`IMPORT.md`](IMPORT.md), and
  [`EXACT-TABLE.md`](EXACT-TABLE.md).
- [ ] If the change is not PoE2 data, stop and remove it rather than adding a
  manual mapping.
- [ ] For PoE1 changes, confirm the `poe1` manifest export, the pinned
  `data/repoe-poe1.snapshot.json`, and the `poe1` safety/corpus artifacts are
  classified and current.

## 2. Data rebuild

Run only when the catalog, vendor/safety corpus, or solver changed.

- [ ] Save the target branch's table for the diff:

  ```sh
  git show origin/main:data/catalogs/poe2.fragments.json \
    > /tmp/opencode/poe2.fragments.main.json
  ```

- [ ] Rebuild the catalog/corpus if their inputs changed:

  ```sh
  dotnet run --project tools/PoeRegex.Data.Build -- build
  dotnet run --project tools/PoeRegex.Data.Build -- corpus
  ```

  For a full RePoE refresh, first run
  `dotnet run --project tools/PoeRegex.Data.Build -- fetch --refresh` and
  regenerate all raw inputs together; do not mix a new file with the previous
  snapshot. The manual **data-audit** workflow is the remote check; the local
  command above remains the release gate.

- [ ] Regenerate the exact table with a stability pass against the shipped
  table:

  ```sh
  dotnet run --project tools/PoeRegex.Data.Build -- matrix-solve \
    --prefer /tmp/opencode/poe2.fragments.main.json \
    --jobs 2 --timeout 60
  ```

  Use `--from-scratch` only when the solver, grammar, or corpus changes, not
  for an ordinary catalog refresh; multi-line patterns can otherwise produce
  a line-crossing fragment that the query shape validator rejects.

- [ ] Verify the PoE2 proof/status:

  ```sh
  dotnet run --project tools/PoeRegex.Data.Build -- matrix-status \
    --check --check-shortest
  ```

  Required: `stale: false`, `shortestProven: true`, `proof.failed: 0`.

- [ ] For PoE1 data changes, rebuild and verify:

  ```sh
  dotnet run --project tools/PoeRegex.Data.Build -- poe1-build
  dotnet run --project tools/PoeRegex.Data.Build -- poe1-matrix --from-scratch
  dotnet run --project tools/PoeRegex.Data.Build -- poe1-status --check --check-shortest
  ```

  Required: 1,626 scopes, 0 unresolved, `stale: false`, `shortestProven: true`,
  `proof.failed: 0`. Copy the updated catalog/table/corpus into
  `web/wwwroot/data/poe1/`.

## 3. Fragment diff report

Keep the report outside the repository; the shipped table and proof remain the
source of truth.

- [ ] Generate the report with fragment lengths and deltas:

  ```sh
  python3 tools/fragment-diff/run.py \
    --old /tmp/opencode/poe2.fragments.main.json \
    --new data/catalogs/poe2.fragments.json \
    --reason "advanced oracle forms; catalog lines added" \
    --out-md /tmp/opencode/fragment-diff.md \
    --out-json /tmp/opencode/fragment-diff.json
  ```

- [ ] Review every changed scope. The report separates changed, new, and
  removed patterns for global and class scopes. Changed rows list the old/new
  fragment, both lengths, and the delta; new and removed rows list only the
  side that exists.
- [ ] Explain every changed or removed scope locally. No unexplained forced
  changes.
- [ ] Keep the full report outside the repository and outside the PR. Do not
  commit, attach, link, or paste it; it is a local/CI release gate only.

## 4. Tests and proof

- [ ] Run the full suite in Release:

  ```sh
  dotnet test PoeRegex.slnx -c Release
  ```

- [ ] Confirm the counts in [`WORKPLAN.md`](WORKPLAN.md) match the run.
- [ ] For common-shortlist changes, run `common-picks --check` and confirm
  the `ci` workflow's **Verify common picks** step is green.
- [ ] Confirm new regression tests fail on the base branch and pass on the
  change branch.
- [ ] Confirm CI `test` and the Cloudflare Pages preview are green.
- [ ] For exact-table changes, confirm the matrix/proof/status artifacts were
  regenerated together. For PoE1, run `poe1-build` (if raw data changed),
  `poe1-matrix --from-scratch`, and `poe1-status --check --check-shortest`.

## 5. Web build and PWA

Run when the web app, shell, service worker, or data/table changed.

- [ ] Build the deployed shape:

  ```sh
  ./build.sh
  ```

- [ ] Run the PWA check:

  ```sh
  node tools/pwa-check/run.mjs --root out/web/wwwroot
  ```

  Required: manifest and installability have no errors, the worker controls
  the page, the cache has the shell and framework, and the offline reload
  boots.

## 6. Performance

Run when data/table size or web code changed.

- [ ] Run the release build with the same host and viewport as the baseline:

  ```sh
  node tools/web-perf/run.mjs --root out/web/wwwroot --runs 5 \
    --width 1920 --height 1080 --out /tmp/opencode/perf-current.json
  ```

- [ ] Compare p50/p95 and long tasks with a baseline build from `origin/main`.
  A material p50 regression blocks the merge; a single host/GC outlier is
  recorded, not hidden.
- [ ] Check advanced picks, bulk add, import parse, explain, builder open, and
  the 10-slot paste update against [`ACCEPTANCE.md`](ACCEPTANCE.md).
- [ ] Optional: run the manual **web-perf** workflow for a CI-host sample.
  It uploads the JSON/log and writes a step summary, but it is observational;
  the local run above remains the blocking gate.

## 7. Merge

- [ ] All PRs are open, non-draft, and `CLEAN` / `MERGEABLE`.
- [ ] Link the stack when there is more than one PR:

  ```sh
  gh stack link <bottom-pr> <middle-pr> <top-pr>
  ```

- [ ] Get explicit approval for the merge(s).
- [ ] Merge the stack atomically:

  ```sh
  gh stack merge <stack-number> --merge --yes
  ```

  Or merge sequentially and retarget each stacked PR to `main` after its base
  lands.
- [ ] Verify the merge commit and run the main-branch checks:

  ```sh
  git fetch origin && git switch main && git pull --ff-only
  dotnet test PoeRegex.slnx -c Release
  dotnet run --project tools/PoeRegex.Data.Build -- matrix-status \
    --check --check-shortest
  ```

- [ ] Confirm branch protection and CI passed on `main`.

## 8. Production

- [ ] Get explicit approval for the production deployment.
- [ ] Run **Actions → deploy-production → Run workflow** from `main`.
- [ ] Confirm the workflow verifies `https://renlys.tools/build-info.json`
  against the deployed commit.
- [ ] Record the deployed commit in the release notes.

Nothing in this checklist authorizes a production deployment by itself. The
approval in step 7 is for merge; step 8 is a separate decision.

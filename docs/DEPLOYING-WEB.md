# Deploying the web app (Cloudflare Pages)

The Blazor WebAssembly app in `web/` — "Renly's Recipes", this project's web
reference implementation — is deployed as a static site on Cloudflare Pages.
Deployments are built by Cloudflare through the Cloudflare Workers & Pages
GitHub App — there are no deploy secrets in this repository.

## How deployments map to branches

| Event | Deployment | URL |
| --- | --- | --- |
| Push to production branch (`main`) | production | <https://renlys-recipes.pages.dev> |
| Push to any other branch (except `dependabot/*`) / any PR | preview | `<branch>.<project>.pages.dev` and `<hash>.<project>.pages.dev` |

Every pull request from an in-repo branch gets a preview deployment, except
Dependabot branches; merging to `main` is the production deploy.

## When builds run

Two filters on the Pages project decide whether a push builds at all
(Settings → Build):

- **Preview branches**: all branches except `dependabot/*` — dependency PRs
  change manifests, not the site, and stay out of the deployment history.
- **Build watch paths**: `web/*`, `src/*`, `data/catalogs/*`, `build.sh`,
  `LICENSE`, `NOTICE`, and `docs/PROVENANCE.md` (directory entries need the
  trailing `*`; exact file paths do not). Docs-only or workflow-only
  pushes (including merges) skip the build, so the current deployment — and
  its `build-info.json` commit — stays until the next app-relevant change.

If a new file becomes part of the build, add it to the watch paths in the
Pages project settings.

## Preview cleanup

`.github/workflows/cleanup-preview.yml` deletes a branch's preview deployments
when its pull request closes (merged or not), using the Cloudflare API;
production is never touched. It needs two repository settings:

- variable `CF_ACCOUNT_ID` — the Cloudflare account id;
- secret `CF_API_TOKEN` — an API token with **Account → Cloudflare Pages →
  Edit** for this account (create it in the Cloudflare dashboard).

The same workflow can be run manually (Actions → *cleanup-preview* → Run
workflow) with a branch name. It is also called by the contributor-preview
workflow below.

## Contributor (fork) previews

Fork PRs do not receive the automatic Pages preview because the Pages GitHub
App only builds branches in this repository. A maintainer can opt a fork PR in
by commenting:

```text
/preview
```

`.github/workflows/preview-contrib.yml` then:

1. verifies the commenter has write/maintain/admin access;
2. verifies the PR is open and from a fork;
3. refuses PRs that change `build.sh`, MSBuild/project files, `_headers`,
   `_redirects`, or workflow files;
4. pushes a temporary `contrib-pr-<number>` branch from
   `refs/pull/<number>/merge` (or the head ref when no merge ref exists) —
   without checking out or executing the fork code;
5. comments the preview URL:
   `https://contrib-pr-<number>.renlys-recipes.pages.dev`.

Cloudflare Pages builds that branch like any other preview. Once the preview
branch exists, new commits on the PR refresh it automatically; `/preview` still
creates or re-creates it. The workflow edits its existing preview comment with
the latest commit instead of posting a new one.

Cleanup happens automatically when the PR closes. A maintainer can also
comment:

```text
/preview-cleanup
```

or run **Actions → preview-contrib → Run workflow** with the PR number and
`cleanup`. Cleanup deletes the branch's Pages deployments and the temporary
Git branch. Privileged jobs only move refs or call the Cloudflare API; they
never check out contributor code. The Pages build environment has no
repository secrets, and the noindex/SPA headers come from the trusted
`_headers`/`_redirects` unless the PR is refused by the guard. The Pages build
still compiles the PR's source; review the diff before opting in.

### Security notes

- The token lives only as an Actions secret. It is used by `cleanup-preview`
  and by the reusable cleanup call in `preview-contrib`; it is masked in logs
  (verified: runs show `***`, never the value).
- Fork pull requests never receive secrets through `pull_request`. The
  `preview-contrib` workflow does use `issue_comment` and
  `pull_request_target`, which run from `main` with repository secrets, but
  those privileged jobs only move refs or call the Cloudflare API. They never
  check out or execute contributor code. No workflow uses `workflow_run`.
- Keep the Cloudflare token scoped to **Account → Cloudflare Pages → Edit**
  (the narrowest available — Pages permissions are account-wide), set an
  expiry, and rotate it on schedule.
- Anyone with **write access** to this repository can read repository secrets
  by editing a workflow, so the collaborator list is the trust boundary. If
  the token ever leaks, delete it in the Cloudflare dashboard and set a new
  secret; the workflow degrades gracefully without it.

## Pages project configuration

| Setting | Value |
| --- | --- |
| Build command | `./build.sh` |
| Build output directory | `out/web/wwwroot` |
| Production branch | `main` |

Project name: `renlys-recipes` (the app's name), production URL
`https://renlys-recipes.pages.dev`.

## What build.sh does

The Pages build image does not include .NET, so `build.sh`:

1. downloads the .NET 10 SDK into `out/dotnet/` (gitignored),
2. runs `dotnet publish web/web.csproj -c Release -o out/web`,
3. deletes pre-compressed `*.br`/`*.gz` siblings — Pages compresses at
   the edge and does not serve those variants correctly,
4. creates an empty `web.styles.css` if the SDK did not emit one
   (`index.html` links it unconditionally; without this the request falls
   through the SPA fallback to `index.html` and the browser blocks it on
   the MIME type),
5. leaves the static site in `out/web/wwwroot`.

Reproduce the Cloudflare build locally with:

```sh
./build.sh
```

## Static-site behavior

- `web/wwwroot/_redirects` — `/* /index.html 200` gives SPA deep links
  (client-side routes survive a full page reload).
- `web/wwwroot/_headers` — `no-cache` on `index.html` so deployments
  roll out immediately; `immutable` caching for `_framework/*` (every
  file is content-fingerprinted in .NET 10, and fingerprints change per
  build); `X-Robots-Tag: noindex` on all `*.pages.dev` hosts so previews
  and the production `pages.dev` URL stay out of search engines (the app is
  link-only). A custom domain is not matched and stays indexable.
- `build-info.json` is generated into the output on every build (commit,
  branch, and deployment URL from Cloudflare's `CF_PAGES_*` build
  variables, with a local `git` fallback). The Advanced panel shows the short
  commit + branch, so any deployment URL — including the
  frozen `<hash>.pages.dev` snapshots — can be traced to its exact commit.

## CI

`.github/workflows/ci.yml` runs the test suite on pull requests and on
pushes to `main`. It needs no Cloudflare credentials; deploys are owned
by the Cloudflare GitHub App integration. The CLI release pipeline
(`release.yml`) is unchanged.

## Analytics

The Pages project has Cloudflare Web Analytics enabled. Pages injects its
cookieless beacon (`static.cloudflareinsights.com`) into every served HTML
response; it is not part of the app source. Disable it in the project
settings if you redeploy this site without analytics. Details:
<https://developers.cloudflare.com/web-analytics/>.

## Deployment retention

Only the current production deployment is kept. After a deploy is verified on
the production URL (build-info trace plus an app boot check), older production
and preview deployments are deleted through the Cloudflare API so their hash
and branch URLs stop resolving. Deleting a preview also removes its branch
alias (`<branch>.<project>.pages.dev`).

## Status

- [x] build script + headers + redirects + PR CI
- [x] Pages project created and connected to this repository (`renlys-recipes`)
- [x] preview deployment verified on a feature branch (headless boot to
      rendered app; SPA deep links, wasm MIME, cache + noindex headers)
- [x] production deploy verified on `main` (build-info trace, noindex,
      `_framework` immutable caching)
- [x] build filters: no previews for `dependabot/*`, docs-only pushes skip
      the build (watch paths)
- [x] manual contributor (fork) preview opt-in with `/preview`, automatic
      refresh on new commits, one upserted preview comment, cleanup on PR
      close, and `/preview-cleanup`
- [ ] custom domain (optional, later)

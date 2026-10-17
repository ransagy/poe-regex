# Deploying the web app (Cloudflare Pages)

The Blazor WebAssembly app in `web/` is deployed as a static site on
Cloudflare Pages. Deployments are built by Cloudflare through the
Cloudflare Workers & Pages GitHub App — there are no deploy secrets in
this repository.

## How deployments map to branches

| Event | Deployment | URL |
| --- | --- | --- |
| Push to production branch (`main`) | production | `<project>.pages.dev` (custom domain TBD) |
| Push to any other branch / any PR | preview | `<branch>.<project>.pages.dev` and `<hash>.<project>.pages.dev` |

Every pull request gets a preview deployment; merging to `main` is the
production deploy.

## Pages project configuration

| Setting | Value |
| --- | --- |
| Build command | `./build.sh` |
| Build output directory | `out/web/wwwroot` |
| Production branch | `main` |

Project name: `renlys-recipes` (production URL:
`renlys-recipes.pages.dev`).

## What build.sh does

The Pages build image does not include .NET, so `build.sh`:

1. downloads the .NET 10 SDK into `out/dotnet/` (gitignored),
2. runs `dotnet publish web/web.csproj -c Release -o out/web`,
3. deletes pre-compressed `*.br`/`*.gz` siblings — Pages compresses at
   the edge and does not serve those variants correctly,
4. leaves the static site in `out/web/wwwroot`.

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
  (and the bare production `pages.dev` URL) stay out of search engines.
  A custom domain is not matched and stays indexable.

## CI

`.github/workflows/ci.yml` runs the test suite on pull requests and on
pushes to `main`. It needs no Cloudflare credentials; deploys are owned
by the Cloudflare GitHub App integration. The CLI release pipeline
(`release.yml`) is unchanged.

## Branch stacking note

This branch (`feature/cf-pages`) was stacked on
`feature/web-workbench`. If that branch merges first, rebase this one
onto `main` and retarget the pull request.

## Status

- [x] build script + headers + redirects + PR CI
- [x] Pages project created and connected to this repository (`renlys-recipes`)
- [x] preview deployment verified on a feature branch (headless boot to
      rendered app; SPA deep links, wasm MIME, cache + noindex headers)
- [ ] production deploy verified on `main`
- [ ] custom domain (optional, later)

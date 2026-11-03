# Branded preview router

Cloudflare Worker for `renlys.tools` previews.

## URLs

```text
https://preview-<branch>.renlys.tools
  -> https://<branch>.renlys-recipes.pages.dev

https://preview-contrib-pr-<number>.renlys.tools
  -> https://contrib-pr-<number>.renlys-recipes.pages.dev

https://www.renlys.tools/*
  -> 301 https://renlys.tools/*
```

`<branch>` is the Cloudflare Pages branch alias: lowercased, with
non-alphanumeric characters replaced by hyphens. For example,
`feat/guide-import` becomes `feat-guide-import`.

## Cloudflare configuration

Worker name: `renlys-preview-router`

Routes:

```text
*.renlys.tools/*
www.renlys.tools/*
```

Cloudflare only allows a wildcard at the start of the hostname and the end of
the path, so the wildcard route covers every subdomain and the Worker filters
for the `preview-` prefix. Non-preview subdomains return 404.

DNS:

```text
renlys.tools       CNAME  renlys-recipes.pages.dev  proxied
www.renlys.tools   A      192.0.2.1                  proxied
*.renlys.tools     A      192.0.2.1                  proxied
```

The proxied wildcard record makes the preview hostnames resolve; the Worker
route selects only `preview-*` hosts. The Worker adds `X-Robots-Tag: noindex`
and `Cache-Control: no-store` to preview responses, so they stay out of search
engines and always show the latest deployment.

# Branded preview router

Cloudflare Worker for `renlys.tools` previews.

## URLs

```text
https://preview-<branch>.renlys.tools
  -> internal Pages branch host for <branch>

https://preview-contrib-pr-<number>.renlys.tools
  -> internal Pages branch host for contrib-pr-<number>

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

The Worker sends `X-Renlys-Preview-Key` on its upstream fetch. The value must
match the Pages project's `PREVIEW_KEY` secret; the Pages middleware returns
404 for direct branch/hash host requests without it. Set the Worker secret:

```sh
npx wrangler secret put PREVIEW_KEY
```

DNS:

```text
renlys.tools       CNAME  <internal Pages host>  proxied
www.renlys.tools   A      192.0.2.1             proxied
*.renlys.tools     A      192.0.2.1             proxied
```

The proxied wildcard record makes the preview hostnames resolve; the Worker
route selects only `preview-*` hosts. The Worker adds `X-Robots-Tag: noindex`
and `Cache-Control: no-store` to preview responses, so they stay out of search
engines and always show the latest deployment.

The Pages project's `functions/_middleware.js` redirects its bare project host
to `https://renlys.tools` and rejects direct branch/hash host requests without
the `PREVIEW_KEY` header. The Worker and Pages must use the same secret.

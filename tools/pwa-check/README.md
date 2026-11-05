# PWA acceptance check

Serves a published `wwwroot` on a non-localhost test origin, waits for the
service worker, checks the manifest/installability, and reloads with the
network offline.

## Usage

```sh
dotnet publish web/web.csproj -c Release -o /tmp/webpwa \
  -p:BlazorEnableCompression=false

node tools/pwa-check/run.mjs --root /tmp/webpwa/wwwroot
```

Options:

```text
--root <dir>       published wwwroot (required)
--port <n>         static server port, default 8117
--debugPort <n>    Chromium CDP port, default 9240
--host <name>      test hostname, default pwa.test
```

The check exits non-zero if the worker does not activate, the shell is not
cached, the manifest is invalid, or the app does not boot offline.

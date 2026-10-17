#!/bin/sh
# Cloudflare Pages build script for the Blazor WebAssembly app.
#
# The Pages build image ships no .NET, so this installs the .NET 10 SDK
# locally, publishes web/web.csproj, and leaves the static site in
# out/web/wwwroot. Everything this script produces lives under the
# gitignored out/ directory, so it is safe to run locally as well.
#
# Pages project configuration:
#   build command:    ./build.sh
#   output directory: out/web/wwwroot
set -eu

mkdir -p out

curl -fsSL https://dot.net/v1/dotnet-install.sh > out/dotnet-install.sh
chmod +x out/dotnet-install.sh
./out/dotnet-install.sh -c 10.0 -InstallDir ./out/dotnet
./out/dotnet/dotnet --version

# Pre-compressed .br/.gz siblings are not served correctly by Cloudflare
# Pages (it compresses at the edge itself). BlazorEnableCompression
# normally suppresses them; the delete is a safety net in case the
# property is renamed in a future SDK.
./out/dotnet/dotnet publish web/web.csproj -c Release -o out/web \
  -p:BlazorEnableCompression=false

find out/web/wwwroot -type f \( -name '*.br' -o -name '*.gz' \) -delete

# index.html always links web.styles.css, but the SDK only emits that file
# when the project has scoped CSS (.razor.css). Ship an empty one so the
# link resolves as text/css instead of falling through the SPA rewrite to
# index.html, which browsers reject as a stylesheet on the MIME type.
[ -f out/web/wwwroot/web.styles.css ] || touch out/web/wwwroot/web.styles.css

# Stamp the build so any deployment can be traced back to a commit.
# Cloudflare injects CF_PAGES_* during Pages builds; locally fall back to git.
COMMIT="${CF_PAGES_COMMIT_SHA:-$(git rev-parse HEAD 2>/dev/null || echo unknown)}"
BRANCH="${CF_PAGES_BRANCH:-$(git rev-parse --abbrev-ref HEAD 2>/dev/null || echo local)}"
printf '{"commit":"%s","branch":"%s","url":"%s"}\n' "$COMMIT" "$BRANCH" "${CF_PAGES_URL:-}" \
  > out/web/wwwroot/build-info.json

echo "build.sh: site ready at out/web/wwwroot"

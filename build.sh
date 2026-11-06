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
# The app is PoE2-only; the embedded catalog/table/vendor corpus stays
# below Cloudflare Pages' per-file limit.
# Clear the previous publish so deleted/stale assets never enter the shell
# or the precache manifest when build.sh is run repeatedly locally.
rm -rf out/web
./out/dotnet/dotnet publish web/web.csproj -c Release -o out/web \
  -p:BlazorEnableCompression=false

find out/web/wwwroot -type f \( -name '*.br' -o -name '*.gz' \) -delete

# index.html always links web.styles.css, but the SDK only emits that file
# when the project has scoped CSS (.razor.css). Ship an empty one so the
# link resolves as text/css instead of falling through the SPA rewrite to
# index.html, which browsers reject as a stylesheet on the MIME type.
[ -f out/web/wwwroot/web.styles.css ] || touch out/web/wwwroot/web.styles.css

# Ship the license notices with the published copy of the site.
cp LICENSE out/web/wwwroot/license.txt
cp NOTICE out/web/wwwroot/notice.txt
cp docs/PROVENANCE.md out/web/wwwroot/provenance.md

# Stamp the build so any deployment can be traced back to a commit.
# Cloudflare injects CF_PAGES_* during Pages builds; locally fall back to git.
# Never write the internal deployment URL here: the public site and preview
# aliases are the only links shown to users.
COMMIT="${CF_PAGES_COMMIT_SHA:-$(git rev-parse HEAD 2>/dev/null || echo unknown)}"
BRANCH="${CF_PAGES_BRANCH:-$(git rev-parse --abbrev-ref HEAD 2>/dev/null || echo local)}"
printf '{"commit":"%s","branch":"%s"}\n' "$COMMIT" "$BRANCH" \
  > out/web/wwwroot/build-info.json

# Version the service worker registration with the deployed commit. The
# query bypasses Cloudflare's cached /sw.js response on the next navigation.
sed -i "s/__BUILD_COMMIT__/$COMMIT/g" out/web/wwwroot/index.html

# The offline shell fetches this list at install time. Exclude the worker,
# deployment metadata, and Cloudflare control files; everything else is the
# immutable app shell.
{
  find out/web/wwwroot -type f \
    ! -name 'sw.js' \
    ! -name 'precache-manifest.json' \
    ! -name 'build-info.json' \
    ! -name '_headers' \
    ! -name '_redirects' \
    ! -name '_routes.json' \
    -printf '/%P\n' \
    | LC_ALL=C sort \
    | awk 'BEGIN { printf "[" } { printf "%s\"%s\"", sep, $0; sep = "," } END { print "]" }'
} > out/web/wwwroot/precache-manifest.json

# Change the worker bytes on every build so the browser installs the new
# version and its cache instead of keeping the previous immutable shell.
printf '\n// build: %s\n' "$COMMIT" >> out/web/wwwroot/sw.js

echo "build.sh: site ready at out/web/wwwroot"

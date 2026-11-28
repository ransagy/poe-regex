#!/bin/sh
# Copy the source-of-truth PoE1 artifacts into a serving directory.
# The checked-in web copy is verified by Poe1ArtifactTests; build.sh also
# syncs the published copy so a stale file can never ship.
set -eu

dest="${1:-web/wwwroot/data/poe1}"
mkdir -p "$dest"
cp data/catalogs/poe1.catalog.json "$dest/catalog.json"
cp data/catalogs/poe1.fragments.json "$dest/fragments.json"
cp data/catalogs/poe1.vendor-corpus.json "$dest/vendor-corpus.json"
echo "sync-poe1-data: $dest"

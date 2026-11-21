#!/usr/bin/env bash
# Checks out EncDotNet.ArcGIS (SoundCharts for ArcGIS, the ArcGIS Pro add-in)
# into external/EncDotNet.ArcGIS so docfx.json can build its user-facing pages
# into this site, under docs/arcgis/. By default it checks out the add-in's
# latest release tag, so the docs describe the add-in users can download, or
# main if there's no release yet. Set ENCDOTNET_ARCGIS_REF to a branch, tag or
# commit to preview other docs locally.
#
# Usage: docs/fetch-encdotnet-arcgis.sh
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dest="$root/external/EncDotNet.ArcGIS"
repo="https://github.com/philliphoff/EncDotNet.ArcGIS.git"

ref="${ENCDOTNET_ARCGIS_REF:-}"
if [ -z "$ref" ]; then
  # The highest final release tag (vMAJOR.MINOR.PATCH); prereleases are skipped.
  ref="$(
    git ls-remote --tags --refs "$repo" 'v*' |
      sed 's#.*refs/tags/##' |
      grep -E '^v[0-9]+\.[0-9]+\.[0-9]+$' |
      sort -V |
      tail -n1 || true
  )"
  ref="${ref:-main}"
fi

rm -rf "$dest"
mkdir -p "$dest"
git -C "$dest" init -q
git -C "$dest" remote add origin "$repo"
git -C "$dest" fetch -q --depth 1 origin "$ref"
git -C "$dest" checkout -q FETCH_HEAD
echo "EncDotNet.ArcGIS $ref ($(git -C "$dest" rev-parse --short HEAD)) checked out to external/EncDotNet.ArcGIS"

#!/usr/bin/env bash
# Checks out EncDotNet (the S-57 and ISO 8211 libraries) into external/EncDotNet
# so docfx.json can build its pages, package READMEs and API reference into this
# site. By default it checks out the release tag matching the EncDotNet.S57
# version in Directory.Packages.props, so the docs describe the packages this
# repository ships. Set ENCDOTNET_REF to a branch, tag or commit to preview
# other EncDotNet docs locally.
#
# Usage: docs/fetch-encdotnet.sh
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dest="$root/external/EncDotNet"
repo="https://github.com/philliphoff/EncDotNet.git"

version="$(sed -n 's/.*<PackageVersion Include="EncDotNet\.S57" Version="\([^"]*\)".*/\1/p' "$root/Directory.Packages.props")"
if [ -z "$version" ]; then
  echo "error: no EncDotNet.S57 version in Directory.Packages.props" >&2
  exit 1
fi
ref="${ENCDOTNET_REF:-v$version}"

rm -rf "$dest"
mkdir -p "$dest"
git -C "$dest" init -q
git -C "$dest" remote add origin "$repo"
git -C "$dest" fetch -q --depth 1 origin "$ref"
git -C "$dest" checkout -q FETCH_HEAD
echo "EncDotNet $ref ($(git -C "$dest" rev-parse --short HEAD)) checked out to external/EncDotNet"

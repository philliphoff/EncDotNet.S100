#!/bin/sh
# fetch-tutorial-nis.sh — download Esri's sample S-57 Nautical Information
# System (NIS) file geodatabase for the NIS tests (issue #1758).
#
# The sample is Tutorial/TUTORIAL_NIS.gdb from Esri's "ArcGIS for Maritime
# Charting quick tutorial" (https://github.com/enags/maritime-charting-quick-tutorial),
# Apache License 2.0, copyright Esri 2015. It holds an ArcMap-era NIS
# (data model "NIS (10.3.0.0)") with NOAA's US5CA98M (Lake Tahoe) and
# US5NY1DM (New York) cells. It is not committed to this repository: this
# script downloads it at a pinned commit and checks every file against
# TUTORIAL_NIS.gdb.sha256, beside this script.
#
# Usage:
#   tools/nis-fixtures/fetch-tutorial-nis.sh
#
# Environment:
#   ENC_DOTNET_NIS_FIXTURES  The cache directory
#                            (default: $HOME/.cache/encdotnet-nis-fixtures).
#                            The tests read the same variable.
#
# Idempotent: with the geodatabase cached and its checksums matching, it
# downloads nothing.

set -eu

REPOSITORY="enags/maritime-charting-quick-tutorial"
COMMIT="0e27e551304dee4779b3af7c484bf10a61a0a127"
GDB="TUTORIAL_NIS.gdb"

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
MANIFEST="$SCRIPT_DIR/$GDB.sha256"
CACHE_DIR="${ENC_DOTNET_NIS_FIXTURES:-$HOME/.cache/encdotnet-nis-fixtures}"
DEST="$CACHE_DIR/$GDB"

if ! command -v git >/dev/null 2>&1 && ! command -v curl >/dev/null 2>&1; then
    echo "ERROR: git or curl (with tar) is required." >&2
    exit 1
fi

if command -v sha256sum >/dev/null 2>&1; then
    SHA256="sha256sum"
elif command -v shasum >/dev/null 2>&1; then
    SHA256="shasum -a 256"
else
    echo "ERROR: neither sha256sum nor shasum is installed." >&2
    exit 1
fi

# Checks every file in the manifest; prints nothing and succeeds when all match.
verify() {
    (cd "$1" && $SHA256 -c "$MANIFEST" >/dev/null 2>&1)
}

if [ -d "$DEST" ] && verify "$DEST"; then
    echo "CACHED  $DEST"
    echo ""
    echo "export ENC_DOTNET_NIS_FIXTURES=$CACHE_DIR"
    exit 0
fi

mkdir -p "$CACHE_DIR"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

# The pinned commit, by git where it is installed, else as a tarball.
if command -v git >/dev/null 2>&1; then
    echo "FETCH   https://github.com/$REPOSITORY @ $COMMIT"
    git -C "$WORK" init -q repo
    git -C "$WORK/repo" fetch -q --depth 1 "https://github.com/$REPOSITORY.git" "$COMMIT"
    git -C "$WORK/repo" -c advice.detachedHead=false checkout -q FETCH_HEAD
    EXTRACTED="$WORK/repo/Tutorial/$GDB"
else
    URL="https://codeload.github.com/$REPOSITORY/tar.gz/$COMMIT"
    echo "FETCH   $URL"
    curl -fSL --retry 3 -o "$WORK/tutorial.tar.gz" "$URL"
    tar -xzf "$WORK/tutorial.tar.gz" -C "$WORK"
    EXTRACTED="$WORK/$(basename "$REPOSITORY")-$COMMIT/Tutorial/$GDB"
fi

if [ ! -d "$EXTRACTED" ]; then
    echo "ERROR: the download has no Tutorial/$GDB." >&2
    exit 1
fi
if ! verify "$EXTRACTED"; then
    echo "ERROR: $GDB does not match $MANIFEST." >&2
    (cd "$EXTRACTED" && $SHA256 -c "$MANIFEST" 2>&1 | grep -v ': OK$' | head -5) >&2
    exit 1
fi

rm -rf "$DEST"
mv "$EXTRACTED" "$DEST"
echo "OK      $DEST"
echo ""
echo "export ENC_DOTNET_NIS_FIXTURES=$CACHE_DIR"

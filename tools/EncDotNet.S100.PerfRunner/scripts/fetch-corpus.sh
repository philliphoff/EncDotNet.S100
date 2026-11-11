#!/bin/sh
# fetch-corpus.sh — download external performance corpus assets.
#
# Reads tests/perf/corpus/corpus.json and downloads any entry with a
# non-null "url" field into a local cache directory. Verifies SHA-256
# after download. Idempotent — re-running with everything cached is
# a no-op.
#
# Usage:
#   tools/EncDotNet.S100.PerfRunner/scripts/fetch-corpus.sh
#
# Environment:
#   ENC_DOTNET_PERF_CORPUS  Override the cache directory
#                           (default: $HOME/.cache/encdotnet-perf-corpus)

set -eu

REPO_ROOT="$(cd "$(dirname "$0")/../../.." && pwd)"
CORPUS_JSON="$REPO_ROOT/tests/perf/corpus/corpus.json"
CACHE_DIR="${ENC_DOTNET_PERF_CORPUS:-$HOME/.cache/encdotnet-perf-corpus}"

if [ ! -f "$CORPUS_JSON" ]; then
    echo "ERROR: corpus.json not found at $CORPUS_JSON" >&2
    exit 1
fi

# Check for required tools.
for cmd in curl shasum python3; do
    if ! command -v "$cmd" >/dev/null 2>&1; then
        echo "ERROR: required command '$cmd' not found." >&2
        exit 1
    fi
done

mkdir -p "$CACHE_DIR"

# Parse corpus.json using python3 (available on macOS/Linux).
# Extracts entries with non-null url fields.
ENTRIES=$(python3 -c "
import json, sys
with open('$CORPUS_JSON') as f:
    data = json.load(f)
for e in data.get('entries', []):
    url = e.get('url')
    sha = e.get('sha256')
    eid = e.get('id', 'unknown')
    if url and sha:
        # Use id as filename (replace / with __)
        fname = eid.replace('/', '__')
        print(f'{fname}\t{url}\t{sha}')
")

if [ -z "$ENTRIES" ]; then
    echo "No external assets to fetch — all corpus entries are bundled."
    echo "Cache directory: $CACHE_DIR"
    echo ""
    echo "Export for PerfRunner:"
    echo "  export ENC_DOTNET_PERF_CORPUS=$CACHE_DIR"
    exit 0
fi

FAILED=0
FETCHED=0
CACHED=0

echo "$ENTRIES" | while IFS="$(printf '\t')" read -r FNAME URL SHA256; do
    DEST="$CACHE_DIR/$FNAME"

    if [ -f "$DEST" ]; then
        # Verify existing file.
        ACTUAL=$(shasum -a 256 "$DEST" | cut -d' ' -f1)
        if [ "$ACTUAL" = "$SHA256" ]; then
            echo "  CACHED  $FNAME"
            CACHED=$((CACHED + 1))
            continue
        else
            echo "  STALE   $FNAME (SHA mismatch, re-downloading)"
            rm -f "$DEST"
        fi
    fi

    echo "  FETCH   $FNAME"
    echo "          $URL"

    if ! curl -fSL --retry 3 -o "$DEST" "$URL"; then
        echo "  ERROR   Failed to download $FNAME" >&2
        rm -f "$DEST"
        FAILED=$((FAILED + 1))
        continue
    fi

    # Verify download.
    ACTUAL=$(shasum -a 256 "$DEST" | cut -d' ' -f1)
    if [ "$ACTUAL" != "$SHA256" ]; then
        echo "  ERROR   SHA-256 mismatch for $FNAME" >&2
        echo "          Expected: $SHA256" >&2
        echo "          Actual:   $ACTUAL" >&2
        rm -f "$DEST"
        exit 1
    fi

    echo "  OK      $FNAME"
    FETCHED=$((FETCHED + 1))
done

echo ""
echo "Cache directory: $CACHE_DIR"
echo ""
echo "Export for PerfRunner:"
echo "  export ENC_DOTNET_PERF_CORPUS=$CACHE_DIR"

if [ "$FAILED" -gt 0 ]; then
    echo ""
    echo "WARNING: $FAILED asset(s) failed to download." >&2
    exit 1
fi

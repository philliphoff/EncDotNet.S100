#!/usr/bin/env bash
#
# PoC: generate S-411 sea-ice "quicklook" preview images using the s100 CLI.
#
# Mirrors the idea behind the BSIS Ice Portal previews
# (https://www.bsis-ice.de/IcePortal/ILP_S411.shtml): download the published
# S-411 exchange-set ZIPs, extract the GML dataset, and rasterise a PNG preview.
# Here the rendering is done entirely by the EncDotNet.S100 `s100` CLI.
#
# Usage:
#   ./s411_previews.sh [OUT_DIR] [REGION ...]
#
#   OUT_DIR   Directory for downloaded data + previews (default: ./s411-previews)
#   REGION    One or more region keys (see REGIONS below). Default: a small set.
#
# Environment:
#   S100        Command used to invoke the CLI. Defaults to running the built
#               Release DLL via `dotnet`. Override to point at a published
#               binary, e.g.  S100="/usr/local/bin/s100"
#   WIDTH/HEIGHT  Output size in pixels (default 1600x1600).
#   PALETTE       day | dusk | night (default day).
#   EXTRA_OPTS    Extra flags passed verbatim to `s100 render` after the
#                 standard --width/--height/--palette (e.g. --no-text for
#                 BSIS-style clean fills, or --hide text,points).
#
set -euo pipefail

BASE_URL="https://www.bsis-ice.de/IcePortal/S411"

# region-key -> zip file name on the BSIS portal.
# (A representative subset; add more from ILP_S411.shtml as needed.)
# Implemented as a case lookup for portability with bash 3.2 (macOS default),
# which lacks associative arrays.
region_zip() {
  case "$1" in
    north-atlantic) echo "S411_MetNo_ice20260605.zip" ;;
    canada-east)    echo "S411_cis_SGRDREC_20260601T1800Z_pl_a.zip" ;;
    hudson-bay)     echo "S411_cis_SGRDRHB_20260601T1800Z_pl_a.zip" ;;
    alaska)         echo "S411_NWS_full_20260605.zip" ;;
    nw-greenland)   echo "S411_DMI_202606032145_NorthWest_RIC.zip" ;;
    *)              echo "" ;;
  esac
}

OUT_DIR="${1:-./s411-previews}"
shift || true
SELECTED=("$@")
if [[ ${#SELECTED[@]} -eq 0 ]]; then
  SELECTED=(north-atlantic hudson-bay nw-greenland)
fi

WIDTH="${WIDTH:-1600}"
HEIGHT="${HEIGHT:-1600}"
PALETTE="${PALETTE:-day}"
EXTRA_OPTS="${EXTRA_OPTS:-}"

# Default invocation: run the built Release DLL. Override via $S100.
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
DEFAULT_DLL="$REPO_ROOT/tools/EncDotNet.S100.Cli/bin/Release/net10.0/s100.dll"
S100="${S100:-dotnet "$DEFAULT_DLL"}"

mkdir -p "$OUT_DIR/data" "$OUT_DIR/previews"

echo "Output dir : $OUT_DIR"
echo "CLI        : $S100"
echo "Size       : ${WIDTH}x${HEIGHT}  palette=$PALETTE"
echo "Regions    : ${SELECTED[*]}"
echo

render_region() {
  local key="$1"
  local zip_name
  zip_name="$(region_zip "$key")"
  if [[ -z "$zip_name" ]]; then
    echo "  ! unknown region '$key' — skipping"
    return 0
  fi

  local zip_path="$OUT_DIR/data/$key.zip"
  local extract_dir="$OUT_DIR/data/$key"
  local preview="$OUT_DIR/previews/$key.png"

  echo "[$key]"
  echo "  downloading $zip_name"
  if ! curl -fsSL -o "$zip_path" "$BASE_URL/$zip_name"; then
    echo "  ! download failed — skipping"
    return 0
  fi

  rm -rf "$extract_dir"
  mkdir -p "$extract_dir"
  unzip -q -o "$zip_path" -d "$extract_dir"

  # S-411 exchange set: the dataset GML lives under data/.
  local gml
  gml="$(find "$extract_dir" -type f -name '*.gml' | head -1)"
  if [[ -z "$gml" ]]; then
    echo "  ! no .gml found in exchange set — skipping"
    return 0
  fi
  echo "  dataset $gml"

  # shellcheck disable=SC2086
  if $S100 render "$gml" "$preview" --width "$WIDTH" --height "$HEIGHT" --palette "$PALETTE" $EXTRA_OPTS; then
    echo "  -> $preview"
  else
    echo "  ! render failed"
  fi
  echo
}

for key in "${SELECTED[@]}"; do
  render_region "$key"
done

echo "Done. Previews in: $OUT_DIR/previews"

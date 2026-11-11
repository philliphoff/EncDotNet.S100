#!/usr/bin/env bash
#
# PoC: generate S-411 sea-ice "quicklook" preview images using the s100 CLI,
# and (optionally) fetch the matching BSIS Ice Portal quicklook for side-by-side
# comparison.
#
# Mirrors the idea behind the BSIS Ice Portal previews
# (https://www.bsis-ice.de/IcePortal/ILP_S411.shtml): download the published
# S-411 exchange-set ZIPs, extract the GML dataset, and rasterise a PNG preview.
# Here the rendering is done entirely by the EncDotNet.S100 `s100` CLI.
#
# The published ZIP file names are dated and rotate (often daily). Rather than
# hard-coding a snapshot, this script *discovers the current ZIP* for each
# region by scanning the live portal index for a stable per-region pattern.
#
# Usage:
#   ./s411_previews.sh [OUT_DIR] [REGION ...]
#
#   OUT_DIR   Directory for downloaded data + previews (default: ./s411-previews)
#   REGION    One or more region keys (see REGIONS below). Default: a small set.
#             Use the special key `all` to render every known region.
#
# Environment:
#   S100        Command used to invoke the CLI. Defaults to running the built
#               Release DLL via `dotnet`. Override to point at a published
#               binary, e.g.  S100="/usr/local/bin/s100"
#   WIDTH/HEIGHT  Output size in pixels (default 1600x1600).
#   PALETTE       day | dusk | night (default day).
#   EXTRA_OPTS    Extra flags passed verbatim to `s100 render` after the
#                 standard --width/--height/--palette (e.g. --no-text for
#                 BSIS-style clean fills, --hide text,points, or
#                 --basemap offline to draw the bundled Natural Earth land
#                 layer beneath the ice so floes read against the coastline).
#   COMPARE       When set to 1, also download the BSIS quicklook(s) for each
#                 region into previews/<key>.bsis-*.png. If ImageMagick's
#                 `montage` is on PATH, a combined previews/<key>.compare.png
#                 contact sheet (our render + BSIS CONC/SOD) is produced too.
#
set -euo pipefail

BASE_URL="https://www.bsis-ice.de/IcePortal"
ZIP_URL="$BASE_URL/S411"
PORTAL_INDEX="$BASE_URL/ILP_S411.shtml"

# ---------------------------------------------------------------------------
# Region registry (bash 3.2 compatible — case lookups, no associative arrays).
#
# Each region key maps to:
#   * a ZIP *pattern* (an extended-regexp matched against the portal index, with
#     the rotating date replaced by a wildcard) — the current file is discovered
#     at run time;
#   * the BSIS quicklook image base name(s) under S411Preview/ for the
#     concentration (CONC) and, where published, stage-of-development (SOD)
#     views, used by COMPARE mode.
# ---------------------------------------------------------------------------
region_zip_pattern() {
  case "$1" in
    cw-greenland)   echo 'S411_DMI_[0-9]+_CentralWest_RIC\.zip' ;;
    nw-greenland)   echo 'S411_DMI_[0-9]+_NorthWest_RIC\.zip' ;;
    ne-greenland)   echo 'S411_DMI_[0-9]+_NorthEast_RIC\.zip' ;;
    se-greenland)   echo 'S411_DMI_[0-9]+_SouthEast_RIC\.zip' ;;
    sw-greenland)   echo 'S411_DMI_[0-9]+_SouthWest_RIC\.zip' ;;
    ce-greenland)   echo 'S411_DMI_[0-9]+_CentralEast_RIC\.zip' ;;
    cape-farewell)  echo 'S411_DMI_[0-9]+_CapeFarewell_RIC\.zip' ;;
    qaanaaq)        echo 'S411_DMI_[0-9]+_Qaanaaq_RIC\.zip' ;;
    canada-east)    echo 'S411_cis_SGRDREC_[0-9A-Za-z]+_pl_a\.zip' ;;
    hudson-bay)     echo 'S411_cis_SGRDRHB_[0-9A-Za-z]+_pl_a\.zip' ;;
    eastern-arctic) echo 'S411_cis_SGRDREA_[0-9A-Za-z]+_pl_a\.zip' ;;
    western-arctic) echo 'S411_cis_SGRDRWA_[0-9A-Za-z]+_pl_a\.zip' ;;
    alaska)         echo 'S411_NWS_full_[0-9]+\.zip' ;;
    north-atlantic) echo 'S411_MetNo_ice[0-9]+\.zip' ;;
    *)              echo "" ;;
  esac
}

region_conc_image() {
  case "$1" in
    cw-greenland)   echo "DMICentralwestCONC.png" ;;
    nw-greenland)   echo "DMINorthWestCONC.png" ;;
    ne-greenland)   echo "DMINorthEastCONC.png" ;;
    se-greenland)   echo "DMISouthEastCONC.png" ;;
    sw-greenland)   echo "DMISouthWestCONC.png" ;;
    ce-greenland)   echo "DMICentralEastCONC.png" ;;
    cape-farewell)  echo "DMICapeFarewellCONC.png" ;;
    qaanaaq)        echo "DMIQaanaaqCONC.png" ;;
    canada-east)    echo "CISEastCoastCONC.png" ;;
    hudson-bay)     echo "CISHudsonbayCONC.png" ;;
    eastern-arctic) echo "CISEasternarcticCONC.png" ;;
    western-arctic) echo "CISWesternarcticCONC.png" ;;
    alaska)         echo "AlaskaCONC.png" ;;
    north-atlantic) echo "NISNorthatlanticCONC.png" ;;
    *)              echo "" ;;
  esac
}

region_sod_image() {
  case "$1" in
    cw-greenland)   echo "DMICentralwestSOD.png" ;;
    nw-greenland)   echo "DMINorthWestSOD.png" ;;
    ne-greenland)   echo "DMINorthEastSOD.png" ;;
    se-greenland)   echo "DMISouthEastSOD.png" ;;
    sw-greenland)   echo "DMISouthWestSOD.png" ;;
    cape-farewell)  echo "DMICapeFarewellSOD.png" ;;
    qaanaaq)        echo "DMIQaanaaqSOD.png" ;;
    canada-east)    echo "CISEastCoastSOD.png" ;;
    hudson-bay)     echo "CISHudsonbaySOD.png" ;;
    eastern-arctic) echo "CISEasternarcticSOD.png" ;;
    western-arctic) echo "CISWesternarcticSOD.png" ;;
    alaska)         echo "AlaskaSOD.png" ;;
    *)              echo "" ;;   # some regions publish no SOD quicklook
  esac
}

ALL_REGIONS=(cw-greenland nw-greenland ne-greenland se-greenland sw-greenland \
  ce-greenland cape-farewell qaanaaq canada-east hudson-bay eastern-arctic \
  western-arctic alaska north-atlantic)

OUT_DIR="${1:-./s411-previews}"
shift || true
SELECTED=("$@")
if [[ ${#SELECTED[@]} -eq 1 && "${SELECTED[0]}" == "all" ]]; then
  SELECTED=("${ALL_REGIONS[@]}")
elif [[ ${#SELECTED[@]} -eq 0 ]]; then
  SELECTED=(cw-greenland hudson-bay alaska)
fi

WIDTH="${WIDTH:-1600}"
HEIGHT="${HEIGHT:-1600}"
PALETTE="${PALETTE:-day}"
EXTRA_OPTS="${EXTRA_OPTS:-}"
COMPARE="${COMPARE:-0}"

# Default invocation: run the built Release DLL. Override via $S100.
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
DEFAULT_DLL="$REPO_ROOT/tools/EncDotNet.S100.Cli/bin/Release/net10.0/s100.dll"
S100="${S100:-dotnet "$DEFAULT_DLL"}"

mkdir -p "$OUT_DIR/data" "$OUT_DIR/previews"

# Cache the portal index once so per-region discovery does not re-fetch it.
PORTAL_CACHE="$OUT_DIR/data/ILP_S411.shtml"
echo "Fetching portal index: $PORTAL_INDEX"
if ! curl -fsSL -o "$PORTAL_CACHE" "$PORTAL_INDEX"; then
  echo "! could not fetch portal index — cannot discover current ZIP names" >&2
  exit 1
fi

echo "Output dir : $OUT_DIR"
echo "CLI        : $S100"
echo "Size       : ${WIDTH}x${HEIGHT}  palette=$PALETTE  compare=$COMPARE"
echo "Regions    : ${SELECTED[*]}"
echo

# Discover the current ZIP file name for a region pattern from the cached index.
# Dates are zero-padded (YYYYMMDD…), so a lexical sort yields chronological order
# and `tail -1` picks the most recent.
discover_zip() {
  local pattern="$1"
  grep -oE 'S411/[A-Za-z0-9_.-]+\.zip' "$PORTAL_CACHE" \
    | sed 's#^S411/##' \
    | grep -E "^$pattern$" \
    | sort \
    | tail -1
}

fetch_reference() {
  # $1 region key, $2 output dir for previews
  local key="$1" pdir="$2"
  local conc sod
  conc="$(region_conc_image "$key")"
  sod="$(region_sod_image "$key")"
  local -a refs=()
  if [[ -n "$conc" ]]; then
    if curl -fsSL -o "$pdir/$key.bsis-conc.png" "$BASE_URL/S411Preview/$conc"; then
      echo "  bsis CONC -> $pdir/$key.bsis-conc.png"
      refs+=("$pdir/$key.bsis-conc.png")
    fi
  fi
  if [[ -n "$sod" ]]; then
    if curl -fsSL -o "$pdir/$key.bsis-sod.png" "$BASE_URL/S411Preview/$sod"; then
      echo "  bsis SOD  -> $pdir/$key.bsis-sod.png"
      refs+=("$pdir/$key.bsis-sod.png")
    fi
  fi
  # Build a combined contact sheet when ImageMagick is available.
  if command -v montage >/dev/null 2>&1 && [[ ${#refs[@]} -gt 0 && -f "$pdir/$key.png" ]]; then
    montage -label 's100 render' "$pdir/$key.png" \
      $(for r in "${refs[@]}"; do echo -label "bsis $(basename "$r" | sed "s/^$key\.bsis-//; s/\.png$//")" "$r"; done) \
      -tile x1 -geometry 400x400+6+6 -background white "$pdir/$key.compare.png" \
      && echo "  compare   -> $pdir/$key.compare.png"
  fi
}

render_region() {
  local key="$1"
  local pattern
  pattern="$(region_zip_pattern "$key")"
  if [[ -z "$pattern" ]]; then
    echo "  ! unknown region '$key' — skipping"
    return 0
  fi

  echo "[$key]"
  local zip_name
  zip_name="$(discover_zip "$pattern")"
  if [[ -z "$zip_name" ]]; then
    echo "  ! no current ZIP matching /$pattern/ on the portal — skipping"
    return 0
  fi
  echo "  current $zip_name"

  local zip_path="$OUT_DIR/data/$key.zip"
  local extract_dir="$OUT_DIR/data/$key"
  local preview="$OUT_DIR/previews/$key.png"

  echo "  downloading $zip_name"
  if ! curl -fsSL -o "$zip_path" "$ZIP_URL/$zip_name"; then
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
    return 0
  fi

  if [[ "$COMPARE" == "1" ]]; then
    fetch_reference "$key" "$OUT_DIR/previews"
  fi
  echo
}

for key in "${SELECTED[@]}"; do
  render_region "$key"
done

echo "Done. Previews in: $OUT_DIR/previews"

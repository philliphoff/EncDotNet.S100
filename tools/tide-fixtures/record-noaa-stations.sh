#!/usr/bin/env bash
# Records NOAA CO-OPS station metadata and NOAA's published subordinate-station
# high and low waters for the tide station catalogue's tests
# (EncDotNet.S100.Tides.Stations, issue #1450). The data is NOAA's (US
# Government work, public domain). Requests carry no personal identifiers.
#
# It writes, under tests/datasets/Tides/NOAA/:
#
#   stations.json.gz           the tide prediction station list with each
#                              subordinate station's offsets (metric), gzipped
#   <reference>/harcon.json    a reference station's harmonic constituents
#   <reference>/datums.json    a reference station's tidal datums
#   <subordinate>/tidepredoffsets.json
#                              a subordinate station's offsets (metric)
#   <subordinate>/mllw-hilo.csv
#                              the year's high and low waters on MLLW, NOAA's
#                              CSV (NOAA publishes no 6-minute series for
#                              subordinate stations)
#
# The reference stations' 6-minute and MSL fixtures come from
# record-noaa-tides.sh.
#
# Usage: tools/tide-fixtures/record-noaa-stations.sh [year]
set -euo pipefail

YEAR="${1:-2026}"

# Seattle and Port Townsend are the reference stations of the Puget Sound
# subordinate stations below.
REFERENCES=(9447130 9444900)
# Tacoma Narrows Bridge and Port Ludlow (ratios, from Seattle) and Marrowstone
# Point (fixed offsets, from Port Townsend).
SUBORDINATES=(9446486 9445017 9444972)

OUT_ROOT="${OUT_ROOT:-tests/datasets/Tides/NOAA}"
MDAPI="https://api.tidesandcurrents.noaa.gov/mdapi/prod/webapi"
DATAGETTER="https://api.tidesandcurrents.noaa.gov/api/prod/datagetter"

fetch() {
  # NOAA returns errors as a 200 with an "error" body, so check the content.
  curl -sSf --retry 3 "$1" -o "$2"
  if grep -q '"error"' "$2" || grep -qi '^ *error' "$2" || grep -q 'No Predictions' "$2"; then
    echo "NOAA returned an error for $1:" >&2
    cat "$2" >&2
    exit 1
  fi
}

mkdir -p "${OUT_ROOT}"
tmp="$(mktemp)"
fetch "${MDAPI}/stations.json?type=tidepredictions&expand=tidepredoffsets&units=metric" "${tmp}"
gzip -9n < "${tmp}" > "${OUT_ROOT}/stations.json.gz"
rm -f "${tmp}"

for station in "${REFERENCES[@]}"; do
  dir="${OUT_ROOT}/${station}"
  mkdir -p "${dir}"
  echo "Recording reference ${station} into ${dir}"
  fetch "${MDAPI}/stations/${station}/harcon.json?units=metric" "${dir}/harcon.json"
  fetch "${MDAPI}/stations/${station}/datums.json?units=metric" "${dir}/datums.json"
done

for station in "${SUBORDINATES[@]}"; do
  dir="${OUT_ROOT}/${station}"
  mkdir -p "${dir}"
  echo "Recording subordinate ${station} (${YEAR}) into ${dir}"
  fetch "${MDAPI}/stations/${station}/tidepredoffsets.json?units=metric" "${dir}/tidepredoffsets.json"
  fetch "${DATAGETTER}?product=predictions&begin_date=${YEAR}0101&end_date=${YEAR}1231&datum=MLLW&station=${station}&time_zone=gmt&units=metric&interval=hilo&format=csv" "${dir}/mllw-hilo.csv"
done

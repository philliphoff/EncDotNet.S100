#!/usr/bin/env bash
# Records NOAA CO-OPS harmonic constituents, datums and NOAA's own published
# tide predictions for the tide prediction engine's tests (EncDotNet.S100.Tides,
# issue #1449). The data is NOAA's (US Government work, public domain).
#
# For each station it writes, under tests/datasets/Tides/NOAA/<station>/:
#
#   harcon.json      the harmonic constituents (metric; amplitude in metres,
#                    phase_GMT is the Greenwich phase lag in degrees)
#   datums.json      the tidal datums (metric, relative to station datum)
#   msl-6min.txt.gz  a year of 6-minute predictions on MSL, heights only: a
#                    "# start=<UTC> step=6" header, then one height in metres
#                    per line (NOAA's CSV, with the time column dropped)
#   msl-hilo.csv     the year's high and low waters on MSL, NOAA's CSV
#   mllw-6min.csv    January's 6-minute predictions on MLLW, NOAA's CSV
#
# Usage: tools/tide-fixtures/record-noaa-tides.sh [year] [station...]
set -euo pipefail

YEAR="${1:-2026}"
shift || true
STATIONS=("$@")
if [ "${#STATIONS[@]}" -eq 0 ]; then
  # Seattle (mixed), Boston (semidiurnal), Pensacola (diurnal),
  # Anchorage (large range), Astoria (strong shallow-water constituents).
  STATIONS=(9447130 8443970 8729840 9455920 9439040)
fi

OUT_ROOT="${OUT_ROOT:-tests/datasets/Tides/NOAA}"
MDAPI="https://api.tidesandcurrents.noaa.gov/mdapi/prod/webapi/stations"
DATAGETTER="https://api.tidesandcurrents.noaa.gov/api/prod/datagetter"

fetch() {
  # NOAA returns errors as a 200 with an "error" body, so check the content.
  curl -sSf --retry 3 "$1" -o "$2"
  if grep -q '"error"' "$2" || grep -qi '^ *error' "$2"; then
    echo "NOAA returned an error for $1:" >&2
    cat "$2" >&2
    exit 1
  fi
}

predictions() {
  # $1 station, $2 datum, $3 begin, $4 end, $5 interval
  echo "${DATAGETTER}?product=predictions&begin_date=$3&end_date=$4&datum=$2&station=$1&time_zone=gmt&units=metric&interval=$5&format=csv"
}

for station in "${STATIONS[@]}"; do
  dir="${OUT_ROOT}/${station}"
  mkdir -p "${dir}"
  echo "Recording ${station} (${YEAR}) into ${dir}"

  fetch "${MDAPI}/${station}/harcon.json?units=metric" "${dir}/harcon.json"
  fetch "${MDAPI}/${station}/datums.json?units=metric" "${dir}/datums.json"

  tmp="$(mktemp)"
  fetch "$(predictions "${station}" MSL "${YEAR}0101" "${YEAR}1231" 6)" "${tmp}"
  {
    echo "# start=${YEAR}-01-01T00:00:00Z step=6"
    tail -n +2 "${tmp}" | cut -d, -f2
  } | gzip -9n > "${dir}/msl-6min.txt.gz"
  rm -f "${tmp}"

  fetch "$(predictions "${station}" MSL "${YEAR}0101" "${YEAR}1231" hilo)" "${dir}/msl-hilo.csv"
  fetch "$(predictions "${station}" MLLW "${YEAR}0101" "${YEAR}0131" 6)" "${dir}/mllw-6min.csv"
done

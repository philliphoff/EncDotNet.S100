#!/usr/bin/env bash
# Records NOAA CO-OPS tidal current harmonic constituents, current prediction
# offsets and NOAA's own published current predictions for the current
# prediction tests (EncDotNet.S100.Tides and EncDotNet.S100.Tides.Stations,
# issue #1454). The data is NOAA's (US Government work, public domain).
# Requests carry no personal identifiers. Needs curl, gzip and jq.
#
# A NOAA current station is a station and a depth bin, written <id>_<bin>
# as NOAA's own offset URLs do. It writes, under
# tests/datasets/Tides/NOAA/Currents/:
#
#   current-stations.json.gz   the current prediction station list, with each
#                              station's offsets and flood and ebb directions
#                              (metric), gzipped
#   <id>_<bin>/harcon.json     a harmonic station's constituents for that bin
#                              only (amplitudes and mean speeds in cm/s,
#                              Greenwich phases in degrees, azi in degrees true)
#   <id>_<bin>/offsets.json    the station's current prediction offsets: flood
#                              and ebb directions, and for a subordinate
#                              station its reference station and differences
#   <id>_<bin>/max-slack.csv   the year's slacks and maximum floods and ebbs,
#                              NOAA's CSV (velocities in cm/s)
#   <id>_<bin>/6min.txt.gz     a harmonic station's January 6-minute velocities
#                              along the major axis: a "# start=<UTC> step=6"
#                              header, then one velocity in cm/s per line
#                              (NOAA's CSV with the other columns dropped)
#
# Usage: tools/tide-fixtures/record-noaa-currents.sh [year]
set -euo pipefail

YEAR="${1:-2026}"

# Deception Pass (mixed, strong), Golden Gate (mixed), The Race
# (semidiurnal) and Mobile Bay Entrance (diurnal).
HARMONIC=(PUG1701_33 SFB1202_17 LIS1001_13 MOB1101_2)
# Golden Gate Bridge, 0.8 mile east of (from SFB1202_17), and Montauk Point,
# 1.2 miles east of (from LIS1001_13, large negative differences).
SUBORDINATE=(PCT0281_1 ACT2401_1)

OUT_ROOT="${OUT_ROOT:-tests/datasets/Tides/NOAA/Currents}"
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

predictions() {
  # $1 station, $2 bin, $3 begin, $4 end, $5 interval
  echo "${DATAGETTER}?product=currents_predictions&begin_date=$3&end_date=$4&station=$1&bin=$2&time_zone=gmt&units=metric&interval=$5&format=csv"
}

mkdir -p "${OUT_ROOT}"
tmp="$(mktemp)"
fetch "${MDAPI}/stations.json?type=currentpredictions&expand=currentpredictionoffsets&units=metric" "${tmp}"
gzip -9n < "${tmp}" > "${OUT_ROOT}/current-stations.json.gz"

record() {
  # $1 <id>_<bin>, $2 harmonic|subordinate
  local key="$1" id="${1%_*}" bin="${1##*_}" dir="${OUT_ROOT}/$1"
  mkdir -p "${dir}"
  echo "Recording $2 ${key} (${YEAR}) into ${dir}"
  fetch "${MDAPI}/stations/${key}/currentpredictionoffsets.json" "${dir}/offsets.json"
  fetch "$(predictions "${id}" "${bin}" "${YEAR}0101" "${YEAR}1231" MAX_SLACK)" "${dir}/max-slack.csv"
  if [ "$2" = harmonic ]; then
    fetch "${MDAPI}/stations/${id}/harcon.json?units=metric" "${tmp}"
    jq --argjson bin "${bin}" '{units, HarmonicConstituents: [.HarmonicConstituents[] | select(.binNbr == $bin)]}' \
      "${tmp}" > "${dir}/harcon.json"
    fetch "$(predictions "${id}" "${bin}" "${YEAR}0101" "${YEAR}0131" 6)" "${tmp}"
    {
      echo "# start=${YEAR}-01-01T00:00:00Z step=6"
      tail -n +2 "${tmp}" | cut -d, -f3
    } | gzip -9n > "${dir}/6min.txt.gz"
  fi
}

for station in "${HARMONIC[@]}"; do record "${station}" harmonic; done
for station in "${SUBORDINATE[@]}"; do record "${station}" subordinate; done
rm -f "${tmp}"

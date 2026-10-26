#!/usr/bin/env bash
# Fetches a small NOAA ERDDAP subset to use as a real-world NetCDF-3 fixture
# for the NetCDF reader (issue #1071; design note docs/design/weather-sources.md
# §7.3). ERDDAP's griddap ".nc" output is NetCDF-3 classic, with time as the
# record dimension and longitudes 0 to 360.
#
# The data is NOAA's (US Government work, public domain). Keep the subset tiny
# (a few grid points, 2-3 time steps, well under 100 KB) and commit it as
# tests/datasets/NetCDF/erddap-ww3-subset.nc.
#
# The default request is the NWW3 global wave model on CoastWatch West Coast
# ERDDAP: significant wave height (Thgt) and mean wave direction (Tdir), the
# last three time steps, 4 x 4 points off Cape Cod (40-41.5N, 70.5-69W =
# 289.5-291E). Override any of the variables below to fetch something else, e.g.
#
#   ERDDAP=https://coastwatch.pfeg.noaa.gov/erddap DATASET=NWW3_Global_Best \
#     LAT0=40 LAT1=41.5 LON0=289.5 LON1=291 tools/weather-fixtures/fetch-erddap-fixture.sh
#
# Usage: tools/weather-fixtures/fetch-erddap-fixture.sh [output.nc]
set -euo pipefail

ERDDAP="${ERDDAP:-https://coastwatch.pfeg.noaa.gov/erddap}"
DATASET="${DATASET:-NWW3_Global_Best}"
VARIABLES="${VARIABLES:-Thgt Tdir}"
TIMES="${TIMES:-(last-2):1:(last)}"
DEPTH="${DEPTH:-(0.0)}"
LAT0="${LAT0:-40}"
LAT1="${LAT1:-41.5}"
LON0="${LON0:-289.5}"
LON1="${LON1:-291}"
OUT="${1:-tests/datasets/NetCDF/erddap-ww3-subset.nc}"

query=""
for variable in ${VARIABLES}; do
  [ -n "${query}" ] && query="${query},"
  query="${query}${variable}[${TIMES}][${DEPTH}][(${LAT0}):1:(${LAT1})][(${LON0}):1:(${LON1})]"
done

# griddap wants [ ] and spaces percent-encoded.
encoded=$(printf '%s' "${query}" | sed -e 's/\[/%5B/g' -e 's/\]/%5D/g' -e 's/ /%20/g')
url="${ERDDAP}/griddap/${DATASET}.nc?${encoded}"

mkdir -p "$(dirname "${OUT}")"
echo "Fetching ${url}"
curl --fail --show-error --silent --location --output "${OUT}" "${url}"

size=$(wc -c < "${OUT}")
magic=$(head -c 4 "${OUT}" | od -An -c | tr -d ' ')
echo "Wrote ${OUT} (${size} bytes, starts with ${magic})"
if [ "${size}" -gt 102400 ]; then
  echo "warning: the subset is over 100 KB; narrow the box or the times before committing it" >&2
fi
case "${magic}" in
  CDF*) ;;
  *) echo "error: ${OUT} isn't NetCDF-3 (ERDDAP may have returned an error page)" >&2; exit 1 ;;
esac

cat <<EOF

Next steps:
  1. Check it reads:   dotnet run --project tools/EncDotNet.S100.Cli -- info ${OUT}
  2. Render it:        dotnet run --project tools/EncDotNet.S100.Cli -- render ${OUT} erddap.png
  3. Commit it and add a test in tests/EncDotNet.S100.Datasets.NetCdf.Tests that
     opens it with NetCdfWeatherDataset.Open (see NetCdfWeatherDatasetTests).
EOF

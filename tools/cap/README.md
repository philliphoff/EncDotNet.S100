# CAP build scripts

Scripts that build the committed data of `EncDotNet.S100.Datasets.Cap`
(issue #1069). They run once, by hand; the build doesn't use them.

## The marine zone layer

`src/EncDotNet.S100.Datasets.Cap/Resources/MarineZones.geojson` (1.6 MB) is
built straight from the NWS coastal (`mz16ap26.zip`) and offshore
(`oz16ap26.zip`) marine zone shapefiles at
<https://www.weather.gov/gis/MarineZones>: 699 zones (569 coastal, 130
offshore), one per shapefile record, simplified with Douglas-Peucker at
0.005° (about 500 m), coordinates to four decimals, 77,246 vertices. Each
feature carries the record's `ID` (its UGC code) as `id`, and its `NAME` and
forecast office (`WFO`) as `name` and `wfo`. NWS data is a US Government work
in the public domain.

`fetch_marine_zones.py` downloads one release's two zips; the release is the
date part of the file names on the page (use the newest). `build_marine_zones.py`
reads the zips with pyshp. The downloads are untrusted, so keep them in a
fresh directory outside the repository, and install pyshp in a virtual
environment outside it too:

```bash
python3 -m venv "$TMPDIR/cap-venv" && "$TMPDIR/cap-venv/bin/pip" install pyshp
mkdir "$TMPDIR/zones"
"$TMPDIR/cap-venv/bin/python" -I tools/cap/fetch_marine_zones.py "$TMPDIR/zones" 16ap26
"$TMPDIR/cap-venv/bin/python" -I tools/cap/build_marine_zones.py "$TMPDIR/zones" \
    src/EncDotNet.S100.Datasets.Cap/Resources/MarineZones.geojson 0.005
```

The NWS publishes a new release a few times a year. The layer only needs
rebuilding when zones are added, renumbered or redrawn. Codes the layer
doesn't have are listed by the processor, not dropped. A host can also load
its own layer with `MarineZoneLayer.Load`.

## The hatching symbols

`make_hatch_symbols.py` writes `src/EncDotNet.S100.Datasets.Cap/Symbols/CAPHATCH*.svg`,
one tile of each severity's hatching. Change `CapStyle.Version` when they change.

# CAP build scripts

Scripts that build the committed data of `EncDotNet.S100.Datasets.Cap`
(issue #1069). They run once, by hand; the build doesn't use them.

## The marine zone layer

`src/EncDotNet.S100.Datasets.Cap/Resources/MarineZones.geojson` (798 KB) is
the NWS coastal (`mz18mr25`) and offshore (`oz18mr25`) marine zone
shapefiles from <https://www.weather.gov/gis/MarineZones>: 696 zones,
simplified with Douglas-Peucker at 0.005° (about 500 m), coordinates to four
decimals, 35,611 vertices. Each feature carries its UGC code as `id`, and its
`name` and forecast office (`wfo`). NWS data is a US Government work in the
public domain.

```bash
python3 -I tools/cap/fetch_marine_zones.py /tmp/zones
python3 -I tools/cap/build_marine_zones.py /tmp/zones \
    src/EncDotNet.S100.Datasets.Cap/Resources/MarineZones.geojson 0.005
```

weather.gov wasn't reachable from the build environment, so
`fetch_marine_zones.py` reads the per-zone files of KDE's FOSS Public Alert
Server (its GitHub mirror, `KDE/foss-public-alert-server`, at
`foss_public_alert_server/alertHandler/data/UGC/<UGC>.geojson`), whose
`generate_UGC.py` wrote each feature of the `mz18mr25` and `oz18mr25`
shapefiles unchanged with pyshp: the NWS geometry and attributes, with no
editing. To build from the NWS files directly, unzip `mz18mr25.zip` and
`oz18mr25.zip` (or a newer release), write each shapefile record as one
GeoJSON feature per file (with pyshp, `shapefile.Reader(...).shapeRecords()`
and each record's `__geo_interface__`), and run `build_marine_zones.py` on
that directory.

The NWS publishes a new release a few times a year. The layer only needs
rebuilding when zones are added, renumbered or redrawn. Codes the layer
doesn't have are listed by the processor, not dropped. A host can also load
its own layer with `MarineZoneLayer.Load`.

## The hatching symbols

`make_hatch_symbols.py` writes `src/EncDotNet.S100.Datasets.Cap/Symbols/CAPHATCH*.svg`,
one tile of each severity's hatching. Change `CapStyle.Version` when they change.

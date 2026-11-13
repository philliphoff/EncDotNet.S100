#### Agent guidance

`tiles serve` serves raster tiles on XYZ URLs, as WMTS and as WMS. Its input is either a built
tile set — a `{z}/{x}/{y}` directory, a `.pmtiles` archive or a `.mbtiles`
database — or datasets in the same forms `tiles export` takes (one dataset,
repeated `--layer`, an exchange set, or a folder of datasets), which are
rendered as their tiles are requested. A directory counts as a built tile set
only when it holds a `tiles.json` or numeric `{z}/{x}` folders; any other
directory that isn't an S-100 exchange set is searched recursively for
datasets, as `mcp serve` searches it (S-57 `ENC_ROOT` folders with a
`CATALOG.031` included). Only PNG, JPEG and WebP raster tiles are served; a
vector (MVT) tile set exits with code `2`.

Routes, under `/<token>/` when a token is in use: `{z}/{x}/{y}.<ext>` for
tiles (XYZ rows, numbered from the north, also for MBTiles), `tiles.json` for
a TileJSON 3.0.0 document whose `tiles` URL is absolute, and `/` for a
MapLibre preview page unless `--no-viewer` is given. When rendering datasets,
`day/`, `dusk/` and `night/` prefix the same routes in that palette, and the
TileJSON's `s100.palettes` lists them; `--palette` picks the palette of the
unprefixed routes. A tile with nothing drawn, or outside the set, answers
`204 No Content`; a malformed or out-of-range request, an unknown palette or
the wrong extension answers `404`. Every response sends
`Access-Control-Allow-Origin: *`. `/healthz` answers `200 OK` with `ok`
outside any token; `s100 health` asks it and exits `0` when it answers.

The same tiles are an OGC WMTS 1.0.0 service: the capabilities document is at
`wmts/1.0.0/WMTSCapabilities.xml` (also `wmts?SERVICE=WMTS&REQUEST=GetCapabilities`),
with one layer, `s100`, on the `WebMercatorQuad` tile matrix set (EPSG:3857,
matrices 0 to the maximum zoom, identifiers are the zoom numbers, limits from
the minimum zoom). A WMTS tile is the XYZ tile with `TILEMATRIX` = z,
`TILECOL` = x and `TILEROW` = y. GetTile works as KVP
(`wmts?SERVICE=WMTS&REQUEST=GetTile&LAYER=s100&STYLE=…&TILEMATRIXSET=WebMercatorQuad&TILEMATRIX=…&TILEROW=…&TILECOL=…`)
and RESTful (`wmts/1.0.0/s100/{Style}/[{Time}/]WebMercatorQuad/{z}/{y}/{x}.<ext>`).
Palettes are styles (the `--palette` one is the default); a built tile set has
the single style `default`. Time-varying datasets add a `Time` dimension (KVP
`TIME=`, or the extra REST segment) listing the steps, defaulting to
`--time-step`. Empty tiles answer `204`; bad requests answer an OWS
`ExceptionReport` with status 400 (501 for unsupported operations). Point QGIS
(Layer > Add WMS/WMTS Layer), ArcGIS Pro or GDAL (`WMTS:<capabilities URL>`)
at the capabilities URL that the startup output prints.

The same server is an OGC WMS 1.3.0 service at `wms` (KVP only;
`SERVICE=WMS&REQUEST=GetCapabilities|GetMap|GetFeatureInfo`). One layer,
`s100`; CRSs `EPSG:3857` (aliases 900913, 102100), `EPSG:4326` (latitude-first
BBOX in 1.3.0, longitude-first in 1.1.1) and `CRS:84`; styles are the palettes
with the `--palette` one first; `TIME` picks the step. GetMap composes from
the XYZ tiles of the nearest zoom (symbols 0.7–1.4× their size; nothing below
the minimum zoom, whose scale is the layer's `MaxScaleDenominator`), PNG or
JPEG, at most 4096 px a side, opaque on `BGCOLOR` (white) unless
`TRANSPARENT=TRUE`. GetFeatureInfo is offered only when rendering datasets (a
built tile set's layer has `queryable="0"`): it opens the datasets into an
identify catalogue on first use and reports what `s100 identify --attributes`
would at the pixel centre (`I`/`J`, or 1.1.1 `X`/`Y`), matching points and
curves within 5 px, up to `FEATURE_COUNT` (default 1, max 200) features, as
`text/html`, `text/plain` or GeoJSON (`application/json`,
`application/geo+json`; each result is a Point at the click). Errors are a WMS
`ServiceExceptionReport` with status 400.

For time-varying datasets (S-104, S-111), add `?t=<ISO 8601>` to a tile or
`tiles.json` URL to pick the time step: it snaps to the nearest of the steps
listed in TileJSON `s100.times`, the chosen one is echoed in `s100.time` and in
the TileJSON tile URL, and each dataset uses its own nearest step. Without `t`,
`--time-step` applies; an unparseable `t` answers 400; datasets without time
steps ignore it. Vector datasets are portrayed once per palette and reused
across time steps; only the most recent eight palette/time combinations are
kept prepared.

Rendering uses the `tiles export` display, zoom and area options, renders
`--metatile`-square blocks around each requested tile, keeps up to `--cache-mb`
of tiles in memory, and runs at most `--parallel` renders at once. The first
request for a block, and for each palette, is slow; later ones are cached.
When rendering datasets, their files (paths, sizes, write times, update
files, every file of an exchange set or folder, so a cell copied into or
removed from a served folder counts) are re-checked every `--refresh` seconds
(default 10; `0` disables). A change that has held for two consecutive checks
reopens and re-portrays the datasets in the background; requests switch over
when that's done, with an empty memory cache and a new disk-cache fingerprint,
and the output logs the reload. A reopen that fails keeps the previous data
serving and is retried only after the files change again.

`--cache-dir <folder>` also keeps rendered tiles on disk across runs, under a
fingerprint of the input files (path, size, write time) and the rendering
options, so a restart with unchanged data and settings serves them without
rendering, and changed ones never reuse stale tiles. `--cache-dir-mb` caps the
folder (default 1024, least recently used evicted first, across fingerprints);
`--clear-cache` empties it first. Only 32-hex-character fingerprint folders
are touched. `--cache-dir` is rejected when serving a built tile set.

The server listens on `127.0.0.1:8200` by default. With a non-loopback
`--host`, a random token is added to every URL unless `--token` or
`--no-token` is given; read the URLs from the startup output. A built tile set
is read at each request, so exporting it again while it is served is picked up
without a restart. The process runs until Ctrl-C or SIGTERM, so start it in
the background in scripts.

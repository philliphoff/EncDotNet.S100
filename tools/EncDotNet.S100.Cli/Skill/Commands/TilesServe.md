#### Agent guidance

`tiles serve` serves raster tiles on XYZ URLs. Its input is either a built
tile set — a `{z}/{x}/{y}` directory, a `.pmtiles` archive or a `.mbtiles`
database — or datasets in the same forms `tiles export` takes (one dataset,
repeated `--layer`, or an exchange set), which are rendered as their tiles are
requested. A directory that is not an exchange set is treated as a tile set;
pass a folder of datasets with `--layer`. Only PNG, JPEG and WebP raster tiles
are served; a vector (MVT) tile set exits with code `2`.

Routes, under `/<token>/` when a token is in use: `{z}/{x}/{y}.<ext>` for
tiles (XYZ rows, numbered from the north, also for MBTiles), `tiles.json` for
a TileJSON 3.0.0 document whose `tiles` URL is absolute, and `/` for a
MapLibre preview page unless `--no-viewer` is given. When rendering datasets,
`day/`, `dusk/` and `night/` prefix the same routes in that palette, and the
TileJSON's `s100.palettes` lists them; `--palette` picks the palette of the
unprefixed routes. A tile with nothing drawn, or outside the set, answers
`204 No Content`; a malformed or out-of-range request, an unknown palette or
the wrong extension answers `404`. Every response sends
`Access-Control-Allow-Origin: *`.

Rendering uses the `tiles export` display, zoom and area options, renders
`--metatile`-square blocks around each requested tile, keeps up to `--cache-mb`
of tiles in memory, and runs at most `--parallel` renders at once. The first
request for a block, and for each palette, is slow; later ones are cached.

The server listens on `127.0.0.1:8200` by default. With a non-loopback
`--host`, a random token is added to every URL unless `--token` or
`--no-token` is given; read the URLs from the startup output. A built tile set
is read at each request, so exporting it again while it is served is picked up
without a restart. The process runs until Ctrl-C or SIGTERM, so start it in
the background in scripts.

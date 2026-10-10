#### Agent guidance

`tiles serve` serves a tile set that `tiles export` (or another tool) already
built: a `{z}/{x}/{y}` directory, a `.pmtiles` archive or a `.mbtiles`
database. It doesn't render; export the tiles first. Only PNG, JPEG and WebP
raster tiles are served; a vector (MVT) tile set exits with code `2`.

Routes, under `/<token>/` when a token is in use: `{z}/{x}/{y}.<ext>` for
tiles (XYZ rows, numbered from the north, also for MBTiles), `tiles.json` for
a TileJSON 3.0.0 document whose `tiles` URL is absolute, and `/` for a
MapLibre preview page unless `--no-viewer` is given. A tile the set doesn't
have answers `204 No Content`; a malformed or out-of-range request, or the
wrong extension, answers `404`. Every response sends
`Access-Control-Allow-Origin: *`.

The server listens on `127.0.0.1:8200` by default. With a non-loopback
`--host`, a random token is added to every URL unless `--token` or
`--no-token` is given; read the URLs from the startup output. The tile set is
read at each request, so exporting it again while it is served is picked up
without a restart. The process runs until Ctrl-C or SIGTERM, so start it in
the background in scripts.

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
files, the whole of an exchange set) are re-checked every `--refresh` seconds
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

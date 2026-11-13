#### Agent guidance

`tiles export` takes the same input forms as `render`: one dataset, repeated
`--layer`, or an exchange set (positional, `--from` or `--exchange-set`). It
also takes a plain folder of datasets, searched recursively as `mcp serve`
searches one; an S-57 exchange set (`ENC_ROOT` with `CATALOG.031`, positional
or `--from`) is read that way. In a folder, loose S-101 cells get their sibling
updates unless `--no-updates`; cells in a nested S-100 exchange set don't, and
datasets that can't be opened or tiled are skipped with a warning.
`-o|--output` is required. An output ending in `.pmtiles` writes one PMTiles
v3 archive, `.mbtiles` one MBTiles 1.3 SQLite database (TMS rows, as the
format requires) and `.gpkg` one OGC GeoPackage 1.4 tile pyramid (EPSG:3857
Web Mercator grid, tile table `chart`, XYZ rows) for GIS desktops and offline
field apps; any other output is a directory of `{z}/{x}/{y}.<ext>` files
plus a `tiles.json` TileJSON document. Use `--container` to choose explicitly.
An existing archive file at the output path is replaced.

Each zoom level draws only what is visible at its scale (SCAMIN and each
cell's minimum display scale), measured at the tiled area's centre latitude
(recorded as `scaleLatitude` in the metadata). Without `--min-zoom` and
`--max-zoom`, the range comes from the datasets' display scales, or from their
extent when they have none. The command prints the tile count before
rendering; above 100000 tiles it stops unless `--yes` is given, so narrow
`--bbox` or the zoom range first.

Tiles are transparent by default so they overlay a web basemap; use
`--background` for opaque tiles and always with `--format jpeg`.
`--tile-size 512` renders high-DPI tiles of the standard 256-pixel grid;
declare the source with `tileSize: 256` in the web map. One tile set holds one
palette and one `--time-step`; run the command again for others.
Where cells of different scales overlap, the finer cell hides the coarser one
at each zoom where the finer cell draws, as in SoundCharts.

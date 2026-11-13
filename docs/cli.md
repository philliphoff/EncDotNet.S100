# Command-line rendering

`s100` is a cross-platform command-line tool for S-100 datasets. It renders one
dataset, or a composite of several, to a PNG, JPEG or WebP image, or to a
raster tile set for web maps. It also
validates datasets and exchange sets, reports what a dataset contains, picks
the features at a point, checks routes, converts S-57 cells, serves datasets to
AI agents over MCP, and publishes datasets as an S-100 feed.

`s100` uses the same portrayal and validation code as the `EncDotNet.S100`
library and SoundCharts. It renders through headless Skia renderers, so it
needs no display and suits batch scripts and CI pipelines.

## Install

Download the `s100` archive for your platform from the latest
[GitHub release](https://github.com/philliphoff/EncDotNet.S100/releases). Each
archive is self-contained: it bundles the .NET runtime and SkiaSharp's native
libraries, so you don't need to install .NET.

| Platform | Asset |
|---|---|
| Windows x64 | `s100-<version>-win-x64.zip` |
| Windows arm64 | `s100-<version>-win-arm64.zip` |
| macOS (Apple silicon) | `s100-<version>-osx-arm64.tar.gz` |
| Linux x64 | `s100-<version>-linux-x64.tar.gz` |
| Linux arm64 | `s100-<version>-linux-arm64.tar.gz` |

On macOS or Linux, extract the archive and run the tool from the extracted
folder:

```bash
tar -xzf s100-<version>-<rid>.tar.gz
./s100 list-specs
```

On Windows, extract the `.zip` and run `s100.exe list-specs`.

The macOS binary is code-signed and notarized, and Gatekeeper checks the
notarization online the first time it runs. If macOS still quarantines the
binary, for example after you copy it from another machine, clear the
attribute:

```bash
xattr -d com.apple.quarantine ./s100
```

The Windows `s100.exe` is Authenticode-signed.

On Linux, every command needs the ICU library for .NET globalization. Minimal
and container images often leave it out, and without it `s100` stops at
startup with a `Couldn't find a valid ICU package` error. On Ubuntu 24.04
install `libicu74`; on Debian 12 install `libicu72`:

```bash
sudo apt-get update
sudo apt-get install -y libicu74
```

`render` draws labels with a font built into the renderer, so fontconfig is
optional. Install `fontconfig` and a font package such as `fonts-dejavu-core`
if you want labels in a system font, or to silence the `Fontconfig error:
Cannot load default config file` warning.

### Nightly builds

Nightly builds of `s100` are published from `main` to the
[`nightly` prerelease](https://github.com/philliphoff/EncDotNet.S100/releases/tag/nightly)
when `main` has changed since the last one. Use them to try a fix or feature
before it's released. They're unsupported: they pass CI but get no other
testing, and they can have bugs or unfinished features.

The release's assets keep the same names, so their links always get the newest
nightly:

| Platform | Asset |
|---|---|
| Windows x64 | `s100-nightly-win-x64.zip` |
| Windows arm64 | `s100-nightly-win-arm64.zip` |
| macOS (Apple silicon) | `s100-nightly-osx-arm64.tar.gz` |
| Linux x64 | `s100-nightly-linux-x64.tar.gz` |
| Linux arm64 | `s100-nightly-linux-arm64.tar.gz` |

Install and run a nightly the same way as a release. Its version is the next
patch release plus the build date, such as `0.25.2-nightly.20261022`, and
`s100 --version` marks it as a nightly build. A nightly still shows
[update notices](#update-notices) for newer stable releases. To go back to a
stable build, install the archive from the
[latest release](https://github.com/philliphoff/EncDotNet.S100/releases/latest)
in its place.

### Docker

The `ghcr.io/philliphoff/s100` container image runs `s100` on Linux x64 and
arm64, with no other install. See [Run s100 in Docker](docker.md).

### The copy inside SoundCharts

SoundCharts ships the same `s100` executable in a `cli/` folder:

| Platform | Location |
|---|---|
| macOS | `SoundCharts.app/Contents/MacOS/cli/s100` |
| Windows | `cli/s100.exe` next to the app executable |
| Linux | `cli/s100` next to the app executable |

On macOS this copy is signed with the app, so Gatekeeper doesn't prompt for it.

### Run from source

From a clone of the repository, run the tool with the .NET SDK. Arguments
after `--` go to `s100`:

```bash
dotnet run --project tools/EncDotNet.S100.Cli -- list-specs
```

## Commands

| Command | What it does |
|---|---|
| [`render`](#render) | Renders a dataset, a composite of datasets, or a whole exchange set to an image, or writes a dataset's display list as JSON. |
| [`tiles export`](#tiles-export) | Renders a dataset, a composite of datasets, or a whole exchange set as XYZ raster tiles: a `{z}/{x}/{y}` folder, a PMTiles archive, an MBTiles database, a GeoPackage or a GeoTIFF. |
| [`tiles serve`](#tiles-serve) | Serves a built tile set or datasets on XYZ URLs and as WMTS and WMS services, for web maps and GIS desktops on this or another computer. |
| [`info`](#info) | Shows the detected product specification, edition, render support, display modes and time steps. |
| [`identify`](#identify) | Lists the features and coverage values at a latitude and longitude, including weather grids and warnings. |
| [`validate`](#validate) | Checks a dataset against its specification's rule pack, or checks an exchange set's signatures and checksums. |
| [`route check`](#route-check) | Checks an RTZ or S-421 route against datasets, and its under-keel clearance when it has a draft. |
| [`tides stations`](#tides-stations) | Lists tide stations near a point, from the offline station catalogue. |
| [`tides predict`](#tides-predict) | Predicts the tide at a station, as heights, highs and lows, or an S-104 dataset. |
| [`tides currents stations`](#tides-currents-stations) | Lists tidal current stations near a point, from the offline station catalogue. |
| [`tides currents predict`](#tides-currents-predict) | Predicts the tidal current at a station, as speeds and directions, slacks and maxima, or an S-111 dataset. |
| [`tides update`](#tides-update) | Downloads NOAA's tide and current stations into the offline catalogue, or checks for newer data. |
| [`list-specs`](#list-specs) | Lists the supported product specifications. |
| [`health`](#health) | Checks that a `tiles serve` or `feed serve` on this computer answers, for container health checks. |
| [`s57 convert`](#s57-convert) | Converts an S-57 cell to an S-101 dataset, or an inland ENC cell to S-401. |
| [`s101 convert`](#s101-convert) | Converts an S-101 dataset to an S-57 cell or exchange set, for testing and S-57-only tools. |
| [`mcp serve`](#mcp-serve) | Serves the S-100 MCP tools over standard input and output for a set of datasets. |
| [`feed serve`](#feed-serve) | Serves a folder, exchange set or dataset as an S-100 feed over HTTP. |
| [`feed export`](#feed-export) | Writes a folder, exchange set or dataset as a static S-100 feed. |

These options work without a command:

| Option | What it does |
|---|---|
| `--help` | Prints help. Add it after a command for that command's help, for example `s100 render --help`. |
| `--version` | Prints the tool's version. Nightly builds add `(nightly build, unsupported)`. |
| `--skill` | Prints a single Markdown document that describes every command, option, example, exit code and limitation. It's written for AI agents, has no ANSI styling, and skips the update check. |

### Update notices

Release and nightly builds check the repository's latest GitHub release at most
once every 24 hours. The latest release is always a stable one, never a nightly. When a newer version exists, each run writes one line to standard
error:

```text
Update available: s100 0.25.0 (current 0.24.0): https://github.com/philliphoff/EncDotNet.S100/releases/tag/v0.25.0
```

Standard output and the exit code don't change, so you can still parse JSON
output. Network and cache failures are silent. Development builds don't check.
To turn the check off, set the `S100_NO_UPDATE_CHECK` environment variable to
`1`, `true` or `yes`. The [container image](docker.md) sets it.

## render

```text
s100 render <dataset> <output> [options]
s100 render --layer <dataset> [--layer <dataset> ...] <output> [options]
s100 render <exchange-set> <output> [options]
```

`render` detects each input's product specification, runs its portrayal, and
writes an image. S-104 defines no portrayal, so S-104 is drawn with
SoundCharts' own gauges and isolines; see
[S-104 portrayal](../src/EncDotNet.S100.Datasets.S104/README.md#portrayal).
It has three forms:

- **Single dataset.** Pass a dataset file and an output path.
- **Composite.** Pass `--layer` once per dataset. The datasets are drawn into
  one image in the order the S-98 interoperability rules give them. Give the
  output path last, or with `-o`.
- **Exchange set.** Pass a folder that holds a `CATALOG.XML`, a `CATALOG.XML`
  file, or a `.zip` with one at its root. `render` composites every dataset it
  can render. `s100` recognises an exchange set given as the first argument;
  you can also name it with `--exchange-set` or `--from`.

The output format comes from the output file's extension: `.png`, `.jpg` or
`.jpeg`, `.webp`, or `.json` for a [display list](#display-list-output). Any
other extension writes PNG, unless you set `--format`.

### Render options

| Option | Default | Description |
|---|---|---|
| `--layer <path>` | none | Adds a dataset as a composite layer. Repeat it for each dataset. |
| `--exchange-set`, `--from <path>` | none | Composites an exchange set: a folder with `CATALOG.XML`, the `CATALOG.XML` file, or a `.zip`. Can't be combined with `--layer`. |
| `--only <specs>` | all | Exchange-set form only. Composites only these product specifications, comma-separated, for example `--only S101,S128`. Hyphens and case are ignored. |
| `-o`, `--output <path>` | positional | The output path. Use it instead of the positional output argument. |
| `-w`, `--width <pixels>` | `1024` | Image width. |
| `-h`, `--height <pixels>` | `768` | Image height. |
| `--format <format>` | from the extension, else `png` | `png`, `jpeg` (or `jpg`), `webp` or `json`. A `--format` that contradicts a recognised output extension is an error. `json` works in the single-dataset form only. |
| `--quality <1-100>` | `90` | Encoder quality for `jpeg` and `webp`. Ignored for `png`. |
| `--palette <palette>` | `day` | `day`, `dusk` or `night`. |
| `--national-language <code>` | the catalogue's, `eng` | ISO 639-2 language, such as `fra`, to prefer for chart text where the product's portrayal supports it (S-101, S-131). A feature with no name in that language shows its default name. |
| `--symbol-scale <factor>` | `1.0` | Symbol scale factor. |
| `--text-scale <factor>` | `1.0` | Text scale factor. |
| `--time-step <index>` | `0` | Zero-based time step for time-series datasets (S-104, S-111, GRIB2, NetCDF, CAP). `s100 info` lists the steps. |
| `--background <hex>` | opaque white | Background colour, `#RRGGBB` or `#AARRGGBB`. |
| `--bbox <minLon,minLat,maxLon,maxLat>` | fit to the data | Draws this WGS-84 bounding box. Can't be combined with `--center` and `--scale`. |
| `--center <lon,lat>` | fit to the data | Centre of the view. Use with `--scale`. |
| `--scale <denominator>` | fit to the data | Scale denominator, for example `50000` for 1:50,000. Use with `--center`. |
| `--no-text` | off | Hides text. Same as `--hide text`. |
| `--hide <categories>` | none | Hides drawing-instruction categories, comma-separated: `text`, `points`, `lines`, `areas`. Adds to `--no-text`. |
| `--basemap <mode>` | `none` | `offline` draws the bundled Natural Earth 1:10m land layer under the chart, in a parchment tone (`238,232,220`). Online tile basemaps aren't available. |
| `--display-mode <mode>` | `ice-concentration` | S-411 only. `ice-concentration`, `ice-sod` (stage of development), or `ice-navigational`. See [Sea-ice display modes](#sea-ice-display-modes). |
| `--show-layer <keys>` | none | Single-dataset form only. Draws sub-layers that are off by default, comma-separated by key, as the SoundCharts Display controls switches do: for example `weather.precipitation,weather.cape,weather.cloud`. `s100 info` lists a dataset's layer keys. |
| `--no-updates` | off | Single-dataset form only. Renders an S-101 base cell without its update files. See [S-101 updates](#s-101-updates). |
| `--lua-engine <engine>` | `luacsharp` | The Lua engine that runs the portrayal rules: `luacsharp` or `moonsharp`. See [Lua engine](#lua-engine). |
| `--debug` | off | Prints full stack traces on error, and portrayal and Lua diagnostics on standard error. |

`--hide`, `--no-text` and `--basemap` apply to every layer of a composite.

### Render examples

```bash
s100 render currents.h5 currents.png --time-step 6 --palette night
s100 render warnings.gml warnings.png --width 2048 --height 1536
s100 render warnings.gml warnings.jpg --quality 85
s100 render warnings.gml preview.webp
s100 render seaice.gml seaice.png --no-text
s100 render seaice.gml seaice.png --basemap offline
s100 render chart.gml chart.png --hide text,points
s100 render enc.000 window.png --bbox -1.5,50.0,-1.0,50.5
s100 render enc.000 window.png --center -1.25,50.25 --scale 50000
s100 render bathy.h5 window.png --bbox -1.5,50.0,-1.0,50.5
```

Composite several datasets:

```bash
s100 render --layer enc.000 --layer bathy.h5 --layer warnings.gml chart.png
s100 render --layer enc.000 --layer bathy.h5 -o chart.png --bbox -1.5,50.0,-1.0,50.5
s100 render --layer enc.000 --layer warnings.gml chart.png --basemap offline
```

Composite an exchange set:

```bash
s100 render exchange-set/ chart.png
s100 render exchange-set/CATALOG.XML chart.png
s100 render --exchange-set exchange-set.zip -o chart.png
s100 render --from exchange-set/ chart.png --only S101,S102
```

### Viewport

Without `--bbox` or `--center` and `--scale`, `render` fits the image to the
dataset's extent, or to the combined extent of every layer. The fit handles
the antimeridian: data that crosses ±180°, such as an S-411 product spanning
175°E to 225°E, is framed on its own narrow extent instead of the whole world.

With an explicit viewport:

- A single vector dataset also applies scale visibility (S-100 Part 9), so
  features outside their display-scale range are hidden.
- Gridded S-102, S-104 and S-111 coverages sample only the cells inside the
  viewport. For a projected grid, the window is transformed into the grid's
  own CRS, and the sampled cells are reprojected for output.
- Station and node datasets (S-104, S-111) draw their glyphs in the viewport.
- Any part of the image outside the data shows the background or basemap.

### Compositing multiple datasets

- **Layer order.** The S-98 interoperability rules order layers by display
  plane. The order of your `--layer` options only breaks ties within a plane,
  so it usually has no visible effect. For example, an S-102 surface already
  draws above an S-101 chart.
- **Overlapping cells.** Where a finer chart cell's data coverage overlaps a
  coarser cell, the coarser cell is hidden there, as in SoundCharts. A cell's
  data coverage is the union of its coverage areas, and a finer cell takes part
  only where its coverage meets the coarser cell's, not just its bounding box.
  Cells are ranked by compilation scale, or by minimum display scale when a
  cell has none. Between cells of the same minimum display scale, the one with
  the finer maximum display scale ranks finer. Cells of the same rank don't hide
  each other.
- **No S-101 updates.** The composite and exchange-set forms don't apply S-101
  update files. To render a cell with its updates, render it on its own.
- **Skipped datasets.** In an exchange set, datasets with an unsupported
  product specification, a missing file, or data protection (encryption) are
  skipped with a warning on standard error. Update files, and updates whose
  base cell isn't in the set, are skipped too. If nothing renderable is left,
  `render` exits with code `2`.
- **ZIP archives.** A `.zip` exchange set is extracted to a temporary folder,
  which is deleted afterwards, even if rendering fails. You need temporary disk
  space about the size of the uncompressed set.

### S-101 updates

When the single-dataset form renders an S-101 base cell (`.000`), it finds the
update files (`.001`, `.002`, and so on) in the same folder and applies them in
order first (S-100 Part 10a). `info` and `validate` do the same. A missing,
out-of-order or unreadable update is reported but doesn't stop the command. Pass
`--no-updates` to use the base cell as it is.

### Sea-ice display modes

An S-411 dataset carries the full WMO egg code, so the same data can be drawn
in any of its display modes:

- `ice-concentration`: total concentration. This is the default.
- `ice-sod`: stage of development.
- `ice-navigational`: a provisional preview derived from total concentration.
  It isn't a POLARIS or RIO navigational-risk calculation.

In a composite, every S-411 layer uses the mode. Passing `--display-mode` for a
dataset that isn't S-411 is an error. `s100 info` lists the modes a dataset
supports.

### Display-list output

With a `.json` output path or `--format json`, the single-dataset form writes
the dataset's S-100 Part 9 display list instead of an image: the drawing
instructions the portrayal produced, with their symbols, line styles, area
fills, colours, display planes, drawing priorities, viewing groups and text.
Use it to review, diff or snapshot-test a portrayal change as text.

```bash
s100 render warnings.gml warnings.json
s100 render warnings.gml out.txt --format json
```

```json
{
  "dataset": "navwarn_surface.gml",
  "product": "S-124",
  "spec": "S-124/2.0.0",
  "palette": "day",
  "instructionCount": 3,
  "categoryCounts": { "areas": 0, "lines": 0, "points": 3, "text": 0 },
  "instructions": [
    {
      "kind": "point",
      "feature": "f1",
      "subLayer": 0,
      "plane": "UnderRadar",
      "viewingGroup": 31020,
      "drawingPriority": 15,
      "symbol": "NavigationalWarningFeaturePart",
      "geometry": { "type": "Surface", "vertexCount": 5, "anchor": [51.05, 1.2] }
    }
  ]
}
```

- Vector products (S-101, S-57 and the GML products) have display lists.
  Coverage products (S-102, S-104, S-111) don't, and `render` exits with code
  `3`.
- Geometry is summarised as its type, vertex count and one latitude/longitude
  anchor. The output has no timings, so two runs over the same dataset and
  options produce identical files.
- `--palette`, `--national-language`, `--symbol-scale`, `--text-scale`,
  `--display-mode` and `--time-step` change the instructions. `--width`, `--height`, `--quality` and
  `--background` are ignored. `--bbox`, `--center` and `--scale` are errors,
  because a display list has no viewport.

## tiles export

```text
s100 tiles export <dataset> -o <output> [options]
s100 tiles export --layer <dataset> [--layer <dataset> ...] -o <output> [options]
s100 tiles export <exchange-set> -o <output> [options]
s100 tiles export <folder> -o <output> [options]
```

`tiles export` renders datasets as raster tiles on the XYZ Web Mercator grid
that MapLibre, Leaflet and OpenLayers use. The tiles carry the S-100 portrayal
itself (symbols, scale minimums and palettes), so a web map shows the chart
without styling it. It takes the same three input forms as
[`render`](#render), and `-o` is required. It also takes a folder of datasets:

- **Folder.** Pass any other folder, and every dataset in it and its
  subfolders is tiled, as [`mcp serve`](#mcp-serve) finds them. An S-100
  exchange set inside it is read through its `CATALOG.XML`. An S-57 exchange
  set (an `ENC_ROOT` folder holding a `CATALOG.031`) is read as a folder of
  cells, whether you pass it, its `CATALOG.031`, or a folder above it, or name
  it with `--from`. Loose S-101 cells get their update files, as a single cell
  does, but cells in an S-100 exchange set don't. A dataset that can't be
  opened or tiled is skipped with a warning.

The container comes from the output path:

- **Folder.** Tiles are written as `<output>/{z}/{x}/{y}.png` (or `.jpg`,
  `.webp`), with a `tiles.json` [TileJSON](https://github.com/mapbox/tilejson-spec)
  file whose tile URL is relative to the folder.
- **PMTiles.** An output ending in `.pmtiles` is written as one
  [PMTiles](https://docs.protomaps.com/pmtiles/) version 3 archive, which a
  static web host can serve. Identical tiles are stored once. The archive's
  metadata holds the same TileJSON fields.
- **MBTiles.** An output ending in `.mbtiles` is written as one
  [MBTiles](https://github.com/mapbox/mbtiles-spec) 1.3 SQLite database, the
  format many tile servers read. Rows are numbered from the south, as MBTiles
  requires. The `metadata` table holds the standard keys plus `tileSize` and
  the display settings, prefixed `s100:`.
- **GeoPackage.** An output ending in `.gpkg` is written as one
  [OGC GeoPackage](https://www.geopackage.org/) 1.4 tile pyramid, which
  QGIS, ArcGIS Pro, GDAL and field apps such as QField open as an offline
  raster layer. The tiles are in a table named `chart`, in EPSG:3857 on the
  Web Mercator grid, so each zoom level is a tile matrix, and rows are
  numbered from the north as in XYZ. The TileJSON is kept as GeoPackage
  metadata. WebP tiles register the `gpkg_webp` extension, which not every
  reader supports, so use PNG or JPEG for the widest reach.
- **GeoTIFF.** An output ending in `.tif` or `.tiff` is written as one
  [Cloud-Optimized GeoTIFF](https://cogeo.org/) in EPSG:3857, for tools that
  take a single georeferenced image. The maximum zoom is the full-resolution
  image and each lower zoom is an overview, down to an image a tile or two
  across, so `--min-zoom` only limits how far the overviews go. The image is
  whole tiles of its smallest overview, so it can reach past the data; tiles
  with nothing on them are left out of the file, and read as transparent.
  PNG and WebP tiles are stored as RGBA with Deflate compression, and JPEG
  tiles as they are (no transparency, and empty areas read as black). The
  file is a BigTIFF when it would pass 4 GB. It can't cross the antimeridian.

Set `--container` to choose the container whatever the extension. An existing
`.pmtiles`, `.mbtiles`, `.gpkg` or `.tif` file at the output path is replaced only once the new
one is finished: the new file is written beside it, with `.partial` added to
its name, and then moved into its place. Until then a [`tiles serve`](#tiles-serve)
reading the old file keeps serving it, and if the export fails or is stopped,
the old file is left as it was.

### Tiles export options

| Option | Default | Description |
|---|---|---|
| `--layer <path>` | none | Adds a dataset as a composite layer. Repeat it for each dataset. |
| `--exchange-set`, `--from <path>` | none | Tiles an exchange set: a folder with `CATALOG.XML`, the `CATALOG.XML` file, or a `.zip`. Can't be combined with `--layer`. |
| `--only <specs>` | all | Exchange-set and folder forms only. Tiles only these product specifications, comma-separated. |
| `-o`, `--output <path>` | required | A folder, a `.pmtiles` file, a `.mbtiles` file, a `.gpkg` file or a `.tif` file. Its parent folder must exist. |
| `--container <kind>` | from the output | `xyz` (a folder), `pmtiles`, `mbtiles`, `gpkg` or `geotiff`. |
| `--min-zoom <0-24>` | from the data | Lowest zoom level. See [Zoom levels](#zoom-levels). |
| `--max-zoom <0-24>` | from the data | Highest zoom level. |
| `--bbox <minLon,minLat,maxLon,maxLat>` | the data's extent | Writes only the tiles that cover this WGS-84 bounding box. Datasets that can't draw in those tiles aren't portrayed. See [Zoom levels](#zoom-levels). |
| `--tile-size <pixels>` | `256` | `256`, or `512` for high-DPI tiles of the same grid. |
| `--format <format>` | `png` | `png`, `jpeg` (or `jpg`) or `webp`. |
| `--quality <1-100>` | `90` | Encoder quality for `jpeg` and `webp`. Ignored for `png`. |
| `--palette <palette>` | `day` | `day`, `dusk` or `night`. |
| `--national-language <code>` | the catalogue's, `eng` | ISO 639-2 language, such as `fra`, to prefer for chart text where the product's portrayal supports it (S-101, S-131). A feature with no name in that language shows its default name. |
| `--symbol-scale <factor>` | `1.0` | Symbol scale factor. |
| `--text-scale <factor>` | `1.0` | Text scale factor. |
| `--time-step <index>` | `0` | Zero-based time step for time-series datasets (S-104, S-111). |
| `--background <hex>` | transparent | Tile background, `#RRGGBB` or `#AARRGGBB`. |
| `--no-text` | off | Hides text. Same as `--hide text`. |
| `--hide <categories>` | none | Hides drawing-instruction categories, comma-separated: `text`, `points`, `lines`, `areas`. |
| `--basemap <mode>` | `none` | `offline` draws the bundled Natural Earth land layer under the chart. |
| `--display-mode <mode>` | `ice-concentration` | S-411 only. See [Sea-ice display modes](#sea-ice-display-modes). |
| `--skip-empty` | off | Doesn't write tiles on which nothing was drawn. A web map shows a missing tile as empty. |
| `--metatile <tiles>` | `4` | Renders blocks of this many tiles square at once, then cuts them apart. |
| `--parallel <workers>` | processor count | Number of blocks rendered at once. |
| `-y`, `--yes` | off | Writes a tile set of more than 100,000 tiles without asking. |
| `--no-updates` | off | Tiles an S-101 base cell, given on its own, with `--layer` or loose in a folder, without its update files. |
| `--lua-engine <engine>` | `luacsharp` | The Lua engine that runs the portrayal rules: `luacsharp` or `moonsharp`. See [Lua engine](#lua-engine). |
| `--debug` | off | Prints full stack traces on error, and portrayal and Lua diagnostics on standard error. |

### Tiles export examples

```bash
s100 tiles export enc.000 -o tiles/
s100 tiles export enc.000 -o chart.pmtiles --min-zoom 10 --max-zoom 15
s100 tiles export --layer enc.000 --layer bathy.h5 -o chart.pmtiles --tile-size 512
s100 tiles export exchange-set/ -o tiles/ --format webp --skip-empty
s100 tiles export enc.000 -o chart.mbtiles --max-zoom 16
s100 tiles export exchange-set/ -o chart.gpkg --format jpeg
s100 tiles export enc.000 -o chart.tif --max-zoom 15 --skip-empty
s100 tiles export --from exchange-set.zip -o night.pmtiles --bbox -1.5,50.0,-1.0,50.5 --palette night
```

To show a PMTiles archive in MapLibre GL JS, register the `pmtiles` protocol
from the PMTiles JavaScript library and add a raster source over your basemap:

```js
const protocol = new pmtiles.Protocol();
maplibregl.addProtocol("pmtiles", protocol.tile);

map.addSource("chart", {
  type: "raster",
  url: "pmtiles://https://example.com/chart.pmtiles",
  tileSize: 256,
});
map.addLayer({ id: "chart", type: "raster", source: "chart" });
```

Declare `tileSize: 256` for 512-pixel tiles too: they cover the same grid at
twice the pixel density. A folder of tiles is added the same way, with
`tiles: ["https://example.com/tiles/{z}/{x}/{y}.png"]` in place of `url`. The
host must answer HTTP range requests for PMTiles; most static hosts do.

### Zoom levels

Each zoom level draws only what's visible at its scale: features outside
their scale minimum, and whole cells past their minimum display scale, are
left out. A zoom level's scale is measured at the centre latitude of the tiled
area, so it's the same in every tile and no feature is cut off at a tile edge.
The metadata records that latitude as `scaleLatitude`.

Where chart cells of different scales overlap, the finer cell hides the
coarser one at each zoom level where the finer cell draws. Zoomed out past
the finer cell's minimum display scale, the coarser cell shows in full again.

Without `--min-zoom`, the lowest zoom is the one nearest the coarsest cell's
minimum display scale. Without `--max-zoom`, the highest is one level past the
finest cell's compilation scale. Products without these scales, such as GML
products and coverages, start at the zoom where their extent fits in about one
tile and go six levels deeper, up to zoom 18.

With `--bbox`, datasets that can't draw in any of its tiles are closed before
they're portrayed, and `tiles export` says how many it left out. A dataset is
kept when its extent reaches the tiles that cover the box at the lowest zoom,
or the ring of tiles around them, so symbols and text near an edge tile are
drawn as they would be without `--bbox`. This saves time and memory when the
box covers a small part of a large folder. The default zoom range then comes
from the datasets that are kept. For a box that crosses the antimeridian,
every dataset is kept.

`tiles export` prints the number of tiles before it renders any. Each zoom level has
four times the tiles of the one before, so a set grows quickly. Above 100,000
tiles, `tiles export` asks before writing, or exits with code `2` when it can't ask.
Pass `--yes` to skip the question, or narrow `--bbox` or the zoom range.

### Tile set limits

- **One palette and time step.** A tile set holds one `--palette` and one
  `--time-step`. Run `tiles export` again for each one you need.
- **Coverages.** S-102, S-104 and S-111 are portrayed again for each zoom
  level, so grid sampling and current-arrow spacing suit its resolution.
- **S-101 updates.** As with `render`, the exchange-set form doesn't apply
  S-101 update files.

## tiles serve

```text
s100 tiles serve <tile-set> [options]
s100 tiles serve <dataset> [options]
s100 tiles serve --layer <dataset> [--layer <dataset> ...] [options]
s100 tiles serve <exchange-set> [options]
s100 tiles serve <folder> [options]
```

`tiles serve` serves raster tiles over HTTP on the standard XYZ URLs, so a web
map running on this computer can use them as a tile source. It serves either:

- **A built tile set**: a `{z}/{x}/{y}` folder, a `.pmtiles` archive, a
  `.mbtiles` database or a `.gpkg` GeoPackage that [`tiles export`](#tiles-export)
  or another tool wrote. A GeoPackage must hold tiles in EPSG:3857 on the Web
  Mercator grid, as `tiles export` and GDAL's `GoogleMapsCompatible` tiling
  scheme write them. The tile set is read at each request, so if you export it again while
  it's served, the next request sees the new tiles. A `.pmtiles`, `.mbtiles` or `.gpkg`
  file keeps serving its old tiles until the new export is finished. Only PNG, JPEG and WebP
  raster tiles are served, not vector tiles.
- **Datasets**, rendered when each tile is first asked for. It takes the same
  inputs and display options as `tiles export`: one dataset, repeated
  `--layer`, an exchange set, or a folder of datasets. Nothing has to be built
  in advance, and the tiles are the same as an export of the same datasets
  would hold.

A folder is served as a built tile set only when it looks like one: it holds a
`tiles.json`, or numbered `{z}/{x}` subfolders. Any other folder is searched for
datasets, as `tiles export` searches it.

When it renders datasets, `tiles serve` renders the block of `--metatile` ×
`--metatile` tiles around a requested tile and keeps every tile of the block in
memory, up to `--cache-mb`, so neighbouring tiles come back at once. At most
`--parallel` blocks render at a time.

While it serves datasets, `tiles serve` checks their files every `--refresh`
seconds (default 10) and reopens them when they change: when you copy in a new
edition, add S-101 update files, or add or remove datasets in a served
exchange set or folder. It
waits until a change has held for two checks in a row, so a file still being
copied isn't read half-written. The new data is opened and portrayed while the
old keeps serving, then tiles are rendered from it with an empty cache. If it
can't be opened, the old data keeps serving until the files change again.
`--refresh 0` turns the checks off. A built tile set needs none: it's read at
each request.

With `--cache-dir`, rendered tiles are also kept in that folder (in its
`served` subfolder), so they come
back without rendering after a restart. They're stored under a fingerprint of
the data and settings: each input file's path, size and write time (S-101
update files and the whole of an exchange set or folder included), the display and
format options, and the `s100` version. Change any of them and new tiles are
rendered, so a kept tile is never stale. The folder holds up to
`--cache-dir-mb`, least recently used tiles going first, including those of
data or settings you no longer serve. `--clear-cache` empties it before
serving. Only the `served` subfolder is read or deleted, so other files in the
folder are left alone, and several servers can share one folder. The zoom levels and the area served are
worked out as for `tiles export`, and `--min-zoom`, `--max-zoom` and `--bbox`
narrow them. `--palette` sets the palette of the plain tile URLs; the other
palettes are served too, under their own URLs.

The server has these routes:

- `{z}/{x}/{y}.png` (or `.jpg`, `.webp`), the tiles. Rows are numbered from
  the north for every container, MBTiles included. Each tile has an `ETag` so
  browsers can revalidate it. A tile with nothing on it, or outside the tile
  set, answers `204 No Content`, which web maps show as empty.
- `tiles.json`, a [TileJSON](https://github.com/mapbox/tilejson-spec) document
  with the zoom levels, bounds and display settings, and an absolute tile URL.
- When rendering datasets, `day/`, `dusk/` and `night/` hold the same two
  routes in that palette, for example `night/{z}/{x}/{y}.png` and
  `night/tiles.json`. Each palette is portrayed the first time it's asked for.
- When rendering time-varying datasets (S-104, S-111), a `t` query parameter
  on either route picks the time step, for example
  `{z}/{x}/{y}.png?t=2026-03-21T07:00Z`. The time is ISO 8601, UTC unless it
  says otherwise, and snaps to the nearest step; each dataset then uses its own
  step nearest to it. The TileJSON lists every step in `s100.times`, and with
  `t` it names the chosen step in `s100.time` and carries it in its tile URL.
  Without `t`, `--time-step` applies. A `t` that isn't a time answers
  `400 Bad Request`; datasets that don't vary with time ignore it.
- `wmts/1.0.0/WMTSCapabilities.xml`, an [OGC WMTS 1.0.0](#wmts) capabilities
  document, and `wmts`, the same service in the KVP encoding, so GIS desktops
  such as QGIS and ArcGIS Pro can add the tiles.
- `wms`, an [OGC WMS 1.3.0](#wms) service: GetCapabilities, GetMap, and,
  when rendering datasets, GetFeatureInfo.
- `/`, a preview page that shows the tiles over OpenStreetMap, with a palette
  menu when rendering datasets and a time menu when they vary with time. It loads MapLibre GL JS and the basemap from
  the internet. Pass `--no-viewer` to turn it off.
- `/healthz`, which answers `200 OK` with the text `ok` while the server runs.
  It never has a token. See [`health`](#health).

Every response allows any origin, so a page served from another port can use
the tiles.

By default only this computer can connect. With `--host 0.0.0.0`, other
computers can, and `tiles serve` adds a random access token to every URL unless
you pass `--token` or `--no-token`. It prints the tile URL, the TileJSON URL
and the preview URL for each network address.

The options below are the server's own. When rendering datasets, the
[`tiles export` options](#tiles-export-options) for inputs, zoom levels, the
area, the tile format and display settings apply too; the output options
(`-o`, `--container`, `--skip-empty`, `--yes`) don't.

| Option | Default | Description |
|---|---|---|
| `--host <address>` | `127.0.0.1` | The address to listen on. `0.0.0.0` accepts other computers. |
| `--port <port>` | `8200` | The port. `0` picks a free one. |
| `--token <token>` | generated for a non-loopback host | An access token that becomes part of every URL. Letters, digits, `-` and `_`. |
| `--no-token` | off | Serves without a token, even on a non-loopback address. |
| `--no-viewer` | off | Doesn't serve the preview page. |
| `--cache-mb <megabytes>` | `256` | When rendering datasets, the most memory rendered tiles are kept in. |
| `--refresh <seconds>` | `10` | When rendering datasets, how often their files are checked for changes. `0` never checks. |
| `--cache-dir <folder>` | none | When rendering datasets, also keeps rendered tiles in this folder, across runs. |
| `--cache-dir-mb <megabytes>` | `1024` | The most disk space the `--cache-dir` tiles take. |
| `--clear-cache` | off | Deletes the tiles cached in `--cache-dir` before serving. |

```bash
s100 tiles serve chart.pmtiles
s100 tiles serve chart.mbtiles --port 9000
s100 tiles serve chart.gpkg
s100 tiles serve tiles/ --host 0.0.0.0
s100 tiles serve enc.000
s100 tiles serve --layer enc.000 --layer bathy.h5 --palette night
s100 tiles serve exchange-set/ --only S101 --max-zoom 16
s100 tiles serve exchange-set/ --cache-dir ~/.cache/s100-tiles
```

To use the tiles in MapLibre GL JS, add a raster source from the TileJSON URL:

```js
map.addSource("chart", {
  type: "raster",
  url: "http://127.0.0.1:8200/tiles.json",
  tileSize: 256,
});
map.addLayer({ id: "chart", type: "raster", source: "chart" });
```

In Leaflet, use the tile URL:
`L.tileLayer("http://127.0.0.1:8200/{z}/{x}/{y}.png").addTo(map)`.

### WMTS

`tiles serve` is also an OGC WMTS 1.0.0 service, for GIS desktops and
libraries that add tiles from a capabilities document. Its URL is printed at
startup:

```text
http://127.0.0.1:8200/wmts/1.0.0/WMTSCapabilities.xml
```

- **QGIS:** choose **Layer > Add Layer > Add WMS/WMTS Layer**, add a
  connection with the capabilities URL, connect, and add the `s100` layer.
- **ArcGIS Pro:** choose **Insert > Connections > Server > New WMTS Server**
  and enter the capabilities URL.
- **GDAL:** open `WMTS:http://127.0.0.1:8200/wmts/1.0.0/WMTSCapabilities.xml`.

The service has one layer, `s100`, on the `WebMercatorQuad` tile matrix set
in EPSG:3857. The tile matrices are numbered by zoom level, from 0 to the
maximum zoom, so a WMTS tile is the XYZ tile of the same zoom, column and row.
Below the minimum zoom the tiles are empty, as on the XYZ URLs, so zoom in to
see the chart. When rendering datasets, each palette is a WMTS style and the
`--palette` one is the default; a built tile set has one style, `default`.
For time-varying datasets (S-104, S-111), a `Time` dimension lists the time
steps and defaults to `--time-step`.

Tiles are served in both WMTS encodings:

- **KVP:** `wmts?SERVICE=WMTS&REQUEST=GetTile&LAYER=s100&STYLE=night&TILEMATRIXSET=WebMercatorQuad&TILEMATRIX=13&TILEROW=2750&TILECOL=4066`,
  with `TIME=` for a time step.
- **RESTful:** `wmts/1.0.0/s100/{Style}/WebMercatorQuad/{TileMatrix}/{TileRow}/{TileCol}.png`,
  with a `{Time}` segment after the style for time-varying datasets.

A request the service can't answer gets an OWS exception report that names
the parameter at fault. The service has no GetFeatureInfo; use [WMS](#wms)
to identify features.

### WMS

`tiles serve` is also an OGC WMS 1.3.0 service, for clients that ask for one
image of the area they show, and to identify what's at a point. Its URL is
printed at startup:

```text
http://127.0.0.1:8200/wms
```

- **QGIS:** choose **Layer > Add Layer > Add WMS/WMTS Layer**, add a
  connection with the URL, connect, and add the `s100` layer. With the
  **Identify Features** tool, click the chart to see what's there.
- **ArcGIS Pro:** choose **Insert > Connections > Server > New WMS Server**
  and enter the URL.
- **GDAL:** open `WMS:http://127.0.0.1:8200/wms?SERVICE=WMS&REQUEST=GetCapabilities`.

The service has one layer, `s100`, in EPSG:3857, EPSG:4326 and CRS:84. As for
WMTS, each palette is a style, the `--palette` one first and the default, and
S-104 and S-111 time steps are a `time` dimension.

**GetMap** draws its image from the same tiles the XYZ URLs serve: those of
the zoom level nearest the requested scale, stitched and resampled to the
requested area and size. Symbols and text are therefore between about 0.7 and
1.4 times their portrayed size. A request coarser than the minimum zoom draws
nothing, and the capabilities give the matching `MaxScaleDenominator`, so
clients hide the layer there. It draws PNG or JPEG images up to 4096 pixels on
a side; set `TRANSPARENT=TRUE` for a PNG to overlay other layers.

**GetFeatureInfo** answers from the datasets, as
[`identify`](#identify) does: the vector features at the clicked pixel, with
their attributes, and the coverage values there (S-102 depths, S-104 water
levels, S-111 currents, weather grids). Point and curve features within 5
pixels of the click are matched. `FEATURE_COUNT` limits the features (1 by
default, as WMS defines it), and `TIME` picks the time step sampled. The
answer is in one of these formats (`INFO_FORMAT`):

| Format | Content |
|---|---|
| `text/html` | A table per feature with its attributes, and the coverage values. |
| `application/json`, `application/geo+json` | A GeoJSON FeatureCollection: a feature per vector feature and per coverage value, each a point at the clicked position, with what `identify --format json --attributes` reports as its properties. |
| `text/plain` | The same as `text/html`, as text. |

A built tile set has no datasets behind it, so its layer isn't queryable.

Requests in WMS 1.1.1 are answered too: their `SRS`, `X` and `Y` parameters,
and EPSG:4326 boxes in longitude, latitude order. In 1.3.0, EPSG:4326 boxes
are latitude first and CRS:84 boxes longitude first. A request the service
can't answer gets a WMS service exception report.

The server runs until you press Ctrl+C or it receives SIGTERM.

## info

```text
s100 info <dataset> [options]
```

`info` prints the detected product specification and edition, and whether the
dataset can be rendered headlessly. For a dataset with positions, **Axis
order** says whether they're latitude first, were swapped back from longitude
first, or couldn't be checked; a swapped or inconsistent order is also
printed on stderr. [Check the axis order](reading-product-data.md#check-the-axis-order)
explains the check. For a time-series dataset it lists the
time steps with the indexes `render --time-step` takes. For a product with
display modes, such as S-411, it lists the values `render --display-mode`
takes. For S-104, a **Portrayal** row says "SoundCharts portrayal: S-104 Ed
2.0.0 defines no symbology", because what `render` draws for it is
[SoundCharts' own](../src/EncDotNet.S100.Datasets.S104/README.md#portrayal).
For an S-101 base cell, it applies the [update files](#s-101-updates) first.
For weather data, which isn't S-100, a **Notice** row says it isn't for
navigation. For a weather grid (GRIB2 or NetCDF), **Model** and **Run (UTC)**
give the model and its run, or say the steps are analyses when the file gives
no run; **Grid** and **Extent** give the grid's size, spacing and bounds, and
**Layers** the layers it can draw. A **Weather parameters** table then lists
each parameter with its level and step count, whether it's used, and if not,
why: for example a second swell partition, a parameter the weather table
doesn't have, or a GRIB2 message packed with CCSDS. The time steps are the
grid's valid times. For a CAP weather warning file (CAP XML, an Atom feed or
NWS GeoJSON), **Alerts** counts the alerts, their areas and the areas drawn,
and a **Warnings** table lists each alert area with its event, severity, when
it's in force (UTC) and how it's drawn (a polygon, a circle, or the NWS marine
zones it names), then any zone codes the marine zone layer doesn't have. Its
time steps are when warnings start and end, so `render --time-step` draws
what's in force then. For a WPC coded surface bulletin (`CODSUS`),
**Bulletin** says whether it's an analysis or a forecast, its resolution and
its WMO heading, **Issued by** and **Issued (UTC)** come from its header,
**Coverage** says it covers WPC's domain only, and a **Surface analyses**
table lists each valid time (one for an analysis, one per forecast time) with
its lead time and its counts of cold, warm, occluded and stationary fronts,
troughs, highs and lows. **Skipped codes** lists any feature codes it
doesn't know. For a tropical cyclone advisory (an NHC forecast zip,
such as `al092022_5day_020.zip`), **Storm**, **Advisory**, **Forecast** and
**Intensity** give the storm and its basin, the advisory number and issue time
(UTC), the forecast's span and the current wind, gusts, pressure and peak
forecast wind. **Cone**, **Wind radii** and **Past track** say what the
advisory holds; the wind radii and past track come from the `_fcst_` and
`_best_track` zips beside the forecast zip. A **Forecast points** table then
lists each point with its time, position, intensity, class and 34, 50 and
64 kt radii by quadrant. [Show wind and waves on the chart](weather.md) covers
the weather formats.

```bash
s100 info currents.h5
s100 info filter_gfswave.pl
s100 info alerts.atom
s100 info al092022_5day_020.zip
```

| Option | Default | Description |
|---|---|---|
| `--no-updates` | off | Reads an S-101 base cell without its update files. |
| `--debug` | off | Prints full stack traces on error. |

## identify

```text
s100 identify <dataset> --lat <latitude> --lon <longitude> [options]
s100 identify --layer <dataset> [--layer <dataset> ...] --lat <latitude> --lon <longitude> [options]
s100 identify <exchange-set> --lat <latitude> --lon <longitude> [options]
```

`identify` reports the vector features at a point and samples the coverage
datasets there, as a click on the map does in SoundCharts. It takes the same
three input forms as `render`. Features are ranked as an ECDIS ranks them:
points before curves before areas, and nearer before farther. Datasets that
can't be read are skipped with a warning on standard error.

Coverage samples come from every dataset that can be sampled: S-102,
S-104, S-111 and the GRIB2 and NetCDF weather grids. A weather grid's sample
lists each parameter at the nearest grid point and time step with its unit,
for example `wave.hs 2.52 m, wind.speed.kn 20.2 kn`. CAP warning areas are
reported as features of type `CapWarningArea`; `--attributes` adds their
event, severity, times and description. When the result includes weather
data, a not-for-navigation notice is printed first.

`identify` uses the same code as the MCP `identify_features` and
`sample_coverage` tools, so the results match.

| Option | Default | Description |
|---|---|---|
| `--lat <latitude>` | required | Latitude in decimal degrees, WGS-84, from -90 to 90. |
| `--lon <longitude>` | required | Longitude in decimal degrees, WGS-84, from -180 to 180. |
| `--layer <path>` | none | Adds a dataset. Repeat it for each dataset. Can't be combined with the exchange-set form. |
| `--from`, `--exchange-set <path>` | none | Picks across every dataset in an exchange set. |
| `--only <specs>` | all | Exchange-set form only. Loads only these product specifications, comma-separated. |
| `--radius <metres>` | `50` | Search distance for point and curve features, from 0 to 100,000. Area features must contain the point, so they ignore it. |
| `--spec <spec>` | all | Reports only this product specification or format, for example `S-124` or `GRIB2`. |
| `--time <iso8601>` | first step | A UTC time for S-104, S-111, GRIB2 and NetCDF coverages. The nearest time step is sampled. Ignored for S-102. |
| `--max-results <n>` | `20` | Maximum features to report, from 1 to 200. |
| `--attributes` | off | Includes each feature's attributes. |
| `--format <format>` | `table` | `table` or `json`. |
| `--debug` | off | Prints full stack traces on error. |

```bash
s100 identify warnings.gml --lat 51.085 --lon 1.30
s100 identify --layer enc.000 --layer bathy.h5 --lat 50.1 --lon -1.4
s100 identify --from exchange-set.zip --lat 50.1 --lon -1.4 --format json
s100 identify chart.000 --lat 50.1 --lon -1.4 --attributes
s100 identify --layer gfswave.grib2 --layer alerts.atom --lat 38.5 --lon -74.5 --time 2026-10-25T03:00Z
```

The JSON output is an object with `point`, `totalMatched`, `truncated`,
`features` (ranked), `samples` (one for each specification with a dataset
that covers the point), `notices` when the result includes weather data and,
when an input was skipped, `warnings`. A weather grid's sample `value` has
`kind` `fields`: a `fields` array of `name`, `quantity`, `unit` and `value`,
plus `sampleTime` and the grid point's `cellCentreLatitude` and
`cellCentreLongitude`.

## validate

```text
s100 validate <dataset-or-exchange-set> [options]
```

`validate` does one of two checks, depending on what you pass it.

**A dataset file** is checked against its product specification's rule pack.
Each finding has a rule id that traces to the specification (for example
`S127-R-2.1`), a severity, a message, and, where the rule can locate the
problem, a feature id or position. The result depends only on the dataset,
not on palette or time step. A specification with no rule pack, such as S-401,
reports that no rules are available and exits with code `0`. That's different
from a dataset that was checked and passed.

An S-57 cell is checked twice: once against S-57 rules for the fields that
don't survive translation, and once against the S-101 rule pack after
translation. Findings from the second pass have rule ids prefixed with
`S101-as-S57/`. See [Bringing S-57 into the pipeline](s57-to-s101.md#validate-a-cell).

**An exchange set** (a folder with `CATALOG.XML`, the `CATALOG.XML` file, or a
`.zip`) has each file's digital signature and checksum verified (S-100
Part 15). An S-57 or S-63 exchange set (`CATALOG.031`) has its CRCs and
signatures verified. The datasets themselves aren't checked against rule
packs; validate them one at a time.

| Option | Default | Description |
|---|---|---|
| `--format <format>` | `text` | `text` prints a table. `json` prints a report for scripts and CI. |
| `--suppress <patterns>` | none | Drops findings whose rule id matches, comma-separated, for example `--suppress S101-R-1.2,S101-R-3.2` or `--suppress "S101-*"`. |
| `--strict` | off | Fails on warnings as well as errors. For an exchange set, also fails unsigned files and files without a checksum. |
| `--no-updates` | off | Validates an S-101 base cell without its update files. See [S-101 updates](#s-101-updates). |
| `--debug` | off | Prints full stack traces on error. |

```bash
s100 validate warnings.gml
s100 validate route.gml --strict
s100 validate currents.h5 --format json
s100 validate chart.000 --suppress S101-R-1.2,S101-R-3.2
s100 validate chart.000 --suppress "S101-*"
s100 validate exchange-set/CATALOG.XML
s100 validate exchange-set.zip --format json
s100 validate s57-set/CATALOG.031
```

`--suppress` patterns match rule ids without regard to case. `*` matches any
run of characters and is the only wildcard, so a pattern without `*` matches
one rule id exactly. Use it to mute a known class of finding, such as a
feature catalogue version mismatch, so the remaining findings stand out.
Suppressed findings are left out of the report and the exit code, and the
summary line says how many were suppressed.

`validate` exits with code `0` when no error-severity findings remain. Warnings
and information findings are reported but don't fail unless you pass
`--strict`. Failing findings exit with code `6`.

## route check

```text
s100 route check <route> <datasets>... [options]
```

`route check` checks a route against datasets, as **Check route** does in
SoundCharts. The route is an RTZ file (IEC 61174 Annex S, versions 1.0, 1.1
and 1.2) or an S-421 GML dataset. The datasets are any mix of dataset files,
one exchange set (a folder with `CATALOG.XML`, the `CATALOG.XML` file, or a
`.zip`) and one folder of datasets. A route file among the datasets is
skipped.

It reads the route into a `PassagePlan` and runs a `RouteChecker` from
`EncDotNet.S100.Navigation`, with every check:

- **chart**: land, drying ground, chart coverage, and, with the safety
  contour and depth, depth areas, S-102 shoals, soundings and charted dangers.
- **areas**: regulated and S-122 protected areas.
- **warnings**: S-124 navigational warnings.
- **clearance**: the under-keel clearance, when every leg has a draft, from
  the file or `--draft`. Charted depth comes from S-102, or S-101 where
  there's no S-102, and water level from S-104 at each point's ETA.

[Route checks and under-keel clearance](route-checks.md) explains every
finding, its thresholds and confidence. The MCP `check_route` tool and
SoundCharts run the same checker, so the verdict, findings, codes and ids
match. The results aren't for navigation.

The route's values come from the file: each leg's cross-track limits, draft
and static UKC, and the speeds and departure of its planning schedule. RTZ and
S-421 have no squat. These options replace the file's value on every leg:

| Option | Default | Description |
|---|---|---|
| `--draft <metres>` | the file's | Draft for every leg. With a draft on every leg, the UKC check runs; each leg's dangers are also judged against its draft + squat + required UKC. |
| `--squat <metres>` | `0` | Squat allowance. |
| `--speed <knots>` | the file's | Speed over ground. Speeds give the ETAs that warnings, seasonal areas and S-104 water levels are judged at. |
| `--departure <iso8601>` | the file's, else now | Departure time. A time without an offset is UTC. |
| `--required-ukc <metres>` | the file's | Required under-keel clearance. |
| `--corridor <metres>` | each leg's limits | The corridor's half-width: how far it reaches to port, and to starboard, of the track. A side without a limit uses 50 m. |

Other options:

| Option | Default | Description |
|---|---|---|
| `--safety-contour <metres>` | `30` | Safety contour, as the mariner setting. |
| `--safety-depth <metres>` | `30` | Safety depth, for charted dangers and soundings. |
| `--max-wind <knots>` | the file's | Wind limit for the weather check: stretches where a loaded GRIB2 or NetCDF forecast has a higher 10 m wind at the ETA are Check findings. Replaces an RTZ route's `vesselMaxWind`. |
| `--max-wave <metres>` | the file's | Significant wave height limit for the weather check. Replaces an RTZ route's `vesselMaxWave`. |
| `--cyclone-radius <knots>` | `34` | Tropical cyclones (loaded NHC advisories): the outermost wind radius tested, 34, 50 or 64 kt. The cone is always tested. |
| `--cyclone-must-review <knots>` | `50` | The weakest radius whose findings are Must review: 64, 50, 34 or `never`. Weaker radii and the cone are Check. |
| `--no-cyclone-cpa` | off | Doesn't report the route's closest approach to each storm. |
| `--window` | off | Also sweeps departures across the S-104 time range to find the departure window (needs a draft), and across the forecast for the weather check (needs a wind or wave limit). |
| `--tide-predictions <file>` | off | A tide station list (the `tide-stations` file, `.json` or `.json.gz`) to predict water level from where no S-104 covers. A station reaches 10 km through the loaded charts' water. Predictions are the astronomical tide only, not a forecast: the text output names the stations and says so. |
| `--only <specs>` | all | Exchange set or folder only. Loads only these product specifications, comma-separated. |
| `--fail-on <level>` | `must-review` | When to exit with code `7`: `must-review`, `check` or `never`. See [Exit codes for CI](#exit-codes-for-ci). |
| `--format <format>` | `text` | `text` or `json`. |
| `--debug` | off | Prints full stack traces on error. |

The default safety contour and depth are the S-101 portrayal defaults, which
flag most coastal water. Set them to the vessel's values.

```bash
s100 route check route.rtz exchange-set/ --safety-contour 10 --safety-depth 10
s100 route check route.gml enc.000 bathy.h5 tides.h5 warnings.gml --draft 7 --required-ukc 1
s100 route check route.rtz exchange-set.zip --corridor 100 --fail-on check
s100 route check route.gml exchange-set/ --departure 2026-03-01T06:00Z --speed 8 --window --format json
s100 route check route.rtz enc.000 gfs.t06z.pgrb2.0p25 gfswave.t06z.grib2 --max-wind 25 --max-wave 3
s100 route check route.rtz enc.000 al142026_5day_017.zip --cyclone-radius 50 --cyclone-must-review 64
s100 route check route.rtz enc/ bathy.h5 --draft 7 --tide-predictions tide-stations.json.gz
```

The text output gives the verdict, the checks that didn't run in full, the
findings by group, then the clearance verdict and its minimum, and the weather
verdict with the highest wind and waves and, for each tropical cyclone near
the route, the strongest radius it meets and its closest approach. The JSON output
is an object with:

- `route`: the `file`, `format` (`rtz` or `s421`), `version`, `routeId`
  (S-421 only), `name`, number of `waypoints` and the `departure` used.
- `settings`: the safety contour and depth, and any options that replaced
  the file's values.
- `check`: the report as the MCP `check_route` tool returns it: the
  `verdict`, each check's status (`checks`), the `findings` with their
  `details`, the `summary`, each leg's corridor, the `clearance` (null when
  the clearance check was skipped; `checks` says why), the `datasets` read
  and the `disclaimer`. See [Check a route](mcp-server.md#check-a-route).
- `loadWarnings`: datasets that couldn't be loaded, when there are any.

### Exit codes for CI

`route check` exits with code `0` when the verdict doesn't reach `--fail-on`,
and with code `7` when it does:

| `--fail-on` | Exits with `7` when the verdict is |
|---|---|
| `must-review` | `must_review` (a Must review finding, or the clearance is below your UKC setting) or `water_level_ends_before_arrival`. |
| `check` | Anything but `clear`: also Check findings, and a tight or incomplete clearance. |
| `never` | Never. |

A route file that can't be read, or datasets that can't be loaded, exit with
code `2`.

## tides stations

```text
s100 tides stations --near <lat,lon> [options]
```

`tides stations` lists the tide stations within a radius of a point, nearest
first, from the offline station catalogue. Each station has its id, name and
kind: **harmonic** stations are predicted from their own harmonic
constituents, and **subordinate** stations from a reference station with time
and height differences. The table shows a harmonic station's datums as heights
above MLLW. A line under the table gives the catalogue's provider and age, and
says when it's over 12 months old.

| Option | Default | Description |
|---|---|---|
| `--near <lat,lon>` | required | The point, latitude first, in decimal degrees: `--near 47.6,-122.34`. |
| `--radius <distance>` | `50` (km) | How far to look: kilometres, or with a unit (`20km`, `10nm`, `5000m`). |
| `--limit <n>` | `10` | The most stations to list, 1 to 500. |
| `--format <format>` | `table` | `table`, `csv` or `json`. JSON is the `list_tide_stations` MCP result, with each station's datums, predictable datums and a subordinate station's offsets. |
| `--catalogue <file>` | the offline cache | A tide-stations JSON file (`.json`, or `.json.gz` for gzip). |

```bash
s100 tides stations --near 47.6,-122.34
s100 tides stations --near 47.6,-122.34 --radius 20nm --limit 5 --format json
```

```text
╭─────────┬───────────────┬─────────────┬───────────┬──────────┬───────────────╮
│ Id      │ Name          │ Kind        │ Reference │ Distance │ Datums (m     │
│         │               │             │           │          │ above MLLW)   │
├─────────┼───────────────┼─────────────┼───────────┼──────────┼───────────────┤
│ 9447130 │ SEATTLE       │ harmonic    │ —         │   0.3 km │ MHHW 3.46 MSL │
│         │ (Madison      │             │           │          │ 2.02 LAT      │
│         │ St.), Elliott │             │           │          │ -1.31         │
│         │ Bay           │             │           │          │               │
│ 9446486 │ Tacoma        │ subordinate │ 9447130   │  39.9 km │ MLLW only     │
│         │ Narrows       │             │           │          │               │
│         │ Bridge        │             │           │          │               │
╰─────────┴───────────────┴─────────────┴───────────┴──────────┴───────────────╯
Stations: NOAA CO-OPS, retrieved 2026-11-11 (0 days ago), 3502 stations.
```

## tides predict

```text
s100 tides predict <station> [options]
s100 tides predict --near <lat,lon> [options]
```

`tides predict` predicts the astronomical tide at a station from the offline
catalogue. Name the station by id (`9447130`) or name (`Seattle`; a unique
part of a name will do), or use `--near` for the nearest station within
`--radius` that can be predicted on the datum. It prints heights in metres
above the datum at each step, or with `--highs-lows` the high and low waters,
with a note that the values are predicted.

A prediction is astronomical: it leaves out wind, pressure and river flow,
which can move the water 0.5 m or more. It isn't a forecast, isn't S-104, and
isn't for navigation. The engine is
[`EncDotNet.S100.Tides`](../src/EncDotNet.S100.Tides/README.md) and the
catalogue is
[`EncDotNet.S100.Tides.Stations`](../src/EncDotNet.S100.Tides.Stations/README.md).

| Option | Default | Description |
|---|---|---|
| `--near <lat,lon>` | none | Predict at the nearest station to this point instead of a named one. |
| `--radius <distance>` | `10` (km) | With `--near`, how far away the station may be: kilometres, or with a unit (`20km`, `10nm`, `5000m`). |
| `--from <time>` | now, rounded down to the step | The first time: ISO 8601 (`2026-11-18T12:00`, `2026-11-18T20:00Z`) or a date. Without an offset, it's in this computer's time zone. |
| `--to <time>` | 24 hours after `--from` | The last time, like `--from`. At most 366 days after it. |
| `--step <step>` | `30` (minutes) | The time between heights: minutes, or with a unit (`10m`, `1h`, `90s`). |
| `--highs-lows` | off | Lists the high and low waters instead of heights at each step. |
| `--datum <datum>` | `MLLW` | `MLLW`, `MLW`, `MSL`, `MHW`, `MHHW`, `LAT` or `HAT`. A harmonic station can use any datum NOAA gives it; a subordinate station is MLLW only. |
| `--utc` | off | Reads and prints times in UTC rather than this computer's time zone. |
| `--format <format>` | `table` | `table`, `csv`, `json` or `s104`. JSON is the `predict_tide` MCP result, with times in UTC. |
| `-o`, `--output <file>` | `104ZZ01<station>.h5` | With `--format s104`, the file to write. |
| `--catalogue <file>` | the offline cache | A tide-stations JSON file. |

```bash
s100 tides predict 9447130
s100 tides predict Seattle --from 2026-11-18T12:00 --to 2026-11-18T18:00 --step 10m
s100 tides predict 9447130 --highs-lows --from 2026-11-18 --to 2026-11-25
s100 tides predict --near 47.27,-122.55 --format csv
```

```text
SEATTLE (Madison St.), Elliott Bay 9447130, harmonic, predicted on MLLW
╭─────────────────────────┬──────────┬──────────────────────────╮
│ Time                    │ High/low │ Predicted (m above MLLW) │
├─────────────────────────┼──────────┼──────────────────────────┤
│ 2026-11-18 04:35 -08:00 │ Low      │                     0.80 │
│ 2026-11-18 11:55 -08:00 │ High     │                     3.42 │
│ 2026-11-18 18:55 -08:00 │ Low      │                     1.41 │
╰─────────────────────────┴──────────┴──────────────────────────╯
Astronomical tide only, not a forecast. Weather and river flow can move the
water 0.5 m or more. Not for navigation.
Stations: NOAA CO-OPS, retrieved 2026-11-11 (7 days ago).
```

CSV has a header row (`time,predicted_m_above_mllw`, or
`time,kind,predicted_m_above_mllw` with `--highs-lows`) and ISO 8601 times
with their offset. The station and the note go to standard error.

### Write an S-104 dataset

`--format s104` writes the series as an S-104 Edition 2.0.0 dataset with one
station (data coding format 8), which SoundCharts and the other `s100`
commands open like any S-104 file:

```bash
s100 tides predict 9447130 --from 2026-11-18 --to 2026-11-25 --step 6m --format s104 -o seattle.h5
```

The dataset says it's a prediction: its producer code is `ZZ01`, which no
real producer has, its data dynamicity is astronomical prediction, and its
method text names the station and says it's not an official product. The
step must be a whole number of seconds up to 65,535 (18 h 12 min), and the
datum must have an S-100 code (all the datums above do). The file is written
with PureHDF, whose writer doesn't work in a NativeAOT build; the `s100`
release builds aren't NativeAOT.

## tides currents stations

```text
s100 tides currents stations --near <lat,lon> [options]
```

`tides currents stations` lists the tidal current stations within a radius of
a point, nearest first, from the offline station catalogue. NOAA lists each
depth bin at a station as its own station, such as `PUG1701_33`, so one place
can appear several times. Each station has its id, name, kind (**harmonic**,
or **subordinate** with a reference station), depth below the surface, and
the directions the flood and ebb set toward, in degrees true. A line under
the table gives the catalogue's provider and age.

| Option | Default | Description |
|---|---|---|
| `--near <lat,lon>` | required | The point, latitude first, in decimal degrees: `--near 48.406,-122.643`. |
| `--radius <distance>` | `50` (km) | How far to look: kilometres, or with a unit (`20km`, `10nm`, `5000m`). |
| `--limit <n>` | `10` | The most stations to list, 1 to 500. Each depth bin counts as one. |
| `--format <format>` | `table` | `table`, `csv` or `json`. JSON is the `list_current_stations` MCP result, with a subordinate station's time differences and speed ratios. |
| `--catalogue <file>` | the offline cache | A tide-stations JSON file (`.json`, or `.json.gz` for gzip). |

```bash
s100 tides currents stations --near 48.406,-122.643
s100 tides currents stations --near 37.82,-122.46 --radius 5nm --format json
```

A catalogue downloaded before current stations were added lists none: run
[`tides update`](#tides-update) again.

## tides currents predict

```text
s100 tides currents predict <station> [options]
s100 tides currents predict --near <lat,lon> [options]
```

`tides currents predict` predicts the astronomical tidal current at a current
station from the offline catalogue. Name the station by id (`PUG1701_33`),
by NOAA's id without its depth bin (`PUG1701`) or by name (`Deception Pass`);
the last two take the shallowest bin. Or use `--near` for the nearest station
within `--radius`. It prints the speed in knots, the direction the current
sets toward and whether it floods or ebbs at each step, or with `--events`
the slack waters and the maximum and minimum floods and ebbs.

A prediction is astronomical: wind and river flow change real currents, and
the strongest stream in a pass can be away from the station. It isn't a
forecast, isn't S-111, and isn't for navigation. The engine is
[`EncDotNet.S100.Tides`](../src/EncDotNet.S100.Tides/README.md#predict-tidal-currents).

| Option | Default | Description |
|---|---|---|
| `--near <lat,lon>` | none | Predict at the nearest station to this point instead of a named one. |
| `--radius <distance>` | `10` (km) | With `--near`, how far away the station may be. |
| `--from <time>` | now, rounded down to the step | The first time: ISO 8601 or a date. Without an offset, it's in this computer's time zone. |
| `--to <time>` | 24 hours after `--from` | The last time, like `--from`. At most 366 days after it. |
| `--step <step>` | `30` (minutes) | The time between values: minutes, or with a unit (`10m`, `1h`, `90s`). |
| `--events` | off | Lists the slacks and maximum and minimum floods and ebbs instead of values at each step. |
| `--utc` | off | Reads and prints times in UTC rather than this computer's time zone. |
| `--format <format>` | `table` | `table`, `csv`, `json` or `s111`. JSON is the `predict_current` MCP result, with times in UTC. |
| `-o`, `--output <file>` | `111ZZ01<station>.h5` | With `--format s111`, the file to write. |
| `--catalogue <file>` | the offline cache | A tide-stations JSON file. |

```bash
s100 tides currents predict PUG1701_33
s100 tides currents predict "Deception Pass" --events --from 2026-11-18 --to 2026-11-19
s100 tides currents predict --near 37.82,-122.46 --step 10m --format csv
```

```text
Deception Pass (Narrows) PUG1701_33, harmonic, 5.5 m below the surface, flood
108°, ebb 277°
╭─────────────────────────┬─────────────────────┬────────────────┬───────────╮
│ Time                    │ Event               │ Predicted (kn) │ Direction │
├─────────────────────────┼─────────────────────┼────────────────┼───────────┤
│ 2026-11-18 09:44 -08:00 │ Slack, ebb begins   │            0.0 │         — │
│ 2026-11-18 12:42 -08:00 │ Max ebb             │            5.5 │      277° │
│ 2026-11-18 17:00 -08:00 │ Slack, flood begins │            0.0 │         — │
╰─────────────────────────┴─────────────────────┴────────────────┴───────────╯
Astronomical tidal current only, not a forecast. Wind and river flow change real
currents, and the strongest stream in a pass can be away from the station. Not
for navigation.
Stations: NOAA CO-OPS, retrieved 2026-11-11 (7 days ago).
```

CSV has a header row (`time,predicted_speed_kn,direction_deg_true,phase`, or
`time,event,predicted_speed_kn,direction_deg_true` with `--events`) and ISO
8601 times with their offset. At slack the direction is empty. The station and
the note go to standard error. A rotary station gives its direction at each
maximum and minimum as well.

### Write an S-111 dataset

`--format s111` writes the series as an S-111 Edition 2.0.0 dataset with one
station (data coding format 8), which SoundCharts and the other `s100`
commands open like any S-111 file:

```bash
s100 tides currents predict PUG1701_33 --from 2026-11-18 --to 2026-11-25 --step 10m --format s111 -o deception-pass.h5
```

The dataset says it's a prediction: its producer code is `ZZ01`, which no
real producer has, its type of current data is astronomical prediction, and
its method text names the station and says it's not an official product. The
station's depth is written as `surfaceCurrentDepth`. The step must be a whole
number of seconds up to 65,535 (18 h 12 min). As for S-104, the file is
written with PureHDF, which doesn't work in a NativeAOT build.

## tides update

```text
s100 tides update [--check] [--catalogue <file>]
```

`tides update` downloads NOAA CO-OPS's tide and tidal current stations, with
their harmonic constituents, datums and subordinate-station offsets, into the
offline catalogue. It reads about 2,100 pages from NOAA's metadata API at a
pace the API accepts, so it takes about 10 minutes. The other `tides`
commands and `s100 mcp serve` read the catalogue with no network.
SoundCharts reads its own station list; pass its path with `--catalogue` to
fill it.

The catalogue is `noaa-tide-stations.json.gz` in an `EncDotNet.S100/Tides`
folder in your local application data:

| Platform | Folder |
|---|---|
| Windows | `%LOCALAPPDATA%\EncDotNet.S100\Tides` |
| macOS | `~/Library/Application Support/EncDotNet.S100/Tides` |
| Linux | `~/.local/share/EncDotNet.S100/Tides` |

`--catalogue` keeps it somewhere else. The file format is documented in the
[station catalogue README](../src/EncDotNet.S100.Tides.Stations/README.md#bring-your-own-stations),
so you can also write your own.

`--check` asks NOAA whether it has newer data than the catalogue, with a few
requests: a new datum epoch, stations added, removed or moved, and datums
accepted again. It exits with code `8` when there's newer data. Without a
network, a catalogue over 12 months old counts as stale.

| Option | Default | Description |
|---|---|---|
| `--check` | off | Only checks for newer data. Exits with `0` when the catalogue is current and `8` when it isn't. |
| `--catalogue <file>` | the offline cache | Where to keep the catalogue. |

```bash
s100 tides update
s100 tides update --check
```

## list-specs

```text
s100 list-specs
```

`list-specs` prints a table of the product specifications `s100` reads, with
whether each has a bundled portrayal catalogue and whether it can be rendered
headlessly. S-57 is listed with its portrayal shown as "via S-101". The
weather formats GRIB2, NetCDF, CAP, CODSUS and TC (tropical cyclone advisories) are listed last, with their portrayal
shown as "SoundCharts, in code", and a line under the table says they aren't
S-100 products. See [Supported specifications](#supported-specifications).

## health

```text
s100 health [--host <address>] [--port <port>] [--timeout <seconds>]
```

`health` asks a running `tiles serve`, `feed serve` or `mcp serve --transport http` for its `/healthz` route
and exits with `0` when it answers `200 OK`, or `1` when nothing is listening,
the server answers with an error, or there's no answer in time. It prints what
it asked and what came back. Use it as a container health check: the
[container image](docker.md) has no shell or `curl`.

| Option | Default | Description |
|---|---|---|
| `--host <address>` | `127.0.0.1` | The server's address. |
| `--port <port>` | `8200` | The server's port. `tiles serve` listens on 8200 by default, `feed serve` on 8100 and `mcp serve --transport http` on 8300. |
| `--timeout <seconds>` | `5` | How long to wait for an answer. |

```bash
s100 health
s100 health --port 8100
```

```text
Healthy: http://127.0.0.1:8200/healthz
```

## s57 convert

```text
s100 s57 convert -o <output> <source> [options]
```

`s57 convert` translates an S-57 base cell (`.000`) and writes it as an
ISO/IEC 8211 encoded S-101 dataset (S-100 Part 10a). An inland ENC cell is
written as an S-401 dataset instead. Update files next to the base cell are
applied first. The command then prints a summary of what translated and what
didn't.

| Option | Default | Description |
|---|---|---|
| `-o`, `--output <path>` | required | The S-101 or S-401 file to write. |
| `--target <target>` | `auto` | `auto` writes S-401 for an inland ENC and S-101 otherwise. `s101` or `s401` forces that product. |
| `--report <path>` | off | Also writes the full translation diagnostics as JSON. |
| `--no-updates` | off | Converts the base cell without its update files. |
| `--debug` | off | Prints a full stack trace on error. |

```bash
s100 s57 convert -o my-s101-dataset.000 my-s57-dataset.000
s100 s57 convert -o my-s401-dataset.000 my-inland-s57-dataset.000
s100 s57 convert --target s101 -o my-s101-dataset.000 my-inland-s57-dataset.000
```

The output is an ordinary dataset: use `info`, `validate` and `render` on it.
[Bringing S-57 into the pipeline](s57-to-s101.md) covers the summary, the
report, inland cells and the translation's known gaps.

## s101 convert

```text
s100 s101 convert --to s57 -o <output> <source> [options]
```

`s101 convert` translates an S-101 base dataset (`.000`) and writes it as an
S-57 Edition 3.1 base cell, following S-65 Annex C. Update datasets next to
the base dataset are applied first. The command then prints a summary of what
converted, what Annex C dropped or folded into `INFORM`, and what had no rule.
It warns when the dataset isn't S-101 Edition 2.0.0, the edition Annex C
converts.

The output is for testing and for tools that read only S-57, such as GDAL/OGR
and OpenCPN. It isn't a production ENC: it isn't checked against S-58, and
Annex C expects the producer to finish it by hand.

| Option | Default | Description |
|---|---|---|
| `-o`, `--output <path>` | required | The S-57 cell to write, or with `--exchange-set` the directory to write the exchange set into. |
| `--to <format>` | `s57` | The format to write. Only `s57` is supported. |
| `--exchange-set` | off | Writes `ENC_ROOT/CATALOG.031` and `ENC_ROOT/<cell>/<cell>.000` under the output directory. |
| `--cell-name <name>` | source file name | With `--exchange-set`, the cell's name without extension, such as `XX5CELL1`. |
| `--report <path>` | off | Also writes the full translation diagnostics, with the Annex C clause of each drop, as JSON. |
| `--no-updates` | off | Converts the base dataset without its update datasets. |
| `--no-optional-inform` | off | Skips the conversions to `INFORM` that Annex C marks optional. |
| `--inform-separator <text>` | `;` | The separator between strings folded into one `INFORM` or `NINFOM`. |
| `--max-inform-length <n>` | `300` | The recommended maximum length of `INFORM` and `NINFOM`. |
| `--inform-length <policy>` | `keep` | `keep` keeps a longer text and reports it; `truncate` cuts it at `--max-inform-length`. |
| `--national-language <code>` | first | The ISO 639-2 language, such as `fra`, to prefer for `NOBJNM`, `NINFOM` and `NTXTDS`. |
| `--name-without-usage <policy>` | `by-language` | A name with no `nameUsage`: `by-language` writes an English or unlabelled name to `OBJNAM` and any other to `NOBJNM`; `drop` doesn't convert it. |
| `--debug` | off | Prints a full stack trace on error. |

```bash
s100 s101 convert --to s57 -o XX5CELL1.000 101XX00CELL1.000
s100 s101 convert --to s57 -o XX5CELL1.000 101XX00CELL1.000 --report report.json
s100 s101 convert --to s57 --exchange-set --cell-name XX5CELL1 -o s57set 101XX00CELL1.000
```

Read the cell back with `info`, and check an exchange set's catalogue with
`validate`. [Converting S-101 to S-57](s101-to-s57.md) covers the summary,
what Annex C drops, the options and what a producer still has to do.

## mcp serve

```text
s100 mcp serve <dataset-or-exchange-set> [options]
s100 mcp serve --layer <dataset> [--layer <dataset> ...] [options]
```

`mcp serve` runs an [MCP server](mcp-server.md) over standard input and
output, so an AI agent that starts the process can query and render datasets
without SoundCharts. With `--transport http`, it serves the same tools over
HTTP instead, so agents on other computers, or in other containers, can
connect to it. See [MCP over HTTP](#mcp-over-http). It takes the same input forms as `render`. Datasets that
fail to load are skipped with a warning.

To use it, configure your MCP client to run the command `s100` with the
arguments `["mcp", "serve", "<dataset-or-exchange-set>"]`.

The server provides the read-only query tools and these session tools:
`open_dataset`, `close_dataset`, `close_all_datasets`, `set_palette`,
`set_display_category`, `set_display_mode`, `set_time_step`, `set_viewport`
and `render_to_image`. The session tools change only the server's in-memory
state: they never edit the source datasets or write files. `open_dataset`
adds more datasets while the server runs. Each process is one session, so
start another process for a separate session. The tide tools
`list_tide_stations` and `predict_tide` read the offline station catalogue
(see [`tides update`](#tides-update)). [MCP tools](mcp-server.md#mcp-tools)
lists them all.

| Option | Default | Description |
|---|---|---|
| `--layer <path>` | none | Adds a dataset. Repeat it for each dataset. Can't be combined with `--from`. |
| `--from`, `--exchange-set <path>` | none | Serves every dataset in an exchange set. |
| `--only <specs>` | all | Exchange-set form only. Loads only these product specifications, comma-separated. |
| `--tide-catalogue <file>` | the offline cache | The tide and current station catalogue `list_tide_stations`, `predict_tide`, `list_current_stations` and `predict_current` read. See [`tides update`](#tides-update). |
| `--tide-predictions` | off | Fills S-104 and S-111 gaps with predictions from that catalogue: `sample_coverage`, `sample_coverage_along` and `check_route` use the nearest tide station within 10 km by water where no S-104 covers a point and time, and `sample_coverage` and `sample_coverage_along` the nearest current station where no S-111 does. See [Predicted values in sampling](mcp-server.md#predicted-values-in-sampling). |
| `--transport <transport>` | `stdio` | `stdio`, or `http` to serve over HTTP. |
| `--host <address>` | `127.0.0.1` | HTTP only. The address to listen on. `0.0.0.0` accepts other computers. |
| `--port <port>` | `8300` | HTTP only. The port. `0` picks a free one. |
| `--token <token>` | generated for a non-loopback host | HTTP only. The bearer token clients must send. Letters, digits, `-` and `_`. |
| `--no-token` | off | HTTP only. Serves without a token, even on a non-loopback address. |
| `--allow-changes` | off | HTTP only. Also serves the tools that open datasets by path and change the Library. |
| `--lua-engine <engine>` | `luacsharp` | The Lua engine that runs the portrayal rules: `luacsharp` or `moonsharp`. See [Lua engine](#lua-engine). |
| `--debug` | off | Prints a full stack trace on error. |

```bash
s100 mcp serve dataset.h5
s100 mcp serve --layer enc.000 --layer bathy.h5
s100 mcp serve --from exchange-set.zip --only S101,S102
```

Standard output carries the MCP protocol. Startup messages, load warnings and
errors go to standard error. The server runs until the client closes standard
input, or until you interrupt it.

### MCP over HTTP

```bash
s100 mcp serve charts/ --transport http
s100 mcp serve charts/ --transport http --host 0.0.0.0 --token my-token
```

With `--transport http`, `mcp serve` serves the MCP
[Streamable HTTP](https://modelcontextprotocol.io/specification/latest/basic/transports#streamable-http)
transport at `http://<host>:<port>/mcp`, and prints that URL for each network
address. It runs until you interrupt it or it's sent SIGTERM. Several clients
can connect at once, and they share one session: one client's `set_palette`
changes what `render_to_image` draws for the others.

By default only this computer can connect. With `--host 0.0.0.0`, other
computers can, and `mcp serve` generates a random token unless you pass
`--token` or `--no-token`. It prints a token it generates at startup, but not
one you pass. Clients send the token in a header:

```text
Authorization: Bearer <token>
```

A request without the token is answered `401 Unauthorized`. Pass `--token` to
keep the same token across restarts. A request whose `Origin` header names
another host is answered `403 Forbidden`, so a web page open in a browser
can't use a server on your computer.

Over HTTP, `mcp serve` leaves out the tools that reach beyond the datasets
you served, because a client could use them to read any file the server can
read or make it fetch any URL:

- `open_dataset`, `close_dataset` and `close_all_datasets`.
- The Library tools that change it: `add_library_source`,
  `refresh_library_source`, `remove_library_source`,
  `set_library_source_options`, `library_action`, `await_library_idle` and
  `set_secom_identity`.

The query, session, Library listing and tide tools are served. Pass
`--allow-changes` to serve every tool, as over standard input and output.
Sources you add with `--collection` are in the Library either way.

`/healthz` answers `200 OK` without a token, so `s100 health --port 8300`
checks the server. See [`health`](#health).

## feed serve

```text
s100 feed serve <path> [options]
```

`feed serve` publishes a folder, an exchange set (folder, `CATALOG.XML`,
`CATALOG.031` or `.zip`) or a single dataset as an
[S-100 feed](s100-feed-format.md) over HTTP, so SoundCharts on another
computer can browse and download it. A folder is scanned recursively.

The path is indexed in place; nothing is loaded or copied. `feed serve`
checks it again at most every `--refresh` seconds, so datasets you add or
change while it runs appear in the feed. Files that can't be read are reported
at startup and left out.

The server has these routes:

- `feed.json`, the feed, with an `ETag` so readers can revalidate it. It's
  compressed when the client asks.
- `items/<id>.zip`, one for each dataset, holding its updates and exchange-set
  catalogue.
- `/healthz`, which answers `200 OK` with the text `ok` while the server runs.
  It never has a token. See [`health`](#health).

By default only this computer can connect. With `--host 0.0.0.0`, other
computers can, and `feed serve` adds a random access token to every URL unless
you pass `--token` or `--no-token`. It prints one feed URL for each network
address.

| Option | Default | Description |
|---|---|---|
| `--host <address>` | `127.0.0.1` | The address to listen on. `0.0.0.0` accepts other computers. |
| `--port <port>` | `8100` | The port. `0` picks a free one. |
| `--token <token>` | generated for a non-loopback host | An access token that becomes part of every URL. Letters, digits, `-` and `_`. |
| `--no-token` | off | Serves without a token, even on a non-loopback address. |
| `--title <title>` | the folder or file name | The feed's title. |
| `--refresh <seconds>` | `10` | The most often the path is checked for changes. |

```bash
s100 feed serve charts/
s100 feed serve exchange-set.zip --port 9000
s100 feed serve charts/ --host 0.0.0.0
```

On the other computer, in the SoundCharts Library, choose **Add** >
**Connect to a shared feed…**, paste the URL into **Feed URL**, and choose
**Connect**.

The server runs until you press Ctrl+C or it receives SIGTERM.

## feed export

```text
s100 feed export <path> --out <directory> [options]
```

`feed export` writes the same feed as static files, so any web host can serve
it without running `s100`: a web server, S3, GitHub Pages or a NAS. It takes the
same kinds of path as `feed serve`, and writes:

- `feed.json`
- `items/<id>.zip`, one for each dataset

Upload the folder's contents, then add the URL of `feed.json` in SoundCharts.

- **Incremental.** Exporting into the same folder again rewrites only the zips
  whose datasets changed, and removes the zips of datasets that are gone. The
  stamps it compares are kept in `.s100-feed-export.json`, which you don't need
  to upload.
- **Consistent during upload.** `feed.json` is written last, so it never lists
  a zip that isn't there yet. Other files in the folder are left alone.
- **Outside the published path.** `--out` can't be inside `<path>`, or the next
  export would publish the export.

| Option | Default | Description |
|---|---|---|
| `-o`, `--out <directory>` | required | The folder to write the feed to. It's created if needed. |
| `--title <title>` | the folder or file name | The feed's title. |

```bash
s100 feed export charts/ --out site/charts
s100 feed export exchange-set.zip -o export --title "Harbour survey"
```

When some datasets can't be written, they're left out of the feed and the
command exits with code `1`.

## Lua engine

S-101, S-57, S-401, S-131, S-125 and S-102 are portrayed by the Lua rules in
their portrayal catalogues. `render`, `tiles export`, `tiles serve` and
`mcp serve` run those rules on Lua-CSharp unless `--lua-engine moonsharp`
selects MoonSharp, the previous engine:

```bash
s100 render chart.000 chart.png --lua-engine moonsharp
```

The engines produce the same chart, with one known exception: a sectored light
whose sectors share a nominal range can list its colours in a different order
in its light description. Lua-CSharp lists them as reference Lua does.

## Supported specifications

| Family | Specifications | Notes |
|---|---|---|
| Vector, ISO 8211 | S-101, S-401, S-57 | S-57 is translated to S-101, or to S-401 for an inland ENC, and drawn with that product's portrayal, not S-52. |
| Vector, GML | S-122, S-124, S-125, S-127, S-128, S-129, S-131, S-201, S-411, S-412, S-421 | |
| Coverage, HDF5 | S-102; S-104 data coding formats 1, 2 and 8; S-111 data coding formats 1, 2, 3 and 8 | Gridded data is drawn as a coverage; station and node data as glyphs. S-104 has no IHO portrayal, so its stations and grids use SoundCharts' own gauges and isolines; see [S-104 portrayal](../src/EncDotNet.S100.Datasets.S104/README.md#portrayal). S-111 draws current arrows over the coverage. Other data coding formats exit with code `4`. |
| Weather, not S-100 | GRIB2, NetCDF-3 and NetCDF-4 (CF), CAP 1.2 and NWS alerts GeoJSON, WPC coded surface bulletins (CODSUS), NHC tropical cyclone GIS zips (`TC`) | Wind and wave grids on a regular latitude/longitude grid, and marine weather warnings, drawn with SoundCharts' own styles; and surface analyses drawn as fronts and pressure centres. Tropical cyclone advisories are drawn in storm ink: the cone, wind radii, tracks, forecast points and storm. Not for navigation. See [Show wind and waves on the chart](weather.md). |

## Exit codes

| Code | Meaning |
|---|---|
| `0` | Success. |
| `1` | Unexpected error. Run again with `--debug` for a stack trace. For `feed export`, some datasets couldn't be written. For `health`, the server didn't answer `200 OK`. |
| `2` | The product specification couldn't be detected, or no datasets could be resolved or loaded from the inputs. An HDF5 file is detected only from its root `productSpecification` attribute, which S-100 requires; when that's missing, names a product the CLI doesn't read, or the file can't be read as HDF5 (for example an encrypted S-100 dataset outside its exchange set), the message says so. For `tiles export`, also: the datasets have no geometry, or the tile set is over 100,000 tiles without `--yes`. For `tiles serve`: the path isn't a raster tile set it can read, or, as for `tiles export`, the datasets can't be read or have no geometry. For `route check`: the route file can't be read. For `tides stations` and `tides predict`: there's no station catalogue, no station matches, or the station can't be predicted on the datum. |
| `3` | The product specification doesn't support headless rendering or display-list output. |
| `4` | The dataset is recognised, but its structure or encoding isn't supported, such as an unsupported data coding format. |
| `5` | The dataset is recognised but doesn't conform: a required attribute, dataset or group is missing or malformed. |
| `6` | `validate` only: failing findings, or failed signature or checksum checks. |
| `7` | `route check` only: findings at or above `--fail-on`. |
| `8` | `tides update --check` only: NOAA has newer data than the catalogue. |
| other non-zero | Invalid arguments, such as a missing file or an unknown palette. |

## See also

- [Render S-102 to PNG](scenarios/render-s102-to-png.md): a worked example of
  the `render` command.
- [Compose S-101 and S-102](scenarios/compose-s101-s102.md): a composite
  render of a chart and a bathymetry surface.
- [Find when the tide is above a height](scenarios/tide-above-a-height.md):
  `tides predict` and the `predict_tide` MCP tool at Seattle.
- [S-98 interoperability](design/s98-interoperability.md): how composite
  layers are ordered.
- [PMTiles](https://docs.protomaps.com/pmtiles/): the single-file tile
  archive format `tiles export` writes, and how to serve and view it.

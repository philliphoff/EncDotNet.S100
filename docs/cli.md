# Command-line rendering

`s100` is a cross-platform command-line tool for S-100 datasets. It renders one
dataset, or a composite of several, to a PNG, JPEG or WebP image, or to a
raster tile set for web maps. It also
validates datasets and exchange sets, reports what a dataset contains, picks
the features at a point, converts S-57 cells, serves datasets to AI agents over
MCP, and publishes datasets as an S-100 feed.

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
| [`tiles export`](#tiles-export) | Renders a dataset, a composite of datasets, or a whole exchange set as XYZ raster tiles: a `{z}/{x}/{y}` folder, a PMTiles archive or an MBTiles database. |
| [`info`](#info) | Shows the detected product specification, edition, render support, display modes and time steps. |
| [`identify`](#identify) | Lists the features and coverage values at a latitude and longitude. |
| [`validate`](#validate) | Checks a dataset against its specification's rule pack, or checks an exchange set's signatures and checksums. |
| [`list-specs`](#list-specs) | Lists the supported product specifications. |
| [`s57 convert`](#s57-convert) | Converts an S-57 cell to an S-101 dataset, or an inland ENC cell to S-401. |
| [`mcp serve`](#mcp-serve) | Serves the S-100 MCP tools over standard input and output for a set of datasets. |
| [`feed serve`](#feed-serve) | Serves a folder, exchange set or dataset as an S-100 feed over HTTP. |
| [`feed export`](#feed-export) | Writes a folder, exchange set or dataset as a static S-100 feed. |

These options work without a command:

| Option | What it does |
|---|---|
| `--help` | Prints help. Add it after a command for that command's help, for example `s100 render --help`. |
| `--version` | Prints the tool's version. |
| `--skill` | Prints a single Markdown document that describes every command, option, example, exit code and limitation. It's written for AI agents, has no ANSI styling, and skips the update check. |

### Update notices

Release builds check the repository's latest GitHub release at most once every
24 hours. When a newer version exists, each run writes one line to standard
error:

```text
Update available: s100 0.25.0 (current 0.24.0): https://github.com/philliphoff/EncDotNet.S100/releases/tag/v0.25.0
```

Standard output and the exit code don't change, so you can still parse JSON
output. Network and cache failures are silent. Development builds don't check.

## render

```text
s100 render <dataset> <output> [options]
s100 render --layer <dataset> [--layer <dataset> ...] <output> [options]
s100 render <exchange-set> <output> [options]
```

`render` detects each input's product specification, runs its portrayal, and
writes an image. It has three forms:

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
| `--symbol-scale <factor>` | `1.0` | Symbol scale factor. |
| `--text-scale <factor>` | `1.0` | Text scale factor. |
| `--time-step <index>` | `0` | Zero-based time step for time-series datasets (S-104, S-111). `s100 info` lists the steps. |
| `--background <hex>` | opaque white | Background colour, `#RRGGBB` or `#AARRGGBB`. |
| `--bbox <minLon,minLat,maxLon,maxLat>` | fit to the data | Draws this WGS-84 bounding box. Can't be combined with `--center` and `--scale`. |
| `--center <lon,lat>` | fit to the data | Centre of the view. Use with `--scale`. |
| `--scale <denominator>` | fit to the data | Scale denominator, for example `50000` for 1:50,000. Use with `--center`. |
| `--no-text` | off | Hides text. Same as `--hide text`. |
| `--hide <categories>` | none | Hides drawing-instruction categories, comma-separated: `text`, `points`, `lines`, `areas`. Adds to `--no-text`. |
| `--basemap <mode>` | `none` | `offline` draws the bundled Natural Earth 1:10m land layer under the chart, in a parchment tone (`238,232,220`). Online tile basemaps aren't available. |
| `--display-mode <mode>` | `ice-concentration` | S-411 only. `ice-concentration`, `ice-sod` (stage of development), or `ice-navigational`. See [Sea-ice display modes](#sea-ice-display-modes). |
| `--no-updates` | off | Single-dataset form only. Renders an S-101 base cell without its update files. See [S-101 updates](#s-101-updates). |
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
order first (S-100 Part 10a). `info` does the same. A missing, out-of-order or
unreadable update is reported but doesn't stop the command. Pass
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
  "spec": "S-124/1.0.0",
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
- `--palette`, `--symbol-scale`, `--text-scale`, `--display-mode` and
  `--time-step` change the instructions. `--width`, `--height`, `--quality` and
  `--background` are ignored. `--bbox`, `--center` and `--scale` are errors,
  because a display list has no viewport.

## tiles export

```text
s100 tiles export <dataset> -o <output> [options]
s100 tiles export --layer <dataset> [--layer <dataset> ...] -o <output> [options]
s100 tiles export <exchange-set> -o <output> [options]
```

`tiles export` renders datasets as raster tiles on the XYZ Web Mercator grid
that MapLibre, Leaflet and OpenLayers use. The tiles carry the S-100 portrayal
itself (symbols, scale minimums and palettes), so a web map shows the chart
without styling it. It takes the same three input forms as
[`render`](#render), and `-o` is required.

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

Set `--container` to choose the container whatever the extension. An existing
`.pmtiles` or `.mbtiles` file at the output path is replaced.

### Tiles export options

| Option | Default | Description |
|---|---|---|
| `--layer <path>` | none | Adds a dataset as a composite layer. Repeat it for each dataset. |
| `--exchange-set`, `--from <path>` | none | Tiles an exchange set: a folder with `CATALOG.XML`, the `CATALOG.XML` file, or a `.zip`. Can't be combined with `--layer`. |
| `--only <specs>` | all | Exchange-set form only. Tiles only these product specifications, comma-separated. |
| `-o`, `--output <path>` | required | A folder, a `.pmtiles` file or a `.mbtiles` file. Its parent folder must exist. |
| `--container <kind>` | from the output | `xyz` (a folder), `pmtiles` or `mbtiles`. |
| `--min-zoom <0-24>` | from the data | Lowest zoom level. See [Zoom levels](#zoom-levels). |
| `--max-zoom <0-24>` | from the data | Highest zoom level. |
| `--bbox <minLon,minLat,maxLon,maxLat>` | the data's extent | Writes only the tiles that cover this WGS-84 bounding box. |
| `--tile-size <pixels>` | `256` | `256`, or `512` for high-DPI tiles of the same grid. |
| `--format <format>` | `png` | `png`, `jpeg` (or `jpg`) or `webp`. |
| `--quality <1-100>` | `90` | Encoder quality for `jpeg` and `webp`. Ignored for `png`. |
| `--palette <palette>` | `day` | `day`, `dusk` or `night`. |
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
| `--no-updates` | off | Tiles an S-101 base cell, given on its own or with `--layer`, without its update files. |
| `--debug` | off | Prints full stack traces on error, and portrayal and Lua diagnostics on standard error. |

### Tiles export examples

```bash
s100 tiles export enc.000 -o tiles/
s100 tiles export enc.000 -o chart.pmtiles --min-zoom 10 --max-zoom 15
s100 tiles export --layer enc.000 --layer bathy.h5 -o chart.pmtiles --tile-size 512
s100 tiles export exchange-set/ -o tiles/ --format webp --skip-empty
s100 tiles export enc.000 -o chart.mbtiles --max-zoom 16
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

Without `--min-zoom`, the lowest zoom is the one nearest the coarsest cell's
minimum display scale. Without `--max-zoom`, the highest is one level past the
finest cell's compilation scale. Products without these scales, such as GML
products and coverages, start at the zoom where their extent fits in about one
tile and go six levels deeper, up to zoom 18.

`tiles export` prints the number of tiles before it renders any. Each zoom level has
four times the tiles of the one before, so a set grows quickly. Above 100,000
tiles, `tiles export` asks before writing, or exits with code `2` when it can't ask.
Pass `--yes` to skip the question, or narrow `--bbox` or the zoom range.

### Tile set limits

- **Overlapping cells.** Every cell draws at each zoom level where it's
  visible. A finer cell doesn't hide a coarser cell under it, as it does in
  SoundCharts. This is tracked in
  [#859](https://github.com/philliphoff/EncDotNet.S100/issues/859).
- **One palette and time step.** A tile set holds one `--palette` and one
  `--time-step`. Run `tiles export` again for each one you need.
- **Coverages.** S-102, S-104 and S-111 are portrayed again for each zoom
  level, so grid sampling and current-arrow spacing suit its resolution.
- **S-101 updates.** As with `render`, the exchange-set form doesn't apply
  S-101 update files.

## info

```text
s100 info <dataset> [options]
```

`info` prints the detected product specification and edition, and whether the
dataset can be rendered headlessly. For a time-series dataset it lists the
time steps with the indexes `render --time-step` takes. For a product with
display modes, such as S-411, it lists the values `render --display-mode`
takes. For an S-101 base cell, it applies the [update files](#s-101-updates)
first.

```bash
s100 info currents.h5
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
| `--spec <spec>` | all | Reports only this product specification, for example `S-124`. |
| `--time <iso8601>` | first step | A UTC time for S-104 and S-111 coverages. The nearest time step is sampled. Ignored for S-102. |
| `--max-results <n>` | `20` | Maximum features to report, from 1 to 200. |
| `--attributes` | off | Includes each feature's attributes. |
| `--format <format>` | `table` | `table` or `json`. |
| `--debug` | off | Prints full stack traces on error. |

```bash
s100 identify warnings.gml --lat 51.085 --lon 1.30
s100 identify --layer enc.000 --layer bathy.h5 --lat 50.1 --lon -1.4
s100 identify --from exchange-set.zip --lat 50.1 --lon -1.4 --format json
s100 identify chart.000 --lat 50.1 --lon -1.4 --attributes
```

The JSON output is an object with `point`, `totalMatched`, `truncated`,
`features` (ranked), `samples` (one for each coverage dataset that covers the
point) and, when an input was skipped, `warnings`.

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

## list-specs

```text
s100 list-specs
```

`list-specs` prints a table of the product specifications `s100` reads, with
whether each has a bundled portrayal catalogue and whether it can be rendered
headlessly. S-57 is listed with its portrayal shown as "via S-101". See
[Supported specifications](#supported-specifications).

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

## mcp serve

```text
s100 mcp serve <dataset-or-exchange-set> [options]
s100 mcp serve --layer <dataset> [--layer <dataset> ...] [options]
```

`mcp serve` runs an [MCP server](mcp-server.md) over standard input and
output, so an AI agent that starts the process can query and render datasets
without SoundCharts. It takes the same input forms as `render`. Datasets that
fail to load are skipped with a warning.

To use it, configure your MCP client to run the command `s100` with the
arguments `["mcp", "serve", "<dataset-or-exchange-set>"]`.

The server provides the read-only query tools and these session tools:
`open_dataset`, `close_dataset`, `close_all_datasets`, `set_palette`,
`set_display_category`, `set_display_mode`, `set_time_step`, `set_viewport`
and `render_to_image`. The session tools change only the server's in-memory
state: they never edit the source datasets or write files. `open_dataset`
adds more datasets while the server runs. Each process is one session, so
start another process for a separate session. [MCP tools](mcp-server.md#mcp-tools)
lists them all.

| Option | Default | Description |
|---|---|---|
| `--layer <path>` | none | Adds a dataset. Repeat it for each dataset. Can't be combined with `--from`. |
| `--from`, `--exchange-set <path>` | none | Serves every dataset in an exchange set. |
| `--only <specs>` | all | Exchange-set form only. Loads only these product specifications, comma-separated. |
| `--debug` | off | Prints a full stack trace on error. |

```bash
s100 mcp serve dataset.h5
s100 mcp serve --layer enc.000 --layer bathy.h5
s100 mcp serve --from exchange-set.zip --only S101,S102
```

Standard output carries the MCP protocol. Startup messages, load warnings and
errors go to standard error. The server runs until the client closes standard
input, or until you interrupt it.

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

The server has two routes:

- `feed.json`, the feed, with an `ETag` so readers can revalidate it. It's
  compressed when the client asks.
- `items/<id>.zip`, one for each dataset, holding its updates and exchange-set
  catalogue.

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

## Supported specifications

| Family | Specifications | Notes |
|---|---|---|
| Vector, ISO 8211 | S-101, S-401, S-57 | S-57 is translated to S-101, or to S-401 for an inland ENC, and drawn with that product's portrayal, not S-52. |
| Vector, GML | S-122, S-124, S-125, S-127, S-128, S-129, S-131, S-201, S-411, S-421 | |
| Coverage, HDF5 | S-102; S-104 data coding formats 1, 2 and 8; S-111 data coding formats 1, 2, 3 and 8 | Gridded data is drawn as a coverage; station and node data as glyphs. S-111 draws current arrows over the coverage. Other data coding formats exit with code `4`. |

## Exit codes

| Code | Meaning |
|---|---|
| `0` | Success. |
| `1` | Unexpected error. Run again with `--debug` for a stack trace. For `feed export`, some datasets couldn't be written. |
| `2` | The product specification couldn't be detected, or no datasets could be resolved or loaded from the inputs. For `tiles export`, also: the datasets have no geometry, or the tile set is over 100,000 tiles without `--yes`. |
| `3` | The product specification doesn't support headless rendering or display-list output. |
| `4` | The dataset is recognised, but its structure or encoding isn't supported, such as an unsupported data coding format. |
| `5` | The dataset is recognised but doesn't conform: a required attribute, dataset or group is missing or malformed. |
| `6` | `validate` only: failing findings, or failed signature or checksum checks. |
| other non-zero | Invalid arguments, such as a missing file or an unknown palette. |

## See also

- [Render S-102 to PNG](scenarios/render-s102-to-png.md): a worked example of
  the `render` command.
- [Compose S-101 and S-102](scenarios/compose-s101-s102.md): a composite
  render of a chart and a bathymetry surface.
- [S-98 interoperability](design/s98-interoperability.md): how composite
  layers are ordered.
- [PMTiles](https://docs.protomaps.com/pmtiles/): the single-file tile
  archive format `tiles export` writes, and how to serve and view it.

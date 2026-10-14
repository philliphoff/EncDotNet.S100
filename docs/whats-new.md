# What's new

This page lists notable changes to the libraries, tools and documentation, by
month. For package versions and release assets, see
[GitHub releases](https://github.com/philliphoff/EncDotNet.S100/releases).

## October 2026

- **Passage on the Timeline.** While Check passage is on, the Timeline shows
  the passage (departure to arrival, with a tick at the minimum UKC) and its
  departure window, which you can drag to set the departure. **Follow
  passage** pins the view time to the ETA at the Clearance cursor, so S-104
  and S-111 show what the ship meets. The MCP tool `set_follow_passage`
  turns it on, and `get_timeline_state` reports the lanes.
- **Departure window.** The Clearance tab has a departure band under the
  profile. It shows which departures across the loaded S-104 range meet your
  settings, and which arrive after the data ends. Drag it to set the
  departure. The verdict card lists the window, such as "Departures
  05:35–06:40 meet your settings", and **Depart {HH:MM}** jumps to the nearest
  departure in it. The new MCP tool `set_departure` sets the departure, and
  `get_check_state`, `get_clearance_state` and `get_passage_result`
  (`includeWindow`) report the window.
- **Findings for a route.** In Check passage mode, the Routes panel's
  **Findings** tab lists what to review along the route, grouped **MUST
  REVIEW**, **CHECK** and **INFO**: stretches below your UKC setting,
  S-101-only depths, water-level gaps and expired S-111 runs. Clicking one
  zooms the map to it and moves the Clearance cursor there. The shared
  `RouteFinding` model in `EncDotNet.S100.Navigation` is ready for the route
  checks in #888. The MCP tools `get_findings` and `select_finding` read and
  select findings.
- **Clearance profile.** While Check passage is on, the bottom dock gains a
  **Clearance** tab next to the Timeline. It shows the seabed, the
  required-UKC band, the water level and the keel along the route, against
  distance or ETA. A readout traces each number to its S-102 cell or S-101
  area, and to its S-104 station and time step. The MCP tools
  `get_clearance_state` and `set_clearance_view` read and set the tab.
- **Check passage.** In SoundCharts, the Routes panel's **Check passage**
  toggle colours the active route by under-keel clearance at ETA. It shows a
  verdict card with the minimum UKC and where it occurs, and a min-UKC chip on
  each leg. Edits recompute live. The MCP tools `set_check_mode`,
  `get_check_state` and `set_passage_cursor` drive it. See the
  [viewer README](../src/EncDotNet.S100.Viewer/README.md) and the
  [route tools](mcp-server.md#route-tools).
- **Under-keel clearance along a route.** The new
  `EncDotNet.S100.Navigation` package computes leg ETAs and checks UKC along a
  passage. It adds S-104 water level to S-102 charted depth at each sample's
  ETA, falls back to S-101 depths as low confidence, and reports datum
  mismatches instead of mixing datums. The new MCP tool `get_passage_result`
  checks a SoundCharts route or a polyline and draft, and can sweep departures
  to find a window. See the
  [package README](../src/EncDotNet.S100.Navigation/README.md) and
  [Check a passage's clearance](mcp-server.md#check-a-passages-clearance).
- **Raster tiles.** The new `s100 tiles export` command renders a dataset, a
  composite or an exchange set as XYZ Web Mercator tiles for MapLibre,
  Leaflet and OpenLayers, written as a `{z}/{x}/{y}` folder, a PMTiles
  archive or an MBTiles database. Each zoom level draws only what's visible at its scale. See
  [`tiles export`](cli.md#tiles-export).
- **Serving tiles.** `s100 tiles serve` serves tiles on XYZ URLs with a
  TileJSON document and a preview page, so a web map on your computer can use
  them as a tile source. It serves a built folder, PMTiles or MBTiles tile set,
  or renders datasets as their tiles are asked for, in the day, dusk and night
  palettes and, for S-104 and S-111, at any time step chosen in the URL. It reopens the datasets when their files change, and with
  `--cache-dir`, rendered tiles are kept on disk across restarts, reused only
  while the data and settings are unchanged. See
  [`tiles serve`](cli.md#tiles-serve).
- **Overlapping chart cells.** In `s100 tiles export`, `s100 tiles serve`,
  composite `s100 render` and the library's headless compositor, a finer
  chart cell now hides the coarser cell it overlaps, as SoundCharts does, so
  overlapping cells of different scales no longer draw on top of each other.
- **Signing and permits.** S-100 Part 15 support now covers the data
  server's side. `Part15Signer` produces ECDSA P-384 signatures, and
  `PermitFileWriter` writes a `PERMIT.XML` and its `PERMIT.SIGN`, using cell
  keys wrapped by `DataPermit.Create`. The existing readers verify everything
  these types write. See
  [Reading protected exchange sets](protected-exchange-sets.md#create-a-test-exchange-set).
- **S-57 validation reports the source data.** Validating a NOAA cell no
  longer reports thousands of `S101-as-S57/*` errors caused by the S-57 to
  S-101 translation. Complex sub-attributes now carry their parent index, ring
  checks follow edge orientation, and bridge spans get their own feature
  identifier. Findings now carry a location, so the viewer's validation
  overlay marks them. See [Bringing S-57 into the pipeline](s57-to-s101.md).
- **Documentation.** [Getting started](getting-started.md) now has a
  quickstart for each of the desktop app, the .NET library and the
  command-line tool, and the [documentation home](index.md) replaces the
  Start here page. A [documentation style guide](docs-style.md) sets the
  structure and tone for these pages.

## September 2026

- **Local collection manifests.** A hand-written `*.s100collection.json`
  file names groups of local folders, such as one per producing country. Add
  it to the viewer's Library, pick groups as you would an online catalogue's
  facets, and browse, load or change each group on its own. See
  [Local collection manifests](local-collection-manifest.md).
- **Developer guides** for the library:
  [Loading datasets](loading-datasets.md),
  [Reading protected exchange sets](protected-exchange-sets.md),
  [Reading product data](reading-product-data.md) and
  [Custom catalogues and validation](catalogues-and-validation.md). Their code
  samples were run against the repository's test data.
- **Facade additions.** The `EncDotNet.S100` package gained
  `S100Dataset.OpenAsync(IAssetSource, …)`, `S100ExchangeSet` (folders,
  `CATALOG.XML` or ZIPs, with S-101 updates applied), and the
  `WithDecryption` and `Validate` extensions.
- **XML documentation.** Every public API in the NuGet packages has XML
  documentation, and the packages include it for IntelliSense.
- **Docs site.** The API reference covers every package, the package READMEs
  are published under **Packages**, and the site is built on every pull
  request.
- **S-401 (IEHG inland ENC).** The IEHG feature and portrayal catalogues are
  bundled, and content-based detection tells S-401, S-101 and S-57 apart
  inside the shared `.000` extension. S-401 datasets support render,
  identify, query and describe. No validation rule pack ships yet, so
  `s100 validate` reports that no rules are available for S-401.
- **S-57 inland ENCs.** Cells that declare `DSID`/`PRSP` = 10, such as USACE
  river charts, are portrayed with the S-401 catalogues and keep their S-57
  identity. Large inland cells that couldn't be opened before now load,
  because of a fix in the ISO 8211 reader in `EncDotNet.Iso8211` 0.6.1. The
  inland object classes and attributes of the IENC Feature Catalogue 2.4 are
  translated to S-401, so inland bridges, distance marks, notice marks and
  waterway gauges appear.
- **`s100 s57 convert` and inland cells.** The command writes an inland ENC
  cell as an S-401 dataset, so its inland features are kept.
  `--target s101|s401` overrides the choice, and the summary and the
  `--report` JSON name the product written.

## July 2026

- **Docs site.** Added the Start here page, the task guides
  ([Render S-102 to PNG](scenarios/render-s102-to-png.md),
  [Inspect S-124 warnings](scenarios/inspect-s124-warnings.md) and
  [Compose S-101 and S-102](scenarios/compose-s101-s102.md)), the
  [Top APIs](top-apis.md) page, architecture and workflow diagrams, and a
  site theme.
- **S-57 guide.** Added [Bringing S-57 into the pipeline](s57-to-s101.md),
  which covers converting, viewing and validating S-57 cells, folding in
  sibling updates, and the `s100 s57 convert` diagnostics summary
  (`--report`).

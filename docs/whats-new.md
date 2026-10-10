# What's new

This page lists notable changes to the libraries, tools and documentation, by
month. For package versions and release assets, see
[GitHub releases](https://github.com/philliphoff/EncDotNet.S100/releases).

## October 2026

- **Raster tiles.** The new `s100 tiles export` command renders a dataset, a
  composite or an exchange set as XYZ Web Mercator tiles for MapLibre,
  Leaflet and OpenLayers, written as a `{z}/{x}/{y}` folder, a PMTiles
  archive or an MBTiles database. Each zoom level draws only what's visible at its scale. See
  [`tiles export`](cli.md#tiles-export).
- **Serving tiles.** `s100 tiles serve` serves tiles on XYZ URLs with a
  TileJSON document and a preview page, so a web map on your computer can use
  them as a tile source. It serves a built folder, PMTiles or MBTiles tile set,
  or renders datasets as their tiles are asked for, in the day, dusk and night
  palettes. With `--cache-dir`, rendered tiles are kept on disk across
  restarts, and reused only while the data and settings are unchanged. See
  [`tiles serve`](cli.md#tiles-serve).
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

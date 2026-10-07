# EncDotNet.S100.Collections

Index collections of S-100 and S-57 datasets without loading them.

## Overview

A **collection** is a named, persisted grouping of exchange sets and datasets, for example "all ENCs for Alaska". It is built from one or more **sources**. **Indexing** a source produces one `CollectionItem` per dataset. Each item carries the product, the edition and update, the issue date, the scale band, the coverage polygons, and where the data lives. The datasets themselves are never loaded.

Key types:

- **`DatasetCollection`** and **`CollectionSource`** — the persisted definitions. Sources are stored as references (paths or URLs), never copies. The kinds are:
  - `LocalFolderSource`
  - `ExchangeSetSource`
  - `S128CatalogueSource`
  - `NoaaEncFeedSource`
  - `UsaceIencFeedSource`
  - `S100CatalogueFeedSource`
  - `S100ForecastFeedSource`
- **`CollectionIndexer`** — indexes any supported source. It reuses a previous `SourceIndex` when the source's fingerprint is unchanged.
- **`LocalSourceIndexer`** — handles folders and exchange sets:
  - S-100 exchange sets (`CATALOG.XML`), including their GML coverage polygons.
  - S-57 exchange sets (`CATALOG.031`), plus each cell's `DSID` record for the edition, update and issue date.
  - Exchange sets inside ZIP files.
  - Loose datasets, read through a host-supplied `DatasetProbe`.
- **`S128CatalogueIndexer`** — turns S-128 Catalogue of Nautical Products entries into catalogue-only items.
- **`NoaaEncFeedIndexer`** — handles the NOAA ENC product catalogue (`ENCProdCat.xml`):
  - Each cell becomes an online item with its coverage polygons, edition, update, issue date and download link.
  - A `NoaaEncFilter` scopes a source by state, Coast Guard district or region.
  - The catalogue is cached on disk and revalidated with conditional requests, so it is only transferred when it changes. When the server can't be reached, the cached copy is used.
  - `NoaaEncFacets` summarises the states, districts and regions in the catalogue, with cell counts and download sizes, for choosing a filter.
- **`UsaceIencFeedIndexer`** — handles the USACE Inland ENC product catalogues (river cells and the buoy overlay):
  - Each cell becomes an online item with its bounding box. USACE publishes no coverage polygons.
  - USACE's `edition` value (e.g. `22.16`) is read as edition 22, update 16.
  - `UsaceIencFilter` scopes a source by river, and `UsaceIencFeedIndexer.Rivers` gives cell counts and sizes per river.
  - Caching is the same as for the NOAA feed.
- **`S100CatalogueFeedIndexer`** — handles remote S-100 exchange catalogues (`CATALOG.XML`) published over HTTP with their datasets beside them, such as NOAA's S-102 bathymetry on AWS Open Data:
  - Dataset file names (including `file:../…`) resolve against the catalogue URL. Each dataset becomes an online item with its coverage polygons and edition, downloading as its own file.
  - Items keep their identity across editions: NOAA's version suffixes are dropped from tile names (`102US004SC1EV262247` → `102US004SC1EV`).
  - Each dataset's folder below the exchange set's root (for NOAA's S-102, `Region/Area`) is its group. `S100CatalogueFilter` scopes a source by folder and navigation purpose, and `S100CatalogueFacets` summarises the regions, areas and purposes.
  - S-100 catalogues carry no sizes. When the catalogue is in an S3 bucket, `S3ObjectListing` lists the selected folders for sizes and dates.
  - Gzip-encoded catalogues are read as they are. Caching is the same as for the NOAA feed.
- **`S100ForecastFeedIndexer`** — handles S-100 forecast feeds: one folder per forecast model, each with a catalogue of its latest run only, such as NOAA's S-111 surface currents on AWS:
  - A source names its models (`ForecastModel`: id, water body, cadence, forecast horizon) and whether runs download as tiles or as one file per model (`ForecastShape`).
  - Each item is stamped with its run time and valid window (`run`, `validTo`). Runs have no editions, so a downloaded run is outdated once a later run is listed. Items keep their identity across runs, so a new run replaces the old one on download.
  - `GetModelsAsync` summarises each model's latest run (tiles, sizes, footprint) for choosing models.
  - Catalogues are revalidated after a minute, since each model's catalogue is overwritten with every run.
- **`EncCellDownloader`** — downloads a cell's zip into a managed folder:
  - The zip is first written to a `.partial` file, then extracted to a staging folder.
  - The new copy replaces any old one only once it is complete.
  - It finds the cell's layout itself, so NOAA, USACE and bare-`.000` zips all work.
  - A download that is not a zip is kept as a bare file when the item states its layout (remote S-100 datasets).
- **`KnownCatalogueSources`** — the curated list of known online chart catalogues. See the next section.
- **`CollectionJson`** — JSON persistence:
  - collection definitions are written as indented JSON
  - source indexes are written as compact, gzip-compressed JSON

See `docs/design/dataset-collections.md` for the design.

## Library runtime

The `EncDotNet.S100.Collections.Library` namespace holds a host-neutral library: the collections a user keeps, and how each item stands right now. The SoundCharts viewer's Library panel is built on it, and a headless host (such as the `s100` MCP server, #792) can use it the same way.

- **`CollectionLibrary`** — owns a set of collections:
  - It persists their definitions to a store file (`collections.json`) and caches each source's index on disk (`CollectionLibraryOptions`).
  - It re-indexes sources one at a time on a background worker. `WhenIdle` waits for the queue to drain.
  - It publishes immutable `LibraryCollection` / `LibrarySource` snapshots and a coarse `Changed` event.
  - Loaded S-128 catalogues can appear in a transient session collection until they are kept.
- **`LibraryItemState`** — one item as a host sees it:
  - `Availability` is one of listed, online, local, missing, on-pan, loaded, update or expired. `LibraryAvailabilityNames` gives each state's wire name.
  - `EffectiveItem` is the copy that would open.
  - `ValidWindow` is the time the data covers: a forecast run's window, or a dataset's own time coverage.
  - The host supplies its downloaded copies (`ILibraryLocalCopies`) and what it has open (`LibraryLoadState`).
- **`LibraryQuery`** — finds items by collection or source, state, product, text, bounding box, covering point (most detailed first, via `CoverageHitTest`) and valid time.
- **`ForecastRuns`** — forecast-run facts of an item: its model, horizon, shown run window, and its S-102 twin tile.
- **`LibraryDownloads`** — downloads online items (ENC cells, community packages, S-100 feeds and the NOAA forecast catalogues) through `EncCellDownloader`, at most three at a time:
  - Progress is a plain `LibraryDownloadProgress` (polled, raised as `ProgressChanged`, or passed to an `IProgress`), with per-item `StatusOf`.
  - `ManagedFolders` routes each download to the viewer's managed folder layout, so every host keeps downloads in the same place.
  - It is the `ILibraryLocalCopies` that turns a downloaded item into a local one.
- **`LibraryLoader`** — opens items into a host's session:
  - `Plan` skips what can't be opened and groups the rest by exchange set.
  - Each group opens through the host's `ILibraryDatasetOpener`. The viewer's opener registers the group with its exchange-set service. `CatalogLibraryOpener` in `EncDotNet.S100.Mcp.Tools` loads it into any `IMutableDatasetCatalog`.
- **`LibraryOperations`** — the actions a headless host runs: load, and download (then re-index packages or open the downloads).
  - Every operation is tracked by a `LibraryActivityTracker` from the call until its datasets are open.
  - `AwaitIdleAsync` therefore never reports idle between a download finishing and its datasets opening.

## Known catalogue sources

`KnownSources/known-sources.json` is embedded in the library and exposed as `KnownCatalogueSources.All`. It lists the online chart catalogues the viewer offers under **Add Online Catalogue**. The list is maintained here, under this repository's MIT licence.

To add a catalogue:

- **Point at the provider's own catalogue URL.** Don't copy entries or data from other projects' source lists, and never from GPL-licensed ones such as OpenCPN's.
- **Check the licence** of any third-party list you reference. For example, the community `chartcatalogs/catalogs` lists are CC0.
- **Use a supported `format`** (`noaaEnc`, `usaceIenc`, `chartCatalogs`, `s100Feed`, `s100ExchangeCatalogue` or `s100ForecastModels`). Entries in a format this build doesn't know are skipped, not rejected, so newer lists stay loadable.
- **Describe what the catalogue provides honestly:**
  - `coverage`: `polygons`, `boundingBoxes` or `none`
  - `editions`: whether it lists editions and updates
  - `sizes`: whether it lists download sizes (or, for an S-100 catalogue in an S3 bucket, whether they can be listed)
  - `product` (optional): the one product the catalogue publishes, e.g. `S-102`
  - `notForNavigation` (optional): `true` when the provider marks all its data as not for navigation
  - `models` (for `s100ForecastModels`): each model's `id`, `name` (water body), `cadenceHours` and `horizonHours`, measured from the published runs, and its `catalogue` path when it is not `<id>/CATALOG.XML`
  - `pilot` (optional): `true` for a pilot service, which may cover little and lapse

  These drive the quality chips users see.


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


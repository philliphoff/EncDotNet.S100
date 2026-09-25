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
- **`CollectionJson`** — JSON persistence:
  - collection definitions are written as indented JSON
  - source indexes are written as compact, gzip-compressed JSON

See `docs/design/dataset-collections.md` for the design.

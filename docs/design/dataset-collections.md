# Dataset Collections — Design Note

> Status: **Design** — no production code in this PR. This note is the
> contract the implementation slices (tracked by epic #655) build against. Open questions are collected in §10.

## 0. Scope of this note

Today the viewer treats data as **files to open**. The user finds an
exchange set folder, an exchange-set ZIP, or a loose dataset in a file
explorer, opens it, and the viewer loads it. Loading happens right away,
or for large S-57 exchange sets it is deferred until the user pans over
the area. The only thing the user knows about a file before opening it
is its name and its parent directory. Recent files help only with loose
files, because exchange sets and folders are never recorded.

That works for testing specific scenarios. It is not how people work
with charts. They think in **collections**: "all ENCs for Alaska", "the
NOAA S-102 tiles for the Southeast", "the IC-ENC test set", or "my
inland corpus". They want to find what is available, see where it is
and what it is, and load only what they need.

This note introduces **dataset collections**:

- A collection is a named, persisted grouping of exchange sets and/or
  datasets. It is built from one or more **sources**.
- **Importing** a collection **indexes** metadata about every dataset in
  it: product, name, edition and update, issue date, scale band, and
  coverage. It does **not** load the datasets.
- The viewer gives quick access to the indexed items. There is a Library
  panel, coverage outlines on the map, and load, load-as-you-pan and
  download actions.

The first slices cover two scenarios together, so the design is proven
for both local and online sources:

1. **Local sources** — folders of exchange sets (S-100 `CATALOG.XML` and
   S-57 `CATALOG.031`) and loose datasets, referenced **in place**.
2. **The NOAA ENC online feed** (`ENCProdCat.xml`) — browse coverage
   before downloading, then download individual cells.

The existing **Catalog panel** (S-128 only) is **replaced** by the
Library. S-128 becomes one kind of collection source.

---

## 1. Background

### 1.1 What the viewer does today

| Area | Today | Where |
|---|---|---|
| Open | File menu (Open Dataset, Open Exchange Set folder/ZIP), drag-drop, and CLI positional args. Everything loads right away. | `MainWindow.axaml.cs` (`OpenDatasetAsync`, `RunExchangeSetAsync`, `OnDrop`), `ExchangeSetService.OpenAsync` |
| Exchange set detection | `CATALOG.XML` (S-100), `CATALOG.031` (S-57), and loose-cell folders | `Services/ExchangeSetDetection.cs` |
| Deferred loading | **S-57 only**, and only above 50 cells. Viewport and usage-band gating with LRU eviction. Extents come from the CATD bounding boxes. | `Services/LazyLoading/ExchangeSetLazyLoadCoordinator.cs`, `LazyCellGate.cs`, `ExchangeSetCellRegistration` |
| Recent files | `ViewerSettings.RecentDatasetPaths` (10 entries). Only single files are recorded, never exchange sets or folders. | `Services/RecentFilesService.cs`, `DatasetLoaderService.cs:463` |
| Metadata peek | `IDatasetMetadataReader` → `DatasetMetadata` (spec, extent, CRS, display scale, time), with a disk cache | `Services/IDatasetMetadataReader.cs`, `Core/Metadata/` |
| Catalog panel | `IDatasetCatalogSource` → `DatasetCatalogEntry`, aggregated by `DatasetCatalogAggregator`. Fed only by **loaded S-128 datasets**. Coverage is a single ring. Nothing is drawn on the map. | `Viewer/Catalogs/`, `CatalogPanelViewModel`, `CatalogPanelView.axaml` |
| Extent overlay | Dashed **rectangles** for deferred or out-of-scale datasets | `DatasetExtentIndicatorController`, `S100DatasetExtentIndicatorLayer` |
| Persistence | `settings.json`, plus a separate JSON store for routes. All paths go through `ViewerDataPaths` (`--data-dir` / `S100_DATA_DIR`). | `ViewerSettings`, `Routing/Persistence/RouteStore.cs`, `ViewerDataPaths` |
| HTTP | GitHub release checks and basemap tiles only. There is no catalogue fetching. | `Services/Updates/GitHubReleaseClient.cs` |

Gaps this design must close:

- The S-100 `DataCoverage.BoundingPolygon` is kept as an **unparsed GML
  string** (`ExchangeCatalogueReader.cs:300`).
- S-57 `CATALOG.031` CATD records have **no edition, update or issue
  date**. Those live in each cell's DSID record. The `EncDotNet.S57`
  package exposes them as `EditionNumber`, `UpdateNumber`, `IssueDate`
  and `UpdateApplicationDate`.
- The extent overlay can only draw rectangles, and catalogue coverage is
  never drawn.
- `ExchangeSetService` opens a **whole** exchange set. There is no way to
  open a chosen subset of its datasets.

### 1.2 Prior art: the older EncDotNet viewer

The older S-57 viewer (`EncDotNet/src/EncDotNet.Noaa`,
`EncDotNet.ChartViewer`) fetched `ENCProdCat.xml` and grouped cells by
**US state** into "packages". A setup wizard and a Manage Charts dialog
installed and uninstalled whole states. It diffed the selected states
and correctly kept cells shared between states.

Lessons to carry forward:

- **Keep:**
  - a provider abstraction (`IChartPackageManager`)
  - streamed downloads with progress and cancellation
  - skipping files that are already on disk
  - NOAA long names as titles
- **Fix:**
  - The catalogue was never cached. It was fetched again on every
    action.
  - Coverage polygons were parsed but **never used**. Outlines were
    CATALOG.031 bounding boxes, and only for installed charts.
  - Editions were never compared, so there was no update detection.
  - A partly downloaded zip was later treated as complete.
  - Selection was per state only.
  - Chart counts were inflated for cells listed under several states.
  - Download sizes were computed and then thrown away.

### 1.3 External catalogue formats (verified 2026-09-24)

| Family | Instances | Coverage | Fields of interest |
|---|---|---|---|
| **NOAA product catalogue XML** | `https://charts.noaa.gov/ENCs/ENCProdCat.xml` (≈10.6 MB, regenerated daily, ≈7,345 cells: 7,117 active, 228 cancelled). Per-area bundles `*_ENCs.zip` and `*_ENCProdCat_19115.xml` by state, Coast Guard district and region. Single cells at `ENCs/<CELL>.zip`. | `<cov><panel><type>E\|I</type><vertex><lat/><long/>` — exterior and interior rings | `name`, `lname`, `cscale`, `status`, `states`, `coast_guard_districts`, `regions`, `zipfile_location`, `zipfile_datetime_iso8601`, `zipfile_size`, `edtn`, `updn`, `uadt`, `isdt` |
| **S-100 exchange catalogue** (Part 17, 5.x) | Local `CATALOG.XML`. Also online: NOAA S-102/S-104/S-111 on AWS Open Data (`noaa-s102-pds` etc.), served gzip-compressed with relative `fileName`s. | GML `boundingPolygon` (EPSG:4326, lat-lon axis order) plus `boundingBox` | `fileName`, edition, update, issue date/time, `productSpecification`, `producingAgency`, display scales, `approximateGridResolution`, `temporalExtent` |
| **S-128** Catalogue of Nautical Products | Ed 2.0.0 adopted July 2025 (IHO CL 31/2025). Trial instances from UKHO and PRIMAR. **Already parsed** by `EncDotNet.S100.Datasets.S128`. | `CoverageRing` | product number, edition, update, issue/update date, spec name/version, status |
| USACE inland ENC (variant of the NOAA format) | `ienccloud.us/ienc/products/catalog/IENCU37ProductsCatalog.xml` | bbox only | name, river, river miles, edition, S-57/SHP/KML file links |
| OpenCPN `chart_sources.xml` | A catalogue of ≈97 catalogues | — | A directory of feeds, not datasets. GPL source data: not bundled or fetched (issue #670); the viewer keeps its own curated list |
| Community `chartcatalogs` lists (`RncProductCatalogChartCatalogs`, a subset of NOAA's RNC catalogue) | `raw.githubusercontent.com/chartcatalogs/catalogs/master/<NAME>_Catalog.xml`, CC0 | none | `number`, `title`, `zipfile_location`, `zipfile_datetime_iso8601`, `target_filename`. An entry is a **download** (one cell, several, or a whole exchange set; sometimes a bare `.000`) |
| SECOM (IEC 63173-2) | REST: `GetSummary` / `Get`. Several anonymous S-124/S-122 servers verified 2026-10-06 (§7.5). Chart products need an MCP certificate. | None in the summary. A WKT query filter, and service geometry in the registry | `dataProductType`, `containerType`, `info_*` |

---

## 2. Goals and non-goals

### Goals

1. A user can **import** a collection from:
   - a local folder (scanned recursively)
   - an exchange-set folder or ZIP
   - the NOAA ENC feed, optionally scoped to states, districts or
     regions (for example "NOAA ENC — Alaska")
   - an S-128 catalogue file
2. Importing produces a **persisted index**. It survives restarts and
   can be refreshed on demand. The datasets themselves are **not
   loaded**.
3. The **Library panel** lists collections and their items with
   metadata, and supports filtering by product, usage band,
   availability and text.
4. **Coverage outlines** on the map, drawn from indexed polygons, with
   styling by availability and gating by band and scale.
5. **Load** a chosen item or subset now, or register it for **load as
   you pan**. S-100 as well as S-57.
6. **Download** NOAA cells (one, a selection, or those in view) into a
   viewer-managed folder. Afterwards they behave as local items.
7. Local sources are **references**: files are never copied or moved.

### Non-goals for the first slices

- Copying local sources into a managed library at import time. This may
  become an import option later.
- Update detection and bulk "update all". The data model carries
  edition and update so this can be added later (§9).
- Other online feeds (NOAA S-102/S-104/S-111 on AWS, USACE, the OpenCPN
  catalogue list), SECOM, and S-128 export (§9).
- Automatic re-indexing when files change on disk. Refresh is manual in
  the first slices.
- Decrypting S-63 or S-100 Part 15 protected data at index time. Items
  are indexed from catalogue metadata. Protection is handled at load
  time, as it is today.

---

## 3. Concepts

```text
Collection  ──1..n──▶  Source  ──indexes──▶  SourceIndex  ──0..n──▶  CollectionItem
 (named,                (local folder,        (snapshot +             (one dataset:
  persisted)             exchange set,         fingerprint,            metadata +
                         NOAA feed, S-128)     cached on disk)         coverage + location)
```

- **Collection** — what the user names and manages ("Alaska ENCs"). It
  owns an ordered list of sources.
- **Source** — where the items come from. Each kind has an **indexer**
  that turns it into items. Sources are persisted by *reference*: a path
  or a URL plus options, never the data itself.
- **Source index** — the result of indexing one source. It holds the
  items plus a **fingerprint** used to decide whether a refresh is
  needed. For files that is the catalogue's mtime and size. For HTTP it
  is the ETag or Last-Modified. It is cached separately from
  `collections.json`, because it can be large (the NOAA index is about
  7k polygons).
- **Collection item** — one dataset. It is the neutral successor to
  `DatasetCatalogEntry`.
- **Availability** — computed in the viewer, never persisted:

| State | Meaning |
|---|---|
| **Listed** | Catalogue-only. The source describes the product but has no data for it (S-128 entries). |
| **Online** | A remote location is known. No local copy. |
| **Local** | A local copy exists and is not loaded. |
| **Deferred** | Registered with the lazy loader. It loads when in view and in band. |
| **Loaded** | Currently loaded in the session. |
| **Missing** | Its local path no longer exists (moved or deleted reference). |

The model leaves room for **Outdated** (the remote edition or update is
newer than the local copy) without committing to it now.

---

## 4. Library model (`EncDotNet.S100.Collections`)

A new, **UI-free** project. The viewer, CLI and MCP server can all use
it, for example to answer "which datasets in my library cover this
point?" It references `EncDotNet.S100.Core`, `EncDotNet.S100.ExchangeSets`,
`EncDotNet.S100.Datasets.S57` and `EncDotNet.S100.Datasets.S128`. It
deliberately does **not** reference `Datasets.Pipelines`, the "god
assembly" noted in #598 Chunk C. Loose-file probing is injected (§5.3).

### 4.1 Types (sketch)

```csharp
public sealed record DatasetCollection(
    Guid Id,
    string Name,
    IReadOnlyList<CollectionSource> Sources,
    DateTimeOffset CreatedAt);

// Polymorphic; persisted with a "kind" discriminator.
public abstract record CollectionSource(Guid Id, string? DisplayName);
public sealed record LocalFolderSource(Guid Id, string? DisplayName, string Path, bool Recursive = true)
    : CollectionSource(Id, DisplayName);
public sealed record ExchangeSetSource(Guid Id, string? DisplayName, string Path)   // folder, ZIP, or catalogue file
    : CollectionSource(Id, DisplayName);
public sealed record NoaaEncFeedSource(Guid Id, string? DisplayName, Uri CatalogUri, NoaaEncFilter Filter)
    : CollectionSource(Id, DisplayName);
public sealed record S128CatalogueSource(Guid Id, string? DisplayName, string Path)
    : CollectionSource(Id, DisplayName);
public sealed record LocalManifestSource(Guid Id, string? DisplayName, string Path, LocalManifestFilter Filter)  // *.s100collection.json (§5.6)
    : CollectionSource(Id, DisplayName);

public sealed record NoaaEncFilter(
    IReadOnlyList<string> States,              // "AK"
    IReadOnlyList<int> CoastGuardDistricts,
    IReadOnlyList<int> Regions,
    bool IncludeCancelled = false);            // empty lists = no filter on that axis

public sealed record SourceIndex(
    Guid SourceId,
    DateTimeOffset IndexedAt,
    string Fingerprint,
    IReadOnlyList<CollectionItem> Items,
    IReadOnlyList<IndexDiagnostic> Diagnostics);

public sealed record CollectionItem
{
    public required string Key { get; init; }            // stable within the source (§4.2)
    public required string ProductSpec { get; init; }    // "S-57", "S-101", "S-102", ...
    public string? ProductSpecVersion { get; init; }
    public required string Name { get; init; }           // "US5AK1AM", "102US00_..."
    public string? Title { get; init; }                  // NOAA lname, catalogue description
    public string? GroupKey { get; init; }               // exchange set this item belongs to, if any
    public int? Edition { get; init; }
    public int? Update { get; init; }
    public DateOnly? IssueDate { get; init; }
    public DateOnly? UpdateApplicationDate { get; init; }
    public int? CompilationScale { get; init; }
    public int? MinimumDisplayScale { get; init; }
    public int? MaximumDisplayScale { get; init; }
    public int? UsageBand { get; init; }                 // S-57 name digit / NOAA band
    public CollectionItemStatus Status { get; init; }    // Active, Cancelled, Unknown
    public GeoBounds? Bounds { get; init; }              // EPSG:4326; null = listed but not drawn
    public GeoCoverage? Coverage { get; init; }          // polygons; null = bounds only
    public required ItemLocation Location { get; init; }
    public IReadOnlyDictionary<string, string> Properties { get; init; } // producer, states, size text, ...
}

public abstract record ItemLocation;
public sealed record LocalItemLocation(
    string RootPath,                 // exchange-set root or folder; the IAssetSource root
    string RelativePath,             // base file
    IReadOnlyList<string> UpdateRelativePaths,
    bool RootIsZip) : ItemLocation;
public sealed record RemoteItemLocation(Uri Uri, long? SizeBytes, DateTimeOffset? LastModified) : ItemLocation;
public sealed record NoItemLocation : ItemLocation;      // S-128 "Listed" entries

// EPSG:4326 multi-polygon; each polygon = exterior ring + holes, (lat, lon) GeoPosition vertices.
public sealed record GeoCoverage(IReadOnlyList<GeoPolygon> Polygons);
public sealed record GeoPolygon(IReadOnlyList<GeoPosition> Exterior, IReadOnlyList<IReadOnlyList<GeoPosition>> Holes);
```

Notes:

- `Bounds` is present for every catalogued item, so hit-testing,
  culling and lazy-load gating never depend on polygons. It is `null`
  only for a loose file whose extent cannot be read cheaply, for example
  an unprobed file or a projected S-102 grid. Such an item is listed but
  not drawn until it is loaded. (This was changed from "always present"
  during slice 1.) `Coverage` is optional because some sources have
  bounding boxes only (CATALOG.031 without a deeper scan, USACE).
- Edition and update are **typed as integers**. S-128 carries them as
  text, and those values that don't parse go into `Properties`. This
  makes update comparison (§9) straightforward.
- `GeoCoverage` replaces the viewer's single-ring
  `DatasetCatalogCoverage`.
- Antimeridian-crossing coverage matters for Alaska and the Aleutians.
  Rings are stored **as published**. The overlay normalizes them for
  display (§6.2), and `Bounds` records a crossing as `West > East`, the
  same convention as `BoundingBox`.

### 4.2 Item identity

`(collectionId, sourceId, item.Key)` identifies an item. `Key` rules:

- **S-57**: cell name (`US5AK1AM`).
- **S-100 exchange set**: the exchange-set root's relative path plus the
  dataset's catalogue `fileName`.
- **Loose file**: the path relative to the folder source.
- **NOAA**: cell name.
- **S-128**: product number.
- **Collection manifest**: `<group id>:<manifest-relative path>/` plus the
  key the item would have under a folder source at that path (§5.6).

The **same physical dataset** can appear in several collections. The
viewer loads it **once**, keyed by its resolved absolute path (and the
member path for ZIPs), and every item that resolves to that path shows
as Loaded.

### 4.3 Indexers

```csharp
public interface ICollectionSourceIndexer
{
    bool CanIndex(CollectionSource source);

    /// Cheap check: returns the current fingerprint, or null if it cannot be determined.
    ValueTask<string?> GetFingerprintAsync(CollectionSource source, CancellationToken ct);

    ValueTask<SourceIndex> IndexAsync(
        CollectionSource source, IProgress<IndexProgress>? progress, CancellationToken ct);
}
```

A `CollectionIndexer` facade picks the indexer and does the fingerprint
short-circuit: if the fingerprint is unchanged, the cached index is
reused.

---

## 5. Indexing

### 5.1 Local folder walk (`LocalFolderSource`)

1. Walk the tree **once**. A directory that contains `CATALOG.XML` (or
   the alternate spellings in `ExchangeSetDetection`) or `CATALOG.031`
   is an **exchange-set root**. It is indexed by §5.2 and the walk does
   **not** descend into it to look for loose files.
2. A `.zip` that contains a catalogue is indexed as an exchange set
   with `RootIsZip`.
3. Any other candidate file becomes a loose dataset, indexed by §5.3.
   Candidates are `.000`, `.h5` and `.gml`. `.xml` counts only when it
   is not a catalogue or support file.
4. S-57 update files (`.001` and up) outside an exchange set are
   attached to their base cell as `UpdateRelativePaths`. This is the
   same grouping the loose-cell-folder path already does
   (`ExchangeSetDetection.EnumerateLooseBaseCells`).

Slice 1 implements the walk in `LocalSourceScanner` and leaves the
viewer's `ExchangeSetDetection` untouched. Consolidating the two is part
of slice 3 (§10 Q4).

### 5.2 Exchange sets

- **S-100 `CATALOG.XML`**: `ExchangeCatalogueReader` with
  `ExchangeCatalogueReadOptions.DiscoveryOnly`. That option skips digital
  signatures and certificates. Their strict Part 15 validation otherwise
  fails the whole catalogue, and about 120 IC-ENC sets in the local
  corpus have malformed signature blocks. There is one item per
  `DatasetDiscoveryMetadata`. Edition and update come from the
  catalogue.
  - **New:** parse `DataCoverage.BoundingPolygon` GML
    (`gml:Polygon` / `gml:exterior` / `gml:posList`, EPSG:4326
    **lat-lon** axis order) into `GeoCoverage`. Fall back to
    `BoundingBox`.
  - Display scales come from the existing `ResolveMinimum/MaximumDisplayScale()`.
  - Updates are grouped per base using the same rules as
    `S101ExchangeSetUpdatePlan`.
- **S-57 `CATALOG.031`**: `S57ExchangeSetCatalog.ReadBaseCells`. It
  gives the cell name, relative path, updates and the CATD bounding box.
  - **New:** `S57DatasetHeader` (in `Datasets.S57`) reads `EDTN`,
    `UPDN`, `ISDT`, `UADT` and the compilation scale for each base cell
    and its latest update. It copies only the DDR, DSID and DSPM records
    (a few KB, sized from each record's leader) and hands them to the
    upstream parser, so no upstream `EncDotNet.S57` change was needed.
  - The peek is not cached per cell. The source-level fingerprint
    already skips re-indexing an unchanged source. Measured on 7.3k S-57
    cells plus about 1k S-100 datasets: a full index takes 3.3 s, and an
    unchanged refresh check takes about 1 s.
  - Coverage is the CATD **bounding box** at first. **M_COVR polygons**
    are an optional "deep index" later, because they require parsing the
    cell.
  - The usage band comes from `CellUsageBand.TryParse`.

### 5.3 Loose datasets

The library defines a probe:

```csharp
// Returns Core's DatasetMetadata (spec, extent, CRS, display scale, time); null = not a dataset.
public delegate DatasetMetadata? DatasetProbe(string path, CancellationToken cancellationToken);
```

The viewer supplies a probe backed by `DatasetPipelineFactory.DetectProductSpec`
plus its existing `CachingDatasetMetadataReader`. That reuses the
per-spec `ReadMetadata` code and its disk cache without dragging
Pipelines into the library.

### 5.4 NOAA ENC feed (`NoaaEncFeedSource`)

- **Fetch** with a conditional GET (`If-None-Match` /
  `If-Modified-Since`). Cache the raw XML under
  `<data>/cache/collections/feeds/`. The fingerprint is the
  ETag/Last-Modified pair.
  - A 304 response, or being offline, reuses the cached file.
  - Never fetch on every action. That was a lesson from §1.2.
- **Parse** with a streaming `XmlReader`. The file is ≈10 MB, so there
  is no `XmlSerializer` tree. Map each `<cell>` to an item:
  - `name` → Name/Key
  - `lname` → Title
  - `cscale` → CompilationScale
  - `edtn` / `updn` / `isdt` / `uadt`
  - `status` → Status
  - usage band from the name
  - `states` / `coast_guard_districts` / `regions` / `zipfile_size` →
    `Properties`
  - `<cov><panel>` rings → `GeoCoverage`. Type `E` is an exterior ring
    and `I` is a hole.
  - `zipfile_location` / `zipfile_size` / `zipfile_datetime_iso8601` →
    `RemoteItemLocation`
- **Filter** by `NoaaEncFilter` at index time. The unfiltered raw feed
  is cached once and shared by every NOAA source.
- **Local copies**: a cell downloaded for this source (§7.3) lives at
  `<data>/downloads/noaa-enc/<CELL>/`. Its location is resolved at
  **runtime**, so the persisted index stays remote-only: if a local copy
  exists, the item is Local, otherwise Online. No separate index is
  needed for downloads.
- **Longitudes (found in slice 2):** NOAA writes western-Pacific
  coverage with **continuous longitudes past −180**, for example −219.5
  for 140.5°E. Grid cells end exactly on −180. Rings are kept as
  published. `GeoBounds.FromPositions` normalises them by wrapping the
  west edge and carrying the span, so only cells that genuinely straddle
  ±180 are recorded as crossing: two of Alaska's 1,193.
- **Filter semantics:** the selected states, districts and regions are
  **unioned**. Cancelled cells are excluded unless `IncludeCancelled` is
  set.
- **Revalidation:** a cached catalogue confirmed within
  `FeedCacheOptions.RevalidationInterval` (15 minutes by default) is used
  without any request. After that, a conditional GET is sent. When the
  server cannot be reached, the cached copy is served with a warning, and
  its unchanged fingerprint keeps an existing index. Measured on the live
  feed: 7,117 cells indexed in 0.6 s, and a 304 revalidation in 10 ms.
- To pick presets, the import dialog reads the distinct `states`,
  `coast_guard_districts` and `regions` values (with counts) from the
  feed after the first fetch, so nothing is hardcoded.

### 5.5 S-128 catalogue file (`S128CatalogueSource`)

`S128Dataset.Open` → `S128ProductEntry` → item:

- Edition and update are parsed to integers, and the text is kept.
- `CoverageRing` → `GeoCoverage`.
- `Status` is mapped from `S128ProductStatus`.
- Location is `NoItemLocation` unless the entry carries an online
  resource, in which case it is `RemoteItemLocation`.
- The details that `S128DatasetCatalogSource` maps today move here
  unchanged.

### 5.6 Collection manifest (`LocalManifestSource`)

Added 2026-09-30. A `*.s100collection.json` file names groups of local
paths, such as one per producing country (see
[Local collection manifests](../local-collection-manifest.md)). It is the
first local source with facets.

- **Indexing.** `LocalManifestIndexer` re-reads the manifest on every
  refresh, so the source is a live reference. It then indexes each path of
  each selected group through `LocalSourceIndexer.IndexPath`, which is the
  same code a folder source uses.
- **Item keys and tags.** Keys are prefixed with the group id and the path,
  so a folder listed by two groups gives two distinct items. Items carry
  `group` and `groupName` properties.
- **Groups on the index.** `SourceIndex.Groups` records the selected groups
  and how many of their paths were missing. This lets the viewer show a
  group with no datasets.
- **Fingerprint.** It hashes the manifest's content (not its timestamp,
  because sync clients touch timestamps), the selection, and the scan
  fingerprint of each selected path.
- **Errors.** An unreadable manifest gives an error diagnostic and no
  fingerprint, and never an exception. A missing path gives a warning.

---

## 6. Viewer

### 6.1 Persistence

- `<data>/collections.json` is **authoritative, small, and edited by the
  user**. It holds the collections and their sources. It is modelled on
  `RouteStore`: versioned document, debounced atomic save, and
  migration hooks.
- `<data>/cache/collections/<sourceId>.index.json.gz` holds the
  **derived** cache, one per source. It is safe to delete, and
  re-indexing rebuilds it. It is registered in `ViewerDataPaths`' cache
  list so "clear caches" covers it.
- `<data>/downloads/noaa-enc/` holds managed downloads. It is **not** a
  cache, because users expect downloaded charts to survive "clear
  caches".

```jsonc
// collections.json
{
  "version": 1,
  "collections": [
    {
      "id": "7c1e…", "name": "Alaska ENCs", "createdAt": "2026-09-24T…",
      "sources": [
        { "kind": "noaaEncFeed", "id": "a31f…", "catalogUri": "https://charts.noaa.gov/ENCs/ENCProdCat.xml",
          "filter": { "states": ["AK"], "coastGuardDistricts": [], "regions": [], "includeCancelled": false } },
        { "kind": "localFolder", "id": "b90c…", "path": "/Users/…/Charts/AK", "recursive": true }
      ]
    }
  ]
}
```

### 6.2 Services

- **`CollectionService`** (singleton):
  - Loads the store.
  - Owns the indexer and runs indexing in the background, with
    progress in the existing status/toast surface and cancellation.
  - Exposes an immutable snapshot of `(collection → items)` plus a
    coarse `Changed` event, the same pattern as `IDatasetCatalogSource`.
  - Computes **availability** by joining items with the loaded-dataset
    registry (`DatasetsViewModel` entries keyed by resolved path), the
    lazy-load coordinator, the download folder, and `File.Exists`.
- **`CollectionLoadService`** turns items into loads (§7).
- **`CollectionCoverageOverlay`** replaces the need to extend
  `DatasetExtentIndicatorController`:
  - It is a Mapsui `MemoryLayer` of **polygons**, added through
    `IMapLayerCollection.AddOverlayLayer`.
  - Web Mercator geometry is precomputed once per index snapshot.
  - Features are **gated by band and scale**, reusing `LazyCellGate`'s
    band-for-scale rule. Only items appropriate to the current zoom are
    drawn, so the ≈7k NOAA outlines don't flood the view at small
    scales.
  - Styling by availability: Online = dashed and hollow, Local = solid
    outline, Deferred = outline plus a light tint, Loaded = no outline by
    default (the data speaks for itself), Missing = red dashed.
  - Hover and selection highlight. A map click hit-tests `Bounds`
    first, then the polygon, and **selects the item in the Library
    panel**. The reverse also works: selecting in the panel highlights
    the outline and offers "Zoom to".
  - Antimeridian: rings that cross ±180° are split or shifted, the same
    way `DatasetExtentIndicatorController` does for boxes.
- **Context menu on the map**: "Datasets in library here…" lists the
  items whose coverage contains the clicked point, across all
  collections.

> **Slice 4 as built:**
> - **What is drawn:** `LibraryCoverageOverlayController` outlines
>   exactly what the Library list shows, after its filters. It is active
>   only while the Library tab is showing, and the panel has a coverage
>   toggle.
> - **Band gating:** a plain `IsBandEligible` rule (the lazy-load rule)
>   was too sparse for browsing: at an Alaska-wide view only band 1
>   showed. Outlines use a **two-band window** instead: the finest band
>   suited to the scale plus the next finer one.
> - **Antimeridian:** rings are unwrapped to continuous longitudes, then
>   drawn at each ±360° shift that overlaps the world. That handles both
>   NOAA's continuous longitudes and S-100's jumping rings without
>   splitting polygons.
> - **Map clicks:** instead of a context menu, a plain map tap outside
>   Pick Mode (`MapInteractionController.PlainTapped`) sets a
>   **location filter** on the panel. The list shows every library
>   dataset under the tap, most detailed first, and repeated taps cycle
>   the selection.
> - **Zoom to:** "Zoom to" frames the selected dataset.
> - **Not built:** hover highlighting.

### 6.3 Library panel (replaces the Catalog panel)

> **UX refinement (2026-09-28):** a design pass reworked the panel. Now:
> - **Vocabulary:** "dataset" everywhere; "package" for community entries;
>   sentence-case states.
> - **Rows:** a swatch drawn like the map outline (`LibraryOutlineStyles`,
>   shared with the overlay, which now outlines by the primary state), plus
>   secondary-state tags.
> - **Tree:** kind tags (DIR / ZIP / WEB / LIST / FEED / S-128) and a status
>   line only when off-normal. A shared feed's reachability comes from
>   `FeedHealth`.
> - **Filter:** the count sits inside the filter box, with Local / Online /
>   Updates segments.
> - **Bulk bar:** one bar with download progress and Cancel, backed by
>   per-item and batch status on `ILibraryDownloader`.
> - **Map-tap banner:** a count, the position and Next.
> - **Details:** grouped fields with labelled actions.
> - **Packages:** community packages become "Unpacked" groups after
>   download.
> - **Shared feeds:** "Connect to a shared feed…", named after the serving
>   machine (`machine` in the feed JSON), with the token masked.
> - **Empty state:** three large targets.

> **Slice 3 as built:**
> - The service is `LibraryService`, not `CollectionService`. It keeps
>   immutable snapshots, a single background indexing queue, and a
>   per-source gzipped index cache.
> - The tree shows collections and their sources. The dataset list is a
>   virtualised `ListBox` with text and "show cancelled" filters, and
>   availability is resolved lazily per row.
> - The Catalog panel's view model, view and `Viewer/Catalogs` types are
>   deleted. `MainViewModel` no longer takes a catalogue panel.
> - The splitter keeps the persisted `CatalogInnerSplit` setting name so
>   users' layouts carry over.
> - Drag-drop: a folder that is not openable goes to the Add to Library
>   dialog. An opened exchange set gets an "Add to Library" notification
>   action unless the library already covers it.
> - Viewer exchange-set detection now shares
>   `EncDotNet.S100.Collections.ExchangeSetLayout` with indexing. This
>   answers §10 Q4: the shared rules live in Collections.
> - Loaded/Deferred availability arrives with the load actions in
>   slice 5.

- The **"Library" activity tab** takes over the Catalog tab's slot.
  `CatalogPanelViewModel`, `CatalogEntryViewModel`, `CatalogPanelView`,
  `DatasetCatalogAggregator` and `S128DatasetCatalogSource` are
  retired. The details pane (key/value properties) is carried over.
- **Tree:** Collection → Source → (exchange set group) → item. There is
  a flat mode for large feeds.
  - Each row shows name, title, product, band, edition and update,
    issue date, and an availability glyph.
- **Filters:** text, product, usage band, availability, "in current
  view", and "show cancelled".
- **Commands:**

  | Target | Commands |
  |---|---|
  | Item or selection | Load · Load as you pan · Download · Zoom to · Reveal in Finder/Explorer · Copy path/URL |
  | Collection or source | Refresh index · Rename · Add source · Remove (the reference only; never deletes data) · Show/hide coverage |

- **Collection manifests (2026-09-30):**
  - A manifest source with two or more groups gets a third tree level, one
    node per group.
  - Selecting a group node lists only its datasets.
  - A missing group shows "Path not found".
  - Group nodes can't be renamed or removed. **Choose groups…** on the
    source (`LibraryService.UpdateSource`) changes the selection in place.
- **Loaded S-128 datasets:** loading an S-128 dataset still renders it
  as a dataset. Its entries also appear under a **transient "Session"
  group** in the Library, so today's behaviour is kept, and a
  **"Keep in library"** action persists it as an `S128CatalogueSource`.

### 6.4 Import and entry points

- **File > Add to Library…** with submenu Folder… · Exchange Set /
  ZIP… · NOAA ENC Feed… · S-128 Catalogue…. A "+" button in the Library
  panel offers the same choices.
  - Each prompts for a collection: a new one (the name defaults to the
    folder or preset name) or an existing one.
- **NOAA ENC Feed dialog:**
  1. Fetch or refresh the feed.
  2. Pick states, districts or regions, with counts and total download
     size. Counts are de-duplicated across the selection.
  3. Enter a name.
  4. Create.

  Nothing is downloaded at this point.
- **Collection manifests** are added through **Collection manifest…**, or
  by dropping one, into a one-page dialog. The dialog reuses the online
  wizard's scope and target views.
- **Drag-drop** of a folder or ZIP asks "Open now" / "Add to library" /
  "Both". A remembered preference is fine.
- **Recents:** exchange sets and folders are recorded in Open Recent as
  well. This is a small, independent fix.
- The **CLI and MCP** reach collections later (§9). Keeping the library
  UI-free keeps that cheap.

---

## 7. Loading and downloading

### 7.1 Load now

`CollectionLoadService.LoadAsync(items)` groups items by `RootPath`, so
each exchange set or folder gets one `IAssetSource`:

- **S-57** → `ExchangeSetCellRegistration` (`IAssetSource` over the root,
  relative path, updates, bounds) → `DatasetsViewModel.AddRangeFromExchangeSet`,
  then load.
- **S-100 exchange-set members** → the existing `AddFromExchangeSet`
  path, fed the chosen `DatasetDiscoveryMetadata` only.
- **Loose** → `DatasetsViewModel.LoadFromPathAsync`.

> **Slice 5 as built:**
> - **Subset open:** instead of splitting `OpenAsync`, a separate
>   `IExchangeSetService.OpenSubsetAsync(request, defer)` opens a subset.
>   It registers the chosen items under one tracked set per root, reusing
>   a set already opened from the File menu along with its header and
>   entries.
> - **Unreadable catalogues:** when a catalogue cannot be read strictly
>   (malformed signatures), the set falls back to the bare source,
>   without verification.
> - **`LibraryLoadService`:** groups library items by root and
>   catalogue, skips non-local, missing and unknown-spec items, and maps
>   items to their entries so the panel can show LOADED and ON PAN.
> - **Lazy gating:** `LazyCellGate.IsScaleEligible` gates items without
>   a band by their coarsest display scale. `DatasetEntry.UsageBand` is
>   now parsed only for S-57 names, because S-101 names parsed as band 1.
> - **Pre-existing bug fixed:** a batch registration's collection Reset
>   released a still-empty tracked set. That dropped the header and
>   disposed the source of deferred S-57 exchange sets opened from the
>   File menu. It is fixed with a `TrackedExchangeSet.IsRegistering`
>   guard and covered by a regression test.
> - **Entries loaded now:** entries loaded immediately are taken out of
>   the lazy loader, with `IsDeferred` cleared.

**Prerequisite refactor:** split `ExchangeSetService.OpenAsync` into
"read catalogue" and "register these entries (with their shared header,
protection and portrayal set-up)". Collections can then open a
**subset** of an exchange set. Opening a whole exchange set from the
File menu becomes "all entries" through the same path.

### 7.2 Load as you pan

"Load as you pan" on a collection, source or selection registers the
items as **deferred** entries with `ExchangeSetLazyLoadCoordinator`.
This is the same mechanism as large S-57 sets, generalized:

- **The gate** uses `UsageBand` when it is present (S-57). Otherwise it
  uses the item's `Minimum/MaximumDisplayScale` (S-100), so
  `LazyCellGate.ShouldBeLoaded` is no longer S-57-only.
- **The threshold** (`CellThreshold = 50`) stays a heuristic for opening
  from the File menu only. An explicit "Load as you pan" always defers.

### 7.3 Download (NOAA)

> **Slice 6 as built:**
> - **Downloader:** `EncDotNet.S100.Collections.Noaa.NoaaEncCellDownloader`
>   fetches the cell zip to a `.partial` file, extracts it into a
>   staging folder, and swaps that in for any previous copy only once it
>   is complete.
> - **Layout discovery:** it finds `CATALOG.031` and `<CELL>.000` plus
>   its updates rather than assuming the layout.
> - **Record:** it writes `.source.json` with the edition, update and
>   layout relative to the cell folder.
> - **In the viewer:** `LibraryDownloadService` downloads at most three
>   at a time, with a cancellable progress notification, and localizes
>   downloaded feed items: the item keeps its identity but gets a local
>   location.
> - **Update detection:** basic detection landed early. A copy older
>   than the feed shows **UPDATE**, and downloading again replaces it.
>   "Update all" is simply "Download N" on a list that includes outdated
>   cells.
> - **Live check:** real NOAA cells (with and without updates, including
>   a western-Pacific cell) downloaded and re-indexed with matching
>   edition and update.

- Download `zipfile_location` to
  `<data>/downloads/noaa-enc/<CELL>.zip.partial`, streaming with
  progress and cancellation. Then **extract into a temporary directory
  and rename atomically** to `<CELL>/`. A partial download can never be
  mistaken for a complete one.
- Bounded concurrency (2–4). The panel shows **total size** before bulk
  downloads.
- Record `edtn`, `updn` and `zipfile_datetime` in `<CELL>/.source.json`.
  That is the local-edition record that later update detection uses
  (§9).
- Once downloaded, the item is **Local**. Download-and-load is one
  action.
- The cell zip contains `ENC_ROOT/<CELL>/<CELL>.000` plus updates. The
  `LocalItemLocation` root is `<CELL>/ENC_ROOT` and the base is
  discovered the way loose-cell folders are.

### 7.4 Community lists and packages (#670)

> **As built:**
> - **Reader and source:** `ChartCatalogsProductCatalogReader` reads
>   `RncProductCatalogChartCatalogs` lists. A `ChartCatalogsFeedSource`
>   (JSON kind `chartCatalogsFeed`) is scoped by a `ChartCatalogsFilter`
>   over entry numbers. Repeated entries are listed once.
> - **Packages:** an entry is a download that may hold many cells, so
>   its `RemoteItemLocation` carries a `DownloadFolder`
>   (`community/<list stem>`) and a `Package` (the entry number, made
>   file-system safe).
> - **Downloading a package:** `EncCellDownloader` saves the whole
>   package under `<folder>/<package>/` and records every `.000` it
>   holds, each with its own exchange-set root and updates, in
>   `.source.json`. A download that is not a zip is kept only when its
>   URL names a `.000`.
> - **Indexing (slice 4, bounds after download):** an entry that has not
>   been downloaded is one online item without bounds. Once downloaded,
>   `ChartCatalogsFeedIndexer` indexes the package folder with the local
>   indexer and lists its cells. They carry their own bounds, editions
>   and titles, and keep the package's remote location with
>   `packageTitle`.
> - **Fingerprint:** it includes each selected package's record time, so
>   a download re-indexes the source. The Library panel also queues that
>   re-index as soon as a package download finishes.
> - **UPDATE:** packages have no edition, so a copy is outdated when the
>   list's `zipfile_datetime` is later than the recorded publication
>   date.
> - **Excluded lists:** CZ (empty), DE (HTTP 403) and the South China Sea
>   list (update-only zips with no base cells).
> - **Live check:** RO Base1 expanded to 10 cells with bounds. Other
>   checks passed on a Rhône bare `.000`, a EuRIS single-cell zip, and a
>   Brazilian exchange set. The 1,385-entry EuRIS list indexes in
>   ≈0.3 s.

### 7.5 SECOM (IEC 63173-2) feasibility (2026-10-06)

> **As built (#804, first slice):**
> - **Client.** `SecomClient` in `EncDotNet.S100.Collections.Secom` calls
>   `Capability`, `GetSummary` (paged; capped, with `SecomSummaryList.Truncated`
>   set only when the cap is hit) and `Get`. It falls back from `/v2` to `/v1`
>   and pins whichever answered. The parser (`SecomJson`) is tolerant of the
>   drift listed under Gotchas below.
> - **Source.** `SecomSource` (JSON kind `secom`) with a `SecomFilter`
>   (products matched on this side, plus an optional WKT area sent as
>   `geometry`). `SecomSourceIndexer`:
>   - lists each object as an online item whose `RemoteItemLocation` points
>     at `Get` with `Envelope = Secom` and a single-file `Layout`
>     (`<identifier>.gml`, `.000` or `.h5`);
>   - caps a source at 5,000 objects;
>   - keeps the last list in memory (one minute) and on disk (served stale
>     when the service is down);
>   - probes downloaded copies for bounds and records their signature in
>     the item's properties;
>   - `DescribeAsync` counts objects per product for the Add to Library
>     dialog.
> - **Download.** `EncCellDownloader` unwraps `Get` responses. It refuses
>   encrypted objects and signatures that do not match. It decompresses
>   single-file ZIP payloads, and records the `SecomSignatureCheck` in
>   `.source.json`.
> - **Signatures.** `SecomSignatureVerifier` checks the hex signature, DER
>   or raw r‖s, over the decoded data with the first `publicCertificate`
>   (base64 DER or minified PEM). Signer trust is optional (supplied roots,
>   root thumbprint matched). It is kept in Collections rather than shared
>   with `ExchangeSetVerifier`: the overlap is a few BCL calls, and the
>   verifier's helpers are private and Part 15-specific (P-384 only).
>   SHA3 algorithms report `Unsupported` where the platform lacks them
>   (macOS). Baleen and ELMAN sign with SHA3-384; CCG and AIVN with
>   SHA2-384.
> - **Discovery.** A URL that answers `Capability` is recognised as
>   `KnownCatalogueFormat.Secom`, both in the directory's "Add a catalogue by
>   URL" and in MCP `add_library_source`. The known-sources list carries
>   the Canadian Coast Guard's S-124 service (`ccg-s124-secom`). The
>   registry search (MSR) is not wired in yet.
> - **Live check (2026-10-06).** All four anonymous services listed and
>   downloaded:
>   - CCG: 1,735 S-124 objects, signatures valid. The signer certificate
>     expired on 2026-05-05 and is reported as expired.
>   - AIVN: 67,640 S-122 objects, so capped. Signatures valid.
>   - Baleen and ELMAN: signatures SHA3-384, not checked on macOS.

**Spec.** Ed 1.0 (2022) is the only published edition. Ed 2 has been
out as a draft for comment (BSI, February 2025 and February 2026) but
is not yet a standard. GLA's SECOMLib (Java, Apache-2.0) is the
reference implementation. Its `master` branch implements v2 only; v1 is
on the `v1` branch. There is no .NET, Python or published TypeScript
library. The IEC text is paywalled, so cite section numbers only.

**Interfaces a read-only client needs.** Paths are relative to the
service's registered endpoint URI. `/api/secom` is a convention, not a
rule.

| Interface | Method and path |
|---|---|
| GetSummary | `GET /v2/object/summary` |
| Get | `GET /v2/object?dataReference=` |
| Capability | `GET /v2/capability` |
| Ping | `GET /v2/ping` |

v1 has the same shapes under `/v1`. The query filters are
`containerType`, `dataProductType`, `productVersion`, `geometry` (WKT),
`unlocode`, `validFrom`/`validTo`, `page` and `pageSize`. Summary
objects carry `dataReference`, `dataProtection`, `dataCompression`,
`containerType`, `dataProductType` and `info_*` (identifier, name,
status, description, last-modified date, product version, size). They
carry **no geometry**.

**Discovery.** The MCP Maritime Service Registry has an anonymous search:
`POST https://msr.maritimeconnectivity.net/api/secom/v1/searchService`
with body `{}`. It returned 79 instances. Data quality is poor
(localhost URIs, bare hostnames, longitudes such as −414°), so results
need sanity filtering. The v2 search expects a signed envelope.

**Reachable without a client certificate** (probed 2026-10-06):

| Service | Products | Notes |
|---|---|---|
| Canadian Coast Guard, `https://s124.ccg-gcc.gc.ca/api/secom` | S-124 | v1 and v2 Get/GetSummary. ECDSA-384 signed. The best test target. |
| Danish Maritime Authority Baleen (test), `https://baleen-test.e-navigation.net/api/secom` | S-124 | v2 only |
| ELMAN, `https://pelagus.elmansrl.eu/s100exchanger/secom` | S-122, S-124 | v2 only, about 200 items |
| AIVN, `https://mpa.aivenautics.co.kr/api/secom` | S-122 | v1 and v2 |
| AMSA, Fintraffic, KHRA (S-101, S-124, S-128) | — | Capability and Ping answer anonymously. A GET summary returns 404, probably because they need a signed POST search or an access request. |

UKHO, NOAA and PRIMAR do not serve S-100 over SECOM. **No S-101, S-102,
S-104 or S-111 is openly downloadable over SECOM today.**

**Security.**
- **Mutual TLS.** It uses Maritime Connectivity Platform (MCP) PKI
  certificates: secp384r1, SHA384withECDSA, with the MRN and ship
  attributes in SAN otherName OIDs. None of the anonymous servers
  above asked for a client certificate. Some servers present
  MCP-issued server certificates; the MCP root is not in OS trust
  stores.
- **Payload signatures.** The signature in
  `exchangeMetadata.digitalSignatureValue` is computed over the
  decoded `data` bytes and is hex-encoded. The signer certificate
  travels as single-line PEM, along with the root thumbprint.
- **Request envelopes (v2 POST search, subscriptions, uploads).**
  These are signed over a canonical string: field values joined with
  `.`, instants as epoch seconds, bytes as base64.
- **Encryption.** The optional encryption is AES-CBC with a session
  key exchanged through the EncryptionKey interface. Part 15
  protection can still apply to the dataset inside.
- **Developer certificates.** Free from the MCC test environment, but
  approval is manual.

**Gotchas.**
- **`data` is base64 inside JSON.** A download is a JSON call, not a
  file URL, so `EncCellDownloader` cannot be reused as-is.
- **Servers drift between v1 and v2.**
  - Product names: `S124` vs `S-124`.
  - The summary list: `informationSummaryObject` vs `summaryObject`.
  - `pagination` may be absent.
  - Ping fields differ, and timestamps come compact or ISO.
  - The parser must be tolerant.
- **No bounds before download.** Items have no bounds until they are
  downloaded. Use the same "bounds after download" pattern as §7.4,
  optionally with the service's registry geometry as an outline.
- **`FeedCache` does not fit.** It is GET and URI-keyed only.

**Plan.**
1. **#804: a read-only source.**
   - A tolerant v2/v1 client with an injectable handler.
   - A `SecomSource` and indexer.
   - A downloader that decodes payloads.
   - Signature checks that reuse the ECDSA and certificate-chain code
     pulled out of `ExchangeSetVerifier`.
   - An Add to Library entry, and `add_library_source` support.
2. **Later: an identity and a "keys & certificates" store.** An MCP
   client certificate, mutual TLS, signed v2 POST search, access
   requests and encryption. Design the store with the Part 15
   permit/key UX as one shared surface. This is what unlocks the chart
   products (KHRA S-101/S-128, KRISO S-102).

---

## 8. Implementation slices

Each slice is a PR. Slices 1–2 are library-only and testable without
the UI.

| # | Slice | Contents | Depends on |
|---|---|---|---|
| 1 | **Collections core + local indexers** | New `EncDotNet.S100.Collections` project: model, JSON (de)serialization, `ICollectionSourceIndexer`, folder walk, S-100 `CATALOG.XML` indexer (with **GML polygon parsing**), S-57 `CATALOG.031` + **DSID peek** indexer, loose-file indexer (probe), S-128 file indexer. Tests over the committed samples. | — (**done**: header read added in `Datasets.S57`) |
| 2 | **NOAA ENC feed indexer** | Conditional-GET fetcher and raw cache (injectable `HttpMessageHandler`), streaming parser, filter, facets (state/district/region counts). Tests over a trimmed committed fixture. No network in tests. | 1 |
| 3 | **Viewer: store, service, Library panel** | `collections.json` store, `CollectionService` (background indexing, availability), Library panel **replacing the Catalog panel** (S-128 session group + "Keep in library"), Add to Library commands and NOAA feed dialog, drag-drop prompt. | 1, 2 |
| 4 | **Coverage overlay** | Polygon overlay, band/scale gating, availability styling, antimeridian handling, map ↔ panel selection sync, "Datasets in library here". | 3 |
| 5 | **Load and load-as-you-pan** | `ExchangeSetService` subset split, `CollectionLoadService`, lazy-gate generalization to S-100 display scales. | 3 |
| 6 | **NOAA download** | Managed download folder, atomic extract, progress, cancellation and size, download-and-load. | 3, 5 |
| 7 | **Small independents** | Record exchange sets and folders in Open Recent. Viewer README and docs update. | — |

Visual validation for slices 4–6 uses the MCP-driven viewer checks
against the local corpora (IC-ENC, UKHO and USACE under the standard
dataset root) and a NOAA "Alaska" collection. Alaska exercises the
antimeridian and a large item count.

---

## 9. Later (explicitly deferred)

- **Update detection**: compare a Local item's recorded edition and
  update (DSID or `.source.json`) with the feed. Show Outdated. Provide
  "Update" and "Update all". Consider NOAA's `OneWeek_ENCs.zip` style
  change bundles.
- **More feeds**:
  - NOAA S-102/S-104/S-111 on AWS. These are remote S-100
    `CATALOG.XML`s: gzip, relative `fileName`s, and per-model-run
    overwrites for S-111.
  - USACE IENC catalogues (bbox only). **Done** (#670 slice 1).
  - A curated directory of known catalogues. **Done** (#670 slice 2),
    as our own `known-sources.json`, not OpenCPN's GPL `chart_sources.xml`.
  - Community `chartcatalogs` lists. **Done** (#670 slices 3–4, §7.4).
  - User-added catalogue URLs. **Done** (#670 slice 5):
    - `CatalogueFormatDetector` reads only the head of the document
      (gzip-aware) and recognises the format by its root element.
    - `S100_ExchangeCatalogue` is recognised but reported as not yet
      supported.
    - The viewer keeps the user's catalogues in `catalogues.json`, a
      known-sources document written by `KnownCatalogueSources.Write`,
      and lists them under "Custom" in the directory.
- **S-128 as the interchange format**: export a collection as an S-128
  catalogue, and import one as a collection.
- **SECOM** source kind: the read-only first slice is #804 (§7.5).
  Certificates, signing and mutual TLS stay deferred.
- **Copy on import** as an option.
- **File watching** and automatic re-indexing for local sources.
- **M_COVR deep index** for S-57 polygons.
- **CLI and MCP**: tracked by #792, which shares the viewer's Library MCP
  tools (`list_library_sources`, `query_library_items`, `add_library_source`,
  `library_action`, …) with the `s100 mcp serve` host rather than adding
  new tool names. *As built (#792 chunk 1):* the library runtime moved
  from the viewer into `EncDotNet.S100.Collections.Library`:
  `CollectionLibrary` (was the viewer's `LibraryService`), the item states
  (`LibraryItemState`, `LibraryAvailability`), forecast-run facts and the
  item query (`LibraryQuery`, `CoverageHitTest`). It stays in this project,
  not a new assembly: the runtime needs only the model, indexers and
  persistence already here. Opening items into a dataset catalog, which
  needs `Datasets.Pipelines`, comes later behind a host interface.
  *As built (#792 chunk 2):* load and download are host-neutral too.
  - `LibraryDownloads` (was the viewer's `LibraryDownloadService`) reports
    progress through plain records. `LibraryDownloads.ManagedFolders` holds the
    download folder layout.
  - `LibraryLoader` groups items by exchange set and opens each group through
    an `ILibraryDatasetOpener` host seam:
    - The viewer's opener wraps `IExchangeSetService.OpenSubsetAsync`.
    - `CatalogLibraryOpener` (in `Mcp.Tools`) loads each item into any
      `IMutableDatasetCatalog`, such as `HeadlessMutableCatalog`.
  - `LibraryOperations` composes these for a headless host and tracks each
    operation in `LibraryActivityTracker` until its datasets are open. That
    covers download, re-index, then open (#790), and `AwaitIdleAsync` waits on
    it.
  - The viewer keeps thin adapters that add its notifications.
  - Later: `s100 collections list|index|find --at lat,lon`.

---

## 10. Open questions

1. **Index cache format.** Gzipped JSON is simple and probably enough
   (7k NOAA polygons, a few MB). Measure in slice 2. If load time
   matters, switch to a compact binary like `DiskS57CatalogCache`.
2. **Overlap between collections.** If a local folder and the NOAA
   download folder both contain `US5AK1AM`, the Library shows two items
   that resolve to different paths, and both could load. Accept this for
   now and revisit with update detection, which needs cross-source
   matching by cell name anyway.
3. **Band gating for S-100 items with no display scale.** Use
   `approximateGridResolution`, or always show them? Initial proposal:
   always show outlines, and gate loading by viewport only.
4. **Where the detection helpers live.** Move `ExchangeSetDetection`
   into `EncDotNet.S100.Collections`, or into `EncDotNet.S100.ExchangeSets`
   so the facade can use it too? Leaning towards ExchangeSets.
5. **Library tab placement.** Should the Library become the default
   first activity tab, ahead of Datasets, since it is now the primary
   way in?

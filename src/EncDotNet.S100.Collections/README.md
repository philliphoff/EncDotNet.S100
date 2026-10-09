# EncDotNet.S100.Collections

`EncDotNet.S100.Collections` indexes collections of S-100 and S-57 datasets
without loading them, from local folders, exchange sets, online chart
catalogues and SECOM services. It also downloads and syncs online items, and
provides a host-neutral Library runtime. The SoundCharts Library panel and the
`s100` MCP server are built on it. Reference it when you build a host that
browses, downloads or keeps chart catalogues up to date.

## Install

```bash
dotnet add package EncDotNet.S100.Collections
```

## Example: index a local folder

```csharp
using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Indexing;

var indexer = CollectionIndexer.CreateDefault();
var source = new LocalFolderSource(Guid.NewGuid(), "My charts", "path/to/charts");

SourceIndex index = await indexer.IndexAsync(source);
foreach (CollectionItem item in index.Items)
    Console.WriteLine($"{item.ProductSpec} {item.Name} edition {item.Edition}, update {item.Update}");
```

`CreateDefault` handles local folders, exchange sets, collection manifests and
S-128 catalogues. Pass the online indexers you need in `feeds`, and a
`DatasetProbe` to read loose datasets.

## Collections, sources and items

A collection is a named, saved group of exchange sets and datasets, such as
"all ENCs for Alaska". It's built from one or more sources. Indexing a source
produces one `CollectionItem` per dataset. Each item has the product, edition
and update, issue date, scale band, coverage polygons, and where the data is.
Indexing never loads the datasets.

- `DatasetCollection` and `CollectionSource` are the saved definitions. A
  source stores a reference (a path or URL), never a copy. The source kinds
  are:
  - `LocalFolderSource`
  - `ExchangeSetSource`
  - `LocalManifestSource`
  - `S128CatalogueSource`
  - `NoaaEncFeedSource`
  - `UsaceIencFeedSource`
  - `ChartCatalogsFeedSource`
  - `S100FeedSource`
  - `S100CatalogueFeedSource`
  - `S100ForecastFeedSource`
  - `SecomSource`
- `CollectionIndexer` indexes any supported source. It reuses a previous
  `SourceIndex` when the source's fingerprint hasn't changed.
- `CollectionJson` saves collection definitions as indented JSON, and source
  indexes as compact, gzip-compressed JSON.

## Indexers

### Local folders and exchange sets

`LocalSourceIndexer` indexes folders and exchange sets:

- S-100 exchange sets (`CATALOG.XML`), including their GML coverage polygons.
- S-57 exchange sets (`CATALOG.031`), plus each cell's `DSID` record for the
  edition, update and issue date.
- Exchange sets inside ZIP files.
- Loose datasets, read through a `DatasetProbe` that the host supplies.

### Local collection manifests

`LocalManifestIndexer` indexes a `*.s100collection.json` manifest, which names
groups of local folders, exchange sets or datasets. It indexes each path of
each selected group in place, as `LocalSourceIndexer` does, and tags every item
with its group. The manifest is re-read on every refresh. An unreadable
manifest gives an error diagnostic and no items, and a missing path gives a
warning; neither fails the index. See
[Local collection manifests](../../docs/local-collection-manifest.md).

### S-128 catalogues

`S128CatalogueIndexer` turns the entries of an S-128 Catalogue of Nautical
Products into catalogue-only items.

### NOAA ENC feed

`NoaaEncFeedIndexer` indexes the NOAA ENC product catalogue (`ENCProdCat.xml`):

- Each cell becomes an online item with its coverage polygons, edition,
  update, issue date and download link.
- A `NoaaEncFilter` limits a source to some states, Coast Guard districts or
  regions. `NoaaEncFacets` summarizes the states, districts and regions in the
  catalogue, with cell counts and download sizes, for choosing a filter.
- The catalogue is cached on disk and revalidated with conditional requests,
  so it's only transferred when it changes. When the server can't be reached,
  the indexer uses the cached copy.

### USACE Inland ENC feed

`UsaceIencFeedIndexer` indexes the USACE Inland ENC product catalogues (river
cells and the buoy overlay):

- Each cell becomes an online item with its bounding box. USACE publishes no
  coverage polygons.
- USACE's `edition` value, such as `22.16`, is read as edition 22, update 16.
- `UsaceIencFilter` limits a source to some rivers, and
  `UsaceIencFeedIndexer.Rivers` gives cell counts and sizes per river.
- Caching works as for the NOAA feed.

### Community chart lists

`ChartCatalogsFeedIndexer` indexes community chart lists in the
`chartcatalogs` format. Each entry is a package: a download that holds one cell
or many. The lists have no coverage, so an entry is one online item without
bounds until you download it. After that, its cells are indexed from the local
copy, with their bounds, editions and updates. Caching works as for the NOAA
feed, and a download re-indexes the source.

### S-100 feeds

`S100FeedIndexer` indexes an S-100 feed (`…/feed.json`), such as one served by
`s100 feed serve` on another computer. Its items are online, with the
publisher's coverage, until downloaded. The feed is revalidated after a minute.
See [S-100 feed format](../../docs/s100-feed-format.md).

### Remote S-100 exchange catalogues

`S100CatalogueFeedIndexer` indexes S-100 exchange catalogues (`CATALOG.XML`)
published over HTTP with their datasets beside them, such as NOAA's S-102
bathymetry on AWS Open Data:

- Dataset file names, including `file:../…`, resolve against the catalogue
  URL. Each dataset becomes an online item with its coverage polygons and
  edition, and downloads as its own file.
- Items keep their identity across editions: NOAA's version suffixes are
  dropped from tile names (`102US004SC1EV262247` becomes `102US004SC1EV`).
- Each dataset's folder below the exchange set's root is its group (for NOAA's
  S-102, `Region/Area`). `S100CatalogueFilter` limits a source by folder and
  navigation purpose, and `S100CatalogueFacets` summarizes the regions, areas
  and purposes.
- S-100 catalogues don't include sizes. When the catalogue is in an S3 bucket,
  `S3ObjectListing` lists the selected folders to get sizes and dates.
- Gzip-encoded catalogues are read as they are. Caching works as for the NOAA
  feed.

### S-100 forecast feeds

`S100ForecastFeedIndexer` indexes S-100 forecast feeds: one folder per forecast
model, each with a catalogue of its latest run only, such as NOAA's S-111
surface currents on AWS.

- A source names its models (`ForecastModel`: id, water body, cadence and
  forecast horizon), and whether runs download as tiles or as one file per
  model (`ForecastShape`).
- Each item has its run time and valid window (`run`, `validTo`). Runs have no
  editions, so a downloaded run is outdated once a later run is listed. Items
  keep their identity across runs, so downloading a new run replaces the old
  one.
- `GetModelsAsync` summarizes each model's latest run (tiles, sizes and
  footprint) for choosing models.
- Catalogues are revalidated after a minute, because each model's catalogue is
  overwritten with every run.

### SECOM services

`SecomSourceIndexer` indexes SECOM (IEC 63173-2) services through `GetSummary`
and `Get`, using `SecomClient`:

- Each data object becomes an online item. Summaries have no coverage, so an
  item gets its bounds from the downloaded copy, through the host's
  `DatasetProbe`.
- The client tries SECOM edition 2 (`/v2`) first and falls back to edition 1
  (`/v1`). It reads both editions' spellings (`S124` and `S-124`,
  `summaryObject` and `informationSummaryObject`, compact dates).
- `SecomFilter` limits a source by product, matched by the indexer, and
  optionally by an area the service filters on. A source is capped at 5,000
  objects, with a warning to narrow it to an area.
- The last listing is kept on disk, so an unreachable service still lists what
  it last offered. `DescribeAsync` counts objects per product, for choosing a
  filter.
- Encrypted data objects aren't supported.

## SECOM trust, identity and registry

- **Signatures.** `SecomSignatureVerifier` checks a data object's signature
  against the signer certificate the object carries.
  - It judges signer trust against `SecomTrustAnchors`, because Maritime
    Connectivity Platform (MCP) roots aren't in operating-system trust stores.
    `SecomTrustAnchors.BuiltIn` is the MCP MCC chain; see
    [Built-in SECOM trust anchors](Secom/TrustAnchors/README.md).
  - It verifies `ecdsa-256-sha2-256`, `ecdsa-256-sha3-256`, `ecdsa-384-sha2`,
    `ecdsa-384-sha3` and `dsa` signatures on every platform. Where the
    platform has no SHA-3 (macOS), it uses a built-in FIPS 202
    implementation.
- **Server trust.** `SecomServerTrust` trusts TLS server certificates issued
  under `SecomTrustAnchors`, for SECOM requests only. Give its
  `CreateHandler()` to SECOM clients and to `EncCellDownloader.SecomHttpClient`,
  and to nothing else.
  - It allows a connection the system rejects only because of the chain, and
    only when the chain reaches an anchor, every certificate is in its
    validity period, and the certificate names the host. It checks DNS names
    and wildcards, IP addresses, and the CN only when the certificate lists no
    DNS names, as MCP device certificates do.
  - `ResultFor(host)` gives the decision: `AnchorTrusted` with the anchor's
    name, `NotTrusted`, `Expired`, `WrongHost` or `Revoked`. For
    `AnchorTrusted`, `Revocation` says whether revocation was checked
    (`NotRevoked`) or couldn't be (`NotChecked`). `SecomRegistry` reports it in
    `SecomProbeResult.ServerTrust`.
- **Revocation.** `SecomRevocation` checks anchor-trusted chains against the
  certificate revocation lists (CRLs) their certificates name. You give it to
  `SecomServerTrust`, `EncCellDownloader.Revocation`,
  `SecomSourceIndexer.Revocation` and `LibraryDownloads.ManagedFolders`.
  - It reads DER and PEM CRLs (MCP serves PEM) and verifies each against its
    issuer. It uses a CRL only while it's current, and fetches it again after
    24 hours.
  - CRLs are kept in memory and in the cache directory. Each fetch times out
    after 5 seconds, and a URL that failed isn't tried again for 5 minutes.
  - The check fails soft: the result is `NotChecked` when no current CRL is
    available, and `Revoked` when a certificate is listed. `CacheOnly` never
    fetches; read paths use it.
  - Signature checks report `SecomSignatureCheck.SignerRevocation`. The item's
    `signature` property adds " · signer certificate revoked" or
    " · revocation not checked" after the trust.
- **Client identity.** `SecomClientIdentity` is an MCP client certificate with
  its private key, from PKCS#12 or PEM. It reports the subject, MRN, trust
  anchor and validity. It's held in memory only; saving it is the host's job.
  - `SecomServerTrust.SetIdentity` makes SECOM handlers present it to services
    that ask for it (mutual TLS). You can change it at runtime.
  - Probes report `OpenWithCertificate`, or `CertificateRefused` (only for an
    active 401, 403 or TLS refusal). They report `NeedsSecom2Search` for a
    service that lists only through SECOM 2.0's signed
    `POST …/v2/object/search`.
- **Signed requests.** `SecomEnvelopeSigner` signs SECOM 2.0 request envelopes
  with the identity, for `POST …/v2/object/search/summary` and
  `POST …/v2/object/search`.
  - `SecomClient` switches to these POST forms when a service has no GET
    summary (`UsesPostInterfaces`).
  - The indexer marks those items `RemoteEnvelope.SecomPost`, and
    `EncCellDownloader.SecomSigner` posts a signed Get for them.
  - Without an identity they fail with `SecomIdentityRequiredException`.
- **Registry.** `SecomRegistry` lists SECOM services from the MCP Maritime
  Service Registry's anonymous search.
  - It cleans the listing: it drops unusable endpoints and deleted and
    duplicate entries, and repairs geometry registered off by whole turns.
  - It caches the listing on disk.
  - `ProbeAsync` says whether a service is open, needs a certificate, presents
    an untrusted (MCP) server certificate, or can't be reached.
  - `KnownCatalogueSources.FromRegistry` turns a service into a directory
    entry.

## Downloads

`EncCellDownloader` downloads an item into a managed folder:

- It writes a ZIP to a `.partial` file first, then extracts it to a staging
  folder. The new copy replaces any old one only once it's complete.
- It works out the cell layout itself, so NOAA, USACE and bare-`.000` ZIPs all
  work.
- It keeps a download that isn't a ZIP as a bare file when the item states its
  layout, as remote S-100 datasets do.
- It decodes a SECOM object (`RemoteEnvelope.Secom`) from its `Get` response
  and saves it as the file its layout names. It checks the signature and
  records it in `.source.json`, and refuses the download if the signature
  doesn't match. It refuses encrypted objects.
- It records the signer's certificates, and judges trust, revocation and
  expiry again each time a download is read.

## Library runtime

The `EncDotNet.S100.Collections.Library` namespace holds a host-neutral
library: the collections a user keeps, and the current state of each item. The
SoundCharts Library panel is built on it, and a headless host such as the
`s100` MCP server uses it the same way.

- `CollectionLibrary` owns a set of collections.
  - It saves their definitions to a store file (`collections.json`) and caches
    each source's index on disk (`CollectionLibraryOptions`).
  - It re-indexes sources one at a time on a background worker. `WhenIdle`
    waits for the queue to empty.
  - It publishes immutable `LibraryCollection` and `LibrarySource` snapshots
    and a coarse `Changed` event.
  - Loaded S-128 catalogues can appear in a temporary session collection until
    the user keeps them.
- `LibraryItemState` is one item as a host sees it.
  - `Availability` is one of listed, online, local, missing, on-pan, loaded,
    update or expired. `LibraryAvailabilityNames` gives each state's wire name.
  - `EffectiveItem` is the copy that would open.
  - `ValidWindow` is the time the data covers: a forecast run's window, or a
    dataset's own time coverage.
  - The host supplies its downloaded copies (`ILibraryLocalCopies`) and what
    it has open (`LibraryLoadState`).
- `LibraryQuery` finds items by collection or source, state, product, text,
  bounding box, covering point (most detailed first, through
  `CoverageHitTest`) and valid time.
- `ForecastRuns` gives an item's forecast-run facts: its model, horizon, shown
  run window and its S-102 twin tile.
- `CollectionSource.ShowOnMap` asks a host to keep a source's local datasets on
  the map. `LibraryLoader` labels each open group with its Library source
  (`LibrarySourceLabel`, `LibraryOpenGroup.Source`), so a host can show a
  source's datasets as one row.
- `LibrarySync` keeps sources that have `CollectionSource.Sync` set up to date
  after each index. An `ILibrarySyncPolicy` sets the rules for each kind:
  - `SecomSyncPolicy` downloads and prunes. It downloads new and changed
    objects, up to `LibrarySyncOptions.MaxBytes`, and prunes copies that no
    source for the service still lists.
  - `OnlineSyncPolicy` covers NOAA and USACE, community lists, S-100 feeds and
    catalogues, and forecast feeds. It downloads newer editions, packages and
    runs, and never prunes, because those folders also hold downloads the user
    made by hand.
  - It never prunes from a stale or capped listing, or a copy the host says is
    in use.
  - It re-indexes once so downloads get their bounds, and SECOM objects their
    signature. Indexers that read downloaded copies take the fingerprint
    before reading, so a download that finishes during an index still changes
    the next fingerprint.
  - `StatusOf` and `Synced` report each source's last sync.
  - `LibraryDownloads.DownloadedNames` and `Delete`, and
    `EncCellDownloader.ListDownloaded` and `Delete`, remove downloads.
- `LibraryDownloads` downloads online items (ENC cells, community packages,
  S-100 feeds and the NOAA forecast catalogues) through `EncCellDownloader`, at
  most three at a time.
  - Progress is a `LibraryDownloadProgress`, which you can poll, receive
    through `ProgressChanged`, or get through an `IProgress`. `StatusOf` gives
    each item's status.
  - `ManagedFolders` puts each download in the same folder layout SoundCharts
    uses, so every host keeps downloads in the same place.
  - It's the `ILibraryLocalCopies` that turns a downloaded item into a local
    one.
- `LibraryLoader` opens items into a host's session.
  - `Plan` skips what can't be opened and groups the rest by exchange set.
  - Each group opens through the host's `ILibraryDatasetOpener`. SoundCharts'
    opener registers the group with its exchange-set service.
    `CatalogLibraryOpener` in `EncDotNet.S100.Mcp.Tools` loads it into any
    `IMutableDatasetCatalog`.
- `LibraryOperations` runs the actions a headless host needs: load, and
  download (then re-index the downloaded items' sources, waiting for packages,
  or open the downloads).
  - A `LibraryActivityTracker` tracks every operation from the call until its
    datasets are open.
  - So `AwaitIdleAsync` never reports idle between a download finishing and
    its datasets opening.

## Known catalogue sources

`KnownSources/known-sources.json` is embedded in the library and exposed as
`KnownCatalogueSources.All`. It lists the online chart catalogues SoundCharts
offers under **Browse online catalogues…** in the Library. The list is
maintained in this repository, under its MIT licence.

To add a catalogue:

- Point at the provider's own catalogue URL. Don't copy entries or data from
  other projects' source lists, and never from GPL-licensed ones such as
  OpenCPN's.
- Check the licence of any third-party list you reference. For example, the
  community `chartcatalogs/catalogs` lists are CC0.
- Use a supported `format`: `noaaEnc`, `usaceIenc`, `chartCatalogs`,
  `s100Feed`, `s100ExchangeCatalogue`, `s100ForecastModels` or `secom`. A build
  skips entries in a format it doesn't know, instead of rejecting the list, so
  newer lists still load.
- Describe accurately what the catalogue provides. These fields drive the
  quality chips users see:
  - `coverage`: `polygons`, `boundingBoxes` or `none`.
  - `editions`: whether it lists editions and updates.
  - `sizes`: whether it lists download sizes, or, for an S-100 catalogue in an
    S3 bucket, whether they can be listed.
  - `product` (optional): the one product the catalogue publishes, such as
    `S-102`.
  - `notForNavigation` (optional): `true` when the provider marks all its data
    as not for navigation.
  - `models` (for `s100ForecastModels`): each model's `id`, `name` (water
    body), `cadenceHours` and `horizonHours`, measured from the published runs,
    and its `catalogue` path when it isn't `<id>/CATALOG.XML`.
  - `pilot` (optional): `true` for a pilot service, which may cover little and
    may stop.

## See also

- [Dataset collections](../../docs/design/dataset-collections.md): the design
  note, including SECOM support.
- [Local collection manifests](../../docs/local-collection-manifest.md) and
  [S-100 feed format](../../docs/s100-feed-format.md): the JSON formats the
  Library reads.
- [MCP server](../../docs/mcp-server.md): the Library tools for AI agents.

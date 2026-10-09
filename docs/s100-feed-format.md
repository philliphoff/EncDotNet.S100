# S-100 feed format

An S-100 feed is EncDotNet.S100's own JSON format for publishing datasets to
other computers. `s100 feed serve` serves a feed over HTTP, and
`s100 feed export` writes one as static files for any web host. SoundCharts on
another computer adds the feed's URL to its Library and lists the datasets
there.

A feed carries the metadata the Library indexes locally, such as coverage
polygons, bounds, editions, updates, display scales and usage bands, so a
reader can show coverage before downloading anything. The Library reads
standard catalogues too, but none of them fit this job:

- An S-100 `CATALOG.XML` describes only S-100 datasets, not S-57 cells or loose
  datasets.
- S-128 is heavyweight and has no download URLs.
- NOAA's product catalogue is specific to NOAA ENCs.

## Example

A feed is one JSON document, named `feed.json` by convention. It's written
compact; here it's indented:

```json
{
  "format": "encdotnet-s100-feed",
  "version": 1,
  "title": "Ohio charts",
  "generatedAt": "2026-09-25T23:27:38.364377+00:00",
  "fingerprint": "local-v2:F053E74084A10F99E7ECB1E2AA827E5778C8B8A962D573D95EDB65B2D1E9F2B4",
  "machine": "bridge-pc",
  "items": [
    {
      "key": "ENC_ROOT/US4OH1MK",
      "productSpec": "S-57",
      "name": "US4OH1MK",
      "title": "Lake Erie - Lake Center Offshore of Cleveland, OH",
      "groupKey": "ENC_ROOT",
      "edition": 1,
      "update": 1,
      "issueDate": "2024-05-07",
      "updateApplicationDate": "2024-02-21",
      "compilationScale": 90000,
      "usageBand": 4,
      "status": "unknown",
      "bounds": {
        "south": 42,
        "west": -81.9,
        "north": 42.3,
        "east": -81.6,
        "crossesAntimeridian": false,
        "longitudeSpan": 0.3
      },
      "location": {
        "kind": "remote",
        "uri": "items/0d8d194517c460ed498b.zip",
        "sizeBytes": 7664,
        "lastModified": "2024-05-08T19:03:40+00:00",
        "package": "0d8d194517c460ed498b",
        "layout": {
          "relativePath": "US4OH1MK/US4OH1MK.000",
          "updateRelativePaths": ["US4OH1MK/US4OH1MK.001"],
          "catalogueRelativePath": "CATALOG.031"
        }
      },
      "properties": {
        "producingAgency": "550",
        "intendedUsage": "4"
      }
    }
  ]
}
```

## Feed properties

| Property | Required | Description |
|---|---|---|
| `format` | Yes | Always `encdotnet-s100-feed`. A reader rejects any other value. |
| `version` | Yes | `1`. A reader rejects a version newer than it knows. |
| `title` | No | The feed's title, such as the published folder's name. |
| `generatedAt` | Yes | When the feed was generated, ISO 8601. |
| `fingerprint` | No | Changes whenever the published data changes. `s100 feed serve` uses it as the HTTP `ETag`, so readers can revalidate with conditional requests. |
| `machine` | No | The publishing computer's name. `s100 feed serve` sets it, and SoundCharts names a shared feed after it. |
| `items` | Yes | The published datasets. See [Item properties](#item-properties). |

A feed lists only items whose files are present on the publishing computer.

## Item properties

Items have the shape of a Library index entry (`CollectionItem`). Unknown
properties are ignored.

| Property | Description |
|---|---|
| `key` | The item's identifier, unique within the feed: an S-57 cell name, an exchange-set-relative dataset path, or a loose file's relative path. |
| `productSpec` | The product specification, such as `S-57` or `S-101`. |
| `productSpecVersion` | The product specification's edition, when known, such as `2.0.0`. |
| `name` | The dataset's name, such as the cell name. |
| `title` | A descriptive title. |
| `groupKey` | The exchange set the item belongs to, or absent for a loose dataset. |
| `edition`, `update` | The edition number, and the number of the latest update. |
| `issueDate`, `updateApplicationDate` | The issue date of the latest file, and the edition's update application date. `YYYY-MM-DD`. |
| `compilationScale`, `minimumDisplayScale`, `maximumDisplayScale` | Scale denominators: the compilation scale (S-57 `CSCL`), and the coarsest and finest scales the dataset is meant to be displayed at. |
| `usageBand` | The ENC usage band, 1 to 6, from an S-57 cell name. |
| `status` | `unknown`, `active`, `superseded`, `cancelled` or `planned`. |
| `bounds` | The bounding box, or absent when the publisher couldn't read it (the item is then listed but not drawn): `south`, `west`, `north`, `east`. A box that crosses ±180° has `west` greater than `east`. `crossesAntimeridian` and `longitudeSpan` are derived and ignored on reading. |
| `coverage` | S-100 items can also carry coverage polygons. |
| `location` | Where to download the item. See [Item location](#item-location). |
| `properties` | Other facts as string pairs, such as `producingAgency`. |

## Item location

An item's `location` in a feed always has `kind` `remote`:

| Property | Description |
|---|---|
| `uri` | The item's download, relative to the feed's URL, so the same feed works on any host, port or static web host. |
| `package` | The item's opaque id, a hash of its key. It never reveals or leads to a path on the publishing computer. |
| `layout` | Where the item's files are inside the download: `relativePath` (the base file), `updateRelativePaths` (update files, in order) and `catalogueRelativePath` (the exchange-set catalogue, or absent for a loose dataset). |
| `sizeBytes` | The total uncompressed size of the item's files. |
| `lastModified` | When the newest of the item's files changed. |

## Downloads

Each item downloads as a zip, `items/<id>.zip`. The zip holds the item's
exchange-set catalogue, base file and updates, at their paths relative to the
item's root. No file is outside that root.

A reader extracts the zip into a folder of its own and opens the dataset at
`layout.relativePath`, with the catalogue and updates beside it, as they were on
the publishing computer.

## Serve a feed

`s100 feed serve <path>` serves `feed.json` and the item zips over HTTP:

```bash
s100 feed serve charts/ --host 0.0.0.0
```

- **Access.** By default the server listens on this computer only. On any
  other address it adds a random access token as the first path segment, as in
  `http://<host>:8100/<token>/feed.json`. Item URLs are relative, so they carry
  the token too.
- **Caching.** The feed's `ETag` comes from its fingerprint, so an unchanged
  folder answers conditional requests with `304 Not Modified`.

For the options, see [`feed serve`](cli.md#feed-serve).

## Export a static feed

`s100 feed export <path> --out <directory>` writes `feed.json` and the item zips
as files, for any static web host:

```bash
s100 feed export charts/ --out site/charts
```

- **Incremental.** Exporting again rewrites only the items whose files
  changed.
- **Consistent.** `feed.json` is written last, so it only lists zips that
  exist.
- **Caching.** The web host supplies its own caching headers, such as `ETag` or
  `Last-Modified`, and readers revalidate with those.

For the options, see [`feed export`](cli.md#feed-export).

## Use a feed in SoundCharts

1. In the Library, choose **Add** > **Connect to a shared feed…**.
2. Paste the feed's URL into **Feed URL** and choose **Connect**. SoundCharts
   checks that the URL answers with a feed before adding it.

You can also add a feed's URL from **Add** > **Browse online catalogues…**
with **Add a catalogue by URL** and **Check & add**. SoundCharts recognises a
feed by its `format` property, and you choose which products to include.

- The feed's items appear on the map with their coverage.
- Downloads go to the `feeds/<host>-<port>-<hash>/` folder under SoundCharts'
  downloads folder.
- SoundCharts revalidates the feed at most once a minute, with a conditional
  request.

## Read and write feeds in code

The feed types are in the `EncDotNet.S100.Collections.Feeds` namespace of the
`EncDotNet.S100.Collections` package. To publish a folder, index it in place,
build a feed from the index, and write each item's zip:

```csharp
using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Feeds;
using EncDotNet.S100.Collections.Indexing;

// Index a folder in place, then publish what was indexed.
var index = await CollectionIndexer.CreateDefault()
    .IndexAsync(new LocalFolderSource(Guid.NewGuid(), null, "/charts/ohio"));
var feed = S100Feed.FromIndex(index, "Ohio charts");

using (var output = File.Create("feed.json"))
    S100Feed.Write(output, feed);

// Write one item's download.
var item = index.Items[0];
using (var zip = File.Create(S100Feed.ItemId(item) + ".zip"))
    await S100FeedPackager.WriteZipAsync(zip, (LocalItemLocation)item.Location);
```

`CollectionIndexer.CreateDefault()` without a `DatasetProbe` recognises loose
S-57 cells only, without bounds. Pass a probe to read metadata from other loose
dataset files.

To read a feed, call `S100Feed.Read(stream, feedUri)`. It resolves each item's
relative URL against `feedUri`, the address the feed was fetched from. It
throws `JsonException` for a document that isn't a feed, and
`NotSupportedException` for a newer format version.

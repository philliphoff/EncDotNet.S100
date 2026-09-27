# S-100 feed format

## Why it exists

An **S-100 feed** is this project's own, simple JSON format for publishing
datasets to other machines (issue #680). A feed can be served over HTTP by
`s100 feed serve`, or written as static files by `s100 feed export` for any web
host. Another machine's viewer then adds the feed's URL as an online catalogue
in its Library.

The Library can already read standard catalogues, but none of them fit this job:

- **S-100 `CATALOG.XML`** describes only S-100 datasets. It cannot describe
  S-57 cells or loose datasets.
- **S-128** is heavyweight and carries no download URLs.
- **NOAA's product catalogue** is specific to NOAA ENCs.

A feed carries the same metadata the Library indexes locally: coverage
polygons, bounds, editions, updates, display scales and usage bands. So a
reader can show coverage before downloading anything.

## Document

A feed is one JSON document, conventionally named `feed.json`. It is written
compact; it is shown indented here:

```json
{
  "format": "encdotnet-s100-feed",
  "version": 1,
  "title": "Ohio charts",
  "generatedAt": "2026-09-25T23:27:38.364377+00:00",
  "fingerprint": "local-v1:F053E74084A10F99E7ECB1E2AA827E5778C8B8A962D573D95EDB65B2D1E9F2B4",
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

- **Header fields:**
  - `format` must be `encdotnet-s100-feed`.
  - A reader rejects a `version` newer than it knows.
- **`fingerprint`:** changes whenever the published data changes. A server
  uses it as the HTTP `ETag`, so readers can revalidate with conditional
  requests.
- **Items:** have the shape of the Library index (`CollectionItem`). Unknown
  properties are ignored.
- **What is published:** only items whose files are present on the publishing
  machine.
- **Coverage:** S-100 items can carry `coverage` polygons as well as `bounds`.
  In `bounds`, `crossesAntimeridian` and `longitudeSpan` are derived and ignored
  on reading. A bounding box that crosses ±180° has `west` greater than `east`.
- **Item `location`:** always `remote`, with these fields:
  - `uri` is the item's download, **relative to the feed's URL**, so the
    same feed works behind any host, port or static web host.
  - `package` is the item's opaque id, a hash of its key. It never reveals,
    or reaches, a path on the publisher.
  - `layout` says where the item's files lie inside the download.
  - `sizeBytes` is the total size of those files, uncompressed.
  - `lastModified` is when the newest of them changed.

## Downloads

Each item downloads as a zip (`items/<id>.zip`). The zip holds the item's
exchange-set catalogue, base file and updates, at their paths relative to the
item's root. Files are never outside that root.

The reader extracts the zip into a folder of its own and opens the dataset at
`layout.relativePath`, with the catalogue and updates beside it. That is the
same layout it would have on the publishing machine.

## Serving a feed

`s100 feed serve <path>` serves `feed.json` and the item zips over HTTP:

```
s100 feed serve charts/ --host 0.0.0.0
```

- **Scope:** by default the server listens on localhost only. On another
  address it adds a random access token as the first path segment
  (`http://<host>:8100/<token>/feed.json`). Item URLs are relative, so they
  carry the token too.
- **Caching:** the feed's `ETag` is derived from its fingerprint, so an
  unchanged folder answers conditional requests with `304 Not Modified`.

See the [CLI reference](../tools/EncDotNet.S100.Cli/README.md) for the options.

## Using a feed in the viewer

In the viewer, open **Library → Online Catalogue** and add the feed's URL with
**Add URL**. The viewer recognises the feed from its `format` property and
lets you choose which products to include.

- **On the map:** the feed's items appear with their coverage.
- **Downloads:** each download goes to `downloads/feeds/<host>-<port>-<hash>/`.
- **Revalidation:** the feed is revalidated at most once a minute, with a
  conditional request.

## Building feeds in code

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

On the reading side, `S100Feed.Read(stream, feedUri)` resolves each item's
URL against the address the feed was fetched from.

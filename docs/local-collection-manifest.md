# Local collection manifests

A collection manifest is a JSON file that names groups of local paths, such as
one group per producing country. In the SoundCharts Library it's a source like
any other, except that you can pick its groups the way you pick an online
catalogue's products, and each group gets its own node in the tree. Use one
when your data is kept in a folder tree organized by something meaningful:

```text
IC-ENC/
  AU/  S-101/ S-102/ S-104/ S-111/
  BE/  S-101/ S-102/
  NL/  S-101/ S-102/ S-104/ S-111/ S-122/ S-123/ S-128/
  …
```

Adding the whole tree as a Library folder gives one flat list. Adding each
subfolder takes one source per folder. A manifest gives you one source with a
node per group, so you can select, filter and load one group at a time.

- **Data stays where it is.** Nothing is copied.
- **The manifest is read live.** Edit it and refresh the source, and the Library
  picks up the change.
- **You write it by hand,** or generate it with a script or an agent. There's
  no editor in SoundCharts and no `s100` command for it.

## Example

A manifest is one JSON file. By convention its name ends in
`.s100collection.json`, such as `ic-enc.s100collection.json`, and it sits at
the root of the tree it describes:

```json
{
  // Comments and trailing commas are allowed.
  "format": "encdotnet-s100-collection",
  "version": 1,
  "title": "IC-ENC",
  "description": "S-100 exchange sets from IC-ENC members, grouped by producing country.",
  "groups": [
    { "id": "AU", "name": "Australia", "paths": ["AU"] },
    { "id": "BE", "name": "Belgium", "paths": ["BE"] },
    { "id": "ID", "name": "Indonesia", "paths": ["ID/101ID005292R4_000", "ID/101ID00300LOMBOK_000"] }
  ]
}
```

## Manifest properties

| Property | Required | Description |
|---|---|---|
| `format` | Yes | Always `encdotnet-s100-collection`. |
| `version` | Yes | `1`. A newer version is refused. |
| `title` | No | Names the collection when it's added. Defaults to the file name. |
| `description` | No | Free text. |
| `groups` | Yes | One or more groups, in display order. |

Other properties, such as `$schema` for an editor's schema, are ignored.

## Group properties

| Property | Required | Description |
|---|---|---|
| `id` | Yes | Letters, digits, `.`, `_` and `-`. Must be unique, ignoring case. The Library stores your selection by id, so you can rename a group, but keep its id. |
| `name` | No | The display name. Defaults to the `id`. |
| `description` | No | Free text. |
| `paths` | Yes | One or more paths. See [Paths](#paths). |
| `recursive` | No | Whether folders are scanned recursively. Defaults to `true`. |

## Paths

- A relative path resolves against the manifest's own folder, so the manifest
  and its data can move together. Absolute paths work too.
- `/` and `\` both work as separators.
- A path can be anything a Library folder source accepts: a folder, which is
  scanned for S-100 `CATALOG.XML` and S-57 `CATALOG.031` exchange sets, zipped
  exchange sets and loose datasets; a single exchange-set folder or catalogue;
  a ZIP; or a loose dataset file.
- The same folder can appear in more than one group. Its datasets are then
  listed once in each group.
- A path that doesn't exist isn't an error. You can still pick the group. It
  lists no datasets and shows **Path not found** until the path exists and you
  refresh the source.

## Errors

The Library reads a manifest strictly and reports every problem in the groups,
each with its line number, for example
`line 12 · groups[5].id: duplicate 'AU' (also groups[0])`.

- **When you add a manifest,** the dialog shows **Couldn't read this
  manifest** with the problems. Nothing is added until the file reads cleanly.
  Fix the file and choose **Reload file**.
- **When a manifest that's already a source becomes unreadable,** the source
  shows an error and lists nothing. The next refresh reads it again.

## Add a manifest in SoundCharts

1. Choose **Add** > **Collection manifest…** in the Library, or **File** >
   **Add to Library** > **Collection manifest…**. You can also drop the file
   on the window. A dropped `.json` file counts as a manifest when its name ends
   in `.s100collection.json` or its `format` says so.
2. Choose **Everything**, or **Only what I select** and tick the groups you
   want. **Everything** also includes groups added to the file later. A
   selection is stored by group id.

To change the selection later, choose **Choose groups...** on the source's
context menu.

A source with two or more groups gets one node per group. Selecting a group
lists only its datasets, and the map's coverage, **Load as you pan** and
**Download** follow that list. An item's details show its group in the Source
section, for example `Belgium (BE)`.

## Use manifests from code

These types are in the `EncDotNet.S100.Collections` package:

- **Source.** `LocalManifestSource` (kind `localManifest`) describes the source,
  and `LocalManifestFilter` the selected groups. A filter with no groups matches
  every group, including groups added later.
- **Reading.** `CollectionManifest.Read` and `CollectionManifest.ReadFile` read
  and validate a manifest. Invalid content throws
  `CollectionManifestException`, whose `Problems` lists each problem. A newer
  format version throws `NotSupportedException`.
- **Indexing.** `LocalManifestIndexer`, which `CollectionIndexer.CreateDefault`
  registers, indexes the selected groups' paths in place:
  - Each item has `group` and `groupName` properties.
  - Each item's key starts with `<group id>:<manifest-relative path>/`.
  - `SourceIndex.Groups` lists the selected groups, including groups whose
    paths are missing.
- **Fingerprint.** The index fingerprint covers the manifest's content, the
  selection, and the files of every selected path. A change to a group that
  isn't selected doesn't trigger a re-index.

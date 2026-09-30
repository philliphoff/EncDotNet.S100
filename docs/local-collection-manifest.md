# Local collection manifests

## Why they exist

Test data and chart sets are often kept in folder trees that are organised by
something meaningful, such as the producing country:

```
IC-ENC/
  AU/  S-101/ S-102/ S-104/ S-111/
  BE/  S-101/ S-102/
  NL/  S-101/ S-102/ S-104/ S-111/ S-122/ S-123/ S-128/
  …
```

Adding the whole tree as a Library folder gives one flat list. Adding each
subfolder separately takes a lot of clicks.

A **collection manifest** is a small JSON file that names **groups** of local
paths. In the viewer's Library it is a source like any other, except that
its groups can be picked like an online catalogue's facets. Each group also
appears as its own node in the tree, so you can select, filter and load one
group at a time.

- **Data stays where it is.** Nothing is copied.
- **The manifest is read live.** Edit it and refresh the source, and the
  Library picks up the change.
- **It is meant to be hand-written.** An agent or a script can also generate
  one, so there is no authoring UI and no CLI command.

## Document

A manifest is one JSON file. By convention its name ends in
`.s100collection.json`, e.g. `ic-enc.s100collection.json`, placed at the root
of the tree it describes:

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

| Property | Required | Meaning |
|---|---|---|
| `format` | yes | Always `encdotnet-s100-collection`. |
| `version` | yes | `1`. Newer versions are refused. |
| `title` | no | Names the collection when it is added. Defaults to the file name. |
| `description` | no | Free text. |
| `groups` | yes | One or more groups, in display order. |
| `$schema` | no | Ignored, so editors can point it at a schema. Any other unknown property is ignored too. |

Each group:

| Property | Required | Meaning |
|---|---|---|
| `id` | yes | Letters, digits, `.`, `_` and `-`. Must be unique, ignoring case. The Library stores the selection by id, so rename groups freely but keep their ids. |
| `name` | no | The display name. Defaults to the `id`. |
| `description` | no | Free text. |
| `paths` | yes | One or more paths (see below). |
| `recursive` | no | Whether folders are scanned recursively. Defaults to `true`. |

### Paths

- A relative path resolves against the manifest's own folder, so the manifest
  and its data can move together. An absolute path is also allowed.
- `/` and `\` both work as separators.
- A path can be anything a Library folder source accepts: a folder, which is
  scanned for S-100 `CATALOG.XML` and S-57 `CATALOG.031` exchange sets, zipped
  exchange sets and loose datasets; a single exchange-set folder or catalogue;
  a ZIP; or a loose dataset file.
- The same folder may appear in more than one group. Its datasets are then
  listed once per group.

### Errors

The Library reads a manifest strictly and reports every problem it finds,
each with its line number, e.g. `line 12 · groups[5].id: duplicate 'AU' (also groups[0])`:

- **When adding a manifest,** the dialog shows the problems, and nothing is
  added until the file reads cleanly.
- **When a manifest that is already a source becomes unreadable,** the source
  shows an error and lists nothing. The next refresh tries again.

A group path that doesn't exist is **not** an error. The group can still be
picked. It lists no datasets, shows a "Path not found" warning, and fills in
once the path exists and the source is refreshed.

## In the viewer

- **Add a manifest.** Use **Library › Add › Collection manifest…** or
  **File › Add to Library › Collection manifest…**, or drop the file on the
  window. A dropped `.json` file counts as a manifest when its name ends in
  `.s100collection.json` or its `format` says so.
- **Pick what to include.** Choose **Everything** or pick groups:
  - Everything also includes groups that are added to the file later.
  - Picked groups are stored by id.
- **Change the selection later.** Use **Choose groups…** on the source's
  context menu.
- **Tree layout.** A source with two or more groups gets one node per group.
  Selecting a group lists only its datasets, and the map's coverage,
  *Load as you pan* and *Download listed* follow that list.
- **Item details** show the group in the Source section as, e.g.,
  `Belgium (BE)`.

## For library users

- **Types.** `LocalManifestSource` (kind `localManifest`) and
  `LocalManifestFilter` describe the source.
- **Reading.** `CollectionManifest.Read`/`ReadFile` read and validate a
  manifest. Invalid content throws `CollectionManifestException` with its
  `Problems`.
- **Indexing.** `LocalManifestIndexer`, which `CollectionIndexer.CreateDefault`
  registers, indexes the selected groups' paths in place:
  - Each item carries the `group` and `groupName` properties.
  - Its key is prefixed with `<group id>:<manifest-relative path>/`.
  - `SourceIndex.Groups` lists the selected groups, including groups that are
    missing on disk.
- **Fingerprint.** The index's fingerprint covers the manifest's content, the
  selection, and every selected path's files. A change to a group that isn't
  selected does not trigger a re-index.

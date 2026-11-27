# EncDotNet.S100.Datasets.Nis

Reads an Esri ArcGIS Maritime S-57 **Nautical Information System (NIS)** and
converts its cells to S-57 ENC base cells. Not for navigation.

- A **source-neutral NIS model** (`NisFeature`, `NisGeometry`,
  `NisRelationship`) and the `INisSource` interface, so the conversion never
  depends on how the NIS is stored.
- **`FileGdbNisSource`**, a managed, read-only file geodatabase reader. It
  needs no Esri software or native library.
- **`NisToS57Translator`**, which turns one cell of the model into an
  `EncDotNet.S57` `S57Document` for `S57DocumentWriter` to encode.

[Converting an NIS to S-57](../../docs/nis-to-s57.md) explains what the NIS
holds and how it's converted. `s100 nis convert` is the command-line driver.

## Convert a cell

```csharp
using EncDotNet.S100.Datasets.Nis;
using EncDotNet.S100.Datasets.Nis.FileGdb;
using EncDotNet.S57;

var source = FileGdbNisSource.Open("production.gdb");
Console.WriteLine(string.Join(", ", source.GetDatasetNames())); // US5CA98M.005, ...

var diagnostics = new NisToS57Diagnostics();
var options = new NisToS57Options { DatasetName = "US5CA98M.005", EditionNumber = 3 };
S57Document cell = new NisToS57Translator(options).Translate(source, diagnostics);
S57DocumentWriter.WriteToFile("US5CA98M.000", cell);
```

`Translate` throws `InvalidOperationException` when the NIS holds more than
one cell and `DatasetName` doesn't choose one. `NisToS57Options` also sets the
written cell name, a bounding box, the issue date, the producing agency and
agency codes, the compilation scale and datums, the sounding group size, and
whether to add M_COVR objects.

## Cache an NIS as S-57 files

`NisCellCache.Materialize` translates every cell of a file geodatabase and
writes them as S-57 base cells, so anything that reads S-57 files can open an
NIS:

```csharp
IReadOnlyList<NisCellFile> cells = NisCellCache.Materialize("production.gdb", cacheRoot);
// cells[0].DatasetName == "US5CA98M.005", cells[0].Path == ".../US5CA98M.000"
```

The geodatabase is read once for all its cells. The cells go in a folder under
`cacheRoot` named after `NisCellCache.Fingerprint`, a hash of the
geodatabase's files (names, sizes, write times, without lock files), and are
reused until it changes. `NisCellPath.TryParse` reads a cell path,
`<path>.gdb/<DSNM>`, and `Find` picks its cell by full name or by name without
extension. `FileGdbNisSource.IsNisGeodatabase` tells an NIS from any other
folder. The `s100` CLI uses these to open an NIS directly.

## Implement a source

A source decodes storage only; the translator interprets the NIS conventions.
For each row, a source gives:

| `NisFeature` member | From |
|---|---|
| `FeatureClass` | The feature class or table name. |
| `ObjectClass` | The S-57 acronym from the subtype name (`BOYSPP_BuoySpecialPurpose` → `BOYSPP`). |
| `LongName`, `DatasetName`, `CompilationScale`, `IsDeleted` | `LNAM`, `DSNM`, `PLTS_COMP_SCALE`, `IS_DELETE`. |
| `Geometry` | The shape, in WGS 84, with soundings' depth as Z. |
| `Attributes` | Every field with a value, by name, as stored. |

Collections (`PLTS_COLLECTIONS`) are features with no geometry, and
`GetRelationships` returns the `PLTS_FREL` rows. `NisQuery.Matches` applies a
query to a feature, for sources that can't filter in their own query.
`InMemoryNisSource` holds features built in code; the tests use it.

The methods are synchronous. An ArcGIS Pro source calls Pro's geodatabase API
on its thread (inside `QueuedTask.Run`), which also gives it enterprise
geodatabases, edit versions and unsaved edits.

## File geodatabase reader

`FileGdbNisSource.Open` reads the system catalogue (`a00000001.gdbtable`),
finds the tables with `FCSubtype` and `LNAM` fields, and decodes subtypes from
the `GDB_Items` definitions. Rows are read one at a time with positioned
reads. The format follows the OpenFileGDB description the GDAL project
publishes. Points, multipoints, polylines and polygons are read with Z; M
values aren't read, and curves come back as straight segments. Enterprise and
mobile geodatabases, versioning and writing are out of scope.

## Dependencies

EncDotNet.S100.Datasets.S57 (for the S-57 chain-node edge graph and lexical
levels), EncDotNet.S57 and NetTopologySuite.

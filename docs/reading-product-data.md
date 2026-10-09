# Reading product data

The `EncDotNet.S100` facade renders any product and lists its features in the
same way for every product. When your code needs the data itself, such as a
warning's text, a route's waypoints, the depth at a grid cell or the current at
a given time, read the dataset with its product's own package. Each
`EncDotNet.S100.Datasets.Sxxx` package parses one product into .NET types that
follow its product specification.

The facade package references every product package, so these types are
available once you install `EncDotNet.S100`. The HDF5 examples also use
`PureHdfFile` from `EncDotNet.S100.Hdf5.PureHdf`, which the facade also
brings in. The examples use files from
[`tests/datasets`](https://github.com/philliphoff/EncDotNet.S100/tree/main/tests/datasets).

## Choose a reader for the encoding

How you open a dataset depends on its encoding:

| Encoding | Products | Open with | You get |
|---|---|---|---|
| GML (`.gml`) | S-122, S-124, S-125, S-127, S-128, S-129, S-131, S-201, S-411, S-421 | `SxxxDataset.Open(path or stream)` | Features and information types, plus a typed model |
| ISO 8211 (`.000`) | S-101 | `S101Dataset.Open(path or stream)` | The ENC's records; features through `S101VectorSource` |
| HDF5 (`.h5`) | S-102, S-104, S-111 | `SxxxDatasetReader.Read(PureHdfFile.Open(path))` | Coverage grids of values, per time step for S-104 and S-111 |

To get a dataset's product, edition and extent without parsing all of it, call
the static `ReadMetadata` method. For GML and S-101 it's on the dataset type;
for HDF5 it's on the reader:

```csharp
using EncDotNet.S100.Datasets.S124;

var metadata = S124Dataset.ReadMetadata("navwarn_mixed.gml");
Console.WriteLine($"{metadata.Spec}, extent {metadata.Extent}");
```

## Read GML features and information types

A GML dataset is a set of **features**, which have geometry, and **information
types**, which don't. Every product's feature type implements `IS100Feature`,
so you read geometry the same way for all of them:

```csharp
using EncDotNet.S100.Datasets.S124;

var dataset = S124Dataset.Open("navwarn_mixed.gml");

foreach (var feature in dataset.Features)
{
    Console.WriteLine($"{feature.Id} {feature.FeatureType} ({feature.GeometryType})");
    foreach (var (code, value) in feature.Attributes)
        Console.WriteLine($"  {code} = {value}");
}
```

- `GeometryType` says which geometry property is filled in: `Points` for
  `Point`, `Curves` for `Curve`, and `ExteriorRing` and `InteriorRings` for
  `Surface`. For a feature with several surfaces, `ExteriorRing` holds them all
  joined; use `Surfaces` to get each surface with its own holes.
- Positions are `GeoPosition` values: latitude and longitude in WGS 84 degrees.
  GML stores them as latitude then longitude (S-100 Part 10b); the reader
  handles that for you.
- `Attributes` holds simple attributes as strings, keyed by attribute code.
  Complex attributes, which have sub-attributes, are in `ComplexAttributes`.
  `xlink` references to other features or information types are in
  `References`.

The information types are in the dataset's `InformationTypes`.

## Read a typed data model

The attribute dictionaries follow the product's feature catalogue, so reading
them means knowing attribute codes and parsing strings. Every GML product also
has a **typed model**: a read-only projection of the dataset into classes named
after the product's concepts, with parsed values and resolved references.
Create it with the root type's `From` method. This example reads an S-124
navigational warning:

```csharp
using EncDotNet.S100.Datasets.S124;
using EncDotNet.S100.Datasets.S124.DataModel;

var dataset = S124Dataset.Open("navwarn_mixed.gml");
var warning = S124NavigationalWarning.From(dataset, out var diagnostics);

Console.WriteLine($"{warning.Preamble?.GeneralArea}, {warning.Preamble?.Locality}: {warning.Parts.Count} part(s)");
foreach (var part in warning.Parts)
    Console.WriteLine($"  {part.Id}: {part.GeometryKind}, {part.Coordinates.Count} position(s)");
```

### Check the diagnostics

`From` doesn't throw on bad data. A reference that doesn't resolve, an
attribute that doesn't parse, or a feature without geometry becomes a
**diagnostic**, and the projection continues with what it could read. It throws
`InvalidOperationException` only when the dataset has nothing to project from,
such as an S-421 dataset with no `Route` feature.

> [!IMPORTANT]
> Always check `diagnostics`. A projection with warnings still returns an
> object, but it can be missing the entities the warnings name.

```csharp
using EncDotNet.S100.Datasets.S421;
using EncDotNet.S100.Datasets.S421.DataModel;

var routeDataset = S421Dataset.Open("RTE-TEST-GFULL.s421.gml");
var plan = S421RoutePlan.From(routeDataset, out var diagnostics);

foreach (var waypoint in plan.Route.Waypoints)
    Console.WriteLine($"{waypoint.WaypointNumber} {waypoint.Name}: {waypoint.Position}");

foreach (var diagnostic in diagnostics)
    Console.WriteLine($"{diagnostic.Severity} {diagnostic.Code}: {diagnostic.Message}");
```

The `RTE-TEST-GFULL.s421.gml` sample references most of its waypoints with IDs
that don't match the waypoints' own IDs. Its route plan comes out with fewer
waypoints than the file holds, and warnings that name the references that
don't resolve.

[Typed data models](typed-data-models.md) lists each product's root type and
its diagnostic codes.

## Read S-101 features

`S101Dataset` holds the cell's ISO 8211 records as they're encoded, with
numeric type codes and geometry split across separate record tables. To get
features with geometry and named attributes, read them through
`S101VectorSource`:

```csharp
using EncDotNet.S100.Datasets.S101;

var cell = S101Dataset.Open("101AA00DS0019.000");
foreach (var feature in new S101VectorSource(cell).GetFeatures().Take(5))
{
    Console.WriteLine($"{feature.Id} {feature.FeatureType} ({feature.GeometryType}), " +
                      $"{feature.Coordinates.Count} position(s)");
    foreach (var (code, value) in feature.Attributes)
        Console.WriteLine($"  {code} = {value}");
}
```

- `FeatureType` is the feature catalogue code, for example `DepthArea`.
- `Coordinates` are WGS 84 positions.
- `Attributes` values are typed (numbers, Booleans and strings) rather than all
  strings.

To apply update files, open the cell with `S101Dataset.OpenWithUpdates`. To
work with the raw records, use `cell.Document`.

## Read an S-102 bathymetry grid

The HDF5 products are **grids** of values. Open the file with `PureHdfFile`
and read it with the product's reader:

```csharp
using EncDotNet.S100.Datasets.S102;
using EncDotNet.S100.Hdf5.PureHdf;

using var file = PureHdfFile.Open("102US004MI1CI262227.h5");
S102Dataset bathymetry = S102DatasetReader.Read(file);

BathymetryCoverage grid = bathymetry.Coverages[0];
Console.WriteLine($"CRS EPSG:{bathymetry.HorizontalCRS}, " +
                  $"{grid.NumPointsLatitudinal} x {grid.NumPointsLongitudinal} cells");

for (int row = 0; row < grid.NumPointsLatitudinal; row++)
{
    for (int col = 0; col < grid.NumPointsLongitudinal; col++)
    {
        BathymetryValue value = grid.Values[row * grid.NumPointsLongitudinal + col];
        if (value.Depth == S102CoverageSource.FillValue)
            continue;   // no data

        double y = grid.OriginLatitude + row * grid.SpacingLatitudinal;
        double x = grid.OriginLongitude + col * grid.SpacingLongitudinal;
        Console.WriteLine($"({x}, {y}): {value.Depth} m ± {value.Uncertainty} m");
    }
}
```

- `Values` is row-major, starting at the grid origin.
- Depth and uncertainty are in metres. Cells without data hold the fill value
  1,000,000, which is also `S102CoverageSource.FillValue`. Skip them before you
  compute statistics.

> [!WARNING]
> Grid origin and spacing are in the dataset's **horizontal CRS**, given by
> `HorizontalCRS` as an EPSG code, and aren't always degrees. For EPSG:4326
> they're degrees, with `OriginLatitude` as the latitude. Many surveys use a
> projected CRS such as UTM (for example EPSG:32617); then origin and spacing
> are metres, `OriginLatitude` is the northing and `OriginLongitude` the
> easting. To get geographic positions, transform them yourself, for example
> with `EncDotNet.S100.Crs.ProjNet`.

## Read S-104 and S-111 time series

S-104 and S-111 files hold either a regular grid per time step or a series of
values per station. `ReadAny` returns whichever the file contains:

```csharp
using EncDotNet.S100.Datasets.S111;
using EncDotNet.S100.Hdf5.PureHdf;

using var file = PureHdfFile.Open("111US00_DBOFS_20260320T18Z_US4DE1BB.h5");

switch (S111DatasetReader.ReadAny(file))
{
    case S111DatasetData.GriddedCoverage gridded:
        foreach (var step in gridded.Dataset.Coverages.Take(3))
        {
            var valid = step.Values.Where(v => v.Speed >= 0).ToList();
            Console.WriteLine($"{step.TimePoint:u}: {valid.Count} cells, " +
                              $"max {valid.Max(v => v.Speed)} kn");
        }
        break;

    case S111DatasetData.StationSeries stations:
        foreach (var station in stations.Dataset.Stations)
            Console.WriteLine($"{station.Identifier} at {station.Latitude}, {station.Longitude}: " +
                              $"{station.NumberOfTimes} samples from {station.StartTime:u}");
        break;
}
```

For a gridded S-111, each coverage in `Coverages` is one time step, with its
time in `TimePoint` (UTC). Each coverage is laid out like the
[S-102 grid](#read-an-s-102-bathymetry-grid). Each value's `Speed` is in knots
and its `Direction` is in degrees true, the direction the current flows
towards. Negative speeds mark land or missing values; the fill value is
`S111CoverageSource.FillValue` (-9999). Skip them before you compute
statistics.

For a station series, each station has its own position and samples.
`NearestTimeIndex(time)` finds the sample closest to a time.

S-104 works the same way: `S104DatasetReader.ReadAny` returns
`S104DatasetData.GriddedCoverage` or `S104DatasetData.StationSeries`. Its grid
values are `WaterLevelValue`s:

- `Height` is the water level in metres relative to the dataset's vertical
  datum.
- `Trend` is 1 (decreasing), 2 (increasing), 3 (steady) or 0 (unknown).

## Next steps

- [Typed data models](typed-data-models.md): every product's typed root, and
  how to add one.
- [Custom catalogues and validation](catalogues-and-validation.md): validate
  datasets with the product's rules or your own.
- [Loading datasets](loading-datasets.md): open datasets from folders, ZIP
  archives and exchange sets.
- The per-product READMEs, such as
  [`EncDotNet.S100.Datasets.S124`](../src/EncDotNet.S100.Datasets.S124/README.md),
  describe each product's types in detail.

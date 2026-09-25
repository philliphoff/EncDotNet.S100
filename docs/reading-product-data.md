# Reading product data

## Why it matters

The `EncDotNet.S100` facade renders any product and lists its features in a
product-neutral way. When your code needs the data itself, such as a warning's
text, a route's waypoints, a depth at a grid cell, or the current at a given
time, read the dataset with its product's own package. Each
`EncDotNet.S100.Datasets.Sxxx` package parses one product into .NET types that
follow its product specification.

The facade package already references every product package, so these types
are available as soon as you install `EncDotNet.S100`. The HDF5 examples also
use `PureHdfFile` from `EncDotNet.S100.Hdf5.PureHdf`, which the facade brings in
too.

## Quick win

Read an S-124 navigational warning into typed objects:

```csharp
using EncDotNet.S100.Datasets.S124;
using EncDotNet.S100.Datasets.S124.DataModel;

var dataset = S124Dataset.Open("navwarn_mixed.gml");
var warning = S124NavigationalWarning.From(dataset, out var diagnostics);

Console.WriteLine($"{warning.Preamble?.GeneralArea}, {warning.Preamble?.Locality}: {warning.Parts.Count} part(s)");
foreach (var part in warning.Parts)
    Console.WriteLine($"  {part.Id}: {part.GeometryKind}, {part.Coordinates.Count} position(s)");
```

## Deep dive

### Which API for which encoding

| Encoding | Products | Open with | You get |
|---|---|---|---|
| GML (`.gml`) | S-122, S-124, S-125, S-127, S-128, S-129, S-131, S-201, S-411, S-421 | `SxxxDataset.Open(path or stream)` | Features and information types, plus a typed model |
| ISO 8211 (`.000`) | S-101 | `S101Dataset.Open(path or stream)` | The ENC's records; features through `S101VectorSource` |
| HDF5 (`.h5`) | S-102, S-104, S-111 | `SxxxDatasetReader.Read(PureHdfFile.Open(path))` | Coverage grids of values, per time step for S-104 and S-111 |

To get a dataset's product, edition and extent without parsing all of it, use
the static `ReadMetadata` method. It's on the dataset type for GML and S-101,
and on the reader for HDF5:

```csharp
var metadata = S124Dataset.ReadMetadata("navwarn_mixed.gml");
Console.WriteLine($"{metadata.Spec}, extent {metadata.Extent}");
```

### GML products: features and information types

A GML dataset is a set of **features**, which have geometry, and **information
types**, which don't. Every product's feature implements `IS100Feature`, so
geometry is read the same way for all of them:

```csharp
var dataset = S124Dataset.Open("navwarn_mixed.gml");

foreach (var feature in dataset.Features)
{
    Console.WriteLine($"{feature.Id} {feature.FeatureType} ({feature.GeometryType})");
    foreach (var (code, value) in feature.Attributes)
        Console.WriteLine($"  {code} = {value}");
}
```

- `GeometryType` says which geometry property is filled in: `Points` for a
  point or multipoint, `Curves` for curves, `ExteriorRing` and `InteriorRings`
  for a surface.
- Positions are `GeoPosition` values: latitude and longitude in WGS 84 degrees.
  GML stores them as latitude then longitude (S-100 Part 10b); the reader has
  already handled that.
- `Attributes` holds simple attributes as strings, keyed by attribute code.
  Complex attributes (with sub-attributes) are in `ComplexAttributes`, and
  `xlink` references to other features or information types are in
  `References`.

### Typed data models

The attribute dictionaries follow the product's feature catalogue, so reading
them means knowing attribute codes and parsing strings. Every GML product also
has a **typed model**: a read-only projection of the dataset into classes named
after the product's concepts, with parsed values and resolved references. You
create it with the root type's `From` method:

```csharp
using EncDotNet.S100.Datasets.S421;
using EncDotNet.S100.Datasets.S421.DataModel;

var routeDataset = S421Dataset.Open("route.s421.gml");
var plan = S421RoutePlan.From(routeDataset, out var diagnostics);

foreach (var waypoint in plan.Route.Waypoints)
    Console.WriteLine($"{waypoint.WaypointNumber} {waypoint.Name}: {waypoint.Position}");

foreach (var diagnostic in diagnostics)
    Console.WriteLine($"{diagnostic.Severity} {diagnostic.Code}: {diagnostic.Message}");
```

`From` doesn't throw on bad data. A reference that doesn't resolve, an
attribute that doesn't parse, or a feature without geometry becomes a
**diagnostic**, and the projection carries on with what it could read. So always
check `diagnostics`. For example, the S-421 route samples in this repository
(`tests/datasets/S421`) reference their waypoints with IDs that don't match the
waypoints' own IDs; their route plans come out with few or no waypoints and one
`xlink.unresolved` warning per missing reference.

[Typed data models](typed-data-models.md) lists each product's root type and the
diagnostic codes.

### S-101 ENCs

`S101Dataset` holds the cell's ISO 8211 records as they're encoded: numeric type
codes and geometry split across separate record tables. For features with
geometry and named attributes, read them through `S101VectorSource`:

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

`FeatureType` is the feature catalogue code (e.g. `DepthArea`) and
`Coordinates` are WGS 84 positions. `Attributes` values are typed (numbers,
Booleans and strings) rather than all strings. `S101Dataset.OpenWithUpdates`
applies update files, and `cell.Document` exposes the raw records if you need
them.

### HDF5 coverages: S-102 bathymetry

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
        if (value.Depth == 1_000_000f)
            continue;   // no data

        double y = grid.OriginLatitude + row * grid.SpacingLatitudinal;
        double x = grid.OriginLongitude + col * grid.SpacingLongitudinal;
        Console.WriteLine($"({x}, {y}): {value.Depth} m ± {value.Uncertainty} m");
    }
}
```

- `Values` is row-major, starting at the grid origin.
- Depth and uncertainty are in metres. Cells without data hold the fill value
  1,000,000 (also available as `S102CoverageSource.FillValue`).
- Origin and spacing are in the dataset's **horizontal CRS**, `HorizontalCRS`,
  as an EPSG code. For EPSG:4326 they're degrees, with `OriginLatitude` as the
  latitude. Many surveys use a projected CRS such as UTM (for example
  EPSG:32617), and then origin and spacing are metres: `OriginLatitude` is the
  northing and `OriginLongitude` the easting. Transform them yourself if you
  need geographic positions, for example with `EncDotNet.S100.Crs.ProjNet`.

### Time series: S-111 currents and S-104 water levels

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

For a gridded S-111, each coverage in `Coverages` is one time step (`TimePoint`,
UTC) laid out like the S-102 grid above. Each value's `Speed` is in knots and
its `Direction` is in degrees true, the direction the current flows towards.
Negative speeds mark land or missing values. For a station series, each station
has its own position and samples; `NearestTimeIndex(time)` finds the sample
closest to a time.

S-104 works the same way: `S104DatasetReader.ReadAny` returns
`S104DatasetData.GriddedCoverage` or `S104DatasetData.StationSeries`. Its grid
values are `WaterLevelValue`s: `Height` is the water level in metres relative to
the dataset's vertical datum, and `Trend` is 1 (decreasing), 2 (increasing),
3 (steady) or 0 (unknown).

## Troubleshooting

> [!IMPORTANT]
> Check every typed model's `diagnostics`. A projection with warnings still
> returns an object, but it may be missing the entities the warnings name.

> [!WARNING]
> HDF5 grid origin and spacing are in the dataset's own CRS, not always degrees:
> check `HorizontalCRS` before treating them as latitude and longitude.

> [!NOTE]
> Fill values mark cells without data: 1,000,000 for S-102 depth, and negative
> speeds for S-111. Skip them before computing statistics.

## Next step

- [Typed data models](typed-data-models.md) — every product's typed root, and
  the contract for adding one.
- [Loading datasets](loading-datasets.md) — opening datasets from folders, ZIPs
  and exchange sets.
- [Custom catalogues and validation](catalogues-and-validation.md) — validating
  datasets, with the product's rules or your own.
- The per-product READMEs, under **Packages**, describe each product's types
  in detail.

# EncDotNet.S100.Mcp.Tools

`EncDotNet.S100.Mcp.Tools` contains the logic of the S-100 Model Context
Protocol (MCP) tools: listing datasets, describing features, spatial queries,
coverage sampling, session changes and the Library. It has no MCP protocol
code, transport, Avalonia or viewer dependency. Reference it to call the tools
in-process, or to build tools for another host.
[`EncDotNet.S100.Mcp`](../EncDotNet.S100.Mcp/README.md) serves these tools over
MCP.

For what each tool does from an agent's point of view, see the
[MCP server](../../docs/mcp-server.md) guide.

## Install

This package isn't published to NuGet. Add a project reference:

```xml
<ProjectReference Include="path/to/src/EncDotNet.S100.Mcp.Tools/EncDotNet.S100.Mcp.Tools.csproj" />
```

The shared catalog, geometry, time and query types that the tools use, such as
`IDatasetCatalog`, `GeoQuery`, `ToolResult<T>`, `IdentifyFeaturesService` and
`SampleCoverageService`, are in `EncDotNet.S100.Datasets.Pipelines`, in the
`Catalog`, `Geometry`, `Spec`, `Time` and `Query` namespaces. The `s100 identify`
command uses the same services, so its results match the tools'.

## Call a tool

Each tool is a class with one `InvokeAsync` method that takes a request record
and returns `Task<ToolResult<T>>`. Most take an `IDatasetCatalog`, the set of
datasets the tools can see. `FileDatasetCatalog` builds one from dataset files:

```csharp
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Geometry;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Mcp.Tools;

string path = args[0];
string spec = DatasetPipelineFactory.DetectProductSpec(path)
    ?? throw new InvalidOperationException($"Couldn't detect the product of {path}.");

IDatasetCatalog catalog = FileDatasetCatalog.Build(
    [new FileDatasetInput(new DatasetId("cell"), spec, path)]);

// List the loaded datasets.
var listed = await new ListDatasetsTool(catalog).InvokeAsync(new ListDatasetsRequest());
if (listed.TryGetValue(out var summary))
{
    foreach (var dataset in summary.Datasets)
        Console.WriteLine($"{dataset.Id} ({dataset.Spec})");
}

// Identify the features at a point, most specific first.
var picked = await new IdentifyFeaturesTool(catalog).InvokeAsync(
    new IdentifyFeaturesRequest(Latitude: 50.77, Longitude: -1.30, RadiusMeters: 50));
if (picked.TryGetValue(out var pick))
{
    foreach (var match in pick.Features)
        Console.WriteLine($"{match.FeatureType} {match.FeatureId} ({match.Geometry}, {match.Containment})");
}
else if (picked.TryGetError(out var error))
{
    Console.WriteLine($"{error.Code}: {error.Message}");
}

// Find S-101 lights in a box that have a name.
var lights = await new QueryFeaturesTool(catalog).InvokeAsync(new QueryFeaturesRequest(
    new GeoQuery.Box(new GeoBoundingBox(50.7, -1.4, 50.8, -1.2)),
    Spec: new SpecRef("S-101", default),
    FeatureType: "LIGHTS",
    Attributes: [new AttributePredicate("objectName", AttributeOperator.Exists, null)]));
if (lights.TryGetValue(out var page))
{
    foreach (var light in page.Features)
        Console.WriteLine($"{light.FeatureType} {light.FeatureId}");
}
```

`SpecRef("S-101", default)` matches every edition of S-101. Pass a
`SpecVersion` to match one edition.

The tests in
[`tests/EncDotNet.S100.Mcp.Tools.Tests`](https://github.com/philliphoff/EncDotNet.S100/tree/main/tests/EncDotNet.S100.Mcp.Tools.Tests)
call every tool.

## Tools

### Query tools

These read an `IDatasetCatalog` and never change it.

| Class | MCP name | What it does |
|---|---|---|
| `ListDatasetsTool` | `list_datasets` | Lists the loaded datasets, with an optional product and bounding-box filter. |
| `ListSpecsTool` | `list_specs` | Lists the product specifications and what each tool supports for them. |
| `ListTimeStepsTool` | `list_time_steps` | Lists the time steps of a time-varying dataset. |
| `FindAtTool` | `find_at` | Finds the datasets whose declared bounding box contains a point or intersects a `GeoQuery`. It doesn't check per-cell coverage or no-data masks. |
| `IdentifyFeaturesTool` | `identify_features` | Picks the features at a point, ranked point before curve before area, with exact containment for areas and a radius in metres for points and curves. S-101 features include the text of the files their `fileReference` attributes name. |
| `NearestFeaturesTool` | `nearest_features` | Ranks features by true distance to a point, including the nearest point on a segment, with the bearing. An area that contains the point has distance 0. |
| `QueryFeaturesTool` | `query_features` | Finds the features that intersect a `GeoQuery`, with optional product, feature type, time window and attribute filters, and pages the results. Set `Precise` for full-geometry intersection instead of the bounding-box test. |
| `CountFeaturesTool` | `count_features` | Counts features by type, for each dataset. |
| `SearchFeaturesTool` | `search_features` | Finds features by name, in `OBJNAM`, `NOBJNM`, `objectName` and `featureName`. Substring and case-insensitive by default; set `Exact` or `CaseSensitive` to narrow it. |
| `DescribeFeatureTool` | `describe_feature` | Describes one feature, by dataset and feature ID. |
| `DescribeFeatureTypeTool` | `describe_feature_type` | Lists a product's feature types, or one type's attributes and allowed values, from the bundled feature catalogue. It needs no catalog or loaded dataset. |
| `SampleCoverageTool` | `sample_coverage` | Samples an S-102, S-104 or S-111 coverage at a point, at one time or over a time window. |
| `SampleCoverageAlongTool` | `sample_coverage_along` | Samples a coverage at each vertex of a polyline. A vertex with no value has an error instead of failing the whole call. |

The vector tools (`identify_features`, `nearest_features`, `query_features`,
`count_features` and `search_features`) work on every vector product: S-101,
S-401, S-57 (through its S-101 translation) and the GML products. For S-101,
S-401 and S-57, the feature type is the acronym, such as `LIGHTS`, and the
feature ID is the record ID.

`query_features` attribute predicates are ANDed together and compare a
feature's simple attributes, with the attribute code matched without regard to
case. The operators are `Exists`, `NotExists`, `Eq`, `Ne`, `Contains`,
`StartsWith`, `Gt`, `Ge`, `Lt` and `Le`. The numeric operators compare both
sides as numbers. Over MCP, the `attributes` parameter takes either a map of
code to value, which tests equality, or an array of
`{attribute, op, value}` objects.

### Session tools

These are in the `Mutable/` folder. Each takes a capability that the host
supplies, such as `IPresentationController`, `ITimeController`,
`IViewportController`, `IImageRenderer` or `IMutableDatasetCatalog`:
`OpenDatasetTool`, `CloseDatasetTool`, `CloseAllDatasetsTool`,
`SetPaletteTool`, `SetDisplayCategoryTool`, `SetDisplayModeTool`,
`SetTimeStepTool`, `SetViewportTool` and `RenderToImageTool`.
SoundCharts and `s100 mcp serve` both run them. To build them as MCP tools, use
`S100MutableTools` in [`EncDotNet.S100.Mcp`](../EncDotNet.S100.Mcp/README.md#add-host-tools).

### Library tools

These are in the `Library/` folder and read or change a Library through
`ILibraryReader` and `ILibraryEditor`:

- Read: `ListLibrarySourcesTool`, `QueryLibraryItemsTool`,
  `DescribeLibraryItemTool`, `ListKnownSourcesTool`, `ListSecomServicesTool`.
- Change: `AddLibrarySourceTool`, `RefreshLibrarySourceTool`,
  `LibraryActionTool`, `RemoveLibrarySourceTool`,
  `SetLibrarySourceOptionsTool`, `AwaitLibraryIdleTool`,
  `SetSecomIdentityTool`.

`HeadlessLibrary` implements both interfaces over the Library core, for a host
without view models.

### Viewer-only tools

Some tools need the live viewer UI, such as `pick_features`,
`capture_app_screenshot` and the panel, route and render-statistics tools. They
live in SoundCharts, not here. See
[Tools only SoundCharts has](../../docs/mcp-server.md#tools-only-soundcharts-has).

## Results and errors

`ToolResult<T>` holds either a value or a `ToolError`. Use `TryGetValue` and
`TryGetError` to read it. Tools don't throw to report a failure; every failure
is a typed `ToolError` with a stable `Code` and a `Message`.

### Errors

| Code | When |
|---|---|
| `invalid_argument` | A request property failed validation, such as a latitude outside −90 to 90. |
| `geometry_invalid` | A `GeoQuery` shape is invalid, such as an unclosed polygon ring or a bounding box that crosses the antimeridian. |
| `dataset_not_found` | The dataset ID isn't in the catalog. |
| `dataset_closed_during_query` | The dataset was unloaded while the tool read it. You can retry once it's reopened. |
| `dataset_load_failed` | A recognized path produced no dataset, such as an empty or unsupported exchange set. |
| `feature_not_found` | The feature ID isn't in the dataset. |
| `feature_type_not_found` | The feature type isn't in the product's feature catalogue. |
| `feature_catalogue_not_available` | There's no bundled feature catalogue for the product. |
| `spec_not_supported_for_tool` | The tool doesn't support the dataset's product. |
| `not_supported_yet` | The tool supports the product, but not this dataset's shape, such as an S-104 data coding format other than 2. |
| `no_dataset_covers_point` | No loaded dataset's bounds contain the point. |
| `out_of_bounds` | The point is outside every loaded dataset of the requested product. |
| `no_data_at_point` | The grid cell at the point holds the product's no-data value. |
| `time_out_of_range` | The requested time is outside the time range of every dataset that covers the point. |
| `host_not_ready` | A session tool's capability isn't attached yet. You can retry once the host is ready. |
| `library_source_not_found` | No Library collection or source has the ID. |
| `library_item_not_found` | No Library item has the ID. |
| `library_change_rejected` | A Library change can't be done as asked; the message says why. |

## `IDatasetCatalog`

```csharp
public interface IDatasetCatalog
{
    IReadOnlyList<LoadedDataset> Datasets { get; }
    event EventHandler<DatasetCatalogChangedEventArgs>? Changed;
}
```

A host implements `IDatasetCatalog` to expose its loaded datasets.
`FileDatasetCatalog` is a fixed catalog built from files, which `s100 identify`
uses; the viewer has its own live implementation.

- **`Datasets` is a snapshot.** The catalog publishes a new list on every
  change. A tool reads the property once and uses that list for the whole call,
  without locking.
- **`LoadedDatasetData` is a closed set of variants.** Vector products carry
  their data model, such as `S101DatasetData` or `S124DatasetData`. Coverage
  products carry a coverage source, such as `S102CoverageData`, or a station
  series, such as `S104StationSeriesData`. Tools pattern-match on the variant.
- **Coverage handles belong to the host.** A dataset can be unloaded between
  the snapshot and the read. Tools catch `ObjectDisposedException` and return
  `dataset_closed_during_query`. The host must publish the new snapshot before
  it disposes the handle.

## Spatial queries

Tools that take an area take a `GeoQuery`, which has four variants:

| Variant | Holds | Use it for |
|---|---|---|
| `GeoQuery.Point` | `GeoPoint(Latitude, Longitude)` | At a position. |
| `GeoQuery.Box` | `GeoBoundingBox(SouthLatitude, WestLongitude, NorthLatitude, EastLongitude)` | Within a rectangle. |
| `GeoQuery.Polygon` | `GeoPolygon(Ring)`, a closed ring of `GeoPoint` | Inside an area. |
| `GeoQuery.Polyline` | `GeoPolyline(Vertices, CorridorWidthMeters)` | Along a route or line. |

Over MCP, the `query` (or `polyline`) parameter takes the same shapes as a JSON
object. A JSON string that contains the object also works.

```json
{"kind": "point",    "latitude": 47.6, "longitude": -122.3}
{"kind": "box",      "south": 47, "west": -123, "north": 48, "east": -122}
{"kind": "polygon",  "ring": [[47, -123], [48, -123], [48, -122], [47, -123]]}
{"kind": "polyline", "vertices": [[47.6, -122.3], [47.7, -122.4]], "corridorWidthMeters": 1000}
```

- **Coarse matching.** Each variant has a bounding box, from
  `GetBoundingBox()`. `SpatialPredicates.Intersects` tests a polyline one
  segment at a time: a box matches when it touches a segment's box, widened by
  `CorridorWidthMeters` on each side with an equirectangular approximation.
  This matches the precision of the dataset bounding boxes. `Precise` on
  `query_features` adds the exact geometry test.
- **Validation.** `GeoQueryValidator.Validate` returns `null` for a valid
  query, `invalid_argument` for an out-of-range or `NaN` value or a negative
  corridor width, and `geometry_invalid` for a polygon ring that isn't closed
  or has fewer than 4 points, a polyline with fewer than 2 vertices, or an
  inverted or antimeridian-crossing bounding box.
- **Predicates.** `SpatialPredicates` also has `Contains(box, point)` and
  `ContainsPoint(ring, point)`, a ray-cast point-in-polygon test.
- **Point requests.** Requests such as `FindAtRequest` take `Latitude` and
  `Longitude`, and an optional `Query` that replaces them when you set it.

## Feature describers

`describe_feature` describes a feature through a describer for its product,
registered in `FeatureDescriberRegistry`:

| Product | Describer | Feature ID |
|---|---|---|
| S-101, S-401, S-57 | `S101FeatureDescriber` | The record ID (RCID), such as `42`. The result includes the geometry: primitive, bounding box and coordinates. Information associations are followed, and the linked information type's attributes are included. |
| S-102 | `S102FeatureDescriber` | The coverage path `BathymetryCoverage.01`. `BathymetryCoverage` also works. |
| S-104 | `S104FeatureDescriber` | The coverage path `WaterLevel[.NN][.Group_KKK]`, or a station identifier. |
| S-111 | `S111FeatureDescriber` | The coverage path `SurfaceCurrent[.NN][.Group_KKK]`, or a station identifier. |
| S-124 | `S124FeatureDescriber` | The warning feature's `gml:id`. |
| S-129 | `S129FeatureDescriber` | The `gml:id` of the plan metadata, plan area, non-navigable or almost non-navigable area, or control point feature. |
| S-122, S-125, S-127, S-128, S-131, S-201, S-411, S-421 | `GmlFeatureDescriber` | The feature's `gml:id`. |

- For S-102, S-104 and S-111, the result describes the coverage instance:
  origin, spacing, grid size, CRS, bounding box, no-data value, value ranges,
  and the number of time steps and stations. Coverage instances don't reference
  each other, so `References` is always empty.
- `GmlFeatureDescriber` returns the feature's attributes, but its references
  are always an empty list. Each of those products models references with its
  own types, which aren't resolved yet.
- A product without a describer returns `spec_not_supported_for_tool`.

## Field conventions

Every public property that crosses the MCP wire, on requests, results, payload
variants, `ToolError` types and the shared types such as `BoundingBox`,
`SpecRef`, `TimeRange` and `DatasetId`, has a
`[System.ComponentModel.Description]` attribute. Its one sentence states the
units, coordinate reference system and meaning. `AnnotationContractTests` in
`tests/EncDotNet.S100.Mcp.Tools.Tests` fails when a new property doesn't have
one. For the conventions themselves, such as WGS-84 decimal degrees, UTC
ISO-8601 times and depths positive down, see
[Field conventions](../../docs/mcp-server.md#field-conventions).

A `DatasetId` is a plain JSON string in both directions, through
`DatasetIdJsonConverter`, so an ID from one tool's result can go straight into
another tool's request. A wrapped `{"value":"…"}` ID is still accepted as
input.

# EncDotNet.S100.Mcp.Tools

Foundation for a Model Context Protocol (MCP) server that exposes
S-100 datasets to LLM-driven tooling. This project ships **the
abstraction and the tool surface only** — no MCP protocol code, no
viewer code, no transport. PR MCP-2 will layer the wire protocol on
top of this.

## Field conventions

Every public property on every record that crosses the MCP wire —
requests, results, payload variants, `ToolError` subtypes, and the
shared `BoundingBox` / `SpecRef` / `TimeRange` / `DatasetId` types —
carries a `[System.ComponentModel.Description]` attribute with a
single-sentence statement of units, coordinate reference system, and
semantics. The conventions (degrees decimal WGS-84, UTC ISO-8601,
metres, m/s + knots, depths positive down, bearings 0..360 from true
north, lower camelCase JSON keys) are documented in
[`docs/mcp-server.md`](../../docs/mcp-server.md#field-conventions).

A reflection-based test (`AnnotationContractTests` in
`tests/EncDotNet.S100.Mcp.Tools.Tests`) enforces that every newly
added wire-crossing property carries a non-empty `[Description]`.

## Architecture

```
┌────────────────────────────────────────────────┐
│ EncDotNet.S100.Viewer                          │
│  (PR MCP-2: hosts MCP server,                  │
│   implements IDatasetCatalog)                  │
└──────────────────────┬─────────────────────────┘
                       │
┌──────────────────────▼─────────────────────────┐
│ EncDotNet.S100.Mcp                             │
│  (PR MCP-2: MCP server + transports)           │
└──────────────────────┬─────────────────────────┘
                       │
┌──────────────────────▼─────────────────────────┐
│ EncDotNet.S100.Mcp.Tools         ← THIS PROJ   │
│  - IDatasetCatalog + LoadedDataset             │
│  - ToolResult<T> + ToolError                   │
│  - ListDatasetsTool / DescribeFeatureTool /    │
│    SampleCoverageTool                          │
└──────────────────────┬─────────────────────────┘
                       │
┌──────────────────────▼─────────────────────────┐
│ EncDotNet.S100.Core + EncDotNet.S100.Datasets.*│
└────────────────────────────────────────────────┘
```

The project intentionally has **no MCP SDK, no Avalonia, and no viewer
reference**. The same tool surface can therefore be hosted by a CLI, a
headless service, or a different viewer in the future.

## Geometry primitives

Spatial inputs to tools are typed as `GeoQuery`, a discriminated union
over the four shapes the surface needs:

| Variant            | Carries                                | Use case                              |
|--------------------|----------------------------------------|---------------------------------------|
| `GeoQuery.Point`    | `GeoPoint(lat, lon)`                   | "at this position"                    |
| `GeoQuery.Box`      | `GeoBoundingBox(s, w, n, e)`           | "within this rectangle"               |
| `GeoQuery.Polygon`  | `GeoPolygon(closed ring of GeoPoints)` | "inside this area"                    |
| `GeoQuery.Polyline` | `GeoPolyline(vertices, corridorWidthMeters?)` | "along this route / line"     |

Every variant projects to a coarse `GeoBoundingBox` via
`GetBoundingBox()`. Polylines with a non-null `CorridorWidthMeters`
inflate the bbox by an equirectangular metres-to-degrees
approximation; this is suitable for "near this route" coarse filtering
and matches the precision of the underlying dataset bounding boxes.

All inputs are validated with `GeoQueryValidator.Validate(...)`, which
returns:

- `null` on success;
- `InvalidArgument` for a scalar that's out of range (lat/lon, NaN,
  negative corridor width);
- `GeometryInvalid` for a composite-shape failure (unclosed polygon
  ring, polygon with < 4 points, polyline with < 2 vertices, inverted
  bounding box, antimeridian-crossing bounding box).

`SpatialPredicates` exposes the planar primitives every tool reuses:
`Intersects(box, GeoQuery)`, `Contains(box, GeoPoint)`, and
`ContainsPoint(polygonRing, GeoPoint)` (ray-cast).

The legacy `FindAtRequest(Latitude, Longitude, ...)` shape continues
to work; tools that accept a `GeoQuery` carry it as an optional
`Query` property that, when supplied, takes precedence over the
scalar lat/lon fields.

## `IDatasetCatalog`

```csharp
public interface IDatasetCatalog
{
    ImmutableArray<LoadedDataset> Datasets { get; }
    event EventHandler<DatasetCatalogChangedEventArgs>? Changed;
}
```

**Why a property, not a method?** "What is loaded right now" reads more
naturally as state than as an operation. The catalog implementation
publishes a fresh `ImmutableArray<LoadedDataset>` on every change.
Consumers capture the reference once and use it for the duration of an
operation without taking any lock.

**Why `LoadedDatasetData` is a discriminated union.** Each spec
contributes either a typed DataModel (vector products — S-122, S-124,
S-125, S-127, S-128, S-129, S-201, S-411, S-421) or a coverage source
(HDF5-encoded coverage products — S-102, S-104, S-111). Tool code
pattern-matches on the variants:

```csharp
return dataset.Data switch
{
    S124DatasetData s124 => DescribeS124(s124.Model, ...),
    S102CoverageData cov => SampleS102(cov.Source, ...),
    _ => ToolResult<T>.Err(new SpecNotSupportedForTool(dataset.Spec, name)),
};
```

**Best-effort coverage handles.** Coverage variants carry live handles
whose lifetime is owned by the host (e.g. the viewer). A dataset can be
unloaded between the catalog snapshot and the actual read. Tool
implementations wrap reads in `try / catch (ObjectDisposedException)`
and surface `DatasetClosedDuringQuery`. The host is responsible for
publishing the next snapshot before disposing the handle.

## Tool contract

Every tool exposes a single async method that returns
`Task<ToolResult<T>>` where `T` is the result record:

```csharp
public sealed class ListDatasetsTool
{
    public ListDatasetsTool(IDatasetCatalog catalog);
    public Task<ToolResult<ListDatasetsResult>> InvokeAsync(
        ListDatasetsRequest request,
        CancellationToken cancellationToken = default);
}
```

`ToolResult<T>` is a small local discriminated union (`OkResult` /
`ErrResult`) — there is no NuGet dependency on
`OneOf` / `LanguageExt`. Tools never throw into the calling code; every
failure case is reified as a typed `ToolError`. This keeps the eventual
MCP-error wire format flexible while giving in-process callers a typed
surface to match on.

The five error variants implemented in this PR:

| Code                          | When                                                                  |
|-------------------------------|-----------------------------------------------------------------------|
| `dataset_not_found`           | The requested `DatasetId` is not in the snapshot.                     |
| `dataset_closed_during_query` | The coverage handle threw `ObjectDisposedException` mid-read.         |
| `no_dataset_covers_point`     | No loaded dataset's bounds contain the requested lat/lon.             |
| `feature_not_found`           | The named feature is not present in the named dataset.                |
| `spec_not_supported_for_tool` | The tool does not (yet) support the requested spec.                   |
| `invalid_argument`            | A request property failed validation (e.g. latitude / longitude out of WGS-84 range). |
| `geometry_invalid`            | A composite-shape input failed validation (unclosed polygon ring, antimeridian-crossing bbox, etc.). |

## Spec strategy pattern

`DescribeFeatureTool` dispatches per-spec through
`FeatureDescriberRegistry` keyed on `SpecRef.Name`. Each describer
implements an internal `ISpecFeatureDescriber` strategy.

The registry currently wires five describers:

| Spec   | Describer                  | Feature id convention                                                                                       |
|--------|----------------------------|-------------------------------------------------------------------------------------------------------------|
| S-101  | `S101FeatureDescriber`     | Record identifier (RCID), e.g. `42`.                                                                        |
| S-102  | `S102FeatureDescriber`     | Coverage path `BathymetryCoverage[.01]` (bare `BathymetryCoverage` accepted).                               |
| S-104  | `S104FeatureDescriber`     | Coverage path `WaterLevel[.NN][.Group_KKK]` (dcf2 grid / dcf8 station-series), or a bare station identifier. |
| S-111  | `S111FeatureDescriber`     | Coverage path `SurfaceCurrent[.NN][.Group_KKK]` (dcf2 / dcf8), or a bare station identifier.                 |
| S-124  | `S124FeatureDescriber`     | GML `gml:id` of the warning feature.                                                                        |

For the coverage describers (S-102/S-104/S-111) the result returns
instance-level metadata (origin, spacing, grid dimensions, CRS,
bounding box, NoData value, value ranges, time-step counts, station
counts, ...) — coverage instances do not xlink to each other, so
`References` is always empty. Specs without a registered describer
return `SpecNotSupportedForTool`.

## Usage

```csharp
using EncDotNet.S100.Mcp.Tools;
using EncDotNet.S100.Mcp.Tools.Catalog;
using EncDotNet.S100.Mcp.Tools.Geometry;

// A host (e.g. the viewer) implements IDatasetCatalog and publishes
// LoadedDataset instances whenever its loaded set changes.
IDatasetCatalog catalog = host.Catalog;

var list = new ListDatasetsTool(catalog);
var describe = new DescribeFeatureTool(catalog);
var sample = new SampleCoverageTool(catalog);
var sampleAlong = new SampleCoverageAlongTool(catalog);
var findAt = new FindAtTool(catalog);
var queryFeatures = new QueryFeaturesTool(catalog);

var listed = await list.InvokeAsync(new ListDatasetsRequest());
if (listed.TryGetValue(out var summary))
{
    foreach (var ds in summary.Datasets)
    {
        Console.WriteLine($"{ds.Id} ({ds.Spec})");
    }
}

// Which loaded datasets cover this point? (bbox-only — does not check
// per-cell coverage or NoData masks.)
var hits = await findAt.InvokeAsync(new FindAtRequest(
    Latitude: 50.77,
    Longitude: -1.30));
if (hits.TryGetValue(out var hit))
{
    foreach (var ds in hit.Datasets)
    {
        Console.WriteLine($"{ds.Id} ({ds.Spec}) covers the point.");
    }
}

var depth = await sample.InvokeAsync(new SampleCoverageRequest(
    new SpecRef("S-102", new SpecVersion(2, 1, 0)),
    Latitude: 47.6,
    Longitude: -122.3));

if (depth.TryGetValue(out var ok) && ok.Value is DepthSample d)
{
    Console.WriteLine($"Depth at point: {d.DepthMeters} m");
}

// What GML features overlap a bounding box? query_features works across
// every GML-encoded spec (S-122/S-124/S-125/S-127/S-128/S-129/S-131/
// S-201/S-411/S-421) via the shared IGmlFeature abstraction. Pass any
// GeoQuery variant — point, bbox, polygon, or polyline (with optional
// corridor width). Results are paginated.
var features = await queryFeatures.InvokeAsync(new QueryFeaturesRequest(
    new GeoQuery.Box(new GeoBoundingBox(47.5, -122.5, 47.7, -122.2)),
    Spec: new SpecRef("S-124", default),       // any S-124 edition
    FeatureType: "NavwarnPart",
    PageSize: 50));
if (features.TryGetValue(out var page))
{
    foreach (var match in page.Features)
    {
        Console.WriteLine($"{match.Spec} {match.FeatureType} {match.FeatureId}");
    }
}

// Sample a coverage product at every vertex of a polyline. Useful for
// route-level questions like "minimum depth along this leg" or "max
// current speed along this transit". Per-vertex misses (OutOfBounds /
// NoDataAtPoint) surface as null entries rather than aborting the
// whole call, so a partial route still returns usable data.
var route = new GeoPolyline(ImmutableArray.Create(
    new GeoPoint(47.60, -122.35),
    new GeoPoint(47.62, -122.33),
    new GeoPoint(47.64, -122.31)));
var depths = await sampleAlong.InvokeAsync(new SampleCoverageAlongRequest(
    new SpecRef("S-102", new SpecVersion(2, 1, 0)),
    route));
if (depths.TryGetValue(out var series))
{
    foreach (var s in series.Samples)
    {
        var d = s.Result?.Value as DepthSample;
        Console.WriteLine($"v{s.VertexIndex} depth={d?.DepthMeters}m");
    }
}
```

## Out of scope (PR MCP-1)

- MCP protocol / server / transports — PR MCP-2.
- Viewer changes — PR MCP-2.
- Pan/zoom/screenshot tools.
- Search / NL tools.
- Write-back tools.
- Comprehensive xlink reference resolution for backfilled describers
  (S-122/S-125/S-127/S-128/S-129/S-131/S-201/S-411/S-421 return their
  feature attributes via the generic `GmlFeatureDescriber`, but
  references arrive as an empty list pending per-spec resolution).

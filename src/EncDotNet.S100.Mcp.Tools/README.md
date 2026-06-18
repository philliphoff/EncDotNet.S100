# EncDotNet.S100.Mcp.Tools

Foundation for a Model Context Protocol (MCP) server that exposes
S-100 datasets to LLM-driven tooling. This project ships **the
abstraction and the tool surface only** — no MCP protocol code, no
viewer code, no transport. The `EncDotNet.S100.Mcp` library layers the
wire protocol on top of it.

> **Packaging:** this library is not currently published to NuGet.
> Consume it via project reference.

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

## Host-injected tools

The catalog tools above are catalog-only and live in this assembly. A
host (e.g. the Avalonia viewer) may inject additional tools at
runtime via `S100McpServerOptions.AdditionalTools` in
`EncDotNet.S100.Mcp`. The viewer uses this extension point to expose
`render_to_image`, which captures the live map as a PNG — that tool
necessarily depends on Mapsui / Skia and therefore deliberately does
not live here. See `docs/mcp-server.md` for details.

The registry currently wires six describers:

| Spec   | Describer                  | Feature id convention                                                                                       |
|--------|----------------------------|-------------------------------------------------------------------------------------------------------------|
| S-101  | `S101FeatureDescriber`     | Record identifier (RCID), e.g. `42`. Result carries a `geometry` block (primitive, bounding box, resolved coordinates). |
| S-102  | `S102FeatureDescriber`     | Coverage path `BathymetryCoverage[.01]` (bare `BathymetryCoverage` accepted).                               |
| S-104  | `S104FeatureDescriber`     | Coverage path `WaterLevel[.NN][.Group_KKK]` (dcf2 grid / dcf8 station-series), or a bare station identifier. |
| S-111  | `S111FeatureDescriber`     | Coverage path `SurfaceCurrent[.NN][.Group_KKK]` (dcf2 / dcf8), or a bare station identifier.                 |
| S-124  | `S124FeatureDescriber`     | GML `gml:id` of the warning feature.                                                                        |
| S-129  | `S129FeatureDescriber`     | GML `gml:id` of the plan-metadata, plan-area, (almost-)non-navigable-area, or control-point feature.        |

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
var identifyFeatures = new IdentifyFeaturesTool(catalog);
var nearestFeatures = new NearestFeaturesTool(catalog);
var queryFeatures = new QueryFeaturesTool(catalog);
var countFeatures = new CountFeaturesTool(catalog);
var searchFeatures = new SearchFeaturesTool(catalog);

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

// Which *features* are under this point? identify_features is the
// feature-aware cursor-pick: it ranks matches most-specific first
// (point before curve before area; smaller / nearer wins), uses exact
// point-in-polygon containment for areas, and a metre radius for
// point / curve features. Works across every vector spec incl. S-101.
var picked = await identifyFeatures.InvokeAsync(new IdentifyFeaturesRequest(
    Latitude: 50.77,
    Longitude: -1.30,
    RadiusMeters: 50));
if (picked.TryGetValue(out var pick))
{
    foreach (var m in pick.Features)
    {
        Console.WriteLine($"{m.Spec} {m.FeatureType} {m.FeatureId} ({m.Geometry}, {m.Containment}).");
    }
}

// What is the nearest feature to my position, and am I inside any area?
// nearest_features ranks by TRUE geometric distance (nearest point on a
// segment, not just the nearest vertex). An area containing the point is
// returned at distance 0 with containment "inside"; everything else
// reports the distance and the bearing toward its nearest point.
var nearest = await nearestFeatures.InvokeAsync(new NearestFeaturesRequest(
    Latitude: 50.77,
    Longitude: -1.30,
    FeatureType: null,
    MaxDistanceMeters: 5000,
    Limit: 5));
if (nearest.TryGetValue(out var near))
{
    foreach (var m in near.Features)
    {
        Console.WriteLine($"{m.FeatureType} {m.FeatureId}: {m.DistanceMeters:F0} m ({m.Containment}).");
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

// What features overlap a bounding box? query_features works across
// every GML-encoded spec (S-122/S-124/S-125/S-127/S-128/S-129/S-131/
// S-201/S-411/S-421) via the shared IS100Feature abstraction, plus the
// ISO 8211-encoded S-101 (whose pipeline Feature records implement
// IS100Feature directly — its FeatureType filter matches the
// feature-type acronym and FeatureId is the decimal RCID). Pass any
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

// Filter on attribute values with the optional Attributes predicate set.
// Predicates combine with logical AND and are evaluated against each
// feature's simple attributes (case-insensitive key lookup). Operators:
// Exists, NotExists, Eq, Ne, Contains, StartsWith, Gt, Ge, Lt, Le
// (numeric operators parse both sides as invariant doubles). Over the
// wire the `attributes` parameter accepts either a code→value map
// (all equality) or an array of explicit {attribute, op, value} objects.
var deepLights = await queryFeatures.InvokeAsync(new QueryFeaturesRequest(
    new GeoQuery.Box(new GeoBoundingBox(47.5, -122.5, 47.7, -122.2)),
    Spec: new SpecRef("S-101", default),
    FeatureType: "LIGHTS",
    Attributes: ImmutableArray.Create(
        new AttributePredicate("categoryOfLight", AttributeOperator.Eq, "8"),
        new AttributePredicate("objectName", AttributeOperator.Exists, null))));

// Set Precise for true full-geometry intersection instead of the default
// bounding-box test: point-in-polygon containment for areas (interior-ring
// holes honoured) and genuine segment crossing — e.g. "which features does
// this route leg actually cross?". A leg endpoint inside an area, or a leg
// that crosses an area boundary or a curve, counts.
var crossed = await queryFeatures.InvokeAsync(new QueryFeaturesRequest(
    new GeoQuery.Polyline(new GeoPolyline(ImmutableArray.Create(
        new GeoPoint(47.60, -122.40),
        new GeoPoint(47.62, -122.30)))),
    Precise: true));

// What kinds of features, and how many, are in a cell? count_features
// answers the discovery question describe_feature can't (it needs an id
// you don't yet have). Works across every vector spec incl. S-101.
// Optionally scope to one dataset / spec / spatial envelope.
var counts = await countFeatures.InvokeAsync(new CountFeaturesRequest(
    Spec: new SpecRef("S-101", default)));
if (counts.TryGetValue(out var tally))
{
    foreach (var t in tally.Types)
    {
        Console.WriteLine($"{t.DatasetId} {t.FeatureType}: {t.Count} ({t.WithGeometry} located)");
    }
}

// Where is the feature called "X"? search_features answers the
// name-oriented question that query_features (geometry-first) and
// describe_feature (needs an id) can't. It searches every place a name
// can live — the simple OBJNAM / NOBJNM / objectName attributes (incl.
// S-101) and the complex featureName.name / .displayName sub-attributes
// (GML specs). Case-insensitive substring by default; set Exact for
// whole-name equality, CaseSensitive for an exact-case match. Optional
// spec / dataset / spatial scope.
var byName = await searchFeatures.InvokeAsync(new SearchFeaturesRequest(
    "Nab Tower",
    Spec: new SpecRef("S-101", default)));
if (byName.TryGetValue(out var hits))
{
    foreach (var hit in hits.Features)
    {
        Console.WriteLine($"{hit.FeatureType} {hit.FeatureId}: {hit.MatchedName} (via {hit.MatchedAttribute})");
    }
}

// What attributes is a feature type allowed to have, and what are the
// legal values of its enumerations? describe_feature_type introspects a
// spec's bundled Feature Catalogue (no loaded dataset required) — the
// schema-discovery counterpart to count_features. Omit FeatureType to
// list every type; supply one for full attribute detail.
var schema = new DescribeFeatureTypeTool();
var buoy = await schema.InvokeAsync(new DescribeFeatureTypeRequest(
    new SpecRef("S-101", default),
    FeatureType: "BuoyLateral"));
if (buoy.TryGetValue(out var typeInfo))
{
    foreach (var attr in typeInfo.FeatureTypes[0].Attributes)
    {
        var card = attr.Mandatory ? "required" : "optional";
        Console.WriteLine($"{attr.Code} ({attr.ValueType}, {card}): {attr.ListedValues.Length} listed values");
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
  (S-122/S-125/S-127/S-128/S-131/S-201/S-411/S-421 return their
  feature attributes via the generic `GmlFeatureDescriber`, but
  references arrive as an empty list pending per-spec resolution).

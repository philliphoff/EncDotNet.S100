# EncDotNet.S100.Datasets.S129.Fusion

`EncDotNet.S100.Datasets.S129.Fusion` combines an IHO S-129 under-keel clearance
(UKC) plan with the datasets it refers to: S-102 bathymetry, S-104 water levels
and the S-421 route. It steps through the plan's control points in time,
resolves the plan's references to other products, samples S-102 and S-104 at
control points, and matches control points to route waypoints and legs.
Reference it when you need to read a UKC plan together with those datasets. It
builds on the typed models in `EncDotNet.S100.Datasets.S129`,
`EncDotNet.S100.Datasets.S102`, `EncDotNet.S100.Datasets.S104` and
`EncDotNet.S100.Datasets.S421`, and doesn't change them.

## Install

This package isn't published to NuGet. Add a project reference to it from a
clone of the repository:

```bash
dotnet add reference path/to/EncDotNet.S100/src/EncDotNet.S100.Datasets.S129.Fusion/EncDotNet.S100.Datasets.S129.Fusion.csproj
```

It depends on `EncDotNet.S100.Datasets.S129`, `EncDotNet.S100.Datasets.S102`,
`EncDotNet.S100.Datasets.S104` and `EncDotNet.S100.Datasets.S421`. To open
S-102 and S-104 files, you also need an HDF5 reader such as
`EncDotNet.S100.Hdf5.PureHdf`.

## Combine a plan with its datasets

This example opens a plan, a bathymetry file and a route, resolves the plan's
references, samples the bathymetry at each control point, and binds the control
points to the route:

```csharp
using EncDotNet.S100.Datasets.S102;
using EncDotNet.S100.Datasets.S129;
using EncDotNet.S100.Datasets.S129.DataModel;
using EncDotNet.S100.Datasets.S129.Fusion;
using EncDotNet.S100.Datasets.S129.Fusion.Routing;
using EncDotNet.S100.Datasets.S421;
using EncDotNet.S100.Datasets.S421.DataModel;
using EncDotNet.S100.Hdf5.PureHdf;

var plan = S129UnderKeelClearancePlan.From(S129Dataset.Open("path/to/ukc-plan.gml"), out _);

using var bathymetryFile = PureHdfFile.Open("path/to/bathymetry.h5");
var bathymetry = S102DatasetReader.Read(bathymetryFile);
var route = S421RoutePlan.From(S421Dataset.Open("path/to/route.gml"), out _).Route;

// Resolve the plan's textual references against the datasets you have.
var resolved = S129CrossProductResolver.Resolve(plan, bathymetry: bathymetry, route: route);
foreach (var unresolved in resolved.Unresolved)
    Console.WriteLine($"Unresolved: {unresolved.ExpectedKind} ({unresolved.Reason})");

// Sample the bathymetry at each control point.
var bathymetrySource = new S102CoverageSource(bathymetry);
foreach (var cp in plan.ControlPoints)
{
    var depth = S129PlanFusion.SampleBathymetryAt(cp, bathymetrySource);
    Console.WriteLine($"{cp.Id}: UKC margin {cp.DistanceAboveUkcLimit:F2} m, depth {depth?.Depth:F1} m");
}

// Match each control point to a route waypoint or leg.
var binding = S129RouteBinder.Bind(plan, route);
```

The sections below cover each part.

## Step through the plan in time

`S129TimelineView`, in the `EncDotNet.S100.Datasets.S129.Fusion.Timeline`
namespace, lists the distinct expected passing times of the plan's control
points in order, and returns the UKC state at any time:

```csharp
using EncDotNet.S100.Datasets.S129.Fusion.Timeline;

var view = new S129TimelineView(plan);
foreach (var snapshot in view.EnumerateTimeline())
    Console.WriteLine($"{snapshot.Time:o}  UKC margin {snapshot.ControlPoint.DistanceAboveUkcLimit:F2} m");

// A time between control points uses the default mode, NearestEarlier.
var now = view.GetSnapshotAt(DateTimeOffset.UtcNow);
```

`S129TimelineSamplingMode` sets how a time between control points is resolved:

| Mode | Returns |
|---|---|
| `NearestEarlier` (default) | The latest sample at or before the time; `null` before the first sample. |
| `NearestLater` | The earliest sample at or after the time; `null` after the last sample. |
| `Nearest` | The closest sample; a tie returns the earlier one. |
| `Exact` | Only a sample at exactly that time; otherwise `null`. |

The view doesn't interpolate UKC values between control points. Interpolating
across gaps the producer left would change the meaning of S-129's
`UnderKeelClearanceControlPoint`.

## Resolve references to other products

In S-129 Edition 2.0.0, the links to the source S-421 route, S-102 bathymetry
and S-104 water levels are text identifiers, kept on the typed plan as
`S129ExternalReference` values.
`S129CrossProductResolver.Resolve(plan, bathymetry, waterLevel, route)` matches
them against the datasets you pass. Each match is an
`S129ResolvedReference<T>`; each reference it can't match is an
`S129UnresolvedReference` with a reason. All dataset arguments are optional,
and `Resolve` doesn't throw on a mismatch.

```csharp
var resolved = S129CrossProductResolver.Resolve(plan, route: route);
if (resolved.Route is { } r)
    Console.WriteLine($"Matched route {r.Value.RouteId} edition {r.Value.EditionNumber}");
foreach (var u in resolved.Unresolved)
    Console.WriteLine($"  unresolved: {u.ExpectedKind} ({u.Reason})");
```

## Sample S-102 and S-104 at control points

`S129BathymetryFusion` and `S129WaterLevelFusion` sample a coverage at a
control point's position. Water levels are sampled at a time as well:

```csharp
using EncDotNet.S100.Datasets.S104;

var bathymetrySource = new S102CoverageSource(bathymetry);
var depth = S129BathymetryFusion.Sample(bathymetrySource, controlPoint.Position!.Value);

var waterLevelSource = new S104CoverageSource(waterLevel);
var level = S129WaterLevelFusion.Sample(
    waterLevelSource, controlPoint.Position!.Value, controlPoint.ExpectedPassingTime!.Value);
```

`S129PlanFusion` takes the control point directly. `SampleWaterLevelAt` uses the
control point's `ExpectedPassingTime` unless you pass a time:

```csharp
var depth = S129PlanFusion.SampleBathymetryAt(controlPoint, bathymetrySource);
var level = S129PlanFusion.SampleWaterLevelAt(controlPoint, waterLevelSource);
```

Sampling uses the nearest cell in space and the nearest time step in time; it
doesn't interpolate.

## Match control points to a route

`S129RouteBinder.Bind(plan, route, options)`, in the
`EncDotNet.S100.Datasets.S129.Fusion.Routing` namespace, matches each control
point to the route by distance:

```csharp
var binding = S129RouteBinder.Bind(plan, route);
foreach (var (cp, mapping) in binding.Mappings)
{
    var label = mapping.Kind switch
    {
        S129RouteMappingKind.OnWaypoint => $"at waypoint {mapping.Waypoint!.Id} ({mapping.DistanceMeters:F1} m)",
        S129RouteMappingKind.OnLeg => $"along leg {mapping.Leg!.Id}, fraction {mapping.LegPositionFraction:F2}",
        _ => "unmapped",
    };
    Console.WriteLine($"{cp.Id}: {label}");
}
```

For each control point, the binder:

1. Finds the closest waypoint within `WaypointToleranceMeters` (default 200 m).
2. If there's none, finds the closest point on a leg within
   `LegToleranceMeters` (default 100 m).
3. If there's none, marks the control point `Unmapped`.

Set the tolerances with `S129RouteBindingOptions`. Distances are great-circle
distances (haversine formula).

## Scope

This package reads and combines data. It doesn't:

- change any portrayal pipeline or produce drawing instructions, XSLT, Lua
  rules or other portrayal output;
- depend on a renderer;
- add anything to the viewer, such as panels, timeline controls or toolbars;
- add a cache beyond the ones the coverage sources and typed models already
  have;
- include a command-line tool or sample;
- change the typed models in `EncDotNet.S100.Datasets.S129`.

## See also

- [`EncDotNet.S100.Datasets.S129`](../EncDotNet.S100.Datasets.S129/README.md):
  read S-129 datasets and the typed UKC plan.
- [Reading product data](../../docs/reading-product-data.md): read S-102
  coverages, S-104 time series and S-421 routes.
- [Typed data models](../../docs/typed-data-models.md): the typed
  root for each product and the shared diagnostic codes.

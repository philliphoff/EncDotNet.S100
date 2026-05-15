# EncDotNet.S100.Datasets.S129.Fusion

Cross-product data-layer helpers for IHO S-129 Under Keel Clearance
Management plans. Fuses an `S129UnderKeelClearancePlan` with the
strongly-typed datasets it references (S-102 bathymetry, S-104 water
level, S-421 route) and surfaces a time-indexed view over the plan
itself.

This library is **purely additive** on top of the existing typed
projections in `EncDotNet.S100.Datasets.S129`,
`EncDotNet.S100.Datasets.S102`, `EncDotNet.S100.Datasets.S104`, and
`EncDotNet.S100.Datasets.S421`. It does not modify them.

## Three capabilities

### 1. Timeline (`Timeline/`)

`S129TimelineView` exposes the ordered sequence of distinct control-point
expected-passing-times in an S-129 plan and lets callers query the UKC
state at an arbitrary time:

```csharp
var view = new S129TimelineView(plan);
foreach (var snap in view.EnumerateTimeline())
    Console.WriteLine($"{snap.Time:o}  UKC margin = {snap.ControlPoint.DistanceAboveUkcLimit:F2} m");

// Between-grid lookup with the default semantic:
var nowSnap = view.GetSnapshotAt(DateTimeOffset.UtcNow); // NearestEarlier
```

`S129TimelineSamplingMode` chooses how off-grid times are resolved:

| Mode | Behaviour |
|---|---|
| `NearestEarlier` *(default)* | greatest sample time ≤ `t`; null if before the first sample |
| `NearestLater` | least sample time ≥ `t`; null if after the last sample |
| `Nearest` | absolute closest sample time; ties resolve to the earlier sample |
| `Exact` | only an exact-match sample; null otherwise |

The library deliberately does **not** interpolate between control-point
UKC values — interpolation across explicit producer gaps would change
the semantics of S-129 §`UnderKeelClearanceControlPoint`. If
interpolation is needed it can be layered on top later.

### 2. Cross-product resolution + S-102 / S-104 fusion (`Fusion/`)

In S-129 Edition 2.0.0 the links to the source S-421 route, S-102
bathymetry, and S-104 water level are *textual* producer identifiers,
preserved verbatim on the typed plan as `S129ExternalReference` values.
`S129CrossProductResolver.Resolve(plan, bathymetry?, waterLevel?, route?)`
turns those textual handles into typed `S129ResolvedReference<T>` values
when matching datasets are supplied, or `S129UnresolvedReference`
entries with a reason when they are not. Resolution is best-effort and
never throws.

```csharp
var resolved = S129CrossProductResolver.Resolve(plan, route: openRoute);
if (resolved.Route is { } r)
    Console.WriteLine($"Matched route {r.Value.RouteId} edition {r.Value.EditionNumber}");
foreach (var u in resolved.Unresolved)
    Console.WriteLine($"  unresolved: {u.ExpectedKind} ({u.Reason})");
```

Once a coverage is resolved, the static fusion helpers sample it at a
control point's geographic position:

```csharp
var bathy = new S102CoverageSource(openBathymetry);
var depth = S129BathymetryFusion.Sample(bathy, controlPoint.Position!.Value);

var wl = new S104CoverageSource(openWaterLevel);
var level = S129WaterLevelFusion.Sample(wl, controlPoint.Position!.Value, controlPoint.ExpectedPassingTime!.Value);
```

`S129PlanFusion` adds CP-aware overloads:

```csharp
var depth  = S129PlanFusion.SampleBathymetryAt(cp, bathy);
var level  = S129PlanFusion.SampleWaterLevelAt(cp, wl);   // uses cp.ExpectedPassingTime
```

Sampling is nearest-cell in space and nearest-time-slice in time — no
interpolation. The helpers never produce drawing instructions and never
depend on a rendering library.

### 3. S-421 route binding (`Routing/`)

`S129RouteBinder.Bind(plan, route, options?)` correlates each control
point with the supplied route by spatial proximity:

```csharp
var binding = S129RouteBinder.Bind(plan, route);
foreach (var (cp, mapping) in binding.Mappings)
{
    var label = mapping.Kind switch
    {
        S129RouteMappingKind.OnWaypoint => $"@ WP {mapping.Waypoint!.Id} ({mapping.DistanceMeters:F1} m)",
        S129RouteMappingKind.OnLeg      => $"along {mapping.Leg!.Id} t={mapping.LegPositionFraction:F2}",
        _                               => "unmapped",
    };
    Console.WriteLine($"{cp.Id}: {label}");
}
```

For each control point the binder first tests every waypoint and keeps
the closest within `WaypointToleranceMeters` (default **200 m**); if
none qualifies it falls back to the closest projection onto a leg's
polyline within `LegToleranceMeters` (default **100 m**); otherwise the
control point is marked `Unmapped`. Distances use the haversine
great-circle formula.

## End-to-end example

```csharp
// Open the typed plan + auxiliary datasets.
var rawPlan = S129Dataset.Open("12900MCTDS130TS.gml");
var plan    = S129UnderKeelClearancePlan.From(rawPlan, out _);
var bathy   = new S102CoverageSource(S102DatasetReader.Read("bathy.h5"));
var route   = ... ; // S421RoutePlan.From(...).Routes[0]

// Resolve cross-product references.
var resolved = S129CrossProductResolver.Resolve(plan, bathymetry: bathyDs, route: route);

// Sample fused bathymetry at each CP.
foreach (var cp in plan.ControlPoints)
{
    var depth = S129PlanFusion.SampleBathymetryAt(cp, bathy);
    Console.WriteLine($"{cp.Id} UKC margin {cp.DistanceAboveUkcLimit:F2} m  depth {depth?.Depth:F1} m");
}

// Correlate CPs with the route.
var binding = S129RouteBinder.Bind(plan, route);
```

## Explicit non-goals

This library is the **data-access half** of S-129 Tier 1. It does not:

- Modify any portrayal pipeline.
- Produce drawing instructions, XSLT, Lua rules, or any portrayal output.
- Touch `MapsuiDisplayListRenderer`, `SkiaCoverageRenderer`, or any other renderer.
- Add viewer UI (panels, timeline sliders, toolbars).
- Introduce a new caching layer beyond what the underlying coverage / projection caches already do.
- Provide a CLI tool or sample executable.
- Change the existing typed `DataModel` shapes from PR #74 / PR #76.

The visualisation half (drawing fused output, timeline scrub UI, route
overlay) is a separate, larger problem that lives in subsequent PRs.

## Installation

```sh
dotnet add package EncDotNet.S100.Datasets.S129.Fusion
```

Depends on `EncDotNet.S100.Datasets.S129`, `EncDotNet.S100.Datasets.S102`,
`EncDotNet.S100.Datasets.S104`, and `EncDotNet.S100.Datasets.S421`.

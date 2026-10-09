# EncDotNet.S100.Datasets.S421

`EncDotNet.S100.Datasets.S421` reads [IHO S-421](https://iho.int/) Route Plan
datasets: GML files (S-100 Part 10b) that hold a ship's route plan, with
waypoints, legs, schedules and action points. It parses a dataset, resolves the
`xlink:href` references between its objects, projects it into a typed route,
validates it, and loads the S-421 portrayal catalogue. Reference it when you
need typed access to an S-421 route or its validation rules. To open and render
any product, including S-421, use the
[`EncDotNet.S100`](../EncDotNet.S100/README.md) package, which includes this
one.

## Install

```bash
dotnet add package EncDotNet.S100.Datasets.S421
```

## Read a dataset

Open the dataset, then project it into the typed route plan:

```csharp
using EncDotNet.S100.Datasets.S421;
using EncDotNet.S100.Datasets.S421.DataModel;

var dataset = S421Dataset.Open("path/to/route.gml");
var plan = S421RoutePlan.From(dataset, out var diagnostics);

Console.WriteLine($"Route {plan.Route.RouteId} edition {plan.Route.EditionNumber}");
Console.WriteLine($"  Author: {plan.Route.Info?.Author}");
Console.WriteLine($"  Vessel: {plan.Route.Info?.Vessel?.Name} (MMSI {plan.Route.Info?.Vessel?.Mmsi})");

foreach (var wp in plan.Route.Waypoints)
{
    var leg = wp.OutgoingLeg;
    Console.WriteLine(
        $"  WP{wp.WaypointNumber} {wp.Position.Latitude:F4}, {wp.Position.Longitude:F4}"
        + (leg is not null ? $", leg {leg.Id}" : ""));
}

// Walk the route through the typed leg endpoints.
var cursor = plan.Route.Waypoints.FirstOrDefault();
while (cursor?.OutgoingLeg is { } next)
{
    Console.WriteLine($"  leg {next.Id}: {next.StartWaypoint?.Id} to {next.EndWaypoint?.Id}");
    cursor = next.EndWaypoint;
}

foreach (var diagnostic in diagnostics)
    Console.WriteLine(diagnostic);
```

To read the features without the typed model, use the dataset directly:

```csharp
using EncDotNet.S100.Datasets.S421;

var dataset = S421Dataset.Open("path/to/route.gml");

Console.WriteLine($"Dataset: {dataset.DatasetIdentifier}");
Console.WriteLine($"Features: {dataset.Features.Count}");
Console.WriteLine($"Information types: {dataset.InformationTypes.Count}");

foreach (var wp in dataset.Features.Where(f => f.FeatureType == "RouteWaypoint"))
{
    var (lat, lon) = wp.Points[0];
    Console.WriteLine($"  Waypoint {wp.Attributes["routeWaypointID"]}: {lat}, {lon}");
}
```

## Main types

- **`S421Dataset`**: the parsed dataset, with its features, information types
  and dataset identification. `Open` takes a path or a stream.
  `ReadMetadata` (static, for a path or stream, or on an open dataset) returns
  the declared product specification and the WGS 84 extent of the feature
  geometry without running portrayal. The extent is `null` when no feature has
  geometry.
- **`S421Feature`**: a feature with its type code, optional geometry, simple
  attributes, complex attributes and `xlink` references.
  **`S421ComplexAttribute`** holds a complex attribute's sub-attributes. The
  geometry kind is the shared `S100GeometryType` enum from
  `EncDotNet.S100.Core` (`None`, `Point`, `Curve`, `Surface`).
- **`S421InformationType`**: an information type without geometry, such as
  `RouteInfo`.
- **`GmlReference`**: an `xlink:href` reference from one object to another. It's
  in the `EncDotNet.S100.Features` namespace, in the `EncDotNet.S100.Core`
  package.
- **`S421RoutePlan`**: the typed model. See
  [Typed data model](#typed-data-model).
- **`S421RoutePlanRules`**: the validation rule set. See [Validate](#validate).
- **`S421FeatureXmlSource`**: an `IFeatureXmlSource` that converts an
  `S421Dataset` to S-100 Part 9 FeatureXML for the XSLT rules.
- **`S421PortrayalCatalogue`**: an `IVectorPortrayalCatalogue` that loads the
  catalogue's XSLT rules, symbols, line styles, area fills and colour palettes.

The reader extracts the route features (`Route`, `RouteWaypoints`,
`RouteWaypoint`, `RouteSchedules`, `RouteSchedule`, `RouteWaypointLeg`,
`RouteActionPoints`, `RouteActionPoint`) and information types such as
`RouteInfo`. It keeps the `xlink:href` references between objects, such as a
`Route` to its `RouteWaypoints` collection and a `RouteWaypoints` to each
`RouteWaypoint`.

## Typed data model

`S421Dataset` is a feature bag: attributes are strings keyed by code, and
references are unresolved. The `EncDotNet.S100.Datasets.S421.DataModel`
namespace projects it into typed objects:

- **`S421RoutePlan.From(dataset, out diagnostics)`** builds the typed graph,
  rooted at `S421Route`. It resolves `xlink:href` references and parses values
  (`int`, `double`, `bool`, `DateTimeOffset`).
- **`S421Route`** has `Info`, `Waypoints`, `Legs`, `ActionPoints` and
  `Schedules`.
- **`S421Waypoint`** has `OutgoingLeg` and `IncomingLeg`, so you can move along
  the route in either direction.
- **`S421Leg`** has `StartWaypoint` and `EndWaypoint`, so from a leg you can
  reach both its waypoints without matching coordinates.
- **`S421ActionPoint`**, **`S421Schedule`** (manual, calculated or
  recommended) and **`S421ScheduleElement`** cover the rest of the plan.

Each object keeps attributes the model doesn't read in its `ExtraAttributes`
dictionary, so extension and later-edition attributes aren't lost. Unresolved
references and date-times that don't parse become `ProjectionDiagnostic`
entries (from `EncDotNet.S100.DataModel`) instead of exceptions. `From` throws
only when the dataset has no `Route` feature.

The typed model uses the shared `ProjectionDiagnostic`, `DiagnosticSeverity`,
`GeoPosition`, `XlinkResolver`, `AttributeParser` and `ExtraAttributes` types
from the `EncDotNet.S100.DataModel` namespace in `EncDotNet.S100.Core`.

## Validate

`S421RoutePlanRules`, in the `EncDotNet.S100.Datasets.S421.Validation`
namespace, is the default rule set for `S421RoutePlan`. Rule IDs have the form
`S421-R-{clause}` and trace to the S-421 specification (IEC 63173-1). The
`EncDotNet.S100` package's `dataset.Validate()` runs the same rule set.

```csharp
using EncDotNet.S100.Datasets.S421;
using EncDotNet.S100.Datasets.S421.DataModel;
using EncDotNet.S100.Datasets.S421.Validation;

var dataset = S421Dataset.Open("path/to/route.gml");
var plan = S421RoutePlan.From(dataset, out _);
var report = S421RoutePlanRules.Validate(plan);

foreach (var finding in report.Findings)
    Console.WriteLine($"[{finding.Severity}] {finding.RuleId}: {finding.Message}");
```

| Rule ID | Severity | Checks |
|---|---|---|
| `S421-R-3.1` | Error | The route has at least two waypoints. |
| `S421-R-3.2` | Error | Consecutive waypoints aren't at the same position (no zero-length leg). |
| `S421-R-4.1` | Error | Waypoint positions are within the WGS 84 latitude and longitude ranges (S-100 Part 10b §6.2). |
| `S421-R-5.1` | Warning | Leg planned speed over ground, when present, is non-negative, with the minimum no greater than the maximum. |
| `S421-R-6.1` | Error | The route edition number, when present, is at least 1. |
| `S421-R-7.1` | Warning | Each `RouteActionPoint` has at least one coordinate. |

The rules check one dataset at a time. Cross-dataset checks, such as comparing
waypoints with charted depths in an S-102 coverage, aren't included. A rule that
needs other datasets would reach them through `ValidationContext.Services`.

## Portrayal

The S-421 dataset processor in `EncDotNet.S100.Datasets.Pipelines` runs the
bundled S-421 portrayal catalogue's XSLT rules over the FeatureXML that
`S421FeatureXmlSource` produces.

## See also

- [Loading datasets](../../docs/loading-datasets.md): open files, folders, ZIPs
  and exchange sets through the `EncDotNet.S100` package.
- [Reading product data](../../docs/reading-product-data.md): features,
  information types and typed models for each product.
- [Typed data models](../../docs/typed-data-models.md): the typed
  root for each product and the shared diagnostic codes.
- [Custom catalogues and validation](../../docs/catalogues-and-validation.md):
  run the bundled rules and add your own.
- [`EncDotNet.S100.Datasets.S129.Fusion`](../EncDotNet.S100.Datasets.S129.Fusion/README.md):
  match an S-129 under-keel clearance plan's control points to an S-421 route.

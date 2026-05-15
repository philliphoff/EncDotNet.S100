# EncDotNet.S100.Datasets.S421

Library for reading and portraying [IHO S-421](https://iho.int/) (Route Plan) datasets.

S-421 defines a standard exchange format for ship route plans — waypoints, leg
information, schedules, and action points — encoded as GML conforming to the
S-100 framework.

## Features

- Parse S-421 GML datasets (S-100 Part 10b encoding)
- Extract route features (`Route`, `RouteWaypoints`, `RouteWaypoint`,
  `RouteSchedules`, `RouteSchedule`, `RouteWaypointLeg`, `RouteActionPoints`,
  `RouteActionPoint`)
- Extract information types (`RouteInfo`, etc.)
- Resolve `xlink:href` cross-references between objects (e.g. a `Route` to its
  `RouteWaypoints` collection, a `RouteWaypoints` to its individual `RouteWaypoint`s)
- Convert to S-100 Part 9 FeatureXML for portrayal pipeline consumption
- XSLT-based portrayal via the bundled S-421 Portrayal Catalogue

## Overview

Key types include:

- **`S421Dataset`** — root model containing parsed features, information types, and dataset identification.
- **`S421Feature`** — a feature with type code, optional geometry, simple attributes, complex attributes, and `xlink` references.
- **`S421InformationType`** — a non-spatial information type instance (e.g. `RouteInfo`).
- **`S421Reference`** — an `xlink:href` reference from one object to another.
- **`S421ComplexAttribute`** — a complex attribute instance containing sub-attribute values.
- **`GmlGeometryType`** — shared enum (from `EncDotNet.S100.Core`) describing the geometry primitive type of a feature (`None`, `Point`, `Curve`, `Surface`).
- **`S421FeatureXmlSource`** — `IFeatureXmlSource` adapter that projects an `S421Dataset` into S-100 Part 9 FeatureXML for XSLT portrayal rules.
- **`S421PortrayalCatalogue`** — `IVectorPortrayalCatalogue` implementation that loads XSLT rules, symbols, line styles, area fills, and color palettes.

### Strongly-typed data model

The types above expose the dataset as a feature bag — schema-agnostic, well
suited for the portrayal pipeline, but inconvenient when client code wants to
inspect routes, waypoints, or schedules directly. The
`EncDotNet.S100.Datasets.S421.DataModel` namespace layers a strongly-typed
projection on top:

- **`S421RoutePlan.From(dataset, out diagnostics)`** — projects an
  `S421Dataset` into a typed object graph rooted at `S421Route`, resolving
  `xlink:href` cross-references and parsing primitive values
  (`int`, `double`, `bool`, `DateTimeOffset`).
- **`S421Route`** with `Info`, `Waypoints`, `Legs`, `ActionPoints`, `Schedules`.
- **`S421Waypoint`** with typed `OutgoingLeg` and `IncomingLeg`
  bidirectional navigation along the route.
- **`S421Leg`** with typed `StartWaypoint` / `EndWaypoint` endpoint
  references, so consumers holding a leg can reach both of its
  bounding waypoints without coordinate matching.
- **`S421ActionPoint`**, **`S421Schedule`** + variants (Manual / Calculated /
  Recommended), **`S421ScheduleElement`**.
- Anything the typed model does not understand is preserved on each object's
  `ExtraAttributes` dictionary, so extension and future-edition attributes
  round-trip verbatim.
- Projection failures (unresolved references, unparseable date/times) surface
  as `ProjectionDiagnostic` entries (from `EncDotNet.S100.DataModel`) rather
  than exceptions.

> **Shared abstractions.** As of Pass 1 of the typed-model initiative, the
> projection layer consumes the cross-cutting `ProjectionDiagnostic`,
> `DiagnosticSeverity`, `GeoPosition`, `XlinkResolver`, `AttributeParser`,
> and `ExtraAttributes` helpers from `EncDotNet.S100.DataModel` in
> `EncDotNet.S100.Core`. The previous per-spec types
> (`S421ProjectionDiagnostic`, `S421DiagnosticSeverity`, `S421Reference`)
> have been removed.

## Installation

```sh
dotnet add package EncDotNet.S100.Datasets.S421
```

## Quick start

```csharp
using EncDotNet.S100.Datasets.S421;

var dataset = S421Dataset.Open("RTE-TEST-GMIN.s421.gml");

Console.WriteLine($"Dataset: {dataset.DatasetIdentifier}");
Console.WriteLine($"Features: {dataset.Features.Length}");
Console.WriteLine($"Information types: {dataset.InformationTypes.Length}");

foreach (var wp in dataset.Features.Where(f => f.FeatureType == "RouteWaypoint"))
{
    var (lat, lon) = wp.Points[0];
    Console.WriteLine($"  Waypoint {wp.Attributes["routeWaypointID"]}: {lat}, {lon}");
}
```

### Quick start (typed model)

```csharp
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.S421;
using EncDotNet.S100.Datasets.S421.DataModel;

var dataset = S421Dataset.Open("RTE-TEST-GMIN.s421.gml");
var plan = S421RoutePlan.From(dataset, out var diagnostics);

Console.WriteLine($"Route {plan.Route.RouteId} ed.{plan.Route.EditionNumber}");
Console.WriteLine($"  Author: {plan.Route.Info?.Author}");
Console.WriteLine($"  Vessel: {plan.Route.Info?.Vessel?.Name} (MMSI {plan.Route.Info?.Vessel?.Mmsi})");

foreach (var wp in plan.Route.Waypoints)
{
    var leg = wp.OutgoingLeg;
    Console.WriteLine(
        $"  WP{wp.WaypointNumber} {wp.Position.Latitude:F4}, {wp.Position.Longitude:F4}"
        + (leg is not null ? $" → leg {leg.Id}" : ""));
}

// Walk the route via typed endpoint references — no href parsing required.
var cursor = plan.Route.Waypoints.FirstOrDefault();
while (cursor?.OutgoingLeg is { } leg)
{
    Console.WriteLine($"  leg {leg.Id}: {leg.StartWaypoint?.Id} → {leg.EndWaypoint?.Id}");
    cursor = leg.EndWaypoint;
}

foreach (var d in diagnostics) Console.WriteLine(d);
```

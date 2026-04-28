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
- **`S421GeometryType`** — enum describing the geometry primitive type of a feature (`None`, `Point`, `Curve`, `Surface`).
- **`S421FeatureXmlSource`** — `IFeatureXmlSource` adapter that projects an `S421Dataset` into S-100 Part 9 FeatureXML for XSLT portrayal rules.
- **`S421PortrayalCatalogue`** — `IVectorPortrayalCatalogue` implementation that loads XSLT rules, symbols, line styles, area fills, and color palettes.

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

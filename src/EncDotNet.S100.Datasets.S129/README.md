# EncDotNet.S100.Datasets.S129

Support for S-129 Under Keel Clearance Management datasets (S-100 Part 10b GML encoding).

## Overview

This library reads S-129 datasets from GML files and provides an XSLT-based portrayal pipeline for under keel clearance features. Two API surfaces are offered:

### Raw GML (feature-bag) surface

- **`S129Dataset`** — root model containing parsed features and dataset identification.
- **`S129Feature`** — a geographic feature with type code, geometry, simple attributes, complex attributes, and `xlink:href` references.
- **`S129ComplexAttribute`** — a complex attribute instance containing sub-attribute values.
- **`S129Reference`** — an `xlink:href` cross-reference carried on a feature's child element (S-100 Part 10b §7.2).
- **`GmlGeometryType`** — enum describing the geometry primitive type of a feature.
- **`S129PortrayalCatalogue`** — `IVectorPortrayalCatalogue` implementation that loads XSLT rules, symbols, line styles, area fills, and color palettes.

### Strongly-typed data model

Mirrors the projection pattern introduced for S-124 / S-125 / S-128 / S-201 (PRs #69–#72). Lives under `DataModel/`:

- **`S129UnderKeelClearancePlan`** — root typed projection of an `S129Dataset` as a single UKC plan (one plan, one plan area, N non-navigable / almost-non-navigable surfaces, ordered control points). Built via `S129UnderKeelClearancePlan.From(dataset, out diagnostics)`.
- **`S129UkcPlanMetadata`** — the `UnderKeelClearancePlan` feature with typed `fixedTimeRange`, `generationTime`, `maximumDraught`, vessel id, and a typed `S129ExternalReference` to the source S-421 route.
- **`S129UkcPlanArea`**, **`S129NonNavigableArea`**, **`S129AlmostNonNavigableArea`** — typed surface features.
- **`S129ControlPoint`** — typed point feature with the per-waypoint UKC time-step measurement (`distanceAboveUKCLimit`, `expectedPassingTime`, `expectedPassingSpeed`). Control points are returned **ordered by expected passing time** (stable; gaps preserved — the typed model does not interpolate across explicit producer gaps).
- **`S129TimeRange`**, **`S129FeatureName`**, **`S129ExternalReference`** — shared sub-types.
- **`S129GeometryKind`** — `None` / `Point` / `Surface`.

Projection issues — duplicate plan features, attribute parse failures, unresolved xlinks — surface as `ProjectionDiagnostic` entries (codes from the shared `EncDotNet.S100.DataModel` set: `feature.duplicate`, `attribute.parse.double`, `attribute.parse.datetime`, `xlink.unresolved`, `feature.geometry.missing`). The projection only throws `InvalidOperationException` for a fully empty dataset.

### Cross-product references

In S-129 Edition 2.0.0, links to the source S-421 route / S-102 bathymetry / S-104 water level are **textual** (the producer records identifiers, not `xlink:href` URLs). The typed projection preserves these on the plan as `S129ExternalReference` values; resolving them against an actual S-421 / S-102 / S-104 dataset is the caller's responsibility, and the typed model never requires those datasets to be present.

```csharp
var dataset = S129Dataset.Open("12900MCTDS130TS.gml");
var typed = S129UnderKeelClearancePlan.From(dataset, out var diagnostics);

Console.WriteLine($"Vessel: {typed.Plan?.VesselId}");
Console.WriteLine($"Route:  {typed.Plan?.SourceRoute?.Identifier} v{typed.Plan?.SourceRoute?.Version}");
Console.WriteLine($"Plan window: {typed.Plan?.FixedTimeRange?.Start} → {typed.Plan?.FixedTimeRange?.End}");

foreach (var cp in typed.ControlPoints)
{
    Console.WriteLine(
        $"  {cp.FeatureName?.Name,-6} @ {cp.ExpectedPassingTime:HH:mm:ss}  " +
        $"UKC margin: {cp.DistanceAboveUkcLimit:F2} m");
}
```

## Installation

```sh
dotnet add package EncDotNet.S100.Datasets.S129
```


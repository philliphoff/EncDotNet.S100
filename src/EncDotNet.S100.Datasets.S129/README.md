# EncDotNet.S100.Datasets.S129

`EncDotNet.S100.Datasets.S129` reads IHO S-129 Under Keel Clearance Management
datasets: GML files (S-100 Part 10b) that hold one under-keel clearance (UKC)
plan for a vessel's passage, with its plan area, non-navigable areas and
control points. It parses a dataset, projects it into a typed plan, validates
it, and loads the S-129 portrayal catalogue. Reference it when you need typed
access to a UKC plan or its validation rules. To open and render any product,
including S-129, use the [`EncDotNet.S100`](../EncDotNet.S100/README.md)
package, which includes this one.

## Install

```bash
dotnet add package EncDotNet.S100.Datasets.S129
```

## Read a dataset

Open the dataset, then project it into the typed plan:

```csharp
using EncDotNet.S100.Datasets.S129;
using EncDotNet.S100.Datasets.S129.DataModel;

var dataset = S129Dataset.Open("path/to/ukc-plan.gml");
var typed = S129UnderKeelClearancePlan.From(dataset, out var diagnostics);

Console.WriteLine($"Vessel: {typed.Plan?.VesselId}");
Console.WriteLine($"Route: {typed.Plan?.SourceRoute?.Identifier} version {typed.Plan?.SourceRoute?.Version}");
Console.WriteLine($"Plan window: {typed.Plan?.FixedTimeRange?.Start} to {typed.Plan?.FixedTimeRange?.End}");

foreach (var cp in typed.ControlPoints)
{
    Console.WriteLine(
        $"  {cp.FeatureName?.Name,-6} at {cp.ExpectedPassingTime:HH:mm:ss}  " +
        $"UKC margin: {cp.DistanceAboveUkcLimit:F2} m");
}
```

## Main types

- **`S129Dataset`**: the parsed dataset, with its features and dataset
  identification. `Open` takes a path or a stream. `ReadMetadata` (static, for
  a path or stream, or on an open dataset) returns the declared product
  specification and the WGS 84 extent of the feature geometry without running
  portrayal. The extent is `null` when no feature has geometry.
- **`S129Feature`**: a feature with its type code, geometry, simple attributes,
  complex attributes and `xlink:href` references. **`S129ComplexAttribute`**
  holds a complex attribute's sub-attributes.
- **`S129Reference`**: an `xlink:href` reference on a feature's child element
  (S-100 Part 10b §7.2).
- **`S129UnderKeelClearancePlan`**: the typed plan. See
  [Typed data model](#typed-data-model).
- **`S129UkcRules`**: the validation rule set. See [Validate](#validate).
- **`S129PortrayalCatalogue`**: an `IVectorPortrayalCatalogue` that loads the
  catalogue's XSLT rules, symbols, line styles, area fills and colour palettes.

## Typed data model

The `EncDotNet.S100.Datasets.S129.DataModel` namespace projects an
`S129Dataset` into one UKC plan:

- **`S129UnderKeelClearancePlan`** is the root, built with
  `S129UnderKeelClearancePlan.From(dataset, out diagnostics)`. It has one plan,
  one plan area, any number of non-navigable and almost-non-navigable areas,
  and the control points.
- **`S129UkcPlanMetadata`** (`Plan`) is the `UnderKeelClearancePlan` feature,
  with a typed `FixedTimeRange`, `GenerationTime`, `MaximumDraught`, vessel ID,
  and an `S129ExternalReference` to the source S-421 route (`SourceRoute`).
- **`S129UkcPlanArea`**, **`S129NonNavigableArea`** and
  **`S129AlmostNonNavigableArea`** are the surface features.
- **`S129ControlPoint`** is a point feature with the UKC values for one
  waypoint: `DistanceAboveUkcLimit`, `ExpectedPassingTime` and
  `ExpectedPassingSpeed`. `ControlPoints` is ordered by expected passing time.
  The sort is stable, and gaps are kept: the model doesn't interpolate across
  gaps the producer left.
- **`S129TimeRange`**, **`S129FeatureName`** and **`S129ExternalReference`**
  are shared value types.
- **`S129GeometryKind`** is `None`, `Point` or `Surface`.

Duplicate plan features, attributes that don't parse and unresolved xlinks
become `ProjectionDiagnostic` entries, with the shared codes
`feature.duplicate`, `attribute.parse.double`, `attribute.parse.datetime`,
`xlink.unresolved` and `feature.geometry.missing`. `From` throws an
`InvalidOperationException` only when the dataset has no features.

### References to other products

In S-129 Edition 2.0.0, the links to the source S-421 route, S-102 bathymetry
and S-104 water levels are text: the producer records identifiers, not
`xlink:href` URLs. The typed plan keeps them as `S129ExternalReference` values.
It never needs those datasets. To resolve the references against loaded
datasets, use
[`EncDotNet.S100.Datasets.S129.Fusion`](../EncDotNet.S100.Datasets.S129.Fusion/README.md),
which also samples S-102 and S-104 at control points and binds control points
to an S-421 route.

## Validate

`S129UkcRules`, in the `EncDotNet.S100.Datasets.S129.Validation` namespace,
exposes each rule as an `IValidationRule<S129UnderKeelClearancePlan>` property
and combines them in `S129UkcRules.Default`. `S129UkcRules.Validate(plan)` runs
the default set. Rule IDs have the form `S129-R-{clause}`, and each rule's XML
documentation cites the S-129 Edition 2.0.0 feature catalogue element it checks.
The `EncDotNet.S100` package's `dataset.Validate()` runs the same rule set.

```csharp
using EncDotNet.S100.Datasets.S129;
using EncDotNet.S100.Datasets.S129.DataModel;
using EncDotNet.S100.Datasets.S129.Validation;

var dataset = S129Dataset.Open("path/to/ukc-plan.gml");
var plan = S129UnderKeelClearancePlan.From(dataset, out _);
var report = S129UkcRules.Validate(plan);

foreach (var finding in report.Findings)
    Console.WriteLine($"[{finding.Severity}] {finding.RuleId}: {finding.Message}");
```

| Rule ID | Severity | Checks |
|---|---|---|
| `S129-R-1.1` | Error | The plan's `fixedTimeRange` start isn't after its end. |
| `S129-R-2.1` | Error | Control-point `expectedPassingTime` values strictly increase. |
| `S129-R-3.1` | Error | All feature coordinates are within the WGS 84 latitude and longitude ranges (S-100 Part 10b §6.2). |
| `S129-R-3.2` | Error | The `UnderKeelClearancePlanArea`, when present, has at least three coordinates. |
| `S129-R-4.1` | Error | The plan's `maximumDraught`, when present, is greater than 0. |
| `S129-R-5.1` | Error | Control-point UKC and speed values are finite. |
| `S129-R-5.2` | Warning | Each control point has a point position. |

The rules check one dataset at a time. Cross-dataset checks, such as comparing a
control point's UKC margin with an S-102 bathymetric grid, aren't included. A
rule that needs other datasets would reach them through
`ValidationContext.Services`.

## Portrayal

The S-129 dataset processor in `EncDotNet.S100.Datasets.Pipelines` runs the
bundled S-129 portrayal catalogue's XSLT rules over the dataset.

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
  combine a UKC plan with the S-102, S-104 and S-421 datasets it refers to.

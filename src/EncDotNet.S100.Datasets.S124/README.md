# EncDotNet.S100.Datasets.S124

`EncDotNet.S100.Datasets.S124` reads
[IHO S-124](https://iho.int/en/s-124-navigational-warnings) Navigational
Warnings datasets: GML files (S-100 Part 10b) that carry NAVAREA, coastal and
local warnings. It parses a dataset into features and information types,
projects it into a typed model, validates it, and loads the S-124 portrayal
catalogue. Reference it when you need typed access to S-124 warnings or their
validation rules. To open and render any product, including S-124, use the
[`EncDotNet.S100`](../EncDotNet.S100/README.md) package, which includes this
one.

## Install

```bash
dotnet add package EncDotNet.S100.Datasets.S124
```

## Read a dataset

Open the dataset, then project it into the typed model:

```csharp
using EncDotNet.S100.Datasets.S124;
using EncDotNet.S100.Datasets.S124.DataModel;

var dataset = S124Dataset.Open("path/to/navwarn.gml");
var warning = S124NavigationalWarning.From(dataset, out var diagnostics);

var msi = warning.Preamble?.MessageSeriesIdentifier;
Console.WriteLine($"Warning {msi?.WarningNumber}/{msi?.Year} ({msi?.ProductionAgency})");
Console.WriteLine($"  Area: {warning.Preamble?.GeneralArea}");

foreach (var part in warning.Parts)
{
    Console.WriteLine($"  Part {part.Id}: restriction {part.Restriction}");
    Console.WriteLine($"    {part.WarningInformation}");
    foreach (var area in part.AffectedAreas)
        Console.WriteLine($"    affected area {area.Id} ({area.GeometryKind})");
}

foreach (var diagnostic in diagnostics)
    Console.WriteLine(diagnostic);
```

## Main types

- **`S124Dataset`**: the parsed dataset, with its features, information types
  and dataset identification. `Open` takes a path or a stream.
  `ReadMetadata` (static, for a path or stream, or on an open dataset) returns
  the declared product specification and the WGS 84 extent of the feature
  geometry without running portrayal. The extent is `null` when no feature has
  geometry.
- **`S124Feature`**: a feature with its type code, geometry, simple attributes,
  complex attributes and `xlink:href` references (`GmlReference`).
  **`S124ComplexAttribute`** holds a complex attribute's sub-attributes. The
  geometry kind is the shared `S100GeometryType` enum from
  `EncDotNet.S100.Core`.
- **`S124InformationType`**: an information type without geometry, such as
  `NavwarnPreamble`.
- **`S124NavigationalWarning`**: the typed model. See
  [Typed data model](#typed-data-model).
- **`S124NavigationalWarningRules`**: the validation rule set. See
  [Validate](#validate).
- **`S124PortrayalCatalogue`**: an `IVectorPortrayalCatalogue` that loads the
  catalogue's XSLT rules, symbols, line styles, area fills and colour palettes.

The reader extracts the features `NavwarnPart`, `NavwarnAreaAffected` and
`TextPlacement`.

## Typed data model

`S124Dataset` is a feature bag: attributes are strings keyed by code, and
references are unresolved. The `EncDotNet.S100.Datasets.S124.DataModel`
namespace projects it into typed objects:

- **`S124NavigationalWarning.From(dataset, out diagnostics)`** builds the
  typed graph, rooted at the warning. It resolves `xlink:href` references and
  parses values.
- **`S124NavwarnPreamble`** has a typed `MessageSeriesIdentifier` (warning
  number, year and production agency), the general area, locality, title,
  NAVAREA and NAVTEX values, and promulgating authority.
- **`S124NavwarnPart`** has the restriction code, the warning text, geometry,
  and the resolved `AffectedAreas` and `TextPlacements`.
- **`S124AffectedArea`** and **`S124TextPlacement`** are the features reached
  through the feature catalogue associations `areaAffected` and
  `TextAssociation`.
- **`S124WarningReference`** has the reference category and message
  reference.
- **`S124SpatialQuality`** has the quality-of-position code.

Each object keeps attributes the model doesn't read in its `ExtraAttributes`
dictionary, so extension and later-edition attributes aren't lost. Unresolved
xlinks, values that don't parse and duplicate preambles become
`ProjectionDiagnostic` entries (from `EncDotNet.S100.DataModel`) instead of
exceptions. `From` throws only when the dataset has no features and no
information types.

## Validate

`S124NavigationalWarningRules`, in the
`EncDotNet.S100.Datasets.S124.Validation` namespace, is the default rule set for
the typed model. It's built on the validation types in the
`EncDotNet.S100.Validation` namespace. Rule IDs have the form `S124-R-{clause}`
and trace to the S-124 Edition 1.0.0 Feature Catalogue, or to S-100 Part 10b for
shared encoding rules. The `EncDotNet.S100` package's `dataset.Validate()` runs
the same rule set.

```csharp
using EncDotNet.S100.Datasets.S124;
using EncDotNet.S100.Datasets.S124.DataModel;
using EncDotNet.S100.Datasets.S124.Validation;

var dataset = S124Dataset.Open("path/to/navwarn.gml");
var warning = S124NavigationalWarning.From(dataset, out _);
var report = S124NavigationalWarningRules.Validate(warning);

foreach (var finding in report.Findings)
    Console.WriteLine($"[{finding.Severity}] {finding.RuleId}: {finding.Message}");
```

| Rule ID | Severity | Checks |
|---|---|---|
| `S124-R-1.1` | Error | The warning contains at least one `NavwarnPart`. |
| `S124-R-2.1` | Error | The warning includes a `NavwarnPreamble`. |
| `S124-R-2.2` | Error | `messageSeriesIdentifier` has a positive warning number and a plausible year. |
| `S124-R-3.1` | Error | `NAVAREA` is one of the IMO NAVAREA codes (I to XXI). |
| `S124-R-4.1` | Error | All coordinates are within the WGS 84 latitude and longitude ranges. |
| `S124-R-4.2` | Error | Surface exterior rings are closed and have at least four positions. |
| `S124-R-5.1` | Warning | Each `NavwarnPart` has non-empty warning text. |
| `S124-R-6.1` | Error | A `References` information type that sets `referenceCategory` includes `messageReference`. |

The rules check one dataset at a time. Cross-dataset checks, such as resolving
the warning that a cancellation refers to, aren't included. Projection
problems, such as a duplicate preamble, are reported in the diagnostics from
`From`, not as findings.

## Portrayal

The S-124 dataset processor in `EncDotNet.S100.Datasets.Pipelines` converts an
`S124Dataset` to S-100 Part 9 FeatureXML with the shared
`GmlFeatureXmlSource<TFeature>` from `EncDotNet.S100.Core`, then runs the
bundled S-124 portrayal catalogue's XSLT rules over it. There's no
S-124-specific FeatureXML source type.

## See also

- [Loading datasets](../../docs/loading-datasets.md): open files, folders, ZIPs
  and exchange sets through the `EncDotNet.S100` package.
- [Reading product data](../../docs/reading-product-data.md): features,
  information types and typed models for each product.
- [Typed data models](../../docs/typed-data-models.md): the typed
  root for each product and the shared diagnostic codes.
- [Custom catalogues and validation](../../docs/catalogues-and-validation.md):
  run the bundled rules and add your own.

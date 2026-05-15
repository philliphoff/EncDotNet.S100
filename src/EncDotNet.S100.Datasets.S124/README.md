# EncDotNet.S100.Datasets.S124

Library for reading and portraying [IHO S-124](https://iho.int/en/s-124-navigational-warnings) (Navigational Warnings) datasets.

S-124 provides a standard data model for distributing navigational warnings (NAVAREA, coastal, local) as GML-encoded datasets conforming to the S-100 framework.

## Features

- Parse S-124 GML datasets (S-100 Part 10b encoding)
- Extract navigational warning features (`NavwarnPart`, `NavwarnAreaAffected`, `TextPlacement`)
- Convert to S-100 Part 9 FeatureXML for portrayal pipeline consumption
- XSLT-based portrayal via the S-124 Portrayal Catalogue

## Overview

Key types include:

- **`S124Dataset`** — root model containing parsed features, information types, and dataset identification.
- **`S124Feature`** — a geographic feature with type code, geometry, simple attributes, complex attributes, and `xlink:href` references (`GmlReference`).
- **`S124InformationType`** — a non-geographic information type instance (e.g. `NavwarnPreamble`).
- **`S124ComplexAttribute`** — a complex attribute instance containing sub-attribute values.
- **`S124GeometryType`** — type alias for `GmlGeometryType` (from `EncDotNet.S100.Core`) describing the geometry primitive type of a feature.
- **`S124FeatureXmlSource`** — `IFeatureXmlSource` adapter that projects an `S124Dataset` into S-100 Part 9 FeatureXML for XSLT portrayal rules.
- **`S124PortrayalCatalogue`** — `IVectorPortrayalCatalogue` implementation that loads XSLT rules, symbols, line styles, area fills, and color palettes.

### Strongly-typed data model

The types above expose the dataset as a feature bag — schema-agnostic, well
suited for the portrayal pipeline, but inconvenient when client code wants to
inspect a warning's preamble, parts, or references directly. The
`EncDotNet.S100.Datasets.S124.DataModel` namespace layers a strongly-typed
projection on top:

- **`S124NavigationalWarning.From(dataset, out diagnostics)`** — projects an
  `S124Dataset` into a typed object graph rooted at the navigational warning,
  resolving `xlink:href` cross-references and parsing primitive values.
- **`S124NavwarnPreamble`** with a typed `MessageSeriesIdentifier`
  (warning number / year / agency), general area, locality, classification.
- **`S124NavwarnPart`** with restriction code, warning information text,
  geometry, and resolved `AffectedAreas` / `TextPlacements`.
- **`S124AffectedArea`**, **`S124TextPlacement`** — features resolved through
  the FC associations `areaAffected` and `TextAssociation` respectively.
- **`S124WarningReference`** with reference category and message reference.
- **`S124SpatialQuality`** with quality-of-position code.
- Anything the typed model does not understand is preserved on each object's
  `ExtraAttributes` dictionary, so extension and future-edition attributes
  round-trip verbatim.
- Projection failures (unresolved xlinks, unparseable values, duplicate
  preambles) surface as `ProjectionDiagnostic` entries (from
  `EncDotNet.S100.DataModel`) rather than exceptions. Only a fully empty
  dataset throws.

## Installation

```sh
dotnet add package EncDotNet.S100.Datasets.S124
```

### Quick start (typed model)

```csharp
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.S124;
using EncDotNet.S100.Datasets.S124.DataModel;

var dataset = S124Dataset.Open("navwarn.gml");
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

foreach (var d in diagnostics) Console.WriteLine(d);
```


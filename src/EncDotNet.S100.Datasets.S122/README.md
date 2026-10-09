# EncDotNet.S100.Datasets.S122

`EncDotNet.S100.Datasets.S122` reads [IHO S-122](https://iho.int/en/s-122)
Marine Protected Areas datasets: GML files (S-100 Part 10b) that describe
marine protected areas, restricted areas, vessel traffic service areas and
related zones. It parses a dataset into features and information types,
projects it into a typed model, validates it, and loads the S-122 portrayal
catalogue. Reference it when you need typed access to S-122 data or its
validation rules. To open and render any product, including S-122, use the
[`EncDotNet.S100`](../EncDotNet.S100/README.md) package, which includes this
one.

## Install

```bash
dotnet add package EncDotNet.S100.Datasets.S122
```

## Read a dataset

Open the dataset, then project it into the typed model:

```csharp
using EncDotNet.S100.Datasets.S122;
using EncDotNet.S100.Datasets.S122.DataModel;

var dataset = S122Dataset.Open("path/to/dataset.gml");
var typed = S122MarineProtectedAreaDataset.From(dataset, out var diagnostics);

foreach (var mpa in typed.MarineProtectedAreas)
{
    Console.WriteLine($"{mpa.Id}: category {mpa.CategoryOfMarineProtectedArea}, designation {mpa.Designation}");

    foreach (var infoRef in mpa.InformationReferences)
        Console.WriteLine($"  {infoRef.Role}: {infoRef.Target.TypeCode} {infoRef.Target.Id}");
}

foreach (var diagnostic in diagnostics)
    Console.WriteLine($"[{diagnostic.Severity}] {diagnostic.Code}: {diagnostic.Message}");
```

## Main types

- **`S122Dataset`**: the parsed dataset, with its features, information types
  and dataset identification. `Open` takes a path or a stream.
  `ReadMetadata` (static, for a path or stream, or on an open dataset) returns
  the declared product specification and the WGS 84 extent of the feature
  geometry without running portrayal. The extent is `null` when no feature has
  geometry.
- **`S122Feature`**: a feature with its type code, geometry, simple attributes
  and complex attributes. **`S122ComplexAttribute`** holds a complex
  attribute's sub-attributes. The geometry kind is the shared
  `S100GeometryType` enum from `EncDotNet.S100.Core`.
- **`S122InformationType`**: an information type without geometry, such as
  `Authority`, `Regulations` or `SpatialQuality`.
- **`S122MarineProtectedAreaDataset`**: the typed model. See
  [Typed data model](#typed-data-model).
- **`S122MarineProtectedAreaRules`**: the validation rule set. See
  [Validate](#validate).
- **`S122PortrayalCatalogue`**: an `IVectorPortrayalCatalogue` that loads the
  catalogue's XSLT rules, symbols, line styles, area fills and colour palettes.

The reader extracts the features `MarineProtectedArea`, `RestrictedArea`,
`VesselTrafficServiceArea`, `DataCoverage`, `InformationArea`,
`QualityOfNonBathymetricData` and `TextPlacement`.

## Typed data model

`S122MarineProtectedAreaDataset`, in the
`EncDotNet.S100.Datasets.S122.DataModel` namespace, is a read-only projection of
`S122Dataset` with typed features and information types. It's built on the
shared typed-model types in the `EncDotNet.S100.DataModel` namespace
(`XlinkResolver`, `ProjectionContext`, `GeoPosition`, `AttributeParser`,
`ExtraAttributes`). The portrayal pipeline doesn't use it; it reads
`S122Dataset` directly.

- `MarineProtectedAreas`, `RestrictedAreas` and `VesselTrafficServiceAreas`
  list the typed features of each kind.
- `xlink:href` associations, such as `theAuthority`, `theInformation` and
  `theCartographicText`, resolve to typed `S122InformationReference` and
  `S122FeatureReference` records. Every typed object also keeps its raw
  `GmlReference` list, so you can read the role, arcrole and href as encoded.
- Feature and information types the model doesn't recognize become
  `S122OtherFeature` and `S122OtherInformationType`, so data from later
  catalogue editions isn't lost.
- `From` throws only when the dataset has no features and no information
  types. Everything else, such as an unresolved xlink or an attribute that
  doesn't parse, becomes a `ProjectionDiagnostic`.

## Validate

`S122MarineProtectedAreaRules`, in the `EncDotNet.S100.Datasets.S122.Validation`
namespace, is the default rule set for the typed model. It's built on the
validation types in the `EncDotNet.S100.Validation` namespace
(`IValidationRule<TModel>`, `ValidationRuleSet<TModel>`, `ValidationFinding`,
`ValidationSeverity`). The `EncDotNet.S100` package's `dataset.Validate()` runs
the same rule set.

```csharp
using EncDotNet.S100.Datasets.S122;
using EncDotNet.S100.Datasets.S122.DataModel;
using EncDotNet.S100.Datasets.S122.Validation;

var dataset = S122Dataset.Open("path/to/dataset.gml");
var typed = S122MarineProtectedAreaDataset.From(dataset, out _);
var report = S122MarineProtectedAreaRules.Validate(typed);

foreach (var finding in report.Findings)
    Console.WriteLine($"[{finding.Severity}] {finding.RuleId}: {finding.Message}");
```

| Rule ID | Severity | Checks |
|---|---|---|
| `S122-R-3.1` | Warning | Features that declare a point, curve or surface geometry have coordinates. |
| `S122-R-4.1` | Error | All coordinates are within the WGS 84 latitude and longitude ranges. |
| `S122-R-5.1` | Error | Surface features have a closed exterior ring (at least four coordinates, first equal to last). |
| `S122-R-6.1` | Error | Feature `gml:id` values are unique within the dataset. |
| `S122-R-6.2` | Error | Information type `gml:id` values are unique within the dataset. |
| `S122-R-7.1` | Warning | `scaleMinimum`, when present, is a positive denominator (at least 1). |
| `S122-R-9.1` | Warning | `productIdentifier`, when present, starts with `S-122`. |

The rules check one dataset at a time. Cross-dataset checks, such as an S-122
restricted area that overlaps an S-101 routing zone, aren't included. A
rule that needs other datasets would reach them through
`ValidationContext.Services`.

## Portrayal

The S-122 dataset processor in `EncDotNet.S100.Datasets.Pipelines` converts an
`S122Dataset` to S-100 Part 9 FeatureXML with the shared
`GmlFeatureXmlSource<TFeature>` from `EncDotNet.S100.Core`, then runs the
bundled S-122 portrayal catalogue (edition 2.0.0) over it. There's no
S-122-specific FeatureXML source type.

> [!NOTE]
> Only the Day palette is available for S-122. The bundled catalogue's
> `colorProfile.xml` defines a `Day` palette and no others, although its
> `Symbols/` folder includes dusk and night stylesheets. Calling
> `SwitchPaletteAsync` with `Dusk` or `Night` leaves `ActivePalette` set to
> Day, so S-122 data keeps its day colours in dusk and night display.

## Encoding notes

- Coordinates in `<gml:pos>` and `<gml:posList>` are latitude then longitude
  for `EPSG:4326` (S-100 Part 10b).
- **Longitude-first coordinates.** Some producers, such as the UKHO trial
  dataset `GBNPI12200002045.gml`, write `<gml:posList>` as longitude then
  latitude while keeping the `<gml:Envelope>` corners as latitude then
  longitude. The reader compares the parsed coordinates with the envelope.
  When they clearly fall outside it as parsed, and clearly inside it when
  swapped, it swaps every feature's coordinates. Datasets that follow the
  specification aren't changed.
- **Commas in `posList`.** GML 3.2 allows only whitespace between values in
  `<gml:posList>`, but some producers write `lon,lat lon,lat` pairs (the older
  `gml:coordinates` style). The reader accepts both whitespace and commas as
  separators.
- The reader accepts the `s100gml` namespaces used across S-122 sample
  releases (`http://www.iho.int/s100gml/1.0`,
  `http://www.iho.int/S100/profile/s100gml/1.0` and
  `http://www.iho.int/s100gml/5.0`). For any other namespace, it looks for one
  among the document's namespace declarations.
- The reader accepts both the standard `<member>` and `<imember>` wrappers and
  the `<members>` and `<imembers>` containers that some sample datasets use.

## See also

- [Loading datasets](../../docs/loading-datasets.md): open files, folders, ZIPs
  and exchange sets through the `EncDotNet.S100` package.
- [Reading product data](../../docs/reading-product-data.md): features,
  information types and typed models for each product.
- [Typed data models](../../docs/typed-data-models.md): the typed
  root for each product and the shared diagnostic codes.
- [Custom catalogues and validation](../../docs/catalogues-and-validation.md):
  run the bundled rules and add your own.

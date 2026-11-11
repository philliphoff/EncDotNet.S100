# EncDotNet.S100.Datasets.S127

`EncDotNet.S100.Datasets.S127` reads [IHO S-127](https://iho.int/en/s-127)
Marine Resources and Services datasets: GML files (S-100 Part 10b) that describe
traffic-management information such as pilot boarding places, routeing
measures, restricted areas, vessel traffic services and signal stations. It
parses a dataset, projects it into a typed model, validates it, and loads the
S-127 portrayal catalogue. Reference it when you need typed access to S-127 data
or its validation rules. To open and render any product, including S-127, use
the [`EncDotNet.S100`](../EncDotNet.S100/README.md) package, which includes this
one.

## Install

```bash
dotnet add package EncDotNet.S100.Datasets.S127
```

## Read a dataset

Open the dataset, then project it into the typed model:

```csharp
using EncDotNet.S100.Datasets.S127;
using EncDotNet.S100.Datasets.S127.DataModel;

var dataset = S127Dataset.Open("path/to/marine_services.gml");
var typed = S127MarineServicesDataset.From(dataset, out var diagnostics);

foreach (var pbp in typed.Features.OfType<S127PilotBoardingPlace>())
    Console.WriteLine($"{pbp.Id}: category {pbp.CategoryOfPilotBoardingPlace}, authority {pbp.Authority?.Id}");

foreach (var diagnostic in diagnostics)
    Console.WriteLine(diagnostic);
```

## Main types

- **`S127Dataset`**: the parsed dataset, with its features, information types
  and dataset identification. `Open` takes a path or a stream.
  `ReadMetadata` (static, for a path or stream, or on an open dataset) returns
  the declared product specification and the WGS 84 extent of the feature
  geometry without running portrayal. The extent is `null` when no feature has
  geometry.
- **`S127Feature`**: a feature with its type code, geometry, simple attributes
  and complex attributes. **`S127ComplexAttribute`** holds a complex
  attribute's sub-attributes.
- **`S127InformationType`**: an information type. S-127 Edition 2.0.0 declares
  none, but the reader keeps any `imember` content so later editions aren't
  lost.
- **`S127MarineServicesDataset`**: the typed model. See
  [Typed data model](#typed-data-model).
- **`S127MarineServicesRules`**: the validation rule set. See
  [Validate](#validate).
- **`S127PortrayalCatalogue`**: an `IVectorPortrayalCatalogue` that loads the
  catalogue's XSLT rules, SVG symbols, line styles and colour palettes.

Feature geometry reaches the renderers through the shared
`FeatureGeometryProvider<TFeature>` from `EncDotNet.S100.Core`.

## Typed data model

`S127MarineServicesDataset`, in the `EncDotNet.S100.Datasets.S127.DataModel`
namespace, is a read-only projection of `S127Dataset`. The portrayal pipeline
and the viewer don't use it; they read `S127Dataset` directly.

- It models the main S-127 feature classes: `S127PilotBoardingPlace`,
  `S127RouteingMeasure`, `S127VesselTrafficServiceArea`,
  `S127ShipReportingService`, `S127SignalStation` (traffic or warning, by
  `S127SignalStationKind`), `S127RegulatedArea` (which covers `RestrictedArea`,
  `RestrictedAreaNavigational`, `MilitaryPracticeArea`, `CautionArea` and
  others) and `S127Authority`. Every other feature type becomes an
  `S127OtherFeature`, so no geometry or attribute is lost.
- Feature-to-feature `xlink:href` bindings, such as `theAuthority`, become
  typed `IS127Feature` references. The projection builds every typed object
  first and then binds references, so cycles resolve.
- Features without geometry, such as `Authority`, have `GeometryKind` `None`
  and empty `Coordinates`.
- Unresolved xlinks and attributes that don't parse become
  `ProjectionDiagnostic` entries, for example with the code
  `xlink.unresolved`. `From` throws only when the dataset has no features and
  no information types.
- Each typed object keeps the source attributes it doesn't read in
  `ExtraAttributes`.

## Validate

`S127MarineServicesRules`, in the `EncDotNet.S100.Datasets.S127.Validation`
namespace, is the default rule set for the typed model. Rule IDs have the form
`S127-R-{clause}` and trace to sections of S-127 Edition 2.0.0. The
`EncDotNet.S100` package's `dataset.Validate()` runs the same rule set.

```csharp
using EncDotNet.S100.Datasets.S127;
using EncDotNet.S100.Datasets.S127.DataModel;
using EncDotNet.S100.Datasets.S127.Validation;

var dataset = S127Dataset.Open("path/to/marine_services.gml");
var typed = S127MarineServicesDataset.From(dataset, out _);
var report = S127MarineServicesRules.Validate(typed);

foreach (var finding in report.Findings)
    Console.WriteLine($"[{finding.Severity}] {finding.RuleId}: {finding.Message}");
```

| Rule ID | Severity | Checks |
|---|---|---|
| `S127-R-12.1` | Error | Coordinates are within the WGS 84 latitude and longitude ranges (S-100 Part 10b §6.2). |
| `S127-R-12.2` | Error | `PilotBoardingPlace` features have non-empty geometry. |
| `S127-R-12.3` | Error | Surface exterior rings have at least four vertices and are closed. |
| `S127-R-12.4` | Error | Curves have at least two vertices. |
| `S127-R-12.5` | Warning | Vessel-size minimums (length, draught, beam) don't exceed their maximums. |
| `S127-R-12.6` | Warning | Availability time-of-day and date ranges have the start on or before the end. |
| `S127-R-12.7` | Error | Feature identifiers are unique within the dataset. |
| `S127-R-12.8` | Warning | `Authority` features have a non-empty `authorityName`. |

Features without geometry, such as `Authority`, pass the geometry rules. Two
candidate rules aren't included: the geometry of VTS reporting points, which
needs cross-feature xlink resolution, and checking `categoryOfService` against
its listed values, which needs typed enumerations. The class remarks on
`S127MarineServicesRules` explain both.

## Portrayal

The S-127 dataset processor in `EncDotNet.S100.Datasets.Pipelines` converts an
`S127Dataset` to S-100 Part 9 FeatureXML (`Dataset/Features/*`) with the shared
`GmlFeatureXmlSource<TFeature>` from `EncDotNet.S100.Core`, then runs the
bundled `main.xsl` rule over it. There's no S-127-specific FeatureXML source
type. The bundled portrayal catalogue (Edition 2.0.0) is byte-identical to
upstream `iho-ohi/S-127-Product-Specification-Development`.

## Encoding notes

- Coordinates in `<gml:pos>` and `<gml:posList>` are latitude then longitude
  for `EPSG:4326` (S-100 Part 10b §6.2).
- The reader accepts the S-100 GML 5.0 namespace
  (`http://www.iho.int/s100gml/5.0`) and the older 1.0 namespaces.
- The reader treats every `<member>` child in the dataset's application schema
  namespace as a feature, so new feature types in the S-127 feature catalogue
  don't need reader changes.

## See also

- [Loading datasets](../../docs/loading-datasets.md): open files, folders, ZIPs
  and exchange sets through the `EncDotNet.S100` package.
- [Reading product data](../../docs/reading-product-data.md): features,
  information types and typed models for each product.
- [Typed data models](../../docs/typed-data-models.md): the typed
  root for each product and the shared diagnostic codes.
- [Custom catalogues and validation](../../docs/catalogues-and-validation.md):
  run the bundled rules and add your own.

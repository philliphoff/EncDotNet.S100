# EncDotNet.S100.Datasets.S122

Library for reading and portraying [IHO S-122](https://iho.int/en/s-122) (Marine Protected Areas) datasets.

S-122 provides a standard data model for distributing information about marine protected areas, restricted areas, vessel traffic service areas, and related zoned overlays as GML-encoded datasets conforming to the S-100 framework.

## Features

- Parse S-122 GML datasets (S-100 Part 10b encoding)
- Extract feature instances (`MarineProtectedArea`, `RestrictedArea`, `VesselTrafficServiceArea`, `DataCoverage`, `InformationArea`, `QualityOfNonBathymetricData`, `TextPlacement`)
- Convert to S-100 Part 9 FeatureXML for portrayal pipeline consumption
- XSLT-based portrayal via the bundled S-122 Portrayal Catalogue (v2.0.0)

## Overview

Key types include:

- **`S122Dataset`** — root model containing parsed features, information types, and dataset identification.
- **`S122Feature`** — a geographic feature with type code, geometry, simple attributes, and complex attributes.
- **`S122InformationType`** — a non-geographic information type instance (e.g. `Authority`, `Regulations`, `SpatialQuality`).
- **`S122ComplexAttribute`** — a complex attribute instance containing sub-attribute values.
- **`S122GeometryType`** has been replaced by the shared `GmlGeometryType` enum from `EncDotNet.S100.Core`.
- **`S122FeatureXmlSource`** — `IFeatureXmlSource` adapter that projects an `S122Dataset` into S-100 Part 9 FeatureXML for XSLT portrayal rules.
- **`S122PortrayalCatalogue`** — `IVectorPortrayalCatalogue` implementation that loads XSLT rules, symbols, line styles, area fills, and color palettes.

## Notes

- Coordinate ordering in `<gml:pos>` / `<gml:posList>` is **lat lon** for `EPSG:4326` (S-100 Part 10b convention).
- **Producer-bug compensation: lon-lat axis order.** Some S-122 producers
  (notably the UKHO trial dataset `GBNPI12200002045.gml`) emit `<gml:posList>`
  in lon-lat order while keeping `<gml:Envelope>` corners correctly in
  lat-lon. The reader detects this by sampling parsed feature coords against
  the declared envelope; if the as-parsed interpretation clearly falls
  outside but the swapped interpretation clearly falls inside, every
  feature's coordinates are swapped before they reach the pipeline.
  Spec-conformant datasets are left untouched.
- **Producer-bug compensation: comma-separated tuples in `posList`.**
  GML 3.2 mandates whitespace-only separators inside `<gml:posList>`, but
  some S-122 producers emit `lon,lat lon,lat ...` tokens (the older
  `gml:coordinates` convention). The reader treats both whitespace and
  commas as coordinate separators so either shape parses correctly.
- The reader tolerates the s100gml namespace variants found across S-122 sample releases (`http://www.iho.int/s100gml/1.0`, `http://www.iho.int/S100/profile/s100gml/1.0`, `http://www.iho.int/s100gml/5.0`) and falls back to scanning the document's namespace declarations.
- Both the standard `<member>`/`<imember>` wrappers and the inline `<members>`/`<imembers>` containers used by some sample datasets are supported.
- **Palette switching is currently a no-op for S-122.** The bundled v2.0.0
  Portrayal Catalogue ships only a `Day` `<palette>` block in
  `colorProfile.xml`, even though `duskSvgStyle.css` and `nightSvgStyle.css`
  are present in `Symbols/`. As a result, requesting `Dusk` or `Night`
  through `SwitchPalette` silently leaves `ActivePalette = Day` and the
  renderer keeps resolving colour tokens to their Day sRGB values.
  TODO: synthesise Dusk / Night palettes locally (e.g. from the S-101 PC
  or via the ship-provided CSS files) so the viewer can offer night-mode
  S-122 portrayal until upstream publishes the missing palette blocks.

## Installation

```sh
dotnet add package EncDotNet.S100.Datasets.S122
```

## Strongly-typed data model

In addition to the feature-bag `S122Dataset`, the library exposes a
strongly-typed projection rooted at `S122MarineProtectedAreaDataset`
under `EncDotNet.S100.Datasets.S122.DataModel`. It uses the shared
typed-model abstractions in `EncDotNet.S100.Core.DataModel`
(`XlinkResolver`, `ProjectionContext`, `GeoPosition`,
`AttributeParser`, `ExtraAttributes`) and follows the same pattern
as the S-124 / S-125 / S-128 / S-201 projections.

```csharp
using EncDotNet.S100.Datasets.S122;
using EncDotNet.S100.Datasets.S122.DataModel;

var dataset = S122Dataset.Open("122TESTDATASET.gml");
var typed = S122MarineProtectedAreaDataset.From(dataset, out var diagnostics);

foreach (var mpa in typed.MarineProtectedAreas)
{
    Console.WriteLine($"{mpa.Id}: cat={mpa.CategoryOfMarineProtectedArea}, designation={mpa.Designation}");

    foreach (var infoRef in mpa.InformationReferences)
        Console.WriteLine($"  -> {infoRef.Role}: {infoRef.Target.TypeCode} #{infoRef.Target.Id}");
}

foreach (var diag in diagnostics)
    Console.WriteLine($"[{diag.Severity}] {diag.Code}: {diag.Message}");
```

Notes:

- Typed projection is purely **additive** — the portrayal pipeline
  continues to run off the feature-bag dataset and is unchanged.
- `From()` throws only when the source dataset has no features and no
  information types; everything else surfaces as a
  `ProjectionDiagnostic` (unresolved xlinks, attribute parse
  failures, etc.).
- `xlink:href` associations (e.g. `theAuthority`, `theInformation`,
  `theCartographicText`) are resolved into typed
  `S122InformationReference` / `S122FeatureReference` records.
  The raw `GmlReference` list is also preserved on every typed
  object for callers that need role / arcrole / href verbatim.
- Unrecognised feature / information types are preserved as
  `S122OtherFeature` / `S122OtherInformationType` so future-edition
  catalogues round-trip without information loss.

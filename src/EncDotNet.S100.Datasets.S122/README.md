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
- **`S122GeometryType`** — enum describing the geometry primitive type of a feature.
- **`S122FeatureXmlSource`** — `IFeatureXmlSource` adapter that projects an `S122Dataset` into S-100 Part 9 FeatureXML for XSLT portrayal rules.
- **`S122PortrayalCatalogue`** — `IVectorPortrayalCatalogue` implementation that loads XSLT rules, symbols, line styles, area fills, and color palettes.

## Notes

- Coordinate ordering in `<gml:pos>` / `<gml:posList>` is **lat lon** for `EPSG:4326` (S-100 Part 10b convention).
- The reader tolerates the s100gml namespace variants found across S-122 sample releases (`http://www.iho.int/s100gml/1.0`, `http://www.iho.int/S100/profile/s100gml/1.0`, `http://www.iho.int/s100gml/5.0`) and falls back to scanning the document's namespace declarations.
- Both the standard `<member>`/`<imember>` wrappers and the inline `<members>`/`<imembers>` containers used by some sample datasets are supported.

## Installation

```sh
dotnet add package EncDotNet.S100.Datasets.S122
```

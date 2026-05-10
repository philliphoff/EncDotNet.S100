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
- **`S124Feature`** — a geographic feature with type code, geometry, simple attributes, and complex attributes.
- **`S124InformationType`** — a non-geographic information type instance (e.g. `NavwarnPreamble`).
- **`S124ComplexAttribute`** — a complex attribute instance containing sub-attribute values.
- **`S124GeometryType`** — type alias for `GmlGeometryType` (from `EncDotNet.S100.Core`) describing the geometry primitive type of a feature.
- **`S124FeatureXmlSource`** — `IFeatureXmlSource` adapter that projects an `S124Dataset` into S-100 Part 9 FeatureXML for XSLT portrayal rules.
- **`S124PortrayalCatalogue`** — `IVectorPortrayalCatalogue` implementation that loads XSLT rules, symbols, line styles, area fills, and color palettes.

## Installation

```sh
dotnet add package EncDotNet.S100.Datasets.S124
```


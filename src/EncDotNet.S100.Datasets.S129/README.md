# EncDotNet.S100.Datasets.S129

Support for S-129 Under Keel Clearance Management datasets (S-100 Part 10b GML encoding).

## Overview

This library reads S-129 datasets from GML files and provides an XSLT-based portrayal pipeline for under keel clearance features. Key types include:

- **`S129Dataset`** — root model containing parsed features and dataset identification.
- **`S129Feature`** — a geographic feature with type code, geometry, simple attributes, and complex attributes.
- **`S129ComplexAttribute`** — a complex attribute instance containing sub-attribute values.
- **`GmlGeometryType`** — enum describing the geometry primitive type of a feature.
- **`S129FeatureXmlSource`** — `IFeatureXmlSource` adapter that projects an `S129Dataset` into S-100 Part 9 FeatureXML for XSLT portrayal rules.
- **`S129PortrayalCatalogue`** — `IVectorPortrayalCatalogue` implementation that loads XSLT rules, symbols, line styles, area fills, and color palettes.

## Installation

```sh
dotnet add package EncDotNet.S100.Datasets.S129
```


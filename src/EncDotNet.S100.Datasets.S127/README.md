# EncDotNet.S100.Datasets.S127

Library for reading and portraying [IHO S-127](https://iho.int/en/s-127) (Marine Resources and Services) datasets.

S-127 provides a standard data model for distributing marine traffic-management information — pilot boarding places, routeing measures, restricted areas, vessel traffic services, signal stations, and similar features — as GML-encoded datasets conforming to the S-100 framework.

## Features

- Parse S-127 GML datasets (S-100 Part 10b encoding)
- Tolerates both the current S-100 GML 5.0 namespace (`http://www.iho.int/s100gml/5.0`) and the legacy 1.0 namespace
- Convert to S-100 Part 9 FeatureXML for portrayal pipeline consumption
- XSLT-based portrayal via the bundled S-127 Portrayal Catalogue (Edition 2.0.0)

## Overview

Key types:

- **`S127Dataset`** — root model containing parsed features, information types, and dataset identification.
- **`S127Feature`** — a geographic feature with type code, geometry, simple attributes, and complex attributes.
- **`S127InformationType`** — a non-geographic information type instance (S-127 Edition 2.0.0 declares none, but the parser preserves any `imember` content for forward compatibility).
- **`S127ComplexAttribute`** — a complex attribute instance containing sub-attribute values.
- **`GmlGeometryType`** — enum describing the geometry primitive type of a feature.
- **`S127FeatureXmlSource`** — `IFeatureXmlSource` adapter that projects an `S127Dataset` into S-100 Part 9 FeatureXML (`Dataset/Features/*`) for the bundled `main.xsl` rule.
- **`S127FeatureGeometryProvider`** — `IFeatureGeometryProvider` adapter exposing feature geometry to the unified Mapsui display-list renderer.
- **`S127PortrayalCatalogue`** — `IVectorPortrayalCatalogue` implementation that loads XSLT rules, SVG symbols, line styles, and color palettes.

## Quick start

```csharp
using EncDotNet.S100.Datasets.S127;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Specifications;

var dataset = S127Dataset.Open("pilot_boarding.gml");

var manager = new PortrayalCatalogueManager(productSpec =>
    Specification.CreatePortrayalCatalogueSource(productSpec));
var catalogue = new S127PortrayalCatalogue(manager.GetProvider("S-127"));
catalogue.SwitchPalette(PaletteType.Day);

var source = new S127FeatureXmlSource(dataset);
var instructions = await new PortrayalPipeline().ProcessAsync(source, catalogue);
```

## Spec compliance

- Coordinate ordering in `<gml:pos>` / `<gml:posList>` is `lat lon` (EPSG:4326), per S-100 Part 10b §6.2.
- The reader treats every `<member>` child whose namespace matches the dataset's application schema as a feature wrapper, so additions to the S-127 Feature Catalogue do not require parser changes.
- Bundled portrayal assets are byte-identical to upstream `iho-ohi/S-127-Product-Specification-Development` (PC 2.0.0).

## Installation

```sh
dotnet add package EncDotNet.S100.Datasets.S127
```

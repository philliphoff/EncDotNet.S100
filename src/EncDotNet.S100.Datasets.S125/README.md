# EncDotNet.S100.Datasets.S125

Library for reading and portraying [IHO S-125](https://iho.int/en/s-100-based-product-specifications) (Marine Aids to Navigation) datasets.

S-125 supersedes the AtoN (Aids to Navigation) feature classes from S-57 / S-101 with a stand-alone S-100-based product specification covering lights, buoys, beacons, daymarks, AIS aids, and other aids to navigation.

## Features

- Parse S-125 GML datasets (S-100 Part 10b encoding using the S-100 GML 5.0 profile)
- Extract concrete AtoN features (`Landmark`, `LateralBuoy`, `CardinalBeacon`, `LightSectored`, `VirtualAISAidToNavigation`, …) and information types (`AtonStatusInformation`, `SpatialQuality`)
- Preserve information bindings (`xlink:href` / `informationRef`) so the XSLT portrayal rules can resolve cross-references
- Project to the S-100 Part 9 FeatureXML neutral form (`Dataset/Features/*` plus `Dataset/InformationTypes/*`) consumed by the S-125 portrayal catalogue
- XSLT-based portrayal via the S-125 Portrayal Catalogue

## Overview

Key types:

- **`S125Dataset`** — root model containing parsed features, information types, and dataset identification.
- **`S125Feature`** — a geographic feature with type code, geometry, simple/complex attributes, and information references. Implements `IGmlFeature`.
- **`S125InformationType`** — an information type instance (e.g. `AtonStatusInformation`). Implements `IGmlInformationType`.
- **`S125InformationReference`** — a feature → information-type association captured from `xlink:href` / `informationRef` attributes.
- **`S125ComplexAttribute`** — a complex attribute group with sub-attribute values. Implements `IGmlComplexAttribute`.
- **`GmlGeometryType`** — shared enum (from `EncDotNet.S100.Core`) describing the geometry primitive type of a feature.
- **`S125FeatureXmlSource`** — `IFeatureXmlSource` adapter that projects an `S125Dataset` into the synthesized `Dataset/Features/*` shape that S-125 XSLT rules match against.
- **`S125FeatureGeometryProvider`** — `IFeatureGeometryProvider` adapter for the unified Mapsui display-list renderer.
- **`S125PortrayalCatalogue`** — `IVectorPortrayalCatalogue` implementation that loads XSLT rules, symbols, line styles, area fills, and color palettes.

## Notes

- S-125 application schema namespace is `http://www.iho.int/S125/1.0`; geometry uses the S-100 GML 5.0 profile namespace `http://www.iho.int/s100gml/5.0`. Older sample datasets that still declare the S-100 GML 1.0 profile are read transparently.
- Coordinate ordering in `<gml:pos>` / `<gml:posList>` follows the S-100 Part 10b convention of **lat lon** for `EPSG:4326`.
- The bundled portrayal catalogue is the development edition from [`iho-ohi/S-125-Product-Specification-Development`](https://github.com/iho-ohi/S-125-Product-Specification-Development); at the time of writing it ships only AtoN status indication, AtoN status information, and DataCoverage rules. Per-feature-class symbology is therefore sparse and renderable output is limited until the upstream catalogue is fleshed out.
- Renderers must tolerate geometry-less features — abstract supertypes such as `AtonAggregation` and `AtonAssociation` carry no geometry.
- Time validity (`fixedDateRange`, `periodicDateRange`) is interpreted as UTC; do not coerce to local time at the source.

## License

The bundled S-125 specification assets in `EncDotNet.S100.Specifications` are © IHO and used in accordance with their open-publication terms; see <https://github.com/iho-ohi/S-125-Product-Specification-Development>.

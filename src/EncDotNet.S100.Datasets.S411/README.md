# EncDotNet.S100.Datasets.S411

Library for reading and portraying [IHO/JCOMM S-411](https://iho.int/en/s-411-ice-information) (Ice Information for Surface Navigation) datasets.

S-411 provides a standard data model for distributing sea-ice and lake-ice information as GML-encoded datasets conforming to the S-100 framework.

## Features

- Parse S-411 GML datasets (S-100 Part 10b encoding, both `s100gml/1.0` and `s100gml/5.0` profile namespaces)
- Extract sea-ice features (`SeaIce`, `LakeIce`, `Iceberg`, `IceEdge`, `IceLead`, etc. — see the S-411 1.2.1 Feature Catalogue for the full set)
- Convert to S-100 Part 9 FeatureXML for portrayal pipeline consumption
- XSLT-based portrayal via the S-411 Portrayal Catalogue

## Overview

Key types:

- **`S411Dataset`** — root model containing parsed features and dataset identification.
- **`S411Feature`** — a geographic feature with type code, geometry, simple attributes, and complex attributes.
- **`S411ComplexAttribute`** — a complex attribute instance containing sub-attribute values.
- **`S411GeometryType`** — enum describing the geometry primitive type of a feature.
- **`S411FeatureXmlSource`** — `IFeatureXmlSource` adapter that projects an `S411Dataset` into S-100 Part 9 FeatureXML for XSLT portrayal rules.
- **`S411FeatureGeometryProvider`** — `IFeatureGeometryProvider` adapter for the unified Mapsui display-list renderer.
- **`S411PortrayalCatalogue`** — `IVectorPortrayalCatalogue` implementation that loads XSLT rules, symbols, line styles, area fills, and color palettes.

## Notes

- S-411 has no information types (`<imember>` elements); only `<member>` / `<members>` feature wrappers.
- The 1.2.1 sample datasets do not declare an S-411 application-schema namespace on the dataset root; the reader keys off the S-100 `productIdentifier` element and the well-known feature type local-names from the 1.2.1 Feature Catalogue.
- Coordinate ordering in `<gml:pos>` / `<gml:posList>` follows the S-100 Part 10b convention of **lat lon** for `EPSG:4326`.
- The S-411 Portrayal Catalogue ships several top-level XSLT entry points (`mainRule`, plus per-ice-class rules such as `SeaiceClass1ARule`). Only `mainRule` is exposed as an active portrayal rule by default; class-specific rules are still loadable by name via `GetCompiledRule`.

## License

The bundled S-411 specification assets in `EncDotNet.S100.Specifications` are © JCOMM/IHO and used in accordance with their open-publication terms; see <https://github.com/iho-ohi/S-411-Product-Specification>.

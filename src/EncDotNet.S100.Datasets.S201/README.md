# EncDotNet.S100.Datasets.S201

Library for reading and portraying [IHO/IALA S-201](https://github.com/IALA-IGO/S-201_AtoN-Information)
(Aids to Navigation Information) datasets.

## What S-201 is — and how it differs from S-125

S-201 is the **IALA-led** S-100 product specification for
**authority-to-authority** Aids to Navigation data exchange. It carries
the rich, authoritative AtoN model used by IALA member authorities —
operational and technical attributes, equipment lifecycle, AIS-AtoN
routing, and so on. It is **not** intended for ECDIS display.

[`EncDotNet.S100.Datasets.S125`](../EncDotNet.S100.Datasets.S125/README.md)
covers overlapping physical objects (lights, buoys, beacons, AIS aids)
but is the leaner ECDIS-facing AtoN feature set defined by IHO. The two
specs share a problem domain but are **separate** standards with
separate Feature Catalogues, separate XSDs, separate Portrayal
Catalogues, and separate intended audiences.

| Use case | Spec |
|---|---|
| ECDIS display of AtoN | **S-125** |
| Authority-to-authority AtoN exchange (operational/technical detail) | **S-201** |

This library treats S-125 and S-201 as fully independent products. If
both an S-125 and an S-201 dataset cover the same area the viewer
renders them as independent layers; no merging or deduplication is
attempted.

## Features

- Parse S-201 GML datasets (S-100 Part 10b encoding using the S-100 GML
  5.0 profile; legacy 1.0 namespaces are also tolerated)
- Namespace-driven feature recognition — no hard-coded allow-list of
  the 62 concrete S-201 feature types
- Capture xlink cross-references and split them into:
  - **information references** (target is an information type — e.g.
    `AtoNStatus` → `AtonStatusInformation`,
    `Positioning` → `PositioningInformation`)
  - **feature references** (target is another feature — e.g.
    `theParentFeature` / `theSubordinateFeature` from the
    `Structure/Equipment` aggregation, `peer` from `Aggregations` /
    `Associations`)
- Project to the S-100 Part 9 FeatureXML neutral form
  (`Dataset/Features/*` plus `Dataset/InformationTypes/*`) consumed by
  the S-201 portrayal catalogue
- XSLT-based portrayal via the bundled S-201 Portrayal Catalogue
  (top-level template `main_PaperChart.xsl`)

## Overview

Key types:

- **`S201Dataset`** — root model containing parsed features,
  information types, and dataset identification. Provides
  `ResolveReferencedFeatures` / `ResolveReferencedInformationTypes`
  helpers for walking xlinks by role name.
- **`S201Feature`** — a geographic feature with type code, geometry,
  simple/complex attributes, information references, and feature
  references. Implements `IGmlFeature`.
- **`S201InformationType`** — an information type instance (e.g.
  `AtonStatusInformation`, `PositioningInformation`,
  `AtoNFixingMethod`, `SpatialQuality`). Implements
  `IGmlInformationType`.
- **`S201InformationReference`** — a feature → information-type
  binding captured from an `xlink:href` attribute.
- **`S201FeatureReference`** — a feature → feature binding (e.g.
  equipment ↔ host structure) captured from an `xlink:href`
  attribute.
- **`S201ComplexAttribute`** — a complex attribute group with
  sub-attribute values. Implements `IGmlComplexAttribute`.
- **`S201FeatureXmlSource`** — `IFeatureXmlSource` adapter that
  projects an `S201Dataset` into the synthesised
  `Dataset/Features/*` shape that the S-201 XSLT rules match against.
- **`S201PortrayalCatalogue`** — `IVectorPortrayalCatalogue`
  implementation that loads XSLT rules, symbols, line styles, area
  fills, and color palettes from the bundled catalogue.

## Notes

- S-201 Edition 2.0.0 application schema namespace is
  `http://www.iho.int/S-201/gml/cs0/1.0`. Note the legacy-style hyphen
  and `/gml/cs0/1.0` suffix; this is intentional and distinct from
  the cleaner `S125/1.0` form.
- Geometry uses the S-100 GML 5.0 profile namespace
  `http://www.iho.int/s100gml/5.0`. The reader is also tolerant of
  the older S-100 GML 1.0 profile namespaces still seen in some
  pre-publication encoders.
- Coordinate ordering in `<gml:pos>` / `<gml:posList>` follows the
  S-100 Part 10b §6.2 convention of **lat lon** for `EPSG:4326`.
- The bundled portrayal catalogue is taken from the IALA-IGO upstream
  repository at commit `7ddfe8145812141fb8ca413107254f42febd893e`; see
  [`EncDotNet.S100.Specifications/content/S201/README.md`](../EncDotNet.S100.Specifications/content/S201/README.md)
  for full provenance and the upstream → bundled rename mapping. The
  catalogue ships a single Day-only color profile.
- Renderers must tolerate geometry-less features — abstract
  supertypes such as `AidsToNavigation`, `StructureObject`, and
  `Equipment`, plus aggregation containers like `AtonAggregation` and
  `AtonAssociation`, may carry no geometry.
- Time validity (`fixedDateRange`, `periodicDateRange`) is interpreted
  as UTC; do not coerce to local time at the source.

## License

The bundled S-201 specification assets in `EncDotNet.S100.Specifications`
are © IALA and used in accordance with their open-publication terms;
see <https://github.com/IALA-IGO/S-201_AtoN-Information>.

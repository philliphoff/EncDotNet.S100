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
- **`GmlGeometryType`** — shared enum (from `EncDotNet.S100.Core`) describing the geometry primitive type of a feature.
- **`S411FeatureXmlSource`** — `IFeatureXmlSource` adapter that projects an `S411Dataset` into S-100 Part 9 FeatureXML for XSLT portrayal rules.
- **`S411FeatureGeometryProvider`** — `IFeatureGeometryProvider` adapter for the unified Mapsui display-list renderer.
- **`S411PortrayalCatalogue`** — `IVectorPortrayalCatalogue` implementation that loads XSLT rules, symbols, line styles, area fills, and color palettes.

## Typed data model

In addition to the raw `S411Dataset` / `S411Feature` shapes used by the
portrayal pipeline, the library exposes a strongly-typed projection under
`EncDotNet.S100.Datasets.S411.DataModel` built on the shared
`EncDotNet.S100.DataModel` abstractions in `EncDotNet.S100.Core`. This mirrors
the typed projections added for S-124, S-125, S-128, and S-201 (see PRs
[#69](https://github.com/philliphoff/EncDotNet.S100/pull/69),
[#70](https://github.com/philliphoff/EncDotNet.S100/pull/70),
[#71](https://github.com/philliphoff/EncDotNet.S100/pull/71), and
[#72](https://github.com/philliphoff/EncDotNet.S100/pull/72)).

Key types:

- **`S411SeaIceInventory`** — top-level projection with a static
  `From(S411Dataset, out IReadOnlyList<ProjectionDiagnostic>)` factory that
  walks the source feature bag and produces typed subclasses.
- **`S411IceFeature`** — abstract base. Concrete subclasses:
  `S411SeaIce`, `S411LakeIce`, `S411Iceberg`, `S411IceEdge`, `S411IceLead`,
  `S411IceThickness`, `S411SnowCover`, `S411StageOfMelt`,
  `S411DataCoverage`, and `S411OtherFeature` (catch-all).
- **`S411EggCode`** — typed bundle for the WMO egg-code attributes carried
  by `SeaIce` / `LakeIce` features. Both vocabularies (JCOMM
  `iceact`/`iceapc`/`icesod`/`iceflz` and the IHO 1.2.1 sample
  `totalConcentration`/`snowDepth`) feed the same shape. List-valued JCOMM
  attributes are preserved as raw text because real-world producers
  serialise them as Python-list-style strings rather than the standard
  WMO tokenisation.
- **`S411GeometryKind`** — `None` / `Point` / `Curve` / `Surface`.

Feature-type normalisation maps the JCOMM lowercase short codes
(`seaice`, `lacice`, `icebrg`, `icelne`, `icethk`, `snwcvr`, `stgmlt`, …)
to the canonical PascalCase Feature Catalogue class names. Both GML
shapes therefore land on the same typed subclass, and consumers can
dispatch on `NormalizedFeatureType` without caring which shape the
dataset was emitted in. The raw element name remains available on
`SourceFeatureType`.

S-411 carries no information types and no xlink cross-references, so
the projection does not need an `XlinkResolver` graph; it still threads
a `ProjectionContext` so attribute-parse failures surface as
`ProjectionDiagnostic` entries rather than exceptions. The projection
only throws when the source dataset has no features at all.

Example:

```csharp
using var s = File.OpenRead("ice.gml");
var dataset = S411Dataset.Open(s);
var inventory = S411SeaIceInventory.From(dataset, out var diagnostics);

foreach (var seaIce in inventory.IceFeatures.OfType<S411SeaIce>())
{
    var conc = seaIce.EggCode?.TotalConcentration;
    // ... dispatch on seaIce.NormalizedFeatureType / seaIce.GeometryKind
}
```

## Notes

### Two GML shapes in the wild

S-411 1.2.1 datasets are encountered in two distinctly different XML shapes,
both of which this reader handles:

1. **JCOMM / Canadian-Ice-Service operational shape** (the common case in
   real-world data). Root element is
   `<ice:IceDataSet xmlns:ice="http://www.jcomm.info/ice">`, members are
   wrapped one-per-`<ice:IceFeatureMember>`, and feature elements use the
   short lowercase codes (`ice:seaice`, `ice:icebrg`, `ice:lacice`,
   `ice:icelne`, …). Geometry is inline as a direct `<gml:Polygon>` /
   `<gml:LineString>` / `<gml:Point>` child. The bundled portrayal catalogue
   was authored against this shape.

2. **IHO 1.2.1 sample shape** (transitional; only seen in the official IHO
   `S-411-Product-Specification` repository's `samples/` folder). Root is a
   bare `<Dataset>` with a plural `<members>` wrapper holding many feature
   siblings, and feature class names are PascalCase (`SeaIce`, `Iceberg`,
   …). The dataset-identification block declares the spec via
   `<S100:productIdentifier>S-411</S100:productIdentifier>`.

The reader dispatches on the root element and produces an `S411Dataset` from
either. The original parsed `XDocument` is preserved on `S411Dataset.SourceDocument`
and passed through unchanged to the XSLT portrayal pipeline so that the
official catalogue's element names and namespaces are honoured exactly.

### Portrayal catalogue

The bundled `pc/` tree under `EncDotNet.S100.Specifications` is **byte-identical
to the upstream catalogue** at
[iho-ohi/S-411-Product-Specification](https://github.com/iho-ohi/S-411-Product-Specification)
(version 1.2.1). No edits, additions, or `<ruleFile>` insertions are made.

The upstream `mainRule` (`pc/Rules/main.xsl`) emits a display-list dialect
that is incompatible with this codebase's `Part9DisplayListReader` (it uses
`<symbol><symbolReference>X</symbolReference></symbol>` instead of
`<symbol reference="X"/>`, etc.). To preserve the catalogue intact while
still rendering, this library ships an embedded adapter at
`Adapter/main.xsl`. `S411PortrayalCatalogue.GetCompiledRule("mainRule")`
substitutes the adapter for the catalogue's `mainRule` only; all other rule
references (sub-templates, simple-symbol templates, etc.) are loaded from
the unmodified PC. The adapter handles both GML shapes described above.

### Other

- S-411 has no information types (`<imember>` elements); only feature wrappers.
- Coordinate ordering in `<gml:pos>` / `<gml:posList>` follows the S-100 Part 10b convention of **lat lon** for `EPSG:4326`.
- The S-411 Portrayal Catalogue ships several top-level XSLT entry points (`mainRule`, plus per-ice-class rules such as `SeaiceClass1ARule`). Only `mainRule` is exposed as an active portrayal rule by default; class-specific rules are still loadable by name via `GetCompiledRule`.

## License

The bundled S-411 specification assets in `EncDotNet.S100.Specifications` are © JCOMM/IHO and used in accordance with their open-publication terms; see <https://github.com/iho-ohi/S-411-Product-Specification>.

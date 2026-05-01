---
name: s411-sea-ice
description: |
  Expert knowledge of IHO/JCOMM S-411 Ice Information for Surface
  Navigation Product Specification. Covers GML encoding (S-100 Part
  10b) of sea-ice and lake-ice features (`SeaIce`, `LakeIce`,
  `Iceberg`, `IceEdge`, `IceLead`, etc.), the two GML shapes
  encountered in real-world data (JCOMM/Canadian-Ice-Service
  operational shape and IHO 1.2.1 sample shape), the XSLT-based
  portrayal pipeline, and the bundled main-rule adapter that bridges
  the upstream catalogue to this codebase's `Part9DisplayListReader`.
  USE FOR: S-411 datasets, sea-ice information, ice edges, icebergs,
  ice concentration / stage of development, GML parsing for S-411,
  XSLT portrayal of ice, vector pipeline changes affecting S-411,
  S-411 reader/source code, S-411 tests, edits to the bundled
  `Adapter/main.xsl`. DO NOT USE FOR: S-124 nav warnings (use
  s124-nav-warnings), S-129 UKC (use s129-ukc), S-421 route plans
  (use s421-route-plans), generic GML / framework concerns (use
  s100-framework).
---

# S-411 Sea Ice expert

## When engaged
- Tasks touching `src/EncDotNet.S100.Datasets.S411/**`
- Edits to the bundled S-411 portrayal catalogue assets under
  `src/EncDotNet.S100.Specifications/content/S411/**`
- GML/XSLT portrayal changes for sea-ice and lake-ice features
- Vector pipeline changes (`IVectorSource`, `VectorPipeline`)
  affecting S-411

## Spec anchors
- Canonical: **S-411 Edition 1.2.1** Ice Information PS (IHO/JCOMM)
- Upstream catalogue:
  <https://github.com/iho-ohi/S-411-Product-Specification>
- S-100 Part 10b: GML encoding
- S-100 Part 9: Portrayal (XSLT path)
- S-411 Annex A: Feature Catalogue
- S-411 Annex B: Portrayal Catalogue (XSLT)

## Two GML shapes (both supported)
1. **JCOMM / Canadian-Ice-Service operational shape** — root
   `<ice:IceDataSet xmlns:ice="http://www.jcomm.info/ice">`, members
   wrapped one-per-`<ice:IceFeatureMember>`, feature class names use
   short lowercase codes (`ice:seaice`, `ice:icebrg`, `ice:lacice`,
   `ice:icelne`, …), geometry inline as direct
   `<gml:Polygon>` / `<gml:LineString>` / `<gml:Point>`.
2. **IHO 1.2.1 sample shape** — bare `<Dataset>` root, plural
   `<members>` wrapper, PascalCase feature names (`SeaIce`,
   `Iceberg`, …), product identifier
   `<S100:productIdentifier>S-411</S100:productIdentifier>`.

The reader dispatches on root element and produces an `S411Dataset`
from either; the original `XDocument` is preserved on
`S411Dataset.SourceDocument` and passed unchanged to the XSLT
portrayal pipeline.

## Review checklist
1. GML parsing must accept both shapes above; do not normalise to a
   single shape before XSLT runs (the catalogue is byte-identical to
   upstream and expects the original element names/namespaces).
2. The bundled `pc/` tree under `EncDotNet.S100.Specifications/content/S411/`
   is **byte-identical to upstream** — do not edit it. If the
   upstream `mainRule` needs adapting for this codebase's display-list
   dialect, modify the embedded `Adapter/main.xsl` only and have
   `S411PortrayalCatalogue.GetCompiledRule("mainRule")` substitute
   it for that single rule reference.
3. Coordinate ordering in `<gml:pos>` / `<gml:posList>` is **lat lon**
   for `EPSG:4326` (S-100 Part 10b convention).
4. S-411 has no information types (`<imember>`); only feature
   wrappers — do not add information-type plumbing.
5. Geometry primitives map to the renderer's `DrawingInstruction` set
   the same way other vector specs do — keep the abstraction shared.
6. XSLT must stay within features supported by .NET's
   `XslCompiledTransform`.
7. Public API changes have xunit tests; synthetic GML fixtures live
   under `tests/datasets/`.

## Known pitfalls in this repo
- The upstream `mainRule` emits `<symbol><symbolReference>X</symbolReference></symbol>`
  rather than `<symbol reference="X"/>`; rendering relies on the
  `Adapter/main.xsl` substitution — do not "fix" the upstream rule.
- Several top-level XSLT entry points exist
  (`SeaiceClass1ARule`, etc.); only `mainRule` is exposed as an
  active portrayal rule by default. Class-specific rules are still
  loadable by name via `GetCompiledRule`.
- Cancellation/empty-geometry features can occur — renderer must
  tolerate geometry-less features.
- Bundled spec assets are © JCOMM/IHO; redistribution must follow
  the upstream open-publication terms.

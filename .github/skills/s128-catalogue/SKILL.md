---
name: s128-catalogue
description: |
  Expert knowledge of IHO S-128 Catalogue of Nautical Products
  Product Specification (Edition 2.0.0). Covers GML encoding (S-100
  Part 10b) in the application namespace
  `http://www.iho.int/S128/2.0` over the S-100 GML 5.0 base, the
  S-128 feature catalogue (ElectronicProduct, PhysicalProduct,
  S100Service, plus producer / distributor / contact metadata
  features), and XSLT-based portrayal.
  USE FOR: S-128 datasets, catalogues of nautical products,
  electronic products, physical products, S-100 services, GML
  parsing for S-128, XSLT portrayal of S-128, vector pipeline
  changes affecting S-128, S-128 reader/source code, S-128 tests,
  edits to bundled `content/S128/**` assets.
  DO NOT USE FOR: S-127 marine resources and services (use
  s127-marine-services), S-122 marine protected areas (use
  s122-marine-protected-areas), S-124 nav warnings (use
  s124-nav-warnings), S-101 ENC (use s101-enc), generic GML
  / framework concerns (use s100-framework).
---

# S-128 Catalogue of Nautical Products expert

## When engaged
- Tasks touching `src/EncDotNet.S100.Datasets.S128/**`,
  `tests/EncDotNet.S100.Datasets.S128.Tests/**`,
  `src/EncDotNet.S100.Datasets.Pipelines/S128DatasetProcessor.cs`,
  or `src/EncDotNet.S100.Specifications/content/S128/**`
- GML/XSLT portrayal changes for S-128 features
- Vector pipeline changes (`IVectorSource`, `VectorPipeline`)
  affecting S-128

## Spec anchors
- Canonical: **S-128 Edition 2.0.0** Catalogue of Nautical Products PS
- S-100 Part 10b: GML encoding (uses the **S-100 GML 5.0** namespace
  `http://www.iho.int/s100gml/5.0`)
- S-100 Part 9: Portrayal (XSLT path; `main.xsl` + per-feature
  sub-templates including a `Default.xsl` fallback)
- S-128 application namespace: `http://www.iho.int/S128/2.0`

## Feature catalogue overview
S-128 datasets describe a catalogue (typically by-region) of the
nautical products themselves — they are *not* navigation data. The
reader surfaces three product feature classes plus a set of metadata
features:

| Feature class            | Role                                  | Portrayal |
|--------------------------|---------------------------------------|-----------|
| `ElectronicProduct`      | Digital nautical products (ENC, etc.) | Yellow area fill (`CHYLW`, transparency 0.30) |
| `PhysicalProduct`        | Paper charts / paper publications     | Green area fill (`CHGRN`, transparency 0.30)  |
| `S100Service`            | Service endpoints (e.g. SECOM)        | Magenta area fill (`CHMGD`, transparency 0.30)|
| `DistributorInformation` | Producer / distributor metadata       | None (handled via `Default.xsl`) |
| `ProducerInformation`    | Producer metadata                     | None |
| `ContactDetails`         | Contact records                       | None |
| `CatalogueSectionHeader` | Logical grouping inside the dataset   | None |

`S128ProductEntry` is the strongly-typed façade over the three
product feature classes; consumers should query through
`S128CatalogueQuery` rather than poking the raw feature graph.

## Review checklist
1. GML parsing accepts the canonical `s100gml/5.0` namespace and
   tolerates the legacy 1.0 profile namespaces
   (`http://www.iho.int/s100gml/1.0`,
   `http://www.iho.int/S100/profile/s100gml/1.0`).
2. Coordinate ordering in `<gml:pos>` / `<gml:posList>` is **lat lon**
   for `EPSG:4326` (S-100 Part 10b §6.2). Do not assume lon,lat.
3. Feature recognition is namespace-driven: any `<member>` /
   `<members>` child whose namespace matches the dataset's
   application schema is surfaced as an `S128Feature`. Do not
   introduce a hard-coded feature-type allow-list — the bundled
   `main.xsl` walks `Dataset/Features/*` so new feature classes
   surface automatically once the FC adds them.
4. Geometry on a feature must be wrapped in a `<S128:geometry>`
   element; the reader's geometry search starts inside that wrapper.
5. `<imember>` / `<imembers>` plumbing is preserved for forward
   compatibility but expected to be empty for 2.0.0 datasets
   (S-128 Edition 2.0.0 has no information types).
6. Container-style / metadata features (`DistributorInformation`,
   `CatalogueSectionHeader`, etc.) may have no geometry; renderers
   must tolerate geometry-less features.
7. Portrayal flows through XSLT (not Lua); keep transforms to
   features supported by .NET's `XslCompiledTransform`.
8. Status heuristic: prefer `serviceStatus`
   (1 Planned / 2 InForce / 3 Withdrawn), fall back to
   `distributionStatus` (1 InForce / 2 Withdrawn), default to
   `InForce`. Superseded resolution via `theReference` xlink with
   `ProductMapping/categoryOfProductMapping=1` is not yet wired —
   document this gap when extending `S128ProductEntry`.
9. The bundled portrayal catalogue under `content/S128/{fc,pc}/` is
   byte-identical to upstream
   ([`iho-ohi/S-128-Product-Specification-Development`
   @ `c266c43820ce…`](https://github.com/iho-ohi/S-128-Product-Specification-Development)).
   Do not edit those files. If upstream needs adapting for this
   codebase, add an adapter (mirror the S-411 pattern) rather than
   editing the catalogue files directly.
10. Public API changes have xunit tests; synthetic GML fixtures
    belong under `tests/datasets/S128/`. The official 2.0.0 sample
    `S128_TDS_sample.gml` is committed there for end-to-end tests.

## Known pitfalls in this repo
- The producer-bug compensations are inherited from the S-122
  reader: `s100gml` namespace tolerance, `<member>` and `<members>`
  wrappers, comma-and-whitespace `posList` tokens, and the lon-lat
  axis-order swap heuristic against `<gml:Envelope>`. Preserve all
  four behaviours when refactoring.
- Per-feature XSLT rules emit area fills as
  `<color transparency="0.30">CHGRN</color>` — i.e. transparency on
  the `<color>` attribute, NOT a child `<transparency>` element.
  `Part9DisplayListReader` accepts both forms; do not regress.
- The official sample contains a giant `PhysicalProduct` (CNP00004,
  ~lat 27–44, lon 120–135) that visually overlaps every other
  product. This is a feature-of-the-sample, not a bug — area
  ordering / selection is a renderer-side UX concern.
- `S128PortrayalCatalogue` is a clone of the S-122 catalogue with
  Day/Dusk/Night enabled (the upstream PC ships all three palettes).

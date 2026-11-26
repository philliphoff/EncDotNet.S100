---
name: s128-catalogue
description: |
  IHO S-128 Catalogue of Nautical Products (Edition 2.0.0, GML,
  application namespace `http://www.iho.int/S128/2.0`): product
  feature classes (ElectronicProduct, PhysicalProduct, S100Service),
  metadata records, the product-entry facade and status heuristic,
  the typed catalogue with supersession, and the outline-only XSLT
  adapter. Load `s100-gml-products` with it.
  USE FOR: `src/EncDotNet.S100.Datasets.S128/**`,
  `tests/EncDotNet.S100.Datasets.S128.Tests/**`,
  `S128DatasetProcessor`, `S128CoverageOverlayTests`,
  `content/S128/**`, `tests/datasets/S128/**`; catalogues of nautical
  products, product coverages, product status/supersession.
  DO NOT USE FOR: S-127 marine services (s127-marine-services), S-122
  (s122-marine-protected-areas), S-124 (s124-nav-warnings), S-101 ENC
  (s101-enc), rules shared by all GML products (s100-gml-products).
---

# S-128 Catalogue of Nautical Products

Load `s100-gml-products` too; this skill records only S-128 specifics.

## Code map
- `src/EncDotNet.S100.Datasets.S128/` — `S128DatasetReader`,
  `S128Dataset`, `S128ProductEntry` + `S128CatalogueQuery`,
  `DataModel/S128ProductCatalogue`, `Validation/S128CatalogueRules`,
  `S128FeatureXmlSource`, `S128PortrayalCatalogue`,
  `Adapter/outlineOnly.xsl` (embedded resource). The README is the
  reference for status, supersession and encoding notes.
- Processor: `src/EncDotNet.S100.Datasets.Pipelines/S128DatasetProcessor.cs`.
- Upstream catalogue: `content/S128/{fc,pc}/`, pinned to
  `iho-ohi/S-128-Product-Specification-Development` @ `c266c438…`
  (PC version 1.1.0; Day/Dusk/Night palettes).
- Fixtures: `tests/datasets/S128/S128_TDS_sample.gml` (official 2.0.0
  sample, inline `<S128:members>`).

## Feature model
- Product classes: `ElectronicProduct`, `PhysicalProduct`,
  `S100Service` (`S128ProductEntry.ProductFeatureTypes`).
- The FC models `CatalogueSectionHeader`, `ContactDetails`,
  `ProducerInformation`, `DistributorInformation`,
  `IndicationOfCarriageRequirement` and `PriceInformation` as
  information types, but the upstream sample encodes the metadata
  records as features inside `<members>`; `<imember>` is usually
  empty. Don't "fix" this in the reader.
- Feature geometry sits inside a `<S128:geometry>` wrapper; the
  reader's geometry search starts there.
- `S128ProductEntry` is the only strongly-typed view over raw product
  features; keep the reader FC-faithful. `S128ProductCatalogue`
  (DataModel) is the richer typed projection.

## Status and supersession
- `S128ProductEntry.Status` is a **heuristic**: `serviceStatus`
  (1 Planned / 2 InForce / 3 Withdrawn), else `distributionStatus`
  (1 InForce / 2 Withdrawn), else `InForce`. Any change must also
  update the README's "Product status" section.
- Supersession is not in `Status`. `S128ProductCatalogue` resolves
  `theReference` xlinks with `ProductMapping/categoryOfProductMapping=1`
  into `Supersedes` / `SupersededBy`; other categories go to
  `RelatedProducts`.

## Portrayal
- Upstream rules fill coverages (`CHYLW`/`CHGRN`/`CHMGD`,
  transparency 0.30) on `OVERRADAR`; stacked over an ENC they hide
  the chart. `S128PortrayalCatalogue` swaps the `main` rule for
  `Adapter/outlineOnly.xsl`, which `xsl:import`s upstream `main.xsl`
  and drops the `areaInstruction` for the three product classes
  (upstream issue #51, Lua port PR #56). Keep it until the bundled
  catalogue is refreshed past upstream #56;
  `tests/EncDotNet.S100.Pipelines.Tests/S128CoverageOverlayTests.cs`
  fails when that happens (then remove the adapter). Lua catalogue
  migration: issue #763.
- Upstream rules write transparency as an attribute
  (`<color transparency="0.30">`), not a child element;
  `Part9DisplayListReader` accepts both — don't regress.

## Known pitfalls
- S-128 GML 1.0 catalogues (IC-ENC, Denmark) use `gml:gmlId` instead
  of `gml:id`, polygons without `<gml:exterior>`, and one ordinate per
  `<gml:pos>`; the reader / shared parser recover all three. Full
  GML 1.0 feature-class portrayal is still open (#247).
- The upstream sample has no `<gml:Envelope>`, so its axis order is
  reported `Unverified`.
- The sample's giant `PhysicalProduct` CNP00004 (lat 27–44,
  lon 120–135) overlaps everything; that is the data, not a bug.

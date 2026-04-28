---
name: s100-framework
description: |
  Expert knowledge of the IHO S-100 Universal Hydrographic Data Model
  (Edition 5.2.1). Covers Parts 1–10: data model, feature catalogues
  (ISO 19110), portrayal catalogues, exchange sets (CATALOG.XML),
  HDF5 encoding (Part 10c), GML encoding (Part 10b), ISO 8211 encoding
  (Part 10a), and the Lua-based portrayal engine (Part 9A). USE FOR:
  S-100 framework questions, exchange set layout, CATALOG.XML schema,
  feature/portrayal catalogue structure, cross-product concerns,
  changes to EncDotNet.S100.Core, EncDotNet.S100.ExchangeSets,
  EncDotNet.S100.Features, EncDotNet.S100.Portrayals, or
  EncDotNet.S100.Specifications, choosing an encoding for a new
  product, designing pipeline abstractions. DO NOT USE FOR: a single
  product spec (use s101-enc, s102-bathymetry, s104-water-level,
  s111-surface-currents, s124-nav-warnings, s129-ukc, or s421-route-plans).
---

# S-100 framework expert

## When engaged
- Tasks touching `src/EncDotNet.S100.Core/**`,
  `src/EncDotNet.S100.ExchangeSets/**`, `src/EncDotNet.S100.Features/**`,
  `src/EncDotNet.S100.Portrayals/**`, `src/EncDotNet.S100.Specifications/**`
- Cross-product feature work (a change that affects more than one S-10x
  dataset library)
- Exchange set discovery, `CATALOG.XML`, asset source design
- Adding support for a new S-10x product

## Spec anchors
- Canonical: **S-100 Edition 5.2.1** — `docs/specs/S-100 Ed 5.2.1_FINAL.pdf`
  (markdown extract under `docs/specs/s100/` when available)
- Part 1: General principles
- Part 3: General Feature Model (GFM)
- Part 5: Feature Catalogue (ISO 19110)
- Part 8: Coverages
- Part 9 / 9A: Portrayal (XML catalogue + Lua rules)
- Part 10a: ISO 8211 encoding
- Part 10b: GML encoding
- Part 10c: HDF5 encoding
- Part 17: Exchange Sets

## Review checklist
1. Does the change respect the abstraction-first pattern? Concrete I/O
   (`PureHdfFile`, `MoonSharpLuaEngine`, `FileSystemAssetSource`,
   `ZipAssetSource`) stays behind interfaces in `EncDotNet.S100.Core`.
2. New product support: is it added as `EncDotNet.S100.Datasets.Sxxx`
   following the existing layout (Reader → Source → Pipeline plumbing)?
3. Does it pick the correct pipeline shape — *coverage* (`ICoverageSource`)
   for gridded products or *vector* (`IVectorSource` +
   `IVectorPortrayalCatalogue`) for feature-based products?
4. Bundled spec assets land under
   `src/EncDotNet.S100.Specifications/content/<SXXX>/` and are exposed via
   `Specification.OpenFeatureCatalogueAsync` /
   `Specification.CreatePortrayalCatalogueSource`.
5. Exchange set parsing changes preserve compatibility with both ZIP and
   filesystem asset sources.
6. New public APIs have XML doc comments and xunit tests (use
   `SkippableFact` when real data files are required).

## Known pitfalls in this repo
- Do not assume an exchange set is a ZIP — both ZIP and directory layouts
  must work via `IAssetSource`.
- Feature catalogues use ISO 19110 namespaces; do not hand-roll XML
  parsing — extend `EncDotNet.S100.Features` instead.
- Portrayal catalogues differ in shape between vector products
  (Lua rules) and coverage products (palette/colour tables); keep these
  paths separate.

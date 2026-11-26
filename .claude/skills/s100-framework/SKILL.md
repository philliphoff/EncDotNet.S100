---
name: s100-framework
description: |
  Expert knowledge of the IHO S-100 Universal Hydrographic Data Model
  (Edition 5.2.1): data model, feature catalogues (ISO 19110),
  portrayal catalogues, exchange sets (CATALOG.XML), ISO 8211 / GML /
  HDF5 encodings (Part 10a/b/c), and the Lua portrayal engine (Part 9A).
  USE FOR: S-100 framework questions, exchange set layout, CATALOG.XML,
  feature/portrayal catalogue structure, cross-product concerns, the Lua
  engines (Lua-CSharp / MoonSharp), the HDF5 abstraction, changes to
  EncDotNet.S100.Core, EncDotNet.S100.ExchangeSets,
  EncDotNet.S100.Features, EncDotNet.S100.Portrayals,
  EncDotNet.S100.Specifications, EncDotNet.S100.Scripting.*,
  EncDotNet.S100.Hdf5.PureHdf, product registration in
  EncDotNet.S100.Datasets.Pipelines, choosing an encoding or pipeline
  shape for a new product. DO NOT USE FOR: a single product spec (use
  that product's skill, e.g. s101-enc, s102-bathymetry), GML-product
  shared rules (s100-gml-products), rendering/cartography
  (chart-cartography).
---

# S-100 framework expert

## When engaged
- Tasks touching `src/EncDotNet.S100.Core/**`,
  `src/EncDotNet.S100.ExchangeSets/**`, `src/EncDotNet.S100.Features/**`,
  `src/EncDotNet.S100.Portrayals/**`, `src/EncDotNet.S100.Specifications/**`,
  `src/EncDotNet.S100.Scripting.*/**`, `src/EncDotNet.S100.Hdf5.PureHdf/**`
- Cross-product work (a change that affects more than one S-1xx
  dataset library), product registration, exchange set discovery,
  `CATALOG.XML`, asset source design
- Adding support for a new S-1xx product

## Spec anchors
- Canonical: **S-100 Edition 5.2.1** — `docs/specs/S-100 Ed 5.2.1_FINAL.pdf`
- Part 3: General Feature Model
- Part 4: Metadata
- Part 5: Feature Catalogue (ISO 19110)
- Part 8: Imagery and Gridded Data (coverages)
- Part 9 / 9a: Portrayal (XML catalogue / Lua rules); Part 13: Scripting
- Part 10a / 10b / 10c: ISO 8211, GML, HDF5 encodings
- Part 15: Encryption and Data Protection (see s100-data-protection)
- Part 16 / 16a: Interoperability catalogue, harmonised portrayal
- Part 17: Discovery metadata for exchange catalogues (`CATALOG.XML`)

## Review checklist
1. Abstraction-first: concrete I/O stays behind interfaces in
   `EncDotNet.S100.Core` — `IHdf5File` (Core/Hdf5; `PureHdfFile` in
   `EncDotNet.S100.Hdf5.PureHdf`), `ILuaEngine` (Core/Scripting;
   `LuaCSharpLuaEngine` / `MoonSharpLuaEngine` in
   `EncDotNet.S100.Scripting.LuaCSharp` / `.MoonSharp`), `IAssetSource`
   (`FileSystemAssetSource`, `ZipAssetSource`).
2. New product: a reader/model project `EncDotNet.S100.Datasets.Sxxx`
   (Reader → Source → pipeline plumbing), an `IDatasetProcessor` in
   `EncDotNet.S100.Datasets.Pipelines`, and a `ProductRegistration` in
   `Registry/S100Products.cs` — the one place that knows how a product
   is recognised and constructed. Don't add product `switch`es elsewhere.
3. Pipeline shape: *coverage* (`ICoverageSource` + `ICoverageStyle`,
   Core/Pipelines/Coverage) for gridded products, *vector*
   (`IVectorSource` + `IVectorPortrayalCatalogue`, Core/Pipelines/Vector)
   for feature-based products.
4. Bundled spec assets live under
   `src/EncDotNet.S100.Specifications/content/<SXXX>/`, byte-identical to
   upstream, and are reached only through `Specification.*`
   (`OpenFeatureCatalogueAsync`, `CreatePortrayalCatalogueSource`) — no
   ad-hoc resource loading or hard-coded paths in dataset libraries.
5. Exchange set parsing works for both ZIP and directory layouts via
   `IAssetSource`.
6. Lua: hosts use `LuaEngineKinds.Default` (Lua-CSharp); MoonSharp stays
   selectable, and `S100_LUA_ENGINE=luacsharp|moonsharp` overrides it for
   tests and tools. Both engines are Lua 5.2, hard-sandboxed (no `io`,
   `os`, `debug`, file system or reflection) — keep it that way. Engine
   contract tests are in `tests/EncDotNet.S100.Scripting.Tests`;
   `LuaEngineDifferentialTests` (Pipelines.Tests) checks both engines
   emit identical portrayal output across the corpora.

## Known pitfalls in this repo
- Feature catalogues use ISO 19110 namespaces; don't hand-roll XML
  parsing — extend `EncDotNet.S100.Features`.
- Portrayal catalogues differ in shape between Lua (S-101/S-401/S-131),
  XSLT (GML products) and coverage (palette/colour tables) products;
  keep these paths separate.
- Lua-CSharp needs the 5.2 compatibility shims in the LuaCSharp
  project's `Compatibility/`; MoonSharp's `table.sort` is stable where
  reference Lua's isn't — the differential test neutralises that one
  divergence only.

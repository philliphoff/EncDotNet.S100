# Top APIs by package

## Why it matters

Generated API trees are complete but can be hard to navigate on first contact.
This page highlights the highest-value entry points per package.

## Quick win

If you only pick three APIs to start, use:

- `S100Dataset.Open(...)`
- `PngS100DatasetRenderer.RenderAsync(...)`
- `S100FeatureCatalogue.Bundled(...)`

## Deep dive

## EncDotNet.S100

- `S100Dataset` — open and inspect datasets
- `PngS100DatasetRenderer` — one-call image rendering
- `S100FeatureCatalogue` — read decoded feature metadata
- `S100Layer` / `S100CompositeOptions` — multi-layer composition
- `S100Dataset.OpenAsync(source, relativePath)` / `S100ExchangeSet` — open
  datasets from a folder, a ZIP or an exchange set, with S-101 updates applied
  and optional Part 15 decryption (`WithDecryption`)

## EncDotNet.S100.Core

- `IAssetSource` with `FileSystemAssetSource`, `ZipAssetSource` and
  `CachingAssetSource` — where datasets, catalogues and support files are read
  from
- `CoveragePipeline` / `VectorPipeline`
- `DrawingInstruction` and instruction subclasses
- `MarinerSettings`, `Viewport`, `BoundingBox`
- Validation primitives (`ValidationRuleSet<TModel>`, `ValidationFinding`)
- Typed data-model helpers (`ProjectionDiagnostic`, `GeoPosition`) — see
  [Typed data models](typed-data-models.md)

## EncDotNet.S100.ExchangeSets

- `ExchangeSet.OpenAsync(source)` — open an exchange set's `CATALOG.XML`
  through an `IAssetSource`, then `FetchDatasetAsync` / `FetchSupportFileAsync`
- `ExchangeCatalogueReader` — parse a catalogue directly
- `ExchangeSetVerifier` — verify digital signatures and checksums
- `EncDotNet.S100.ExchangeSets.Protection` — S-100 Part 15 decryption:
  `PermitSignatureVerifier`, `PermitKeyProvider` and `DecryptingAssetSource`

## EncDotNet.S100.Datasets.Pipelines

- `DatasetPipelineFactory` — detect a file's product specification and create
  its `IDatasetProcessor`, including S-101 / S-57 update application
- `ExchangeSetLoader` — load every dataset in an exchange set

## EncDotNet.S100.Datasets.S101

- `S101Dataset.Open(...)` / `S101Dataset.OpenWithUpdates(...)`
- `S101DocumentReader` — the lower-level ISO 8211 record reader
- `S101PortrayalCatalogue` — the Lua (Part 9A) portrayal catalogue

## EncDotNet.S100.Datasets.S102 / S104 / S111

- `S102DatasetReader` / `S104DatasetReader` / `S111DatasetReader` — read an
  `IHdf5File` (open one with `PureHdfFile` from `EncDotNet.S100.Hdf5.PureHdf`)
- `S102CoverageSource` / `S104CoverageSource` / `S111CoverageSource` — grid and
  time-step access for the coverage pipeline

## EncDotNet.S100.Datasets (GML products)

- `SxxxDatasetReader.Read(stream)` for S-122, S-124, S-125, S-127, S-128,
  S-129, S-131, S-201, S-411 and S-421
- A typed data model for each of them, built with
  `Sxxx{Root}.From(dataset, out diagnostics)` (for example `S421RoutePlan`,
  `S124NavigationalWarning`) — see [Typed data models](typed-data-models.md)

## EncDotNet.S100.Datasets.S57

- `S57ToS101Translator` — translate a legacy S-57 cell into the S-101 model —
  see [Bringing S-57 into the pipeline](s57-to-s101.md)
- `S57ExchangeSetVerification` — CRC-check an S-57 / S-63 exchange set

## Catalogues

- `FeatureCatalogueReader` / `FeatureCatalogueManager`
  (`EncDotNet.S100.Features`) — S-100 Part 5 feature catalogues
- `PortrayalCatalogueReader` / `PortrayalCatalogueManager`
  (`EncDotNet.S100.Portrayals`) — S-100 Part 9 portrayal catalogues
- `Specification` (`EncDotNet.S100.Specifications`) — the bundled official
  catalogues

## Renderers

- `SkiaDisplayListRenderer`
- `HeadlessVectorRenderer`
- `HeadlessCompositeRenderer`
- `VectorScene` / `VectorSceneBuilder` (`EncDotNet.S100.Rendering.Scene`) — the
  backend-neutral scene every renderer draws — see
  [Embedding the renderer](embedding-the-renderer.md)
- `AddS100` / `AddS100Mapsui` (`EncDotNet.S100.Renderers.Mapsui`) and
  `S100MapControl` (`EncDotNet.S100.Renderers.Mapsui.Avalonia`) — interactive
  maps

## Backends

- `PureHdfFile` (`EncDotNet.S100.Hdf5.PureHdf`) — managed HDF5 reader
- `MoonSharpLuaEngine` (`EncDotNet.S100.Scripting.MoonSharp`) — Lua engine for
  Part 9A portrayal rules
- `ProjNetCrsTransformFactory` (`EncDotNet.S100.Crs.ProjNet`) — CRS transforms
  for projected grids

## Troubleshooting

> [!TIP]
> If you need the shortest path, prefer `EncDotNet.S100` facade APIs. Drop down to per-spec readers when you need custom catalogue wiring or low-level control.

## Next step

- [Getting started](getting-started.md)
- [Embedding the renderer](embedding-the-renderer.md)
- [API reference index](../api/index.md)

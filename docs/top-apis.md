# Top APIs

This page lists the main public entry points in each EncDotNet.S100 package.
For every type and member, see the [API reference](../api/index.md).

Start with the `EncDotNet.S100` facade package. It covers opening, reading,
validating and rendering a dataset with the bundled catalogues. Use the
per-product and lower-level packages when you need your own catalogue wiring
or more control over the pipeline.

## EncDotNet.S100

| API | Use it to |
|---|---|
| `S100Dataset.Open(path)` | Open a dataset file and detect its product specification. |
| `S100Dataset.OpenAsync(source, relativePath)` | Open a dataset inside an `IAssetSource`, such as a folder or a ZIP. |
| `S100ExchangeSet.OpenAsync(...)` | Open an exchange set from a folder, a `CATALOG.XML` or a ZIP, with S-101 updates applied. |
| `WithDecryption(keys)` | Decrypt an S-100 Part 15 protected exchange set. |
| `S100FeatureCatalogue.Bundled(spec)` | Read decoded features using the bundled feature catalogue. |
| `PngS100DatasetRenderer.RenderAsync(...)` | Render a dataset, or a list of `S100Layer` values, to PNG bytes. |
| `S100RendererOptions`, `S100CompositeOptions` | Set image size, palette, scale, time step and, for composites, the viewport and mariner settings. |
| `Validate()` | Run the product's bundled validation rules. |

[Getting started](getting-started.md#net-library) shows these together, and
[Loading datasets](loading-datasets.md) covers the open methods in detail.

## EncDotNet.S100.Core

- `IAssetSource`, with `FileSystemAssetSource`, `ZipAssetSource` and
  `CachingAssetSource`: where datasets, catalogues and support files are read
  from.
- `CoveragePipeline` and `VectorPipeline`: the portrayal pipelines.
- `DrawingInstruction` and its subclasses: the pipeline output.
- `MarinerSettings`, `Viewport` and `BoundingBox`.
- `ValidationRuleSet<TModel>` and `ValidationFinding`: validation building
  blocks.
- `ProjectionDiagnostic` and `GeoPosition`: shared types for the typed data
  models. See [Typed data models](typed-data-models.md).

## EncDotNet.S100.ExchangeSets

- `ExchangeSet.OpenAsync(source)`: open an exchange set's `CATALOG.XML`
  through an `IAssetSource`, then read its files with `FetchDatasetAsync` and
  `FetchSupportFileAsync`.
- `ExchangeCatalogueReader`: parse a catalogue directly.
- `ExchangeSetVerifier`: verify digital signatures and checksums.
- `EncDotNet.S100.ExchangeSets.Protection`: S-100 Part 15 protection.
  `PermitSignatureVerifier`, `PermitKeyProvider` and `DecryptingAssetSource`
  read protected data; `Part15Signer`, `PermitFileWriter` and `DataPermit`
  create it. See [Reading protected exchange sets](protected-exchange-sets.md).

## EncDotNet.S100.Datasets.Pipelines

- `DatasetPipelineFactory`: detect a file's product specification and create
  its `IDatasetProcessor`, including S-101 and S-57 update application.
- `ExchangeSetLoader`: load every dataset in an exchange set.

## EncDotNet.S100.Datasets.S101

- `S101Dataset.Open(...)` and `S101Dataset.OpenWithUpdates(...)`: read an
  ISO 8211 base cell, with or without its updates.
- `S101PortrayalCatalogue`: the Lua (S-100 Part 9A) portrayal catalogue.

## EncDotNet.S100.Datasets.S102, S104 and S111

- `S102DatasetReader`, `S104DatasetReader` and `S111DatasetReader`: read an
  `IHdf5File`. Open one with `PureHdfFile` from `EncDotNet.S100.Hdf5.PureHdf`.
- `S102CoverageSource`, `S104CoverageSource` and `S111CoverageSource`: grid
  and time-step access for the coverage pipeline.

## GML product packages

- `SxxxDataset.Open(path)` or `SxxxDataset.Open(stream)` for S-122, S-124,
  S-125, S-127, S-128, S-129, S-131, S-201, S-411 and S-421.
- A typed data model for each, built with
  `Sxxx{Root}.From(dataset, out diagnostics)`, for example `S421RoutePlan` and
  `S124NavigationalWarning`. See [Typed data models](typed-data-models.md).

## EncDotNet.S100.Datasets.S57

- `S57ToS101Translator`: translate an S-57 cell into the S-101 model. See
  [Bringing S-57 into the pipeline](s57-to-s101.md).
- `S57ExchangeSetVerification`: check the CRCs of an S-57 or S-63 exchange
  set.

## Catalogues

- `FeatureCatalogueReader` and `FeatureCatalogueManager`
  (`EncDotNet.S100.Features`): S-100 Part 5 feature catalogues.
- `PortrayalCatalogueReader` and `PortrayalCatalogueManager`
  (`EncDotNet.S100.Portrayals`): S-100 Part 9 portrayal catalogues.
- `Specification` (`EncDotNet.S100.Specifications`): the bundled official
  catalogues.

See [Custom catalogues and validation](catalogues-and-validation.md).

## Renderers

- `SkiaDisplayListRenderer`, `HeadlessVectorRenderer` and
  `HeadlessCompositeRenderer` (`EncDotNet.S100.Renderers.Skia`): headless
  rendering to images.
- `VectorScene` and `VectorSceneBuilder` (`EncDotNet.S100.Rendering.Scene`):
  the backend-neutral scene that every renderer draws.
- `AddS100` and `AddS100Mapsui` (`EncDotNet.S100.Renderers.Mapsui`), and
  `S100MapControl` (`EncDotNet.S100.Renderers.Mapsui.Avalonia`): interactive
  maps.

See [Embedding the renderer](embedding-the-renderer.md).

## Backends

- `PureHdfFile` (`EncDotNet.S100.Hdf5.PureHdf`): managed HDF5 reader.
- `MoonSharpLuaEngine` (`EncDotNet.S100.Scripting.MoonSharp`): Lua engine for
  S-100 Part 9A portrayal rules.
- `ProjNetCrsTransformFactory` (`EncDotNet.S100.Crs.ProjNet`): coordinate
  reference system transforms for projected grids.

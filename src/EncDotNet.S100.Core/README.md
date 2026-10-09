# EncDotNet.S100.Core

`EncDotNet.S100.Core` holds the abstractions and pipeline framework that the
other EncDotNet.S100 packages share: asset sources, HDF5 and Lua interfaces,
the coverage and vector portrayal pipelines, the validation framework, dataset
metadata, spec-version checks and dynamic feature sources. Reference it
directly when you write a product reader, an HDF5 or Lua backend, a validation
rule pack or a rendering host. If you only want to open and render datasets,
use the [`EncDotNet.S100`](../EncDotNet.S100/README.md) facade, which brings
this package in.

## Install

```bash
dotnet add package EncDotNet.S100.Core
```

## Example: read a file from a folder or ZIP

Readers get their files through `IAssetSource`, so the same code reads from a
folder or a ZIP archive:

```csharp
using EncDotNet.S100.Core;

using IAssetSource source = FileSystemAssetSource.Create("path/to/exchange-set");
// or: using IAssetSource source = ZipAssetSource.Create("path/to/exchange-set.zip");

await using Stream catalogue = await source.OpenAsync("CATALOG.XML");
```

## Main entry points

### Asset sources

`IAssetSource` (namespace `EncDotNet.S100.Core`) reads files by relative path.
`FileSystemAssetSource` reads from a folder, `ZipAssetSource` from a ZIP
archive, and `CachingAssetSource` caches reads from another source.

### HDF5

`IHdf5File` and `IHdf5Group` (namespace `EncDotNet.S100.Hdf5`) read HDF5 data
without binding to a specific HDF5 library.
[`EncDotNet.S100.Hdf5.PureHdf`](../EncDotNet.S100.Hdf5.PureHdf/README.md)
implements them.

HDF5 readers throw two exception types:

- `S100DatasetSchemaException`: a required attribute or group is missing or
  malformed.
- `S100DatasetNotSupportedException`: the file uses an optional part of the
  specification that the reader doesn't implement yet.

Both carry the product, file, group path and specification reference, and a
`WithFile(...)` helper that processors use to attach the source file name.
`S100DatasetSchemaException` also carries an optional `AdditionalContext`, set
with `WithAdditionalContext(...)` and kept across `WithFile(...)`. Readers use
it to add an explanation, such as a note that the dataset declares an
unexpected product-specification edition.

`Hdf5RequiredAttributeExtensions` provides `ReadRequiredDoubleAttribute`,
`ReadRequiredInt64Attribute` and `ReadRequiredStringAttribute`. They turn a
backend's "missing attribute" failure into these typed exceptions.

### Lua scripting

`ILuaEngine` and `ILuaContext` (namespace `EncDotNet.S100.Scripting`) run
sandboxed Lua portrayal scripts. `S100LuaHost` is the host API the scripts
call.
[`EncDotNet.S100.Scripting.MoonSharp`](../EncDotNet.S100.Scripting.MoonSharp/README.md)
implements the engine.

### Coverage pipeline

`ICoverageSource`, `ICoverageRenderer<T>` and `CoveragePipeline` (namespace
`EncDotNet.S100.Pipelines.Coverage`) render gridded data, with
`GridGeoreferencer`, `CoverageColorScheme` and `StyledCoverageLayer`.

- `ICoveragePortrayalCatalogue.ResolveColorScheme` returns
  `CoverageColorScheme?`. Catalogues that only emit symbols, such as S-111's
  arrow-only portrayal, return `null`. The coverage renderers throw if you pass
  them a layer with a `null` scheme.
- `CoveragePipeline.ProcessAsync` takes an optional `Viewport`. When you pass
  one, it uses `GridRegion.FromViewport` to sample only the cells inside the
  viewport, at the viewport's ground resolution. It clamps the subset to the
  intersection of the viewport and the grid extent, and picks a stride so the
  ground resolution isn't finer than the cell size. Without a viewport it
  samples the full grid.
- For a grid in a CRS other than EPSG:4326, also pass `wgs84ToNative`, an
  `ICrsTransform` from WGS 84 to the grid's CRS.
- The georeferencer on the resulting `StyledCoverageLayer` is built from the
  sampled subset's `GridMetadata`: its origin is adjusted for the subset and
  its spacing scaled by the stride. A sampled subset is drawn at its true
  location.
- Viewport sampling happens when the pipeline builds the layer.
- `ICoverageSource` can also serve an overview pyramid (S-100 Part 10c):
  `AvailableOverviewLevels` lists the levels and `SelectOverviewLevel` picks one.
  `CoveragePyramid` and `CoveragePyramidBuilder` (namespace
  `EncDotNet.S100.Pipelines.Coverage.Pyramid`) build one in memory.

### Vector pipeline

`IVectorSource`, `IVectorPortrayalCatalogue` and `VectorPipeline` (namespace
`EncDotNet.S100.Pipelines.Vector`) run vector portrayal. The output is the
`DrawingInstruction` hierarchy (`AreaInstruction`, `LineInstruction`,
`PointInstruction`, `TextInstruction`), modelled on the S-100 Part 9 display
list.

- Rule execution is pluggable behind `IVectorRuleExecutor`. There are two
  implementations:
  - `Pipelines.Vector.Xslt.XsltRuleExecutor` (S-100 Part 9 §9.4) gets the
    FeatureXML, selects rules, runs the XSLT transformation and assembles the
    display list.
  - `Pipelines.Vector.Lua.LuaRuleExecutor` (S-100 Part 9A) is one
    product-independent executor. Products supply their own parts through
    `ILuaDataProvider` and `ILuaDataProviderFactory` (the host bridge),
    `LuaContextParameterBinding` (mariner settings to context parameters),
    `IFeatureAnchorProvider` and `IDrawingInstructionTransform`. S-101 and
    S-131 share the executor this way.
- `VectorPipeline` runs the built-in XSLT executor, appends the output of an
  optional injected Lua executor, then filters by viewing group and display
  plane and sorts by priority. It contains no XSLT-specific code.
- `DrawingInstructionParser` parses Lua-emitted drawing instructions.
- `Part9DisplayListReader` parses the Part 9 display-list XML produced by the
  XSLT pipelines (S-124, S-129, S-421) into the same `DrawingInstruction`
  hierarchy that S-101's Lua pipeline emits, so one renderer handles both.

### Portrayal-instruction caching

`IPortrayalInstructionCache` (namespace
`EncDotNet.S100.Pipelines.Vector.Caching`) caches the `DrawingInstruction` list
that the vector pipeline produces. When you reopen a dataset you've portrayed
before, a cache hit skips the portrayal run, which for S-101 is a Lua run
taking several seconds.

- `GetOrCompute(key, factory)` returns the cached list or computes it.
- `InMemoryPortrayalInstructionCache` is a bounded LRU cache that holds list
  references.
- `DiskPortrayalInstructionCache` writes each list to a `.dlist` file with
  `DrawingInstructionSerializer`. Writes go to a temporary file and are then
  moved into place, and an LRU byte cap bounds the cache. A corrupt file or a
  `FormatVersion` mismatch counts as a miss, so a stale or partial file never
  breaks a render.
- You supply a key that captures every portrayal input. `S101DatasetProcessor`
  hashes the dataset bytes, the feature and portrayal catalogue content
  (including overrides and Lua rules) and the engine assembly versions.
- The cache stores the pipeline's final output, so a hit returns exactly the
  list a fresh run would produce.

### Dataset metadata

`DatasetMetadata` (namespace `EncDotNet.S100.Core`) is what every dataset
reader returns from its `ReadMetadata` path: the facts a host can get without a
full parse and portrayal.

- It has `Spec` (`SpecRef`), `Extent` (`BoundingBox?`), `HorizontalCrsEpsg`
  (`int?`), `DisplayScale` (`DisplayScaleRange?`) and `TimeCoverage`
  (`TimeCoverage?`).
- Only `Spec` is always set. A `null` optional value means it isn't available
  cheaply; load the dataset fully to get it.
- A host can use it to load a folder of datasets with no catalogue in phases:
  read every dataset's metadata, frame the viewport from the union of their
  extents, and load each dataset fully when it comes into view.
- `Gml.GmlDatasetMetadata.Create(specName, declaredEdition, features)` folds
  feature geometry into an extent for the GML products.

The `EncDotNet.S100.Core.Metadata` namespace caches metadata between sessions:

- `DatasetMetadataSerializer` writes a `DatasetMetadata` to a small versioned
  binary blob. A corrupt blob or a `FormatVersion` mismatch counts as a miss.
- `IDatasetMetadataCache` and `DiskDatasetMetadataCache` store one `.dmeta`
  file per dataset, keyed by the source file's last-write time and length.
  Any mismatch, or an unwritable or corrupt entry, is a miss and never breaks
  loading. Writes are atomic and an LRU byte cap bounds the cache, as for the
  portrayal-instruction cache.

### Validation

The `EncDotNet.S100.Validation` namespace has the product-independent types
for checking normative clauses against the typed data models:

- `IValidationRule<TModel>` is one rule.
- `ValidationRuleSet<TModel>` runs a set of rules, collects every finding, and
  catches exceptions from individual rules.
- `ValidationFinding` has the rule id, severity, message, an optional
  `GeoPosition` or `BoundingBox`, and a related feature id.
- `ValidationSeverity` and `ValidationReport`.
- `ValidationContext` carries `ReferenceTime` and an optional
  `IServiceProvider` for rules that look across datasets.
- `ValidationRuleBuilder` builds rules fluently:
  `RuleFor<T>("rule-id").Check(predicate, message).Build()`, or
  `.Yield(producer)` for rules that return several findings.

Each product's rule pack lives in the `Validation/` folder of its
`EncDotNet.S100.Datasets.Sxxx` project.

- S-101, S-102, S-104, S-111, S-122, S-124, S-125, S-127, S-128, S-129, S-131,
  S-201, S-411 and S-421 ship a rule pack. S-57 delegates to the S-101 pack.
- S-401 (IEHG Inland ENC) is read and portrayed but has no rule pack. The S-101
  pack checks S-101 clauses and doesn't run against inland data, so
  `Validate()` reports that no rules are available.
- Rule packs for vector products read from a façade that follows the
  specification, not from the raw reader types. For S-101 that's
  `S101DatasetView`, `S101FeatureView` and `S101AttributeView`. Rule code then
  doesn't change if a typed data model replaces the reader types later.
- Coverage records have a `GroupPath` (`BathymetryCoverage.GroupPath` in S-102,
  `WaterLevelCoverage.GroupPath` in S-104). Rule packs use it as the
  `ValidationFinding.RelatedFeatureId` for each coverage.

[Custom catalogues and validation](../../docs/catalogues-and-validation.md)
shows how to run the bundled rules and write your own.

### Spec-version assessment

`SpecRef`, `CatalogueRef` and `SpecVersion` identify product specifications
and catalogues. `SpecCompatibility.Classify(declared, implemented)` compares
two editions (S-100 Edition 5.2.1 Part 2 §6).

`SpecVersionAssessment` compares a dataset's declared product-specification
edition with the editions the application supports.

- It doesn't compare against the feature or portrayal catalogue version. A
  catalogue declares only its own version, not the product-specification
  edition it targets, so that comparison would give false warnings.
- `IsWarning` is `true` only when the application supports an older edition on
  the same major version, or no edition on the declared major version.
- `BuildMessage()` returns the note to show the user.
- `Gml.GmlDatasetIdentification.ReadDeclaredEdition(root)` reads the declared
  edition from a GML dataset's
  `DatasetIdentificationInformation/productEdition` (S-100 GML 5.0 or the
  legacy 1.0 profile).

### Shared types

- `IPortrayalCatalogue`, `ICrsTransform`, `ICrsTransformFactory`, `Viewport`,
  `BoundingBox`, `RgbaColor` and `ColorPalette` (namespace
  `EncDotNet.S100.Pipelines`).
- `MarinerSettings`: the S-100 Part 9 §4.2 mariner selections, including the
  four depth contours and S-101 portrayal options such as `FourShades`,
  `SimplifiedSymbols`, `RadarOverlay` and `NationalLanguage`.
- `DepthUnit` and `DepthFormatting`: culture-invariant conversion, formatting
  and parsing of depths in metres, feet, fathoms, and fathoms and feet.
- [`EncDotNet.S100.Crs.ProjNet`](../EncDotNet.S100.Crs.ProjNet/README.md)
  implements `ICrsTransformFactory`.

### Data-coverage geometry

`DataModel.CoverageArea` is the data-coverage footprint of a vector cell, in
EPSG:4326 latitude and longitude (S-100 Part 10b §6.2). It has an
`ExteriorRing` and optional `InteriorRings` (holes with no coverage). The S-101
and S-57 processors build it from `DataCoverage` surfaces with
`categoryOfCoverage = 1`. A host uses it to hide a coarser cell where a finer,
overlapping cell in the display band has coverage.

### Dynamic feature sources

The `EncDotNet.S100.DynamicSources` namespace describes features that change
over time and are pushed to the host, such as own ship, AIS targets, route
previews and sensor overlays. They're drawn alongside static datasets. The
types don't depend on any graphics library.

- `IDynamicFeatureSource` provides a snapshot of features and a `Changed`
  event.
- `DynamicFeature` uses the same geometry vocabulary as the static vector
  pipeline: the `GeometryType` enum and `(Latitude, Longitude)` tuples.
- `DynamicMotion` adds motion to a moving point feature.
- `DynamicVesselGeometry` adds vessel dimensions: length, beam, and the CCRP
  and GPS-antenna offsets (IEC 62388). It matches the AIS type 5
  `dimA`/`dimB`/`dimC`/`dimD` fields.
- `DynamicSourceMetadata` carries `DisplayName` and `RendererKey`.
  `EncDotNet.S100.Renderers.Mapsui` uses the key to find the renderer.
- `DynamicFeaturesChanged` and `DynamicSourceChangeKind` describe a change.
- `DynamicFeatureTracker<TInbound>` helps adapters that age features out, such
  as AIS sleep and lost timers or stale-sensor styling.

[Dynamic feature sources](../../docs/design/dynamic-feature-source.md)
explains the design.

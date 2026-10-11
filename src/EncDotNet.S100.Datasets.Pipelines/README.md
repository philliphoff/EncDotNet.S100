# EncDotNet.S100.Datasets.Pipelines

This package turns a dataset file into a dataset processor (`IDatasetProcessor`)
that renders, picks, lists features and validates in the same way for every
product. It contains one processor per product, the `DatasetPipelineFactory`
that detects a file's product and creates its processor, the exchange-set
loader, the headless pick services, and the headless compositor that applies
the S-98 interoperability rules. SoundCharts, `s100` and the MCP server are
built on it.

Most applications should use the [`EncDotNet.S100`](../EncDotNet.S100/README.md)
package, which sets this package up with the bundled feature and portrayal
catalogues. Reference this package directly when you need control over the
catalogues, the CRS handling or the pipeline itself.

## Install

```bash
dotnet add package EncDotNet.S100.Datasets.Pipelines
```

## Create a processor and validate a dataset

The example uses `BundledDatasetProcessorFactory` from the `EncDotNet.S100`
package, which creates processors with the bundled catalogues:

```csharp
using EncDotNet.S100;
using EncDotNet.S100.Datasets.Pipelines;

using var factory = BundledDatasetProcessorFactory.Create();
IDatasetProcessor processor = factory.CreateProcessor("path/to/dataset.000");

Console.WriteLine($"{processor.Spec}, extent {processor.Metadata.Extent}");

var report = processor.Validate();
if (report is null)
    Console.WriteLine("No rule pack for this product.");
else
    foreach (var finding in report.Findings)
        Console.WriteLine($"{finding.RuleId} {finding.Severity}: {finding.Message}");

(processor as IDisposable)?.Dispose();
```

Keep the factory alive while you use its processors; it owns the catalogue
caches they share.

To choose the catalogues, Lua engine or CRS transforms yourself, construct a
`DatasetPipelineFactory`. It takes a `PortrayalCatalogueManager`, an
`ILuaEngine`, an `ICrsTransformFactory`, a `FeatureCatalogueManager` and an
`IDisplayPlaneAuthorityProvider`, plus an optional shared
`IPortrayalInstructionCache` and an optional `S100ProductRegistry`. A registry
with a subset of products gives you a host that handles only those products.
[Loading datasets](../../docs/loading-datasets.md) shows the processor API in
more detail.

## Main types

- **`IDatasetProcessor`**: a parsed dataset with rendering, picking, feature
  listing, metadata and validation.
- **`DatasetPipelineFactory`**: detects a file's product and creates its
  processor. See [Product detection](#product-detection).
- **`ExchangeSetLoader`**: reads an S-100 exchange-set catalogue and returns one
  processor per dataset.
- **`S100Products`** and **`S100ProductRegistration`**: the products a factory
  can create, and how each one is recognised.
- **`DatasetProcessorOwner`**: owns the processors loaded into a map. See
  [Processor lifetime](#processor-lifetime).
- **`MapPresentationState`** and **`MapDataset`**: renderer-neutral snapshots
  of a map's display settings and of each dataset on it. See
  [Map presentation](#map-presentation).
- **`HeadlessCompositor`**: draws several datasets into one image with the
  S-98 rules. See [S-98 interoperability](#s-98-interoperability).
- **`ValidationRunner`**: runs any processor's validation for a host. See
  [Validation](#validation).
- The pick services in the `.Query` namespace and the dataset catalog in
  `.Catalog`. See [Pick services and dataset catalog](#pick-services-and-dataset-catalog).

Some of these types, including `IDatasetProcessor`, `DatasetProcessorOwner`,
`MapPresentationState`, `MapDataset` and the S-98 engine, are compiled into
`EncDotNet.S100.Core` but use the `EncDotNet.S100.Datasets.Pipelines`
namespaces. `IMapPresentationController` is in `EncDotNet.S100.Maps`.

## Processors

| Processor | Product | Portrayal |
|---|---|---|
| `S101DatasetProcessor` | S-101, S-401 | Vector, Lua |
| `S102DatasetProcessor` | S-102 | Coverage, Lua |
| `S104DatasetProcessor` | S-104 | Coverage, built-in colour bands |
| `S111DatasetProcessor` | S-111 | Coverage, arrow symbols |
| `S122DatasetProcessor` | S-122 | Vector, XSLT |
| `S124DatasetProcessor` | S-124 | Vector, XSLT |
| `S125DatasetProcessor` | S-125 | Vector, XSLT |
| `S127DatasetProcessor` | S-127 | Vector, XSLT |
| `S128DatasetProcessor` | S-128 | Vector, XSLT |
| `S129DatasetProcessor` | S-129 | Vector, XSLT |
| `S131DatasetProcessor` | S-131 | Vector, Lua |
| `S201DatasetProcessor` | S-201 | Vector, XSLT |
| `S411DatasetProcessor` | S-411 | Vector, XSLT |
| `S421DatasetProcessor` | S-421 | Vector, XSLT |
| `S57DatasetProcessor` | S-57 | Translated to S-101 (maritime ENC) or S-401 (inland ENC), then drawn by the S-101 vector pipeline with that product's catalogues |

`S101DatasetProcessor` handles S-401 inland ENCs when it's created with the
S-401 catalogues.

## Product detection

`DatasetPipelineFactory` recognises a file by its extension, its HDF5
signature or its GML application namespace, and returns the matching processor.
`DetectProductSpecFromSourceAsync(...)` runs the same GML check on a file in an
asset source. Use it for exchange sets whose catalogue has no machine-readable
product identifier, such as JCOMM S-411 catalogues.

Each product describes its own detection. An `S100ProductRegistration` in
`S100Products` says both how to construct the processor and how to recognise
the product's files:

- A GML product matches on its namespace and `productIdentifier` through
  `MatchGml`, using a `GmlRootInfo` read once per document.
- An ISO 8211 product matches on its file envelope through `MatchIso8211`,
  using an `Iso8211RootInfo` read once per file.

The factory reads the root of a GML document, or the envelope of an ISO 8211
file, once, and returns the first registered product whose matcher accepts it.
To add a product, you add a registration to `S100Products`; the factory
doesn't change. The product identifiers `MapProductIdentifierToSpec` accepts
also come from the registrations (`S100Products.All`). The HDF5 products
(S-102, S-104, S-111) are recognised by a header check in the factory.

Three products share the `.000` extension, so the envelope decides:

- **S-57** is recognised by the `DSPM` field in its Data Descriptive Record,
  which only S-57 has.
- **S-101** and **S-401** are recognised by the product identifier in the
  `PRSP` subfield of the `DSID` record (`INT.IHO.S-101.…` or
  `INT.IHO.S-401.…`).

An S-57 cell's `PRSP` is a small integer naming its S-57 product
specification: `1` for a maritime ENC, `10` for an inland ENC (IENC).
`Iso8211RootInfo.DeclaresS57ProductSpecification` tests it against the
`S57ProductSpecification` codes. An inland S-57 cell is still an S-57 cell and
keeps the `S-57` identity; the code only decides which catalogue portrays it
(see [Product identity and portrayal product](#product-identity-and-portrayal-product)).
A cell that declares no `PRSP`, or whose product isn't in the host's registry,
is treated as S-101. A host that registers only some products never gets a
processor it can't build.

## Processor lifetime

`DatasetProcessorOwner` owns the processors loaded into a map, independent of
any renderer or UI framework. It's keyed by a host-stable `MapDatasetId`. It
rejects a duplicate ID without taking ownership of the rejected processor.
While a render or other operation holds a `DatasetProcessorLease`, removing
the processor doesn't dispose it until the lease is released. Disposing the
owner retires every processor and disposes each `IDisposable` processor
exactly once. Layer rendering, S-98 composition, time registration and
refreshing the display are outside its scope.

## Dataset metadata

`IDatasetProcessor.Metadata` is a `DatasetMetadata`: the declared product,
geographic extent, horizontal CRS, display scale window and time coverage. It's
derived from the dataset the processor has already parsed, not from a second
read of the file. Use it to frame a view, register a layer, show an
out-of-scale indicator or decide visibility.

Each processor calculates the value once:

- GML processors calculate the raw WGS 84 envelope in one pass over the
  features, shared with the padded render extent (`ComputeGeographicExtent`),
  so later renders don't walk every coordinate again.
- HDF5 processors (S-102, S-104, S-111) take the extent and CRS from the
  georeferencing the coverage source has already read, without reading the
  values again. S-104 and S-111 station series (format 8) use the union of
  their station positions.

The default interface implementation carries only `Spec`, so a processor that
can't supply an extent cheaply still compiles.

## Declared edition check

Every processor sets `IDatasetProcessor.Spec` to the product specification
edition the dataset declares: the HDF5 `productSpecification` root attribute,
the S-101 `ProductSpecificationEdition`, or the GML `productEdition`.
`IDatasetProcessor.VersionAssessment` (`SpecVersionAssessment?`) compares that
edition with the editions this library supports for the product, using
`SupportedSpecEditions.Assess(...)`.

`SupportedSpecEditions` lists product specification editions, not catalogue
versions. A feature or portrayal catalogue declares only its own version, not
the product edition it targets, so the supported editions are listed in code.
When the declared edition differs in a way that may affect rendering,
`VersionAssessment.IsWarning` is `true`. `s100 info`, `s100 render` and the
SoundCharts dataset list show the warning without blocking.

## Product identity and portrayal product

`IDatasetProcessor.Spec` is the dataset's product identity: what it is. Use it
for labels, validation, links to the specification and the edition check.
`IDatasetProcessor.PortrayalSpec` is the product whose feature catalogue,
portrayal catalogue and ECDIS display conventions process and draw the
dataset. The two are the same for every native S-100 product. They differ only
for S-57 cells, which keep the identity `S-57` while acting as the product
they're translated into.

`SpecConventions` holds the default mapping (`PortrayalSpecFor(SpecRef)`,
`PortrayalSpecName(string)`), which maps `S-57` to `S-101`. The default
`PortrayalSpec` uses it.

An inland S-57 cell (`DSID`/`PRSP` = 10) is translated into S-401 and drawn
with the S-401 catalogues, so `S57DatasetProcessor` sets `PortrayalSpec` per
cell. If the host has no S-401 portrayal catalogue, it uses S-101, so a host
that registers only S-101 still loads inland cells.

When you resolve a catalogue, store viewing-group or display-category state, or
choose a display mode, use the processor's `PortrayalSpec`. The string mapping
is only a default before the dataset loads: SoundCharts' `DatasetEntry.PortrayalSpec`
starts from it and is corrected when the processor loads. When you label or
validate a dataset, use `Spec`.

Catalog entries follow the same rule. A `LoadedDataset` for an S-57 cell
reports `Spec` `S-57`, whether `LoadedDatasetProjector` creates it from the
loaded `S57DatasetProcessor` or from the cell's bytes, even though its payload
is an `S101DatasetData`. From bytes, the projector reads the cell with the S-57
reader and translates it into the product the cell declares (S-101, or S-401
for an inland ENC), as the processor does. It never opens S-57 bytes with
`S101Dataset.Open`. It has no portrayal catalogues, so it doesn't fall back to
S-101 when S-401 is missing.

## S-101 sequential updates

An S-101 cell can come as a base (`….000`) and ordered update files (`….001`,
`….002`, …). The updates are applied to the base before portrayal (S-100
Part 10a). `S101UpdateApplicator` in `EncDotNet.S100.Datasets.S101` applies
them; this package finds them:

- **In an exchange set**, `S101ExchangeSetUpdatePlan.Build(...)` pairs each
  base cell with its updates from the same catalogue, ordered by
  `updateNumber`. `ExchangeSetLoader` and SoundCharts use it, then call
  `DatasetPipelineFactory.CreateS101ProcessorWithUpdates(source, baseRelativePath, updateRelativePaths)`.
- **For a loose file**, `S101FilesystemUpdateDiscovery.FindSequentialUpdates(baseFilePath)`
  finds update files next to the base cell. `s100` uses it, then calls
  `DatasetPipelineFactory.CreateS101ProcessorWithUpdates(baseFilePath, updateFilePaths)`.
  `DatasetPipelineFactory.CreateProcessorWithFilesystemUpdates(baseFilePath)`
  does both steps.

Application is best-effort. A missing, out-of-order or unreadable update is
recorded in `S101DatasetProcessor.UpdateReport`, and the partly updated cell
still renders. Updates aren't applied across exchange sets or directories.

## Portrayal output without Mapsui

This package doesn't depend on Mapsui, so headless users such as the
`EncDotNet.S100` facade and `s100` don't pull it in. Processors don't build
Mapsui layers. Instead they return renderer-neutral portrayal output:

- `IVectorPortrayalSource.BuildVectorPortrayalAsync(...)` returns a
  `VectorPortrayalResult`. It holds the drawing instructions, a geometry
  provider, the resolved palette and assets, the EPSG:3857 extent, layer keys,
  the scale at which the cell goes out of its scale band, the cell's coverage
  areas (`CoverageAreas`, EPSG:4326 polygons from its `DataCoverage` surfaces,
  used to suppress overlap between cells), and the S-98 display plane
  metadata.
- `ICoveragePortrayalSource.BuildCoveragePortrayalAsync(...)` returns a
  `CoveragePortrayalResult`. It holds the `StyledCoverageLayer`s, the viewport
  and georeferencing, feature info, layer keys, and, for S-111, the arrow
  symbol scheme with its SVGs prepared.

Both methods run under the processor's render lock and copy everything they
return, so the result is safe to use from another assembly. Converting the
output to Mapsui layers, and the `MapsuiDatasetResult` type, are in
[`EncDotNet.S100.Renderers.Mapsui`](../EncDotNet.S100.Renderers.Mapsui/README.md)
(`MapsuiDatasetRenderer`), which references this package. The renderer-neutral
S-98 types (`IDisplayPlaneAuthority`, `DisplayPlaneAuthorityProvider`) stay
here; the Mapsui stack entries are in the renderer.

The ProjNet implementation of `ICrsTransformFactory` is in the separate
[`EncDotNet.S100.Crs.ProjNet`](../EncDotNet.S100.Crs.ProjNet/README.md)
package, so CRS handling doesn't need Mapsui either.

## Map presentation

`MapPresentationState` is an immutable snapshot of the display settings shared
by every dataset on a map: palette, symbol and text scale, ECDIS settings,
mariner settings, and the product-specific display modes in
`EcdisDisplaySettings.ActiveDisplayModes`. Its constructor copies the ECDIS
collections, so you can reuse one snapshot across concurrent renders.

`presentation.CreateRenderContext(processor, selectedTime)` picks the
product-specific `RenderContext` from `processor.PortrayalSpec` and applies all
the map-wide settings. S-104, S-111 and S-411 contexts carry the selected time;
other products ignore it. Use `presentation.ApplyTo(context, processor.PortrayalSpec)`
when you need your own context with a viewport, basemap or instruction filter.

`IMapPresentationController.SetPresentationAsync` applies a new
`MapPresentationState` for a host or session that owns loaded datasets. It
takes the state explicitly and applies it asynchronously, without exposing UI
refresh events or renderer types. Implementations keep ownership of the
processors, refreshes and disposal. The Mapsui backend's
`MapsuiDatasetLayerSession` uses the snapshot directly to create product
contexts, render layers, compose S-98, apply time filters and combine refreshes.

`MapDatasetId` and `MapDataset` are the matching per-dataset snapshot.
`MapDataset` combines `DatasetMetadata` (extent, CRS, display scale and time
coverage) with visibility and active flags, opacity, available and current
time, `MapDatasetSubLayer` state, the `ValidationReport` and the
`SpecVersionAssessment`. It contains no rendered layers, localized strings, UI
commands or framework events. SoundCharts projects its loaded datasets into
this model. `MapsuiDatasetLayerSession` uses it as the identity and display
state of each dataset when layers are replaced. The session also uses
`IInteroperabilityAuthorityProvider` and `MarinerSettings` to handle S-98
ordering, suppression, authority changes and the active dataset's layer band,
so SoundCharts doesn't have to.

## Pick services and dataset catalog

The pick logic, which finds the vector features and samples the coverage values
at a geographic point, is shared by the MCP tools
(`EncDotNet.S100.Mcp.Tools`) and `s100 identify`, so neither depends on the
other:

| Namespace | Contents |
|---|---|
| `.Query` | `IdentifyFeaturesService`, `SampleCoverageService`, `DescribeFeatureService` and their request and result records, and the `ToolResult<T>` and `ToolError` types they return. |
| `.Catalog` | `IDatasetCatalog`, `LoadedDataset`, `LoadedDatasetData` and `DatasetId`; `LoadedDatasetProjector`, which maps a product name to its reader, `LoadedDatasetData` variant and bounds; and `FileDatasetCatalog`, a read-only catalog backed by files. |
| `.Geometry` | Point, polyline and bounding box helpers for the pick services. |
| `.Spec` | `FeatureDescriberRegistry`, which `DescribeFeatureService` uses to describe a feature per product, and the `FeatureAccessor`, `FeatureGeometryQuery` and `FeatureNames` helpers. |
| `.Time` | `FeatureValidity` and the time window query helpers. |

SoundCharts' `ViewerDatasetCatalog` and the headless `FileDatasetCatalog` both
use `LoadedDatasetProjector`, so a pick from `s100` produces the same catalog
entries as one in SoundCharts. The exception is S-57: SoundCharts projects S-57
cells from their loaded `S57DatasetProcessor`, so its entries include any
exchange-set updates the processor applied. The MCP `identify_features`,
`sample_coverage` and `describe_feature` tools map `ToolResult<T>` onto the MCP
protocol.

## Validation

Every processor implements `IDatasetProcessor.Validate()`:

```csharp
ValidationReport? Validate();
```

It works the same way for every product:

- **Runs once.** The first call runs the product's rule pack (from its
  `EncDotNet.S100.Datasets.Sxxx.Validation` namespace) on the parsed dataset
  and caches the `ValidationReport`. Later calls return the cached report.
  Validation doesn't depend on palette, opacity or time step, so the cache is
  valid for the processor's lifetime.
- **Depends only on the parsed dataset.** Each finding has a rule ID, a
  severity, a message, an optional `GeoPosition` or `BoundingBox`, and a
  `RelatedFeatureId`: the feature identifier for vector features, or the HDF5
  group path for coverages.
- **`null` means there's no rule pack; `ValidationReport.Empty` means the rules
  ran and found nothing.** Every product in the table above has a rule pack,
  but an S-401 dataset returns `null`, and so does a processor that doesn't
  override `Validate()`. A UI can use the difference to show "not validated"
  rather than "no findings".
- **Schema failures become findings.** Coverage processors catch
  `S100DatasetSchemaException` and return a single `Sxxx-PROJ-SCHEMA` finding
  with the `GroupPath`, attribute name and specification reference. Vector
  processors reserve `Sxxx-PROJ-PARSE` for the same purpose.

For the rules each pack checks, see the product package READMEs, for example
[`EncDotNet.S100.Datasets.S101`](../EncDotNet.S100.Datasets.S101/README.md#validation).
To write your own rules, see
[Custom catalogues and validation](../../docs/catalogues-and-validation.md).

### Validate S-57 cells

`S57DatasetProcessor` returns a report built from two passes:

1. **Before translation**, `S57PreTranslationRules.Default` checks the raw
   `EncDotNet.S57.S57Document` for things that don't survive translation, such
   as `DSID` and `DSPM` presence and `M_COVR` coverage.
2. **After translation**, `S101DatasetRules.Default` checks the translated
   S-101 document. This pass runs only for a maritime cell. An inland cell is
   translated into S-401, which has no rule pack (the S-101 pack checks S-101
   clauses), so its report is the first pass only.

The two reports are joined with finding order kept and counters summed. Rule
IDs from the second pass get the prefix `S101-as-S57/`, so `S101-R-2.1` shows
as `S101-as-S57/S101-R-2.1` and you can tell which stage a finding came from.
Findings from the first pass keep their `S57-*` IDs.

### ValidationRunner

`ValidationRunner` is the product-neutral entry point SoundCharts and the MCP
server use. Given an `IDatasetProcessor`, it calls `Validate()` and converts
the result into the host's shape, such as UI rows or an MCP tool response,
without the host knowing each product's rule namespace.

## S-98 interoperability

The S-98 engine is in the `EncDotNet.S100.Datasets.Pipelines.Interoperability`
namespace: `InteroperabilityAuthority`, `LayerStackBuilder`, `S98RuleContext`,
`S98DefaultRules`, `S98SuppressionPolicy`, and the load-order fallback
`LoadOrderInteroperabilityAuthority`. It works on renderer-neutral
`SubLayerStackItem` and `StackPayload` values.

The authority gives each sub-layer a display plane (Under Radar, Standard, Over
Radar or Dynamic Arrows) and a priority within that plane. It then applies the
inter-product rules (R-101-102, R-101-124, R-104, R-111), which drop or change
sub-layers that other loaded products supersede. Suppression filters
`DrawingInstruction`s, matched by their `VectorFeatureTag`, rather than Mapsui
features, so the same decision drives both renderers:

- **SoundCharts**: `DatasetLoaderService` orders and suppresses
  `SubLayerStackItem`s, then maps the result back to the Mapsui layers it has
  built.
- **`HeadlessCompositor`**: runs the same engine and turns each ordered vector
  or coverage sub-layer into a Skia `CompositeLayer`. It paints all datasets in
  one shared viewport without Mapsui, with the same order and depth suppression
  as SoundCharts, such as drawing S-101 under S-102 (S-98 Annex A §A-6.9.1).
  The public entry point is the `EncDotNet.S100` facade's
  `IReadOnlyList<S100Layer>` render overload.

For the design, see [S-98 interoperability](../../docs/design/s98-interoperability.md).

## Other utilities

- `MapPresentationState`, `IMapPresentationController`, `MapDataset`,
  `MapDatasetId`, `MapDatasetSubLayer`, `EcdisDisplaySettings`,
  `FeatureInfoBuilder`, `PickAttribute`, `CoveragePickHelper` and
  `StationTimeSeriesSnapshot`: shared building blocks for the processors'
  render, feature info and coverage info methods.
- `IceEggCode` and `IceEggCodeBuilder`: the WMO / SIGRID-3 "egg code" of an
  S-411 sea-ice or lake-ice feature, ready to draw (S-411 Ed 1.2.1 Annex A).
  `IceEggCodeBuilder.Build` assembles the total concentration (`iceact`), up to
  three ice types inside the oval (partial concentration `iceapc`, stage of
  development `icesod`, form of ice `iceflz`), the thinner fourth and fifth
  ice classes outside the oval (Cd/Ce, Sd/Se, Fd/Fe), and snow depth as a
  caption. `S411DatasetProcessor` returns it in `FeatureInfo.EggCode` and adds
  each value's feature catalogue definition (through
  `FeatureCatalogueDecoder.ResolveListedValueDefinition`), so a pick report
  can show the meaning on hover.
- `ExternalTextFileResolver`: reads the text of external files named by S-100
  `fileReference` attributes (S-101 feature catalogue; S-57 `TXTDSC` and
  `NTXTDS`, for example on Caution Area and Tidal Stream Panel Data) from the
  dataset's exchange-set asset source. When the exchange-set catalogue's
  `supportFileDiscoveryMetadata` is available (`ExchangeSetLoader` builds it
  and passes it through the factory), the file is found through it first, as
  an ECDIS does, which honours the `support/` folder the catalogue declares.
  Otherwise it looks in the dataset folder, the exchange-set root, and a
  sibling `support/` folder. `S101DatasetProcessor` uses it, through
  `FeatureInfoBuilder.ResolveFileReferences`, to fill
  `PickAttribute.ExternalText`, so a pick report can show the referenced text.
  `FeatureInfoBuilder.CollectResolvedFileReferences` and
  `WithoutResolvedFileReferences` move those texts out of the attribute table
  into their own section.
- `GmlDatasetProcessorBase`: the base class for the GML processors (S-122,
  S-124, S-125, S-127, S-128, S-129, S-131, S-201, S-411, S-421).
- `AssetSourceHelpers`: set-up helpers for exchange sets and loose datasets.
- `Diagnostics/`: the `ActivitySource` and `Meter` instrumentation that the
  OpenTelemetry exporter uses. See [Observability](../../docs/observability.md).

## S-101 render caching

This section describes internals, for contributors.

`S101DatasetProcessor` caches the Lua drawing instructions between renders.
They depend only on the `MarinerSettings` and the ECDIS display state (display
category and hidden S-101 viewing groups and display planes). They don't depend
on the palette or the symbol and text scale, which the renderer applies later.
So a Day, Dusk or Night palette switch, the most common re-render, reuses the
cached instructions and skips the Lua portrayal, which can take several
seconds. The internal `BuildPortrayalCacheKey` builds the key, and the internal
`PortrayalCacheHits` and `PortrayalCacheMisses` counters (visible to tests
through `InternalsVisibleTo`) let tests check it.

The key has to summarise everything that feeds the portrayal, so
`EcdisDisplayExtensions.ApplyTo` clears earlier viewing-group overrides before
it applies the current hidden set. The catalogue's visibility then depends only
on the settings, not on the order of earlier calls. `BuildVectorPortrayalAsync`
is serialized by a `SemaphoreSlim`: the processor keeps one catalogue whose
palette, viewing-group and display-plane state changes on each build, and
SoundCharts can start a new render while one is running.

That cache only helps an open processor. A shared cache
(`IPortrayalInstructionCache`, in `EncDotNet.S100.Core`'s
`Pipelines.Vector.Caching`) lets a new processor for a cell portrayed before
skip the Lua run, including after a restart when the host passes a
`DiskPortrayalInstructionCache`. On a miss in the processor's own cache, the
pipeline runs inside `GetOrCompute(key, factory)` with the key
`"{portrayalContentHash}|{BuildPortrayalCacheKey(...)}"`.

`GetPortrayalContentHashAsync()` (calculated once) is a SHA-256 over:

- the dataset content;
- the resolved feature and portrayal catalogue content, from
  `ICatalogueProvider<T>.GetCatalogueHashAsync`: the SHA-256 of the feature
  catalogue XML, and a SHA-256 over the portrayal catalogue XML and the bytes
  of every asset it declares, including each rule file's Lua source, symbols
  and palettes;
- the module version IDs of the pipeline, executor, Lua engine, portrayal and
  feature assemblies.

Any change to the dataset, a catalogue override, the bundled rules or the
engine therefore misses and recomputes. The hash covers content, never only
declared version strings. The same hash strengthens the pattern-clip cache key.
The internal `SharedInstructionCacheHits` counter lets tests check reuse. With
no shared cache, the processor uses a bounded in-memory cache, so tools and
tests run the same code.

This relies on S-101 portrayal being Lua only, as the bundled catalogue is,
which keeps the instructions independent of palette and scale. An XSLT S-101
catalogue would need the palette in the key; bump the processor's
`PortrayalContentFormatVersion` if you add it.

## See also

- [Loading datasets](../../docs/loading-datasets.md): open files, folders,
  ZIPs and exchange sets, and use processors.
- [Render images and map tiles](../../docs/embedding-the-renderer.md): draw
  portrayal output in your own map.
- [S-98 interoperability](../../docs/design/s98-interoperability.md): the
  design behind the layer ordering and suppression rules.

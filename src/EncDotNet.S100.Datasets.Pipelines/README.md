# EncDotNet.S100.Datasets.Pipelines

Per-spec `IDatasetProcessor` implementations, the S-98 interoperability
authority, and the validation runner consumed by the viewer and the
MCP server.

> **Looking for the easy path?** Most consumers should use the
> [`EncDotNet.S100`](../EncDotNet.S100/README.md) facade package, which wires this
> factory to the bundled feature and portrayal catalogues and exposes a small
> "open → render / read features" API. Use this package directly only when you
> need full control over catalogues, CRS handling, or the pipeline itself.

This package is published to NuGet (`IsPackable=true`) so the facade — and
advanced à-la-carte consumers — can depend on it.

## Overview

Each supported product ships an `IDatasetProcessor` that owns a parsed
dataset and exposes a uniform surface for rendering, picking,
enumerating features, and validating:

| Processor | Spec | Pipeline |
|---|---|---|
| `S101DatasetProcessor` | S-101 | Vector (Lua portrayal) |
| `S102DatasetProcessor` | S-102 | Coverage (Lua portrayal) |
| `S104DatasetProcessor` | S-104 | Coverage (hand-coded palette) |
| `S111DatasetProcessor` | S-111 | Coverage (arrow symbology) |
| `S122DatasetProcessor` | S-122 | Vector (XSLT portrayal) |
| `S124DatasetProcessor` | S-124 | Vector (XSLT portrayal) |
| `S125DatasetProcessor` | S-125 | Vector (XSLT portrayal) |
| `S127DatasetProcessor` | S-127 | Vector (XSLT portrayal) |
| `S128DatasetProcessor` | S-128 | Vector (XSLT portrayal) |
| `S129DatasetProcessor` | S-129 | Vector (XSLT portrayal) |
| `S131DatasetProcessor` | S-131 | Vector (Lua portrayal) |
| `S201DatasetProcessor` | S-201 | Vector (XSLT portrayal) |
| `S411DatasetProcessor` | S-411 | Vector (XSLT portrayal) |
| `S421DatasetProcessor` | S-421 | Vector (XSLT portrayal) |
| `S57DatasetProcessor`  | S-57 (legacy) | Translates to S-101, then delegates to the S-101 vector pipeline |

`DatasetPipelineFactory` discriminates an input file by extension,
HDF5 signature, or GML application namespace and returns the matching
processor wrapped in an `IDatasetProcessor`. Its source-based GML sniff
is also available through `DetectProductSpecFromSourceAsync(...)` for
exchange-set callers whose catalogue metadata omits a machine-readable
product identifier (notably JCOMM S-411 catalogues). `ExchangeSetLoader`
walks an S-100 exchange-set catalogue and yields one processor per
dataset entry.

### Headless pick services and catalog (issue #480)

The protocol-neutral "pick" logic — identify the vector features and
sample the coverage values at a geographic point — lives here so it can
be shared by the MCP tools (`EncDotNet.S100.Mcp.Tools`) and the CLI
`s100 identify` command without either depending on the other:

| Namespace | Contents |
|---|---|
| `.Query` | `IdentifyFeaturesService`, `SampleCoverageService`, `DescribeFeatureService` and their request / result records, plus the neutral `ToolResult<T>` / `ToolError` result types the services return. |
| `.Catalog` | `IDatasetCatalog`, `LoadedDataset` / `LoadedDatasetData`, `DatasetId`; `LoadedDatasetProjector` (the one place a product-spec name is mapped to its per-spec `Open` reader and matching `LoadedDatasetData` variant + bounds); and `FileDatasetCatalog`, a read-only file-backed catalog. |
| `.Geometry` | Point / polyline / bounding-box helpers used by the pick services. |
| `.Spec` | `SpecRef` and spec-capability metadata. |
| `.Time` | `FeatureValidity` and the time-window query helpers. |

`LoadedDatasetProjector` is used by both the Avalonia viewer's
`ViewerDatasetCatalog` and the headless `FileDatasetCatalog`, so a pick
run from the CLI produces byte-identical catalog entries to one run in
the viewer. The MCP `identify_features` / `sample_coverage` /
`describe_feature` tools are thin wrappers that map `ToolResult<T>` onto
the MCP protocol.

### Metadata as a parse byproduct (issue #467 / #460)

`IDatasetProcessor.Metadata` exposes the lightweight, product-agnostic
`Core.DatasetMetadata` (declared spec, geographic extent, horizontal CRS,
display-scale window, time coverage) **derived once from the dataset the
processor already parsed** — never a second parse or a separate
`ReadMetadata(path)` call. Hosts that need to frame a viewport, register a
layer, draw an out-of-scale indicator, or gate visibility read it from the
open processor rather than re-reading the file.

The value is memoized per processor. GML processors compute the raw
(unpadded) WGS-84 envelope with a single feature scan that is **shared**
with the padded render extent (`ComputeGeographicExtent`), so repeated
renders no longer re-walk every coordinate. HDF5 (S-102/104/111) derive
the extent + CRS from the coverage source's already-read georeferencing
metadata (no `values` payload is re-read); S-104/S-111 fixed-station
(dcf8) datasets union their station coordinates. The default interface
implementation carries only `Spec`, so a processor that cannot cheaply
supply an extent still compiles.

### Declared-edition assessment (issue #248)

Every processor populates `IDatasetProcessor.Spec` with the dataset's
*declared* product-specification edition (HDF5 `productSpecification`
root attribute, S-101 `ProductSpecificationEdition`, or GML
`productEdition`) and exposes an optional
`IDatasetProcessor.VersionAssessment` (`SpecVersionAssessment?`). The
assessment is computed by `SupportedSpecEditions.Assess(...)`, which
compares the declared edition against the **editions this application
supports** for that product (the central `SupportedSpecEditions`
table — product-spec editions, *not* catalogue version numbers; an
FC/PC declares only its own version, never the product-spec edition it
targets, so the supported edition must be asserted in code). When
the declared edition diverges in a way that may degrade rendering,
`VersionAssessment.IsWarning` is true and surfaces non-blockingly in
the CLI (`s100 info` / `render`) and the viewer's dataset list.

### Product identity vs. portrayal spec (issue #450)

`IDatasetProcessor.Spec` is the dataset's **product identity** — what it
*is* (labels, validation rebadging, examiner links, version assessment).
`IDatasetProcessor.PortrayalSpec` is the specification whose Feature
Catalogue, Portrayal Catalogue, and ECDIS display conventions actually
process and draw it. The two coincide for every native S-100 product and
diverge only for legacy S-57 cells, which keep identity `S-57` while
acting as `S-101` (they are translated in-memory and portrayed through the
S-101 catalogue). The mapping lives in one place — `SpecConventions`
(`PortrayalSpecFor(SpecRef)` / `PortrayalSpecName(string)`), which the
default `PortrayalSpec` member delegates to. Callers resolving a catalogue,
keying viewing-group / display-category state, or selecting a display mode
must key off `PortrayalSpec`; callers labelling or validating use `Spec`.

### S-101 sequential updates (S-100 Part 10a)

An S-101 cell may ship as a base (`….000`) plus ordered update files
(`….001`, `….002`, …). Updates are folded into the base to produce an
"up-to-date" dataset before portrayal. The merge engine lives in
`EncDotNet.S100.Datasets.S101` (`S101UpdateApplicator`); this library
supplies the two ways callers locate the pieces:

- **From an exchange set** — `S101ExchangeSetUpdatePlan.Build(...)`
  groups a catalogue's entries so each base cell is paired with its
  in-set updates (ordered by `updateNumber`). `ExchangeSetLoader` and
  the viewer use this, then call
  `DatasetPipelineFactory.CreateS101ProcessorWithUpdates(source, baseRelativePath, updateRelativePaths)`.
- **From a loose file** — `S101FilesystemUpdateDiscovery.FindSequentialUpdates(baseFilePath)`
  finds sibling update files in the base cell's directory. The CLI
  uses this, then calls
  `DatasetPipelineFactory.CreateS101ProcessorWithUpdates(baseFilePath, updateFilePaths)`.

Application is **best-effort**: a missing, out-of-order, or unreadable
update is recorded in `S101DatasetProcessor.UpdateReport` but never
prevents the (partially) updated cell from rendering. Cross-exchange-set
/ cross-directory application is intentionally not supported.

## Mapsui-free render seam (issue #189)

This package is **Mapsui-free** so headless consumers (the
[`EncDotNet.S100`](../EncDotNet.S100/README.md) facade and the `s100`
CLI) do not acquire Mapsui as a transitive dependency. The processors
do not build `ILayer`s; instead they expose a narrow, renderer-neutral
portrayal-output seam:

- `IVectorPortrayalSource.BuildVectorPortrayalAsync(...)` →
  `VectorPortrayalResult` — immutable drawing-instruction slices,
  geometry provider, resolved palette / asset snapshot, EPSG:3857
  extent, layer keys, the out-of-scale-band cutoff *value*, the cell's
  data-coverage footprints (`CoverageAreas`, a `CoverageArea` list in
  EPSG:4326 resolved from `DataCoverage` surfaces — used for cross-cell
  overlap suppression, issue #438 Phase 2), and Mapsui-free S-98
  display-plane metadata.
- `ICoveragePortrayalSource.BuildCoveragePortrayalAsync(...)` →
  `CoveragePortrayalResult` — materialized `StyledCoverageLayer`(s)
  plus viewport/georef, info, layer keys, and (S-111) the arrow symbol
  scheme with prewarmed SVGs.

Both build methods run under the processor's render gate and snapshot
everything so the result is safe to convert in another assembly. The
`payload → ILayer` conversion — and the Mapsui-owned
`MapsuiDatasetResult` — live in **`EncDotNet.S100.Renderers.Mapsui`**
(`MapsuiDatasetRenderer`),
which references this package (not the other way round). The map-free
S-98 concepts (`IDisplayPlaneAuthority`, `DisplayPlaneAuthorityProvider`)
stay here; the Mapsui-typed stack entries moved to the renderer.

The `ProjNet`-based `ICrsTransformFactory` implementation lives in the
separate **`EncDotNet.S100.Crs.ProjNet`** package, keeping CRS handling
Mapsui-free too.

### Renderer-neutral map presentation

`MapPresentationState` is the immutable, UI- and renderer-neutral snapshot of
presentation choices shared across every dataset on a map: palette, symbol and
text scale, ECDIS settings, mariner settings, and the product-specific display
modes carried by `EcdisDisplaySettings.ActiveDisplayModes`. Its constructor
defensively freezes the ECDIS collections, so a host can safely reuse the
snapshot across concurrent renders.

Call `presentation.ApplyTo(context, processor.PortrayalSpec)` to project those
map-wide choices onto a product-specific `RenderContext`. Dataset/request state
such as time step, viewport, basemap, and instruction filtering remains on the
context and is preserved. The Avalonia Viewer now uses this projection at its
processor-to-renderer boundary; future Mapsui sessions can construct the same
state without depending on Viewer, Mapsui, or Avalonia types.

## Validation

Every processor implements `IDatasetProcessor.Validate()`:

```csharp
ValidationReport? Validate();
```

The contract is uniform across coverage and vector products:

- **Lazy + cached.** The first call runs the spec's normative rule
  pack (from the matching `EncDotNet.S100.Datasets.Sxxx.Validation`
  namespace) against the parsed dataset and caches the resulting
  `ValidationReport` on a private field. Subsequent calls return the
  cached report. Validation does not depend on the current palette,
  opacity, or selected time step, so the cache is correct for the
  processor's lifetime.
- **Pure function of the parsed dataset.** Findings carry rule id,
  severity, message, an optional `GeoPosition` / `BoundingBox`, and a
  `RelatedFeatureId` (the FOID for vector features, the HDF5 group
  path for coverage records).
- **`null` means "no rule pack"; `ValidationReport.Empty` means
  "rules evaluated, nothing found".** All fifteen supported products
  now ship a rule pack, so `null` is exotic; the distinction matters
  for client UIs that want to show "not validated" rather than
  "clean".
- **Schema failures degrade gracefully.** Coverage processors wrap
  the rule run in a `try` / `catch (S100DatasetSchemaException)` and
  surface a single `Sxxx-PROJ-SCHEMA` finding carrying the offending
  `GroupPath`, attribute name, and spec reference. Vector processors
  reserve `Sxxx-PROJ-PARSE` for the same purpose.

### Render caching (S-101)

`S101DatasetProcessor` caches the Part 9A Lua drawing-instruction list
between renders. That list is a pure function of the
`MarinerSettings` and the effective ECDIS display state (display
category plus hidden S-101 viewing groups / display planes) — it does
**not** depend on the palette or the symbol / text scale, which are
applied later by the Mapsui renderer. So a Day/Dusk/Night palette
switch (the dominant re-render trigger) reuses the cached instructions
and skips the multi-second Lua pipeline. The cache key is built by the
internal `BuildPortrayalCacheKey`; `PortrayalCacheHits` /
`PortrayalCacheMisses` counters (internal, exposed via
`InternalsVisibleTo`) let tests assert the hit/miss behaviour.

Because the cache key must be a faithful summary of everything that
feeds the pipeline, `EcdisDisplayExtensions.ApplyTo` clears any prior
viewing-group user overrides before applying the current hidden set, so
the catalogue's effective visibility is a pure function of the settings
value rather than of call history. The portrayal build
(`BuildVectorPortrayalAsync`) is serialized by a `SemaphoreSlim` gate:
the processor holds one long-lived catalogue whose palette /
viewing-group / display-plane state is mutated per build and read
throughout, and the viewer fires re-renders re-entrantly.

That single-slot cache only helps re-renders of an *already-open*
processor. A **cross-load** cache (`IPortrayalInstructionCache`, from
`EncDotNet.S100.Core`'s `Pipelines.Vector.Caching`) closes the gap so a
*fresh* processor re-opening a previously-portrayed cell — even after a
restart, when the host injects a `DiskPortrayalInstructionCache` — skips
the multi-second Lua run entirely. On a single-slot miss the pipeline
run is wrapped in `GetOrCompute(key, factory)`, keyed by
`"{portrayalContentHash}|{BuildPortrayalCacheKey(...)}"`. The
`GetPortrayalContentHashAsync()` prefix (memoized) is a SHA-256 over the
dataset content, the **resolved** feature- and portrayal-catalogue
content (both via `ICatalogueProvider<T>.GetCatalogueHashAsync` — the FC
hash is the SHA-256 of the resolved FC XML; the PC hash is an aggregate
SHA-256 of the PC XML plus the bytes of every referenced asset it
declares, including every rule file's Lua source, symbols and palettes),
and the module version ids of the pipeline / executor / Lua-engine /
portrayals / features assemblies — so any change to the dataset, an FC /
PC override, the bundled rules, or the engine yields a miss and a
recompute (it hashes *actual content*, never declared version strings
alone). The same hash also strengthens the pattern-clip cache key.
`SharedInstructionCacheHits` (internal) lets tests assert cross-load
reuse. When no shared cache is injected the processor falls back to a
bounded in-memory instruction cache, so tools and tests exercise the
same path. This assumes S-101 portrayal is Lua-only (true for the
bundled catalogue), which keeps the instruction list independent of
palette and scale; an XSLT S-101 catalogue would require adding the
palette to the key (bump the processor's `PortrayalContentFormatVersion`).

### `S57DatasetProcessor` — pre-translation + delegation

`S57DatasetProcessor` is the only processor that produces a composite
report. It runs two passes:

1. **Pre-translation** rules over the raw `EncDotNet.S57.S57Document`
   (`S57PreTranslationRules.Default`) — things that don't survive
   translation, e.g. DSID / DSPM presence, `M_COVR` coverage.
2. **Post-translation** rules over the translated S-101 document via
   the standard `S101DatasetRules.Default`.

The two reports are joined by the internal `ConcatReports.Concat`
helper, which preserves finding order, sums counters, and optionally
**rebadges** the second report's rule ids with a prefix. The
processor uses `rebadgePrefix: "S101-as-S57/"` so a finding from
S-101 rule `S101-R-2.1` surfaces as `S101-as-S57/S101-R-2.1` and the
user can tell at a glance which layer of the pipeline a problem came
from. Pre-translation findings keep their `S57-*` ids verbatim.

`ConcatReports` is internal to this assembly and shared with the
matching test project via `InternalsVisibleTo`.

### `ValidationRunner`

`ValidationRunner` is the spec-agnostic entry point used by the
viewer and the MCP server: given an `IDatasetProcessor` it calls
`Validate()` and translates the result into the host's preferred
shape (UI rows, MCP tool response, etc.) without each consumer
needing to know the spec-specific rule namespaces.

## S-98 interoperability

`Interoperability/` houses the renderer-neutral S-98 inter-product plumbing
(`InteroperabilityAuthority`, `LayerStackBuilder`, `S98RuleContext`,
`S98DefaultRules`, `S98SuppressionPolicy`, plus the load-order
`LoadOrderInteroperabilityAuthority` fallback). The engine operates on
Mapsui-free `SubLayerStackItem` / `StackPayload` values: the authority assigns
each sub-layer a display plane (Under Radar / Standard / Over Radar / Dynamic
Arrows) and a within-plane priority, then evaluates a set of inter-product
rules (R-101-102, R-101-124, R-104, R-111) to drop or transform sub-layers that
other loaded products supersede. Suppression filters encoding-neutral
`DrawingInstruction`s (matched to their `VectorFeatureTag`) rather than Mapsui
`IFeature`s, so the *same* decision drives both renderers.

Two consumers share this single source of truth:

- The **Mapsui viewer** re-platforms onto it — `DatasetLoaderService` sorts and
  suppresses `SubLayerStackItem`s, then maps the ruled items back to prebuilt
  `ILayer`s.
- The **headless `HeadlessCompositor`** (top-level namespace) drives the same
  engine and lowers each ordered vector / coverage sub-layer into a Skia
  `CompositeLayer`, painting all datasets against one shared viewport with no
  Mapsui dependency — reproducing the viewer's cross-dataset draw order and
  depth suppression (e.g. the S-101-under-S-102 interleave, S-98 Annex A
  §A-6.9.1). The `EncDotNet.S100` facade's `IReadOnlyList<S100Layer>` overload
  is the public on-ramp.

See [`docs/design/s98-interoperability.md`](../../docs/design/s98-interoperability.md)
for the full design rationale.

## Other utilities

- `MapPresentationState`, `EcdisDisplaySettings`, `FeatureInfoBuilder`, `PickAttribute`,
  `CoveragePickHelper`, `StationTimeSeriesSnapshot` — shared building
  blocks for the per-processor `Render` / `GetFeatureInfo` /
  `GetCoverageInfo` paths.
- `IceEggCode` / `IceEggCodeBuilder` — a render-ready projection of an
  S-411 sea-ice / lake-ice feature's WMO / SIGRID-3 "egg code"
  (S-411 Ed 1.2.1 Annex A). `IceEggCodeBuilder.Build` assembles the
  total concentration (`iceact`), up to three in-oval ice types
  (partial concentration `iceapc`, stage of development `icesod`, form
  of ice `iceflz`), and the thinner fourth / fifth ice classes flanked
  *outside* the oval (Cd/Ce, Sd/Se, Fd/Fe) plus snow depth as a caption.
  `S411DatasetProcessor` surfaces it on `FeatureInfo.EggCode` and
  enriches each cell with its Feature-Catalogue enumeration definition
  (via `FeatureCatalogueDecoder.ResolveListedValueDefinition`) so the
  pick report can show the prose meaning on hover.
- `ExternalTextFileResolver` — resolves the textual content of external
  files named by S-100 `fileReference` attributes (S-101 FC; alias
  `TXTDSC` / `NTXTDS`, e.g. on Caution Area, Tidal Stream Panel Data)
  from the dataset's exchange-set asset source. When the exchange-set
  catalogue's `supportFileDiscoveryMetadata` is supplied (built by
  `ExchangeSetLoader` and passed through the factory), the file is located
  through it first — the canonical ECDIS mechanism, which honours the
  catalogue-declared `support/` sub-directory — before falling back to
  probing the dataset directory, the exchange-set root, and a sibling
  `support/` directory. `S101DatasetProcessor` uses it (via
  `FeatureInfoBuilder.ResolveFileReferences`) to populate
  `PickAttribute.ExternalText`, so a pick / object-info consumer can show
  the referenced text the way an ECDIS does. Presentation layers can
  then call `FeatureInfoBuilder.CollectResolvedFileReferences` /
  `WithoutResolvedFileReferences` to lift those resolved blocks out of the
  key/value attribute table into a dedicated "referenced text" section.
- `GmlDatasetProcessorBase` — common base for the GML-encoded vector
  processors (S-122 / S-124 / S-125 / S-127 / S-128 / S-129 / S-131 /
  S-201 / S-411 / S-421).
- `AssetSourceHelpers` — exchange-set + loose-dataset bootstrapping.
- `Diagnostics/` — `ActivitySource` / `Meter` instrumentation
  consumed by the OpenTelemetry exporter (see
  [`docs/observability.md`](../../docs/observability.md)).

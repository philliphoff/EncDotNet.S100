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
processor wrapped in an `IDatasetProcessor`. `ExchangeSetLoader`
walks an S-100 exchange-set catalogue and yields one processor per
dataset entry.

## Mapsui-free render seam (issue #189)

This package is **Mapsui-free** so headless consumers (the
[`EncDotNet.S100`](../EncDotNet.S100/README.md) facade and the `s100`
CLI) do not acquire Mapsui as a transitive dependency. The processors
do not build `ILayer`s; instead they expose a narrow, renderer-neutral
portrayal-output seam:

- `IVectorPortrayalSource.BuildVectorPortrayalAsync(...)` →
  `VectorPortrayalResult` — immutable drawing-instruction slices,
  geometry provider, resolved palette / asset snapshot, EPSG:3857
  extent, layer keys, the out-of-scale-band cutoff *value*, and
  Mapsui-free S-98 display-plane metadata.
- `ICoveragePortrayalSource.BuildCoveragePortrayalAsync(...)` →
  `CoveragePortrayalResult` — materialized `StyledCoverageLayer`(s)
  plus viewport/georef, info, layer keys, and (S-111) the arrow symbol
  scheme with prewarmed SVGs.

Both build methods run under the processor's render gate and snapshot
everything so the result is safe to convert in another assembly. The
`payload → ILayer` conversion — and the Mapsui-typed `DatasetResult` —
live in **`EncDotNet.S100.Renderers.Mapsui`** (`MapsuiDatasetRenderer`),
which references this package (not the other way round). The map-free
S-98 concepts (`IDisplayPlaneAuthority`, `DisplayPlaneAuthorityProvider`)
stay here; the Mapsui-typed stack entries moved to the renderer.

The `ProjNet`-based `ICrsTransformFactory` implementation lives in the
separate **`EncDotNet.S100.Crs.ProjNet`** package, keeping CRS handling
Mapsui-free too.

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

`Interoperability/` houses the S-98 inter-product plumbing
(`InteroperabilityAuthority`, `LayerStackBuilder`, `S98RuleContext`,
`S98DefaultRules`, plus the load-order `LoadOrderInteroperabilityAuthority`
fallback). The authority assigns each layer a display plane (Under
Radar / Standard / Over Radar / Dynamic Arrows) and a within-plane
priority, then evaluates a set of inter-product rules (R-101-102,
R-101-124, R-104, R-111) to drop or transform layers that other
loaded products supersede. The viewer consumes the resulting
ordered `LayerStackEntry` list to compose its paint stack.

See [`docs/design/s98-interoperability.md`](../../docs/design/s98-interoperability.md)
for the full design rationale.

## Other utilities

- `EcdisDisplaySettings`, `FeatureInfoBuilder`, `PickAttribute`,
  `CoveragePickHelper`, `StationTimeSeriesSnapshot` — shared building
  blocks for the per-processor `Render` / `GetFeatureInfo` /
  `GetCoverageInfo` paths.
- `GmlDatasetProcessorBase` — common base for the GML-encoded vector
  processors (S-122 / S-124 / S-125 / S-127 / S-128 / S-129 / S-131 /
  S-201 / S-411 / S-421).
- `AssetSourceHelpers` — exchange-set + loose-dataset bootstrapping.
- `Diagnostics/` — `ActivitySource` / `Meter` instrumentation
  consumed by the OpenTelemetry exporter (see
  [`docs/observability.md`](../../docs/observability.md)).

# Mapsui rendering performance

> **Historical (#600 / #601).** The per-feature Mapsui "A" render arm this page
> optimises — and its optimizations described below (the translation-invariant
> **path cache**, the **raster vector snapshot**, and **line simplification /
> precomputed line-LOD**) — were **removed** when the A arm was retired (#600)
> and its orphaned line-LOD producer was retired (#601). The base plane now
> rasterises the `VectorScene` IR directly; see
> `S100-Render-Subsystem-Design.md`. This page is kept as the measurement record
> behind those (now-removed) choices. A future renderer wanting resolution-aware
> line simplification should reintroduce it against the IR, not this path.

This page records the May/June 2026 performance review of the
Mapsui-based viewer pipeline (Avalonia frontend, SkiaSharp backend),
the data captured, and the optimization plan that follows from it.

Branch: `philliphoff/mapsui-performance-review`

## Context

Real-world S-101 datasets (especially when several are loaded at once)
felt visibly laggy when panning in the viewer. Headline question:
**are we getting as much performance out of Mapsui as we can?**

The investigation was structured as a series of measurement passes,
each adding instrumentation that narrowed where the cost lives.

## TL;DR

Mapsui is using the GPU and is not throttled — it is
**vertex-bound**. ~93% of every paint is spent inside Mapsui's
`VectorStyleRenderer`, and per-call cost scales linearly with geometry
vertex count at ~1 µs/point. The single highest-leverage optimization
is **resolution-aware geometry simplification at display-list build
time**, projected to bring mean paint from ~100 ms (8 fps) to
~30–40 ms (25–30 fps) on real-world multi-S-101 datasets.

## What was measured

### 1. GPU acceleration is on

`GpuAccelerationProbe` (1×1 invisible Avalonia `Control`) reads
`ISkiaSharpApiLease.GrContext` once on first paint. Reported on the
investigator's Apple Silicon machine:

```
gpuAccelerated=True backend=OpenGL
```

Avalonia 11 on macOS prefers Metal first, then OpenGL, then software.
The probe found OpenGL — meaning rendering goes through Apple's
deprecated OpenGL→Metal translation layer. Forcing Metal is a possible
incremental gain but unmeasured.

### 2. Mapsui's pipeline is not throttled — it's saturated

`InstrumentedMapControl` brackets `base.Render(DrawingContext)` with
two `ICustomDrawOperation` markers that record the actual Skia paint
duration on the compositor render thread. Heavy real-world S-101
panning shows paint/interval ratio ≥ 0.9, i.e. the renderer is busy
most of every cycle and has no slack to recover frame rate.

### 3. Per-style apportionment

`MapPaintInstrumentation` reflects into
`Mapsui.Rendering.Skia.MapRenderer._styleRenderers` (a private static
`Dictionary<Type, IStyleRenderer>`) and replaces each registered
`IStyleRenderer` with a `CountingStyleRenderer` wrapper that times each
`Draw` call. The wrapper's start/end is bracketed inside the per-paint
markers, so per-paint accumulators reset cleanly each frame.

Wrapper coverage = ~92% of paint wall-time. Across all measured
configurations, **`VectorStyle` accounts for 90–94% of paint**.
`RasterStyle`, `ImageStyle`, `SymbolStyle`, and `LabelStyle` are each
< 5%.

### 4. Per-call cost is purely a function of vertex count

The decisive measurement: per-call cost tagged by both `layer` and
`points` (vertex-count bucket). On a real multi-S-101 session
(rasterizer off, mean paint 98 ms, 8 fps):

| points | calls | total ms | µs/call | % of paint |
|---|---:|---:|---:|---:|
| 1–9 | 45,198 | 328 | 7 | 0.7% |
| 10–99 | 109,620 | 3,421 | 31 | 6.8% |
| **100–999** | **59,091** | **23,568** | **399** | **47.1%** |
| **1k–10k** | **10,481** | **22,726** | **2,168** | **45.4%** |

Per-vertex cost is ~1 µs across all buckets — meaning per-call cost is
fully explained by vertex count alone. Layers with cheap geometries
remain cheap regardless of how many other layers are loaded. There is
no measurable structural overhead from layering itself.

#### Top offending (layer × bucket) combinations

| layer | bucket | total ms | calls | µs/call |
|---|---|---:|---:|---:|
| 101GB00GB302045 (lines) | 1k-10k | 11,476 | 4,032 | 2,846 |
| 101GB00GB302045 (lines) | 100-999 | 9,631 | 13,344 | 722 |
| 101GB00502038 (lines) | 100-999 | 8,338 | 18,326 | 455 |
| 101GB0040242C (lines) | 1k-10k | 4,033 | 1,509 | 2,673 |

A 3,000-vertex polyline (typical coastline / depth contour) costs
~2.8 ms per draw. Drawing 4,000 such polylines per paint = 11 seconds
of paint wall-time over a ~60 s measurement window.

### 5. RasterizingTileLayer prototype: removed

An earlier `S100RasterizingTileLayer` prototype (gated by a now-removed
`EnableVectorRasterization` viewer setting) wrapped a `MemoryLayer` in
Mapsui's tile-cached vector layer. Its measured behaviour was
workload-dependent:

| | OFF | ON | Δ |
|---|---:|---:|---:|
| Mean paint (moderate single dataset) | 30.3 ms | 36.1 ms | +19% |
| Max paint (moderate single dataset) | 136 ms | 114 ms | −16% |

Tail latency improved under heavy load when the cache warmed, but mean
regressed on lighter loads — never safe to default-on. It has since been
**superseded** by the raster vector snapshot
(`S100VectorSnapshotRenderer`), which delivers the same pan-time win
without the mean-paint regression, so the prototype and its setting were
removed. The four current optimizations (path cache, line simplification,
raster snapshot, off-thread snapshot prebuild) are now exposed under
**Settings → Map → Rendering optimizations** and all default on.

## What this rules out

- **❌ Mapsui pipeline overhead.** Per-vertex cost is ~1 µs, which is
  about as fast as Skia can tessellate a stroke. Patching
  `VectorStyleRenderer` would not help.
- **❌ State thrashing across layers.** Per-call cost depends only on
  geometry, not on layer count or layer ordering.
- **❌ Style class complexity.** The dominant cost is plain
  `VectorStyle`; symbol / label / pattern-fill styles are negligible.
- **❌ SKPaint allocations.** Even if every per-call paint were free,
  max savings ≤ a few µs × N calls, swamped by the per-vertex cost.

## High-leverage optimizations

### 1. Resolution-aware geometry simplification (PRIMARY)

Apply Douglas-Peucker (or equivalent) at display-list build time, with
tolerance ≈ 1 screen pixel at the current zoom. Polylines with
thousands of vertices typically reduce 10–100× without visible quality
loss.

**Status:** **lines** simplified and **on by default**. **Polygon**
simplification was implemented, measured, and **removed** — a live
measurement disproved the vertex-count → paint projection for polygons
(see the callout below), so polygons are now always rendered **vertex-exact**
through the cached fast path. Line simplification lives in
`CachedVectorStyleRenderer` (the registered `VectorStyle` renderer) at
`SKPath`-build time, keyed by build resolution and cached per
`(feature, position, resolution)`, so the cost is paid once per
`(feature, zoom)` and inherited by the vector-snapshot record/prebuild.
Lines are gated behind the viewer's **Simplify dense geometry** setting
(default **on**, the only simplification knob in the UI). See the renderer
[README → Resolution-aware geometry simplification](../../src/EncDotNet.S100.Renderers.Mapsui/README.md#resolution-aware-geometry-simplification)
for the implementation, gating, telemetry, and known limits.

**Lines.** `LineString` / `MultiLineString` are simplified inline with a
radial-distance pixel filter while building the path.

**Polygons (vertex-exact — simplification investigated and rejected).**
`Polygon` / `MultiPolygon` (land/depth/sea areas — the highest-vertex
S-101 features) are fast-pathed and cached *vertex-exact*; they are not
geometrically simplified. A topology-preserving simplifier
(`TopologyPreservingSimplifier` + `IsValid`/`Buffer(0)` validation with a
safe pass-through fallback) was built and A/B-tested, then removed.

> **Important finding — the vertex-count → paint projection (below) was
> disproved for polygons by live measurement. Polygon simplification was
> rejected (no paint benefit; worse under load) and removed.** The
> translation-invariant path cache already neutralizes vertex count on
> *warm* paints: once a path is built and cached, a pan re-uses it and the
> paint is cache-served (~0 ms) regardless of how many vertices it has, so
> dropping vertices cannot make warm paints cheaper, while cold builds pay
> the simplifier cost. Two live A/B runs confirmed there is **no paint
> win**, and under multi-dataset pressure it is reproducibly slower:
>
> *Single dense cell (`101GB00GB302045`), live viewer:* cold-pan
> `VectorStyle` paint OFF mean **29.1 ms** vs ON **30.2 ms**; warm/cached
> paints ~**0 ms** in both arms. The TPS cost offsets the reduced-vertex
> fill; warm paints are cache-bound, so ON ≈ OFF.
>
> *Multi-dataset stress (all 15 AU IC-ENC S-101 cells, basemap off,
> pan/zoom across boundaries, rolling-window telemetry, 4 reps cold+warm,
> each arm solo):*
>
> | metric (warm avg) | OFF | ON (polygon simplify) |
> |---|---|---|
> | frame max | ~480 ms | ~800 ms |
> | vector max | ~570 ms | ~1240 ms |
> | vector mean | ~62 ms | ~78 ms |
> | settle mean | ~675 ms | ~715 ms |
>
> ON is **reproducibly ~1.6× worse frame / ~2.2× worse vector** across
> both cold and warm reps. Under multi-dataset cache pressure the path
> cache thrashes so the simplifier cost is re-paid on every rebuild and
> never amortized, and **GPU (Metal) fill is area-bound, not
> vertex-bound**, so fewer vertices do not reduce fill cost. The earlier
> 1 µs/vertex figure was a CPU-build model that does not reflect cached GPU
> paint. The coordinate-budget cache eviction (below) keeps the
> vertex-exact polygon paths bounded in memory without simplifying them.
> **Don't re-chase polygon simplification as a paint optimization.**

**What landed instead (the real, proven wins).** A separate, never-wired
NTS Douglas-Peucker layer (`Simplification/` +
`InstrumentedMemoryLayer.EnableSimplification`, half-octave zoom buckets)
was a dormant duplicate of the live path the docs once described — it has
been **removed** and the codebase consolidated onto the single live
`CachedVectorStyleRenderer`. Its two genuinely-better ideas —
**coordinate-budget cache eviction** (evict LRU by total cached
coordinates, not just entry count, so a handful of dense vertex-exact
polygon paths can't blow the memory budget) and **simplification
telemetry** — were carried forward. Rolling-window render telemetry was
also added to capture transient expensive frames. These — not any polygon
paint speedup — are the proven wins.

**Defaults.** Line PixelTolerance = 0.6 (chosen so thicker strokes such as
depth contours and fairway boundaries don't show visible kinks);
MaxCachedCoordinates = 5_000_000 (≈ 80 MB), evicted LRU by both entry cap
and coordinate budget.

**Future work (the actual next paint lever).** Multi-dataset lag is
**draw-call / feature-count bound**, not vertex-bound (frame max ~480 ms,
~7k draw calls). The synchronous cache-miss build can briefly stall the
first paint after a zoom change (the `vectorMax` spikes), so an
**off-thread cold path-build** is the most direct next lever; **draw-call
batching** (shared `SKPath` per style) and **overlapping-cell coverage
suppression** are the larger structural wins. Per-`(layer, style, feature-class, vertex-bucket)` draw attribution is
collected in `MapPaintInstrumentation` so residual cost can be ranked before
investing in batching or off-thread cold path construction.

The 1 µs/vertex projection below predates the GPU-path measurement and
**held for lines but not polygons** (the path cache neutralizes vertex
count on warm GPU paints) — treat it as a CPU-build upper bound, not a
GPU-paint prediction:
- 1k-10k bucket → 100-999 bucket: ~5× cheaper draws (saves ~18 s of
  paint over the measurement window).
- 100-999 bucket → 10-99 bucket: similar magnitude (saves ~20 s).
- Combined: mean paint drops from ~98 ms → ~30–40 ms on the heaviest
  workload measured.

### 2. Verify SCAMIN / scale-visibility filtering is effective

Some of the worst offenders may be features that should have been
culled at the current zoom but weren't. The
`s100.layer.get_features.*` metrics already track filter ratio;
cross-check against the offending layer/feature combinations.

### 3. Investigate per-feature-class cost distribution

`101GB00GB302045` was responsible for ~67% of paint cost on its own.
Worth identifying which S-101 feature class
(`DepthContour`? `CoastlineLine`?) contributes most, so the
remaining optimization work can target the highest-impact features first.
`MapPaintInstrumentation` now emits a `featureClass` dimension using the
existing `S100.FeatureType` tag attached during layer construction; no
per-paint feature-reference lookup is required.

An August 2026 MCP-driven diagnostic run loaded the four cells that dominated
the original review (`101GB00GB302045`, `101GB0040242B`,
`101GB0040242C`, and `101GB00502038`), pinned the Mapsui subsystem, disabled
the vector snapshot, and exercised their union plus four tighter viewports.
The selected viewports attributed `VectorStyle` paint to the dense
`101GB00GB302045` cell as follows:

| feature class | total ms | draws | µs/draw | % of classified vector time |
|---|---:|---:|---:|---:|
| **Coastline** | **460.1** | **4,318** | 106.6 | **62.9%** |
| **DepthArea** | **170.2** | **1,677** | 101.5 | **23.3%** |
| CautionArea | 40.4 | 164 | 246.5 | 5.5% |
| LandArea | 21.2 | 325 | 65.1 | 2.9% |
| LocalDirectionOfBuoyage | 18.4 | 45 | 408.5 | 2.5% |

`Coastline` in the 100–999-point bucket alone contributed 457.3 ms
(3,218 draws); `DepthArea` split between 1k–10k (92.7 ms) and 100–999
(74.7 ms). The top two classes therefore account for **86.2%** of classified
vector time in the offending cell. Prioritise coastline draw-call batching
and cold path construction first; `DepthArea` remains the next target, but
the rejected polygon-simplification experiment above still applies.

### 4. Batch polylines into shared SKPath objects

Skia amortizes setup across multiple sub-paths in a single
`canvas.DrawPath`. Lower priority than (1); explore after
simplification lands.

## What is NOT worth doing

- **Forcing Metal backend on macOS.** Bottleneck is vertex-bound,
  not blit-bound; backend swap unlikely to move the needle.
- **Patching `VectorStyleRenderer` per-call optimisations.** Per-call
  cost is already ~1 µs/point; nothing left to squeeze.
- **SKPaint pooling.** Same reasoning.

## Instrumentation reference

See [`src/EncDotNet.S100.Renderers.Mapsui/README.md`](../../src/EncDotNet.S100.Renderers.Mapsui/README.md#performance-instrumentation)
for the OTel instrument table and how to capture a measurement session.

Files added on this branch:
- `src/EncDotNet.S100.Viewer/Diagnostics/InstrumentedMapControl.cs`
- `src/EncDotNet.S100.Viewer/Diagnostics/MapPaintInstrumentation.cs`
- `src/EncDotNet.S100.Viewer/Diagnostics/GpuAccelerationProbe.cs`
- `src/EncDotNet.S100.Renderers.Mapsui/InstrumentedMemoryLayer.cs`

Files added by issue
[#164](https://github.com/philliphoff/EncDotNet.S100/issues/164)
implementing optimization (1):
- `src/EncDotNet.S100.Renderers.Mapsui/Simplification/IFeatureSimplifier.cs`
- `src/EncDotNet.S100.Renderers.Mapsui/Simplification/DouglasPeuckerLineSimplifier.cs`
- `src/EncDotNet.S100.Renderers.Mapsui/Simplification/SimplificationOptions.cs`
- `src/EncDotNet.S100.Renderers.Mapsui/Simplification/SimplificationCache.cs`
- `src/EncDotNet.S100.Renderers.Mapsui/Simplification/Simplification.cs`

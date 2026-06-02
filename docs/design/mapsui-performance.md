# Mapsui rendering performance

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

### 5. RasterizingTileLayer prototype: workload-dependent

`S100RasterizingTileLayer` (gated by the `EnableVectorRasterization`
viewer setting) wraps a `MemoryLayer` in Mapsui's tile-cached vector
layer. Picking is preserved by delegating `GetFeatures` to the
underlying source layer.

| | OFF | ON | Δ |
|---|---:|---:|---:|
| Mean paint (moderate single dataset) | 30.3 ms | 36.1 ms | +19% |
| Max paint (moderate single dataset) | 136 ms | 114 ms | −16% |

Tail latency improves under heavy load when the cache warms; mean
regresses on lighter loads. Right call to keep as an opt-in
experimental setting, not safe to default-on.

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

Projected impact based on the measured cost model:
- 1k-10k bucket → 100-999 bucket: ~5× cheaper draws (saves ~18 s of
  paint over the measurement window).
- 100-999 bucket → 10-99 bucket: similar magnitude (saves ~20 s).
- Combined: mean paint drops from ~98 ms → ~30–40 ms on the heaviest
  workload measured.

Open design questions:
- Where in the pipeline does simplification live? Almost certainly
  inside `MapsuiDisplayListRenderer` when geometry is materialized
  from `IDisplayList.GetGeometry`, gated by a resolution bucket.
- Cache shape: keyed by `(feature-ref, zoom-bucket)`; values are
  pre-simplified NTS geometries (or pre-built `SKPath`s).
- Eviction policy: on zoom-band change, not per-paint.

### 2. Verify SCAMIN / scale-visibility filtering is effective

Some of the worst offenders may be features that should have been
culled at the current zoom but weren't. The
`s100.layer.get_features.*` metrics already track filter ratio;
cross-check against the offending layer/feature combinations.

### 3. Investigate per-feature-class cost distribution

`101GB00GB302045` was responsible for ~67% of paint cost on its own.
Worth identifying which S-101 feature class
(`DepthContour`? `CoastlineLine`?) contributes most, so the
simplification work in (1) can target the highest-impact features
first. Requires extending `MapPaintInstrumentation` to tag by feature
class via the `FeatureRefKey` round-trip.

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
- `src/EncDotNet.S100.Renderers.Mapsui/S100RasterizingTileLayer.cs`

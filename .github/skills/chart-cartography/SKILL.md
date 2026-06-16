---
name: chart-cartography
description: |
  Cross-cutting cartography and chart-rendering domain expertise for
  electronic navigational displays — the principles of how marine
  charts are composed, layered, generalized, coloured, projected, and
  made performant. Spec- and renderer-agnostic: it explains the
  *why/what* of good chart rendering and how those principles manifest
  in this codebase, then defers to the per-spec skills for product
  semantics and to the renderer READMEs for implementation. USE FOR:
  layer/draw-order and display-priority decisions; scale-dependent
  generalization, declutter, SCAMIN/scale-band culling; label/text
  placement and deconfliction; day/dusk/night palette and contrast
  design; CRS/projection choices and distortion/antimeridian/pole
  pitfalls; rendering-performance strategy (geometry simplification,
  path/snapshot caching, tiling, vertex-bound cost reasoning); reviewing
  a rendering change for cartographic correctness across products.
  DO NOT USE FOR: a specific product's encoding/attribute/portrayal
  rules (use the sXXX skill); Mapsui/Skia API mechanics (see the
  renderer READMEs); driving the viewer to test (use viewer-evaluation).
---

# Chart cartography & rendering principles

This skill carries the **portable cartographic knowledge** that sits
above any one S-1xx product and below any one rendering backend. The
goal is charts that are *legible, correctly prioritized, scale-
appropriate, and fast*. For product semantics load the matching `sXXX`
skill; for backend mechanics read
[`Renderers.Mapsui`](../../../src/EncDotNet.S100.Renderers.Mapsui/README.md)
and
[`Renderers.Skia`](../../../src/EncDotNet.S100.Renderers.Skia/README.md).

## 1. Layering & draw order
A nautical display is a stack of semi-transparent thematic layers; the
*order* is as meaningful as the symbols.

- **Type-based draw order within a display list:** solid area fills →
  pattern area fills → lines → points/symbols → text. Text and point
  symbols draw last so they are never occluded by fills. This codebase
  encodes that order explicitly (see
  `Renderers.Skia/Scene/VectorSceneBuilder` and the Mapsui renderer),
  then sub-orders by **`drawingPriority`** within each band.
- **Display planes / radar:** an instruction's `displayPlane`
  (`OverRadar` vs under) decides whether content draws above an
  underlying radar/overlay image. Honour it before per-type ordering.
- **Viewing groups & display categories:** features carry a
  `viewingGroup` and map to ECDIS display categories
  (DisplayBase / Standard / OtherInformation). The mariner-selected
  category gates *visibility*, not draw order — keep the two concerns
  separate (`Core/Pipelines/MarinerSettings`).
- **Overlay transparency:** coverage overlays (e.g. bathymetry over a
  base chart) must let lower layers show through where there is no
  data — render NODATA/fill values **transparent**, never as an opaque
  block. A wrong fill colour reads as "sea" or "land" and is a safety
  issue, not a cosmetic one.

## 2. Scale-dependent generalization & declutter
Showing everything at every zoom destroys legibility *and* performance.

- **Scale-band culling (SCAMIN-style):** instructions carry
  `scaleMinimum` / `scaleMaximum`; a feature is drawn only when the
  current display scale falls in its band. Verify culling is actually
  effective — features that *should* drop out but don't are both a
  clutter bug and a top performance offender.
- **Geometry generalization:** at small scales, collapse detail you
  cannot see. Resolution-aware line simplification (Douglas–Peucker at
  ≈ ½–1 screen pixel tolerance) reduces thousand-vertex coastlines /
  contours 10–100× with no visible loss. Keep tolerance conservative so
  thick strokes (depth contours, fairway limits) don't visibly kink.
- **Quantize by zoom band, not per-paint:** cache generalized geometry
  keyed by a coarse (e.g. half-octave) zoom bucket and evict on band
  change — never re-simplify every frame.
- **Polygons need care:** per-ring simplification can self-intersect or
  invalidate a polygon; use a topology-preserving simplifier + validity
  check, or leave polygons un-simplified, rather than risk holes/spikes.

## 3. Labels & text placement
- Text is the most expensive thing to place well and the first to
  clutter. Deconflict overlapping labels; prefer suppressing a label to
  overprinting two.
- **Geometry-less text features are normal.** A `TextPlacement` /
  text instruction may carry no geometry of its own and instead anchor
  to a *referenced* feature. Renderers must resolve the reference and
  must not crash or skip on a null own-geometry.
- Anchor text to its feature with stable offsets; keep labels upright
  and screen-aligned (don't rotate with the map unless the symbology
  spec demands it).

## 4. Colour & palettes (day / dusk / night)
- Provide **Day, Dusk, Night** palettes; night especially must minimise
  emitted light (dark background, low-luminance, red-safe accents) to
  preserve the mariner's dark adaptation. Never hard-code colours in
  render code — resolve through the active palette.
- Maintain figure/ground contrast within *each* palette; a colour pair
  that works in Day can vanish in Night.
- If a product's portrayal catalogue ships only a Day palette, palette
  switching is a no-op for it — surface that honestly (synthesise local
  Dusk/Night only as a documented stopgap), don't silently mis-render.

## 5. CRS & projection
- Source coordinates here are **WGS-84 lat/lon (EPSG:4326)**; the live
  map projects to **Web Mercator (EPSG:3857)** for display (ProjNet in
  the Mapsui renderer). Keep the source→display projection at one well-
  defined boundary; don't smear half-projected coordinates through the
  pipeline.
- **Mercator distortion** grows with latitude — scale bars and any
  metric reasoning must be latitude-aware, not assumed uniform.
- **Antimeridian & poles** are sharp edges: bboxes crossing ±180° are
  not generally supported; great-circle paths and high-latitude data
  need explicit handling rather than naive linear interpolation in
  projected space.

## 6. Rendering performance (the cost model in this codebase)
A measured review (`docs/design/mapsui-performance.md`) established the
governing facts — reason from them, don't re-derive:

- Rendering is **vertex-bound**: ~90%+ of paint time is in vector
  geometry, at ~**1 µs per vertex**. Per-call cost is explained almost
  entirely by vertex count; layer *count* and ordering add no
  measurable structural overhead.
- Therefore the highest-leverage win is **reducing vertices**
  (generalization §2) and culling (scale bands), **not** micro-
  optimizing paints, pooling `SKPaint`, or swapping GPU backends.
- **Caching strategies that pay off:** pre-built/simplified geometry or
  `SKPath`s keyed by `(feature, zoom-bucket)`; **raster snapshots** of a
  settled vector layer to make pans instant (re-rasterize on
  zoom/rotation change, not on every pan frame); optional tile-cached
  vector layers help *tail* latency under heavy load but can regress
  mean cost on light loads — keep them opt-in.
- **Watch rotation and fast-pan paths** specifically: they have
  historically exposed disappearing fills and render hangs; validate
  them when touching snapshot/caching code.
- Measure with the **viewer-evaluation** loop + `get_render_stats` and
  `dotnet-trace`; attribute by `(layer × vertex-bucket)` before
  optimizing, and target the few highest-cost feature classes first.

## Review checklist (cartographic correctness)
1. Draw order: areas → patterns → lines → points → text, then
   `drawingPriority`; radar plane honoured.
2. Visibility gated by display category & scale band — separate from
   draw order; culling demonstrably effective.
3. NODATA / no-coverage renders transparent, not opaque.
4. Generalization conservative, zoom-bucketed, polygon-safe; no kinks
   on thick strokes.
5. Geometry-less text/overlay features tolerated.
6. Colours resolved through the active palette; contrast holds in Day,
   Dusk, and Night.
7. Projection applied at one boundary; latitude-aware metrics;
   antimeridian/pole handled or explicitly out of scope.
8. Performance change justified against the vertex-bound cost model and
   verified with the viewer-evaluation loop.

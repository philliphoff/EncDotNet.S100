---
name: chart-cartography
description: |
  Cross-cutting cartography and chart-rendering expertise: how marine
  charts are composed, layered, generalized, coloured, projected and made
  fast, and where those principles live in this codebase (Rendering.Scene,
  Rendering.Engine, Skia/Mapsui renderers). USE FOR: draw-order and
  display-priority decisions; scale-dependent generalization, declutter,
  SCAMIN/scale-band culling; label placement; day/dusk/night palette and
  contrast; CRS/projection, antimeridian and pole pitfalls; rendering
  performance strategy (simplification, tiling, cost reasoning); reviewing
  a rendering change for cartographic correctness across products.
  DO NOT USE FOR: one product's encoding/portrayal rules (use the sXXX
  skill); Mapsui/Skia API mechanics (see the rendering READMEs); driving
  the viewer to test (use viewer-evaluation).
---

# Chart cartography & rendering principles

This skill carries the **portable cartographic knowledge** that sits
above any one S-1xx product and below any one rendering backend. The
goal is charts that are *legible, correctly prioritized, scale-
appropriate, and fast*. For product semantics load the matching `sXXX`
skill; for mechanics read the READMEs of
[`Rendering.Scene`](../../../src/EncDotNet.S100.Rendering.Scene/README.md)
(backend-neutral scene IR),
[`Rendering.Engine`](../../../src/EncDotNet.S100.Rendering.Engine/README.md)
(tile compositor, caches),
[`Renderers.Skia`](../../../src/EncDotNet.S100.Renderers.Skia/README.md)
and
[`Renderers.Mapsui`](../../../src/EncDotNet.S100.Renderers.Mapsui/README.md)
(thin map adapter).

## 1. Layering & draw order
A nautical display is a stack of semi-transparent thematic layers; the
*order* is as meaningful as the symbols.

- **Part 9 draw order:** display plane (`UnderRadar` before
  `OverRadar`) → **`DrawingPriority`** → geometry type *within* a
  priority (solid area fills → pattern fills → lines → points → text).
  A higher-priority area therefore covers a lower-priority line (#1126);
  the sort is stable, so ties keep display-list order. This lives in one
  place, `VectorSceneBuilder.DrawOrder` (`Rendering.Scene`); backends
  draw the scene's ops in order and must not re-sort.
- **Viewing groups & display categories:** instructions carry a
  `ViewingGroup` and map to ECDIS display categories (DisplayBase /
  Standard / OtherInformation). Category, hidden viewing groups and
  hidden planes gate *visibility*, not draw order — keep the two
  concerns separate (`EcdisDisplaySettings` in Core/DatasetPipelines,
  `ViewingGroupController`).
- **Overlay transparency:** coverage overlays (e.g. bathymetry over a
  base chart) must let lower layers show through where there is no
  data — render NODATA/fill values **transparent**, never as an opaque
  block. A wrong fill colour reads as "sea" or "land" and is a safety
  issue, not a cosmetic one.

## 2. Scale-dependent generalization & declutter
Showing everything at every zoom destroys legibility *and* performance.

- **Scale-band culling (SCAMIN-style):** instructions carry
  `ScaleMinimum` / `ScaleMaximum` (`ScaleVisibility` in Rendering.Scene;
  `MarinerSettings.IgnoreScaleMinimum` overrides); a feature is drawn only when the
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
  clutter. Deconflict overlapping labels (`LabelDeclutterer` /
  `WorldLabelDeclutterer` in Renderers.Skia); prefer suppressing a
  label to overprinting two.
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
- Vector sources yield **WGS-84 lat/lon (EPSG:4326)**; the scene
  projects to **Web Mercator (EPSG:3857)** (`WebMercator` in
  Rendering.Scene). Coverage grids in other CRSs (e.g. UTM S-102) go
  through `ICrsTransform` (ProjNet implementation in
  `EncDotNet.S100.Crs.ProjNet`). Keep the source→display projection at
  one well-defined boundary; don't smear half-projected coordinates
  through the pipeline.
- **Mercator distortion** grows with latitude — scale bars and any
  metric reasoning must be latitude-aware, not assumed uniform.
- **Antimeridian & poles** are sharp edges. Datasets keep their own
  longitude frame (e.g. S-411 in 0…360° or continuous past 180°);
  `AntimeridianGeometry` takes the short way across the seam and draws
  pole-enclosing rings as caps, `WorldCopies` draws the ±1 world copies,
  and `SeamAwareBoundsAccumulator` frames extents. Reuse these rather
  than interpolating naively in projected space.

## 6. Rendering performance (the cost model in this codebase)
The base plane rasterises the `VectorScene` IR directly in
`EncDotNet.S100.Rendering.Engine`: `S100VectorTileRenderer` (tiled, the
default) or `S100VectorSceneRenderer` (single surface,
`S100_VECTOR_SCENE_MODE=single`). Design record:
`docs/design/S100-Render-Subsystem-Design.md`, the engine README
(Internals) and `docs/design/map-render-architecture.md`.

- Cost is **vertex-bound** (~1 µs per vertex in the historical
  measurement, `docs/design/mapsui-performance.md`; that page's
  per-feature Mapsui path, path cache, raster snapshot and line-LOD were
  removed in #600/#601). Reducing vertices (generalization §2) and
  effective scale-band culling beat micro-optimising paints or swapping
  GPU backends. Any reintroduced simplification targets the scene IR.
- The tile grid is fixed to the world origin, so a constant-zoom pan
  reuses interior tiles; caches (hot, GPU, disk) are sized by
  `MachineProfile` tiers. Prediction/pre-warm and the overscale curtain
  are in the engine too — change them there, not in host code.
- **Watch rotation, fast-pan and zoom-band hand-off** specifically: they
  have historically exposed disappearing fills, seams, and render hangs.
- Measure with the **viewer-evaluation** loop + `get_render_stats` and
  `dotnet-trace`; attribute by `(layer × vertex-bucket)` before
  optimizing, and target the few highest-cost feature classes first.

## Review checklist (cartographic correctness)
1. Draw order: plane → `DrawingPriority` → areas, patterns, lines,
   points, text; set once in `VectorSceneBuilder`, not re-sorted.
2. Visibility gated by display category & scale band — separate from
   draw order; culling demonstrably effective.
3. NODATA / no-coverage renders transparent, not opaque.
4. Generalization conservative, zoom-bucketed, polygon-safe; no kinks
   on thick strokes.
5. Geometry-less text/overlay features tolerated.
6. Colours resolved through the active palette; contrast holds in Day,
   Dusk, and Night.
7. Projection applied at one boundary; latitude-aware metrics;
   antimeridian/pole handled via the shared seam helpers.
8. Performance change justified against the vertex-bound cost model and
   verified with the viewer-evaluation loop.

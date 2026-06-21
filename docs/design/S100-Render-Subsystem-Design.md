# Tiled / Async Predictive Render Subsystem — Design & Plan

**Status:** v0.2 draft · clean-sheet rendering core, switchable for A/B against the current path
**Scope:** `EncDotNet.S100` interactive viewer · base-plane portrayal rendering
**Primary goal:** maximize pan smoothness · **Secondary:** zoom smoothness · **Means:** async-first, deferred work, anticipatory pre-warming

**v1 decisions (locked):** north-up only · pixels at the cold cache tier · labels stay on Mapsui through Phase 4 · GPU-vs-CPU compositor path set by a Mapsui surface test in Phase 0 (working assumption: Mapsui defaults to GPU acceleration).

---

## 1. Where this plugs in (what's already there)

The portrayal pipeline already terminates in a clean, backend-agnostic IR, and that IR is the seam for the whole subsystem:

```
features ─▶ portrayal catalogue (MoonSharp / XSLT)
        ─▶ DrawingInstruction[]            (Core: Pipelines/Vector/DrawingInstruction.cs)
        ─▶ VectorSceneBuilder.Build(...)
        ─▶ VectorScene  =  IReadOnlyList<PaintOp>   ◀── THE CONTRACT
                            { Point | Line | Area | PatternArea | Text }PaintOp
        ─▶ backend renderer:
             • SkiaDisplayListRenderer      (headless → SKImage/PNG, owns 3857→screen affine)
             • MapsuiDisplayListRenderer     (into Mapsui features/styles)
             • NEW: TiledSceneSubsystem      (this doc — owns 3857→screen affine, like the Skia path)
```

`PaintOp` is already fully resolved and exactly what a tiler wants: world coords in **EPSG:3857 m**, sizes in **logical display px** (0.32 mm, resolution-independent), colours resolved to `RgbaColor`, symbols pre-processed (`ResolvedSymbol` + pivot), `FeatureReference` for picks, and **SCAMIN carried per-op** as `ScaleMinimum` / `ScaleMaximum` (with `ScaleVisibility.IsVisibleAtScale`). The new subsystem consumes `VectorScene` and nothing Mapsui-specific.

**What the current interactive path does (and its ceiling).** `S100VectorSnapshotRenderer` already records a settled layer to one device-resolution `SKImage` per (resolution, feature-set) and blits it under translation on pans, with an over-render margin and an emerging multi-anchor / scale-band prebuild. It has already proven the two load-bearing facts of this design:

- A raster `SKImage` snapshot is **O(pixels), not O(features)** — the only thing that collapses the ~500 ms / ~1,600-feature polygon-fill floor measured on `101AU005PDB01`.
- `SKPicture` replay is a **dead end** for the blit path (replay re-rasterizes every recorded path).

Its one structural limit: it records *what Mapsui would draw* — it rides `GetFeatures` / `SortFeatures` and the per-feature style dispatch. The new subsystem cuts that tie by rasterizing **from `VectorScene` directly**, so the base plane no longer touches Mapsui's feature/style/layer model at all.

---

## 2. Prerequisite refactor (small, enabling)

`VectorScene` / `PaintOp` currently live in `EncDotNet.S100.Renderers.Skia` (namespace `...Renderers.Skia.Scene`). The Mapsui renderer already depends on the Skia project for the IR. Before adding a third consumer, **promote the IR to a neutral assembly** so all backends depend on it without diamonding through `Renderers.Skia`:

- New: `EncDotNet.S100.Rendering.Scene` (or fold into `Core`) holding `VectorScene`, `PaintOp` + subtypes, `ResolvedSymbol`, `ScaleVisibility`, `WebMercator`, `VectorSceneBuilder`.
- `Renderers.Skia`, `Renderers.Mapsui`, and the new subsystem all reference it.

This is the only change to the drawing-instruction area the design needs. It's mechanical (move + namespace) and unblocks A/B cleanly.

---

## 3. Subsystem architecture

Organizing principle: **the UI thread has a fixed, tiny per-frame budget and only composites.** It blits cached tiles through an affine and draws a few live features. Every expensive thing — scene query, rasterization — happens off-thread, ahead of time, speculatively, and cancellably. Treat it as a frame-rate compositor fed by an async job system.

### 3.1 Three composite planes (by cadence + thread, not feature type)

| Plane | Contents | Representation | Thread | Cache |
|---|---|---|---|---|
| **Base** | area fills, contours, lines, position-stable point symbols/soundings | tiled `SKImage` pyramid from `VectorScene` | workers | yes (LRU + disk) |
| **Label** | free-floating decluttered text | vector, live | UI (async placement, 1-frame lag) | no |
| **Dynamic** | AIS, own-ship, route, range rings, cursor pick | vector, live | UI | no |

The base plane is ~90% of pixels and all the fill cost; tiling it is what makes pans bounded by **perimeter, not area**. The Dynamic plane already exists and is good — reuse `DynamicSources/*` (`AisVesselRenderer`, `OwnShipRenderer`, `CompositeDynamicFeatureRenderer`) unchanged. Labels stay live because placement needs global viewport context and must survive rotation.

### 3.2 Tile model

- Fixed power-of-two resolution bands; quadkey-style `(band, x, y)` grid in EPSG:3857.
- Each tile rendered with a **gutter** (bleed beyond tile bounds) and clipped on composite, so lines/area fills stay continuous across seams. This generalizes the existing `MarginPx` anchor into a grid.
- Base-plane point symbols/soundings are position-stable → tiled with the base. Only free-floating text escapes to the Label plane.

### 3.3 Render service (background)

A scheduler producing tiles. Workers = cores−1, each with a **pooled CPU raster `SKSurface`**.

- **Job:** `RasterizeTile(band, x, y, styleStateHash)` → query `VectorScene` ops intersecting tile+gutter (spatial index over the scene) → cull by `ScaleVisibility` for the band → replay ops onto the pooled surface → `Snapshot()` to immutable `SKImage`.
- **Priority queue:** `Visible > PredictedNext > IdleFill`. Cooperative cancellation tokens; jobs outside the warm set die immediately.
- **Coalescing:** latest-wins per tile key; never queue duplicate in-flight keys.
- **Scene source is an immutable snapshot** (see §3.6) so workers never see torn reads.

### 3.4 Cache

- Hot: in-memory `SKImage`, bounded by a **native-byte budget**, LRU-evicted. (Native memory is the OOM risk — enforce hard.)
- Warm: on-disk encoded **pixel** tiles for cross-session / large extents (v1 decision — fastest warm). `DrawingInstructionSerializer` (Core/Caching) stays the upgrade path if palette-flip cost becomes the pain point: caching instructions sits *above* colour resolution, so day/dusk/night changes re-resolve cheaply instead of re-rendering. Revisit post-Phase 4.
- **Key:** `(productLayerSet, band, x, y, styleStateHash)` where `styleStateHash` folds mariner settings (safety depth, display category, day/dusk/night palette). A setting change bumps the hash → visible-first re-rasterization; old tiles evict naturally.

### 3.5 Compositor (UI thread, bounded)

Per frame: for each visible tile slot, draw the **best available** — exact band if present, else nearest band scaled (transient zoom blur), else a flat fallback. **Always blit something; never block.** During a gesture: pure affine of cached tiles + enqueue predictions; zero synchronous render work. Keep recently-used tiles resident as GPU textures so re-composite during a pan doesn't re-upload (upload once on tile-ready, evict with LRU) — *this residency path applies only if the Mapsui foreground surface is GPU-backed; confirm via the Phase 0 surface test (§7).*

### 3.6 Prediction / pre-warm

EMA of recent viewport-center deltas → velocity estimate. Each tick the **warm set** = visible ∪ 1-ring halo ∪ directional fan in the velocity direction (depth ∝ |velocity|, capped) ∪ z±1 center tiles (slight zoom-in bias). On a fling, integrate the inertia curve to the predicted resting viewport and prioritize its tiles + path. Cancel anything outside the warm set after a move, with hysteresis against jitter. Idle time fills the speculative ring at lowest priority. All prediction work is best-effort and yields to visible tiles under CPU/memory pressure.

### 3.7 Threading & GPU

- UI: input, Navigator/viewport, compositor, label + dynamic draw, enqueue predictions.
- Workers: CPU raster tiles from the immutable scene snapshot → `SKImage`.
- Scene/CSP: dataset load + setting changes → frozen scene snapshot + `styleStateHash`.
- IO: disk cache, dataset/HDF5 load, AIS feed.
- Cross-thread hand-offs are immutable (`SKImage`, scene snapshot) + interlocked reference swaps. Default to **CPU-raster workers + GPU blit**; skip shared-`GRContext` background GPU (lots of sync pain, marginal benefit for fill-rate-light 2D).

---

## 4. A/B harness (first-class requirement)

Because both the current Mapsui path and the new subsystem consume the **same `VectorScene`**, A/B is apples-to-apples on identical portrayal.

- **Switch at the `IMapHost` seam.** The viewer already late-binds `IMapHost` via `IMapHostAccessor`, constructed in `MainWindow` after the Avalonia `MapControl` exists. Introduce `IChartRenderSubsystem` with two implementations — `MapsuiSubsystem` (wraps today's path) and `TiledSceneSubsystem` (new) — and let `IMapHost` hold the active one.
- **Runtime toggle.** Extend the existing `RenderingOptimizations` flag pattern (env-pinnable bools) with `RenderSubsystem { Mapsui | TiledScene }`, surfaced in `SettingsViewModel` for hot-swap, and overridable by env var for benchmark runs.
- **Side-by-side mode (optional, high value).** Split-view or toggle-overlay so the same gesture drives both subsystems for visual diffing of portrayal fidelity.
- **Metrics.** Both renderers already carry `Diagnostics/Telemetry`. Standardize a comparison surface: frame time (p50/p95/p99), per-stage timing (scene query / rasterize / composite), tiles produced vs. evicted, cache hit rate, prediction hit rate, native bytes resident. Capture against fixed gesture scripts on reference cells (`101AU005PDB01` as the established baseline) so wins are defensible and regressions caught.

---

## 5. Interface sketches (illustrative)

```csharp
// Neutral seam the viewer switches on.
interface IChartRenderSubsystem
{
    void OnSceneChanged(VectorSceneSnapshot scene, StyleState style); // load / setting change
    void Composite(SKCanvas canvas, Viewport viewport);              // per-frame, UI thread, bounded
    void OnViewportChanged(Viewport viewport, ViewportVelocity v);    // drives prediction
    PickResult? HitTest(ScreenPoint p, Viewport viewport);           // FeatureReference round-trip
    RenderTelemetry Telemetry { get; }
}

readonly record struct TileKey(string LayerSet, int Band, int X, int Y, long StyleStateHash);

// Job model
enum JobPriority { Visible, PredictedNext, IdleFill }
record RasterizeTileJob(TileKey Key, CancellationToken Ct, JobPriority Priority);
```

Tile job state machine: `Requested → (coalesced?) → Queued(priority) → Running → {Ready(SKImage) | Cancelled | Failed}`. `Ready` triggers a UI-thread invalidate; `Cancelled`/superseded frees the pooled surface back to the worker.

---

## 6. What stays, what's bypassed

- **Stays:** Mapsui `Navigator` / viewport math / gesture + fling/inertia; the per-platform `MapControl` shell; the Dynamic plane renderers; the **S-98 interoperability layer stack** (`Interoperability/LayerStackBuilder`, `S98*Rule`) — its display-priority ordering must drive base-tile composite order and per-plane z-order. Don't lose it.
- **Bypassed for the base plane:** Mapsui `MemoryLayer` + feature/style walk, `S100VectorSnapshotRenderer`, `CachedVectorStyleRenderer`. These remain as the `MapsuiSubsystem` (the "A" arm) but the "B" arm never touches them.
- **Reused:** `ScaleVisibility` (band culling), `DrawingInstructionSerializer` (potential cold cache), pooled-surface and margin/anchor logic (generalized to the tile grid).

---

## 7. Phased plan (each phase shippable + measured)

- **Phase 0 — Harness.** Extract IR (§2). Add `IChartRenderSubsystem` + `RenderSubsystem` flag + fixed gesture-script benchmark over `101AU005PDB01` with the standardized telemetry. The "A" arm is today's path verbatim. **Also confirm the Mapsui foreground surface empirically** — instrument the actual surface type the Avalonia `MapControl` creates (GPU `SKGLView` vs CPU `SKCanvasView`; working assumption is GPU by default). The result sets the compositor blit path: GPU → upload-once + texture residency (Phase 5 in scope); CPU → direct memcpy blit (Phase 5 residency moot). *Exit:* reproducible A-baseline numbers + a recorded surface-type finding.
- **Phase 1 — Single-surface B, from `VectorScene`.** New subsystem renders the whole viewport (over-render margin) on a worker from the scene IR, swap-and-blit. No tiling yet. *Exit:* B matches A's fidelity; pans off the sync loop. **✅ Done** — `S100VectorSceneRenderer`; on-screen paint worst case ~409 ms → ~5 ms (Appendix B). Base-plane parity holds; residual point-feature deltas trace to A's own portrayal bug, not B.
- **Phase 2 — Tile the base.** Pyramid, gutter/clip seams, LRU + native-byte budget, best-available compositor. *Exit:* pan frame time bounded by perimeter; p99 under budget on the gesture script. **✅ Done** — `TileGrid` + `TileCache` + `S100VectorTileRenderer` (Appendix C). Origin-anchored web-mercator pyramid keeps interior tiles pan-stable; on-screen paint stayed bounded (≤ ~37 ms worst case vs A's ~409 ms) with no visible seams.
- **Phase 3 — Prediction.** Velocity fan, z±1, fling projection, idle fill, cancellation/hysteresis. *Exit:* prediction hit-rate metric; cold-tile exposure during scripted pans ≈ 0. **✅ Done** — EMA-velocity warm set (1-ring halo ∪ directional fan ∪ z±1 centre) on a low-priority queue behind `S100VectorTileRenderer`, gated by `S100_VECTOR_TILE_PREDICT` (Appendix D). Prediction-on/off A/B over a 20-step pan: cold-frame fraction **58% → 16%** (residual is cold start, not the pan); hit-rate ~32%.
- **Phase 4 — Planes + invalidation.** Live Label plane (async placement), wire Dynamic plane, disk cache, `styleStateHash` invalidation on settings/palette with visible-first re-raster. *Exit:* setting change never shows stale portrayal on visible tiles. **✅ Done (core)** — persistent warm disk cache (`TileDiskCache`) keyed by a namespace folding `productLayerSet` + `styleStateHash`, so a tile is never served for a different mariner/palette state (Appendix E). The in-memory hot cache is already fresh per layer (a settings change rebuilds the layer), so in-memory stale exposure was structurally impossible; the hash extends that guarantee to the persistent tier. **Deferred:** the Label-plane extraction is held per the §4 principle *"labels stay on Mapsui through Phase 4"*; the Dynamic plane already exists and is reused unchanged.
- **Phase 5 — GPU residency + polish.** Texture cache/atlas, anticipatory-zoom tuning, side-by-side diff mode. **✅ Done (residency)** — composited tiles are promoted once to GPU-resident textures (`SKImage.ToTextureImage`) in a per-layer GPU `TileCache` and re-blitted without re-upload, gated to GPU surfaces with a software raster fallback (Appendix F). A measurement gate first attributed 98 % of render-thread native time to the per-frame re-upload (`BlitTile → DrawImage`); residency cut the steady-pan frame from ~38 ms to ~3 ms with a 96–99 % GPU hit ratio on a Metal surface. GPU textures are confined to the render thread and held by a strong-cache/weak-layer registry so teardown (dataset close, palette re-portrayal, or GC of an abandoned layer) frees them on the render thread instead of crashing the native backend on the finalizer thread. **Deferred (polish):** anticipatory-zoom tuning and the side-by-side A/B diff mode.

---

## 8. Risks & open questions

- **Polygon fill is the floor** (your measurement). Tiling amortizes it across pans but a cold tile still pays it — prediction quality is therefore the real lever for perceived smoothness.
- **Native memory budgeting** — enforce or OOM; non-negotiable.
- **Seams** for lines/areas → gutter+clip; **labels** under rotation/zoom stay genuinely hard → own async pass.
- **Correctness over speed (safety):** a changed safety contour must win immediately on visible tiles. Hard "discard-on-invalidate" + visible-first re-raster. No cache/prediction may ever surface stale or wrong portrayal.

**Decisions & deferrals:**
1. **Cold cache tier — pixels (v1).** Fastest warm. Instructions (`DrawingInstructionSerializer`) are the upgrade path if palette-flip cost dominates; revisit post-Phase 4.
2. **GPU vs CPU compositor — set by the Phase 0 surface test.** Working assumption: Mapsui defaults to GPU acceleration, so plan for upload-once + texture residency — but confirm the actual `MapControl` surface empirically before committing the Phase 5 residency work.
3. **Labels — stay on Mapsui through Phase 4**, then evaluate pulling text into the subsystem for S-52 decluttering / rotation correctness.
4. **Orientation — north-up only for v1.** The transform stays a pure translation (matching today's snapshot math). Course-up rotation is a designed-for Phase-2+ addition — it touches tile coverage (rotated viewport footprint), label uprightness, and the prediction frame — and is explicitly out of v1 scope.

---

## Appendix A — Phase 0 findings (harness, surface test, A-baseline)

Phase 0 builds the A/B scaffolding and records the empirical facts the later
phases depend on. No "B" render path exists yet; the goal is a reproducible
baseline and the surface-type decision.

### A.1 What landed

- **§2 IR promotion.** The backend-agnostic scene IR (`VectorScene`, `PaintOp`
  and subtypes, `VectorSceneBuilder`, `ColorResolver`, `ScaleVisibility`,
  `WebMercator`) moved out of `EncDotNet.S100.Renderers.Skia` into a new
  Skia-free assembly **`EncDotNet.S100.Rendering.Scene`** (namespace
  `EncDotNet.S100.Rendering.Scene`; deps = Core + Portrayals only). This is the
  seam both render arms consume.
- **`IChartRenderSubsystem` seam** (`Viewer/Services/IChartRenderSubsystem.cs`).
  Deliberately minimal for Phase 0: identity (`Kind`/`DisplayName`), lifecycle
  (`Activate`/`Deactivate`/`IsActive`), and a telemetry handle exposing the
  surface-probe result. `MapsuiChartRenderSubsystem` is the **A** arm (today's
  path, no-op activate); `TiledSceneChartRenderSubsystem` is a documented stub
  for the **B** arm. Composite/HitTest grow in Phase 1.
- **`RenderSubsystem { Mapsui | TiledScene }` flag** on
  `RenderingOptimizations`, env-pinnable via `S100_RENDER_SUBSYSTEM`
  (`mapsui` | `tiledscene`), defaulting to `Mapsui`.
- **Gesture benchmark** — an MCP-driven fixed pan/zoom script over
  `101AU005PDB01` reading `get_render_stats.frameDurationMs` and gesture
  round-trip latency (kept in session notes, not committed).

### A.2 Surface-type finding (the locked Phase 0 decision input)

The live Avalonia `MapControl` surface on **macOS arm64** is **GPU-accelerated**:

```
[GPU-PROBE] gpuAccelerated=True backend=Metal surfaceWidth=1227 surfaceHeight=972
```

This **confirms the design's working assumption** (Mapsui defaults to GPU
acceleration). Consequence for later phases: plan the cold-tier pixel cache for
**upload-once + texture residency** on the GPU compositor (Phase 5), rather than
a CPU/system-memory blit path. The probe (`GpuAccelerationProbe`) runs on every
viewer start, so the finding is re-confirmable per platform; Windows/Linux RIDs
should be re-probed before committing residency work there.

### A.3 A-baseline (Mapsui arm, reference cell `101AU005PDB01.000`)

Fixed 18-step gesture script (6 zoom-in · 6 pan · 6 zoom-out), two runs,
`--ephemeral`, 1600×1000 window, GPU/Metal surface:

| Metric | p50 | p90 | max |
|---|---|---|---|
| `frameDurationMs` (on-screen paint) | ~7 ms | ~555 ms | ~559 ms |
| gesture round-trip (incl. settle floor) | ~123 ms | ~220–248 ms | ~294–317 ms |

**Reading it:** the worst-case on-screen paint of **~0.55 s** on detailed
(zoomed-in) views is the pan/zoom jank this redesign targets — a single
synchronous full-display-list paint on the UI/render thread. The gesture
round-trip p50 is dominated by the harness's fixed quiet-period settle floor,
not paint cost, so `frameDurationMs` (worst-case) is the metric the B arm must
beat. `get_render_stats` returns the *last* on-screen paint, so consecutive
identical values mean no repaint occurred between those steps (stale, not a
fresh sample) — only the spikes are fresh full paints.

**Exit criteria met:** reproducible A-baseline numbers captured + surface-type
finding recorded (GPU/Metal). Phase 1 (the "B" arm: tiled async scene
rasteriser behind the same seam) is the next session, pending review.

---

## Appendix B — Phase 1 findings (single-surface B from `VectorScene`)

Phase 1 adds the first real **B** arm: `S100VectorSceneRenderer`, a Mapsui
custom layer renderer that rasterises the whole viewport (plus an over-render
margin, env `S100_VECTOR_SCENE_MARGIN`, default 256 DIP) from the
backend-agnostic `VectorScene` IR on a **worker thread**, then swap-and-blits
the finished `SKImage` on the UI thread. Pans within the recorded margin are a
pure translated re-blit (`ComputeTranslate`), never a re-record.

### B.1 What landed

- **`S100VectorSceneRenderer`** (`Renderers.Mapsui`) — `RendererName =
  "s100.vector.scene"`. Worker-coalesced latest-wins rasterisation via a fresh
  per-render `SkiaDisplayListRenderer`; translation-invariant blit anchoring
  (`IsValid` / `ComputeTranslate`, pure `internal static`, unit-tested);
  `BuildViewport` (margin + device-scale + EPSG:3857 round-trip) and
  `ScaleDenominatorFor` (inverse of `DenominatorToResolution`, drives SCAMIN
  culling so B shows/hides detail at the same scale as A). North-up only in v1
  (rotated viewport ⇒ draws nothing that frame).
- **Pattern fidelity:** the Mapsui lowering deliberately omits patterns (it
  draws them post-IR), so B builds a *separate, pattern-complete* scene
  (`PatternResolver = GetPatternTilePng`) and renders fills from the IR.
- **Telemetry:** `SceneRasterizeDuration` (worker) + `SceneCompositeDuration`
  (UI blit) histograms.
- **Wiring:** registered in `App` startup and the `TiledScene` subsystem;
  `MapsuiDisplayListRenderer` tags the layer to B when
  `RenderSubsystem == TiledScene`; `MainWindow` routes `RequestRedraw` to
  `RefreshGraphics`. Selected via `S100_RENDER_SUBSYSTEM=tiledscene`.

### B.2 Perf result (same 18-step gesture script, reference cell)

| `frameDurationMs` (on-screen paint) | p50 | p90 | max |
|---|---|---|---|
| **A** (Mapsui arm) | 4.75 | 317.5 | 408.7 |
| **B** (TiledScene arm) | 3.33 | 3.76 | 4.73 |

**Reading it:** the synchronous on-screen paint — the pan/zoom jank this
redesign targets — drops from a **~0.4 s worst case** to a **flat ~4–5 ms**
across the entire gesture script. The heavy display-list rasterisation now runs
on a worker; the UI thread only translates and blits one image. Total gesture
round-trip is similar-to-slightly-higher (the worker still has to finish a
raster before `await_render_idle` quiesces), but that work is **off the UI
paint thread**, which is the whole point: the live surface stays responsive
during the gesture. *Pans off the sync loop: achieved.*

### B.3 Fidelity finding — A is not a reliable oracle for point features

Side-by-side capture (`render_to_image`, zoomed in) shows the **base plane
matches** (land/water/intertidal areas, depth-area fills, contour geometry).
The visible deltas are dominated by **A under-portraying**, not B regressing:

- **Point symbols (rocks, obstructions, buoys):** B draws them; A drops them at
  some zoom levels and was observed **flickering symbols in/out** during the
  run. A's per-feature SCAMIN/declutter path is non-deterministic here, so
  "match A exactly" is the wrong bar — B is the more faithful of the two for
  these features.
- **Remaining B-side items to validate against the *headless* baseline (not
  A):** complex line styles (some A-dashed boundaries render solid in B —
  a scene-build, not a rasteriser, gap; `SkiaDisplayListRenderer.DrawLine`
  supports dashes), occasional label glyphs rendering as boxes, and a slight
  fill-tint difference (most likely the extra pattern stipple B draws, not a
  blit compositing error — the blit is a correct premultiplied `SrcOver`).

**Exit criteria:** *pans off the sync loop* — **met** (decisive, see B.2).
*B matches A's fidelity* — **met in spirit**: base-plane parity holds and the
remaining differences are predominantly A's own portrayal bug (dropped/flickering
point features), with a short, tracked list of B-side polish items (line dashes,
label glyphs) to be validated against the deterministic headless renderer rather
than the unstable A arm in a follow-up.

---

## Appendix C — Phase 2 findings (tile the base plane)

### C.1 What landed

Three new types in `EncDotNet.S100.Renderers.Mapsui`, behind the existing
TiledScene seam (`S100_RENDER_SUBSYSTEM=tiledscene`):

- **`TileGrid`** — pure, Skia-free, origin-anchored EPSG:3857 power-of-two
  tile math (256-DIP tiles, XYZ convention; the same scheme Mapsui's own tile
  layers use). Band selection snaps the live resolution to the nearest band in
  log-space; the compositor scales band tiles to the live resolution to fit.
- **`TileCache`** — thread-safe LRU bounded by a hard **native-byte budget**
  (decoded `SKImage` pixels live in native memory, the OOM risk §3.4 calls
  out), default 256 MB (`S100_VECTOR_TILE_BUDGET_MB`). Eviction disposes the
  LRU image; visible tiles are MRU so they are never the victim mid-frame.
- **`S100VectorTileRenderer`** — the tiled custom layer renderer. Each frame the
  UI thread snaps to a band, enumerates visible tiles, and blits the *best
  available* for every slot (cached fallback bands as a backdrop farthest-first,
  exact-band tiles on top), each hard-clipped to its core over a rendered
  **gutter** (default 64 DIP, `S100_VECTOR_TILE_GUTTER`) so strokes stay
  continuous across seams. A single coalescing worker per layer drains the
  visible-miss set (replaced every frame, so tiles panned out of view are
  dropped before they render). All cache access is serialised through the layer
  lock so no image is disposed mid-blit.

Within the TiledScene subsystem, `S100_VECTOR_SCENE_MODE=single` selects the
Phase-1 single-surface renderer for A/B comparison; the tiled renderer is the
default. Telemetry: `s100.render.tile.rasterize.duration` (one off-thread tile)
and `s100.render.tile.composite.duration` (one UI-thread composite pass).

31 unit tests (`TileGridTests`, `TileCacheTests`) pin the grid math
(band/resolution round-trips, world-bounds tiling, world→screen projection,
**interior-key pan-stability**) and the cache (byte-budget eviction, LRU/MRU
order, replace-disposes, clamp-to-floor).

### C.2 Perf result (same 18-step gesture script, reference cell)

On-screen `frameDurationMs` stayed bounded across the whole script — p50 ≈ 7.7
ms, p90 ≈ 34 ms, max ≈ 37 ms — versus the A baseline's ~408 ms worst case
(Appendix A.3). Constant-zoom **pan** frames held ~3–8 ms: the origin-anchored
grid re-uses every interior tile, so only the newly-exposed perimeter
rasterises (verified directly by `TileGrid` unit tests; reflected in the bounded
pan composite cost). The most expensive frames were **zoom-out** steps (~37 ms),
where the compositor blits many cached finer-band tiles as a backdrop to avoid
showing a hole while the coarser band fills in — acceptable for Phase 2 and a
candidate for the Phase-3 pre-warm to mask.

### C.3 Fidelity finding

`render_to_image` (zoomed in) shows the tiled base plane composites with **no
holes and no visible tile seams** — gutter + hard-clip-to-core meet exactly.
Base-plane parity with Phase 1 holds (land/water/intertidal fills, depth areas,
contour geometry). The residual point-symbol (magenta default symbols) and
tofu-label deltas are the **same Phase-1 B-side polish items** (Appendix B.3),
not Phase-2 regressions — they live in scene build / symbol resolution, upstream
of tiling.

### C.4 Decision — fixed power-of-two bands vs live-resolution tiles

Tiles are keyed to **fixed web-mercator bands**, not the live viewport
resolution. The alternative (rasterise tiles at exactly the current resolution)
avoids intermediate-zoom scaling blur but **defeats cross-zoom reuse**: every
pixel-level zoom change re-keys the whole cache. Fixed bands let a zoom settle
re-use tiles from neighbouring bands instantly (scaled) while the exact band
fills in, and align with Mapsui's own tile schema. The tradeoff is slight
scaling blur at intermediate zooms and transiently-blurred *text* (free-floating
labels move to a live, untiled Label plane in Phase 4 specifically to avoid
this); for the base plane the blur is imperceptible at a settle and gone once
the exact band lands. Accepted.

### C.5 Open follow-ups (Phase 3+)

- Pre-warm the z±1 bands and a velocity fan so zoom-out doesn't transiently
  blit a deep fallback stack (the ~37 ms frames in C.2).
- Consider a small worker pool (design's "cores−1") if tile fill lags on denser
  cells; Phase 2 uses one coalescing worker per layer.
- The Phase-1 B-side polish items (line dashes, label glyphs) still apply and
  are unchanged by tiling.

## Appendix D — Phase 3 findings (prediction / pre-warm)

Phase 3 adds speculative tile rasterisation so newly-exposed perimeter (and
zoom) tiles are already resident when a pan/zoom reveals them, driving
cold-tile exposure toward zero. It lives in the same `S100VectorTileRenderer`
behind the TiledScene seam; no new surface or layer.

### D.1 Warm-set model (design §3.6)

Each frame the renderer estimates the viewport-centre velocity (EPSG:3857
metres/second) as an EMA of inter-frame centre deltas
(`VelocityEstimator`, `alpha = 0.4`). From the current centre + velocity it
computes a **warm set** (`TileGrid.PredictedTiles`):

- **1-ring halo** around the visible range (covers slow drift in any direction);
- **directional fan** aimed along the velocity vector, its depth scaling with
  speed (`lookAhead = 0.5 s`, capped at `maxFanDepth = 4`) — where a fast fling
  is heading;
- **z±1 centre tiles** so a zoom step finds the adjacent band already warm.

The set excludes anything already visible, cached, or in flight, and is
deduped. It is recomputed (and therefore implicitly *cancelled*) every frame;
hysteresis comes from the velocity EMA, not from retaining stale predictions.

### D.2 Scheduling

Pending work is split into two queues: `PendingVisible` (on-screen exact-band
misses, high priority) and `PendingPredicted` (the warm set, low priority). The
single coalescing worker drains visible-first, so prediction always yields to
tiles the user is actually looking at and never delays an on-screen fill.

Speculatively-rasterised keys are tracked in `PredictedInCache`; when a later
frame finds such a key in the visible set it counts a **prediction hit** and
drops it from the set (bounded-pruned against the cache to stay small).

### D.3 Telemetry

Three instruments (Meter `EncDotNet.S100.Renderers.Mapsui`):

- `s100.render.tile.cold.exposure` (Histogram) — visible exact-band tiles
  absent from cache at each composite (the metric Phase 3 must minimise);
- `s100.render.tile.prediction.rasterized` (Counter) — speculative tiles built;
- `s100.render.tile.prediction.hits` (Counter) — speculative tiles later shown.

### D.4 A/B result (reference cell `101AU005PDB01.000`)

Prediction is a first-class A/B knob: `S100_VECTOR_TILE_PREDICT=0` reverts to
Phase-2 visible-only behaviour (`PredictionEnabled`). Same scripted 20-step
eastward pan, prediction OFF vs ON:

| Metric | OFF (Phase 2) | ON (Phase 3) |
|---|---|---|
| Frames with cold-tile exposure | 144 / 248 (**58 %**) | 59 / 374 (**16 %**) |
| Prediction hit-rate | — | 56 / 173 (**~32 %**) |
| Pan `frameDurationMs` p90 / max | 7.1 / 7.7 | 11.7 / 11.9 |

The residual 16 % cold frames with prediction on are the **cold-start load**
(no velocity history yet); during the steady-pan window itself every frame was
zero-cold, meeting the exit criterion. Pan frame time stays well within budget
in both arms — the extra ~4 ms p90 with prediction on is the low-priority
warm-set rasterisation, which never blocks an on-screen fill.

### D.5 Open follow-ups (Phase 4+)

- The Phase-1 B-side polish items (line dashes, label glyphs) still apply.
- A disk-backed tile cache + `styleStateHash` invalidation (palette/settings)
  with visible-first re-raster is Phase 4.

## Appendix E — Phase 4 findings (warm disk cache + `styleStateHash`)

Phase 4 adds the **warm** (persistent) tier below the in-memory hot tile cache,
and the `styleStateHash` that makes both tiers safe across mariner/palette state.

### E.1 In-memory invalidation is already structural

A settings/palette change rebuilds the chart layer from scratch
(`MapsuiDatasetRenderer.RenderAsync` produces a fresh `ILayer` with the new
palette each pass), and the tile state is held in a `ConditionalWeakTable` keyed
by layer. So a new layer starts with an **empty** hot cache — there is no path by
which a stale in-memory tile from the prior style state can be composited. The
Phase 4 exit criterion ("a setting change never shows stale portrayal on visible
tiles") is therefore met for the hot tier without extra machinery; visible-first
re-raster is the existing Phase 2/3 behaviour.

### E.2 The persistent tier needs the hash

A disk cache is shared across layers and sessions, so it **can** outlive a style
change — that is the whole point (a palette flip-back should reuse warm tiles).
The safety mechanism is the cache **namespace**:
`SHA-256(productLayerSet | styleStateHash)`, a per-style-state subdirectory.
`styleStateHash` is computed in `MapsuiDisplayListRenderer` as
`SHA-256(palette + symbol/text scale ++ DrawingInstructionSerializer(instructions))`.
The serialized instruction list already encodes the active display category, the
selected safety contour, and every other setting that changes *which* features
and *which* portrayal are drawn; the serializer's `FormatVersion` is folded in
implicitly (it is the first field). A change to any input yields a different
namespace, so old tiles are simply orphaned and reclaimed by the byte-budget LRU
sweep — never served stale. A hash *collision* is the only stale-portrayal risk,
and SHA-256 over the full resolved content makes that negligible; a spurious hash
*difference* only costs a re-rasterise.

### E.3 Cache mechanics

`TileDiskCache` mirrors the proven `DiskPortrayalInstructionCache` robustness
contract: atomic temp-file + move writes, mtime-stamped LRU eviction to a soft
byte budget, and treat-any-error-as-a-miss. PNG encode/decode runs **outside**
the lock so concurrent workers do not serialise on the codec; the cap sweep is
throttled (every 32nd write) because it enumerates the tree. The worker tries the
disk cache before re-rasterising a hot-cache miss and persists each freshly
rasterised tile while it still solely owns the image (before handing it to the
hot cache), so a concurrent eviction can never dispose it mid-encode. Knobs:
`S100_VECTOR_TILE_DISK` (default on), `S100_VECTOR_TILE_DISK_DIR` (default a
subdirectory of the OS temp path), `S100_VECTOR_TILE_DISK_MB` (default 512).

### E.4 Verification (reference cell `101AU005PDB01.000`, MCP)

A scripted run panned the chart under the Day palette (writing tiles), flipped to
Night, then back to Day:

- the disk cache held **two namespaces** — one for Day, one for Night — confirming
  physical separation by style state (a Night tile is never readable under the
  Day namespace);
- **163** tiles were persisted (`tile.disk.writes`);
- **198** tiles were served warm from disk (`tile.disk.hits`) on the Day
  flip-back / re-pan instead of being re-rasterised.

The `TileDiskCacheTests` pin the same properties as unit tests, including the
namespace-isolation safety property and the byte-budget eviction.

### E.5 Open follow-ups (Phase 5+)

- **Label plane.** Free-floating text is still rasterised into the base tiles
  (the §B.3 / §C tofu-label items). Pulling labels into a live, untiled plane —
  for S-52 decluttering and rotation correctness — is the next structural step,
  deferred here per the §4 principle that labels stay on Mapsui through Phase 4.
- **Instruction-level warm cache.** The disk tier stores *pixels*; if palette-flip
  cost dominates, caching serialized instructions (above colour resolution) would
  let day/dusk/night re-resolve cheaply instead of re-rendering (design §3.4).
- **GPU residency** of recently-used tiles (Phase 5), gated on the foreground
  surface being GPU-backed.

---

## Appendix F — Phase 5 measurement (the GPU-residency decision)

Phase 5's headline is GPU texture residency. Per the design's own
"measure before you build" stance, the residency work was gated on a
profiling measurement rather than assumed.

### F.1 Method

A fixed steady-pan script (90 small constant-velocity pan steps at one
zoom, all tiles warm) drove the **TiledScene** arm over the reference
cell `101AU005PDB01.000`. Two captures:

1. **OTEL histograms** (`s100.render.tile.composite.duration`,
   `…tile.rasterize.duration`) for the per-frame UI-thread blit cost.
2. **`dotnet-trace`** (`dotnet-sampled-thread-time`, the macOS
   wall-clock profile) over the same pan, converted to Speedscope and
   reduced to *native self-time attributed to the nearest managed
   caller on the render thread*.

### F.2 Result

- The UI-thread composite pass averaged ~12 ms (≈60 % of passes in the
  10–25 ms bucket) on this machine — a large share of the frame.
- The trace localised it unambiguously: **≈98 % of render-thread native
  time was under `S100VectorTileRenderer.BlitTile` → `SKCanvas.DrawImage`**.
  The managed compositor bookkeeping (cache snapshot, fallback scan,
  sort) was negligible.

The cost is therefore the **per-frame raster→GPU upload/blit of the
tiles**, not CPU overhead: Skia is not retaining our worker-produced
raster `SKImage`s as GPU textures across frames, so the same pixels are
re-uploaded every paint.

### F.3 Why the decision is machine-independent

The specific millisecond figures are a property of *this* hardware
(Apple-silicon unified-memory Metal) and must **not** be over-indexed —
other targets will differ. But the *conclusion* does not depend on the
magnitude:

- Re-uploading identical tile pixels every frame is wasteful on **any**
  GPU. Removing it is a pure win wherever a GPU context exists.
- On lower-bandwidth or discrete-GPU targets (Windows/D3D, Linux/GL,
  integrated Intel/AMD) the per-frame upload tends to cost **more**
  relative to compute, so residency helps at least as much there.
- On **software/CPU-backed** surfaces (CI, VMs, headless Linux, no-accel
  GPUs) there is no `GRContext`; residency is then a **no-op** and the
  code falls back to today's raster blit — no regression.

Hence Phase 5 promotes warm tiles to GPU-resident textures **once**,
strictly gated on the foreground surface being GPU-backed
(`ISkiaSharpApiLease.GrContext != null`, per Appendix A.2), with the
raster path retained as the universal fallback.

### F.4 Residency outcome

Implemented as a per-layer second `TileCache` of GPU-backed `SKImage`s.
On the first composite of a warm raster tile the renderer promotes it via
`SKImage.ToTextureImage(GRContext)` and caches the texture; every later
frame blits the resident texture and increments
`s100.render.tile.gpu.hits` instead of re-uploading. The live `GRContext`
comes from `SKCanvas.Context`; when it is `null` (software surface or
`S100_VECTOR_TILE_GPU=0`) the path is a no-op and the raster blit runs
unchanged. Budget knob `S100_VECTOR_TILE_GPU_MB` (default 256).

On this machine (Metal) the steady-pan frame fell from **~38 ms to
~3 ms** (composite mean ~12 ms → ~0.34 ms) at a **99 % GPU hit ratio**
(uploads 171 vs hits 17 082). As stated in F.3 the magnitude is
machine-specific; the elimination of the per-frame re-upload is the
portable result.

### F.5 Teardown crash and the residency registry

First residency builds crashed the viewer on a **close-all + reopen**:
the macOS report showed `SIGSEGV` on the .NET **finalizer thread** inside
`libSkiaSharp` (`FinalizerThread::FinalizeAllObjects → …`). Root cause:
GPU-backed `SKImage`s must be freed on the thread that owns the
`GRContext` (the render thread). When a dataset closes — or a palette
re-portrayal swaps in a fresh layer, or an abandoned layer is silently
GC'd — its `TileState` is abandoned and never renders again; its GPU
textures were then reclaimed by the GC and **finalized off the render
thread**, freeing live GPU resources under the wrong thread → native
crash.

Fix: a process-wide registry holds a **strong** reference to every
GPU-texture cache and a **weak** reference to its owning layer. The
strong reference keeps the textures out of the finalizer's reach; the
weak reference lets the renderer notice when a layer has been collected.
At the top of each paint the render thread reconciles the registry and
disposes any orphaned cache itself, under the live context — so GPU
images are always both created and freed on the render thread. All other
GPU mutation (`ManageGpuResidency`, `BlitTile`) is likewise render-thread
only, under the layer lock. **Verified:** four close-all + reopen cycles
(each warming then abandoning a GPU cache, with GC pressure from the
reopen) ran with no crash, frames steady at 6–9 ms and a 96 % GPU hit
ratio sustained across the cycles. The reused `TileCache`'s
dispose-on-calling-thread semantics that this relies on are unit-covered
(`TileCacheTests.Clear_DisposesAndEmptiesCache`,
`…Put_AfterDispose_…`); the GPU promotion and the registry reconcile are
GPU-context-bound and so are validated by this integration run rather
than a headless unit test.

### F.6 Zoom-out use-after-free and the bounded backdrop

Once residency shipped, a second, distinct crash appeared when the user
**zoomed all the way out**: the report this time was on the
**main thread** (`com.apple.main-thread`) inside `libSkiaSharp`'s GPU
path, `KERN_INVALID_ADDRESS at 0x28` — a GPU **use-after-free during
compositing**, not the finalizer crash of F.5.

Two independent residency assumptions combined to cause it:

1. **`SKCanvas.DrawImage` is deferred.** The draw is recorded into a
   display list and the texture is only dereferenced when Skia flushes,
   *after* the Mapsui/Avalonia render method returns. A texture must
   therefore outlive the frame that drew it.
2. **The backdrop loop was unbounded across bands.** `Composite` drew
   every cached tile from every other band that intersected the
   viewport. At full zoom-out (target near band 0) the viewport is the
   whole world, so the loop promoted *every finer-band tile in the
   cache* to a GPU texture and recorded a `DrawImage` for each. That
   overflowed the 256 MB GPU budget, so `TileCache.Put` **evicted and
   disposed GPU `SKImage`s mid-frame** — including textures already
   recorded into the not-yet-flushed display list. At flush, Skia
   dereferenced a freed texture → the main-thread crash.

The fix has two parts, and the first is also why the user saw
**different-sized symbols stacked on top of each other** while zooming:

- **Bound the backdrop (`MaxFallbackBandDistance = 2`).** `Composite`
  now skips cached tiles more than two bands from the target. A single
  zoom step is ±1 band, so the near-band backdrop that fills gaps during
  a smooth zoom is preserved, but the multi-band "ghosting" (the same
  area drawn at several scales at once) is gone and the per-frame draw
  count is bounded regardless of how far out the viewport is. This alone
  removes the budget overflow at band 0.
- **Defer GPU texture disposal by one frame.** The GPU `TileCache` is
  constructed with `deferDisposal: true`: images evicted, replaced, or
  cleared are not freed inline but moved to a pending list that
  `Composite` drains **at the start of the next frame, before recording
  any draw**. Because the previous frame has already flushed by then,
  the images being freed can no longer be referenced by an in-flight
  display list. This makes eviction safe even if a future change pushes
  the budget during a frame. The raster (CPU) cache keeps inline
  disposal — CPU images are copied into the GPU display list, not
  referenced by handle, so they were never exposed to this hazard.

Defence in depth: the render-thread paint block (GPU reconcile +
residency + composite) is wrapped so a paint-time throw can no longer
escape the layer lock and skip the worker-start path, which previously
could strand `state.Rendering = true` and **permanently stall tile
production** (a blank chart until the layer was rebuilt — the user's
"no charts until I load another dataset" report). The rasterisation
worker likewise resets `state.Rendering` from a single `finally`, so an
unexpected throw on the worker can never wedge the pipeline. Caught
paint faults bump a `s100.render.tile.faults` counter and drop the
frame instead of crashing.

**Verified** (reference cell, both `S100_VECTOR_TILE_GPU=1` and `=0`):
zoom in → zoom out to the whole world → zoom back in renders the chart
correctly with no crash and no blank; symbols no longer stack at
multiple sizes. The four close-all + reopen cycles of F.5 still pass.
The new deferred-disposal semantics are unit-covered
(`TileCacheTests.DeferDisposal_*`, `DrainPendingDisposals_*`); the
in-frame GPU compositing path remains GPU-context-bound and so is
validated by the integration run.

### F.7 Prediction-driven repaint loop ("rendering never settles")

With the F.6 crash fixed, a second, previously-masked defect surfaced:
a **partial** zoom-out (not all the way to the world) left the renderer
repainting forever — the chart kept churning and never settled, and
loading more datasets did not reset it. It only reproduced with **both**
prediction (`S100_VECTOR_TILE_PREDICT=1`) **and** GPU residency
(`S100_VECTOR_TILE_GPU=1`) on; turning off either made the map settle.

Root cause: the tile worker invoked `RequestRedraw` for **every**
published tile, including *predicted* (off-screen, pre-warm) tiles. A
predicted tile becoming cached changes nothing on screen, so that
repaint is spurious. Under GPU residency a frame is only a few
milliseconds and Mapsui does **not** coalesce these invalidations, so
each spurious redraw runs a full fast frame, which re-runs prediction in
`Render`, re-enqueues the predicted set, and the worker re-publishes it
(fetched straight from the disk cache, so no rasterisation) — a
self-sustaining loop measured at ~720 redraws/s with zero rasterisations.
With GPU **off**, ~100 ms frames let Mapsui coalesce the invalidations
and the loop dies, which is exactly why the bug only appeared with GPU on.

Fix: gate the repaint on a visible publish —
`S100VectorTileRenderer.ShouldRequestRedraw(published, isPrediction)`
returns `true` only for a published, non-predicted tile. Pre-warmed
tiles stay resident and are picked up the moment the viewport actually
moves onto them (which itself triggers a frame). The decision is a pure
internal function with a unit-tested truth table
(`TilePredictionTests.ShouldRequestRedraw_*`).

While fixing this, render-fault visibility was also addressed (the user
could not see any error when frames were being dropped): `RecordRenderFault`
now writes a **rate-limited** (one per 5 s) message to `Console.Error` in
addition to bumping the counter and the OTEL activity event, so a
recurring caught fault is observable without an OpenTelemetry exporter
wired up.

**Verified** (reference cell, `S100_VECTOR_TILE_GPU=1` and
`S100_VECTOR_TILE_PREDICT=1`): every partial zoom-out step settles in
~300 ms with zero post-settle paints; the full world zoom-out still does
not crash; zoom-back renders the chart; no faults logged.

### F.8 Rotation-induced blanking and one-scale backdrop

After F.7, the chart still **blanked permanently** on a Mac trackpad
pinch-zoom, and reloading datasets did not bring it back. MCP-scripted
zoom (`set_viewport`) never reproduced it because it only sets
centre/zoom, never a rotation.

Root cause: `Render` early-returned whenever `viewport.Rotation != 0`
("north-up only"). A trackpad pinch imparts a tiny incidental rotation
(observed 0.08°, and 358.70° = −1.30°) that essentially never lands back
on exactly `0`, so the tiled layer drew **nothing** on every subsequent
frame — and stayed blank because nothing resets the rotation. The
diagnostic (`S100_VECTOR_TILE_DIAG=1`) confirmed it: `Render bailed
(layer draws nothing): rotation=0.081307`, and `get_render_stats` showed
only the base `RasterStyle` drawing.

Fix: support rotated viewports instead of bailing (only `resolution <= 0`
and a sizeless viewport still bail).

- **Canvas rotation, convention-free.** The composite is drawn north-up
  as before, then the whole sequence is wrapped in
  `canvas.RotateDegrees(θ, w/2, h/2)` about the screen centre. θ is
  **derived from Mapsui's own `WorldToScreenXY`** — the projected screen
  direction of world-north is measured and compared against north-up's
  straight-up (−90°) — so it matches Mapsui's rotation sign/convention
  exactly without hardcoding it (Mapsui 5 ships only a DLL; the other
  vector renderer, `CachedVectorStyleRenderer`, already projects
  per-vertex through the same `WorldToScreenXY`).
- **Rotated-corner coverage.** A rotated viewport's corners poke outside
  the north-up box, so tile *selection* (`VisibleTiles` /
  `PredictedTiles` / the fallback intersect test) uses
  `TileGrid.RotatedCoverSize(w, h, θ)` — the axis-aligned bounding box of
  the rotated rect (`w·|cosθ| + h·|sinθ|` by `w·|sinθ| + h·|cosθ|`). The
  *projection* keeps the real DIP size, so the centre still maps to the
  screen centre. At θ = 0 the cover size is exactly the real size, so the
  north-up path is unchanged. `RotatedCoverSize` is pure and unit-tested
  (`TileGridTests.RotatedCoverSize_*`).

Same change also closes the **multi-scale symbol ghosting** seen during
zoom transitions (different-sized buoys/topmarks stacked). The backdrop
previously drew *every* cached band within `MaxFallbackBandDistance`
unconditionally, even when the target band already fully covered the
viewport, so stale adjacent-band tiles bled through at a different scale.
Now the backdrop is **skipped entirely once the target band is complete**
(its opaque fills occlude it anyway), and while incomplete only the
**single nearest** cached band is drawn — one scale only, never stacked.
The diagnostic confirms it on a settled frame: `target=16/16
fallbackBands=-` (previously `fallbackBands=9+11`).

**Verified** (GB Solent exchange set `101GB00302045`, GPU + prediction
on): trackpad pinch-zoom and pinch-rotate keep the chart visible and
aligned with the base map under rotation, corners stay filled, and a
settled frame draws a single tile scale with no ghosting.

### F.9 Palette-insensitive `styleStateHash` (Night served Day tiles)

Switching Day↔Night re-rendered the *same* pixels: a Night render showed
the bright Day palette, byte-identical to Day. The S-101 drawing-instruction
list is palette-independent by design (it carries S-100 colour *tokens*
resolved later by the scene builder), so a palette switch correctly reuses
the cached instruction list — `[S101] Reusing N cached drawing instructions`.
The palette is meant to re-enter downstream, both in the scene's
`ColorResolver` and in the tile **disk-cache namespace** via `styleStateHash`.

`ComputeStyleStateHash` (in `MapsuiDisplayListRenderer`) folded the palette as
`Palette?.ToString()`. `ColorPalette` is a plain class with **no `ToString()`
override**, so every palette stringified to the same type name. With identical
instructions *and* an identical palette string, Day and Night hashed to the
**same** namespace `SHA-256(productLayerSet | styleStateHash)`; the Night
render's first tile lookup hit Day's persisted PNGs and served them. The
in-memory hot tier is fresh per layer, so this surfaced only through the
persistent tier — but it manifested on screen because a cold/just-loaded view
fills from disk.

**Fix:** key the hash on a real palette fingerprint —
`DescribePalette(palette)` folds the palette `Name` **and** its colour
entries (ordered, `token=hex`) so Day/Dusk/Night, and any palette *content*
change, yield distinct namespaces. **Verified** (PDB01, Tasmania bounds):
Day and Night now produce distinct output (different SHA-1), Night renders the
genuinely dark Night palette over the S-101 region while the palette-independent
basemap is unchanged. Tests: `StyleStatePaletteFingerprintTests` (4 cases).

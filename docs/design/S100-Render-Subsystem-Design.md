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

### 1.1 Processor lifecycle seam

Processor lifetime is independent of rendering backend and Viewer policy.
`DatasetProcessorOwner` in `EncDotNet.S100.Datasets.Pipelines` owns processors
by stable `MapDatasetId`. Registration is an explicit ownership transfer;
duplicate identities are rejected without taking ownership. Removal makes a
processor unavailable immediately but defers its `IDisposable` cleanup until
active `DatasetProcessorLease` instances finish, so cancellation or concurrent
remove cannot dispose a processor beneath an in-flight render.

The Viewer `DatasetLoaderService` creates processors and coordinates catalogue
prompts, notifications, recent files, validation, rendering, layer policy, and
optional framing, but delegates registration, lookup, failed-load rollback,
lazy-unload removal, shutdown, and disposal to that reusable owner. Layer
replacement, S-98 composition, time registration, and presentation refresh
remain Viewer responsibilities until later map-session extraction slices.

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
| **Base** | area fills, contours, lines | tiled `SKImage` pyramid from `VectorScene` | workers | yes (LRU + disk) |
| **Overlay** | point symbols, point-anchored soundings | vector, live (constant screen size) | UI (render thread, per-frame) | no |
| **Label** | free-floating decluttered text | vector, live | UI (async placement, 1-frame lag) | no |
| **Dynamic** | AIS, own-ship, route, range rings, cursor pick | vector, live | UI | no |

The base plane is ~90% of pixels and all the fill cost; tiling it is what makes pans bounded by **perimeter, not area**. The Dynamic plane already exists and is good — reuse `DynamicSources/*` (`AisVesselRenderer`, `OwnShipRenderer`, `CompositeDynamicFeatureRenderer`) unchanged. Labels stay live because placement needs global viewport context and must survive rotation. Point symbols and soundings are drawn live on the **Overlay plane** rather than baked into base tiles: a tile is rasterized once per resolution band and then composited scaled by `ResolutionForBand(band)/resolution`, so anything baked in scales with the band fit (and transiently with a coarser fallback band). S-100 requires point symbols and soundings to hold a **constant on-screen size** through a zoom, so they must be drawn each frame against the live viewport (see Appendix F.11).

### 3.2 Tile model

- Fixed power-of-two resolution bands; quadkey-style `(band, x, y)` grid in EPSG:3857.
- Each tile rendered with a **gutter** (bleed beyond tile bounds) and clipped on composite, so lines/area fills stay continuous across seams. This generalizes the existing `MarginPx` anchor into a grid.
- Base-plane tiles carry area fills, contours, and lines. Point symbols and soundings are **not** tiled — they would scale with the band fit — so they escape to the live Overlay plane (constant screen size). Free-floating text escapes to the Label plane.

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

- **Switch at the map adapter seam.** `MainWindow` attaches the optional
  `AvaloniaMapsuiMapAdapter` after the live `CaptureSynchronizedMapControl`
  exists, then constructs the Viewer-owned `MapsuiMapHost`. The host implements
  focused layer, viewport, coordinate, snapshot, and invalidation capabilities
  and retains the active `IChartRenderSubsystem` lifecycle without exposing a
  monolithic consumer contract. Layer ownership and viewport behavior delegate
  to reusable `MapsuiLayerBands` and `MapsuiMapNavigator` components that
  operate on `Mapsui.Map` without Avalonia. UI dispatch, live-control
  invalidation, coordinate conversion, and framework capture live in
  `EncDotNet.S100.Renderers.Mapsui.Avalonia`; Viewer telemetry subclasses its
  capture-synchronized control without moving dataset or UX policy into the
  adapter.
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
- **Phase 4 — Planes + invalidation.** Live Label plane (async placement), wire Dynamic plane, disk cache, `styleStateHash` invalidation on settings/palette with visible-first re-raster. *Exit:* setting change never shows stale portrayal on visible tiles. **✅ Done** — persistent warm disk cache (`TileDiskCache`) keyed by a namespace folding `productLayerSet` + `styleStateHash`, so a tile is never served for a different mariner/palette state (Appendix E). The in-memory hot cache is already fresh per layer (a settings change rebuilds the layer), so in-memory stale exposure was structurally impossible; the hash extends that guarantee to the persistent tier. The **Label plane** is complete: free-floating text was lifted out of the base tiles onto the live Overlay plane with the symbol/sounding work (Appendix F.11, giving constant on-screen size), and the Label-plane completion adds priority-driven declutter, upright-under-rotation text, and per-run glyph fallback on that plane (Appendix G). The Dynamic plane already exists and is reused unchanged.
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
3. **Labels — pulled into the subsystem (Phase 4 complete).** The §4 "stay on Mapsui through Phase 4" hold is **lifted**. Free-floating text already escaped to the live Overlay plane with the symbol/sounding work (Appendix F.11), so labels hold a constant on-screen size; the Label-plane completion adds priority-driven declutter, upright-under-rotation text, and per-run glyph fallback on that same plane (Appendix G). Text is never baked into a base tile.
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
  continuous across seams. A tier-sized pool of coalescing workers per layer
  drains the visible-miss set (replaced every frame, so tiles panned out of view
  are dropped before they render). All cache access is serialised through the
  layer lock so no image is disposed mid-blit.

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
- A small per-layer worker pool (`S100_VECTOR_TILE_WORKERS`, sized by tier) now
  drains the visible-miss queue in parallel, with a process-wide cap of the
  logical-core count so multi-cell exchange sets don't oversubscribe; measured to
  cut single-cell cold-tile latency ≈3× on a 16-core host. `LowEnd` keeps one
  worker. The per-layer size is a floor, not a ceiling: a busy layer borrows idle
  global capacity toward the process-wide cap for visible work, with a fairness
  floor reserving each other active-visible layer its share (issue #432; design
  §D.2).
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

Pending work is split into three queues drained in strict priority order:
`PendingVisible` (on-screen exact-band misses, high priority), `PendingPredicted`
(the same-band warm set, low priority), and `PendingCrossBand` (the idle
cross-band ±1 pre-warm set, lowest priority — see D.5). The pool of coalescing
workers drains visible-first, so prediction always yields to tiles the user is
actually looking at and never delays an on-screen fill; same-band prediction in
turn drains before cross-band pre-warm, so warming an adjacent band never delays
either higher tier. Within each tier a worker dequeues **centre-first**
(`TakeNearest`): the pending tile whose world centre is nearest the current
viewport centre rasterises before the perimeter, cutting time-to-centre-fill on a
cold pan/zoom. Equal-distance ties break deterministically on `(band, y, x)`, so
the drain order never depends on the pending set's hash iteration order. The pool
size floor is `RenderingOptimizations.TileWorkerCount` (tier-sized); a per-layer
`ActiveWorkers` count plus a process-wide `s_activeWorkerTotal` cap (core count)
bound how many run at once.

**Elastic borrowing (issue #432).** The per-layer count is a *floor*
(reservation), not a hard ceiling. When a layer has a visible cold backlog and
global room exists, it may start workers above its floor toward `s_maxTotalWorkers`
so idle cores drain a single busy layer's cold-miss queue instead of sitting
unused (the common one-busy-layer / idle-siblings shape of a cold pan). Three
invariants keep this safe. **(1) Visible-only:** the elastic ceiling is gated to
the *visible* pending count — predicted/speculative tiles (including the
cross-band pre-warm set) never justify borrowing, so a busy layer's off-screen
prewarm can't occupy borrowed cores a sibling wants for on-screen work.
`ComputeWorkersToStart` encodes the cap arithmetic (the predicted and cross-band
sets are summed into its speculative term, so both are served only by the floor).
**(2) Prompt give-back:** a borrowed (above-floor) worker sheds itself at the
visible→speculative boundary — `ShouldWorkerExit` returns true once no visible work
remains and `ActiveWorkers > TileWorkerCount` — releasing its slot under
`state.Sync` so a cascade of sheds converges on the floor without over-shedding.
A *baseline* worker instead stays alive while any predicted **or** cross-band work
remains, so the speculative tiers still drain. Tile rasters are short and
non-preemptible, so capacity returns within roughly one tile's raster time; no
preemption machinery is needed. **(3) Fairness floor:** before lending, each
*other* layer that currently has visible cold work (tracked in a time-windowed
active-visible-layer registry, keyed by `TileState`) keeps its `TileWorkerCount`
reservation, so a dense bottom-of-z-order layer painting first cannot borrow the
whole budget and starve later-painting siblings — it can only lend *leftover*
room. On a `LowEnd` (single-worker) host the elastic ceiling collapses to the
floor and the whole path no-ops.

Speculatively-rasterised keys (both same-band predicted and cross-band pre-warm)
are tracked in `PredictedInCache`; when a later frame finds such a key in the
visible set it counts a **prediction hit** and drops it from the set
(bounded-pruned against the cache to stay small). Because `TileKey` carries the
band, a cross-band pre-warm tile scores its hit when a zoom later makes that band
visible.

### D.3 Telemetry

Three instruments (Meter `EncDotNet.S100.Renderers.Mapsui`):

- `s100.render.tile.cold.exposure` (Histogram) — visible exact-band tiles
  absent from cache at each composite (the metric Phase 3 must minimise);
- `s100.render.tile.prediction.rasterized` (Counter) — speculative tiles built;
- `s100.render.tile.prediction.hits` (Counter) — speculative tiles later shown.

Two cold-path latency instruments quantify the user-felt cost of a cold
gesture (zoom/pan starting from an empty cache):

- `s100.render.tile.cold.latency` (Histogram, ms) — end-to-end age of a visible
  tile, from the first frame it is seen cold-visible to the worker publishing
  it (queue wait + disk read/rasterise). Distinguishes a slow worker from a
  slow Mapsui paint;
- `s100.render.tile.visible.queue.depth` (Histogram, tiles) — cold-miss burst
  depth when the worker spins up.

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

### D.5 Idle cross-band pre-warm (issue #428)

Same-band prediction (D.1) biases only the two band ± 1 **centre** tiles, so a
zoom that crosses a band boundary still pays near-full cold latency at the new
band. Cross-band pre-warm closes that gap: when a layer is otherwise idle the
renderer warms the whole viewport footprint of both adjacent bands, so a
subsequent zoom-in or zoom-out starts warm.

- **Warm set** — `TileGrid.CrossBandPrewarmTiles` returns the band ± 1 tiles
  covering the current viewport (selected against the same world viewport at the
  same live resolution; only the tile size differs by band). Out-of-range
  neighbour bands are skipped. The set is **centre-first** and truncated to
  `CrossBandPrewarmMaxTiles` (24), so the most-central — most-likely-next-zoom —
  tiles win under the cap (the band + 1 footprint alone is ~4× the visible tile
  count).
- **Idle gate** — populated only on a frame with **no cold visible misses**
  (`coldExposure == 0` — no cold visible tile this frame, whether still pending
  *or* already handed to a worker), so it never competes with an on-screen fill.
  This is deliberately stricter than a `PendingVisible` empty test: `PendingVisible`
  excludes cold tiles already in flight, so gating on it would let pre-warm start
  while the viewport still had cold holes mid-raster and steal a worker from the
  fill. Also gated on the hot cache being below `CrossBandPrewarmHeadroomFraction`
  (0.75) of its byte budget, so its speculative inserts cannot evict the current
  working set. Visible target-band tiles are additionally pinned
  (`TileCache.Protect`) so they are never evicted regardless; the headroom guard
  protects the same-band predicted and fallback tiles.
- **Priority** — drained at the lowest worker tier, strictly behind
  `PendingVisible` and `PendingPredicted` (D.2). Even though it is enqueued in the
  same frame as the same-band warm set, it only rasterises once that has drained.
- **Redraw-safe** — its tiles are treated as predictions: they never trigger a
  redraw (so a published adjacent-band tile cannot start a repaint loop) and are
  cancelled (rebuilt) every frame. A later zoom that makes one visible scores an
  ordinary prediction hit.

Cross-band pre-warm is a first-class A/B knob (`S100_VECTOR_TILE_XBAND`,
`CrossBandPrewarmEnabled`), default on except off by default on the `LowEnd` tier
(an explicit opt-in is still honoured there). Its
tiles flow through the existing prediction telemetry
(`s100.render.tile.prediction.rasterized` / `.hits`), so a zoom-transition A/B
reads time-to-fill at the new band from the cold-latency histogram and the
prediction-hit counter.

**A/B result (reference cell `101GB00510210.000`, UKHO S-101 trial set, Apple
Silicon).** Scripted per-transition harness: fresh dataset load (hot cache
empty, warm disk cache disabled to remove the cross-run confound), settle at the
source band, dwell idle ~6 s so the pre-warm workers can drain, then zoom one
band and measure time-to-idle (`await_render_idle` `waitedMs`, 150 ms quiet
period) at the new band. `S100_VECTOR_TILE_XBAND=0` (OFF) vs `=1` (ON):

| Zoom transition | OFF `waitedMs` | ON `waitedMs` | Δ |
|---|---|---|---|
| 15 → 16 | 729 | 345 | **−53 %** |
| 15 → 16 (repeat) | 716 | 370 | **−48 %** |
| 16 → 17 | 641 | 611 | −5 % |
| 16 → 17 (repeat) | 638 | 601 | −6 % |
| 17 → 18 | 766 | 749 | −2 % |
| 17 → 18 (repeat) | 754 | 427 | **−43 %** |
| **Total** | **4245** | **3102** | **−27 %** |

The largest, most reliable win is the fully-warmed transition (15 → 16: ~52 %
faster time-to-fill, reproducible). Heavier band + 1 footprints (17 → 18) need a
longer idle dwell to fully warm under the 24-tile cap + lowest-priority drain —
the first 17 → 18 trial had only started warming (−2 %), the repeat with more
accrued idle time reached −43 %. **No transition regressed** in either arm
(every Δ ≤ 0), confirming the idle gate keeps pre-warm off the on-screen fill
path. Pre-warm cannot alter draw output — it only pre-rasterises tiles that a
later zoom would draw identically — so visual parity is exact.

### D.6 Metatile raster jobs (issue #427)

Adjacent cold misses may be processed as a fixed, aligned 2×2 raster batch.
The nearest pending tile remains the seed, and peers are claimed only from the
same visible, predicted, or cross-band tier. One union viewport is rendered
with a single outer gutter and sliced into independent core-plus-gutter images;
cache keys, disk entries, cold timestamps, prediction state, eviction, and GPU
residency remain logical-tile granular.

SCAMIN remains exact despite latitude-dependent row denominators. Candidate
operations are evaluated at each claimed row's denominator; a job splits into
2×1 rows or singles when any visibility result differs. Oversized temporary
surfaces and batches reduced to one miss by disk hits also fall back to the
existing single-tile path. Fractional device scales also fall back when integer
output dimensions cannot preserve the exact independent-tile world-to-pixel
ratio. Visible slices publish together and request one redraw, while speculative
slices do not redraw.

`S100_VECTOR_TILE_METATILE` / `TileMetatileEnabled` is an opt-in A/B knob and
defaults off pending performance results. Telemetry separates union raster
time, slicing overhead, achieved tiles per job, completed jobs, and fallbacks
(`reason=sparse|disk|scamin|dimension|scale`). Existing logical-tile raster
duration receives the batch elapsed time divided by produced tiles so aggregate
CPU comparisons remain meaningful. Synthetic slicing tests require exact
pixels; the opt-in real UKHO S-101 test allows at most 0.1% differing decoded
pixels with maximum channel delta 8 to accommodate subpixel antialiasing changes
from the larger viewport origin.

The shipping gate is a ≥10% median reduction in aggregate raster duration on a
dense real cell, with no material cold-latency, idle-time, paint, memory, or
fidelity regression. A neutral result is valid: the base spatial index already
removes whole-scene traversal, so the remaining opportunity is limited to
shared setup and overlapping candidate geometry.

**Preliminary smoke A/B (not a shipping decision).** One isolated-cache pair on
the dense UKHO cell `101GB00GB302045.000`, at the same 1600×1000 zoom-15
viewport with disk, prediction, and cross-band pre-warm disabled, produced:

| Measure | Off | On | Delta |
|---|---:|---:|---:|
| Logical-tile raster p50 | 30.47 ms | 19.16 ms | −37% |
| Logical-tile raster p95 | 97.13 ms | 43.50 ms | −55% |
| Cold visible-tile latency p95 | 293.5 ms | 136.5 ms | −53% |
| Time to render idle | 275.9 ms | 306.8 ms | +11% |
| Viewer paint p95 | 6.84 ms | 7.32 ms | +7% |
| Peak process working set | 697 MiB | 818 MiB | +17% |

The candidate achieved four tiles/job and median slice overhead was 5.92 ms
against 72.25 ms union-raster time (8.2%). The raster signal is promising, but
the single pair fails the idle, paint, and memory gates and is sensitive to
process/load variance. The feature therefore remains default-off until the
alternating warm-up plus ≥10-run protocol is completed.

### D.7 Open follow-ups (Phase 4+)

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

- **Label plane.** ✅ **Done (Appendix G).** Free-floating text is no longer
  rasterised into the base tiles — it escaped to the live Overlay plane with the
  symbol/sounding work (Appendix F.11), resolving the §B.3 / §C tofu-label
  band-scaling deltas (text now holds a constant on-screen size). The
  Label-plane completion adds priority-driven declutter, upright-under-rotation
  text, and per-run glyph fallback (the residual tofu cause) on that plane.
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
  as before, then rotated `θ` about the screen centre. θ is
  **derived from Mapsui's own `WorldToScreenXY`** — the projected screen
  direction of world-north is measured and compared against north-up's
  straight-up (−90°) — so it matches Mapsui's rotation sign/convention
  exactly without hardcoding it (Mapsui 5 ships only a DLL; the other
  vector renderer, `CachedVectorStyleRenderer`, already projects
  per-vertex through the same `WorldToScreenXY`). **Seam-free rotation
  (issue #330):** the backdrop + target tiles are first composited
  north-up into an **off-screen surface** (`CompositeRotated`) and then
  the *single* finished image is rotated about the screen centre — rather
  than rotating the live canvas and blitting each tile under it. Rotating
  per-tile turned every hard clip-to-core edge and the cross-band
  backdrop/target boundary into an independently-rasterised rotated seam,
  so a non-north-up zoom transition revealed banding/seams between tiles
  and bands. Compositing north-up first keeps those joins in the clean
  axis-aligned space (where they abut exactly) and carries no internal
  seam through the one rotated blit. The off-screen spans the
  `RotatedCoverSize` box at device resolution; its layout
  (`TileGrid.RotationCompositeLayout`) is pure and unit-tested
  (`TileGridTests.RotationCompositeLayout_*`). Because `DrawImage` is
  deferred until the frame flushes, the surface/image are held on
  `TileState` and freed at the next composite. If the (GPU) off-screen
  cannot be allocated the method falls back to the old rotate-canvas
  per-tile blit so the chart stays visible. North-up (the common case) is
  unchanged: tiles are blitted straight onto the canvas with no off-screen.
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

### F.10 Shutdown teardown race (worker rasterising into a dying Skia)

A second, distinct teardown crash (separate from F.5's finalizer-thread GPU
free) appeared on process **exit** — most reliably on a fast headless quit
(historically the removed `--exit-after-screenshot` one-shot path; capture is
now MCP-driven), but latent on any quit. The macOS report showed
`SIGSEGV` with the **main thread** in `exit()` → C++ `__cxa_finalize` tearing
down `libSkiaSharp`, while a background **tile worker** (.NET TP thread) was
mid-rasterise inside Skia (`sk_typeface_create_from_name`). The worker
dereferenced Skia globals the finalizer had already freed.

The tiled renderer's workers are per-layer `Task.Run(Worker)` loops with no
global stop, so nothing held the process back from tearing down Skia while a
worker still ran. Fix: a process-wide one-way drain gate
(`WorkerDrainGate`, exposed as `S100VectorTileRenderer.ShutdownAndDrain`). The
host calls it on `IClassicDesktopStyleApplicationLifetime.ShutdownRequested`
(which Avalonia raises on every exit path — explicit `Shutdown()`,
last-window-close, OS quit), so it covers a headless/scripted quit and a
normal quit alike. The gate sets a permanent draining flag and blocks (bounded, 5 s)
until in-flight workers finish. Every worker `TryRegister`s before starting and
`Complete`s in a `finally`; a worker refused at register time (or one that sees
the flag at the top of its loop) returns **before any Skia call**. The
invariant: no Skia call happens after the drain wait returns except for an
already-registered worker the wait explicitly awaits.

The gate's start/drain/complete races are unit-covered
(`WorkerDrainGateTests`, 5 cases) since they are pure synchronisation with no
GPU/window dependency; the end-to-end clean-exit on a headless quit
is environment-bound (it needs a window-server-attached GUI session to render
tiles first) and so is verified live rather than headlessly.

### F.11 Screen-space symbol/sounding overlay (constant-size point features)

**Symptom.** In the tiled "B" arm, point symbols (buoys, rocks, obstructions)
and soundings grew during a zoom gesture, then **shrank smaller and smaller**
as the user zoomed in and settled — the opposite of the S-100 requirement that
point symbols hold a constant on-screen size.

**Cause.** Base tiles are rasterized at a discrete quad-tree band resolution
(`TileGrid.ResolutionForBand(band)` = `Band0Resolution / 2^band`) and composited
into the live viewport scaled by `ResolutionForBand(band)/resolution = 2^δ`,
δ∈[−0.5,+0.5]. Anything **baked into a tile** therefore scales 0.707–1.414×
within a band, snaps 2× at band boundaries, and shows a transient ~2× while a
coarser fallback band is the only cached backdrop. Area fills and lines *should*
scale with the map; point symbols and soundings must not. You cannot
counter-scale a baked symbol because the composite scale varies continuously
with live resolution while a tile is rasterized once per band.

**Fix.** Partition the `VectorScene` at bind time
(`S100VectorTileRenderer.PartitionScene`): `PointPaintOp` and `TextPaintOp`
route to an **overlay** scene; everything else (`AreaPaintOp`,
`PatternAreaPaintOp`, `LinePaintOp`) stays in the **base** scene that feeds the
tile pyramid. Each frame, after compositing the base tiles, `DrawOverlay`
builds a live full-screen DIP-space viewport and draws the overlay through the
shared display-list executor's new `SkiaDisplayListRenderer.RenderOnto(canvas,
scene, viewport)` (the op-dispatch loop without bitmap allocation, clear, or
flush — flushing the foreground canvas mid-composite could prematurely flush
deferred tile `DrawImage`s and interfere with GPU residency). Because the
overlay draws against the real viewport every frame, symbol px sizes
(`symbol.Scale`, fallback-dot radius, `FontSizePx` — all already in logical
display px per the `PaintOp` unit contract) are constant on screen regardless
of zoom. Under rotation the overlay is rotated about the screen centre to match
how `Composite` rotates tiles, so anchors stay aligned. Overlay z-order is
strictly above all base tiles, matching S-100 draw order (symbols/text on top).

**Cache invalidation.** `TileDiskCache.FormatVersion` was bumped `1 → 2` so v1
tiles (which had symbols/soundings baked in) are never reused alongside the
overlay — reusing them would double-draw every symbol. The in-memory hot cache
is per-layer and cleared on `BindScene`, so it needs no separate invalidation.

**Tests.** `SymbolOverlayTests` (Pipelines.Tests):
`PartitionScene` routes points/text to the overlay and keeps fills/lines/
patterns in the base, order preserved (and an empty-overlay case); and
`RenderOnto` draws a point at an **identical pixel footprint** across two
viewports differing 4× in world span but sharing pixel dimensions — proving the
constant on-screen size the overlay exists to guarantee. Both are pure,
machine-independent Skia rasters.

### F.12 Failure-mode → regression-test traceability

Every native/threading defect found by manually driving "B" (this appendix)
is now pinned by a deterministic, headless regression test, so it cannot
silently return once "B" is the default everyone runs (issue #347, Stability ▸
"Regression tests for each known failure mode"). The GPU/context-bound paths
that cannot run headlessly are exercised with CPU-backed Skia surfaces and a
sentinel context — the registry lifecycle is identical for CPU- and GPU-backed
resources — and backed by the live integration runs recorded above.

| # | Failure mode (symptom) | Regression test(s) | Project |
|---|---|---|---|
| §F.5 | Teardown finalizer-thread GPU free on close-all + reopen | `GpuRegistryTeardownTests.ReconcileGpuCaches_OwningLayerCollected_*`, `TileCacheTests.Clear_DisposesAndEmptiesCache` / `Put_AfterDispose_*` | Pipelines.Tests |
| §F.6 | Zoom-out GPU use-after-free (mid-frame eviction) | `TileCacheTests.DeferDisposal_*`, `DrainPendingDisposals_*` | Pipelines.Tests |
| §F.7 | Prediction-driven repaint loop ("never settles") | `TilePredictionTests.ShouldRequestRedraw_RepaintsOnlyForVisiblePublishedTiles` | Pipelines.Tests |
| §F.8 | Rotation-induced blanking / one-scale backdrop | `TileGridTests.RotatedCoverSize_*`, `RotationCompositeLayout_*`, `VisibleTiles_RotatedViewport_FillsExtentThatRawSizeMisses`; `TilePredictionTests.PredictedTiles_RotatedViewport_WarmFrameExpandsWithCoverSize` | Pipelines.Tests |
| §F.9 | Palette-insensitive `styleStateHash` (Night served Day) | `StyleStatePaletteFingerprintTests` (4 cases) | Pipelines.Tests |
| §F.10 | Shutdown teardown race (worker into a dying Skia) | `WorkerDrainGateTests` (5 cases) | Pipelines.Tests |
| #345 | B→A switch crash (abandoned-layer GPU free off-thread) | `GpuRegistryTeardownTests.ReconcileGpuCaches_SwitchBToA_FreesAbandonedBLayerButKeepsSurvivingLayer` (+ the `*_LiveLayer_*` / `*_DeadLayerDifferentContext_*` cases) | Pipelines.Tests |

The §F.8 prediction-under-rotation cases were added once rotation became
scriptable end-to-end: the `set_render_subsystem` MCP tool (the last missing
soak affordance — rotation was already driveable via `set_viewport {rotation}`)
lets a scripted soak exercise the A↔B switch and rotated viewports without the
GUI. The two new tests pin the renderer's `RotatedCoverSize → VisibleTiles` /
`PredictedTiles` composition (`S100VectorTileRenderer.Render`), so a revert to
selecting tiles against the raw north-up DIP size — the shape of the §F.8 bug —
fails CI.

### F.13 Measurable exit criteria for defaulting "B"

The flip of the default to `RenderSubsystemKind.TiledScene` is gated on three
**measurable** bars (issue #347 ▸ "Define measurable exit criteria"). All three
are now met: fidelity and stability via committed gates, and **performance** via
the A↔B telemetry capture recorded below. The flip itself (`SeedRenderSubsystem()`
empty-value branch + enum default in `RenderingOptimizations.cs`) remains a
separate, deliberately deferred step that keeps "A" selectable as a fallback for
at least one release.

| Bar | Criterion | Gate / evidence | Status |
|---|---|---|---|
| **Fidelity** | "B" ≥ "A" on the golden-image set (S-101 + every non-S-101 product), with coverage rasters near-pixel-identical and declutter/label survival unchanged. | `RenderParityTests` (S-101) + `MultiProductParityTests` (coverage exact-match, per-product B-arm goldens, label preservation) — committed CI gates (#360). | ✅ |
| **Performance** | A bounded on-screen frame-time budget on the pan/zoom/rotate gesture script for a dense cell **plus** a bounded async tile-build latency for "B". | **Met.** Two complementary measurements on the GB Solent `101GB00302045` cell with an identical (seeded) smooth pan/zoom/rotate gesture burst per arm, dataset-load asserted (`count=1`, `loadDurationMs`≈6.2 s) and a non-zero draw count asserted (A: 1817 draws, B: 90 composite draws). **(1) UI-thread on-screen paint** (`get_render_stats` `window`, the apples-to-apples *responsiveness* number): **B** mean **6.0 ms** / p95 **21.6 ms** / max 125 ms at **90 draws**, vs **A** mean **13.1 ms** / p95 **57.1 ms** / max 227 ms at **1817 draws** — "B" keeps the UI thread ≈2× cheaper on both mean and p95 by compositing tiles instead of re-iterating every feature. **(2) "B"'s off-thread cost** (`EncDotNet.S100.Renderers.Mapsui` `Meter` via `dotnet-counters`, typical = median 1-s window / worst = max window): `tile.rasterize.duration` typ **1.6 ms** / worst-window p95 55.5 ms; `tile.composite.duration` typ 0.2 ms / worst-window p95 67 ms; `tile.cold.exposure` typ ~17 tiles / worst ~46 — i.e. "B" trades a bounded async build latency and brief cold-tile exposure during rapid panning for the UI-thread win. Numbers reproduced across two runs (±0.5 ms). | ✅ |
| **Stability** | Zero crashes / blanks / paint-fault growth across an ≥ M-minute multi-product soak that exercises the A↔B switch in both directions, palette/category churn, rotation, and dataset open/close GC; and every known failure mode (§F.5–§F.10, #345) maps to a passing regression test. | A 10-minute GPU/Metal soak (real S-101 GB cell — real `loadDurationMs`≈6.4 s confirming S-101 portrayal, not a base-map-only frame — + synthetic S-124/125/127/131/201 GML churn): **863 steps, 149 A↔B switches, 0 paint faults, 0 render bails, 0 never-settles, 0 blanks, process alive, no crash-log, no native/Skia fatal frame.** Failure-mode coverage: traceability matrix §F.12. | ✅ |

The stability figures are the output of a scripted MCP soak (the
`set_render_subsystem` tool added for this work makes the A↔B switch driveable
from outside the GUI; rotation rides on `set_viewport {rotation}`). The harness
is a session-only evaluation artefact and is intentionally **not** committed —
only the deterministic unit regressions (§F.12) are CI gates; the soak is the
developer/manual confidence gate, reproducible on demand.

> **Measurement caveat (learned the hard way).** Three traps invalidate a naïve
> perf number here: (1) `open_dataset` returns `map_not_ready` during the
> viewer's cold layout, so a harness must **retry until the dataset actually
> loads** — otherwise the gesture burst measures the OSM base map, not the
> chart; (2) because "B" renders off the UI thread, `get_render_stats`'
> per-style/`window` figures capture "B"'s *UI-thread* cost but **not** its
> off-thread tile-build cost — the two arms are only comparable on the
> UI-thread responsiveness number, with "B"'s async build cost read separately
> from the `EncDotNet.S100.Renderers.Mapsui` `Meter`; and (3) `frame.duration`
> and `layer.getfeatures.duration` fire on **layer rebuild**, not per paint, so
> they are silent during a pan/zoom burst — use `get_render_stats` for the
> per-frame UI cost and the `tile.*` histograms for "B"'s worker cost. Any
> committed performance claim must assert a non-zero `VectorStyle`/tile draw
> count for the arm under test before trusting the timings.

---

## Appendix G — Phase 4 Label-plane completion (declutter, upright text, glyph fallback)

**Context.** #347's "Extract the Label plane" item was written when free-floating
text was assumed to be baked into the base tiles and therefore scaling with the
resolution band. By the time this work started, that was **already false**: the
screen-space overlay (Appendix F.11) routes **every** `TextPaintOp` (alongside
`PointPaintOp`) to the live Overlay plane via `S100VectorTileRenderer.PartitionScene`,
drawn each frame against the real viewport, so labels already held a constant
on-screen size and `TileDiskCache.FormatVersion` was already at `2` (no baked
text). There is exactly one text op type and no text anchored into base tiles,
so the "labels not free-floating" sub-item is empty. The genuine remaining
Label-plane gaps — all on the live plane — were declutter, rotation-correct
uprightness, and missing-glyph fallback.

> **Terminology.** "S-52 declutter" in #347 and earlier appendices is shorthand.
> S-52 is the S-57/ECDIS Presentation Library; S-100 replaces it with Part 9 +
> the product Portrayal Catalogue. Part 9 makes overlap avoidance the **portrayal
> engine's** responsibility (no per-label collision rule ships in the catalogue),
> so the declutter is implemented here in the renderer, driven by the
> drawing-priority order already carried on each `PaintOp`.

### G.1 Deterministic, priority-driven declutter

`LabelDeclutterer` (`EncDotNet.S100.Renderers.Skia/Scene/`) is a pure,
Skia-measure-only pass run each frame over the Overlay scene **before** drawing:

- **Footprints (final screen space).** Each `TextPaintOp` projects to its screen
  anchor + alignment + px offset + measured text bounds + a small pad → an AABB.
  Each `PointPaintOp` → an AABB from the symbol `CullRect`×`Scale` (or fallback-dot
  radius). Determinism is a **feature** (Appendix B.3 — do not chase A's
  nondeterministic placement).
- **Order = priority.** `VectorScene.Ops` is in ascending Part 9 drawing
  priority (later = on top). Point symbols reserve their footprints **first**
  (S-100 draw order: symbols/text on top, and symbols win over text); then text
  is processed **highest-priority-first** (reverse op order). A label whose
  footprint collides with an already-reserved footprint is **suppressed**.
  Survivors are returned as a suppression set and drawn in original order to
  preserve z-order.
- **Fullest fidelity.** Soundings (a `TextPaintOp`) participate as both occupant
  and obstacle, and labels avoid **symbol** footprints too.
- **O(n) index, alloc-lean hot path.** A uniform screen-bucket grid
  (`ScreenRectIndex`, 64 px cells) keeps collision queries near-O(1) for the
  thousands of ops/frame, and — because a suppressed label is **never** inserted
  — each cell holds only mutually-non-overlapping placed rects (area-bounded), so
  clustering does **not** degrade toward O(n²). The pass walks the whole-cell
  `OverlayScene` once, culling each op inline (`cullBounds.Contains`) before any
  layout/index work, so off-screen labels never enter the placement set. The
  grid's `Add` inlines its cell loop (no per-footprint capturing-lambda
  allocation). See G.6 for the per-frame allocation profile and the remaining
  `OverlayScene` viewport-scoping opportunity (#332).

### G.2 Upright text under rotation

The old overlay rotated the whole canvas about screen-centre (F.11), which
rotated glyphs too. `DrawOverlay` now splits the pass:

- **Point pass** — unchanged: drawn under the rotated canvas (symbols rotate
  with the chart, a deliberate scope guard).
- **Text pass** — drawn on the **unrotated** canvas; each anchor is rotated in
  code about the screen centre (`RotateAbout`, matching `SKCanvas.RotateDegrees`
  sense) so the label stays pinned to its feature, while the glyph baseline stays
  **horizontal** (upright). `RenderOnto`/`DrawText` gained an optional screen-space
  anchor rotation (`OverlayDrawOptions.TextAnchorRotationDegrees` + screen
  centre) that moves the projected anchor but never the glyph orientation.

Declutter footprints use the **post-rotation** anchors so collision is computed
in true on-screen space. North-up (the v1 default) is the rotation==0 no-op path
— a single `RenderOnto` with the suppression set.

### G.3 Tofu / missing-glyph fallback

`canvas.DrawText(string, …)` emits `.notdef` boxes for any codepoint the chosen
face lacks (no shaping/fallback) — the residual "tofu-label" cause from §B.3/§C.3.
`DrawText` now keeps the fast path when the primary face has all glyphs;
otherwise `SegmentRuns` splits the string into runs
by resolved face and `DrawRunsWithFallback` draws each run with the primary face
or a `SKFontManager.MatchCharacter`-resolved fallback, advancing the pen by the
measured run width. The all-glyphs probe (`Typeface.ContainsGlyphs`, which
allocates a `ushort[]` and runs a full glyph-mapping pass) is **memoised
per (face, text)** in a process-wide cache, so a stable frame re-scans nothing on
the common all-ASCII path. Resolved fallback faces and their fonts are likewise
held in **process-wide, app-lifetime** caches (keyed by codepoint and by
(face, size)) — `SKFontManager.MatchCharacter` is an expensive platform
font-enumeration and must not re-run per frame. `SegmentRuns` is codepoint-aware
(handles surrogate pairs) and pure for deterministic testing.

### G.4 Where it plugs in

- `LabelDeclutterer` + `OverlayDrawOptions` — new in `Renderers.Skia/Scene/`.
- `SkiaDisplayListRenderer.RenderOnto(canvas, scene, viewport, OverlayDrawOptions)`
  — new overload accepting the suppression set, screen anchor-rotation, and
  point/text draw filters; `DrawText` gained per-run font fallback.
- `S100VectorTileRenderer.DrawOverlay` — runs declutter over `state.OverlayScene`
  each frame, then composes the north-up single pass or the rotated point-pass +
  upright-text-pass.
- **No `TileDiskCache.FormatVersion` bump** — the overlay is live, not baked, so
  no persisted tile is affected.

### G.5 Verification

- **Pure (Pipelines.Tests).** `LabelDeclutterTests` — non-overlapping labels both
  survive; overlapping suppresses the lower priority; label yields to symbol;
  soundings participate; stable across runs. `LabelPlaneTextTests` — upright
  anchor-rotation yields an axis-aligned (wider-than-tall) opaque AABB vs a
  canvas-rotated control (taller-than-wide), and `SegmentRuns` font-fallback
  segmentation (ASCII single run, missing-glyph split, surrogate pairs).
- **Integrated (VisualRegression.Tests).** `LabelOverlayCompositionTests`
  composes the exact `DrawOverlay` north-up pieces (declutter → `RenderOnto`
  with the suppression set) and asserts decluttered label count < raw, a separate
  symbol+label both render, and a label sharing a symbol's anchor is suppressed
  while the symbol survives. Machine-independent; no committed golden binary.
- **Viewer (executed, north-up).** End-to-end confirmation in the tiled "B"
  subsystem (`S100_RENDER_SUBSYSTEM=TiledScene`, Metal GPU surface) on cell
  `101GB0050242H.000` (Portsmouth Harbour): place names, soundings, and light
  characters render crisp and horizontal with **no `.notdef` tofu** and point
  symbols drawn on top; label/sounding glyph heights are **identical** at zoom
  15 vs 16 while the base chart scales (constant on-screen size). An A/B capture
  at the same frame shows subsystem A omitting all point symbols/soundings/labels
  (the §B.3 A-side point-feature bug), so B is strictly more faithful on labels.
  Rotation is a UI gesture with no CLI/MCP control, so upright-under-rotation is
  pinned by the unit tests above. The full real-cell labels+symbols
  **golden-image set** remains its own #347 item.

### G.6 Per-frame hot-path cost (#332)

The Overlay/declutter pass runs every frame, so its allocation and CPU profile is
part of the #332 "overlay cost under the *All* category on dense cells" budget.

- **Complexity.** Was O(N_overlay) per frame over the cell's whole point+text op
  count; **now O(N_visible)** via the overlay spatial index (Appendix H). Collision
  queries are near-O(1) via the 64 px bucket grid and stay bounded under clustering
  (suppressed labels are never indexed).
- **Glyph fast path is alloc-free.** The per-(face,text) coverage memo means the
  dominant all-ASCII soundings/labels draw without the `ContainsGlyphs`
  `ushort[]` allocation or a doubled glyph-mapping pass; fallback faces/fonts are
  resolved once for the process, not per frame.
- **Declutter `Add` is closure-free.** The bucket-grid insert inlines its cell
  loop, so placing a footprint allocates nothing beyond the (amortised) cell
  list growth.
- **All three deferred levers are now shipped (Appendix H):** (a) the per-frame
  `HashSet`/`ScreenRectIndex`/`TextDrawScratch` are **pooled** as render-thread
  reusable buffers (`Clear()` not realloc); (b) the overlay walk is **viewport
  scoped** to O(N_visible) via `OverlaySpatialIndex`, preserving the rotated
  footprint and draw order; (c) upright point symbols draw from a **rasterised
  sprite atlas** instead of replaying their vector picture every frame. The
  fourth floated idea — runtime LOD-thinning of dense soundings/symbols — is
  **rejected on safety grounds** (Appendix H.0): it omits data and is not an
  ECDIS action, so it is out of scope.


## Appendix H — Phase 6: Overlay density (#332)

The first unchecked **Fidelity** item in #347 (make tiled "B" the default).
Pan/zoom under the *All* display category lagged on dense cells because the
screen-space symbol/sounding overlay is redrawn live every frame and its op set
was the **whole cell**, walked O(N_cell) (up to 3× under rotation) with every
visible symbol replayed as a vector picture. The fix is three **fidelity-neutral**
levers — the identical scene rendered faster, no feature dropped.

### H.0 Fidelity-preservation principle (binding on all optimisation work)

**An optimisation must never alter what the chart presents — in particular it
must never omit, drop, thin, merge, or hide source data — unless either (1) the
user has explicitly turned that data/category off (a display category, a layer
toggle, a declutter/own-ship setting), or (2) it is a standard ECDIS-style
decluttering action defined by the spec/portrayal (e.g. SCAMIN minimum-display-
scale culling, text-label overlap declutter).**

Pure speed-ups that render the **identical** scene faster — caching, batching,
atlasing, viewport-scoping the *walk*, buffer pooling, GPU residency — are always
fair game. Dropping hazards/soundings to save frame time is **not**: it is a
safety-of-navigation regression dressed up as performance.

Direct consequence for #332: runtime **LOD-thinning of dense soundings/symbols is
out of scope**. The sanctioned density controls are **SCAMIN** (Part 9 §11.1,
encoded per-feature by the producing HO, already applied per-op via
`ScaleVisibility`) and **text-label declutter** (already shipped, Appendix G).
If a small-scale view is still over-dense, that is a producer-side SCAMIN
encoding matter or an overscale indication — not the renderer dropping hazards.

### H.1 Measured baseline (the data drives the priorities)

Two sources. (1) **Live** instrumentation of `DrawOverlay` on real UKHO trial
cells (Release, Metal, warm tiles): the densest *available* cell
(`101GB00510210`, z13 *All*) carries **1 802 point ops, 0 sounding text** and the
overlay costs **declutter 0.6 ms + draw ~3 ms**; Portsmouth `101GB0050242H` is
365 pt / ≤1 ms. The earlier "~30 ms" figure was **cold tile-generation**, not the
overlay. **The available trial cells do not reach the #332 regime**, so they
cannot rank the levers. (2) **Synthetic** micro-measurement of
`Declutter` + `RenderOnto` over N symbol+text ops in a 1600×1000 viewport
(Release, Apple Silicon, 20-iter mean):

| N visible | declutter | draw (all visible) | draw (1 % visible) | declutter alloc/frame |
|---|---|---|---|---|
| 1 000 | 0.9 ms | 4.1 ms | 0.2 ms | 180 KB |
| 5 000 | 1.6 ms | 20 ms | 0.5 ms | 770 KB |
| 10 000 | 5.9 ms | **48 ms** | 1.0 ms | 1.5 MB |
| 25 000 | 10 ms | 105 ms | 2.2 ms | 3.1 MB |
| 50 000 | 16 ms | **202 ms** | 4.1 ms | 6.3 MB |

Reading: native **draw of *visible* point ops is the steep ~4 µs/op curve** (the
vector `DrawPicture` replay) and dominates when a dense cell is zoomed so most ops
are on-screen (where culling can't help) → **atlas is the primary lever**. The
per-op cull already keeps *drawing* cheap when ops are off-screen (≤4 ms at 50k,
1 % visible), but `Declutter` still walks **all N** (16 ms + 6.3 MB at 50k) →
**scoping** bounds that to O(visible) and **pooling** removes the per-frame
allocation.

### H.2 The three levers (all fidelity-neutral)

- **(c2) Symbol sprite atlas — `SkiaDisplayListRenderer` (primary).** Each unique
  `(processed SVG, symbol scale, device scale)` is rasterised **once** to an
  `SKImage` at device resolution (never-evicted, process-wide cache, immutable
  CPU image so cross-thread safe). Upright point ops then blit the sprite 1:1
  through the HiDPI matrix instead of replaying the vector picture. Pivot
  placement reuses the exact `DrawPicture` pivot math; **per-op-rotated** symbols
  (oriented lights/secondary symbols) and over-large/degenerate symbols fall back
  to the vector path. A `UseSymbolAtlas` toggle (default on) drives pixel-parity
  tests. Verified pixel-identical at device scale 1×/2× with offset pivots.
- **(b) Viewport scoping — `OverlaySpatialIndex` (`Renderers.Mapsui`).** A uniform
  grid over the overlay anchors (EPSG:3857) built **once** at scene-bind (off the
  per-frame path, on `TileState`). Each frame `DrawOverlay` queries the **world
  preimage of the (rotated, margin-inflated) viewport footprint** and feeds the
  scoped candidate set to declutter + all draw passes. The query is a deliberate
  **conservative superset** returned in original op order (z-order/priority
  preserved); the precise per-op screen cull still runs downstream, so the
  suppression set and the drawn set are **identical** to the whole-cell walk —
  scoping bounds the walk, it never drops a feature. Reuses caller-owned scratch
  buffers for zero steady-state allocation.
  - *Correctness edges:* (i) inflate the world query by the largest op pixel
    offset (the cull tests anchor+offset, the index keys the anchor); (ii) under
    rotation use the rotated footprint, never the axis-aligned screen rect (it is
    larger, and covers the point pass plus declutter's in-code anchor rotation
    plus the upright-text pass); (iii) each op anchors at a single world point so
    it sits in exactly one cell — no duplicates, just sort indices.
- **(a) Declutter/overlay pooling — `LabelDeclutterer` (render-thread-confined).**
  The `suppressed` `HashSet`, the `ScreenRectIndex` (+ its per-bucket lists), and
  the `TextDrawScratch` are reused across frames (`Clear()` not realloc; scratch
  held and disposed on teardown, not per frame). Confined to the render-thread
  overlay path — the worker tile-raster path keeps its own per-call scratch; no
  shared cross-thread mutable state. `Clear()` yields byte-identical output to a
  fresh buffer (determinism preserved).

### H.3 Verification

- **Unit (`Pipelines.Tests`):** `OverlaySpatialIndexTests` (query returns exactly
  the in-bounds ops in original order; whole-extent query reproduces the full op
  list; offset/rotated/degenerate edges); `SymbolAtlasParityTests` (atlas vs
  vector pixel parity at 1×/2× with offset pivots); `LabelDeclutterPoolingTests`
  (reused buffer ≡ fresh buffer, zero steady-state allocation); and a committed
  micro-measurement `OverlayScopingBenchTests` (40k-op cell → candidate set O(
  visible), viewport-bounded under pan, zero steady-state query allocation) as
  the regression guard for the synthetic curve above.
- **Live:** the available trial cells (≤1 802 overlay ops, ≤3 ms) do not stress
  the regime, so the synthetic microbench is the primary quantitative evidence;
  the live path confirms no visual regression. (When driving the viewer headless
  for this, note Avalonia.Native needs an active display surface — a render-timer
  start failure (`-6661`) means no Metal surface is bound to the shell session.)

### H.4 Exit criterion (feeds #347)

On a genuinely dense *All* cell, a warm-tile tiny-pan burst should hold the
overlay's per-frame declutter+draw within frame budget at the worst (zoomed-out,
whole-dense-cell) scale **without dropping any feature**. If atlas + scoping +
pooling do not reach budget, the next step is **not** to thin hazards — it is to
confirm SCAMIN is encoded/honoured as intended and, if needed, surface an
overscale indication.


## Appendix I — Phase 7: Cold tile-generation (base-plane spatial index, #332 / #347)

A new **perf** line under #347 (make tiled "B" the default) — **not** a Fidelity
item (Appendix H.0 is untouched: this renders the *identical* base plane,
pixel-for-pixel, only faster). It addresses the lag the original #332 report
actually felt: Appendix H.1 established that the "~30 ms" figure was **cold
tile-generation**, not the live overlay (the overlay on the densest available
trial cell was already declutter 0.6 ms + draw ~3 ms). This phase attacks that
cold cost.

### I.1 Measured cold-tile breakdown

Dense Solent/St Peter Port cell `101GB00510210.000` (the same cell named in
Appendix H.1), Release, Apple Silicon, 768×768 px tiles (256 dip + 64 gutter @
deviceScale 2). The bound **base plane** = 10,398 ops (2,690 area + 2,907
pattern + 4,801 line), **1.37 M vertices**, cell span 55 × 25 km. Cold cost is
**~85–100 ms per fresh tile, roughly flat across zoom** — so a fresh pan band of
several tiles costs hundreds of ms.

The GUI viewer could not bind a Metal surface in the measurement shell
(`Avalonia.Native … RenderTimer … -6661`, as in Appendix H.1), so the breakdown
was taken GUI-free with an env-gated (`S100_TILEGEN_TIMING=1`) micro-benchmark
driving the **same `VectorScene` IR and `SkiaDisplayListRenderer`** the worker
uses, over real per-tile viewports reconstructed exactly as `RasterizeTile`
builds them. Instrumentation was reverted before commit (skill hygiene rule).
A/B per cold tile, swept by zoom band:

| zoom | full scene | + per-tile op cull (index) | + cull **& generalize** | ops intersecting | verts intersecting |
|---|---|---|---|---|---|
| +1 | 82.9 ms | 60.8 ms (−27 %) | 15.4 ms (−81 %) | 13.8 % | 29 % |
| +3 | 101.8 ms | 91.2 ms (−10 %) | 42.6 ms (−58 %) | 10.0 % | 43 % |
| +5 | 88.2 ms | 76.8 ms (−13 %) | 56.0 ms (−37 %) | 2.6 % | 40 % |
| +7 | 88.3 ms | 76.9 ms (−13 %) | 73.5 ms (−17 %) | 1.3 % | 37 % |

**Dominant stage = vertex-bound rasterisation of the large *intersecting*
area/line geometry** (`DrawArea`/`DrawLine` build a full-resolution `SKPath` then
antialiased-fill it). Time tracks vertex count ~1:1 (~0.2 µs/vertex): only
1–14 % of ops intersect a tile, but they carry 29–43 % of all vertices (the big
depth/land polygons and contours). It is **not** fill-bound and **not**
pattern-bound (patterns were 7–16 % of clipped cost).

### I.2 What shipped this phase — the base-plane spatial index (fidelity-neutral)

The shipped `RasterizeTile` rasterised the **whole** base scene for **every**
tile, even though design §3.3 (line 74) specified *"query VectorScene ops
intersecting tile+gutter (spatial index over the scene)"*. That base-plane index
was never built — only the *overlay* plane got one (Appendix H.3 /
`OverlaySpatialIndex`, #351). This phase finally builds the base-plane
counterpart, `BaseSpatialIndex`:

- A uniform grid over base-op **world AABBs** (area/pattern = `WorldShell`
  bounds; line = polyline bounds), built **once** at `BindScene` and rebuilt on
  every scene generation. An op straddling cells is inserted into each; `Query`
  de-duplicates and returns matches **in ascending original op index** (S-100
  Part 9 draw/z-order). Any op whose geometry the index cannot bound becomes an
  **always-candidate** so it is never dropped.
- `RasterizeTile` queries the tile+gutter world AABB and rasterises only the
  returned ops. The query is a deliberate **conservative superset** (bbox
  intersection only) and the renderer still applies the exact per-op
  `ScaleVisibility` cull and pixel clip — so the output is **pixel-identical** to
  rasterising the whole scene. A null index (defensive) falls back to the full
  scene.

Measured payoff of the index alone: **10–27 %** faster cold tiles (27 % at the
coarse bands a fresh pan most exposes). Scoping is also the structural enabler
for the deferred generalization lever below.

### I.3 Threading / correctness

`BaseSpatialIndex` is **immutable after construction** and touches no Skia/GPU
objects — a pure-CPU transform over the immutable paint-op IR — so it is safe to
`Query` concurrently from the multiple **worker** threads that rasterise tiles,
and it is independent of the render thread's GPU-context lifetime rules (§3.7 /
Appendix F: GPU-backed `SKObject`s are created **and** freed on the render
thread). Tile rasterisation itself remains CPU/software (`SKBitmap` →
`SKImage.FromBitmap`, no `GRContext`). `Query` allocates its own result list per
call (no shared scratch), so concurrent worker queries never race.

### I.4 Deferred — Lever A: band-keyed geometry generalization

The data's biggest lever is **resolution-aware geometry generalization**
(Douglas–Peucker at ~½ device-pixel, keyed per zoom band): 81 % / 58 % / 37 %
cold-tile reductions at zoom +1 / +3 / +5, tracking the vertex reduction ~1:1.
It is **deferred** by branch-owner decision because, unlike every lever shipped
so far, it is the first that **alters rendered chart geometry**, so it is gated
on:

1. a **topology-preserving** simplifier (naive per-ring DP can self-intersect /
   invalidate area fills; thick strokes need a conservative tolerance) — see the
   `chart-cartography` skill §2;
2. **golden-image sign-off** (the #347 golden-image set), to confirm the ½-px
   tolerance is sub-perceptual at the band it is keyed to; and
3. a **Metal-capable GUI session** for in-viewer A/B confirmation
   (`TileRasterizeDuration` histogram + `dotnet-trace`), unavailable in the
   profiling shell.

It remains fidelity-*bounded* (no feature dropped — unlike the LOD-thinning
ruled out in Appendix H.0), just not fidelity-*neutral*, so it ships separately.

### I.5 Exit criterion (feeds #347)

A cold pan into a fresh band on a dense *All* cell rasterises each tile by
walking only the ops that intersect it, not the whole cell, with **pixel-
identical** output to the pre-index path. Verified by `BaseSpatialIndexTests`
(conservative-superset / draw-order / de-dup / degenerate / always-candidate) and
`BaseScopingBenchTests` (an interior tile-sized query over a dense synthetic base
plane returns O(covering), not O(cell)). Absolute wall-clock reconfirmation
in-viewer is pending a Metal GUI session.

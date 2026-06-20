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
- **Phase 1 — Single-surface B, from `VectorScene`.** New subsystem renders the whole viewport (over-render margin) on a worker from the scene IR, swap-and-blit. No tiling yet. *Exit:* B matches A's fidelity; pans off the sync loop.
- **Phase 2 — Tile the base.** Pyramid, gutter/clip seams, LRU + native-byte budget, best-available compositor. *Exit:* pan frame time bounded by perimeter; p99 under budget on the gesture script.
- **Phase 3 — Prediction.** Velocity fan, z±1, fling projection, idle fill, cancellation/hysteresis. *Exit:* prediction hit-rate metric; cold-tile exposure during scripted pans ≈ 0.
- **Phase 4 — Planes + invalidation.** Live Label plane (async placement), wire Dynamic plane, disk cache, `styleStateHash` invalidation on settings/palette with visible-first re-raster. *Exit:* setting change never shows stale portrayal on visible tiles.
- **Phase 5 — GPU residency + polish.** Texture cache/atlas, anticipatory-zoom tuning, side-by-side diff mode.

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

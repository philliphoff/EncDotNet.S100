# Handoff — Mapsui rendering performance baseline (viewer-driven)

> **Read this first, local Claude.** This task started in a Claude Code
> **on-the-web** session (a cloud container). That container has **no GPU,
> no display, no .NET SDK, and no access to the user's local datasets**, so
> it could not run the viewer or capture a real GPU paint baseline. The work
> below has to run **on the user's local machine**, where the GPU, SDK, and
> the `~/Downloads/IC-ENC/AU` cells live.
>
> **Branch:** `claude/mapsui-performance-alternatives-iihbbb` (you are on it).
> **Your immediate job:** capture a *before* performance baseline by driving
> the **live viewer over its embedded MCP server** — **not** the PerfRunner
> harness (PerfRunner's warm scenarios deliberately exclude the live Skia
> raster pass, which is exactly the cost we care about).

---

## 1. Why we're here (the goal)

The user reports the Avalonia + Mapsui viewer feels laggy when panning
real-world, multi-cell S-101 ENC data. We want to improve perceived
rendering performance. Before changing anything, we must capture a
reproducible baseline so we can prove the optimization actually helps.

## 2. What's already known (don't re-derive this)

A prior instrumented review — see **`docs/design/mapsui-performance.md`** and
**`src/EncDotNet.S100.Renderers.Mapsui/README.md`** — established:

- Mapsui is GPU-accelerated and **not throttled — it is vertex-bound.**
- **~93% of every paint** is inside Mapsui's `VectorStyleRenderer`, scaling
  linearly at **~1 µs per vertex**. A 3,000-vertex coastline ≈ 2.8 ms/draw.
- Cost buckets on a real multi-S-101 session: the **100–999** and **1k–10k**
  vertex buckets together are ~92% of paint. Mean paint ≈ 98 ms (~8 fps).
- This is a property of CPU-side Skia stroke tessellation, **not** Mapsui
  framework overhead — so swapping map libraries to another managed Skia
  renderer would hit the same wall. The leverage is in **reducing vertices
  per frame** or **changing the cost model** (raster tiles / GPU vector
  tiles), not replacing Mapsui.

Already-shipped mitigations: resolution-aware **line** simplification (v1,
Douglas-Peucker, default OFF), pattern-fill clip cache (in-memory + disk),
per-palette asset cache, coverage projection-layout cache.

**Top untapped win (the work that follows this baseline):**
topology-preserving **polygon** simplification (depth areas, `M_QUAL`
coverage) extending `src/EncDotNet.S100.Renderers.Mapsui/Simplification/`,
reusing the `TopologyPreservingSimplifier` + `buffer(0)` pattern already used
in the pattern-clip path. Secondary: async/pre-warm the simplification miss
path; verify SCAMIN/scale culling is firing; add a spatial index to
`InstrumentedMemoryLayer.GetFeatures` (currently O(N) linear scan).

## 3. THE TASK — capture the baseline via the live viewer + MCP

### 3.1 Prerequisites (local machine)

- .NET SDK that builds the repo (`dotnet build` at repo root).
- The taxing dataset(s): `~/Downloads/IC-ENC/AU` (S-101 ENC cells). Confirm
  whether it's an **exchange set** (a folder with `CATALOG.XML`, or a `.zip`)
  or **loose `.000` cells** — `open_dataset` accepts either.
- Keep the **experimental simplification toggle OFF** for the baseline. It is
  off by default (`ViewerSettings.EnableGeometrySimplification`, the
  "Simplify line geometry (experimental)" checkbox). The baseline must
  reflect today's shipped default so the later polygon-simplification work
  has a clean "before" to beat.

### 3.2 Launch the viewer in agent mode with MCP + isolated settings

```sh
mkdir -p /tmp/s100-baseline
ENC_DOTNET_OTEL_CONSOLE=1 OTEL_METRIC_EXPORT_INTERVAL=2000 \
dotnet run -c Release --project src/EncDotNet.S100.Viewer -- \
  --ephemeral \
  --mcp --mcp-port-file /tmp/s100-baseline/mcp.url \
  --window-size 1600x1000 \
  --log-file /tmp/s100-baseline/viewer.log -v
```

- `--ephemeral` → throwaway settings, never written back to the real profile.
- `--mcp --mcp-port-file …` → starts the embedded MCP server on an ephemeral
  loopback port and writes the bound endpoint URI to that file once listening
  (also echoed to stdout as `[MCP] listening on …`).
- `--window-size` → fixed window so captures are reproducible across machines.
- `ENC_DOTNET_OTEL_CONSOLE=1` → streams the OTel histograms
  (`s100.map.paint.*`, `s100.simplify.*`, `s100.layer.get_features.*`) to the
  console every 2 s — a second, independent record alongside `get_render_stats`.
- **Do not** pass `--bbox`/`--center` or a dataset path here; we load and frame
  via MCP so the whole sequence is scripted and repeatable.

### 3.3 Register the viewer's MCP server with this Claude session

Read the URL from `/tmp/s100-baseline/mcp.url`, then:

```sh
claude mcp add --transport http s100viewer "$(cat /tmp/s100-baseline/mcp.url)"
```

After this, the viewer tools appear as `mcp__s100viewer__*`. The ones you need:

| Tool | Purpose | Key params |
|---|---|---|
| `open_dataset` | Load an AU cell / exchange set through the real open path | `path` (file, `CATALOG.XML` folder, or `.zip`), `spec?` |
| `set_viewport` | Drive the live navigator (pan/zoom) | bbox: `south,west,north,east` **or** centre: `centerLat,centerLon,zoom` (0–24) |
| `await_render_idle` | Block until the live map settles before measuring | `quietPeriodMs?` (def 250), `timeoutMs?` (def 5000) |
| `get_render_stats` | Cost of the **last on-screen paint** | → `frameDurationMs`, `intervalMs`, `totalDrawCalls`, `paintSequence`, per-style `{style, calls, durationMs}` |
| `set_palette` | Day/Dusk/Night (optional extra dimension) | `palette` |
| `close_all_datasets` | Reset between corpora | — |
| `render_to_image` | Optional PNG snapshot for visual confirmation | — |

> **Important:** `get_render_stats` reports the **live, on-screen** paint, not
> the offscreen `render_to_image` clone. Always call `await_render_idle`
> *before* `get_render_stats` so the number reflects a settled view, not a
> frame mid-pan.

### 3.4 The measurement loop (per viewport)

For each viewport in the plan below:

1. `set_viewport(...)`
2. `await_render_idle(quietPeriodMs=400, timeoutMs=15000)` — heavy cells can
   take seconds; give it room.
3. `get_render_stats()` → record `frameDurationMs`, `intervalMs`,
   `totalDrawCalls`, and the `VectorStyle` row's `durationMs` / `calls`.
4. Repeat the viewport **3–5×** (nudge it slightly or revisit) and keep the
   **median** — single frames are noisy.

Also snapshot the console OTel block at the end (it gives the per-vertex-bucket
`s100.map.paint.style.*` distribution and `s100.layer.get_features.visible/total`,
which `get_render_stats` doesn't break down).

### 3.5 Suggested viewport plan (make it concrete from real extents)

Use `open_dataset`'s returned bbox (and `list_datasets`) to derive real
coordinates for the loaded AU cells, then exercise the cost curve from cheap to
pathological:

1. **Whole-corpus overview** — bbox covering all loaded cells (lots of features,
   low zoom; this is where SCAMIN culling should kick in).
2. **Single-cell fit** — zoom to one dense cell's bbox.
3. **Coastline pan** — 3–4 overlapping bboxes panned along a dense
   coastline / depth-contour area at a mid zoom (the 100–999 & 1k–10k buckets).
4. **Deep zoom** — centre+zoom into the busiest sub-area (max vertices on
   screen).
5. **Palette sweep (optional)** — at viewport 3, call `set_palette` Day→Night
   to confirm re-render cost (exercises the asset/clip caches).

Record the exact bboxes/centres you used so the *after* run is identical.

### 3.6 Record the results

Save a `baseline-before.md` (and the raw `get_render_stats` JSON + the OTel
console capture) under a stable location, e.g.
`tools/EncDotNet.S100.PerfRunner/baselines/viewer-au-<shortsha>/` or a new
`docs/design/baselines/` folder. Capture, per viewport: median
`frameDurationMs`, `intervalMs`, `totalDrawCalls`, `VectorStyle` ms and % of
frame, and the dataset list + window size + commit SHA + `simplification=off`.
Commit it to this branch so the *after* run can diff against it.

### 3.7 Acceptance bar (so we know the later change worked)

Per `docs/design/mapsui-performance.md`, the polygon-simplification work should
move the heavy viewports materially (the line-simplification target was ≥50%
reduction in mean `s100.map.paint.duration` on the multi-S-101 workload, ≥95%
steady-state cache hit). Re-run **the identical** viewport plan with the
optimization on and diff median `frameDurationMs` + `VectorStyle` ms per
viewport.

## 4. Key files / pointers

- `docs/design/mapsui-performance.md` — the diagnosis + optimization plan.
- `src/EncDotNet.S100.Renderers.Mapsui/README.md` — instrument table, OTel knobs,
  existing simplification + clip-cache machinery.
- `src/EncDotNet.S100.Renderers.Mapsui/Simplification/` — where polygon
  simplification gets added (v1 is lines-only).
- `src/EncDotNet.S100.Renderers.Mapsui/InstrumentedMemoryLayer.cs` — `GetFeatures`
  seam (simplification hook + O(N) filter to spatially index later).
- `src/EncDotNet.S100.Viewer/McpTools/` — the MCP tools above
  (`SetViewportTool`, `GetRenderStatsTool`, `AwaitRenderIdleTool`,
  `OpenDatasetTool`, `RenderToImageTool`, `SetPaletteTool`).
- `src/EncDotNet.S100.Viewer/README.md` → "Automation / agent control" — the
  full CLI flag reference and agent walkthrough.
- `src/EncDotNet.S100.Viewer/Diagnostics/InstrumentedMapControl.cs` /
  `RenderActivityMonitor.cs` — what populates `get_render_stats` and the OTel
  paint instruments.

## 5. Open question to confirm with the user before optimizing

After the baseline is in hand, confirm the direction (the web session
recommended **staying on Mapsui and finishing vertex reduction** —
polygon simplification first — over swapping libraries). The full options
comparison the user already saw: finish vertex reduction (low risk) →
adaptive raster tiles (medium) → MapLibre GPU vector tiles (high effort,
breaks the project's no-native-deps promise) → custom Skia control (high
effort). Don't start a library swap without explicit sign-off.

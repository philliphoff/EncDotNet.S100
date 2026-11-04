# Map rendering architecture

This note designs how SoundCharts and the `s100` tool turn chart cells into
pixels. It starts from a clean slate, then maps the design onto the code on
`main` and lays out a staged migration. It is a proposal. Nothing here is
built yet.

It replaces the scheduling, caching and layer model in
[S-100 render subsystem design](S100-Render-Subsystem-Design.md). It keeps that
note's tile grid, scene IR, planes and most of its hard-won rules. That note's
appendices remain the record of why those rules exist.

Each choice is tagged:

- **[M]**: a measured number from the perf rounds drives it.
- **[H]**: a hypothesis. The slice that ships it must measure it first.
- **[F]**: a fact read from the code, not a measurement.

## The problem

A user jumps to a new place in a 298-cell NOAA collection. Today:

- A cold jump settles in 5–18 s. After the background pre-warm it settles in
  1.2–1.8 s, with loads arriving in waves about 300 ms apart. [M]
- Lua portrayal takes 0.2–7 s per cell. Parsing takes 10–500 ms. [M]
- Cold loads allocate about 2 GB/s. 73% of allocations come from MoonSharp and
  18% from NTS `CascadedPolygonUnion` in pattern clipping. [M]
- A spike for [#1278](https://github.com/philliphoff/EncDotNet.S100/issues/1278)
  ran the same S-101 rules on Lua-CSharp 0.5.8. Output was byte-identical, with
  about 9× fewer bytes allocated and `PortrayalMain` 2.3–2.8× faster. US3WA1EF
  went from 3.7 s and 5,027 MB to 1.3 s and 553 MB. [M]
- With workstation GC about 3 of 16 cores were busy. Server GC cut pauses from
  13.5 s to 1.1 s but holds 3–5 GB committed. [M]
- Peak working set is 7–10 GB with about 40 cells loaded: about 5.5 GB GC heap
  and 3.5 GB native tile bitmaps. [M]
- Layers of a tile slot reveal one at a time. [M, Appendix J of the old note]

Five structural causes sit under these numbers. They're read from the code.

1. **Nothing schedules across stages.** Loads, portrayal, pre-warm, tile
   raster, pattern clip and validation each have their own concurrency limit:
   `MaxConcurrency = max(2, cores/2)` for lazy loads, `MaxSpeculativeConcurrency
   = 1`, one pre-warm worker, `MaxRefreshConcurrency = max(2, cores/2)` for bulk
   refresh, per-layer tile worker floors, and `Task.Run` for validation. No
   component can say "this visible tile matters more than that pre-warm cell". [F]
2. **The unit of rendering is the Mapsui layer.** Each cell has two layers
   (areas, line work). Each layer has its own `TileState`, its own pending sets,
   its own workers, its own 256 MB tile cache and its own GPU cache. Memory and
   worker count scale with cells × layers, not with the screen. [F]
3. **One cell is one serial Lua run, on an interpreter that allocates for every
   value.** `PortrayalMain` runs every feature of a cell in one MoonSharp
   `Script` on one thread. Every Lua value is a heap `DynValue`, and every call
   allocates a `CallStackItem`. A 7 s cell holds one core for 7 s while the
   visible view waits for it, and its garbage stalls the other cores. [F]
4. **The UI thread composes.** `ComposeLayers` (stack projection, S-98 rules,
   overlap suppression with O(n²) NTS `Intersects`, layer diff) runs on the UI
   thread after every cell load: about 230 ms per 14-cell load. [M]
5. **Two compositors.** The viewer uses `S100VectorTileRenderer`. The CLI and
   headless MCP use `HeadlessCompositor` and `HeadlessTileRenderer`, which
   handle coverage overlap with bounding boxes instead of NTS. [F]

## Goals and constraints

- The visible view always comes first.
- Keep every core busy whenever there is useful work, without delaying
  visible work by more than one short job.
- Do work on demand. Do speculative work only when it can't delay visible
  work, and release it early when it's no longer likely to be needed.
- The core depends on no host. Mapsui and Avalonia sit behind an adapter.
  The CLI and MCP hosts use the same core.
- Packages are in preview, so breaking API changes are fine.
- Never copy GPL code, for example from OpenCPN.

## Design principles

1. **Everything expensive is a content-addressed asset.** A parsed cell, a
   portrayal output, a cell scene, a pattern clip and a raster tile each have a
   key that hashes their inputs. The same key always means the same bytes. This
   is what makes caching, single-flight and disk reuse safe.
2. **Demand flows down; assets flow up.** The view says which tiles it needs
   and how urgently. Each asset asks for its own inputs with the priority it
   inherited. Nothing upstream guesses what the view wants.
3. **One scheduler, short jobs.** Every stage runs on one pool of worker
   threads, through one priority queue. Long work is split into jobs short
   enough that a newly visible job never waits long for a core.
4. **Hosts only blit.** The UI and render threads read immutable snapshots and
   draw. They never parse, portray, compose, clip or rasterise.
5. **The screen, not the cell count, bounds memory.** One global budget covers
   managed and native memory. Raster tiles are composited across cells, so
   their count depends on the viewport, not on how many cells are loaded.

## Components

```mermaid
flowchart TB
  subgraph Host["Host adapter (Mapsui + Avalonia, CLI, MCP)"]
    View[View source<br/>viewport, gestures, device scale]
    Blit[Frame compositor<br/>blit + live overlay]
  end
  subgraph Core["Host-neutral engine"]
    Pred[Prediction model]
    Demand[Demand planner]
    Sched[Work scheduler]
    Budget[Memory budget]
    subgraph Assets["Asset managers"]
      Cells[Cell catalogue + parsed cells]
      Port[Portrayal outputs]
      Scenes[Cell scenes + pattern clips]
      Stack[Chart stack]
      Tiles[Chart tiles]
    end
    Disk[(Content store on disk)]
  end
  View --> Pred --> Demand
  View --> Demand
  Demand -->|tile and cell demand sets| Tiles
  Demand --> Cells
  Tiles --> Stack --> Scenes --> Port --> Cells
  Assets <--> Sched
  Assets <--> Budget
  Assets <--> Disk
  Tiles -->|frame snapshot| Blit
```

### Chart session

`ChartSession` is the engine's root object. A host creates one per map. It owns
the cell catalogue, the asset managers, the demand planner and the chart stack.
It takes a scheduler, a memory budget and a content store, which a process can
share across sessions. The CLI `tiles` command and the viewer construct it the
same way.

```csharp
public sealed class ChartSession : IAsyncDisposable
{
    public ChartSession(ChartSessionOptions options, IWorkScheduler scheduler,
        IMemoryBudget budget, IContentStore store);

    public ICellCatalogue Cells { get; }            // sources, bounds, coverage
    public ChartSettings Settings { get; set; }      // mariner, palette, display
    public ChartStackSnapshot Stack { get; }         // latest published stack

    // The host reports what it shows. Cheap; never blocks.
    public void SetView(ViewState view);

    // Latest frame snapshot for a host to draw. Never blocks, never allocates
    // per call beyond the returned struct.
    public FrameSnapshot GetFrame(ViewState view);

    // Headless hosts await a complete result instead.
    public ValueTask<ChartImage> RenderAsync(ViewState view, RenderQuality q,
        CancellationToken ct);
    public IAsyncEnumerable<ChartTile> RenderTilesAsync(TileRequest request,
        CancellationToken ct);

    public event Action<FrameChanged>? FrameChanged;  // raised off-thread
}

public readonly record struct ViewState(
    double CenterX, double CenterY,     // EPSG:3857 metres
    double Resolution,                  // metres per device-independent pixel
    double Rotation,
    int WidthDip, int HeightDip,
    float DeviceScale,
    long TimestampTicks,
    ViewMotion Motion);                 // idle, pan, fling (with target), animated (with target)
```

`ViewState` is the only thing a host must supply. `ViewMotion` carries the
destination when the host knows it: a fling's resting viewport, or the target
of an animated `CenterOnAndZoomTo`. [H: the Mapsui `Navigator` exposes enough to
fill this in; see the open questions.]

### Work scheduler

One scheduler serves every stage: parse, translate, portray, build scene,
clip, compose stack, rasterise, encode, write to disk and validate.

```csharp
public enum WorkClass
{
    Visible = 0,     // needed for the current frame
    Predicted = 1,   // likely needed within the prediction horizon
    Background = 2,  // pre-warm, idle fill, cache writes
    Validation = 3,  // never competes with drawing
}

public readonly record struct WorkPriority(WorkClass Class, double Urgency);
// Lower Urgency runs first: seconds until needed, or distance from the view centre.

public enum WorkStage { Parse, Translate, Portray, Scene, Clip, Stack, Raster, Encode, Io, Validate }

public interface IWorkScheduler
{
    // Single-flight: a second Schedule with the same key joins the first job.
    WorkHandle<T> Schedule<T>(WorkKey key, WorkStage stage, WorkPriority priority,
        Func<WorkContext, ValueTask<T>> run);

    SchedulerStats Stats { get; }  // queue depth per class and stage, busy workers, GC pause share
}

public sealed class WorkHandle<T>
{
    public Task<T> Result { get; }
    // Interest is reference counted. Priority is the best priority of all
    // current holders. When the last holder goes, the job is cancelled.
    public WorkInterest AddInterest(WorkPriority priority);
}

public sealed class WorkContext
{
    public CancellationToken Cancelled { get; }  // no one wants the result
    public bool YieldRequested { get; }          // a better job is waiting for this core
    public WorkPriority Priority { get; }        // may rise while the job runs
    // Splits work into child jobs that inherit this job's priority and token.
    public WorkHandle<T> Spawn<T>(WorkKey key, WorkStage stage,
        Func<WorkContext, ValueTask<T>> run);
}
```

Rules:

- **Worker threads.** The scheduler owns `ProcessorCount − 1` dedicated threads
  in the viewer, leaving a core for the UI and render threads, and
  `ProcessorCount` in the CLI. It doesn't run jobs on the .NET thread pool, so
  pool starvation ([#820](https://github.com/philliphoff/EncDotNet.S100/issues/820),
  [#982](https://github.com/philliphoff/EncDotNet.S100/issues/982)) can't block
  it, and its own jobs can't starve async continuations. Disk reads go
  through async IO and don't hold a worker. [F: the starvation cases; H: that
  dedicated threads remove them]
- **Strict priority, then urgency.** A free worker takes the best job across
  all stages. There are no per-stage or per-layer quotas, and no worker floors.
  Visible work can use every worker.
- **Priority inheritance.** A visible tile that needs a scene that needs a
  portrayal raises the portrayal to `Visible`. A pre-warm portrayal that a jump
  suddenly needs is promoted while it runs and doesn't restart.
- **Short jobs.** The target is that no job holds a core for more than about
  100 ms. Long stages split into child jobs: Lua portrayal by feature shard,
  pattern clipping by pattern group, scene building by instruction range, and
  tile raster is already short. A visible job then waits at most one job length
  for a core. [H: 100 ms; measure the job-length distribution]
- **Cooperative yield.** Jobs that can't split cleanly poll `YieldRequested`
  and `Cancelled`. The Lua emit hook that #1266 added for cancellation already
  gives this a per-feature check point. [F]
- **Cancellation by interest.** A job is cancelled when its last interest is
  dropped. The demand planner drops interest when it publishes a new demand set
  that no longer contains the asset. No component holds a
  `CancellationTokenSource` per load any more.
- **Allocation-aware admission.** GC pauses stop every core, so the scheduler
  caps concurrent allocation-heavy jobs (`Portray`, `Clip`) in the `Background`
  class by observed GC pause share (`GC.GetTotalPauseDuration` over a sliding
  window), not by a fixed count. Visible work is never capped. [M: GC paused
  40–60% of wall clock under workstation GC; H: that a pause-share feedback loop
  keeps throughput up without the committed-memory growth in
  [#1276](https://github.com/philliphoff/EncDotNet.S100/issues/1276)]. After the
  Lua-CSharp switch this is a backstop rather than a main control: Lua
  allocates about 9× less, and NTS pattern clipping becomes the largest
  allocator. [M: 9×; H: the new allocation split, which step 0 measures]

### Asset managers

Each asset kind has a manager with the same shape. The sketch called for
"fetch", "likely needed" and "no longer needed" per asset. This design keeps
fetch and makes the hints declarative: a producer publishes a whole demand set,
and the manager diffs it against the previous one. A key that drops out of the
set is no longer needed. No one has to remember to call "unlikely" for each
key, so missed calls can't leak work or memory.

```csharp
public interface IAssetManager<TKey, TAsset> where TKey : notnull
{
    // Returns at once if resident. Otherwise schedules the work (or joins it).
    ValueTask<AssetLease<TAsset>> FetchAsync(TKey key, WorkPriority priority,
        CancellationToken ct);

    // Never schedules work. Used by the frame path.
    bool TryGet(TKey key, out AssetLease<TAsset> lease);

    // Replaces the demand this producer placed earlier. Keys not in the new
    // set lose this producer's interest; queued work for them is cancelled
    // and resident copies become eviction candidates at once.
    void SetDemand(DemandProducer producer, IReadOnlyList<Demand<TKey>> demand);

    // A strong signal, for example a cell the user has scrolled far away from.
    void Release(TKey key);
}

public readonly record struct Demand<TKey>(TKey Key, WorkPriority Priority,
    double Probability, TimeSpan TimeToNeed);

public readonly struct AssetLease<T> : IDisposable
{
    public T Value { get; }
    // Reference counted. Eviction never frees an asset while a lease is open.
}
```

Leases fix a class of bugs the old note records: use-after-free on zoom-out
(F.6) and worker raster into a dying Skia (F.10). Native objects are freed only
when the last lease closes, and GPU objects only on the render thread.

The asset kinds, their keys and their costs:

| Asset | Key (hash of) | Cost | Where it lives |
|---|---|---|---|
| Cell catalogue entry | catalogue record | none | always resident |
| Parsed cell | file bytes, updates, translator version | 10–500 ms [M] | memory |
| Portrayal output | today's portrayal content hash + rule key | 0.2–7 s [M] | memory, disk (`.dlist`) |
| Cell scene | portrayal key, display filter, palette, symbol and text scale | unmeasured [H] | memory, disk (new) |
| Pattern clip | today's clip key | 18% of cold allocations [M] | memory, disk (`.clip`) |
| Chart stack | ordered loaded cells, S-98 rules, settings | about 230 ms per 14 cells today [M] | memory |
| Chart tile | tile slot, ordered scene keys that touch it, pixel size | unmeasured per tile [H] | memory, GPU, disk |

The portrayal key already exists and is sound: it hashes the dataset bytes, the
feature and portrayal catalogues, assembly versions and the mariner settings
that change Lua output. Display toggles re-filter that output without re-running
Lua ([#1149](https://github.com/philliphoff/EncDotNet.S100/issues/1149)). The
cell scene key keeps that rule: it hashes the filter, not the Lua inputs. [F]

### Chart tiles: one raster per slot, across all cells

This is the largest change. Today a tile slot on screen is the sum of one tile
per loaded layer that touches it, each from its own cache. In this design a
slot has one **chart tile**. It holds the base plane of every cell that touches
the slot, drawn in stack order, with finer-cell coverage clipping applied while
rasterising.

```csharp
public readonly record struct ChartTileKey(
    int Band, int X, int Y, int ScaleClass,
    int PixelSize,          // device scale is part of the key [M: 1x MCP captures polluted retina caches]
    Hash128 StackSlice);    // hash of the ordered scene keys and clip inputs for this slot only
```

What this buys:

- **Atomic reveal.** A slot changes from one complete image to another. Layers
  and cells can't flash in one at a time. `TileRevealGroup` and its 400 ms hold
  go away. [F: per-dataset groups today can't hold across cells]
- **Stale-while-revalidate.** When a cell lands, only slots whose
  `StackSlice` changes get new keys. Their old images stay on screen until the
  new ones are ready, then swap. This generalises today's predecessor stand-in
  (`TryBindFilteredScene`, `PredecessorLifetime`).
- **Memory bounded by the screen.** Resident tiles ≈ visible + predicted slots
  × a few bands, whatever the cell count. [M: 3.5 GB native today, with 80
  per-layer caches of up to 256 MB]
- **Fewer blits and uploads.** One `DrawImage` per slot instead of one per
  layer per slot. [M: warm pan frames are gated by `BlitTile` and
  `ToTextureImage`, not CPU]
- **Hidden coarse tiles cost nothing.** A coarse cell whose part of a slot one
  finer coverage hides contributes no ops to that slot's raster. The
  rule stays "one finer coverage hides it", not a union, to avoid hairlines at
  the seams. [M]
- **Content-addressed disk reuse.** The key depends only on what's in the slot,
  so a slot cached in one session is valid in the next whenever its cells and
  settings match. The CLI `tiles` command can share the store.

What it costs:

- **Re-raster on change.** A landing cell re-rasterises every slot it touches,
  including the other cells' ops there. Loads that land within one stack
  version (below) are batched into one re-raster. [H: the per-slot raster cost
  with 5–10 overlapping cells is small enough. Measure it first; see the risks.]
- **More scale classes.** A slot's `ScaleClass` thresholds are the union of its
  cells' SCAMIN values inside the band's √2 window. [H: the class count per
  slot stays small]

Raster tiles are still drawn with a gutter so lines stay continuous across
seams. But the stored image holds only the core plus a 2-pixel apron for
bilinear sampling. Today the stored image includes the full 64 DIP gutter on
each side: `(256 + 128)² / 256² = 2.25×` the core's pixels. Storing core plus
apron needs `(260/256)² ≈ 1.03×`. That cuts tile memory by about 54% before any
other change. [F: `TilePixelSize` and `BlitTile` clip to the core; H: nothing
else reads the stored gutter]

The planes from the old note stay:

| Plane | Contents | Drawn |
|---|---|---|
| Base | areas, patterns, lines | chart tiles, off-thread |
| Overlay | point symbols, soundings | live per frame, from a merged overlay index across cells |
| Label | free text, decluttered | live per frame |
| Coverage | S-102 and other gridded products | chart tiles, in stack order [H: phase 2] |
| Dynamic | AIS, own ship, route, pick | live per frame, unchanged |

The overlay and label planes stay live because point symbols must hold a
constant screen size (F.11). They draw a cell's symbols only once its base
plane shows in that slot.

### Chart stack

The chart stack is the ordered list of cell scenes for the session, with the
S-98 interoperability result and the finer-coverage relations. It's an
immutable snapshot with a version number.

- It's built as a `Stack`-stage job, never on the UI thread.
- It's incremental. A new cell computes its relations to the cells it overlaps,
  found through an R-tree of coverage envelopes: O(k) NTS tests, not the
  O(n²) `CollectFinerCoverages` loop. [F: O(n²) today; M: 230 ms per 14 cells]
- **Stack versions batch arrivals.** Cells that finish within a short window
  (start at 50 ms) go into one version. Each version triggers one round of
  slot re-rasters. This turns today's 300 ms load waves into fewer, larger
  reveals. [M: waves about 300 ms apart; H: the window]
- S-98 feature suppression (for example R-101-102-B, S-101 depth areas under
  S-102) becomes a filter on the scene's ops in the stack, not a filtered copy
  of a Mapsui layer.

### Demand planner and prediction

The demand planner turns a `ViewState` and a prediction into demand sets for
two asset kinds. Everything else follows by inheritance.

- **Tile demand** covers what draws within about a second: the visible slots,
  a halo, a fan along the pan velocity, and ±1 band around the centre.
- **Cell demand** covers what portrays within a few seconds. Lua takes up to
  7 s per cell, so cell demand must look further ahead than tiles. [M]

```csharp
public interface IPredictionModel
{
    // Called on every ViewState. Returns weighted future views.
    IReadOnlyList<PredictedView> Predict(ViewHistory history, PredictionInputs inputs);
}

public readonly record struct PredictedView(ViewState View, double Probability, TimeSpan Horizon);

public sealed record PredictionInputs(
    RouteLegs? ActiveRoute,     // legs ahead of own ship
    OwnShipState? OwnShip,      // in follow mode
    IReadOnlyList<GeoPoint> RecentPlaces);
```

Model, by strength of signal:

1. **Known destinations.** A fling's resting viewport and an animated zoom's
   target are known when the gesture starts. These get probability near 1 and
   `Predicted` class. [H: the strongest predictor; today only an EMA of the
   centre delta is used]
2. **Motion.** EMA of centre velocity (today's `VelocityEstimator`, α = 0.4)
   plus a zoom-rate estimate (new). The fan depth scales with speed, as now.
   [F]
3. **Route and own ship.** In follow mode, cells along the next few legs of the
   active route get `Predicted` cell demand, nearest first.
4. **History.** Recent places and the start view get low-probability cell
   demand, so jumping back is warm.
5. **Idle.** When the view is idle, `Background` cell demand covers the whole
   collection, nearest first: today's pre-warm.

Release hints come from the same model. A cell behind the direction of travel,
outside the ahead window and not in recent history drops out of cell demand.
Its scene and tiles become first in line for eviction. Parsed cells and
portrayal outputs stay on disk, so a cell that comes back costs a warm load,
not a cold one.

Today's covered-cell deferral
([#1265](https://github.com/philliphoff/EncDotNet.S100/issues/1265)) stays
inside the planner: a coarse cell whose part of the view finer cells cover gets
`Predicted` rather than `Visible` demand. It still uses catalogue coverage
polygons, because bounding boxes are unsafe along coastlines. [M]

The planner measures itself. Each visible slot that was already resident when
it became visible counts as a prediction hit. Each prediction that expired
unused counts as waste. Today's `PredictedInCache` counter becomes this metric.

### Memory budget

One budget covers every asset manager and both kinds of memory.

```csharp
public interface IMemoryBudget
{
    long LimitBytes { get; }
    MemoryAccount Open(string name, MemoryKind kind);  // Managed or Native
    MemorySnapshot Snapshot();  // per account, GC committed, GC heap, native
}

public sealed class MemoryAccount
{
    public void Charge(long bytes);   // exact for native (bitmaps), estimated for managed
    public void Credit(long bytes);
}
```

- **Limit.** A share of physical memory, by machine tier. Proposed: 25% of RAM,
  capped at 6 GB in the viewer. [H]
- **Native memory is exact.** Raster bytes are `w × h × 4`. GPU textures have
  their own account, sized from the Skia resource budget that is already tiered
  by machine. [M: the default of about 28 MB thrashed]
- **Managed memory is estimated.** Each asset reports an estimate when it's
  built (op counts × typical sizes, feature counts for parsed cells). The
  budget checks the sum against `GC.GetGCMemoryInfo().HeapSizeBytes` and
  scales estimates to match. [H: estimates track the real heap within 30%]
- **Eviction ranks by value.** Score = demand class and probability, then
  time since last use, then rebuild cost per byte. Assets with no demand go
  first. A leased asset is never evicted.
- **A parsed cell is the cheapest to drop and the costliest to keep.** It's
  only needed to build a scene and to answer picks. Once the scene exists, the
  parsed cell can go. A pick re-parses on demand at `Visible` priority. [H:
  10–500 ms pick latency on a cold pick is acceptable; or keep parsed cells for
  visible cells only]
- **Give memory back after bursts.** When the scheduler has been idle for a
  few seconds after heavy work, the budget runs one compacting, decommitting
  gen-2 collection, rate-limited and never during a gesture
  ([#1276](https://github.com/philliphoff/EncDotNet.S100/issues/1276) item 1).
- **Report it.** `MemorySnapshot` feeds `get_render_stats` with the managed,
  native and GC-committed split
  ([#1276](https://github.com/philliphoff/EncDotNet.S100/issues/1276) item 3).

### Content store

One content-addressed disk store serves every asset kind.

- A file per key under a kind folder. Writes go through a temp file and an
  atomic move, as today.
- One global byte budget with per-kind shares. A running size index replaces
  directory scans on each write
  ([#691](https://github.com/philliphoff/EncDotNet.S100/issues/691) item 4).
- Writes are background jobs. Reads are async IO and hold no worker.
- **Tile codec.** PNG at level 100 is slow to encode. Use a fast lossless
  format for the cache (LZ4 or zstd over premultiplied pixels) and keep PNG for
  exports. [H: measure encode and decode per tile against PNG]
- **Empty slots are recorded.** A slot with no ops gets a zero-byte marker so
  nothing rasterises or decodes it. The spatial-index filter that removed the
  63% of empty PNGs stays as the first check. [M]
- **New: cell scenes on disk.** A warm load today still parses the cell and
  rebuilds its scene from cached Lua output. Storing the scene (ops and index)
  lets a warm slot render without parsing at all. [H: measure the warm load
  breakdown of 8–360 ms first]

### Threading model

| Thread | May do | Must not do |
|---|---|---|
| UI | input, `SetView`, settings, read snapshots | parse, portray, compose, NTS, raster, disk IO |
| Render (Mapsui draw callback) | `GetFrame`, blit chart tiles, draw live planes, GPU upload within a per-frame budget, free GPU objects | wait on a lock held by workers, schedule work synchronously |
| Workers (`ProcessorCount − 1`) | every scheduled job | touch a `GRContext` |
| Async IO | disk reads and writes | CPU-heavy work |

- **Snapshots.** Workers publish immutable results by atomic reference swap:
  `ChartStackSnapshot`, the tile index for the current view, overlay indices.
  The render thread reads them without locks.
- **GPU upload.** CPU-raster workers produce `SKImage`s. The render thread
  uploads them with `ToTextureImage` up to a per-frame byte budget, visible
  slots first. A slot swaps to its new image only once it's uploaded, so an
  upload never shows a half-ready slot. Once uploaded, the CPU copy is released;
  the disk store can restore it. [M: pans are gated by upload; H: the
  per-frame budget smooths it]
- **No `SynchronizationContext` assumptions.** Viewport callbacks call
  `SetView`, which is thread-safe. Anything that must run on the UI thread goes
  through an injected dispatcher
  ([#1138](https://github.com/philliphoff/EncDotNet.S100/issues/1138)).

### Keeping every core busy without starving the view

1. Every stage is in one queue, so an idle core always finds the best work
   available, whatever stage it's in.
2. Splitting long jobs bounds how long a visible job waits: at most one job
   length, about 100 ms. [H]
3. **Lua runs in shards.** S-100 Part 9a defines `PortrayalMain(featureIDs)`,
   and `LuaPortrayalSession` already calls it with a feature subset. A cell's
   features split into shards of about 500 features. Each shard runs in its own
   Lua state on its own worker, and the outputs merge in feature order before
   the existing de-duplication and priority sort. On Lua-CSharp the slowest
   measured cell takes 1.3 s on one core, so shards could bring it to a few
   hundred milliseconds. [F: the entry point takes IDs; M: 1.3 s; H: the
   speed-up, and that shard outputs match a full run byte for byte]
   - `TextPlacement` features must go in the same shard as the feature they
     belong to, because `PortrayalMain` handles them after their parent.
   - Each shard pays the Lua state set-up cost. Pool and reuse prepared states
     per catalogue and context. [H: set-up is a small share; measure on
     Lua-CSharp, not MoonSharp]
   - Every shard runs with the same engine shims as a full run (the `__eq` and
     `require` fixes from [#1278](https://github.com/philliphoff/EncDotNet.S100/issues/1278)), so shard parity and engine parity are tested
     together.
4. Pattern clips split by `(PatternReference, priority)` group, which
   `ClipPatternAreas` already forms. [F]
5. **Pre-warm uses idle cores.** When nothing visible or predicted is queued,
   `Background` portrayal fills every free worker, limited only by the GC
   pause feedback. Today one worker takes 63 s for 298 cells. [M: 63 s; H: 15
   workers bring that under 10 s]
6. Validation runs in the lowest class and never delays a tile.

### Hosts

```mermaid
flowchart LR
  Engine[EncDotNet.S100.Rendering.Engine<br/>ChartSession, scheduler, budget, store, tiles]
  Skia[Renderers.Skia<br/>SkiaDisplayListRenderer]
  Scene[Rendering.Scene<br/>VectorScene, clipper]
  Mapsui[Renderers.Mapsui<br/>one chart layer + adapter]
  Viewer[SoundCharts]
  Cli[s100 render / tiles / mcp serve]
  Engine --> Skia --> Scene
  Mapsui --> Engine
  Viewer --> Mapsui
  Cli --> Engine
```

- **Engine.** A new host-neutral project. It depends on `Rendering.Scene`,
  `Renderers.Skia`, the dataset pipelines and NTS, but not on Mapsui or
  Avalonia. Skia is a raster library here, not a host.
- **Mapsui adapter.** One custom layer, `ChartLayer`, draws the base, overlay
  and label planes for the whole session. Mapsui no longer gets one layer per
  cell, so `ReplaceDatasetLayers` no longer walks hundreds of layers. Its
  renderer delegate calls `session.GetFrame(view)` and blits. Picks go through
  the engine's spatial index (`IdentifyFeaturesService` already works this way),
  not through one Mapsui `IFeature` per op.
- **Avalonia.** Provides the dispatcher, device scale and the `GRContext` on
  the render thread. Nothing else.
- **`s100 tiles`.** Creates a `ChartSession` with prediction off, a large
  memory limit, all cores, and the shared content store. It calls
  `RenderTilesAsync` over the requested range. Tile jobs run in the `Visible`
  class in the order they will be written. MBTiles, PMTiles and XYZ sinks stay
  as they are.
- **`s100 render`.** Calls `RenderAsync` for one view. The engine renders the
  view's slots and stitches them, or rasterises the view as one tile when that
  is cheaper. Portrayal of several cells runs in parallel, which `render`
  doesn't do today. [F]
- **`s100 mcp serve`.** Same as `render`, with a resident session. Captures
  honour `pixelDensity` through `DeviceScale`, which the headless path ignores
  today. [F]
- **SoundCharts MCP captures.** Call `RenderAsync` at the requested size and
  density instead of re-running Mapsui's renderer into a bitmap. A capture's
  tiles go in a different pixel-size key from the screen's tiles.

The engine replaces `HeadlessCompositor`, `HeadlessTileRenderer` and the tile
half of `S100VectorTileRenderer`. The CLI and the viewer then share one coverage
model (NTS), one scale-class rule and one disk format.

### Rules that must stay solved

| Earlier problem | Where the rule lives in this design |
|---|---|
| 63% empty disk tiles | spatial-index content check before scheduling, plus empty markers in the store |
| GPU upload thrash | Skia resource budget tiered by machine, plus a per-frame upload budget |
| Clip paths for every finer cell | stack relations found through an envelope R-tree; tile raster clips only by coverages that touch the slot |
| Overscale curtain rebuild | regions memoised per stack version; only the overscale filter runs per zoom step |
| Hidden coarse tiles | skipped only when one finer coverage hides them, never by a union |
| Device scale in tile keys | `PixelSize` in `ChartTileKey` |
| Snapped band up to √2 off the live scale | `ScaleClass` and `ScaleMinimumCap` unchanged, per slot |
| World copies at −C, 0, +C | unchanged in the tile grid; seam and gutter rules move with `RasterizeTile` |
| `SynchronizationContext` in viewport handlers | `SetView` is thread-safe; injected dispatcher |
| `ReplaceDatasetLayers` O(L²) | one chart layer; the diff stays for the other bands |
| Display toggles re-run Lua | cell scene key hashes the filter, not the Lua inputs |
| Pan frames gated by upload | one blit per slot, per-frame upload budget |
| UI-thread compose | `Stack` jobs on workers |

## How the existing code fits

### Component map

Keep: moves unchanged or nearly. Reshape: same idea, new shape. Replace: new
code does the job. Delete: no longer needed.

| Today | Verdict | In this design |
|---|---|---|
| `TileGrid`, `TileRange`, `VisibleTileRange`, `PredictedTiles`, `CrossBandPrewarmTiles` | keep | engine tile grid and demand planner |
| `VelocityEstimator` | keep | prediction model, motion input |
| `TileKey` | reshape | `ChartTileKey`: add `PixelSize`, `StackSlice` |
| `TileScaleClass` | keep | thresholds become the union of a slot's scenes |
| `TileCache` (per layer) | replace | chart tile manager on the global budget |
| GPU `TileCache` and `GpuRegistry` | reshape | render-thread GPU account; one cache, not one per layer |
| `TileDiskCache` | reshape | content store, tile kind; running size index; faster codec |
| `DiskPortrayalInstructionCache` | keep | content store, portrayal kind; add single-flight |
| `DiskPatternClipCache`, `InMemoryPatternClipCache` | keep | content store, clip kind |
| `DiskCacheBudget` | reshape | one global disk budget with per-kind shares |
| `S100VectorTileRenderer`: `Render`, `Composite`, `BlitCopies`, `BlitTile`, `CompositeRotated`, `DrawOverlay` | reshape | frame compositor in the Mapsui adapter, reading `FrameSnapshot` |
| `S100VectorTileRenderer`: `RasterizeTile`, `RasterizeMetatile`, `DrawAdjacentWorldOps` | keep | engine raster job; input becomes several scenes |
| `S100VectorTileRenderer`: `TileState`, pending sets, `Worker`, `ComputeWorkersToStart`, `ShouldWorkerExit`, fairness registry, `HasVisibleTileWork` | replace | work scheduler |
| `TileRevealGroup` | delete | chart tiles reveal atomically |
| Predecessor stand-in (`TryBindFilteredScene`, `AdoptPredecessor`) | replace | stale-while-revalidate on `StackSlice` |
| `CoverageClip`, `BuildActiveDifferencePaths` | reshape | tile-space clip during raster; no `ILayer` key |
| `HiddenCoverage`, `HiddenCoverageCache` | keep | per-slot op culling |
| `OverlapSuppression`, `CollectFinerCoverages` | reshape | incremental stack relations, off the UI thread |
| `LayerStackProjector`, S-98 filtered layers | reshape | op filter on the stack, not filtered Mapsui layers |
| `OverscaleCurtain`, `OverscaleCurtainRegionCache` | keep | memoised per stack version |
| `MachineProfile` | reshape | budget limits and upload budget by tier; no worker counts |
| `RenderingOptimizations` env knobs | reshape | `ChartSessionOptions` ([#607](https://github.com/philliphoff/EncDotNet.S100/issues/607) Q2) |
| `WorkerDrainGate` | keep | scheduler shutdown |
| `MapsuiDatasetLayerSession` (2.6k lines) | replace | `ChartSession`; the Mapsui adapter keeps band management |
| `MapsuiDatasetRenderer`, `MapsuiDisplayListRenderer` | reshape | cell scene manager; no per-op `IFeature`, no `InstrumentedMemoryLayer` per cell |
| `MapsuiLayerBands` | keep | for basemap, overlay and tool bands |
| `SharedExclusiveGate`, `RenderGates`, `_renderGate` | delete | jobs take an immutable portrayal context; nothing mutates a shared catalogue per render |
| `ExchangeSetLazyLoadCoordinator`, `LoadPrioritizer`, `LoadWindow` | reshape | cell demand in the planner |
| `CoveredCellGate` | keep | planner |
| `LruEvictionPolicy`, `RetentionBudget = 400` | replace | global memory budget |
| `PortrayalCacheWarmer` | reshape | `Background` cell demand producer; no worker of its own |
| `PortrayalWarmLedger`, `PortrayalWarmSignature` | keep | skip cells already in the store |
| `DatasetLoaderService` | reshape | catalogue entry state; loading moves into asset managers |
| `MoonSharpLuaEngine`, `EncDotNet.S100.Scripting.MoonSharp` | replace | `EncDotNet.S100.Scripting.LuaCSharp` behind the same `ILuaEngine` ([#1278](https://github.com/philliphoff/EncDotNet.S100/issues/1278)) |
| `LuaNoOpCallStripper`, provider `Debug` shims ([#1277](https://github.com/philliphoff/EncDotNet.S100/pull/1277)) | reshape | re-measure on Lua-CSharp; delete if they no longer pay |
| `LuaRuleExecutor` | reshape | shard runner; pooled prepared states |
| `VectorPipeline`, `DrawingInstructionParser`, `DrawingInstructionSerializer` | keep | portrayal manager |
| `VectorSceneBuilder` | keep | add cancellation; split by instruction range |
| `PatternPriorityClipper` | keep | split by pattern group |
| `BaseSpatialIndex`, `OverlaySpatialIndex` | keep | per cell scene; merged overlay index per stack |
| `SkiaDisplayListRenderer` | keep | raster jobs |
| `HeadlessCompositor`, `HeadlessTileRenderer`, `TileRenderSession`, `TileScene` | replace | engine `RenderAsync` and `RenderTilesAsync` |
| `MbTilesTileSink`, `PmTilesTileSink`, `XyzDirectoryTileSink` | keep | CLI |
| `AvaloniaMapsuiMapAdapter.RenderCurrentViewToPngAsync` | replace | `RenderAsync` |

### Staged migration

Each slice ships alone, is measured with the harness from slice 1, and leaves
the app working. The order puts the biggest gains users can see first, and
moves risky structural work after the measurements it depends on.

| # | Slice | User-visible gain | Measure | Depends on |
|---|---|---|---|---|
| 0 | Switch the Lua engine to Lua-CSharp ([#1278](https://github.com/philliphoff/EncDotNet.S100/issues/1278)) | cold settle, pre-warm, memory | cold portrayal time and bytes; goldens, S-164 | — |
| 1 | Settle harness and metrics | none | baselines below | — (best before 0 merges) |
| 2 | Lua shards within a cell | cold jump settle | cold settle p50/p95; shard output equals full run | 0, 1 |
| 3 | Pre-warm on idle cores | warm sooner after open | pre-warm wall time; GC pause share | 0, 1 |
| 4 | Global tile budget, core-only tiles, idle GC | working set | peak and idle working set | 1 |
| 5 | One work scheduler | settle, no starvation | cores busy; visible job wait p95 | 1 |
| 6 | Stack off the UI thread, incremental | no UI hitch on load | longest UI-thread task | 5 |
| 7 | Extract the engine ([#607](https://github.com/philliphoff/EncDotNet.S100/issues/607)) | none | VisualRegression unchanged | 5 |
| 8 | Chart tiles and one chart layer | atomic reveal, memory, pan | reveal trace; native bytes; pan p95 | 4, 6, 7 |
| 9 | Prediction v2 | warm next view | prediction hit and waste rates | 8 |
| 10 | CLI and MCP on the engine | parallel `render`, density | CLI wall time; render parity | 7 (better after 8) |
| 11 | Content store unification and cell scenes on disk | warm settle | warm settle; disk bytes | 8 |

**0. Switch the Lua engine to Lua-CSharp.** This is
[#1278](https://github.com/philliphoff/EncDotNet.S100/issues/1278), done as that
issue describes: a new `EncDotNet.S100.Scripting.LuaCSharp` package behind the
existing `ILuaEngine` and `ILuaContext`, the two compatibility shims (`__eq`
dispatch and `require` recording modules that return nothing), a sandbox
equivalent to MoonSharp's hard sandbox, and every host switched before
MoonSharp is removed. It goes first because:

- **It's the largest single gain on the table.** Cold portrayal is 2.3–2.8×
  faster and allocates about 9× less. Lua was 73% of cold-load allocations, so
  total allocations should fall to roughly a third. Every later slice that
  portrays (shards, pre-warm, display changes that re-run Lua, the CLI) gets it
  for free. [M: spike; H: the total, which slice 1 confirms]
- **It changes the numbers the rest of the plan is tuned on.** GC pause share,
  committed memory, how many portrayals can run at once, and whether shards are
  still worth their cost all depend on how much Lua allocates. Tuning the
  scheduler's admission or the memory budget against MoonSharp would be tuning
  against a cost that's about to go away.
- **It needs no architecture change.** The engine already sits behind an
  interface in Core, and the spike drove the real processors through it.
- **It's independent of the render work,** so it can run in parallel with
  slice 1.

Two choices in that issue interact with this design:

- **Sync bridge first.** Keep `ILuaContext` synchronous for step 0, as the
  host functions are synchronous and calls complete synchronously. Revisit an
  async context in slice 5, where scheduler jobs are `ValueTask`-based and a
  yield point inside Lua would be useful.
- **Cancellation.** Pass the `CancellationToken` through Lua-CSharp's calls.
  Keep the per-feature emit check until slice 5 shows whether the scheduler
  needs a finer yield point; it's the scheduler's `YieldRequested` hook too.

After step 0 merges, re-baseline every "Today" figure in
[Measuring success](#measuring-success) before setting gates.

**1. Settle harness and metrics.** Extend `viewer-stress` with a `jumps` route:
Seattle → Tacoma → Port Townsend at 1:44 300 on the "Washington" source, from
empty caches and after pre-warm. Add a **settled** signal to the viewer: every
visible cell loaded and every visible slot showing its target tile. Record time
to settle, pan frame p50/p95/max, cores busy (process CPU time ÷ wall time ÷
cores), GC pause share, bytes allocated, peak working set, and the
managed/native/committed split. Add the memory split to `get_render_stats`
([#1276](https://github.com/philliphoff/EncDotNet.S100/issues/1276) item 3).
None of these metrics exist in PerfRunner today. [F] If the harness lands
before step 0 merges, it records step 0's effect on whole jumps, not only on
single cells.

**2. Lua shards within a cell.** Change `LuaRuleExecutor` to split
`PortrayalMain` across K prepared Lua states, keeping `TextPlacement` with its
parent, and merge in feature order. Run shards through today's thread pool with
`Parallel.ForEachAsync`, capped at the visible-load concurrency. Gate it behind
an option, compare output against a full run for the whole Washington and IC-ENC
corpora, and keep the disk cache keys unchanged. After step 0 the slowest cell
still holds one core for about 1.3 s, so this remains the next gain for cold
settle. Measure it on Lua-CSharp; if state set-up eats the gain, drop it. [M]

**3. Pre-warm on idle cores.** Let `PortrayalCacheWarmer` run N cells at once
while the map is idle, where N follows GC pause share. It still yields to
visible work through the existing busy predicate. Small, separate, and it shrinks
the 63 s window in which jumps are still cold. Step 0 shortens that window by
itself; this slice multiplies the effect. [M]

**4. Global tile budget, core-only tiles, idle GC.** Three changes in the
current renderer:

- Store the core plus a 2-pixel apron instead of the full gutter.
- Replace per-layer `TileCache` budgets with one process-wide LRU account, so
  culled layers' tiles are the first to go
  ([#691](https://github.com/philliphoff/EncDotNet.S100/issues/691) item 1).
- Run the rate-limited compacting collection when idle after bursts
  ([#1276](https://github.com/philliphoff/EncDotNet.S100/issues/1276) item 1).

This doesn't need the new architecture and gives back most of the 3.5 GB of
native tile memory. Step 0 should already shrink the managed half.

**5. One work scheduler.** Add `IWorkScheduler` with dedicated threads. Move,
in order: tile raster jobs (deleting `ComputeWorkersToStart`, the per-layer
floors and the fairness registry), lazy-load portrayal, pre-warm, validation,
and pattern clipping. The per-layer pending sets become interests on scheduler
jobs. After this, priority runs across stages and nothing has a private worker
count.

**6. Stack off the UI thread.** Run `ProjectLayerStack`,
`ApplyOverlapSuppression` and the layer diff as a `Stack` job. Find finer
coverages through an envelope R-tree, only for the new cell. Publish the
result and apply it to Mapsui in one dispatcher post. Add stack-version
batching here.

**7. Extract the engine.** This is
[#607](https://github.com/philliphoff/EncDotNet.S100/issues/607). Move the Tier 1
files as listed there, swap `ILayer` for an opaque identity token, use the core
`Viewport`, and turn the `RenderingOptimizations` statics into
`ChartSessionOptions`. Doing this after slice 5 means the scheduler, rather than
the per-layer worker code, moves into the new project.

**8. Chart tiles and one chart layer.** Replace per-layer tiles with chart
tiles and `StackSlice` keys. Replace per-cell Mapsui layers with one
`ChartLayer`, and per-op pick features with engine picks. Delete
`TileRevealGroup`, the predecessor stand-in and S-98 filtered layers. First,
prototype the per-slot raster cost with 5, 10 and 20 overlapping cells; if
re-raster on a landing cell is too slow, see the fallback in the risks.

**9. Prediction v2.** Feed fling and animation destinations into `ViewMotion`.
Add a zoom-rate estimate. Add route and history inputs. Drive cell demand and
release hints from the model. Report hit and waste rates. With faster
portrayal, the cell horizon can be shorter than it would be on MoonSharp.

**10. CLI and MCP on the engine.** Move `render`, `tiles export`, `tiles serve`
and `mcp serve` to `ChartSession`. Delete `HeadlessCompositor` and
`HeadlessTileRenderer`. Captures honour density.

**11. Content store.** One store and budget for every kind, a running size
index ([#691](https://github.com/philliphoff/EncDotNet.S100/issues/691) item 4),
the faster tile codec, and cell scenes on disk if slice 1's warm-load breakdown
shows parsing and scene building dominate.

### How the plan absorbs the open epics

- **[#1275](https://github.com/philliphoff/EncDotNet.S100/issues/1275) and
  [#1278](https://github.com/philliphoff/EncDotNet.S100/issues/1278)** (Lua
  allocations, engine switch) are step 0. Slice 2's shards build on the new
  engine.
- **[#607](https://github.com/philliphoff/EncDotNet.S100/issues/607)** is slice
  7, and its Q2 knob decision is the `ChartSessionOptions` surface. The engine
  is the "renderer-neutral project". Its identity token is the chart session, not
  a layer, because chart tiles make the per-layer key unnecessary.
- **[#691](https://github.com/philliphoff/EncDotNet.S100/issues/691) item 1**
  (memory with many cells) is slice 4 in the short term and slice 8 for good:
  per-layer budgets become one global budget, then the number of raster caches
  stops depending on the number of cells. Items 2 and 3 are already fixed and
  their rules carry over. Item 4 is slice 11.
- **[#1276](https://github.com/philliphoff/EncDotNet.S100/issues/1276)** (GC
  committed memory) starts with step 0, which cuts the allocation rate that
  drives committed memory. Slice 4 adds the idle collection and slice 1 the
  reporting. Its item 2 (re-check GC settings) belongs right after step 0: with
  9× fewer Lua allocations, Server GC's committed-memory cost may no longer be
  worth its pause savings, or a heap-count cap may suffice. Slice 5's GC-aware
  admission is the backstop.
- **The per-layer worker and budget model** goes in three steps: slice 4
  removes per-layer budgets, slice 5 removes per-layer workers, and slice 8
  removes per-layer state altogether.

## Risks and open questions

### Risks

1. **Lua-CSharp maturity.** It's a 0.5.x release with known deviations from
   Lua 5.2. The spike found two that broke S-101 and needed shims. Others may
   show up in S-102, S-131, S-125 or S-401 rules that the spike didn't run.
   Mitigation: [#1278](https://github.com/philliphoff/EncDotNet.S100/issues/1278)'s done-when covers every Lua-portrayed product, the
   visual-regression goldens and S-164; report deviations upstream and keep each
   shim until a fixed release ships. Because the engine sits behind
   `ILuaEngine`, keep MoonSharp buildable until the switch has shipped in a
   release, so a regression can be bisected by engine.
2. **Lua shard parity.** Shards must give byte-identical output to a full run.
   Risks: rules that keep state across features in Lua globals, `TextPlacement`
   ordering, and de-duplication across shard boundaries. Mitigation: a parity
   test over whole corpora, and an option to turn shards off per catalogue.
3. **Shard memory.** Each Lua state holds its own feature proxies. K states may
   allocate more in total than one, which pushes GC. Lua-CSharp's struct values
   make this cheaper than on MoonSharp, but measure allocations per cell with
   K = 1, 4 and 8.
4. **Chart tile re-raster cost.** A slot with many overlapping cells costs more
   to raster than one layer's tile, and a landing cell re-rasters all of it.
   Fallback if the prototype is too slow: cache per-cell raster fragments as
   inputs, and make a chart tile a cheap composite of fragments. This trades some
   memory back for fewer full re-rasters.
5. **Scale class growth.** The union of SCAMIN thresholds across a slot's cells
   may split bands into many classes, multiplying tiles. Measure the class count
   per slot on the Washington source.
6. **Seams.** Clipping coarse cells by finer coverage in tile space must match
   today's anti-aliasing at coverage edges. VisualRegression must pass before
   per-layer clipping goes.
7. **Managed estimates drift.** If asset size estimates are far off, the budget
   either over-evicts or lets the heap grow. Calibrate against the GC heap, and
   keep native memory, which is exact, as the hard limit.
8. **Mapsui.** One custom layer for all chart planes changes how picks,
   legends and layer toggles reach the UI. The app's per-dataset visibility and
   opacity become stack settings, not Mapsui layer properties.

### Open questions for the owner

1. **Composite or per-cell tiles?** Chart tiles are the core of the reveal and
   memory story. Do you accept re-raster on cell arrival, subject to the slice 8
   prototype, or prefer per-cell fragments from the start?
2. **Memory limit.** Is 25% of RAM, capped at 6 GB, the right default? Should
   the user be able to set it?
3. **Parsed cells.** May the engine drop a parsed cell once its scene exists,
   and re-parse on pick? Or keep parsed cells for visible cells only?
4. **Background CPU.** Pre-warm on every idle core is fast but costs battery
   and heat on laptops. Should there be a setting, or a lower default on
   battery power?
5. **Navigator destinations.** Can the Mapsui adapter read a fling's resting
   viewport and an animation's target, or does the app need its own gesture
   handling to know them?
6. **Disk format change.** Moving the tile cache from PNG to a raw compressed
   format invalidates every user's tile cache once. Acceptable?
7. **CLI parity.** Moving the CLI to the engine changes its coverage handling
   from bounding boxes to NTS, so some exports will differ. Is a one-time
   change in CLI output acceptable?
8. **S-102 in chart tiles.** Should gridded products join chart tiles (phase 2
   of the coverage plane), or stay a separate layer drawn above them?
9. **Settle targets.** The targets below are proposals. Which do you want as
   gates? They should be set against the post-step-0 baseline.
10. **Harness before engine?** Step 0 can start now. Do you want to
    hold its merge until slice 1's harness can record its effect on whole
    jumps, or merge on the per-cell measurements?
11. **GC mode after step 0.** Should #1276 item 2 (re-check Server GC and its
    settings) be a gate on step 0, or a follow-up?

### Measuring success

All figures from the slice 1 harness, Release, on the 16-core Mac used for
[#1276](https://github.com/philliphoff/EncDotNet.S100/issues/1276), NOAA
"Washington", load as you pan, 1:44 300, Seattle → Tacoma → Port Townsend.

"Today" is MoonSharp on `main`. Re-baseline after step 0; the spike suggests
cold portrayal times drop by more than half on their own.

| Metric | Today [M] | Proposed target |
|---|---|---|
| Jump settle, cold | 5–18 s | p95 ≤ 3 s |
| Jump settle, after pre-warm | 1.2–1.8 s | p95 ≤ 0.8 s |
| Pre-warm, 298 cells | 63 s | ≤ 15 s |
| Cold portrayal, US3WA1EF | 3.7 s, 5,027 MB (1.3 s, 553 MB on Lua-CSharp in the spike) | ≤ 1.3 s, ≤ 600 MB after step 0; lower after shards |
| Bytes allocated, three cold jumps | about 43 GB | ≤ 15 GB |
| Cores busy during a cold jump | about 3 of 16 (workstation GC) | ≥ 12 of 16 |
| GC pause share during a cold jump | 40–60% (workstation), lower with server GC | ≤ 10% |
| Peak working set, about 40 cells | 7–10 GB | ≤ 3 GB |
| Idle working set after pre-warm | 4.3 GB (live heap 0.7 GB) | within 1 GB of live data plus caches |
| Warm pan frame | about 3 ms | p95 ≤ 8 ms, no frame over 33 ms |
| Longest UI-thread task during a load | about 230 ms per 14-cell compose | ≤ 8 ms |
| Layers revealing separately in a slot | yes | never (reveal trace) |
| Prediction hit rate (visible slots already resident) | not measured | report; no target yet |

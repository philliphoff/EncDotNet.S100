# Dual map — Design Note

> Status: **Draft**, round 1. This note looks at what SoundCharts needs
> to show two maps side by side. The end goal is that each map can show a
> different subset of datasets, or a different time (for example, a harbour
> approach at low water and at high water). Step 1 is narrower: a second
> map with its own viewport that shows the same datasets at the same time.
>
> How the split map looks and behaves is set by a design pass from
> [dual-map-brief.md](dual-map-brief.md). This note covers the services
> and engine changes underneath it. Where they disagree, the design pass
> decides the behaviour and this note changes to match.

## Decisions so far (2026-11-23)

1. **The services make no assumption about the number or arrangement of
   maps.** Step 1 ships two panes side by side. Nothing under the window
   may assume two maps, "left and right", or a fixed primary and
   secondary. They work with a `MapViewId` and the `MapViews` registry.
   Layout (side by side, stacked, a grid) belongs to the window only.
2. **Linked viewports are an optional toggle.** The panes are independent
   by default. The design pass decides what a link keeps in step (centre
   only, or centre and scale) and where the toggle lives.
3. **There is an active map, chosen by last use.** The design pass decides
   how it is shown and what follows it.

## 0. Goal and steps

| Step | What the user gets | What differs between maps |
|---|---|---|
| 1 | View › Split map: two panes, each panned and zoomed on its own | Viewport only |
| 2 | Each pane shows its own subset of the loaded datasets | Viewport, dataset visibility |
| 3 | Each pane has its own timeline time | Viewport, visibility, time |
| Later | Per-pane display settings (palette, viewing groups), linked viewports | Presentation |

This note designs step 1 in full. For steps 2 and 3 it says only enough
to make sure that step 1 doesn't block them.

## 1. Findings

### 1.1 The rendering libraries are mostly ready

`mapControl.AddS100(...)` already builds a complete set of per-map
objects: `MapsuiLayerBands`, `MapsuiDatasetLayerSession`, `ChartSession`,
the navigator, the dynamic-source host, the adapter and the redraw sink
(`Renderers.Mapsui/S100MapExtensions.cs:76-123`). Each session draws one
`ChartLayer`. Tile state, scenes and coverage clips are keyed on that
layer (`S100VectorTileRenderer.cs:477`, `S100VectorSceneRenderer.cs:95`).

**Time and per-dataset display state are already per session.** The
session holds `_time` (`MapsuiDatasetLayerSession.cs:73`, `SetCurrentTime`
at `:503`) and snaps it per dataset. It passes the time into each render
through `CreateRenderContext(processor, selectedTime)`. Processors don't
hold a current time. Mariner settings, viewing groups, hidden features
and portrayal time live on the session's own `MapDataset`. **Steps 2 and 3
need no change to the engine's model.**

These can be shared across two sessions as they are:

- `DatasetProcessorOwner` and `MapsuiDatasetRenderer`. Render gates and
  asset caches are keyed per processor, so two maps' renders of one cell
  run one after the other.
- `LayerRepaint`, which routes by owner layer, and `ChartEngine.Options`.
- `WorkScheduler.Shared`. Both maps' visible work competes evenly.
- `TileMemoryPool`. One budget covers both maps (see §4).
- The disk tile cache, which is content-addressed, so map B reuses map
  A's tiles. The GPU registry is keyed by `GRContext`.

These must change:

| # | Problem | Where | Change |
|---|---|---|---|
| R1 | `DemandPlanner` tracks one viewport history, motion and fling scale. Two navigators feeding it corrupt prediction, motion-end detection and the stack hold. | `Rendering.Engine/Prediction/DemandPlanner.cs:39-44`, `MapsuiMapNavigator.cs:112,155,164` | One planner per map. The lazy-load coordinator takes the union of the planners' cell demand. |
| R2 | `AddDatasetAsync` calls `TryRegister`, which fails when another map already registered the processor. Its rollback also retires the processor. | `S100MapSession.cs:142,174` | The viewer registers a processor once, then calls `SetDataset` and `RenderAsync` on each session. It already works this way at the lower level. |
| R3 | `RemoveDataset` defaults to `removeProcessor: true`, which retires the processor for both maps. | `MapsuiDatasetLayerSession.cs:1274` | Sessions remove with `removeProcessor: false`. One owner retires the processor. |
| R4 | `CaptureCoordinator` is static. A drain on map B can release a capture waiting on map A. | `Renderers.Mapsui.Avalonia/CaptureCoordinator.cs:10-16` | Make it one instance per control. |
| R5 | `RevealTrace.Slots` has no owner in its key. | `RevealTrace.cs:28` | Add the owner. Diagnostics only. |

### 1.2 The viewer assumes one map in four places

**1. MainWindow wiring.** The constructor (`MainWindow.axaml.cs:202-624`)
builds one `MapsuiMapHost` and about 15 controllers against it. These
include pick highlight, legend find, extent indicators, AML outlines,
library coverage, adjusted-area boundary, overscale curtain, validation
overlay, the dynamic-source host, `MapInteractionController`, map tools,
route-check hit testing (`:1101-1340`), and render-activity and settle
probes. Teardown (`:239-304`) nulls each one by hand. Most of these
controllers already take the host as an argument, so moving them is
mechanical.

**2. The dataset loader owns the one session.**
`DatasetLoaderService.Initialize` keeps one `_mapSession` (`:289`). It
drives about 20 calls on that session: `SetDataset`, `PublishStoredAsync`,
`RenderAsync`, `RefreshAsync`, `RefreshTimeAsync`, `SetCurrentTime`,
`SetMarinerSettings`, `SetOrder` and `RemoveDataset(s)`. It also pulls
state back into `DatasetEntry` (`ProjectSessionState`, `:1674`).
`GlobalTimeService.AttachTo` throws when it is attached to a second
session (`GlobalTimeService.cs:94`).

**3. Singletons that stand for "the map".** These are
`MapCapabilityAccessor<IMapViewportController | IMapCoordinateConverter |
IImageRenderer>` and `MapViewportNotifier`. About 20 services read them:
legends, depth basis, AML display, tide-prediction station choice, route
and vessel zoom-to, feedback, and the pick presenter. Most of them should
follow whichever map the user is working in. `MapViewportNotifier.Bind`
already detaches the previous navigator, so rebinding it on focus change
is cheap.

Two consumers must not follow the active map. They need the union of
both viewports, or the other map starves:

- exchange-set lazy loading, pre-warm and `ViewportActivity`
  (`ExchangeSetLazyLoadCoordinator.cs:264,310,408`)
- the AIS and hosted-source zoom gate (`HostedSourceActivator.cs:55-58`)

**4. MCP and diagnostics.** These tools reach the map through the
singletons: `set_viewport`, `pick_features`, `fling`, `render_to_image`,
`identify_features`, and `await_settled`/`await_render_idle`/`get_render_stats`.
`RenderActivityHub.Sink` is static (`Diagnostics/RenderActivityMonitor.cs:462`).
The image-renderer accessor is the MCP readiness gate.

### 1.3 What steps 2 and 3 will need

These don't block step 1, but they shape it.

- **Per-map time.** Time is stored on `DatasetEntry`:
  - `DateDependentDrawingService` sets `PortrayalTime`.
  - `NavigationalWarningDrawingService` sets `HiddenFeatures`.
  - `LayerTimeCoordinator` sets each row's layer time.

  The loader then projects these onto the one session. Per-map time needs
  these to be per (map, dataset). It also needs a time service per map,
  and about 15 `IViewTime` and `GlobalTimeService` consumers split between
  "active map" (Timeline, HUD, legends, picks) and "per map" (drawing
  services).
- **Presentation is global.** `ViewerPresentationCoordinator` applies one
  presentation through one `IMapPresentationController`. Per-map palettes
  or viewing groups would also thrash `S101DatasetProcessor`'s single-slot
  portrayal cache (`:101-107`). This stays global until a later step.

## 2. Design for step 1

### 2.1 `MapView`: one per pane

Add a `MapView` that owns everything that belongs to one map:

- the `MapsuiMapHost`, with its own `DemandPlanner`
- a per-map `MapViewportNotifier`
- the per-map overlay controllers from §1.2 point 1
- `MapInteractionController` and the route-check hit testing
- the render-activity and settle probes
- `IDisposable` teardown

A `MapPane` user control holds the map and its own chrome: scale bar,
compass, zoom buttons and the Time HUD. The mode buttons, the top-left
tool cards and the right-dock overlay stay at window level. Most of this
is extracted from `MainWindow.axaml.cs` and `MainWindow.axaml:630-1270`.

`MapView` gets a stable `MapViewId`, which MCP and settings use. Ids are
opaque, such as `map-1` and `map-2`. They aren't positional names like
"left" or "secondary", because a later layout can arrange the panes
differently. One view is the *first* view, which can't be closed. It is
the readiness gate and the session that `ProjectSessionState` reads. It
isn't special in any other way.

### 2.2 `MapViews`: the registry and the active map

Add a singleton `MapViews` with `All`, `Active`, `ActiveChanged`,
`Add`/`Remove`, and a union viewport.

- The active map is the last one the user clicked, scrolled or focused.
  The pane shows a thin accent edge when there are two.
- On `ActiveChanged`, the existing `MapCapabilityAccessor<T>.Current`
  values and the window-level `MapViewportNotifier` are rebound. So every
  current consumer follows the active map without changes.
- The lazy-load coordinator and the hosted-source gate read the union
  viewport, and take the planners from every view.
- The status bar reads lat/lon from the hovered pane, and scale from the
  active one.
- The keyboard-scope checks (`ReferenceEquals(visual, MapControl)` at
  `MainWindow.axaml.cs:151-173,1829`) become "is inside any `MapPane`".

### 2.3 The loader drives a set of sessions

`DatasetLoaderService` keeps one dataset model and fans the session calls
out to every `MapView`'s session:

- **Load.** Register the processor once in `DatasetProcessorOwner`. Then
  `SetDataset` and `RenderAsync` (or `PublishStoredAsync`) run per
  session. The load counts as visible when the active map has painted it.
- **Remove.** Each session removes with `removeProcessor: false`, then the
  loader retires the processor once (R3).
- **Order, mariner settings, refresh, time.** These are applied to each
  session.
- **`ProjectSessionState`** reads from the first view's session. In step 1 the
  sessions agree, because the inputs are the same.
- **A pane opened after datasets are loaded** is caught up by the same
  code path that publishes a new dataset.

`GlobalTimeService` stays attached to the first view's session. The loader
pushes `SetCurrentTime` to every session, so in step 1 the maps share
one time. Step 3 replaces this with one time service per map.

The other option is a separate "mirror" service that copies the first
view's session into the others. I'd not do that: the loader already owns
ordering, sub-layer state and reload generations, and a mirror would
duplicate all of it.

### 2.4 Tools, picks and overlays

| Thing | Step 1 behaviour |
|---|---|
| Pick and Object Information | One report. The highlight is drawn in the pane the pick came from. |
| Measure, range & bearing | Drawn and edited in the active pane. State stays single. |
| Routes and route-check overlay | Drawn in both panes, because a route is data and not a tool. Editing happens in the active pane. |
| Dynamic sources (AIS, own ship) | Drawn in both panes. The visibility settings are shared. |
| Library coverage, extent indicators, overscale curtain | Per pane, from the pane's own notifier. Library coverage registers one legend source, not one per pane. |

### 2.5 MCP

- Each map tool gets an optional `map` parameter. The default is the
  active map. `ICapabilityAccessor<T>` gains `Get(MapViewId)`.
- Add `list_maps` and `set_active_map`, plus a `set_split_map` toggle.
- `await_settled` and `await_render_idle` wait for every pane.
  `get_render_stats` reports per pane. `RenderActivityHub` becomes an
  instance that `MapView` owns.
- The readiness gate stays "the first view exists".

### 2.6 Layout and settings

- View › Split map (and MCP) adds or removes a second view. A window-level
  layout arranges the views. Step 1 has one layout, side by side in a
  `Grid` with a splitter. Adding a layout changes only the window.
- Settings store the layout, the sizes of the panes, and each view's
  viewport, keyed by `MapViewId`. They don't store a "left" and "right".
- `ShellLayoutViewModel.MapWidth` becomes the width of the whole map area.
  Each Time HUD reads its own pane's width.
- When the split opens, the new pane starts at the active pane's view.

### 2.7 Sharing between maps

Two panes shouldn't hold two copies of everything. The rule: **key
content by what it depends on, not by which map asked for it.** Two panes
whose inputs match then get the same object. They get separate objects
only where their inputs really differ, such as a time-varying dataset at
two times in step 3.

**Already shared.** These are processors and parsed data, portrayal
assets (`AssetCaches`, per processor), `WorkScheduler.Shared`, the
`TileMemoryPool` budget, the disk tile cache, catalogues and the cell
scene store.

**Duplicated today, and could be shared by content key:**

| What | Today | Shared form |
|---|---|---|
| **Vector scenes**, with their spatial index and coverage clip regions | Built on each session's `RenderAsync` (`MapsuiDatasetRenderer.cs:210-337`) | A process-wide, reference-counted scene cache keyed by processor plus scene inputs: presentation, selected time, viewing groups, hidden features and portrayal time. A session that asks for a scene that already exists borrows it. `RenderGates` already serialises renders per processor, so the second pane hits the cache instead of racing. |
| **Chart tile rasters in memory** | One `TileCache` per owner (`S100VectorTileRenderer.cs:4326`) | One process-wide memory cache keyed by `ChartTileKey`. That key is already content-addressed: `StackSlice` hashes the slot's ordered scene keys, suppression, opacity and clipping coverages, and the disk cache uses it in exactly this way. Each owner keeps only its slot plan, view state and settle state. |
| **Tile jobs in flight** | Each owner queues its own | Deduplicated by `ChartTileKey`. A second request for a tile that is already being drawn waits for that job. Prediction and pre-warm from both panes then add up to one set of jobs. |
| **GPU textures** | Residency cache per owner, registered per `GRContext` (`:501`) | Keyed by (`GRContext`, `ChartTileKey`). Panes in one window share a compositor and its context, so a tile uploads once. This needs checking on Avalonia's Skia backend. A pane in its own window may get its own context and fall back to its own copy. |
| **Processor portrayal cache** | Single slot (`S101DatasetProcessor.cs:101-107`) | A small LRU keyed by render context. Panes with different contexts then don't evict each other's entries. This matters from step 3. |

**Stays per map.** View state, the demand planner, the slot plan and stack
binding, settle tracking, and the overlay layers (which are small).

**Caveat: stable scene keys.** Content addressing works only when every
scene in a slot has a stable key. `CellSceneKey.For` returns null today
for an entry with viewing groups, hidden features or a portrayal time
(`Viewer/Services/Caching/CellSceneKey.cs`). The scene cache needs a key
that covers those inputs too, as a hash in memory if not on disk.
Without it, those slots fall back to per-owner tiles, which is correct
but not shared.

**What it buys.** In step 1 every input matches, so scenes, rasters,
jobs and textures exist once, whatever the number of panes. Each extra
pane costs only the tiles it shows that no other pane does, plus its view
state. In step 3, only time-varying datasets diverge. S-101, S-57 and
S-102 cells are still shared between a low-water pane and a high-water
pane, and only the S-104, S-111 and weather layers are drawn twice.

The shared caches also help with one pane. Today a stack change or a
session rebuild throws away tiles whose content hasn't changed, and the
CLI's `s100 tiles` and MCP renders could use the same cache. So this work
can land before the split map does.

## 3. Slices for step 1

Each slice ships on its own and keeps one map working.

1. **Engine fixes (R1–R5).** Add one planner per session, let a session
   attach an already-registered processor, make `CaptureCoordinator` an
   instance per control, and add the owner to the reveal-trace key. Test
   with two headless sessions over one owner.
2. **Extract `MapView` and `MapPane`.** This is a refactor with one map
   and no behaviour change. It is the largest diff, but it is mechanical.
3. **Add the `MapViews` registry.** The accessors and notifier rebind on
   `ActiveChanged`. Lazy loading and the hosted-source gate read the union
   viewport. Ships with one view, so it is inert.
4. **Fan the loader out to multiple sessions.** Ship with tests that use
   two fake sessions.
5. **Add the Split map toggle.** This covers the second pane, active-pane
   focus, settings, and the status bar and keyboard scope.
6. **MCP.** Add the `map` parameter, `list_maps`/`set_active_map`, and
   per-pane settle.

Slices 1 and 2 can run in parallel. Slice 3 needs slice 2, slice 4
needs slices 1 and 3, and slice 5 needs slice 4.

The sharing work (§2.7) is a separate track in the engine. It doesn't
block the slices above, but it should land before slice 5 ships:

- **S1. Scene cache.** Reference-counted and keyed by content, with a key
  that covers viewing groups, hidden features and portrayal time.
- **S2. Shared memory tile cache.** Keyed by `ChartTileKey`, with
  in-flight deduplication. The per-owner `TileCache` becomes a view onto
  it.
- **S3. Shared GPU residency.** Keyed by (`GRContext`, `ChartTileKey`),
  after checking that the panes share a context.
- **S4. LRU processor portrayal cache.** Needed for step 3.

Measure each one with the Puget Sound harness, with one pane and with two
headless sessions over the same owner. The target is two panes on the
same view using no more than about 1.1× the memory of one pane.

## 4. Costs and risks

- **Memory.** Until the sharing track (§2.7) lands, tiles, scenes and
  spatial indexes are per session. Two panes on the same cells then use
  about twice the memory against one `TileMemoryPool` budget. Afterwards,
  only content that differs is held twice. Panes far apart still need
  their own tiles, so the budget has to grow with what is visible, not
  with the number of panes.
- **CPU.** Without the sharing track, scene building and rasterising
  happen once per pane. With it, they happen once per distinct content.
  The work scheduler treats both panes' visible work as one class. A cold
  split over two different areas of a large exchange set is still slower
  than one pane.
- **UI-thread paint.** Two `SKCanvas` paints per frame. Watch pan frame
  times with both panes visible.
- **Test churn.** The `IDatasetLoaderService` fakes and the MCP
  accessor fakes need updating in slices 3 and 4.

## 5. Open questions

The behaviour questions (picks, tools, the active map, linking, per-pane
subsets and time) are passed to the design pass
([brief](dual-map-brief.md) §5). §2.4 is a placeholder until the design
pass answers them.

Engineering questions that remain:

1. **Linked viewports and the planner.** When panes are linked, does one
   pane drive and the others follow through `SetViewportToCenterAndResolution`?
   Do the followers' planners then see a jump, or copied motion? This
   decides whether prediction works in the following panes.
2. **Memory budget.** Is it a fixed budget split across the panes, or does
   it grow with the number of panes? This needs measurements from slice 5.

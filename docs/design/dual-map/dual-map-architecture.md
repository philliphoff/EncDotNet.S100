# Dual map — Design Note

> Status: **Draft**, round 1. This note looks at what SoundCharts needs
> to show two maps side by side. The end goal is that each map can show a
> different subset of datasets, or a different time (for example, a harbour
> approach at low water and at high water). Step 1 is narrower: a second
> map with its own viewport that shows the same datasets at the same time.
>
> How the split map looks and behaves is set by the design pass in
> [dual-map-design.md](dual-map-design.md) (from
> [dual-map-brief.md](dual-map-brief.md)); its decisions are cited here
> as D1–D28. This note covers the services and engine changes underneath
> it. §2.1, §2.2, §2.4–§2.6 and §3 were updated to match the design pass
> on 2026-11-24.

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

The design pass (2026-11-24) settled the behaviour questions; see
[dual-map-design.md](dual-map-design.md). The user resolved its open
questions O1–O4 the same day: the link keeps the scale ratio, a
measurement may span panes, the split button goes in the title bar, and
step 3 steps with a fixed offset.

## 0. Goal and steps

| Step | What the user gets | What differs between maps |
|---|---|---|
| 1 | Split map (title bar, View menu, MCP): two panes, each panned and zoomed on its own | Viewport only |
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

A `MapPane` user control holds the map and its own chrome (D8, D9): the
pane header (number, name, link toggle, close; shown only while split),
the scale bar, the zoom buttons, the compass and the map stamp. The mode
column, the Display settings and Legend buttons, the View pill, the
top-left tool cards, the route-check and AML chips and the right-dock
overlay stay at window level, placed against the whole map area. There is
one Time HUD, also window level, drawn in the active pane. Most of this
is extracted from `MainWindow.axaml.cs` and `MainWindow.axaml:630-1270`.

`MapView` gets a stable `MapViewId`, which MCP and settings use. Ids are
opaque, such as `map-1` and `map-2`. They aren't positional names like
"left" or "secondary", because a later layout can arrange the panes
differently. Any view can be closed (D3), so no view is permanently
"first". The registry keeps a *primary* role that moves to a surviving
view when its holder closes. The primary view is the readiness gate and
the session `ProjectSessionState` reads; it isn't special in any other
way. The number shown to the user ("Map 1", "Map 2") comes from the
layout's reading order (D19) and isn't the id.

### 2.2 `MapViews`: the registry and the active map

Add a singleton `MapViews` with `All`, `Active`, `ActiveChanged`,
`Add`/`Remove`, and a union viewport.

- The active map is the last one the user pressed in, scrolled or
  pinched, moved keyboard focus into, or used a tool in. Hover doesn't
  count, and an MCP call with a `map` argument doesn't either (D4). The
  active pane's header shows an accent line and a filled number (D5).
- On `ActiveChanged`, the existing `MapCapabilityAccessor<T>.Current`
  values and the window-level `MapViewportNotifier` are rebound. So every
  current consumer follows the active map without changes.
- The lazy-load coordinator and the hosted-source gate read the union
  viewport, and take the planners from every view.
- The status bar reads lat/lon from the hovered pane, prefixed with its
  number, and scale, overscale and DEPTHS from the active one (D6, D7).
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

From the design pass (D10–D16, D27):

| Thing | Step 1 behaviour |
|---|---|
| Pick and Object information | One report. The highlight is drawn in **every pane that draws the feature**, not only the one picked in. A pane whose view contains the location but doesn't draw the feature draws a dashed grey **ghost** with a reason tag. In step 1 all panes draw the same features, so a ghost appears only from step 2 (dataset hidden) and step 3 (not at that time). The pick result carries `shownIn` per map. |
| Measure, Range & bearing | Window modes with single state, as today. Clicks from **any pane** add points (state is geographic), so a measurement can span panes. Results are drawn in every pane where in view. The click makes its pane active. |
| Routes and route-check overlay | Drawn in every pane. In Edit route mode, waypoints are added and dragged in any pane. |
| Dynamic sources (AIS, own ship) | Drawn in every pane. The visibility settings are shared. |
| Library coverage, extent indicators, overscale curtain, adjusted-area edge | Per pane, from the pane's own notifier. Library coverage registers one legend source, not one per pane. |
| Legend hover-to-find rings | Every pane that draws matching features. The Legend lists the active pane's products. |
| Extent boxes (new) | A pane outlines any other pane whose extent lies inside it and is less than a quarter of its width, with that pane's number. Linked or not. |
| Cursor echo (new) | A ghost crosshair at the pointer's position in every other pane whose view contains it, while split. |
| Map stamp | Per pane. In step 3 only panes not at real time show it. |

Each pane's pick-highlight controller asks the pick model whether its own
session draws the feature (dataset visible in that session and, in step
3, present at that session's time). It draws the outline, the ghost or
nothing from the answer. The extent boxes and the cursor echo read the
other views' viewports and the hovered position from `MapViews`.

### 2.5 MCP

- Each map tool gets an optional `map` parameter. The default is the
  active map, and the call never changes the active map (D4).
  `ICapabilityAccessor<T>` gains `Get(MapViewId)`.
- Add `list_maps` (`id`, `label`, `name`, `active`, `linked`, `viewport`,
  `folded`), `set_active_map` and `set_split_map { open }`, plus
  `set_map_link { linked }` and `set_map_name { map, name }` (D28).
- Pick results gain `shownIn: [{ map, shown, reason }]`.
- `await_settled` and `await_render_idle` wait for every pane.
  `get_render_stats` reports per pane. `RenderActivityHub` becomes an
  instance that `MapView` owns.
- The readiness gate stays "the first view exists".

### 2.6 Layout and settings

- The title-bar split button, View › Split Map (⌘\\) on macOS, and MCP add
  or remove a second view (D1). Closing a pane from its header removes
  that view; turning the split off keeps the active view (D3). A
  window-level layout arranges the views. Step 1 has one layout, side by
  side in a `Grid` with a splitter. Adding a layout changes only the
  window.
- Panes are at least 320 px wide. When the map area is narrower than
  644 px, panes that aren't active fold to a 28 px strip and keep their
  state; clicking a strip swaps which pane is folded (D17). The right dock
  keeps opening over the map.
- The viewport link (D13, D14) is one window-wide state in step 1, off by
  default. It keeps the centre and the scale ratio captured when it was
  turned on. The driving pane is the one being used; the others follow
  through `SetViewportToCenterAndResolution` (see open question 1).
- Settings store the layout, the sizes of the panes, and each view's
  viewport, keyed by `MapViewId`. They don't store a "left" and "right".
- `ShellLayoutViewModel.MapWidth` becomes the width of the whole map area.
  The one Time HUD reads the active pane's width for its compact rules.
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
5. **Add the split.** The title-bar button and menu, View › Split Map,
   the second pane at the active pane's view, closing any pane, the pane
   header with the active accent, active-pane focus rules (D4), the status
   bar badge and lat/lon rule, the keyboard scope, folding below 644 px,
   and settings (D1–D9, D17).
6. **Picks, tools and echo across panes.** The pick outline in every
   pane, with the ghost path in place for steps 2 and 3; measure, range and
   bearing and route-edit clicks from any pane; extent boxes; cursor echo
   (D10–D12, D15, D16).
7. **Viewport link.** The header chain, the split-menu item and ⌘⇧\\,
   centre plus scale ratio (D13, D14). Needs §5 question 1 here answered.
8. **MCP.** Add the `map` parameter, `list_maps`, `set_active_map`,
   `set_split_map`, `set_map_link`, `set_map_name`, `shownIn` on picks,
   and per-pane settle (D28).

Slices 1 and 2 can run in parallel. Slice 3 needs slice 2, slice 4
needs slices 1 and 3, and slice 5 needs slice 4. Slices 6 and 7 need
slice 5 and can run in parallel. Slice 8 needs slice 5; its link and pick
parts follow slices 6 and 7.

The sharing work (§2.7) is a separate engine track. All four slices
merged on 2026-11-24, ahead of slice 5:

- **S1. Shared vector scenes** (#1706). The renderer keeps the scenes it
  built per processor, keyed like the tile disk cache, and a second map
  binds the first map's scene; the engine derives a scene's planes and
  indexes once. Measured on 4 NOAA Puget Sound cells: the second map
  renders in about 40 ms instead of 2.75 s.
- **S4. Shared portrayal results** (#1707). A fresh result equal to one
  another map shows is dropped in favour of the shared one. With S1, the
  second map's heap cost fell from +62 MB to +1.8 MB. The processor still
  portrays per render, so its own single-slot cache is unchanged; an LRU
  for step 3 is still open.
- **S2. Shared chart tiles in memory** (#1716). Chart-layer tile caches
  share through `TileMemoryPool`: adopt on miss, keep one copy of an equal
  put, charge once. A second map over the same view settles in about 4 ms
  instead of 60 ms and adds about 1 MB instead of 12 MB. In-flight
  deduplication was left out: a queued job only skips tiles published
  since it was queued, so two panes asking for the same tile at the same
  moment can both draw it (only one copy is kept).
- **S3. Shared GPU textures** (#1717). Texture caches share per
  `GRContext`; a texture another cache held waits out the orphan delay
  before it is freed. Not yet measured: check it with slice 5.

The stable-scene-key caveat in §2.7 still applies: slots with viewing
groups, hidden features or a portrayal time fall back to per-map tiles.

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
subsets and time) are answered in [dual-map-design.md](dual-map-design.md).
Its questions O1–O4 were resolved by the user on 2026-11-24.

Engineering questions that remain:

1. **Linked viewports and the planner.** When panes are linked, does one
   pane drive and the others follow through `SetViewportToCenterAndResolution`?
   Do the followers' planners then see a jump, or copied motion? This
   decides whether prediction works in the following panes.
2. **Memory budget.** Is it a fixed budget split across the panes, or does
   it grow with the number of panes? This needs measurements from slice 5.

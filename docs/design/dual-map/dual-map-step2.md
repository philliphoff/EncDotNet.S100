# Dual map, step 2: a dataset subset per pane

> Status: **Draft**, round 1 (2026-11-26). The slice plan in §4 waits for
> the user's approval; no issues are filed yet.

Step 1 split the map into panes that each have their own viewport and
show the same datasets at the same time
([dual-map-architecture.md](dual-map-architecture.md), epic #1727). Step 2
lets each pane show its own subset of the loaded datasets: an S-57 cell
in one pane and its S-101 conversion in the other, or an ENC with and
without the S-102 under it.

The behaviour is set by the design pass
([dual-map-design.md](dual-map-design.md)): number chips on the Datasets
and Layer stack rows (D18), renamable panes (D19), Timeline lanes for
every pane (D20), ghost picks with a reason (D10), and `map` on
`set_dataset_state` (D28). Display settings stay global (D26). This note
covers the model and the services underneath, and plans the slices.

## 1. Findings

### 1.1 Visibility is one flag on `DatasetEntry`

`DatasetEntry.IsVisible` is the row's eye. `CreateMapDataset` folds it
with `IsHiddenByDisplay` into `MapDataset.IsVisible`
(`ViewModels/DatasetsViewModel.cs:952`). `DatasetLoaderService` hands
that one `MapDataset` to `MapSessionSet.SetDataset`, which gives every
map's session the same value (`Services/MapSessionSet.cs:181`) and
replays it to a session added later.

Other per-dataset state sits next to it on the entry and in
`MapDataset`: `IsActive` (the Layer stack's S-98 Active toggle, set by
display combinations), `Opacity`, the sub-layer switches, `ViewingGroups`,
`HiddenFeatures` and `PortrayalTime`. Step 2 makes only visibility per
map (§2.1).

About 20 places read or write `IsVisible`. They fall into four groups,
and each needs a different answer once panes differ:

| Group | Where | Step 2 answer |
|---|---|---|
| Policies that hide on load or on a mode | `DatasetLoaderService` (duplicate coverage `:1222`, S-104 gridded surface `:1242`), `ReceivedRoutesService` (Native/Planning), `ExchangeSetHeader` (hide all) | Every map |
| What a pane draws | the session projection, `DatasetExtentIndicatorController`, `OverscaleCurtainController`, `PickPresence` | That pane's own map |
| What the user is looking at | `LegendService`, `PositionLegendService` (DEPTHS), `AmlDisplayService` counts, `FeedbackService`, Timeline *In view* | The active map (D6) |
| Work that serves every pane | `ExchangeSetLazyLoadCoordinator` (`IsShown`, `:700`), `HostedSourceActivator` via `SourceDatasetService:116`, Timeline lanes (D20) | Any map, or per view |

### 1.2 The session already handles a hidden dataset

A session holds a hidden dataset's state and layers and disables them
(`MapsuiDatasetLayerSession.ApplyDisplayState`, `:2459`). A visibility
change counts as a composition change (`ChangesComposition`, `:2601`), so
the session rebuilds its stack. `GetLayerStackEntries` still lists hidden
and inactive datasets, so the Layer stack can show a chip to switch one
back on. Nothing in the engine assumes that two sessions agree. **The
engine needs no change for step 2.**

### 1.3 Differing panes stop sharing only the tiles that differ

A chart tile's key is `ChartTileKey.StackSlice`, a hash of the slot's
plan: the stack layers that draw there, their suppression and opacity,
and the finer coverages that clip each
(`Rendering.Engine/ChartTileKey.cs:19`). When the session builds the plan,
a hidden cell contributes neither layers nor a clipping coverage
(`ComputeOverlapSuppression`, `MapsuiDatasetLayerSession.cs:1936` →
`DrawsWithDisplayState`). So hiding a cell in one pane changes the keys
of the slots that cell draws in or clips, and no others. Tiles elsewhere
keep one key and stay shared through `TileMemoryPool` and the disk cache
(step 1 S2), and GPU textures stay shared per `GRContext` (S3).

Scenes are shared per processor whatever a pane shows (S1, S4). A
dataset hidden in a pane still binds the shared scene in that pane's
session, which is cheap, and switching it on draws at once.

One exception: S-98 interoperability composes per session, from the
datasets that are active and visible there
(`BuildLoadedDatasetInfos`, `:2190`). A pane that shows an S-102 over an
S-101 can draw the S-101 differently from a pane that doesn't. That is
correct, but the rebuilt layers may lack the stable scene key that tile
sharing needs (the §2.7 caveat of the step 1 note), and then they fall
back to per-pane tiles. Slice 8 measures this.

### 1.4 Step 1 left these seams

- `MapSessionSet` (slice 4) fans each call out to every session and
  replays recorded state to a session added later. It takes one
  `MapDataset` for all.
- `MapSessionSet.Reader` is the active map's session; picks, the Layer
  stack, entry layers and overscale read it (slice 5).
- `PickPresence.Draws` is true when the session *holds* the dataset
  (`Services/PickPresence.cs:38`). A hidden dataset is still held, so
  step 2 must check visibility instead. `PickHighlightController` doesn't
  re-run when a session's datasets change (slice 6 note).
- `PickMapPresencePresenter` takes the picked map from `MapViews.Active`,
  not from the pick (#1763). With differing panes, the "Shown in" chips
  and ghosts are wrong for a pick made over MCP on another map.
- `IMapViewportSet` gives the lazy-load coordinator one frame per view
  (slice 3), and `MapPaneSettings.Name` holds the D19 name, set only from
  MCP (slice 8, #1761).

## 2. Design for step 2

### 2.1 Per-map visibility: the model and its owner

**Visibility is per (map, dataset). The entry owns it.** `DatasetEntry`
keeps the set of maps that hide it. The set is empty by default, so a
dataset is shown in every map, which is step 1's behaviour. The Datasets
and Layer stack rows, the header checklist and MCP all bind to the
entry, so one owner keeps them in agreement.

`IsVisible` is removed (a clean break; the packages are preview) and
replaced by calls that say which map they mean:

```csharp
bool IsVisibleIn(MapViewId map);
void SetVisibleIn(MapViewId map, bool visible);
void SetVisible(bool visible);          // every map
bool IsVisibleAnywhere { get; }
event EventHandler<MapViewId?>? VisibilityChanged; // null = every map
```

Each of the ~20 consumers in §1.1 picks one of these. With one map they
all give today's answer, so the change can ship before anything else.

Everything else stays global, for every pane: `IsActive`, `Opacity`, the
sub-layer switches, `IsHiddenByDisplay`, `ViewingGroups`,
`HiddenFeatures` and `PortrayalTime` (D26; step 3 makes the last two per
map). See question Q3.

The entry knows map ids but not maps. A small `MapDatasetVisibility`
service follows `MapViews`:

- **A pane opens** (D2). For every entry hidden in the active map, it
  also hides the entry in the new map. This happens before the loader
  catches the new session up, so a dataset hidden in the active pane is
  never drawn in the new one, even for a frame.
- **A pane closes.** Its id is removed from every entry.
- **The split closes** (D3). The surviving pane keeps its own set.

Per-map visibility isn't saved. The loaded datasets aren't restored
after a restart today, so there is nothing to apply it to. A restored
split opens with every dataset shown in every pane.

### 2.2 The loader projects visibility onto each session

`MapSessionSet.SetDataset` takes a per-map projection instead of one
`MapDataset`:

```csharp
void SetDataset(Func<MapViewId, MapDataset> datasetFor, int? min, int? max);
```

The loader passes `map => entry.MapDatasetFor(map)`, which is today's
`CreateMapDataset` with `IsVisibleIn(map) && !IsHiddenByDisplay`. The set
records the function, not a value, so a replay to a session added later
asks the entry again and gets the seeded value from §2.1. A
`VisibilityChanged` from the entry calls `SetDataset` on the affected
maps only.

Nothing else in the loader changes:

- **Load.** The processor registers once and each session renders it, as
  in step 1. A dataset hidden in a pane still binds the shared scene
  there.
- **Remove and unload.** Removing a dataset removes it from every map.
  There is no "remove from this pane"; hiding does that. A lazy unload
  keeps the entry, so it keeps its per-map visibility.
- **Order.** One draw order for every pane. The Datasets list is one
  list, and moving a row moves it in every pane.
- **`ProjectSessionState`** still reads the primary session. The state it
  projects back (sub-layers, times, info, content resolution) doesn't
  depend on visibility.
- **The post-load paint wait** listens to the window's activity monitor,
  which hears every pane. A dataset shown only in a non-active pane still
  ends the wait.

### 2.3 Dataset added while split

A dataset loaded while the map is split is **shown in every pane**,
whichever pane is active. Policies that hide on load (duplicate
coverage, S-104 gridded surfaces) hide it in every pane. The cells of an
exchange set opened while split are all shown in every pane. To compare
two products, the user turns one off in each pane with the exchange-set
header's chips (§2.6).

The other choice is to show it only in the active pane, which suits a
comparison already set up but makes a fresh load look as if it failed in
the other pane. See Q1.

### 2.4 Order and the Layer stack

The Layer stack shows the active pane's composed stack, as in step 1
(`MapSessionSet.Reader`), and it lists hidden datasets too (§1.2). Its
rows get the same chips as the Datasets rows (D18). The S-98 Active
toggle stays one global switch: it is a display-combination setting, and
those stay global (D26). Dynamic sources (AIS, own ship) in the Layer
stack get no chips; they are drawn in every pane (D27).

### 2.5 Lazy loading, pre-warm and hosted sources

- **Lazy loading.** The coordinator already classifies a cell against
  every view's frame and keeps the best tier. Step 2 changes one rule:
  when it decides whether finer cells cover a coarser one
  (`RankCoverage`), it counts only finer cells shown in *that frame's*
  map. Otherwise a pane showing only S-57 cells would defer its coarse
  cells because the S-101 pane's finer cells cover them. A cell in view
  but hidden in every pane still loads, as a hidden cell in view does
  today.
- **Pre-warm.** `PortrayalCacheWarmer` portrays processors, which every
  pane shares. It needs no change.
- **Hosted sources.** `HostedSourceActivator` takes a per-view predicate
  in place of one `visible` flag. A source runs while some pane both
  shows it and has a view in its range.
- **Demand prediction.** Each pane's planner already predicts the tiles
  of its own stack. No change.

### 2.6 UI

**Chips (D18).** While split, the eye on each Datasets row becomes one
number chip per pane, in reading order. A filled chip means that pane
draws the dataset. Click toggles it in that pane; Alt-click (Option-click
on macOS) shows the dataset in that pane only. The keyboard and
automation path is the row's context menu, **Show in** › *Map 1*, *Map 2*,
*Only Map n*, *Every map*. The Layer stack rows get the same chips. With
one pane, the row shows today's eye, bound to `SetVisible`.

The exchange-set header row gets chips too, with the same click rules
over all its cells; a chip is half-filled when the pane shows some of the
cells (Q2). A received S-421 route's eye switches it between Native and
Planning display (`ReceivedRoutesService`), and routes are drawn in every
pane (D12), so a route row keeps one eye (Q4).

Each chip is a `ToggleButton` with an automation id
(`Datasets.Row.Chip`, scoped by row and pane number), a name ("Map 2"),
and a tooltip such as "Shown in Map 2. Click to hide it there.
Alt-click to show it only there."

**Header count and checklist (D18).** While split, each pane header shows
"*n* of *m* datasets" for its own map. Clicking it opens a checklist for
that pane. With an exchange set loaded, *m* can be in the hundreds, so
the checklist lists each exchange set as one tri-state row, then the
loose datasets (Q5). It ends with **Open Datasets**, which opens the
left dock's Datasets tab.

**Rename (D19, #1761).** Double-click the header name to edit it in
place. Enter commits, Esc cancels, and an empty name restores "Map *n*".
The keyboard path is a **Rename** item on the pane's split menu. The
name is saved with the layout (slice 8 already saves it).

**Picks (D10, D11).** `PickPresence.Draws` becomes "the map's session
holds the dataset, and it is active and visible there". A map that hides
the dataset while another draws it shows the ghost tagged "S-57 hidden
in this map" (`SplitMap_GhostHiddenFormat`, already in place). Each
pane's pick highlight re-runs when its own session's layers change, so a
toggle updates the outline or ghost at once. Object information's "Hidden
in Map 2" chip already selects the dataset row. Picking queries the
session of the pane picked in, which the press made active.

**Timeline (D20).** A lane is shown for every time-varying dataset shown
in any pane. Each lane label carries the chips of the panes that draw
it. A lane the active pane doesn't draw is dimmed but stays in place, so
switching panes doesn't reflow the dock. *In view* uses the active pane
(D6).

**Overlays that follow the view (D27).** Extent indicators, the
overscale curtain and Legend hover-to-find rings work per pane, from
that pane's own visibility. The Legend, DEPTHS, AML product counts and
feedback reports follow the active pane, and refresh on `ActiveChanged`.
Search keeps reading the active pane's session (D6). Route checks and UKC
use every loaded dataset whether it is shown or not, as today.

### 2.7 MCP

From D28:

- `set_dataset_state` gains `map`. With `map`, `visible` applies to that
  map only. Without it, `visible` applies to every map, as the eye does
  with one pane (Q6). `opacity` and `order` are global, so a request
  that pairs them with `map` returns `invalid_argument`. The result gains
  `shownIn: ["map-1", …]`.
- `list_maps` gains `datasets: { shown, total, hidden: [ids] }` per map.
  It lists the hidden ids because they are usually the shorter list.
- `list_datasets` gains `shownIn` for each dataset.
- `get_timeline_state` lanes gain `shownIn`.
- `get_legend` and `get_depth_basis` gain an optional `map`, defaulting
  to the active map.
- `pick_features`' `shownIn` now reports `dataset_hidden` for real.

`s100 mcp serve` has one map and refuses `map`, as in step 1.

### 2.8 Memory and CPU cost

The cost of differing panes is the tiles they don't share. Scenes,
portrayal results and processors stay shared (§1.3).

- **Panes that differ in a few cells** pay only for the slots those cells
  draw in or clip. Elsewhere a second pane adds about 1 MB, as in step 1
  (S2 measured +1 MB against +12 MB for a pane with its own tiles, at one
  view of four NOAA cells).
- **Panes with disjoint content over the same area** (S-57 in one, S-101
  in the other) share almost no chart tiles. They cost about what two
  unshared maps cost: twice the rasterising, and tile and texture bytes
  that grow with the number of panes against one `TileMemoryPool`
  budget. This is the common comparison case.

So step 1's open question 2 (a fixed budget split across panes, or one
that grows with them) becomes real in step 2. Slice 8 measures the
disjoint case before anyone changes the budget.

## 3. Risks

- **Interoperability keys.** Per-pane S-98 composition may produce
  layers without stable scene keys, which then use per-pane tiles
  (§1.3). That is correct but costs memory. Slice 8 checks it.
- **Seeding order.** If the new pane's session catches up before its
  visibility is seeded, it draws hidden datasets for a frame and rasters
  tiles it then drops. §2.1 seeds first; slice 2 tests it.
- **Events from the primary only.** `MapSessionSet` forwards
  `LayersChanged` and render events from the primary session. Per-pane
  consumers (pick highlight, extent indicators) must subscribe to their
  own session.
- **Silent "active map" assumptions.** Some `IsVisible` readers may be
  missed in §1.1. Removing the property makes the compiler find them all.
- **Test churn.** `DatasetEntry` fakes, `IDatasetLoaderService` fakes and
  the MCP state tests change in slices 1, 2 and 7.

## 4. Slices for step 2

Each slice ships on its own and keeps the viewer working with one pane
and with a split.

0. **Prerequisites from step 1.**
   - **0a. Carry the picked map to the presenter** (#1763). *Done when*
     the picked map travels with the pick to `PickMapPresencePresenter`,
     and a test picks on the non-active map of two fake panes that differ
     and checks `shownIn`, the ghost and the reporting map. *Needs*
     nothing.
   - **0b. Rename a pane from its header** (#1761, D19). *Done when* as
     in the issue, plus the **Rename** item on the split menu. *Needs*
     nothing.
1. **Per-map visibility model.** `DatasetEntry`'s per-map API (§2.1),
   `MapDatasetVisibility` (seed on open, prune on close, keep on
   unsplit), and every `IsVisible` consumer moved to an explicit choice
   per §1.1. With one map, nothing changes. *Done when* unit tests cover
   seed, prune, keep and the hide-everywhere policies, and the full
   viewer suite passes. *Needs* nothing.
2. **Project visibility per session.** `MapSessionSet.SetDataset` with a
   per-map projection and replay, seeding before catch-up,
   `PickPresence.Draws` on visibility, the pick highlight re-running on
   its own session's changes, and extent indicators and the overscale
   curtain from their own map. *Done when* `DatasetLoaderSplitMapTests`
   hides a dataset in one of two sessions and checks both snapshots; a
   pane opened from a pane that hides a dataset never shows it visible;
   a ghost test passes with two fake sessions; and an engine test shows
   that two owners whose stacks differ by one cell share the keys of
   every slot that cell doesn't touch. *Needs* 1.
3. **Services that serve every pane.** Per-frame coverers in lazy
   loading, the per-view predicate in hosted-source activation, and the
   Legend, DEPTHS, AML counts and feedback following the active pane and
   refreshing on `ActiveChanged`. *Done when* lazy-load tests with two
   frames show a coarse cell deferred in one pane and loaded for the
   other, and the active-pane consumers have tests that switch the
   active map. *Needs* 1.
4. **Chips on Datasets and Layer stack rows** (D18). Click, Alt-click,
   the **Show in** menu, the exchange-set header chips, and the one eye
   for route rows. *Done when* headless view tests drive the chips by
   automation id, the accessibility guard and live-name tests pass, and
   a live check shows an S-57 cell in Map 1 and its S-101 conversion in
   Map 2 at Shilshole, with a pick ghost. *Needs* 1 and 2.
5. **Header count and checklist** (D18). *Done when* the count follows
   toggles in every pane, the checklist groups exchange sets with
   tri-state rows, and view tests cover it. *Needs* 1 and 2.
6. **Timeline lanes across panes** (D20). Lanes for every pane, chips on
   lane labels, dimmed lanes the active pane doesn't draw. *Done when*
   a view test switches the active pane and the lane order doesn't
   change. *Needs* 1.
7. **MCP and docs.** §2.7, `docs/mcp-server.md`, `docs/whats-new.md`,
   and the Viewer README. *Done when* `SplitMapMcpToolsTests` covers
   `set_dataset_state` with and without `map`, `list_maps.datasets`,
   `list_datasets.shownIn`, the refused `opacity` with `map`, and a
   `dataset_hidden` pick. *Needs* 2; its Legend and Timeline parts follow
   3 and 6.
8. **Measure differing panes.** Over MCP, with S-57 in Map 1 and S-101
   in Map 2 at Shilshole (and an S-101 over S-102 case for
   interoperability), record tile and GPU bytes, cold-paint time and pan
   frame times against matching panes. *Done when* the numbers are in
   this note and step 1's budget question has an answer or an issue.
   *Needs* 7.

Slices 0a, 0b and 1 can run in parallel. After slice 1, slices 2, 3 and
6 can run in parallel; slices 2 and 3 both touch `DatasetLoaderService`
lightly, so the second to merge rebases. Slices 4 and 5 need slice 2 and
can run in parallel. Slice 7 needs slice 2, and slice 8 needs slice 7.
#1762 (the `set_viewport` result) isn't needed for step 2 and can run at
any time.

## 5. Open questions

These need the user's answer before the slices are filed. Each has a
recommendation.

- **Q1. A dataset loaded while split.** Shown in every pane
  (recommended, §2.3), or only in the active pane?
- **Q2. The exchange-set header.** Chips that act on all the set's cells
  in one pane (recommended), or one eye that acts on every pane?
- **Q3. What else is per pane.** Only visibility (recommended). Opacity,
  sub-layer switches (for example S-111 arrows) and the S-98 Active
  toggle stay global. Should any of them be per pane in step 2?
- **Q4. Received S-421 routes.** Keep one eye that switches Native and
  Planning display for every pane (recommended), since routes are drawn
  in every pane?
- **Q5. The header checklist with exchange sets.** One tri-state row per
  exchange set, then the loose datasets (recommended), or every cell?
- **Q6. `set_dataset_state` without `map`.** Every map, as the eye with
  one pane (recommended), or the active map, as the other map tools
  default (D4)?

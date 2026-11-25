# Design brief: split map in SoundCharts

Status: pre-design (2026-11-23). This is input for a design pass. The pass
should produce mocks in the SoundCharts design canvas
(<https://claude.ai/artifact/2hWbgQ9wKM1gC2gM51mSCs>), on a new **Split
map · design pass** artboard on the *Chart display* page. Mock
alternatives where this brief asks for them. The pass should also write a
design note, `dual-map-design.md`, that answers each question in §5.

The services and engine changes are in
[dual-map-architecture.md](dual-map-architecture.md). It describes what is
cheap and what is hard. The design pass decides behaviour, and the
architecture note changes to match.

## 1. What we're adding

SoundCharts shows one map. The split map shows two or more map panes in
one window, and each pane is panned and zoomed on its own. It ships in
steps:

| Step | Panes differ in |
|---|---|
| 1 | Viewport only. Same datasets, same time, same display settings. |
| 2 | Which loaded datasets each pane shows. |
| 3 | Each pane's timeline time. |

Step 1 is the one being built next. Mock steps 2 and 3 too, so that step
1's layout and controls can take them without rework (§5 Q8–Q9).

## 2. Who uses it, and for what

- **A skipper checking a harbour approach** at low water and at high
  water, side by side, with water level adjustment on. This needs step 3.
- **A passage planner** keeping an overview of the whole route in one pane
  and the tricky entrance zoomed in the other. Step 1 covers this.
- **A chart user comparing products**, such as S-57 and S-101 of the same
  area, or an ENC and the S-102 bathymetry under it. This needs step 2.
  Users have asked for S-57/S-101 comparison.
- **A weather user** comparing a forecast at two times. This needs step 3.
- **An agent over MCP** setting up the same comparisons.

## 3. Decisions already taken

- **Side by side first.** Step 1 ships two panes left and right. Other
  layouts may follow (stacked, a grid, a pane in its own window on a
  second display). Neither the services nor the design may assume two
  panes or a left/right arrangement. Mock the side-by-side layout, and say
  how the design extends to others.
- **Linked viewports are an optional toggle.** Panes are independent by
  default. The link keeps them in step.
- **One active pane.** The user's last-used pane is active. The design
  decides what counts as use, how it shows, and what follows it.
- **Display settings stay global for now.** Palette, display category,
  viewing groups and text groups apply to every pane. Q10 asks whether
  anything should differ.
- **Not for navigation**, like the rest of SoundCharts.

## 4. What exists in the viewer today

The mocks should look like they belong. Each of these is drawn over the
one map, or reads from it.

- **Over the map:**
  - the scale bar and the Time HUD (bottom)
  - the route-check legend and the AML CLB chip
  - the top-left stack, with the Measure and Range & bearing cards
  - the right-hand overlay column: the mode buttons, the Legend button,
    zoom in, zoom out, zoom to extent, and the compass rose
- **The status bar:** cursor lat/lon, scale and overscale, and the
  **DEPTHS** pill with its flyout.
- **The bottom dock:** the Timeline, with lanes per layer, *Live* and
  *Pinned*, and time window presets including *In view*. Other tabs are
  Clearance and Logs.
- **The right dock:** Object information (pick report), Legend, Datasets
  and the Layer stack, Display controls, Routes, Navigational warnings and
  the Library.
- **Map overlays that follow the view:**
  - pick highlight and cursor echo
  - legend hover-to-find rings
  - extent indicators for out-of-scale datasets
  - Library coverage outlines
  - the overscale curtain and the adjusted-area edge
  - tide stations
  - AIS and own ship
  - routes and route-check findings
- **Keyboard:** timeline keys, route editing, Measure, and Range &
  bearing. Each works while the map has focus.

## 5. Questions for the design pass

Answer each with a recommendation. Where the brief asks for alternatives,
mock at least two.

1. **Turning it on and off.** Where does the user split the map and close
   a pane: the View menu, a map overlay button, a toolbar? What does a new
   pane show first: the active pane's view, the dataset extent, or the
   last view used in that pane? What happens to a closed pane's view?
2. **The active pane.**
   - What makes a pane active: a click, a scroll or pinch, or hover?
   - How does it show: an accent edge, a header, or dimming the others?
     *Mock alternatives.*
   - What follows it: the Timeline's *In view* preset, the Legend, the
     DEPTHS pill, the status bar's scale, zoom to extent, and the keyboard?
   - Should the status bar's cursor lat/lon follow the hovered pane
     instead of the active one?
3. **Pane chrome or window chrome.** Each pane needs some of today's map
   overlays, and the others stay once per window. Which go where? Consider
   the scale bar, compass, zoom buttons, Time HUD, mode buttons, Legend
   button, route-check legend and the top-left cards. Does each pane need
   a header, with a name, a close button and the link toggle? Or is the
   chrome on the map enough? *Mock alternatives.*
4. **Picks across panes.** A pick fills one Object information report.
   - Is the selected feature outlined in every pane, or only in the pane
     where it was picked?
   - The feature may not be drawn in another pane. In step 1 it can be
     off screen. In steps 2 and 3 its dataset may be hidden in that pane,
     or the feature may not exist at that pane's time. What does that pane
     show: nothing, a marker at the location, or a note?
   - With different times (step 3), does Object information show the
     values at the active pane's time, or both?

   *Mock alternatives.*
5. **Tools across panes.** Measure, Range & bearing, and route editing.
   - Can a tool be used in any pane, or only the active one?
   - Is its result drawn in every pane where it's in view?
   - Can a measurement start in one pane and end in another? (Probably
     not, but say so.)
   - Routes are data, not a tool, so they're drawn in every pane. Where is
     a route edited?
6. **Linked viewports.**
   - Where is the toggle?
   - What does the link keep in step: the centre only, or the centre and
     the scale? Do different scales need an outline of one pane's extent
     drawn in the other (overview and detail)?
   - How does a pane show that it's linked?
   - Is there a cursor echo, a ghost crosshair at the same position in the
     other panes?

   *Mock alternatives.*
7. **Narrow windows and docks.** What's the smallest useful pane width?
   What happens when the window is too narrow for two panes, or the right
   dock is open over the map? Is split disabled, or are the panes stacked?
8. **Step 2: a different dataset subset per pane.**
   - How does the user choose which panes show a dataset? Options include
     per-pane visibility in the Datasets list or the Layer stack, a layer
     list in each pane, or a "show in" menu on a dataset. *Mock
     alternatives.*
   - How are panes named so that the choice reads well: A/B, numbers,
     user-given names, or named after their content?
   - What does the Timeline show when panes have different datasets?
9. **Step 3: a different time per pane.**
   - How does the Timeline show two or more times: one cursor per pane,
     or the active pane's time only?
   - Does each pane get its own Time HUD?
   - Is there a time link, and a fixed offset such as "+6 h 12 m",
     separate from the viewport link?
   - How do *Live* and *Pinned* work per pane?
   - The low-water/high-water case: can the user say "this pane at the
     next low water, that pane at the next high water"?

   *Mock alternatives.*
10. **Display settings.** Should anything be per pane, such as the
    palette, the display category or S-102 relief? Assume not unless a
    use case in §2 needs it. Per-pane display settings are costly.
11. **Overlays that follow the view.** For each overlay in §4, say whether
    it is drawn in every pane, only in the active pane, or in each pane
    from that pane's own view. Library coverage and the overscale curtain
    are examples.
12. **MCP.** Tools gain an optional map id, and `list_maps` and
    `set_active_map` are added. Mock the agent-facing names only if the
    design changes them.

## 6. Out of scope

- Per-pane palettes and display categories (Q10 asks only whether they're
  needed).
- Building layouts beyond side by side. The design should allow them, but
  mock them only to show that it does.
- Rendering performance and memory, which the architecture note covers.

## 7. Fixtures

Use Puget Sound with NOAA ENCs and tide predictions. A good scene is the
approach to Shilshole Bay Marina and the Ballard Locks, with Seattle
9447130 for the tide, and water level adjustment *At view time*:

- **Step 1:** one pane on central Puget Sound at 1:250,000, and one on the
  marina entrance at 1:12,000.
- **Step 3:** both panes on the entrance, one at the next low water and
  one at the next high water.
- **Step 2:** S-57 in one pane and S-101 in the other, over the same
  area. If there's no S-101 for Puget Sound, use the Solent sample data.

Times, heights and dataset names in the mocks are fixtures. Label them as
such where it matters.

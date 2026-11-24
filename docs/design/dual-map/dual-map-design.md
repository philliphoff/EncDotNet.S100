# Split map: design pass

Status: **Accepted** (2026-11-24). The user resolved O1–O4 the same day
(see the end). Answers Q1–Q12 of the
[brief](dual-map-brief.md). The services and engine side is in
[dual-map-architecture.md](dual-map-architecture.md); its §2.4 and §3
now follow the decisions below.

The mocks are the **Split map · design pass** artboard on the *Chart
display* page of the SoundCharts design canvas
(<https://claude.ai/artifact/2hWbgQ9wKM1gC2gM51mSCs>,
`project/Split-Map.dc.html`), to the right of *Tide predictions · design
pass*. Frame ids below (1a, 4b, …) are the black tags on the artboard.

The window chrome follows a capture of the current viewer (Release build,
2026-11-24, NOAA cells US3WA46M, US4WA11M, US5WA12M and US5WA13M at
Shilshole, 1:12,000). Three things in that capture shaped the design:

- The scale bar is at the **top** centre of the map, not the bottom.
- The overlay column wraps: the mode buttons (Pick, Measure, Range &
  bearing, Edit route, Display settings, Legend) are the outer column, and
  zoom in, zoom out, zoom to extent and the compass are the inner one.
- The right dock (Object information, Legend, Reference) opens **over**
  the map; it doesn't narrow it. Datasets is in the left dock.

The coastline and the marina are schematic. Soundings, light
characteristics, the S-124 warning, the converted S-101 cells and the tide
times and heights (Seattle 9447130, Thu 26 Nov 2026: low water 08:06
0.4 m, high water 14:18 3.3 m) are fixtures.

The screenshots are a plain local render of the artboard's HTML, not the
canvas viewer.

## Frames

| Frame | Question | What it shows | Screenshot |
|---|---|---|---|
| Header, 1a, 1b | 1 | Decisions; step 1 overview and detail; title-bar button and menu, macOS View menu, a map button (rejected) | [00-header-q1.png](screenshots/00-header-q1.png) |
| 2a–2c | 2 | Active pane: header accent (recommended), accent edge, dimming | [02-q2-active-pane.png](screenshots/02-q2-active-pane.png) |
| 3a, 3b, table | 3 | Pane header (recommended), chip on the map; what is pane chrome and what is window chrome | [03-q3-chrome.png](screenshots/03-q3-chrome.png) |
| 4a–4d | 4 | Ghost of a pick hidden in a pane (step 2); outline in every pane (step 1); nothing / ghost / note; a warning in force in one pane's time only (step 3) | [04-q4-picks.png](screenshots/04-q4-picks.png) |
| 5a | 5 | A measurement from the overview to the detail | [05-q5-tools.png](screenshots/05-q5-tools.png) |
| 6a–6c | 6 | Chain in the headers (recommended), chain on the splitter; what the link keeps | [06-q6-link.png](screenshots/06-q6-link.png) |
| 7a | 7 | A window too narrow for two panes | [07-q7-narrow.png](screenshots/07-q7-narrow.png) |
| 8b (and 4a) | 8 | Number chips on Datasets rows (recommended), a list from the header, a "Show in" menu | [08-q8-step2.png](screenshots/08-q8-step2.png) |
| 9a, 9b | 9 | Low water and high water side by side with the Timeline; one cursor only (rejected); tide events; Live with an offset | [09-q9-step3.png](screenshots/09-q9-step3.png) |
| Q10–Q12 | 10–12 | Overlays table, MCP names, other layouts | [10-q10-q12.png](screenshots/10-q10-q12.png) |

## Summary

- The split is switched from the **title bar** and closed from each
  pane's **header**. Any pane can be closed.
- Each pane gets a 26 px header while the map is split. It holds the
  number, name, link toggle and close, and later the pane's dataset count
  and time.
- **Active pane:** the last one pressed, scrolled, focused or used with a
  tool. Its header has an accent line. The Legend, DEPTHS, the status
  bar's scale, *In view* and the map keys follow it.
- **A pick is outlined in every pane that draws the feature.** A pane
  whose view contains the location but doesn't draw the feature shows a
  dashed grey ghost with a tag that gives the reason. Off screen, nothing.
- **Tools work in any pane.** Their state is in positions, so results are
  drawn everywhere, and a measurement can span panes.
- **The viewport link is off by default.** It keeps the centre and the
  scale ratio. A pane outlines a smaller pane's extent whether linked or
  not. The cursor is echoed in the other panes.
- **Step 2:** number chips on each Datasets row say which maps draw it.
- **Step 3:** one numbered cursor per pane on the Timeline, a time link
  with an offset, Live/Pinned per pane, and "next low/high water" jumps.
- **Display settings stay global.**

## 1. Turning it on and off

**D1. The split is switched from the title bar.** A split button sits in
the title bar's right cluster, before Feedback and the theme toggle. It
toggles the split; a small menu beside it has *Split map*, *Link views*,
*Close other maps* and *Swap maps*. On macOS, View › Split Map (⌘\\) and
View › Link Map Views (⌘⇧\\) join the existing view items. MCP has
`set_split_map`. *(Frames 1a, 1b.)*

The split is a window layout, not a map tool, so it goes in window
chrome. The title bar is the only window chrome on every platform:
Windows and Linux have no View menu. A seventh button in the map's
overlay column was rejected. The column already wraps on a 370 px map
(legends round 4), and the button would either repeat in every pane or
sit in only one.

**D2. A new pane opens at the active pane's view**, the same centre and
scale. The user then pans or zooms it away. The dataset extent was
rejected because it throws away where the user was looking. "The last
view in that pane" was rejected because there is no such view the first
time, so the pane would behave two ways. The split, the panes and each
pane's view (keyed by `MapViewId`) are restored after a restart, as the
window placement already is. *(Frame 1a.)*

**D3. Every pane has a close button, and any pane can be closed.** The
others keep what they show. Turning the split off from the title bar
keeps the active pane and closes the rest. A closed pane's view is
forgotten. Letting only "the first pane" survive would be wrong when the
user is looking at the detail in the right-hand pane and wants to keep
it. *(Frames 1a, 3a.)*

## 2. The active pane

**D4. A pane becomes active when it is used:** a press (left, right or a
drag start), a scroll or pinch zoom, keyboard focus moving into it, or a
tool click in it. Hover doesn't count. With hover, moving the pointer
across one pane on the way to the left or right dock would flip the
Legend, DEPTHS and the status bar. An MCP call with a `map` argument
doesn't change the active pane either; only `set_active_map` does, so an
agent doesn't pull focus from the user.

**D5. The active pane's header is white, with a 2 px accent line along
its top and a filled number chip.** The other headers are grey with a
grey chip. *(Frame 2a.)* This is the same cue as the selected dock tab
(TIMELINE, OBJECT INFORMATION). Two alternatives were mocked:

- *An accent edge around the pane (2b).* It competes with the chart's
  blue depth areas, magenta boundaries and blue selection, and it still
  needs a header for the name and close button.
- *Dimming the other panes (2c).* Rejected. It changes the chart's
  colours, so the inactive pane is no longer a faithful chart. That
  defeats the S-57/S-101 and low/high-water comparisons, and it reads as
  "disabled".

**D6. These follow the active pane:**

- the Legend tab (products in the active pane's view)
- the Timeline's *In view* preset
- the DEPTHS pill and its flyout
- the status bar's scale and overscale
- Search's "in view" scope
- tide-station choice for depth readouts
- zoom-to from panels (route, vessel, dataset, search result)
- map keys (timeline keys, route editing, Measure, Range & bearing)

Zoom to extent is a button in each pane, so it acts on its own pane.
While split, the status bar's scale and DEPTHS group starts with a small
"Map 2" badge. *(Frame 1a.)*

**D7. The status bar's lat/lon follows the pane under the pointer, not
the active one.** It is prefixed with the pane number ("Map 1 ·
47°40.6′N …") while split, and it is blank when the pointer is off the
maps, as today. A position is about where the pointer is. Showing the
active pane's position while the pointer is in the other pane would be
wrong.

## 3. Pane chrome and window chrome

**D8. Anything that describes a view is pane chrome; anything that is a
mode, a global setting or about the data is window chrome, placed
relative to the whole map area.** *(Frame 1a, table in §3.)*

| Element | Where |
|---|---|
| Header (number, name, link, close) | Pane, only while split |
| Scale bar (top centre) | Pane |
| Zoom in, zoom out, zoom to extent, compass | Pane (inner column) |
| Map stamp (Display Not Real Time) | Pane |
| Mode buttons: Pick, Measure, Range & bearing, Edit route | Window: the outer column at the right edge of the map area |
| Display settings and Legend buttons | Window, same column |
| View pill (display combination) | Window, top right of the map area |
| Measure / Range & bearing cards | Window, top left of the map area |
| Route-check legend card, AML CLB chip, route edit chip, warning note | Window |
| Time HUD | Window: one HUD, in the active pane (bottom centre) |

In a side-by-side split, the rightmost pane gets both columns, exactly
as today's wrapped layout. The other panes get only the zoom column. The
Time HUD moves with the active pane because it controls that pane's time
in step 3. Putting it in the centre of the map area would straddle the
splitter.

**D9. Each pane gets a 26 px header while the map is split.** From left
to right it holds the number chip and the name, then step 2's "2 of 4
datasets" and step 3's time chip, then the link toggle and the close
button. With one pane, the header goes away and the window looks exactly
as it does today. *(Frame 3a.)* A floating chip on each map (3b) saves
26 px. But it sits where the tool cards, the map stamp and the AML chip
already go, and step 3's time would need a second chip.

## 4. Picks across panes

**D10. A pick is outlined in every pane that draws the picked feature.**
A pick is about a feature, not a pane. *(Frame 4c.)* When the feature
isn't drawn in a pane:

- **The location is off screen** (step 1). That pane shows nothing.
  Panning there shows the outline, as it does today.
- **The location is in view, but the feature isn't drawn.** In step 2 its
  dataset is hidden in that pane; in step 3 it doesn't exist at that
  pane's time. That pane draws a **ghost**: the same outline, dashed and
  grey, with a small tag that gives the reason. Examples are "S-57 hidden
  in this map" and "Not in force at 08:06". *(Frames 4a, 4d.)*

This answers the user's question. If a highlight shows in one map and
nothing shows at the same place in the other, it looks like a bug, and in
a comparison tool that is the worst outcome. The ghost says that the
feature is there and that this map doesn't draw it, and why. Two
alternatives were mocked in 4b: nothing, and a note in the pane header.
The header note says something differs but not where.

In step 2, the S-57 light and its S-101 copy are different features. A
pick in the S-57 pane picks the S-57 light; the S-101 pane shows the
ghost, not the S-101 light. Matching equivalent features across products
is out of scope.

**D11. Object information reports at the picked pane's time and says
where the feature is shown.** Under the title, chips read "Shown in
Map 1" and "Hidden in Map 2"; clicking a hidden chip selects the dataset
row. In step 3, values that change with time (water level, current,
whether a warning is in force) get an **Other maps** row with the value
at each other pane's time. The skipper then reads 0.4 m and 3.3 m in one
place. *(Frames 4a, 4d.)*

## 5. Tools across panes

**D12. Tools work in any pane, and their results are drawn in every pane
where they are in view.** *(Frame 5a.)*

- Measure, Range & bearing and Edit route are window modes (D8). A click
  in any pane goes to the one tool, and using a tool in a pane makes it
  active (D4).
- Points are positions, not pixels, so **a measurement can start in one
  pane and end in another**. Each click adds a point in whichever pane it
  lands, and the rubber band follows the pointer in that pane. The brief
  expected "probably not". But with an overview and a detail it is the
  natural way to measure from far away to an exact point, and the state
  is already geographic, so it costs nothing extra. Accepted (O2).
- There is still one measurement and one range and bearing, with one
  card at the top left of the map area.
- **Routes are drawn in every pane.** In Edit route mode a waypoint can
  be added or dragged in any pane, and the edit shows live in all of
  them.

## 6. Linked viewports

**D13. The link toggle is a chain button in each pane header.** It is
off by default. In step 1 there is one link state for the window: the
chain in any header, *Link views* in the split menu, or ⌘⇧\\ turns it on
for all panes, and each linked header reads "Linked". *(Frame 6a.)* With
more panes later, the same chain can become per pane ("link this pane")
without moving. A chain on the splitter (6b) is clear for two panes, but
it doesn't extend. A 2 × 2 grid has four splitters and no single place
for it, and a pane in its own window has none.

**D14. The link keeps the centre, and the scale ratio from the moment of
linking.** Panning one linked pane pans all of them, and zooming one
zooms the others by the same factor. One rule covers both uses
*(frame 6c)*:

- Two panes linked at the same scale stay identical. S-57/S-101 and
  low/high-water comparisons need this.
- An overview at 1:250,000 and a detail at 1:12,000 keep that ratio.
  Zoom the detail in and the overview zooms in by the same factor, and
  both stay centred on the same point.

"Centre only" breaks the comparison case, because the scales drift
apart. "Centre and scale" breaks overview and detail, because linking
would jump one pane to the other's scale. Accepted (O1).

**D15. A pane outlines the extent of any other pane that lies inside it
and is much smaller** (less than a quarter of its width), with that
pane's number in a small chip. This is drawn whether or not the panes are
linked; the passage planner's overview and detail needs it either way.
*(Frames 1a, 6c.)*

**D16. The cursor is echoed in the other panes while split.** A ghost
crosshair is drawn at the pointer's position in every other pane whose
view contains it. It is always on; it is quiet, and it is what makes two
views of the same place readable. *(Frames 1a, 6a.)*

## 7. Narrow windows and docks

**D17. A pane is at least 320 px wide, and panes that don't fit fold
rather than change layout.** *(Frame 7a.)*

- 320 px holds the two control columns, a 160 px scale bar and the
  compact Time HUD (no offset, no PINNED pill). Narrower panes would clip
  the HUD. The splitter stops at 320 px.
- When the map area is narrower than 2 × 320 + 4 = 644 px, the panes
  that aren't active fold into a 28 px strip with their number. Clicking
  a strip unfolds that pane, makes it active and folds the other. A
  folded pane keeps its view and, later, its datasets and time.
- The default 1100 × 700 window with the left dock open has a 732 px
  map area, so two 364 px panes fit. Frame 7a is a 1000 px window (a
  632 px map area), so one pane folds.
- The right dock keeps opening over the map, as it does today, so it
  never folds a pane. It covers part of the rightmost pane.
- The split is never disabled, and the panes are not stacked
  automatically. Switching to top and bottom as the window narrows would
  move the user's panes under them. Stacked can be a layout the user
  picks later.

## 8. Step 2: a dataset subset per pane

**D18. While split, each Datasets row's eye becomes one number chip per
pane.** A filled chip means the dataset is drawn in that pane. Click to
toggle; Alt-click shows the dataset only in that pane. The Layer stack
rows get the same chips. The header's "2 of 4 datasets" opens a
checklist for that pane as a shortcut. *(Frames 4a, 8b.)* Every row
then shows where it is drawn without opening a menu, and one chip per
pane extends to three or four. Two alternatives were mocked in 8b:

- *A list per pane, from its header.* It answers "what does this map
  show?", but it hides the other pane's state. It is kept only as the
  shortcut.
- *"Show in" on the row's context menu.* It is two levels deep, and the
  list doesn't show the result. It is kept only as the keyboard path.

**D19. Panes are numbered in reading order (Map 1, Map 2) and can be
renamed.** Double-click the name to rename it, for example "S-57" or
"Low water". Numbers keep the chips short. Names taken from the content
("S-101 + 1") were rejected because they change as the user toggles and
they run long. The number is a label from the layout; the `MapViewId`
stays opaque and stable.

**D20. The Timeline shows a lane for every time-varying dataset drawn in
any pane.** Each lane label carries the chips of the panes that draw it.
Lanes that the active pane doesn't draw are dimmed but kept, so switching
panes doesn't reflow the dock. *In view* uses the active pane (D6).

## 9. Step 3: a time per pane

**D21. The Timeline has one cursor per pane, each flagged with its
number and time.** The active pane's cursor is solid accent; the others
are dashed and outlined. Dragging a cursor sets that pane's time and
makes it active. *(Frame 9a.)* Showing only the active pane's cursor (9b)
was rejected. The other pane's time vanishes from the Timeline, and
setting "both at the next tide" takes two trips.

**D22. Each pane header shows its time in a chip, and the one Time HUD
controls the active pane.** There is still one HUD, following the same
rules as today: hidden while the dock shows the Timeline tab, compact on
narrow panes. *(Frames 9a, 9b.)*

**D23. Link times is separate from the viewport link.** It is a toggle in
the Timeline toolbar. While on, dragging or stepping either cursor moves
all of them and keeps their offset, which is shown as a chip between the
cursors ("+6 h 12 m"). In step 3, times start linked with a zero offset,
which is step 1 and 2's behaviour. Setting one pane's time with the link
off, then turning it on, sets the offset. *(Frame 9a.)*

**D24. Live and Pinned are per pane.** *Go live* and the HUD act on the
active pane. A pane linked to a live pane follows it at the offset: Map 1
is Live and Map 2 reads "Live +6 h 12 m", which gives "now and in six
hours". Each pane whose time isn't real time shows its own *Display Not
Real Time* stamp, because S-98 asks for it per display. *(Frames 9a,
9b.)*

**D25. The header's time chip has a menu with *Go live*, *Same time as
Map n*, *Next low water* and *Next high water*.** The tide station is the
one water level adjustment uses in that pane's view; here it is Seattle
9447130. This covers the skipper's case: this pane at the next low water,
that one at the next high water. The jumps are one-shot. After them,
stepping with the times linked keeps the fixed offset; it doesn't re-pair
low and high water (O4).
*(Frame 9b.)*

## 10. Display settings

**D26. Display settings stay global.** No use case in the brief needs a
per-pane palette, display category or S-102 relief. Comparing an ENC with
the S-102 under it is a dataset subset (step 2), not a display setting.
Water level adjustment stays one setting, applied at each pane's own
time. The View pill (display combination) stays in window chrome.

## 11. Overlays that follow the view

**D27.** *(Q10–Q12 frame.)*

| Overlay | Drawn |
|---|---|
| Pick highlight | Every pane that draws the feature; ghost where hidden or absent (D10) |
| Cursor echo | Every pane except the one under the pointer, where the position is in view (D16) |
| Other panes' extent boxes | A pane that contains a much smaller pane's extent (D15) |
| Legend hover-to-find rings | Every pane that draws matching features. The Legend lists the active pane's products |
| Extent indicators for out-of-scale datasets | Each pane, from its own scale and view |
| Library coverage outlines | Each pane, from its own view. One legend entry, not one per pane |
| Overscale curtain | Each pane, from its own scale |
| Adjusted-area edge | Each pane, at its own time (step 3) |
| Tide stations | Each pane, values at its own time (step 3) |
| AIS and own ship | Every pane, with today's rule for a pinned time |
| Routes and route-check findings | Every pane |
| Measure, Range & bearing | Every pane where in view (D12) |
| Map stamp (Display Not Real Time) | Each pane whose time isn't now (D24) |

## 12. MCP

**D28. The names in the architecture note stay, with two additions.**
*(Q10–Q12 frame.)*

- `list_maps` returns `id` (opaque, such as `map-1`), `label` ("Map 1"),
  `name`, `active`, `linked`, `viewport` and `folded`. Step 2 adds
  `datasets`; step 3 adds `time`, `live` and `timeLinked`.
- `set_active_map { map }`.
- `set_split_map { open }`. Opening copies the active map's view (D2);
  closing keeps the active map (D3).
- **New:** `set_map_link { linked }` for the viewport link (D13), and
  `set_map_name { map, name }` (D19).
- Map tools (`set_viewport`, `pick_features`, `fling`, `render_to_image`,
  `identify_features`, `await_settled` and the rest) gain an optional
  `map`. The default is the active map, and the call never changes the
  active map (D4).
- Pick results gain `shownIn: [{ map, shown, reason }]`, where `reason`
  is `dataset_hidden`, `not_at_time` or `out_of_view` (D10).
- Step 2: `set_dataset_state` gains `map`. Step 3: `set_view_time` and
  `step_time` gain `map`, and `set_time_link { linked, offset }` is
  added.

## How it extends beyond side by side

Nothing above depends on two panes or on left and right *(Q10–Q12
frame)*:

- Numbers come from reading order. Chips, cursors and the status-bar
  badge carry numbers, never sides.
- The header belongs to the pane. In a stacked layout the splitter turns
  horizontal and the headers are unchanged. A 2 × 2 grid has chips 1–4
  and four cursors.
- The window chrome is placed against the whole map area. A pane in its
  own window would carry its header and a copy of the map-area chrome,
  while the docks stay in the main window.
- Folding (D17) is per pane, not per layout.

## Decisions

| # | Decision | Frame |
|---|---|---|
| D1 | Split from a title-bar button and menu, View › Split Map ⌘\\ on macOS, and MCP; not from a map button | 1a, 1b |
| D2 | A new pane opens at the active pane's view; the split and the views are restored after a restart | 1a |
| D3 | Any pane can be closed from its header; the others keep their views; turning the split off keeps the active pane | 1a, 3a |
| D4 | Active on press, scroll, pinch, focus or tool use; not hover; MCP only with `set_active_map` | 2a |
| D5 | Active header: white, 2 px accent line, filled number; no edge, no dimming | 2a–2c |
| D6 | Legend, *In view*, DEPTHS, status-bar scale, Search, zoom-to and map keys follow the active pane | 1a |
| D7 | Status-bar lat/lon follows the pane under the pointer, prefixed with its number | 1a |
| D8 | View elements are pane chrome; modes, global settings and data chips are window chrome; one Time HUD in the active pane | 1a, table |
| D9 | A 26 px header per pane while split | 3a, 3b |
| D10 | A pick is outlined in every pane that draws it; a ghost with a reason where it is in view but not drawn; nothing off screen | 4a–4d |
| D11 | Object information at the picked pane's time, with "Shown in" chips and an "Other maps" row | 4a, 4d |
| D12 | Tools in any pane; a measurement may span panes; results and routes drawn everywhere | 5a |
| D13 | Chain toggle in each header; one window-wide link state in step 1; off by default | 6a, 6b |
| D14 | The link keeps the centre and the scale ratio | 6c |
| D15 | Extent boxes of much smaller panes, linked or not | 1a, 6c |
| D16 | Cursor echo, always on while split | 1a, 6a |
| D17 | 320 px minimum; inactive panes fold to a strip; no automatic stacking; the right dock stays an overlay | 7a |
| D18 | Step 2: number chips per pane on Datasets and Layer stack rows; header checklist shortcut | 4a, 8b |
| D19 | Panes numbered in reading order, renamable; ids opaque | 3a, 8b |
| D20 | Step 2 Timeline: lanes for every pane's datasets, tagged and dimmed | — |
| D21 | Step 3 Timeline: one numbered cursor per pane | 9a, 9b |
| D22 | Step 3: time chip in each header; one HUD for the active pane | 9a |
| D23 | Step 3: Link times with an offset, separate from the viewport link | 9a |
| D24 | Live/Pinned per pane; a linked pane follows a live one at its offset; stamp per pane | 9a, 9b |
| D25 | Step 3: next low / next high water from the time chip | 9b |
| D26 | Display settings stay global | — |
| D27 | Overlays per the table in §11 | Q10–Q12 |
| D28 | MCP: `list_maps`, `set_active_map`, `set_split_map`, `set_map_link`, `set_map_name`, optional `map` on map tools, `shownIn` on picks | Q10–Q12 |

## Resolved questions

Answered by the user on 2026-11-24.

- **O1. Link: scale ratio or same scale (D14).** The ratio. Linked panes
  keep the centre and the scale ratio from the moment of linking.
- **O2. Measuring across panes (D12).** Yes. A measurement's points can
  come from different panes.
- **O3. The split button in the title bar (D1).** Accepted.
- **O4. Stepping in step 3 (D25).** A fixed offset. After the low/high
  water jumps, stepping with the times linked keeps the offset; it
  doesn't move to the next low/high pair. A fixed offset slowly stops
  being "low against high" (the pair drifts about 50 minutes a day); the
  user re-jumps from the time chip when needed.

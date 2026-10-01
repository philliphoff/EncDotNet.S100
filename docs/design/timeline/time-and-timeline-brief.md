# Design brief: time, "now" and the Timeline at collection scale

Status: brainstorm / pre-design (2026-10-01). Input for a design pass that should produce mocks of refinements and alternatives.

## 1. Why now

The Timeline was built for one or two time-varying files with a few dozen samples (S-104/S-111 demo sets). The Library can now bring in data with a lot more time in it:

- NOAA S-111: 14 forecast models, a new run every 6 h (some daily), each about 2 days long, with the latest run kept per model (#685, #702).
- NOAA S-104: a pilot today, but the same mechanics will produce many stations and runs.
- Exchange sets such as the Rotterdam NL S-111 set: about 8 months of range, made of a few clusters separated by multi-month gaps.
- S-411 ice: weekly snapshots, each valid until the next one.
- Live sources: AIS and own ship are always "now".

Slice 4 of #685 (#703) added a Now marker, a Now button, "forecast ended" and run names. That fixed the most urgent problem: forecasts age. But the model underneath is still *one slider over the union of samples from whatever happens to be loaded*. This brief sets out everything the design has to cover, so that the next pass looks at the whole system rather than patching the slider.

## 2. What exists today (measured from code)

| Area | Today | Where |
|---|---|---|
| Clock | One global view time for the map session. It is clamped to `[min, max]` of the **loaded** time-aware datasets. | `GlobalTimeService`, `MapsuiDatasetLayerSession.SetCurrentTime` |
| Range | Min/max over loaded datasets only. Temporal extents known to the Library (catalogue `temporalExtent`, S-111 run windows) never reach the Timeline. | `MapsuiDatasetLayerSession` |
| Axis | A gap-collapsing axis. Data spans are proportional, with a 2 % floor. **Every gap gets the same width**, so a 3-hour gap and an 8-month gap look identical. | `TimelineAxisMap` |
| Ticks | One tick per real sample up to 50 samples, with snapping. Above 50, 10 evenly spaced decorative ticks and free scrubbing. | `TimelineViewModel.Ticks` |
| Step ‹ › | Walks the union of all samples. **Disabled whenever there are more than 50 samples** (`CanStep*` requires snapping), which covers almost any real forecast collection. | `TimelineViewModel` |
| Sample union | Every dataset's samples merge into one list. With mixed cadences (hourly S-111 + 6-min S-104), stepping follows the densest dataset. | `AllSamples` |
| Per-product time policy | S-111 shows the nearest sample within about one step of its range, otherwise hides. S-411 shows the latest snapshot at or before the clock, open-ended. **Everything else (including S-104) shows the nearest sample with no limit**: data from months away renders as if current, with no indication. | `TimePolicy` |
| Now | Only when a loaded dataset *name* parses as a forecast run. Now is a one-shot jump: **the view does not follow the clock**, so an hour later the map shows the past while the marker drifts. | `TimelineViewModel.IsForecastTimeline`, `GoToNow` |
| Readout | One timestamp (Local/UTC preference), a range label ("A → B · 55 h · cbofs 12:00Z · hourly"), "forecast ended N h ago". | `TimelineView.axaml` |
| Per-dataset visibility | None. The coverage band is the merged union, so you cannot tell which dataset or model covers which stretch, or which layers have data at the current time. | — |
| Station chart | The pick-panel time series has a Now line, but it is read-only and cannot set the time. | `StationTimeSeriesViewModel` |
| Library | Knows run, valid window, a time-left bar, New run, Expired. Not connected to the Timeline. | #702 |
| Time-bearing data **not** on the clock | S-124 warning validity; S-122/S-127/S-131 date ranges; S-57 `DATSTA/DATEND/PERSTA/PEREND` and S-101 date-dependent features (seasonal buoys); S-421 route ETAs; S-129 UKC plan passing times (`S129TimelineView` exists as a data accessor); AIS/own ship (live). | — |

Bugs or quick wins found along the way, which can be fixed regardless of the redesign (filed as #706):

1. ‹ › are disabled above 50 samples (the XML doc comment says the opposite).
2. S-104 (and any future "Nearest" product) renders arbitrarily stale samples with no cue.
3. Now does not keep following the clock.

## 3. Vocabulary the design should settle

Different kinds of "time" are currently blurred together. Proposed terms:

- **Now**: wall-clock time.
- **View time** (or "chart time"): the time the map is showing. Today this is `CurrentTime`.
- **Valid time**: when a sample applies. For a forecast this is "T+14 h".
- **Issue / run time**: when a forecast or observation was produced ("cbofs 12:00Z").
- **Edition / publication time**: when a chart or dataset was published. This is about *freshness*, not *validity*.
- **Layer time**: the sample a given layer actually drew for the current view time. It can differ from view time: snapped, at-or-before, or "nearest, 3 days away".

The modes the clock can be in:

- **Live**: view time follows now, advancing on its own like a DVR "LIVE" button.
- **Pinned**: the user chose a time, and it stays there.
- **Playing**: animating across a range.

A clear "you are not live" state matters most when live sources (AIS, own ship) are on the map next to scrubbed forecast data.

## 4. Problem areas, with ideas

### A. What is "now", and staying live
- Make Live an explicit mode, not a jump. Show a LIVE / "+3 h from now" pill. Any scrub leaves Live, and one click returns.
- Show Now for every timeline, not just forecasts. Observations, ice and charts all have a relationship to now.
- Express offsets relative to now: "in 2 h 10 min", "yesterday 18:00", "T+14 h of cbofs 12Z".
- Decide what Live means when now falls in a gap or past every forecast: hold the last frame dimmed, show "no data now", or offer "Check for new runs" (the hook into Library refresh already exists).

### B. Moving through time at scale
- **Zoomable, pannable axis** (as in video editors, Grafana or weather apps), replacing the fixed [0,1] slider. Wheel or pinch zooms, drag pans, and an overview strip shows the whole extent.
- **Range presets**: Now ± 6 h, Today, Next 48 h, "This run", "All loaded", Custom. A calendar/date picker handles large jumps.
- **Step granularity**: by sample, 10 min, 1 h, 1 day, next/previous dataset boundary, next/previous data (skips gaps). Keyboard: ←/→ step, Shift for coarse steps, Home/End, N for Now.
- **Driver dataset**: when cadences differ, choose what stepping follows (the densest, a chosen layer, or a fixed interval).
- Snapping that scales: snap to real samples when they can be told apart at the current zoom, run free otherwise. Today the cutoff is a hard-coded 50.

### C. Knowing what data exists, and when
- **Lanes**: an expandable Gantt with one row per product → model/station → dataset. Collapsed, it is the current single band. Expanded, it answers "which model covers Thursday?".
- **Loaded vs available**: draw Library-indexed but unloaded windows as hollow/outline bands ("online", "on disk, not loaded"), with Get/Load from the band. This connects the Timeline to the Library's run windows and Expired state.
- **Space–time coupling**: optionally show only data that intersects the current map view, because 14 models nationwide create noise. Hovering a lane highlights its footprint on the map; tapping the map filters lanes ("what covers here, when?").
- **Density** instead of ticks: a heat strip of sample density for 10k-sample series.
- **Run stacks**: newer runs overlap older ones. Show supersession: the latest run is solid, superseded runs are ghosted or hidden behind a disclosure.

### D. Gaps
- **Gap size must be readable**. Options: a labeled break ("⋯ 4 mo ⋯"), a log-compressed width, or a zigzag break glyph. A 6-hour gap and a 6-month gap must not look alike.
- **Jump over gaps**: "next data ›" and "‹ previous data", and on hover a gap tooltip with its duration and what lies either side.
- **What the map shows inside a gap**, per product:
  - Hide.
  - Show the nearest frame ghosted with an "8 d earlier" badge.
  - At-or-before (ice).
  - A per-product staleness tolerance. This replaces the unbounded Nearest policy.
- **Gaps as opportunities**: "No currents loaded for this time. 3 runs available online [Get]".

### E. Telling users what each layer is actually showing
- **Layer time badges**: the legend or layer list shows each time-aware layer's layer time and offset ("S-104 Charleston · 12:06 (−6 min)", "S-111 cbofs · no data at this time").
- **Map time stamp**: an on-map HUD with the view time. It is included in screenshots/exports so an image is never ambiguous.
- **Pick panel**: the station or grid-cell time series is a mini timeline. Clicking the chart sets view time (today it is read-only). Grid products (S-111 tiles, S-104 grids) could also get a "series at this point" chart.

### F. Forecast runs as first-class objects
- Separate run selection from valid-time selection. "Show cbofs 06Z vs 12Z" and run comparison come later, but leave space for them.
- Browsing older runs ("last Tuesday") is explicitly *later* in #685. The lane model should accommodate it.
- Expired at the Timeline level: dim plus the "Refresh for runs" text exists. Consider a direct "Check for new runs" action in the Timeline.

### G. Playback
- Play/pause, speed (steps per second or a time multiplier), and a loop range (drag handles on the axis).
- Constraints: renders are debounced and not frame-accurate, and large tile sets are slow. The design needs a loading or "catching up" state, and maybe frame preloading over the loop range.

### H. Time-dependent chart and vector data
- ECDIS practice is to display date-dependent features (seasonal buoys, temporary features) relative to a chosen date. Should the view clock drive S-57/S-101 date-dependent display, S-124 warning validity and S-122/S-127 date ranges? Probably as an opt-in ("Apply view time to charts and notices"). Most users expect charts to be "as of today" even while scrubbing tomorrow's currents.
- If enabled, these products add *validity intervals* (not samples) to the lanes.

### I. Live sources versus scrubbed time
- When view time is not now, AIS and own ship can hide, stay at "now" with a "live, not at view time" label, or (later) replay history.
- Steering own ship (`steerable-own-ship.md`) and voyage planning (S-421 ETAs, S-129 passing times) suggest **time follows the route**: scrub along a planned voyage and see currents at each leg's ETA. This is a later use case, but the time model should not rule it out.

### J. Time zones and formatting
- Local/UTC already exists as a preference. Mariners often think in **local time at the port**, which may differ from the computer's zone. Consider a "data-area local" option, or show both times in the readout.
- Use relative and absolute times together ("Thu 14:00 · in 26 h").
- Decide on 24-hour and day-boundary labelling on the axis at different zoom levels.

### K. Placement and footprint
- Today: a bottom dock panel that auto-opens when time data appears.
- Options:
  - A compact one-line strip plus an expandable lanes view.
  - A map HUD pill (time, LIVE, ‹ ›) with the full Timeline on demand.
  - Docked and floating variants.
- On narrow windows, the HUD pill alone should be usable.

### L. Library ↔ Timeline
- A shared time filter, so the Library can show "datasets valid at view time" (a new facet beside All / Local / Online / Updates).
- Actions in both directions: from a Library row, "Show on timeline" / "Go to start of run"; from a Timeline band, reveal it in the Library.
- Expired / New run states appear in both places with the same swatches.

## 5. Scale and constraints the design must respect

- Sample counts range from 48 (one S-111 run) through tens of thousands (6-minute S-104 over months) to unbounded (live).
- Dataset counts reach dozens of models × tens to hundreds of tiles, so lanes must group: product → model/station → tiles collapsed.
- Ranges run from hours to many months, with gaps from minutes to months.
- Rendering after a time change is debounced and asynchronous, so the UI must tolerate lag between the scrub position and the drawn frame.
- It must work offline. Library-known (unloaded) windows come from cached indexes.
- Accessibility: keyboard-complete stepping and jumping. The readout and offsets must not rely on colour alone.

## 6. Scenarios to mock against

1. **Plan a departure**: the user loads cbofs, wants currents tomorrow 06:00–10:00, and steps hourly through it.
2. **Underway, live**: the map should stay on now, with S-111 currents, S-104 water level and AIS. The run expires mid-trip, and a new one arrives.
3. **Rotterdam exchange set**: 8 months of range with a few clusters. The user needs to see that clusters exist and jump between them.
4. **Mixed cadences**: hourly S-111 plus 6-minute S-104 observations, where one layer has no data at the chosen time.
5. **Nationwide noise**: 14 S-111 models loaded while viewing only Chesapeake Bay.
6. **Ice**: weekly S-411 snapshots, where "valid until the next one" behaves differently from forecasts.
7. **Stale pilot**: S-104 Charleston from December 2025, which today renders as if current.
8. **(Later)** Voyage: a route with ETAs, where scrubbing follows the ship.

## 7. Directions to explore (alternatives, not a decision)

1. **Evolved strip.** Keep one track and add:
   - a zoomable axis
   - labeled, size-honest gaps
   - Live mode
   - working ‹ › with a granularity menu
   - next/previous data
   - layer time badges in the legend

   This is the smallest change.
2. **Lanes timeline.** A compact summary track that expands into grouped lanes (product → model/station). It includes loaded and available-online bands, run supersession and map-footprint hover. This is the most informative option.
3. **Time HUD + picker.** A map-overlay pill (view time, LIVE, ‹ ›). Clicking it opens a popover with presets, a calendar with an availability heatmap and "next data". The dock Timeline becomes optional, for power use.
4. **Time as a shared facet.** View time drives the map *and* filters the Library and pick panel. Time-series charts double as scrubbers.

These combine: for example, 3 as the everyday surface, with 2 as the expanded panel.

## 8. Questions for design

1. Is Live a mode with its own visual state, and what exits it (any scrub, stepping, or only explicit actions)?
2. How should gap size be shown honestly while staying usable, and what does the map show for a layer at a time inside its gap?
3. Do lanes show only loaded data, or loaded plus Library-available data? If both, how do they look different, and what does acting on an available band do?
4. Should the Timeline's lanes or range follow the map viewport?
5. Where does per-layer "actual time shown" live: the legend, the layer list, an on-map badge, or all of them?
6. Should the view clock affect charts and notices (date-dependent features, S-124 validity)? Is it on by default?
7. How are AIS and own ship treated when not live?
8. Should the Timeline be a dock panel, a map HUD, or both, and what does it auto-open into?
9. Is playback in scope for the first pass?
10. Local time: the computer's zone, the data area's zone, or a dual display?

## 9. Requested deliverables

- Mocks (annotated `.dc.html` + `HANDOFF.md`, as in the library-panel and NOAA handoffs) for at least directions 1 and 2. Direction 3 is welcome as the compact surface.
- Each mock is shown against scenarios 2, 3, 4 and 5 in §6, in these states: live, pinned in data, pinned in a gap, every forecast expired, and loading after a scrub.
- Recommendations for §8, so engineering can split the work into slices. The §2 quick wins can ship ahead of the redesign.

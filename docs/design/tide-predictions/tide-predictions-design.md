# Tide and tidal current predictions: design pass

Status: Proposed (2026-11-11), revised 2026-11-12: recommendations awaiting
the maintainer's decisions. Revision: the station keys moved from under the
Layer stack rows to the Legend tab (maintainer's review). Answers the nine questions in the [brief](tide-predictions-brief.md)
for the design slice
[#1448](https://github.com/philliphoff/EncDotNet.S100/issues/1448) of epic
[#1447](https://github.com/philliphoff/EncDotNet.S100/issues/1447). The viewer
slices [#1453](https://github.com/philliphoff/EncDotNet.S100/issues/1453)
(tides) and [#1454](https://github.com/philliphoff/EncDotNet.S100/issues/1454)
(currents) build from the decisions at the end.

The mocks are the **Tide predictions · design pass** artboard on the *Chart
display* page of the SoundCharts design canvas
(<https://claude.ai/artifact/2hWbgQ9wKM1gC2gM51mSCs>,
`project/Tide-Predictions-Design-Pass.dc.html`), to the right of *Legends ·
placement (round 4)*. It reuses the tokens and pieces of the Depth Basis
Indicator, Chart-wide Water Level Adjustment and AML passes: the **DEPTHS**
pill, the flyout sections, the Depth card, the Clearance readout, the
S-104 gauge symbol and the S-111 arrow.

Every height, time, offset, current speed and direction, catalogue date and
station kind in the frames is a fixture, except Seattle 9447130's id and
position. The Puget Sound maps are schematic and not to scale. The view time
is 12 Nov 2026 14:20 local.

The screenshots are a plain local render of the artboard's HTML, not the
canvas viewer: the canvas renderer couldn't be run in the session that made
them. Fonts may differ slightly from the canvas.

## Frames

| Frame | Question | What it shows | Screenshot |
|---|---|---|---|
| Header | — | Recommendations in brief, and the fixture note | [00-header.png](screenshots/00-header.png) |
| Q1a–Q1c | 1 | The source switch in Mariner Settings, the DEPTHS flyout's offer, and how the switch and the station overlay relate | [01-q1-turning-it-on.png](screenshots/01-q1-turning-it-on.png) |
| Q2a, Q2a′, Q2b | 2 | A Layer stack row with a **Legend ›** link (recommended), its key as Legend tab sections, and a Display controls section (alternative) | [02-q2-stations-placement-glyphs.png](screenshots/02-q2-stations-placement-glyphs.png) |
| G1–G4 | 2 | Station glyph options beside today's S-104 gauge | ″ |
| Q2c–Q2e | 2 | Puget Sound at 1:250,000: no decluttering, thinned (recommended), clustered | [03-q2-stations-declutter.png](screenshots/03-q2-stations-declutter.png) |
| Q3a–Q3d | 3 | Pick cards for a harmonic station, a subordinate station, a station S-104 also covers, and a calendar-day curve (rejected) | [04-q3-station-pick.png](screenshots/04-q3-station-pick.png) |
| Q4a–Q4d | 4 | DEPTHS pill alternatives (suffix, dashed pill, "predicted tide") and the Full/Short/Compact forms | [05-q4-pill.png](screenshots/05-q4-pill.png) |
| Q4e–Q4i | 4 | DEPTHS flyout, Depth card, route Clearance readout and findings, adjusted soundings, and edge alternatives | [06-q4-depth-card-route.png](screenshots/06-q4-depth-card-route.png) |
| Q5a–Q5c | 5 | The 10 km reach in a dense sound (today), the seam and a picked station's cell, and an open coast | [07-q5-reach-edge.png](screenshots/07-q5-reach-edge.png) |
| Q6a, Q6b | 6 | The Timeline with S-111 loaded, and with only predictions | [08-q6-timeline.png](screenshots/08-q6-timeline.png) |
| Q7a–Q7c | 7 | The Library source, the setting's date line, and the stale notice | [09-q7-catalogue.png](screenshots/09-q7-catalogue.png) |
| Q8a, Q8b | 8 | Current stations beside an S-111 grid, and the Deception Pass pick card | [10-q8-currents.png](screenshots/10-q8-currents.png) |
| Q9a–Q9c | 9 | `sample_coverage` source, `get_depth_basis` additions, `list_tide_stations` | [11-q9-mcp-cli.png](screenshots/11-q9-mcp-cli.png) |

## Summary

| # | Question | Recommendation |
|---|---|---|
| 1 | Turning it on | One switch, **Use tide predictions where there's no S-104**, in Mariner Settings under Water level adjustment. Off by default. The DEPTHS flyout offers it when a station is in reach and no S-104 is. Independent of the station overlay. |
| 2 | Stations on the map | A **Tide stations · predicted** row under *Other chart overlays*, like NDBC, off by default. The S-104 gauge with a dashed frame, height beside it from 1:150,000. Harmonic and subordinate look the same. Thin by 40 px, harmonic first. |
| 3 | Station pick | The S-104 fixed-station chart, dashed, ±12 h round the view time. Last high or low greyed, then the next four. Datum and station in the header, a "Used for depths" row, the "not a forecast" note at the foot. |
| 4 | Predicted vs S-104 | Same adjusted state; the words say predicted. Pill: **+ water level 14:20 · predicted**. Every value names its station. Dashed means predicted, as dashed already means lower confidence on a route. ◆ unchanged. |
| 5 | Reach and the edge | Keep 10 km and one edge. Add a thin seam where S-104 hands over to predictions. Show a station's cell only when it's picked. |
| 6 | Timeline | One lane with open ends that never sets the axis. With only predictions, *All loaded* and *In view* show the next 48 h. Live stays the default. |
| 7 | Catalogue and freshness | A NOAA source in the Library with its date and **Refresh**. The setting shows the date; the DEPTHS flyout only when it's over 12 months old. |
| 8 | Tidal currents | S-111's arrow and speed colours rising from a dashed station frame, with the speed. Hidden where S-111 covers. A pick card with slacks and maxima. |
| 9 | MCP and CLI | `source.kind = "prediction"` with one `station` object and a `caveat`; `predicted` on `get_depth_basis`; `water_level_predicted` finding. |

## 1. Turning it on

Recommendation: one source switch in **Settings** > **Chart display** >
**Mariner Settings**, in a new **Water level sources** group under **Water
level adjustment**: **Use tide predictions where there's no S-104**. Off by
default. The DEPTHS flyout offers the same switch. The station overlay is a
separate switch (Q1a–Q1c).

- **A source, not an adjustment mode.** S-104 reaches the Depth card and the
  route check whether chart adjustment is on or off. A prediction should
  behave the same, so the switch has to sit above all three consumers, not
  inside the adjustment choice.
- **Off by default.** The first use downloads the station list (Q7), the list
  is US-only for now, and a prediction can be 0.5 m or more off in weather.
  Turning it on silently would change the UKC a route check reports for every
  US route.
- **The flyout offers it** in the one case where it helps: predictions are
  off, no S-104 meets the view, and a station is within reach. "Tide
  predictions are available here" with **Use tide predictions**. This is the
  pattern the relief shading pass chose: the popup gets a shortcut to the
  same switch. The pill itself stays **Charted**; it never nags.
- **Independent of the overlay.** The overlay is display (where the stations
  are); the switch is a source (whether depths use them). Either can be on
  alone. Each points to the other in one line: the setting's line links to
  the Library, and the pick card of a station whose predictions aren't used
  says "Not used for depths" with **Use tide predictions**.

## 2. Stations on the map

Recommendation: a **Tide stations · predicted** row under *Other chart
overlays* in the Layer stack, off by default and kept after a restart (Q2a).
Its key is a section in the Legend tab, with every other product's (Q2a′).
Glyph G1. Thinning Q2d.

- **Layer stack, not Display controls.** Display controls sections appear only
  while a product is loaded (Weather, Bathymetry, AML). Predictions have no
  loaded product, so a section would need a new rule to show. A live overlay
  with no dataset is what NDBC's row already is.
- **The key goes in the Legend tab** ([legends](../legends.md) D1, D4, D8).
  The row carries only the one-line **Legend ›** link that every Layer stack
  entry with a legend now has. The Legend tab gets a **Tide stations ·
  predicted** section while the row is on: rising, falling, turning, and the
  height label, with the caption "Height above chart datum at the view time ·
  dashed frame = predicted". It comes from an `ILegendSectionSource`, like the
  route check and Library coverage, so MCP `get_legend` lists it too. The
  S-104 section keeps explaining S-104's own gauges.
- **Glyph: the S-104 gauge with a dashed frame** (G1): rising, falling or a
  bar for turning (within 20 minutes of a high or low). The height above the
  station datum sits beside it from 1:150,000, the name from 1:50,000.
  Rejected: the same glyph with a "P" (G2, a code nobody knows), a new
  tide-staff glyph (G3, a second symbol family for one quantity), and a size
  difference for harmonic and subordinate (G4, size reads as importance).
- **Harmonic and subordinate look the same.** Both give a prediction for that
  place. How it was made matters when judging it, which is the pick card's job.
- **Where S-104 covers the station** at the view time, the predicted glyph
  isn't drawn; the S-104 gauge or grid is what depths use.
- **Decluttering.** Draw from 1:500,000 (NDBC's 1:2,000,000 is too small for 30
  stations in one sound). Keep a station when no kept station is within 40 px;
  place harmonic stations first, then subordinates by id so the choice is
  stable while panning. At 1:250,000 this hides 2 of 12 in the fixture and
  drops the labels; they return as you zoom in. Stations in one basin predict
  nearly the same height, so a representative glyph loses little. Counted
  clusters (Q2e, AML's SBO pattern) hide the rising/falling state, which is
  the reason to look; they're the fallback if thinning proves jumpy.

## 3. Station pick

Recommendation: the Object information card reuses the S-104 fixed-station
time-series chart with the curve dashed (Q3a–Q3c).

- **Header:** gauge glyph, name, a **Predicted** chip (dashed outline), then
  "NOAA tide station 9447130 · harmonic · position", or for a subordinate
  "subordinate · from Seattle 9447130". The chip is the card's provenance
  strip, as AML's tag is.
- **Now:** "PREDICTED HEIGHT AT 14:20 · 0.4 m falling 0.6 m/h", in a
  dashed-outline box (the S-104 card uses a solid one).
- **Curve: 12 hours each side of the view time.** A calendar day (Q3d) puts
  the view time at an edge late in the day and can drop the next high. ±12 h
  keeps the view time in the middle and always shows the next high and low.
  MLLW and MHHW are drawn as reference lines.
- **Highs and lows:** the last one before the view time greyed, then the next
  four, as "Today 16:20 · −0.2 m" in the Local/UTC setting.
- **Subordinate stations** add a **Reference station** block (station as a
  link, distance, high and low time offsets and height factors) and a line
  saying the curve between highs and lows is the reference station's curve
  fitted to them, so it's less certain than the highs and lows.
- **Datum, MHHW, Used for depths, Station list date** in a short table. On a
  datum other than the chart's, "Used for depths" says "No: station on MLLW,
  depths on LAT", as S-104 is reported today.
- **When S-104 also covers the station** (Q3c), a teal box at the top says
  "Used for depths here: S-104 Puget Sound OFS (forecast, run 12:00Z)". The
  chart draws S-104 solid and the prediction dashed, and a row gives the
  difference. That row is the only place two sources are subtracted, as a
  check, never as a depth.
- **"Not a forecast"** is a fixed note at the foot: "Predicted from NOAA
  harmonic constants: the astronomical tide only. Wind, pressure and river
  flow can move the real water level 0.5 m or more. Not for navigation."

## 4. Telling predicted from S-104

Recommendation: the chart is adjusted either way, so the adjusted state and
its teal stay. Words carry "predicted" and the station everywhere a value
appears. The only visual addition is the dash, which already means lower
confidence on a checked route.

- **DEPTHS pill: a suffix** (Q4a), as **· datum unconfirmed** works today:
  **+ water level 14:20 · predicted**, or **· partly predicted** when S-104
  covers part of the view. Rejected: a dashed, unfilled pill (Q4b), which
  reads as a weaker hint and doesn't say predicted in words, and **+ predicted
  tide 14:20** (Q4c), which brings back "tide" after the depth basis note
  retired it.
- **Narrow status bar** (Q4d): Full **+ water level 14:20 · predicted**, Short
  **+ predicted 14:20**, Compact **Predicted 14:20**. Partly: **+ water level
  14:20 · partly predicted**, then **Partly predicted 14:20**. With part of
  view as well: **+ water level 14:20 · predicted · part of view**, then **+
  predicted 14:20 · part**, then **Predicted 14:20**. "Predicted" outlives
  "part of view", because the edge and the flyout already show the part.
- **Flyout** (Q4e): the **Water level** section lists S-104 datasets as today
  and adds one row per group of predicted stations in a dashed frame
  ("Predicted · 5 NOAA stations · Seattle, Edmonds, +3"), then "S-104 first;
  predictions only where no S-104 covers", the datum agreement, and
  "Predictions leave out wind, pressure and river flow." A **Tide predictions**
  link goes to the setting.
- **Depth card** (Q4f): the header line adds "(predicted)"; the **Water level**
  row is "+0.4 m" with a **Predicted · Seattle 9447130** chip and "4.2 km
  away · harmonic · not a forecast" under it. Uncertainty says "Prediction:
  none given; weather can move it 0.5 m or more" rather than inventing a
  figure. Depth over time draws the adjusted line dashed where the water
  level is predicted.
- **Route check** (Q4g): the Clearance readout's water level source is
  "Predicted · Edmonds · 15:05". A sample on predicted water is **lower
  confidence** (dashed), the state S-101 depths and unconfirmed datums already
  use; no new state. Findings: INFO "Water level predicted for 14.6 NM",
  naming the stations; and a CHECK "Tight on predicted water level for 1.2 NM"
  where a Tight or Below stretch rests on a prediction, because Tight is
  0.5 m wide, the same as the weather error.
- **Adjusted soundings keep ◆** (Q4h). The sounding is adjusted whatever the
  source. A hollow ◇ can't be told from ◆ at 9 px. The pick report names the
  source.
- **Adjusted-area edge** (Q4i, maps in Q5): one dash-dot edge for both
  sources (E1), plus a seam. Rejected: two edges (E2), which run side by side
  where the sources meet and both read as boundaries of something, and no
  edge for predictions (E3), which would claim the predicted area is charted.

## 5. Reach and the adjusted-area edge

Recommendation: keep 10 km and the one edge; add a seam and an on-demand cell
(Q5a–Q5c).

- **In a dense sound the edge leaves the view** (Q5a). At 1:250,000 a 10 km
  disc is about 150 px across the screen, so the union covers the sound and
  most of the land. There's nothing to draw, which is right: everything in
  view is adjusted.
- **On an open coast it's the most useful line** (Q5c): it shows exactly where
  ◆ soundings stop. Its style and meaning ("outside it the chart is charted")
  don't change.
- **Seam** (Q5b): a thin teal dotted line where S-104 coverage meets
  predicted reach, so the hand-over is visible. Inside S-104 coverage the
  predicted glyphs aren't drawn; S-104 gauges are.
- **Which station covers where:** on demand. Picking a station outlines its
  cell (its Voronoi cell clipped to 10 km) in the pick-highlight style; the
  Depth card names the station at any point. Drawing every cell edge (the
  chart-wide note's §4 idea) is too busy at 1:250,000; it could appear from
  1:50,000 if wanted.
- **Reach stays 10 km**, the S-104 station rule, so one rule covers both.
  Distance by water would be better than straight-line distance across a
  headland (open question 2).
- Selecting the edge or the seam shows the basis for that side, as the
  chart-wide note already plans.

## 6. Timeline

Recommendation: one **Tide predictions** lane that never sets the axis (Q6a,
Q6b).

- **One lane**, not one per station: stations are many and continuous. It
  exists while the source switch or the overlay is on. It fills whatever range
  is shown, with open ends (‹ ›) instead of a start and end, a dashed edge, and
  the curve with H and L ticks of the station nearest the map centre, named
  under the lane.
- **Never sets the axis.** It's left out of the *All loaded*, *In view* and
  *This run* extents and out of gap compression. *Now ± 6 h*, *Today* and
  *Next 48 h* work as today.
- **Only predictions:** *All loaded* and *In view* show the next 48 hours from
  now, and the preset's tooltip says why: "Only predictions are loaded, and
  they have no end: showing the next 48 hours."
- **The time HUD appears** while predictions are on, because depths then
  change with time.
- **Live stays the default.** It already is, and "what's the water now" is the
  first question.
- **Stepping:** predictions have no samples, so they're not in the **BY DATA**
  step menu, **‹ Previous data** and **Next data ›** skip them, and "no data
  at the view time" never counts them. **BY TIME** works.
- Rejected: a 7-day band that pretends predictions end. It would stretch *All
  loaded* to a week and suggest the data runs out.

## 7. Catalogue and freshness

Recommendation: the Library (Q7a), with a date line under the switch (Q7b) and
a stale notice in the flyout (Q7c).

- **Library.** A **Tide and current predictions** source under NOAA in the
  curated directory, beside the S-104 and S-111 forecasts. It's added
  automatically the first time the switch or a stations row is turned on. Its
  row shows the station counts, date and size, with **Refresh**; its details
  say it holds harmonic constants and subordinate offsets, the provider, terms
  (public domain) and "Astronomical tide only". The Library is where every
  other source shows its age.
- **The setting** shows one line under the switch: "NOAA station list · 3 Mar
  2026 · Manage in Library", or download progress on first use. No separate
  Settings page.
- **The flyout** says nothing about the list until it's over 12 months old
  and predictions are in use: "The station list is from 3 Mar 2025, over a
  year old. Predictions still work; NOAA may have revised some stations
  since. Refresh". Constants change rarely, so a younger list isn't worth a
  notice.

## 8. Tidal currents (T6)

Recommendation: a **Current stations · predicted** row under Tide stations,
off by default (Q8a, Q8b), with its key as a **Current stations · predicted**
section in the Legend tab (Q2a′): S-111's speed bands, the slack bar, and the
caption "Speed, kn · arrow points where the current flows".

- **Glyph:** S-111's arrow and its nine speed-band colours, rising from the
  same dashed station frame, with the speed ("2.6 kn") and, from 1:50,000, the
  name. Arrow length doesn't scale; the label gives the speed. At slack the
  frame holds a bar and the label gives the turn time ("slack 14:35").
- **Beside S-111 grids:** grid arrows have no frame and no label, so a station
  can't pass for a grid node. Where S-111 covers a station at the view time,
  the station isn't drawn: S-111 wins, as S-104 does for tides.
- **Pick card:** the same layout as Q3 with "PREDICTED AT 14:20 · Slack 0.1
  kn, turning to flood at 14:35"; the speed curve ±12 h with flood above and
  ebb below the zero line; **Slacks and maxima** (time, speed, direction);
  flood and ebb directions; the current's depth below the surface; "Display
  only. Not used in route checks."; and a note that wind and river flow change
  real currents and the strongest stream in a pass can be away from the
  station.
- **Rotary stations** draw the arrow at the predicted direction and the card
  adds a direction to each maximum.
- Not a water level source, so no setting beside it.

## 9. MCP and CLI

Recommendation: three small changes to what #1452 plans, so agents see the
same words as the UI (Q9a–Q9c).

- **One `station` object** wherever a prediction is the source
  (`sample_coverage`, `sample_coverage_along`, `check_route` water levels,
  `predict_tide`): `id`, `name`, `type` (`tide` or `current`), `kind`
  (`harmonic` or `subordinate`), `referenceStationId`, `distanceMeters`.
- **`source`** in sampling: `kind: "prediction"`, `station`, `verticalDatum`,
  `caveat` ("Astronomical tide only, not a forecast.") and `catalogueDate`. An
  agent quoting a value can quote the caveat and the age with it.
- **`get_depth_basis`**: a top-level `predicted` (`none`, `part` or `all`),
  `display` matching the pill ("+ water level 14:20 · partly predicted"), and
  predicted stations in `waterLevelSources` with `spec: "prediction"`, `kind`,
  `name` and `catalogueDate`.
- **`check_route`**: `waterLevels[].kind` gains `"prediction"`; finding codes
  `water_level_predicted` and `tight_on_predicted`.
- **`list_tide_stations`**: a `catalogue` object (`date`, `stale`) and each
  station's `type`, `kind`, position, distance, datums and, for subordinates,
  `referenceStationId` and `offsets` (`highMinutes`, `highFactor`,
  `lowMinutes`, `lowFactor`).
- **Display state** (#1453): `get_tide_display` and `set_tide_display` with
  `tideStations`, `currentStations` and `usePredictions`, mirroring
  `get_weather_display`.
- **CLI:** text output prints "predicted" in the source column and the caveat
  once under the table; `s100 tides predict` heads its height column
  "Predicted (m above MLLW)".

## Decisions

Proposed, for #1453 (tides) and #1454 (currents) to cite once confirmed.

1. **D1 Source switch.** **Use tide predictions where there's no S-104**, a
   viewer setting in a **Water level sources** group under **Water level
   adjustment** in Mariner Settings. Off by default. It feeds the Depth card,
   Depth over time, the route check and chart adjustment together.
2. **D2 Flyout offer.** The DEPTHS flyout shows "Tide predictions are available
   here" with **Use tide predictions** only when the switch is off, no S-104
   meets the view and a station is within reach. The pill doesn't change for
   it.
3. **D3 Overlay and source are independent.** Neither turns the other on.
4. **D4 Station overlay.** A **Tide stations · predicted** row under *Other
   chart overlays*, off by default, kept after a restart. Its key is a
   **Tide stations · predicted** section in the Legend tab (legends D1, D4),
   from an `ILegendSectionSource`; the row has only the **Legend ›** link
   (legends D8). No legend on the map or under the row.
   Drawn from 1:500,000; heights from 1:150,000; names from 1:50,000.
5. **D5 Predicted glyph.** The S-104 gauge symbol with a dashed frame
   (rising, falling, turning within 20 minutes of a high or low). Harmonic and
   subordinate drawn the same. Not drawn where S-104 covers the station at the
   view time.
6. **D6 Thinning.** 40 px screen distance; harmonic first, then subordinates
   by id.
7. **D7 "Dashed means predicted."** Station frames, the **Predicted** chip, the
   pick card's now-box and curve, predicted rows in the flyout, Depth over
   time, and the Timeline lane. It matches the route check's "lower
   confidence is dashed".
8. **D8 Pick card.** ±12 h curve round the view time; last high or low greyed
   plus the next four; header with NOAA id and kind (and "from" reference
   station); Reference station block for subordinates; Datum, MHHW, Used for
   depths, Station list date; the fixed "not a forecast" note. When S-104
   covers the station: "Used for depths here: S-104 …", both curves, and a
   difference row.
9. **D9 DEPTHS pill.** Suffixes **· predicted** and **· partly predicted** on
   the adjusted state, with the Full, Short and Compact forms in §4. Kind and
   colour unchanged.
10. **D10 Values name their source.** Every predicted value reads "Predicted ·
    {station}" (Depth card, Clearance readout, sounding pick report, flyout),
    with distance and kind where there's room.
11. **D11 Route check.** Predicted water level makes a sample lower confidence.
    INFO finding "Water level predicted for {n} NM" with the stations; CHECK
    finding "Tight on predicted water level for {n} NM" where a Tight or Below
    stretch rests on a prediction.
12. **D12 Soundings keep ◆.** No new adjusted-sounding mark.
13. **D13 One edge, a seam, a picked cell.** The adjusted-area edge covers
    S-104 and predictions alike. A thin dotted teal seam marks where S-104
    hands over. A picked station outlines its cell (Voronoi, clipped to
    10 km). Reach stays 10 km.
14. **D14 Timeline lane.** One **Tide predictions** lane with open ends, the
    curve and H/L ticks of the station nearest the map centre. It never sets
    an axis extent; with only predictions, *All loaded* and *In view* show the
    next 48 h and say so. Not in **BY DATA** steps or data jumps. The time HUD
    shows while predictions are on. Live stays the default.
15. **D15 Catalogue.** A NOAA **Tide and current predictions** source in the
    Library (directory and auto-added on first use) with date, counts, size
    and **Refresh**. A date line under the switch. A stale notice in the
    flyout after 12 months.
16. **D16 Current stations.** A **Current stations · predicted** row, off by
    default, with its key as a section in the Legend tab (as D4). S-111 arrow and speed colours from a dashed frame, speed label,
    slack bar with turn time. Hidden where S-111 covers. Pick card with
    slacks and maxima, flood and ebb directions, current depth, "Not used in
    route checks".
17. **D17 MCP fields.** `source.kind = "prediction"`; one `station` object
    (`id`, `name`, `type`, `kind`, `referenceStationId`, `distanceMeters`);
    `caveat` and `catalogueDate`; `predicted` on `get_depth_basis`;
    `water_level_predicted` and `tight_on_predicted` finding codes;
    `get_tide_display` / `set_tide_display`.

## Open questions

1. **Map heights without the word.** The map label is "0.4 m" beside a dashed
   frame; the epic says every predicted value is labelled predicted with its
   station. This note treats the dashed frame, the row name and the tooltip as
   the label on the map, and puts the word and station in every card and
   readout. Is that enough, or should the map label carry "pred."?
2. **Reach by water.** 10 km straight-line can cross a headland into a
   different basin (Hood Canal vs Admiralty Inlet). Should reach be measured
   over water, or should T2 give stations a basin?
3. **Subordinate curves.** NOAA publishes only highs and lows for subordinate
   stations. Is a curve fitted to the reference station's shape acceptable for
   the Depth card and route check between the extremes, or should T3 use the
   reference curve with interpolated offsets and say so?
4. **Stale threshold.** 12 months is a guess. How often does NOAA revise
   constants and offsets in practice?
5. **Current stations' plane.** S-111 arrows draw on their own sub-layer.
   Should predicted current stations sit in *Dynamic arrows* with them rather
   than *Other chart overlays*?
6. **Rotary currents.** How many NOAA Puget Sound current stations are rotary,
   and does the arrow need more than direction for them?
7. **Edge over land.** The edge isn't clipped to water today. With
   predictions it will more often run over land. Clip it to the loaded chart's
   water, or leave it?

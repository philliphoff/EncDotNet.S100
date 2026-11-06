# Tropical cyclones, fronts and pressure centres

> Status: **Implemented** (2026-11-06). Slices S0–S4 and S6–S9 merged in
> #1304–#1306, #1308, #1313, #1314, #1318, #1319 and #1326; later work is in
> [#1302](https://github.com/philliphoff/EncDotNet.S100/issues/1302) (JTWC) and
> [#1303](https://github.com/philliphoff/EncDotNet.S100/issues/1303).
> Originally **Design, round 1** (2026-11-04). Covers the
> vector items of epic [#1226](https://github.com/philliphoff/EncDotNet.S100/issues/1226):
> [#1232](https://github.com/philliphoff/EncDotNet.S100/issues/1232) (tropical
> cyclone track, cone and wind radii) and
> [#1233](https://github.com/philliphoff/EncDotNet.S100/issues/1233) (surface
> analysis fronts and pressure centres). Revision 2 (same day) redraws the
> mock-ups with every non-map element in the SoundCharts panel it lives in,
> and makes §6 say exactly where each one goes. Revision 3 (same day)
> records the user's decisions on Q1–Q7 (§10) and adds the tropical cyclone
> route-check settings to Settings (§6.6). Mock-ups at Day, Dusk and Night are in
> [`vector-weather/mockups.html`](vector-weather/mockups.html) (published as
> <https://claude.ai/artifact/VLAwoquLkwdESYSKrCxGQF>). Decisions are
> numbered **V1–V39** so implementation PRs can cite them. The user's
> decisions on the open questions are in §10.

The grid items of #1226 (#1227 SST, #1228 currents, #1229 visibility) reused
what #1055 built: a parameter row, a `WeatherCoverageStyle`, a legend and a
Library source. Cyclones and fronts don't fit that mould. They're features
with identity and a forecast of their own, they need symbols the toolkit
doesn't draw yet, and the time at which they are "true" works differently
from a grid step. This note settles the visual language, Timeline behaviour,
panels and data sources before any of that is built.

Weather data stays labelled "not for navigation" everywhere, as
[`weather.md`](../weather.md) and [`weather-sources.md`](weather-sources.md)
§3.1 require. The repo is MIT: nothing here comes from GPL sources, and the
symbols are drawn from the WMO conventions, not copied from another viewer.

## 1. What's there today

| Piece | Where | Relevant here |
|---|---|---|
| Grid styles, planes, priorities | `Datasets.Weather/*Style.cs`, `weather-sources.md` §4–§5 | Wave fill on `OnDemandSurface` 0; isobars `OtherChartOverlays` 90; barbs `DynamicArrows` 20; observations `OtherChartOverlays` 100 |
| Weather colours | `WeatherPalette` (`WX*` tokens) | Violet waves (hue 295), sepia wind (hue 55), grey visibility, diverging SST, S-111 current colours; Night under luminance 0.2 |
| CAP warnings | `Datasets.Cap/CapStyle.cs`, `CapDatasetProcessor` | `CautionsAndWarnings` plane; outline and hatching by severity (`CAPEXT` purple, `CAPSEV` red, `CAPMOD` orange, `CAPMIN` olive); labels SCAMIN 1:3,000,000; `ITimeAwareDatasetProcessor` with steps at each start and end |
| Toolkit | `Portrayals/Application` (`ApplicationPalette`, `SymbolSet`, `InstructionBuilder`) | Points, text, simple (dashed) lines, colour and pattern fills. **No complex line style** (symbols along a line), though the scene renderer draws S-101's (`PaintOp`, `VectorSceneBuilder`) |
| Layer stack legends | `GridCoverageSubLayer.Legend` (`CoverageLegendEntry(Color, Label)`) | Colour swatches only, no glyphs |
| Display controls | `EcdisDisplaySettings.LayerVisibility`, Weather section | Switches only for optional layers; always-on layers have none |
| Timeline | `TimePolicy` nearest / at-or-before, median-step tolerance; lanes per dataset; layer time on Datasets rows | Grids step by valid time; CAP steps at start and end |
| Library staleness | `staleAfterHours`, **Stale** tag, **Load latest run** | Weather runs stale after 12 h; newer runs replace in place |
| Route findings | `RouteFinding`, sleeves and shaped markers (#959) | Weather check #1093 is still a proposal |

## 2. Principles

1. **Form carries the meaning, colour supports it.** Every hue the chart
   could use is taken: S-101 owns blue and green (depth), red and amber
   (danger), magenta (mariner information); the weather layers own violet,
   sepia, grey and the SST ramp; CAP owns a severity ramp. So cyclone and
   front portrayal is legible in monochrome first. That is also what
   mariners know from radiofax charts, which are black and white.
2. **Official shapes, not ours.** The NHC cone is NHC's construction. We
   draw the polygon the advisory gives, never a cone we compute.
3. **Hazard prominence doesn't fade with age; position confidence does.** An
   old advisory still describes a storm that exists, so it keeps its
   weight and gains an age stamp. An old front is in the wrong place, so it
   fades.
4. **One time on the map.** What the chart shows at the view time matches
   what the Timeline, the Time HUD and the pick card say.
5. **Not for navigation**, in the pick card, the Layer stack and the
   Library, worded as for the grids.

## 3. Tropical cyclone portrayal (#1232)

### 3.1 Parts and layer order

| Part | Geometry | Plane, priority | Drawn |
|---|---|---|---|
| Cone, days 1–3 | Polygon from the advisory | `OnDemandSurface` 10 (over the wave fill, under S-104 and the ENC's line work) | Always |
| Cone, days 4–5 | Same polygon, the part beyond 72 h | `OnDemandSurface` 10 | Always, stippled |
| Wind radii at view time (34, 50, 64 kt) | Four-quadrant polygons | `OnDemandSurface` 12 | **Wind radii** switch (V26) |
| Past track | Line and best-track points | `OtherChartOverlays` 94 | **Past track** switch |
| Forecast track | Line through forecast points | `OtherChartOverlays` 95 | Always |
| Forecast points | Class symbol per point | `OtherChartOverlays` 96 | Always |
| Forecast times | Text beside each point | `OtherChartOverlays` 96 | **Forecast times** switch |
| Storm at view time | Class symbol, larger, with label | `OtherChartOverlays` 97 | Always while in range |
| Radii outlines | Lines | `OtherChartOverlays` 93 | With the radii |

**V1.** Fills go on `OnDemandSurface`, so soundings, contours and S-101 line
work stay on top. Lines, symbols and labels go on `OtherChartOverlays`
between the isobars (90) and observations (100). The cone and radii stay
under CAP hatching (`CautionsAndWarnings`), so an official Hurricane Warning
area is never hidden by our tint.

### 3.2 Colour

Options considered:

| | Option | For | Against |
|---|---|---|---|
| A | **Storm ink**: one low-chroma plum-grey family, nested opacity for 34/50/64, dash pattern per radius | No collision with S-101, CAP or weather ramps; legible in monochrome; works at Night | Less familiar than NHC's yellow/orange/red |
| B | NHC colours: yellow 34, orange 50, red 64; white cone | Familiar from NHC web graphics | Orange and red are S-101 danger and CAP Severe; white cone vanishes on S-101 Day deep water (white) |
| C | A new saturated hue (for example teal or rose) | Stands out | Teal is S-104; rose sits between CAP Severe and S-101 magenta |

**V2. Option A.** New tokens, all `WX`-prefixed, OKLCH hue about 330 with
chroma under 0.05 so they read as tinted grey, far from S-101's saturated
`CHMGD` magenta:

| Token | Day | Dusk | Night | Use |
|---|---|---|---|---|
| `WXTCINK` | `#2A1F2D` | `#EADFEA` | `#6B5F6B` | Track, symbols, labels, cone outline, radii outlines |
| `WXTCHALO` | `#FFFFFF` | `#2A2530` | `#000000` | Label halo |
| `WXTCCONE` | `#6F5E70` at 0.14 | `#EADFEA` at 0.12 | `#6B5F6B` at 0.18 | Cone fill |
| `WXTC34` | `#7A5C78` at 0.16 | `#D9C7D7` at 0.14 | `#6B5F6B` at 0.10 | 34 kt radius fill |
| `WXTC50` | `#5E3F5C` at 0.26 | `#E3D3E1` at 0.22 | `#6B5F6B` at 0.18 | 50 kt radius fill |
| `WXTC64` | `#43243F` at 0.38 | `#F1E6EF` at 0.32 | `#6B5F6B` at 0.28 | 64 kt radius fill |

Night values are under relative luminance 0.2 (`#6B5F6B` is 0.12). The
fills nest (64 inside 50 inside 34), so the 64 kt core is the darkest by day
and the brightest at dusk and night, without a fourth hue.

**V3. The cone is tinted, not whitened.** NHC's white cone works on a
coloured base map. S-101 Day deep water is near white, so the cone
darkens slightly by day and lightens slightly at dusk.

**V4. Line patterns separate what colour doesn't:**

| Line | Pattern | Width |
|---|---|---|
| Cone outline | Dashed 1.2 / 0.8 mm | 0.4 mm |
| 34 kt radius | Dashed 0.6 / 0.6 mm | 0.4 mm |
| 50 kt radius | Solid | 0.4 mm |
| 64 kt radius | Solid | 0.8 mm |
| Forecast track | Solid | 0.6 mm |
| Past track | Dotted 0.2 / 0.6 mm | 0.6 mm |

### 3.3 Classification symbols

**V5.** The WMO weather-map glyphs, which also appear on OPC charts, with
one symbol per class:

| Class (NHC `DVLBL` / JTWC) | Symbol |
|---|---|
| Tropical depression (D) | Open circle |
| Tropical storm (S) | Open circle with two arms |
| Hurricane or typhoon (H) | Filled circle with two arms |
| Major hurricane, cat. 3+ / super typhoon (M) | Filled circle with two arms and an outer ring |
| Subtropical depression or storm | The D or S symbol with a square centre |
| Post-tropical, remnant low, disturbance | `×` in a circle, with "PT" or "LOW" in the label |

**V6. Southern hemisphere symbols are mirrored**, because cyclones there turn
clockwise. This follows the barb rule (`WindBarbSymbols.For(knots,
latitude)`), and the symbol generator writes both sets. Check the arms'
direction against the WMO glyph before generating; the mock-ups only
approximate it.

**V7.** Symbols come from a new pass of `tools/GenerateWeatherSymbols`
(`WXTCD`, `WXTCS`, `WXTCH`, `WXTCM`, `WXTCSD`, `WXTCSS`, `WXTCPT`, each with
an `S` suffix for the southern set), checked in as SVG. Forecast points use
them at 3.5 mm; the storm at view time uses them at 6 mm with a halo.

### 3.4 Labels

**V8. Storm at view time** (two lines, halo, 3.5 mm then 3 mm):

```
HURRICANE ZELDA · 115 kt · 948 hPa
Adv 17 · issued Tue 3:00 PM · NW 9 kt
```

The class word is spelt out (HURRICANE, TYPHOON, TROPICAL STORM, TROPICAL
DEPRESSION, POST-TROPICAL). Intensity is the maximum sustained wind in knots,
as advisories give it; the pressure is shown when the advisory has it.

**V9. Forecast points:** one line, `Thu 6:00 AM · 110 kt`. Times use the viewer's format and the
Local/UTC preference with the machine's zone, as every viewer time does. No
storm-local time.

**V10. Scale:** forecast time labels have SCAMIN 1:20,000,000. The storm
label has none; at very small scales its second line is dropped first (text
priority), so the name and intensity stay.

## 4. Time (#1232)

### 4.1 What's drawn at the view time

Options:

| | Option | For | Against |
|---|---|---|---|
| A | **Whole forecast always, plus the storm at the view time** | The cone and track are one product; mariners plan against all of it | The view-time marker must be clearly an estimate between points |
| B | Only the storm at the view time | Matches grids exactly | Hides the forecast, which is the point of the product |
| C | Forecast up to the view time only | Shows "where it's been" | Hides the future at Live, the most common view |

**V11. Option A.** The cone, forecast track and forecast points are drawn
for the whole advisory whatever the view time. On top of them, the **storm
at view time** is drawn:

- **Inside the forecast** (issue time to the last forecast point): position
  and intensity interpolated linearly in time between the forecast points on
  either side (great-circle between positions). Radii are interpolated per
  quadrant; a radius that is zero at one end shrinks to nothing. The symbol
  is the class at the earlier point, drawn with a **dashed ring** to say
  "estimated", and the label reads `est. Wed 21:00 · 112 kt`.
- **On a forecast point:** the point itself is drawn large, with its
  forecast radii, with no dashed ring.
- **Before the issue time, on the past track:** the best-track position and
  intensity, interpolated the same way, with the observed radii where the
  best track has them. Solid ring.
- **Before the first past-track point or after the last forecast point:** no
  storm marker. The cone stays (it's still the latest forecast); the
  Datasets row says "Beyond the forecast" or "Before the track", as layer
  time messages do today (#709).

**V12. Radii are drawn at the view time only.** Drawing every forecast
point's radii at once makes an unreadable stack. The cumulative envelope
("wind swath") is a later option (§9, S10).

**V13. Interpolation is a display aid, labelled so.** The pick card says
"Estimated between forecast points; NHC doesn't forecast this position."

### 4.2 The Timeline

**V14.** The dataset's lane is a band from the first past-track time to the
last forecast point, with:

- ticks at each forecast point, which are the dataset's **steps** for ‹ ›;
- a solid diamond at the advisory's issue time;
- a hollow diamond at the **next advisory expected** time (NHC: 03, 09, 15
  and 21 UTC, plus intermediate advisories every 3 h under watches or
  warnings).

The processor is time-aware with continuous selection (any time in range
draws), not at-or-before, so dragging the Timeline moves the storm smoothly.

**V15. A new advisory replaces the old one in place**, as a newer GRIB run
does. Advisories aren't kept as a history; the past track carries the
history. Pinning (`LibraryDownloads.Pin`) keeps an advisory for review.

**V16.** In Live mode the storm at view time follows the clock between
forecast points, so the marker creeps along the track between advisories.
This is the expected look; staleness (§7) says when it no longer means much.

## 5. Fronts and pressure centres (#1233)

### 5.1 Symbols

**V17.** WMO surface-analysis symbology, pips on the side the front moves
towards:

| Feature | Line | Pips |
|---|---|---|
| Cold front | Solid 0.7 mm | Filled triangles, one side |
| Warm front | Solid 0.7 mm | Filled semicircles, one side |
| Occluded front | Solid 0.7 mm | Triangle and semicircle alternating, same side |
| Stationary front | Solid 0.7 mm | Triangle and semicircle alternating, **opposite** sides |
| Trough | Dashed 1.5 / 1 mm, 0.5 mm | None |
| Squall line | Dash-dot-dot, 0.5 mm | None |
| Dry line | Solid 0.5 mm | Open semicircles (scalloped) |
| Tropical wave | Solid 0.5 mm, labelled `TRPCL WAVE` | None |
| Frontogenesis / frontolysis | Pipped line broken by gaps / by bars | As the base type |

### 5.2 Colour

Options:

| | Option | For | Against |
|---|---|---|---|
| A | **Monochrome front ink**, shape-coded | Matches radiofax, which mariners read daily; no hue collisions; heavier than isobars so it reads over them | Less familiar to people used to WPC's colour chart |
| B | WPC colours: cold blue, warm red, occluded purple, stationary red/blue, trough brown | Familiar from TV and WPC | Blue is depth, red is danger, purple is CAP Extreme and close to S-101 magenta |

**V18. Option A.** One token, `WXFRONT`: Day `#1A2030`, Dusk `#E3E6EE`,
Night `#646A76` (luminance 0.14). It's darker and twice as wide as the
isobars (`WXISOBAR`), so a front reads as the stronger line where it lies
along an isobar kink. There's no colour option in v1 (§10 Q3).

### 5.3 Pressure centres

**V19.** A bold `H` or `L` (5 mm), an `×` at the exact centre, and the
pressure underlined below (`1032`), in `WXFRONT` with the isobar halo. Where
the bulletin gives a forecast position (the coded forecast bulletins do), a
dashed arrow runs to a small `×` with the time (`Thu 12:00 AM`). The coded
analysis gives no movement vector, so none is drawn from it.

### 5.4 Placement and clutter

**V20.** Pips are placed in **screen space**: one pip every 9 mm along the
line, starting 4 mm in, so their density is the same at every scale. This
needs a complex line style (a symbol repeated along a line) on the toolkit's
`InstructionBuilder`; the scene renderer already draws S-101's. Stationary
fronts flip side on every pip.

**V21. Smoothing.** The coded bulletin gives a polyline at 1° (standard) or
0.1° (high resolution). The line is smoothed with a Chaikin pass (three
iterations) before drawing, as hand-drawn analyses are. The pick report gives
the bulletin's own points.

**V22. Scale rules:**

- Fronts and pressure centres: always drawn (synoptic features matter most
  at small scales).
- Troughs, squall lines, dry lines, tropical waves: SCAMIN 1:40,000,000,
  so a world view keeps only the fronts.
- Pressure values under `H`/`L`: SCAMIN 1:60,000,000.
- `H`/`L` labels have text priority over isobar labels, so the isobar label
  moves, not the centre.
- No maximum scale. Zoomed right in, a front is a broad line through the
  chart; that's honest about its precision.

## 6. Panels

Everything in this section lives in an existing SoundCharts panel, not on
the chart. The chart shows only what's drawn on the map (§3, §5) and the
map overlays that are already there: scale bar, tool column, compass, the
Live stamp while the Timeline dock is open, the Time HUD while it's closed,
and the route-check legend card in check mode. No new floating panel or
on-map legend is added.

| Panel | Where it docks | How you get there | What this design adds |
|---|---|---|---|
| **Object information** | Right dock, 360 px | Pick on the map | A tropical cyclone, front or pressure-centre pick (V23, V24) |
| **Layer stack** | Left dock, 320 px | Activity bar, stack icon | Entries and glyph legends (V25) |
| **Display controls** | Left dock | Activity bar, eye icon | Three switches in the Weather section (V26) |
| **Datasets** | Left dock | Activity bar, first icon | Layer-time messages and the Stale tag (§7) |
| **Library** | Left dock | Activity bar, books icon | Two sources and their items (V27, V28) |
| **Timeline** | Bottom dock, 260 px, first tab | Time HUD's dock button | Lanes (V14) |
| **Routes** | Left dock | Activity bar, route icon | A Weather row in the check summary, and weather findings (V38) |
| **Settings** | Fills the content area | Activity bar, gear at the bottom → **Chart display** | Tropical cyclone route-check settings in Mariner Settings (V39) |

The mock-ups draw each of these in its own panel, laid out from
`MainWindow.axaml` and the panel views, at Day/Light, Dusk/S100Dusk and
Night/S100Night.

### 6.1 Object information

**V23.** Picking any part of a storm (cone, track, a point, a radius) adds
one hit for the storm to the existing hit list ("3 FEATURES AT THIS POINT").
A CAP warning and a weather grid point at the same place are hits too.
Selecting the storm fills the pinned header and the attributes as for any
feature:

- **Notice line.** The existing navigation notice (12 px, semibold, warning
  colour): "Tropical cyclone forecast, not for navigation. Follow official
  NHC advisories." This is the processor's `PickReportNotice`, as for CAP
  and the weather grids.
- **Stale line.** New. When the advisory is stale (§7), a second
  warning-coloured line under the notice: "Advisory 17 is 9 h old. NHC has
  probably issued a newer one. Load latest run". The link runs the
  Library's existing **Load latest run**.
- **Data credit.** The existing line: "Data: NOAA/NWS National Hurricane
  Center · Public domain".
- **Identity block.** The class symbol in the 36 px tile; "HURRICANE ZELDA"
  as the primary label, with a class pill ("Category 4"); the caption
  "AL142026 · TC · al142026_5day_017.zip".
- **Attributes table.** The existing key/value table, one row each:
  - Advisory (number, issue time, age)
  - Next advisory
  - At view time (estimated between forecast points, or on one)
  - Position
  - Max wind
  - Pressure
  - Movement
  - Picked part
  - 64, 50 and 34 kn radii, each as "NE / SE / SW / NW nm"
  - Cone (NHC's caution, paraphrased: the centre stays inside about
    two-thirds of the time, and winds reach well outside it)

  The radii are attribute rows, not a new table component.

Times use the viewer's own format and Local/UTC preference, for example
"11/4/2026 3:00 PM".

**V24.** A front pick has the same structure:

- Notice: "Weather analysis, not for navigation."
- Credit: the WPC line.
- Identity: "Cold front", pill "Moderate", caption "COLD 3 · CODSUS ·
  codsus_hr_2026110412.txt".
- Attributes: Analysis (time and age), Strength, Points, and Next analysis.

A pressure centre's identity is "Low 996 hPa", and its attributes add the
forecast position and pressure when the bulletin has one.

### 6.2 Layer stack

**V25.** The Layer stack lists sub-layers by S-98 plane, so a storm has two
entries (V1):

| Plane | Entry (file name, layer key, priority) | Legend under the entry |
|---|---|---|
| Other chart overlays | `al142026_5day_017.zip`, `weather.tc.track`, #95 | One symbol per class (TD, TS, HU, major); the southern-hemisphere mirror; forecast track; past track |
| On-demand surface | `al142026_5day_017.zip`, `weather.tc.areas`, #10 | Cone days 1–3; cone days 4–5; 34, 50 and 64 kt areas |

A surface analysis has one entry: Other chart overlays,
`codsus_hr_….txt`, `weather.fronts`, #92. Its legend has the five line
types and "Pressure centre, hPa".

The legend sits under the entry that draws it, as the wave fill's colour
legend does today (`LayerStack.Entry.Legend`). Today a legend row is a
14×10 colour swatch and a label. This design adds **glyph rows**:

- `CoverageLegendEntry` gains an optional symbol name or line style.
- The Layer stack draws the glyph from the same `SymbolSet` at legend size.

Swatch rows don't change.

### 6.3 Display controls

**V26.** Three new switches are added to the end of the existing
**Weather** section, after Currents:

| Switch | Default | Draws |
|---|---|---|
| **Wind radii** | On | 34/50/64 kt areas at the view time |
| **Past track** | On | Best-track line and points |
| **Forecast times** | On | Time and intensity beside forecast points |

Two conditions change:

- The section's condition (`HasWeatherData`, today only for GRIB2 and
  NetCDF datasets) widens to any weather-format dataset, including tropical
  cyclones and surface analyses.
- The three new switches show only while a tropical cyclone is loaded.

The cone, the forecast track and points, and the storm at view time have no
switch. To hide a whole storm, use its eye in Datasets. Fronts get no
switch in v1. The switches are stored in `EcdisDisplaySettings.LayerVisibility`
like the grid switches.

### 6.4 Library

**V27.** Two new sources go in the source tree, each with the `FEED` kind
tag:

- **Tropical cyclones — NHC** (Atlantic, East and Central Pacific).
  - One item per active storm. The item's swatch is the class symbol, its
    name is the storm and ATCF id, its subtitle is the latest advisory, and
    its summary is the forecast end and intensity.
  - The forecast window bar shows how much of the forecast has passed.
  - Indexing reads `CurrentStorms.json`, and downloading fetches the
    advisory's files.
  - A new advisory makes the download outdated and replaces it, as a newer
    run does. This uses the existing **New run** tag and the **Updates**
    segment.
- **Tropical cyclones — JTWC** (West Pacific, Indian Ocean, Southern
  Hemisphere). The same, from JTWC's warning list (§8.2). It's added later,
  from its own issue after the spike (§10 Q5).
- **Surface analysis — WPC**. One item, the latest analysis. The coded
  forecast bulletins are added as items when chosen.

**V28.** Freshness overrides the 12 h weather default:

- Cyclones are stale after 7 h without a new advisory (the 6 h cycle plus an
  hour's grace).
- Fronts are stale after 6 h (two 3-hourly analyses missed).

Staleness uses the existing parts:

- the **Stale** tag on the item;
- the details' stale-run warning, worded "Advisory 17 is over 7 hours old"
  instead of "Run … is over 12 hours old";
- **Load latest run** and **Pin this run**.

A new advisory for a **loaded** storm raises an Information notification in
the existing notification centre ("HURRICANE ZELDA: advisory 18 is
available"), with Load latest run as its action. A storm becoming active
in a source's basin doesn't notify; it just appears in the Library
(§10 Q4).

### 6.5 Datasets, Timeline and Routes

- **Datasets.** A storm row shows its name, "TC" in the spec column, and the
  layer time. Outside the advisory's range, the layer time is
  warning-coloured with a tag ("Beyond forecast"), and the inspector's
  "At view time" field says why (V11). When stale, the row gets the Stale
  tag (§7).
- **Timeline.** New lane groups go in the bottom dock's Timeline tab:
  "Tropical cyclones" and "Surface analysis".
  - Lanes use the "other" lane colour, as weather lanes do today.
  - The lane label shows the storm name (or WPC) in mono, a sub line
    ("adv 17 · 6-hourly"), and the layer time below it.
  - The track shows the V14 marks: a forecast-point tick, the issue diamond
    and the next-advisory diamond.
  - The step menu lists the storm, so ‹ › step through its forecast points.
- **Routes.** In the Routes panel's check summary card, a **Weather** row
  sits beside Clearance and Route. Weather findings join the Findings list
  with their group: Must review, Check or Info (V38, V39).
  - The route-check legend card on the map (`RouteCheckLegendView`) gains a
    Weather group with "Storm at ETA (closest approach)".
  - Check mode folds Object information to its edge tab, as it does today.

### 6.6 Settings

**V39.** The tropical cyclone route-check settings (§10 Q6, Q7) go in
**Settings → Chart display → Mariner Settings**, after **Water level
adjustment**. That follows how the route check's other standing values are
surfaced:

- The check reads the safety contour and safety depth from Mariner Settings
  (`RouteCheckSettings.FromMarinerSettings`).
- #1132 put Water level adjustment, which the Depth card and route checks
  also use, in the same section, beside the mariner depths.
- The Routes passage card's **Your check settings** group (the
  check-settings layer, kept per route in `check-settings.json`) holds only
  per-passage values: draft, squat, required UKC and departure. These
  thresholds are the mariner's standing choice, not a property of one
  passage, so they don't go there and aren't written to the route.

The settings are a sub-heading, **Tropical cyclones on a route**, with a
help line ("Used by the route check's weather findings. Weather findings
are advisory. Not for navigation."), three rows styled like the section's
other rows, and a line under them that spells out the current mapping:

| Setting | Control | Default | Effect |
|---|---|---|---|
| **Check against** | ComboBox: 34 kt radius, 50 kt radius, 64 kt radius | 34 kt radius | The outermost wind radius the check tests. Radii outside it aren't reported. The cone is always tested. |
| **Report closest approach** | ToggleSwitch | On | Adds the `TC_CPA` finding and the CPA line on the map |
| **Must review inside** | ComboBox: 64 kt radius, 50 kt radius, 34 kt radius, Never | 50 kt radius | Radii at or above this are **Must review**. Weaker radii and the cone are **Check**. |

- **Must review inside** never offers a radius outside **Check against**.
  Narrowing **Check against** past it moves it in with it.
- A change re-runs an open route check, as a change to the safety contour
  does.
- Each control gets a `Settings.*` AutomationId and a tooltip, and the
  values are readable and settable through MCP, like the other mariner
  settings.
- The Routes check summary's Weather row tooltip names the values in use
  ("34 kt radius and cone · Must review inside 50 kt").

## 7. Stale and expired data

| State | Cyclone: chart | Cyclone: panels | Fronts: chart | Fronts: panels |
|---|---|---|---|---|
| Current | As designed | Age in card | As designed | Age in card |
| **Stale** (V28) | Unchanged weight. Storm label line 2 becomes `Adv 17 · 9 h old · newer advisory likely`; the age is in bold | **Stale** tag in Datasets and Library; card banner "This advisory is 9 h old. Load latest"; **Load latest run** | Lines and labels at 50 % opacity; the `H`/`L` labels gain `9 h old` | **Stale** tag; card banner |
| **Final advisory** (storm dissipated or handed over) | Past track only; no cone; storm symbol `PT` | "Final advisory issued Wed 9:00 AM"; item drops out of the active list but stays loaded | — | — |
| **Out of range** (view time outside the advisory or beyond tolerance) | Cone stays (V11); no storm marker | Datasets row "Beyond the forecast" | Not drawn | Datasets row "No analysis at view time" |

**V29. Cyclone staleness doesn't fade the storm** (principle 3). A storm
fades out of view only when the user removes it.

**V30. Fronts are drawn at or before the view time, within 6 h.** Outside
that, nothing; a newer analysis (or a forecast bulletin valid nearer the
view time) takes over. In Live mode a 3 h old analysis is current, a 7 h old
one is stale and faded.

**V31.** Display Not Real Time (#993) doesn't change: weather is never
"real time" data in that sense, and the map stamp rules in
`time-overlays.md` stay as they are.

## 8. Data sources

### 8.1 NHC (Atlantic, East and Central Pacific)

| Product | Format | Gives | Use |
|---|---|---|---|
| `CurrentStorms.json` | JSON | Active storms: ATCF id, name, class, intensity, pressure, position, movement, last update, product links | Library indexing (V27) |
| Forecast track and cone (`{ATCF}_5day_{adv}`) | Shapefile zip (also KMZ) | Cone polygon (`_pgn`), track line (`_lin`), forecast points (`_pts`, with valid time, tau, max wind, gust, pressure, class letter) | **Canonical input** |
| Forecast wind radii (`{ATCF}_fcst_{adv}`) | Shapefile zip | 34/50/64 kt quadrant polygons per forecast time | Radii |
| Best track (`{ATCF}_best_track`) | Shapefile zip | Past points with intensity and class, line, observed radii | Past track |
| Watches and warnings | Shapefile zip, KMZ | Coastal breakpoint segments | Later (§9 S10) |
| Wind speed probabilities | Shapefile, KMZ, GRIB2 | 34/50/64 kt probabilities | Later, via the grid path |
| Forecast advisory (TCM) | Text | The same numbers as the shapefiles, in quadrant tables | Cross-check only |
| GIS RSS per basin, `nhc_active.kml` | RSS, KML | Links to the latest files | Alternative to `CurrentStorms.json` |

**V32. Shapefiles over KMZ.** Shapefile attributes are typed DBF fields;
the KMZ puts them in HTML descriptions. A shapefile reader (SHP + DBF, polygons,
lines and points only) is small in managed code. The alternative is
`NetTopologySuite.IO.Esri.Shapefile` (BSD-3, compatible with MIT), since
NTS is already a dependency. File names, field names and the per-storm
`api/storms/{year}/{id}/` folders were seen on nhc.noaa.gov but must be
checked against live files before the reader is written.

### 8.2 JTWC (West Pacific, Indian Ocean, Southern Hemisphere)

JTWC publishes, per warning, a text warning, a graphic, and a Google Earth
KMZ with the track, forecast points and 34/50/64 kt radii. It doesn't
publish NHC's cone. Its graphic's "danger area" is the forecast track error
widened by the 34 kt radius, which is a different construction.

**V33. Don't draw a JTWC danger area as if it were an NHC cone.** It's drawn
with the same fill (so the chart stays consistent) but the Layer stack names
it "Danger area (JTWC)", and the pick card explains the construction. If the
KMZ lacks a danger polygon, only the track, points and radii are drawn.

**V34. JTWC is a spike first** (§9 S5): confirm what the KMZ holds, whether a
machine-readable warning list exists, and whether the site can be reached
reliably from the Library.

### 8.3 Fronts

| Product | Format | Gives |
|---|---|---|
| WPC coded surface bulletin (CODSUS) | Text, every 3 h | `HIGHS`, `LOWS` (pressure + position), `COLD`, `WARM`, `STNRY`, `OCFNT`, `TROF` (strength + point strings), 1° |
| WPC coded surface bulletin, high resolution | Text, every 3 h | The same at 0.1° |
| WPC coded forecast bulletins | Text | The same features at forecast valid times |
| OPC unified analyses | Images (radiofax, GIF, PDF) | Not machine-readable; #1235 (images) covers them |

**V35. The high-resolution CODSUS is the canonical input.** The format is
plain and documented by WPC; MetPy's parser (BSD-3) is a reference for its
edge cases, read for understanding, not copied. The side the pips go on is
implied by point order; **confirm the rule** against WPC's documentation and
a sample before building V17.

**V36. Coverage is limited to WPC's domain** (North America and adjacent
waters). There's no free machine-readable global fronts product from NOAA.
The Library item says so. Fronts derived from a model (a thermal front
parameter from GFS) are a possible later item, not part of #1233.

## 9. Interplay

### 9.1 With CAP, S-124 and S-412

- **V37.** CAP Hurricane, Typhoon and Tropical Storm Warnings for marine
  zones overlap the radii. Both draw: CAP (official) is hatched on
  `CautionsAndWarnings` above the cyclone's fills, and the cyclone's lines
  draw over the hatching. The pick list shows the CAP warning first, then
  the storm.
- S-124 navigational warnings occasionally mention a storm; there's no
  geometric link to make, and they're on their own plane.
- S-412 (when published) will carry warning polygons for wind, waves and
  icing. They'll follow CAP's place in the stack.

### 9.2 With the grid layers

- **Isobars:** fronts lie on isobar kinks and draw above them (priority 92
  over 90), twice as wide and darker. `H`/`L` take label priority.
- **Wind barbs:** stay on `DynamicArrows` above everything here. Barbs inside
  the 64 kt radius are expected to be dense pennants; no special handling.
- **Wave fill:** violet under the plum-grey radii. The radii fills are light
  and the outlines carry the radii, so both read; see the mock-up's frame 1.
- **Model vs official:** GFS's vortex and NHC's forecast often disagree.
  The design doesn't reconcile them; each layer labels its source.

### 9.3 With S-101

- No cyclone or front colour is red, amber, magenta, blue or green (V2,
  V18).
- The fills are under S-101 line work and soundings (V1).
- The cone and radii aren't clipped to water: the wind is over land too, and
  landfall is the point.

### 9.4 With a planned route (#1093)

**V38.** A tropical cyclone rule for the weather check, as #1232 suggests:

- At each sample along the route, at its ETA, take the storm at that time
  (V11) and test the position against the wind radius chosen in Settings
  (V39; 34 kt by default) in its quadrant, and against the cone.
- Findings, advisory like the rest of #1093:
  - `TC_RADIUS_34`, `TC_RADIUS_50`, `TC_RADIUS_64`: "Legs 4–5 inside the
    34 kt wind radius of HURRICANE ZELDA, Wed 6:00 PM to Thu 2:00 AM (forecast,
    adv 17)." The strongest radius sets the finding.
  - `TC_CONE`: "Leg 5 inside the forecast cone at ETA."
  - `TC_CPA`: reported when the storm is loaded and **Report closest
    approach** is on (V39, on by default): "Closest approach 85 nm at Thu
    1:00 AM."
  - `TC_BEYOND_FORECAST`: legs after the last forecast point are "not
    checked".
  - Severity follows **Must review inside** (V39). By default 64 kt and 50 kt
    radii are Must review, and the 34 kt radius and the cone are Check.
- On the map: the existing finding sleeves (#959) on the affected legs, plus
  a **storm at ETA** ghost (the class symbol with a dashed ring) at the time
  of closest approach, joined to the route by a thin dashed CPA line.
- The departure sweep (`DepartureWindow`) shows which departures keep the
  route outside the chosen radius.
- The rule doesn't add NHC's old "1-2-3" buffer: NHC retired it in favour of
  wind speed probabilities, and the cone already widens with time. The user
  chooses the radius instead (§10 Q6).

## 10. Decisions

The user decided these on 2026-11-04. Q1–Q5 follow the recommendations. Q6
and Q7 use the recommendations as defaults, which the user can change in
Settings (V39).

1. **Q1. Cyclone colour (V2). Decided: storm ink.** Monochrome plum-grey
   `WXTC*` tokens, not NHC's yellow/orange/red radii, which clash with S-101
   danger and CAP Severe.
2. **Q2. Storm at view time (V11). Decided: interpolate.** The position
   between forecast points is interpolated and labelled "est.", rather than
   snapping to the nearest forecast point, which jumps up to 12–24 h.
3. **Q3. Fronts colour (V18). Decided: monochrome only in v1.** No Colour
   fronts switch. WPC colours stay in S10 (later).
4. **Q4. Notifications (V28). Decided: a new advisory for a loaded storm
   only.** A storm becoming active in a basin doesn't notify.
5. **Q5. JTWC (V33, V34). Decided: split.** NHC ships first under #1232;
   the JTWC spike, reader and source get their own issue.
6. **Q6. Route check radius (V38, V39). Decided: the 34 kt radius by
   default, user-changeable.** No separate nm buffer. **Check against**
   chooses the radius (34, 50 or 64 kt), and **Report closest approach**
   (on by default) adds the CPA finding.
7. **Q7. Severity of TC findings (V38, V39). Decided: Must review inside the
   50 kt and 64 kt radii, Check for the 34 kt radius and the cone, by
   default, user-changeable** with **Must review inside** (64 kt, 50 kt,
   34 kt or Never).

## 11. Implementation slices

```
S1 TC model + NHC reader ──▶ S2 TC portrayal ──▶ S3 TC time ──▶ S9 Route rule + settings (needs #1093)
        │                          │                 │
        └──────────▶ S4 NHC Library + switches ◀─────┘
                                   │
                    S5 JTWC: own issue (after S2, S4)

S6 CODSUS model + reader ──▶ S7 Fronts portrayal ──▶ S8 Fronts time + Library
        (parallel with S1)          ▲
S0 Toolkit: complex line style + glyph legends ──┘ (also feeds S2)
```

| Slice | Content | Depends on |
|---|---|---|
| **S0** | Toolkit: `InstructionBuilder` complex line style (symbol repeated along a line, alternating sides); `CoverageLegendEntry` glyphs; Layer stack draws them | — |
| **S1** | `TropicalCycloneAdvisory` model (storm, advisory, past track, forecast points, cone, radii); NHC shapefile reader (or NTS.IO.Esri); `TC` format name; `s100 info`; fixtures from a public-domain archived advisory | — |
| **S2** | Cyclone portrayal: `WXTC*` tokens, symbol generation (N and S sets), V1–V10, pick card V23, goldens Day/Dusk/Night | S0, S1 |
| **S3** | Time: interpolation, storm at view time, Timeline lane and next-advisory marker, out-of-range messages (V11–V16) | S2 |
| **S4** | Library source from `CurrentStorms.json`, advisory replace-in-place, stale at 7 h, new-advisory notification for a loaded storm, Display controls switches (V26–V28), final-advisory state | S1; S2 for display |
| **S5** | JTWC spike, then reader and source (V33–V34). Filed as its own issue, not part of #1232 (Q5) | S2, S4 |
| **S6** | `SurfaceAnalysis` model (fronts, centres, valid time); CODSUS reader (standard and HR) and forecast bulletins; `s100 info`; fixtures | — |
| **S7** | Fronts portrayal: `WXFRONT`, pips, smoothing, scale rules, `H`/`L`, pick card, goldens | S0, S6 |
| **S8** | Fronts time (at-or-before within 6 h, fading), WPC Library source, stale at 6 h | S7 |
| **S9** | TC rule in the route weather check (V38). The three Settings rows in Chart display → Mariner Settings (V39): persisted with the mariner settings, re-run on change, MCP get/set, AutomationIds, headless view tests | S3, #1093's `WeatherCheck` |
| **S10** | Later: NHC coastal watches and warnings, wind speed probability grids, cumulative wind swath, other RSMCs (JMA, BoM, IMD, Météo-France), colour fronts | S2–S4 |

S0 and S6 can start at once, in parallel with S1. S2 is the first slice the
user sees.

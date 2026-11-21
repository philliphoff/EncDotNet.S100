# Design brief: harmonic tide and tidal current predictions in SoundCharts

Status: pre-design (2026-11-11). This is input for a design pass. The pass
should produce mocks in the SoundCharts design canvas
(<https://claude.ai/artifact/2hWbgQ9wKM1gC2gM51mSCs>), on a new **Tide
predictions · design pass** artboard on the *Chart display* page, with
alternatives where this brief asks for them, and a design note,
`tide-predictions-design.md`, that answers each question in §5.

Epic: [#1447](https://github.com/philliphoff/EncDotNet.S100/issues/1447).
Design slice: [#1448](https://github.com/philliphoff/EncDotNet.S100/issues/1448).
The viewer slices that build from this are
[#1453](https://github.com/philliphoff/EncDotNet.S100/issues/1453) (tides) and
[#1454](https://github.com/philliphoff/EncDotNet.S100/issues/1454) (currents).

## 1. What we're adding

SoundCharts adds S-104 water level to charted depths in three places: the
**Depth** card, the route check's under-keel clearance, and chart-wide water
level adjustment ([design note](../chart-wide-water-level-adjustment.md)). The
**DEPTHS** pill says what the chart's depths are relative to
([design note](../depth-basis-indicator.md)).

All three need S-104, and S-104 exists only in a few pilot areas. NOAA
publishes harmonic constituents for about 1,000 tide stations and offsets for
about 2,000 subordinate stations. From those, the libraries will predict the
astronomical tide at any station for any time (#1449, #1450), and feed it to
the same three places where no S-104 covers the point (#1451). Tidal current
stations follow (#1454).

A prediction is not a forecast. It leaves out wind, pressure and river flow,
so a strong southerly or a spring freshet can put the real water level
0.5 m or more off it. S-104 forecasts and observations include those effects.
Decisions already taken (epic #1447):

- **S-104 always wins** where both cover a point and time. A prediction only
  fills gaps.
- **Every predicted value is labelled** as predicted, with its station.
- **Same datum rules as S-104.** Predictions are on the station's chart
  datum (MLLW in the US). On a different datum from the depths, they're
  reported and not added, as S-104 is today.
- **Not for navigation**, like the rest of SoundCharts.

## 2. Who uses it, and for what

- A recreational skipper in Puget Sound with NOAA ENCs and no S-104: "Is
  there enough water over the bar at the marina entrance at 1600?" and "When
  is slack at Deception Pass?"
- A passage planner running a route check: "Does the UKC hold along this
  route if I leave at 0800, using the tide as predicted?"
- An agent over MCP asking the same questions.

## 3. What a station has

| Field | Example |
|---|---|
| Name, id | Seattle, 9447130 |
| Kind | Harmonic (reference) or subordinate |
| Reference station (subordinate only) | Seattle 9447130, with offsets: high +0 h 12 m, ×1.02; low +0 h 25 m, ×0.98 |
| Position | 47.6026° N, 122.3393° W |
| Datum and its offsets | MLLW; MHHW 3.44 m above MLLW (fixture) |
| At a time | Height above datum, rising/falling, rate |
| Over a day | Curve, highs and lows (time, height) |
| Current stations (T6) | Flood and ebb directions, speed and direction at a time, slack and max flood/ebb times |

Stations are dense in some places (Puget Sound has dozens, a few km apart)
and sparse on open coasts.

## 4. What exists in the viewer today

Reuse these where they fit; the mocks should look like they belong.

- **NDBC buoy observations**, a row under *Other chart overlays* in the Layer
  stack, off by default, station glyphs at 1:2,000,000 and larger, and a pick
  report (viewer README, "Show buoy observations").
- **S-104 station gauges** (`S104GAUGE_RISING`, `_FALLING`, `_STEADY`,
  `_NODATA`) and the S-104 fixed-station time-series chart in Object
  information.
- **The Depth card** and **Depth over time** chart: Base, Tide, Depth at
  selected time.
- **The DEPTHS pill** and its flyout: *Depths on the chart*, *Water level*,
  *Where water level is applied*.
- **Water level adjustment** in Mariner settings (*Off* / *At view time*),
  adjusted soundings marked ◆, and the dash-dot teal edge of the adjusted area
  (S-104 grid nodes and 10 km round each station).
- **The route check** clearance readout (Charted depth, Water level, UKC) and
  findings, and the route-check section of the Legend tab.
- **The Timeline** and time HUD: lanes per layer, *Live* and *Pinned*, time
  window presets (*Now ± 6 h*, *Today*, *Next 48 h*, *This run*, *All loaded*,
  *In view*).
- **The Library**, with online sources and a curated directory of known
  sources.
- **Display controls** sections shown only while a product is loaded
  (Weather, Bathymetry, AML).

## 5. Questions for the design pass

Answer each with a recommendation. Where the brief asks for alternatives,
mock at least two.

1. **Turning it on.** Where does a mariner turn predicted water level on as a
   source: in Mariner settings beside *Water level adjustment*, in the DEPTHS
   flyout, or both? On or off by default? Does turning on the station overlay
   (Q2) have anything to do with it, or are they independent?
2. **Stations on the map.** How are tide stations drawn: a new row under
   *Other chart overlays* like NDBC, or a Display controls section? What does
   a station glyph show at the view time (height, rising/falling)? How are
   harmonic and subordinate stations told apart, and does a mariner need to
   tell them apart on the map at all? How does it declutter in Puget Sound at
   1:250,000? *Mock alternatives.*
3. **Station pick.** The Object information card: how wide a curve (a day? ±
   12 h round the view time?), how the next highs and lows are listed, where
   the datum, reference station and "predicted, not a forecast" note go, and
   how a station that also has S-104 says which one SoundCharts uses.
4. **Telling predicted from S-104.** In the DEPTHS pill and flyout, the Depth
   card, the route-check clearance readout and findings, adjusted soundings
   (◆) and the adjusted-area edge: how does the mariner see that a number is
   predicted rather than S-104, without a second visual language? *Mock
   alternatives for the pill and the adjusted-area edge.*
5. **Reach and the adjusted-area edge.** A prediction applies within some
   distance of its station (10 km today for S-104 stations). With many
   stations, the 10 km circles merge into a blob. Is that edge still useful,
   and how should it look when S-104 grid coverage and prediction circles
   overlap? Should a mariner be able to see which station covers where?
6. **Timeline.** Predictions have no end. Do they get a lane? How do they
   interact with *All loaded* and *In view* so they don't stretch the axis to
   infinity? Does *Live* make more sense as the default when only predictions
   are present?
7. **Catalogue and freshness.** The station catalogue is cached offline.
   Where does a mariner see its age and refresh it: the Library (as a known
   source), Settings, or the DEPTHS flyout? Does it need to be visible at all
   until it's stale?
8. **Tidal currents (T6).** Current stations at the view time with the S-111
   arrow vocabulary, and a pick card with the next slacks and maxima. How do
   they sit beside loaded S-111 grids without looking like the same thing?
9. **MCP and CLI.** Any wording or field names the design changes in
   `list_tide_stations`, `predict_tide` and `sample_coverage`'s `source`
   (#1452). Mock the agent-facing JSON only if the design changes it.

## 6. Out of scope

- The engine's maths and the NOAA client (#1449, #1450).
- Using currents in the route check's ETA and speed over ground.
- Storm surge and other non-astronomical forecasts.

## 7. Fixtures

Use Puget Sound: Seattle 9447130 (harmonic), with subordinate stations such
as Shilshole Bay and Edmonds, and Admiralty Inlet and Deception Pass for
currents. Heights, offsets and times in the mocks are fixtures; label them as
such where it matters.

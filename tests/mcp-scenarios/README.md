# Salish Sea MCP scenarios

Agent-style test cases that drive `s100 mcp serve` (and, for S9, SoundCharts)
across several products and data sources at once, on real NOAA ENCs. Each
scenario is a prompt an agent could be given, the tool sequence a good answer
needs, the fusion seams it crosses, and assertions with an oracle where one
exists. They double as SoundCharts showcases.

`harness/run_scenarios.py` runs S1–S3 and prints PASS, FAIL or SKIP per
assertion. A FAIL is a behaviour the scenario expects and the server doesn't
give yet; see [FINDINGS.md](FINDINGS.md). Nothing here is for navigation.

## Run

```bash
pip install fiona shapely numpy scipy rasterio   # only for re-planning routes (harness/grid.py, plan.py)
S100=path/to/s100 ENC=path/to/enc-folder TIDES=path/to/catalogue.json \
  python3 harness/run_scenarios.py            # or: ... run_scenarios.py S2 S3
```

- `ENC`: a folder holding the NOAA cells below, unzipped (`ENC_ROOT/...`).
- `TIDES`: `tests/datasets/Tides/Stations/noaa-currents.json` covers S2 and
  S3; the full catalogue from `s100 tides update` also covers the San Juan
  current and tide steps (S1 `.cur`, S3.5), which SKIP without it.

| Area | Cells |
|---|---|
| San Juans, Anacortes, Deception Pass, Swinomish | US3WA1EF, US4WA1JH, US4WA1JI, US4WA1JJ, US4WA1KH, US4WA1KI, US5SEAPI, US5SEAPJ, US5SEAQH, US5SEAQI, US5SEAQJ, US5SEARH, US5SEARI, US5SEARJ, US5SEASH, US5SEASI, US5WA1EN–EP, US5WA1FL, US5WA1FN, US5WA1FO, US5WA1GL, US5WA1GN, US5WA1GO, US5WA1HI, US5WA1HK–HO, US5WA1II–IL, US5WA1IN–IQ, US5WA1JN–JP, US5WA1KM–KO |
| Port Townsend to Port Ludlow | US3WA1DF, US4WA1HI, US4WA1HJ, US4WA1II, US4WA1IJ, US5SEAJH, US5SEAJI, US5SEAKG, US5SEAKH, US5SEAKI, US5SEALF–LI, US5SEAMF–MI, US5SEANF–NI |

## Routes

`routes/san-juans-7day.json` and `routes/port-townsend-canal.json` hold the
validated waypoints. They were planned independently of the library: GDAL's
S-57 driver rasterises each cell's DEPARE, DRGARE, LNDARE and point hazards
on a 25 m grid, finest compilation scale wins (`harness/grid.py`), and an A*
search with a clearance penalty joins hand-picked via points (`harness/plan.py`).
The Swinomish Channel leg follows the charted Fairway and DredgedArea on a
5 m grid. `check_route` then validated every leg: no land or drying crossings.
That makes the planner a second, independent reading of the same cells, so
disagreements between it and `check_route` are worth a look.

## Scenarios

### S1 — Seven-day San Juan loop from Cap Sante *(run)*

> Plan a 7-day itinerary for a cruise through the San Juans, starting and
> ending at Cap Sante Marina in Anacortes. Create and validate routes between
> each stop, predict running times, and suggest starting times given
> currents, tides and running speed. Boat: 40' twin diesel, 1.1 m draft,
> 16 kn cruise (8 kn displacement).

Stops: Cap Sante → Friday Harbor → Roche Harbor → Reid Harbor → Fossil Bay
(Sucia) → Cascade Bay (Rosario) → Spencer Spit → Cap Sante.

- **Tools.** `search_features` (resolve stops; "Cap Sante Marina" isn't a
  charted name, so the agent has to land on the Cap Sante Waterway),
  `check_route` per day with draft, speed, safety contour and depth,
  `describe_feature` on area findings, `list_current_stations`,
  `predict_current`, `sample_coverage_along` (S-111), `predict_tide`.
- **Seams.** Mixed 1:12k / 1:45k / 1:180k cells (finest-cell rule for the
  chart *and* the area check); S-57 → S-101 translation; area applicability to
  vessel size; current predictions joined to SOG, which `check_route`
  doesn't do (it uses entered SOG only).
- **Assertions.** Per day: no land or drying crossings; area spans honour the
  finest-cell rule; a large-vessel RNA doesn't headline a pleasure boat's
  route; shoreline construction isn't a safety-contour crossing; currents are
  available for running-time correction.
- **Oracle.** The independent planner; running time = distance / SOG ±
  along-track current component.

### S2 — Port Townsend Canal on a predicted tide *(run)*

> Can a 2.0 m-draft boat get from Point Hudson to Port Ludlow through Port
> Townsend Canal tomorrow with 0.5 m under the keel? When should it leave?

- **Tools.** `check_route` with `includeSamples` and `includeWindow`,
  `list_tide_stations`, `predict_tide`.
- **Seams.** S-101 charted depth + predicted water level (no S-104) per
  sample at its ETA; station hand-off between a harmonic and a subordinate
  station; MLLW on both sides; the departure-window sweep across a
  water-level gap.
- **Assertions.** Every sample's water level equals `predict_tide` for the
  same station and time (≤ 1 cm); a gap in water deep enough at any tide
  doesn't empty the window; the control sweep before the gap finds one;
  floating obstructions aren't described as "-15.0 m".
- **Oracle.** `predict_tide`, and UKC = charted + predicted − draft
  recomputed per sample (agreed to the centimetre in this run).

### S3 — Return through Deception Pass and the Swinomish Channel *(run)*

> Day 7: go home the long way, through Deception Pass at slack and up the
> Swinomish Channel. When do we leave Spencer Spit, and is there water in the
> channel?

- **Tools.** `predict_current` (events), distance-to-pass from the route,
  `sample_coverage` S-111 (prediction fallback), `query_features` for
  `Fairway` and `DredgedArea` to build the channel centreline, `check_route`.
- **Seams.** Current-station fallback radius vs. local flow; route ETA joined
  to current events; tide fallback where there's no nearby station.
- **Assertions.** Departure = slack − distance / SOG; S-111 at the pass is
  about 0 kn at slack and matches `predict_current`; the pass's peak isn't
  carried ~10 km into Skagit Bay; the centreline has no land crossings;
  Swinomish UKC has a water level (needs La Conner, 9448794).

### S4 — Canada variant: Friday Harbor to Bedwell Harbour and Victoria *(proposed)*

Leaves US ENC coverage at the boundary. Expect `data.outside_chart_coverage`
where the track leaves DataCoverage, and a clean result once CHS cells are
loaded alongside (mixed producers, overlapping usage bands). A quick probe
gave `data.no_charted_depth` on Canadian legs with no
`outside_chart_coverage`; it needs a proper route before it counts.

### S5 — Weather window for the Rosario Strait crossing *(proposed)*

Load a GFS/GFS-Wave GRIB2 for the Salish Sea, set
`set_route_weather_settings` (20 kn, 1.0 m) and pick the day and hour for
Thatcher Pass → Anacortes. Seams: weather at ETA along the route, interplay
with tide-current timing (wind against an ebb in Rosario Strait).

### S6 — Warnings in force at the ETA *(proposed)*

A synthetic S-124 (e.g. Friday Harbor ferry terminal works, 08:00–17:00
local) on day 1. Assert `in_force` at an ETA inside the window and
`not_in_force` when departure shifts it outside; S-124 time zones vs UTC
ETAs.

### S7 — Anchoring at low water *(proposed)*

For Reid Harbor and Fossil Bay: `nearest_features` for `MooringBuoy` and
`AnchorageArea`, `predict_tide` extremes for the night, and the least depth
in a 40 m swing circle (`query_features` DepthArea/Sounding). Assert depth at
the night's lowest low ≥ draft + 1 m. Seams: S-101 soundings vs depth areas,
subordinate-station low-water offsets.

### S8 — Pick report with enrichment *(proposed)*

`identify_features` at Roche Harbor marina, plus the Coast Pilot enrichment
(the pick-report epic). Assert the marina is identified despite no
`HarbourFacility` in the cell, and that enrichment text is attributed and
kept apart from charted attributes.

### S9 — Host parity: SoundCharts vs `s100` *(proposed, SoundCharts)*

Build S1's day 4 with `create_route`, `append_waypoint` and `set_passage`, turn
on `set_check_mode`, and compare `get_findings` with headless `check_route`
for the same polyline and settings. This tests the drift listed in
`docs/design/route-check-architecture.md` §1.2 (keel rule, missing safety
settings, warnings set, datasets used, clearance findings).

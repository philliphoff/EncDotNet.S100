# Findings, 17 November 2026

Run of S1–S3 with `s100` 0.29.2 (nightly, linux-x64), `s100 mcp serve` over
stdio, 67 NOAA ENC cells downloaded 17 November 2026, and the repo's
`tests/datasets/Tides/Stations/noaa-currents.json` catalogue. 22 assertions
passed, 16 failed, 10 skipped (skips need the full tide catalogue).

## Works as it should

- **Tide fusion into UKC.** Over the Port Townsend Canal route, every sampled
  water level in `check_route` matched `predict_tide` for the same station
  and time within 0.5 cm, and UKC recomputed as charted + predicted − draft
  matched to the centimetre. The hand-off from PORT TOWNSEND (harmonic) to
  Port Ludlow (subordinate, via Seattle) is clean, and datums are reported
  as MLLW on both sides.
- **Current fallback at the station.** `sample_coverage` S-111 at Deception
  Pass equals `predict_current` exactly (0.02 kn at slack, 5.37 kn at max
  flood, 6.32 kn at max ebb).
- **Chart check on mixed scales.** All seven planned San Juan days came back
  with no land or drying crossings, in agreement with an independent GDAL
  reading of the same cells. The one disagreement (a 0.01 NM clip of
  LandArea at the Cap Sante Waterway mouth) was the planner's 25 m grid, not
  the check.

## Problems

### 1. A current station's prediction is applied 10 km away, across basins (S3.3)

At 19:54Z on 18 July 2027, the Deception Pass station's 5.37 kn max flood is
returned for Skagit Bay at the Swinomish south entrance (9.9 km) and off Hope
Island (4.8 km), and for Rosario Strait west of the pass (9.5 km). A
constricted pass's current says little about the open water either side of
it. An agent correcting running times with these values would be off by
several knots on an 8 kn boat. The docs describe the 10 km by-water rule, so
this is a design question rather than a bug: a much smaller radius for current
stations, a confidence that falls with distance, or a same-channel test.

### 2. A water-level gap in deep water empties the departure window (S2.4)

Port Townsend Canal route, 2.0 m draft, 0.5 m required UKC, swept every
15 minutes over 24 hours: `windows: []`. The only cause is a 1.2 NM
`no_water_level_coverage` gap (6.0–7.2 NM, between the reaches of the two
stations) in 9.1 m of charted water, which clears the keel at any tide. The
same sweep on the legs before the gap finds a window from 19:00Z. Suggest
treating a gap as passable when charted depth alone (or with the lowest
predicted low) meets the setting, or reporting lower-confidence windows
instead of none.

### 3. Area findings don't follow the finest-cell rule (S1.d4.areas)

Day 4 (Reid Harbor → Sucia): the US4WA1KH (1:45k) RestrictedArea is reported
from 0 to 9.9 NM, overlapping three US5 (1:12k) findings for the same
Regulated Navigation Area at 2.2–2.5, 3.4–6.7 and 6.7–8.9 NM. The chart check
keeps only the finest cell; the area check doesn't, so the same area is listed
twice where the usage bands overlap. The other days don't show it because
their US5 coverage is continuous.

### 4. A large-vessel RNA makes every San Juan route must-review (S1.*.rna)

Every day's verdict is "Enters restricted area". The areas are
`RESTRN 8` (entry restricted) RestrictedAreas whose information is
"Regulated Navigation Area, 33 CFR 165.1301 and 165.1303", which apply to
large and tank vessels. They cover whole cells, so they swamp the headline
for a 40' boat and hide real findings. Options: grade entry *restricted* (8)
below entry *prohibited* (7), or let a vessel profile (length, tonnage)
filter regulations that don't apply. The Indian Island naval restricted area
on the Port Townsend route is a real `entry prohibited` and should stay
must-review.

### 5. Shoreline construction reported as "Crosses the safety contour" (S1.*.shore)

At every marina end, piers and breakwaters produce
`route_check.crosses_safety_contour` with meta "ShorelineConstruction" and
zero length, several at the same position (three at 11.02 NM on day 2). They
read as depth findings. A structure finding (`structure_on_track` or
similar) fits better.

### 6. Floating obstructions shown as "-15.0 m" (S2.6)

Log booms and floating breakwaters (S-57 WATLEV 7) get the DCEG default
clearance depth of -15 m (`S57HazardDepths.DefaultClearanceDepth`), which is
correct as a portrayal default, but the finding's meta prints it as a depth:
"Obstruction · 4 m to starboard · -15.0 m · shallower than draft". Saying
"floating" (as "covers and uncovers" is said for WATLEV 4) would be clearer.

### 7. Smaller observations

- Without `safetyContourMeters` and `safetyDepthMeters`, `check_route` over
  MCP skips the depth and danger checks without saying so in `checks`. With
  them, the Port Townsend route has 47 danger findings; without, none. This
  is the drift already listed in `route-check-architecture.md` §1.2.
- `search_features` returns the same object once per overlapping cell (4 hits
  for 2 objects at Friday Harbor). A `finestOnly` option would save agents
  the de-duplication.
- `check_route` ETAs use entered SOG only, so "running time given currents"
  is entirely the agent's job. A `currents: "predicted"` option on
  `check_route` (or a passage-time tool) would make S1's ask a single call
  and keep the arithmetic consistent across hosts.
- Canada probe (S4): legs past the boundary gave `data.no_charted_depth` but
  no `data.outside_chart_coverage`. The probe route also crossed land, so this
  is unconfirmed.

## Not tested here

The cloud session couldn't reach NOAA CO-OPS, so there was no full tide
catalogue: no current stations in the San Juans (S1 `.cur`) and no La Conner
tide station for the Swinomish UKC (S3.5). Run `s100 tides update` and the
harness again to cover them. SoundCharts-only tools (S9) weren't run.

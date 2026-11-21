# Findings

## Rerun, 20 November 2026: s100 0.30.1

Same 67 NOAA cells and catalogue. The harness is on the 0.30 API (`sample`,
`get_navigability_grid`, and `check_route`'s `vessel`, `limits`, `samples` and
`window` objects), with assertions added for the new tools, on top of the
agent commits that set the S1 boat's safety contour and depth to 1.5 m and
routed the Cap Sante ends round the marina breakwater. **65 passed, 0
failed, 9 skipped** (was 22 / 16 / 10 on 0.29.2).

| Before (0.29.2) | Issue | Now (0.30.1) |
|---|---|---|
| Deception Pass's 5.37 kn carried 10 km into Skagit Bay and Rosario Strait (S3.3) | #1545 | Only the narrows get the station; 4.8–9.9 km out returns no current |
| Departure window empty because of a deep-water gap (S2.4) | #1544 | Window 19:00Z–11:30Z, matching the control sweep |
| Coarse RNA span overlapping finer cells on day 4 (S1.d4.areas) | #1543 | No overlapping spans on any day |
| Every day must-review for a large-vessel RNA (S1.*.rna) | #1546 | `entry restricted` is now `route_check.restricted_area` (check) |
| Berth pontoons as zero-length safety-contour crossings (S1.*.shore) | #1548 | One `route_check.berth_structures` (info) per end, with `isBerth`, `isStructure` and `cutByTrack` in details |
| Floating obstructions as "-15.0 m" (S2.6) | #1547 | "floating" |
| 4 search hits for 2 objects (S1.2) | #1549 | 2 with `finestOnly` |

New capabilities, all verified against an independent source:

- **`sample` depth** (#1557) at 105 clearance samples across the seven days
  is never shoaler than `check_route`'s sample there (which keeps the
  shoalest of its stretch). With a time, its water level matches the route
  sample to the centimetre.
- **`get_navigability_grid`** (#1558): the San Juans in one call (1046 × 1178
  cells at 50 m, 18 s). Against the harness's GDAL grid, land and water agree
  on 99.75 % of cells and navigable / not on 98.95 %. Every disagreement is
  within two cells of an edge, and the server is the cautious side (it takes
  the least depth of a cell's centre and corners). `connect` joins every
  day's ends.
- **Tidal-gate timing** (#1561): with a slack-within-15-minutes gate at
  Deception Pass, the window is 15:05–15:35Z with best departure 15:20Z,
  exactly the hand calculation; the gate ETA is 20 seconds from predicted
  slack. `applyCurrents` changes running time only on the legs near the
  pass, now that currents stay local.

### What the new checks turned up

- **Two of my routes had real problems, in both versions.** Day 2 cut the
  Friday Harbor breakwater 0.5 NM out and crossed a 0–1.8 m depth area by
  awash rocks 3.6 NM out; Day 4 passed 15 m from a charted wreck leaving Reid
  Harbor. 0.29.2 reported all three; the old harness asserted only on land
  and drying crossings, so it missed them. Both days are now re-planned with
  an A* search over the server's navigability grid and have no must-review
  chart findings more than 0.3 NM from either end. (Those legs are no longer
  independent of the server; the other days still come from the GDAL
  planner.)
- **Marina approaches.** At a 3 m safety contour, Cap Sante's basin (dredged
  0.4–1.6 m) and Fossil Bay (1.8 m) made every day's last few hundred metres
  must-review. The 1.5 m contour the agents chose for the S1 boat removes
  that; the harness still ignores chart findings within 0.3 NM of either end.

### Remaining gaps

- **No snap to navigable water** (#1559, open). `connect` answers
  `from_not_navigable` or `to_not_navigable` for four of the seven days
  because the anchorage or marina waypoint is shoal. The harness snaps to the
  nearest navigable cell itself (50–420 m).
- **No route suggestion** (#1560, open). Re-planning Days 2 and 4 still took
  a client-side A* over the grid file.
- **Berth radius vs large marinas.** Roche Harbor's docks start 128 m from
  the arrival point, past the 100 m default, so two pontoons there are still
  must-review findings (titled for the structure). Working as documented; a larger default, or a
  radius taken from the harbour area, would suit big marinas.
- **Out-of-reach current sample error.** `sample` current more than the
  station reach from any station returns `no_dataset_covers_point`, which
  reads as a missing dataset rather than "no station within reach".
- **Still skipped:** San Juan currents and the La Conner tide need the full
  catalogue from `s100 tides update`.

## First run, 17 November 2026: s100 0.29.2

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

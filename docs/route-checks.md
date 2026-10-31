# Route checks and under-keel clearance

A route check compares a route with the loaded data. It covers land, drying
ground, the safety contour, charted dangers, regulated and protected areas,
S-124 navigational warnings and chart coverage. With a draft, it also computes
the under-keel clearance (UKC) along the route at each point's ETA. Three
hosts run the same checks and report the same findings and codes:

- **SoundCharts**: **Check route** in the **Routes** panel. See the
  [SoundCharts README](../src/EncDotNet.S100.Viewer/README.md#check-a-route).
- **MCP**: the `check_route` tool. See
  [Check a route](mcp-server.md#check-a-route).
- **The command line**: `s100 route check`, for batch runs and CI. See
  [route check](cli.md#route-check).

In your own code, `RouteChecker` in the `EncDotNet.S100.Navigation` package
runs every check on one `PassagePlan` and returns one `RouteCheckReport`, as
all three hosts do. [How a route check works](how-a-route-check-works.md)
explains which data each check reads, the time and keel rules, the verdict
and each check's status.

The results are an aid for exploring the data, not for navigation.

## What a check uses

- **The route.** Each leg is a rhumb line or a great circle between two
  waypoints.
- **The corridor.** Each leg's port and starboard cross-track limits (XTD)
  bound the area swept beside the track. They're half-widths: how far the
  corridor reaches to each side. A side without a limit uses 50 m. MCP and
  `s100 route check` can replace every leg's limits with one half-width.
- **The safety contour and safety depth.** These are the mariner settings
  that also drive S-101 portrayal. SoundCharts uses **Mariner Settings**. MCP
  and the command line take them as arguments; the command line defaults to
  30 m, the S-101 portrayal default.
- **The draft, squat and required UKC** of each leg. They set the depth a
  leg's dangers are judged against, and the UKC check runs when every leg
  has a draft. See [Keel](how-a-route-check-works.md#keel).
- **The departure and speeds.** Speeds give each point an ETA, and
  warnings, seasonal areas and S-104 water levels are judged at the ETAs.
  See [Time](how-a-route-check-works.md#time) for the time used where there's
  no ETA.

## Finding groups

Each finding is in one of three groups:

| Group | Meaning |
|---|---|
| Must review | Something on the track that needs a decision: land, drying ground, water shallower than the safety contour, a danger inside the limits, a prohibited area, a warning in force on the route, or UKC below your setting. |
| Check | Something inside the limits or near the route that's worth a look, such as shoal ground beside the track, a danger just outside the limits, a caution area or a warning near the route. |
| Info | Context that doesn't need action, such as an area or warning that isn't in force during the passage. |

The summary names the worst group: "2 to review", "1 to check", or "No
findings for your settings". A finding inside the limits but off the track is
one group lighter than the same finding on the track.

Each finding has a code, a title, a meta line (leg, distance, feature and
values), its leg, its distance from departure, a location (a point or a span
of the route) and, when one is behind it, a feature reference you can open
with **Identify** in SoundCharts or `describe_feature` over MCP.

## Land, drying ground and coverage

These checks need no draft and no mariner settings.

| Code | Group | When |
|---|---|---|
| `route_check.crosses_land` | Must review | The track crosses a `LandArea`, or a `Coastline` where no land area is charted. |
| `route_check.crosses_drying` | Must review | The track crosses a `DepthArea` that dries (DRVAL1 below the datum). |
| `route_check.land_in_corridor` | Check | Land reaches inside the cross-track limits, but the track doesn't cross it. |
| `route_check.drying_in_corridor` | Check | The same, for drying ground. |
| `data.outside_chart_coverage` | Check | The track leaves the `DataCoverage` of every loaded cell. |

The charts are S-101 cells, S-401 inland cells and S-57 cells translated to
S-101. Where cells overlap, only the finest cell at a place counts, so a
coarse cell's generalised land doesn't fire where a finer cell charts water.
Crossings of the track are exact. Ground that only reaches into the corridor
is found at cross-sections every 10 m.

## Depths and charted dangers

These checks run when the safety contour and safety depth are both set.

| Code | Group | When |
|---|---|---|
| `route_check.crosses_safety_contour` | Must review | The track crosses depth, dredged or drying areas shallower than the safety contour, or another feature that raises the `SafetyContour` alert. |
| `route_check.safety_contour_in_corridor` | Check | Such areas or features inside the limits, off the track. |
| `route_check.soundings_below_safety_contour` | Check | Soundings no deeper than the safety contour inside the limits, outside water already shallower than it. |
| `route_check.crosses_s102_shoal` | Must review | The track crosses S-102 cells shallower than the safety contour. |
| `route_check.s102_shoal_in_corridor` | Check | Such S-102 cells inside the limits, off the track. |
| `route_check.danger_in_corridor` | Must review | A wreck, obstruction or rock shallower than the threshold, awash, or of unknown depth, inside the cross-track limits. |
| `route_check.danger_near_route` | Check | Such a danger within 0.1 NM beyond the limits. |

Besides depth areas, every feature whose S-101 portrayal raises the
`SafetyContour` alert is safety-contour ground: floating docks, hulks,
pontoons, shoreline constructions, unsurveyed areas, and land charted as
points or curves. An area is crossed or in the corridor as the depth areas
are; a point or curve is crossed when the track passes within the track
clearance (30 m) of it.

Soundings follow the portrayal's `SOUNDG03`: a sounding counts when it's no
deeper than the safety contour. The safety depth only decides how the chart
draws a sounding (bold when shallower), so a soundings finding notes it when
its least depth is also shallower than the safety depth.

A danger is a `Wreck`, an `Obstruction` or an `UnderwaterAwashRock`:

- Its depth is its `valueOfSounding` (VALSOU). Without one, it's the
  `defaultClearanceDepth` S-101 2.0 gives instead, as the portrayal's
  `OBSTRN07` and `WRECKS05` take it, unless the danger reaches the surface.
  The S-57 translation fills that attribute in for hazards without VALSOU.
- One without a charted depth (above water, covering and uncovering, awash,
  or of unknown depth) always counts.
- One with a depth counts when it's no deeper than the safety contour, as
  the portrayal's `UDWHAZ05` judges it, or, on a leg with a draft, when
  it's shallower than that leg's draft + squat + required UKC.
  The meta line names the threshold that fired, the draft one when both do.
- A danger counts wherever it lies, also inside water already shallower
  than the safety contour, where the safety-contour finding covers the
  stretch: `UDWHAZ05` raises its alert either way.

A danger finding is located at the danger itself, and its distance is the
point on the route nearest to it. A danger near a turn is reported once, on
the closer leg. Shoal soundings or S-102 cells less than 100 m apart along the
route make one finding.

## Navigational hazards

Buoys, beacons, piles, platforms, bridges and other features whose S-101
portrayal raises the `NavHazard` alert are reported by where they lie from the
track. These checks need no depth settings.

| Code | Group | When |
|---|---|---|
| `route_check.aid_to_navigation` | Check or Info | An aid to navigation (a buoy, beacon, daymark, light float or vessel, or AIS aid) inside the cross-track limits; Info within 0.1 NM beyond them. |
| `route_check.structure_on_track` | Must review | A structure, such as a pile, dolphin, platform or fishing facility, within the track clearance (30 m) of the track line. |
| `route_check.structure_in_corridor` | Check or Info | A structure elsewhere inside the limits; Info within 0.1 NM beyond them. |
| `route_check.overhead_on_route` | Check or Info | A bridge, span, overhead cable or pipeline, or conveyor inside the limits; Info within 0.1 NM beyond them. |

The route check runs the S-101 portrayal rules on the features in each leg's
corridor, at the check's safety contour, and keeps the alerts they raise, so a
hazard counts exactly when an ECDIS would raise the alert for it. Wrecks,
obstructions and rocks are the dangers above. A feature type the check
doesn't know is treated as a structure. The track clearance and the 0.1 NM
near band are settings (`--track-clearance` and `--nearby` in
`s100 route check`, `trackClearanceMeters` and `nearbyDistanceMeters` in
`check_route`). Overhead features aren't judged against an air draft yet.

## Regulated and protected areas

These checks need no draft. An area's date range or season is judged at the
ETAs where it meets the route.

| Code | Group | When |
|---|---|---|
| `route_check.enters_prohibited_area` | Must review | The track enters an S-101 area where entry is prohibited or restricted. |
| `route_check.prohibited_area_in_corridor` | Check | Such an area inside the limits, off the track. |
| `route_check.restricted_area` | Check, Info or Must review | Another S-101 restricted or military practice area, offshore production area, seaplane landing area or marine farm; Info when its restrictions don't bear on a passage, such as fishing or anchoring prohibited; Must review when the track enters an area to be avoided. |
| `route_check.caution_area` | Check | A caution area. |
| `route_check.traffic_area` | Check, Info or Must review | A precautionary area or submarine transit lane; Info for an inshore traffic zone; Must review when the track enters a traffic separation zone. |
| `route_check.anchorage_area` | Check | An anchorage area. |
| `route_check.protected_area` | Check or Info | An S-122 marine protected or restricted area. It's never Must review. |
| `route_check.area_not_in_force` | Info | The area's date range or season doesn't cover the ETAs. |
| `route_check.area_kind_off` | Info | The route meets areas of a kind whose indication is turned off; one finding per kind. |

The S-101 areas are the ones whose portrayal raises the `ProhAre` alert, in
the 13 kinds of the S-101 alert catalogue, plus precautionary areas. Areas
charted as points count too. Each kind can be turned off (`--areas-off` in
`s100 route check`, `areaKindsOff` in `check_route`; S-164 calls this the
user selection of prohibited areas). The areas of a kind that's off aren't
reported; instead one finding, worded as the catalogue words it, says the
indication of some areas is off, with the kind and how many the route meets.
The kinds are `traffic_separation_zone`, `inshore_traffic_zone`,
`restricted_area`, `area_to_be_avoided`, `particularly_sensitive_sea_area`,
`caution_area`, `offshore_production_area`, `user_area_to_be_avoided`,
`military_practice_area`, `seaplane_landing_area`, `submarine_transit_lane`,
`anchorage_area` and `marine_farm`.

## S-124 navigational warnings

Each part of every loaded S-124 warning is compared with the corridor. A
warning **crosses** the route when its line or area reaches the corridor. It's
**near** the route when it's within 0.5 NM of the track, or when it's a point
inside the corridor. Whether it applies is judged at the ETA where the route
meets it, from its validity, its cancellation date, cancelling warnings and
the latest in-force bulletin.

| Code | Group | When |
|---|---|---|
| `s124.on_route` | Must review | The warning crosses the route and is in force while the vessel is there. |
| `s124.near_route` | Check | The warning is near the route and in force while the vessel is there. |
| `s124.not_in_force` | Info | The warning is on or near the route, but not in force while the vessel is there. |
| `s124.cancelled` | Info | The warning is on or near the route, but cancelled by the time the vessel is there, or left out of the latest in-force bulletin. |

Each S-124 finding also carries the warning: its number, title, locality,
validity, text, and when the vessel reaches and leaves it.

## Under-keel clearance

With a draft on every leg, the UKC check samples the route and computes, at
each sample's ETA:

UKC = charted depth + water level − draft − squat

and compares it with the required UKC (0 m when none is set).

- **Charted depth** comes from S-102. Where there's no S-102, or a cell has
  no data, it comes from S-101: the shallowest covering dredged area, then
  depth area (DRVAL1), then a sounding within 250 m.
- **Water level** comes from S-104 at the ETA, interpolated between time
  steps: grids first, then the nearest station within 10 km. Outside the
  S-104 extent or time range there's no water level, and the stretch is a
  gap.
- **Datums.** When the depth and water-level vertical datums differ, the
  water level is reported but not applied, and the stretch is a gap.
- **Squat** is a fixed allowance. It isn't modelled from speed, and S-111
  currents aren't applied to ETAs.

Each sample has a state:

| State | Meaning |
|---|---|
| Meets your settings | UKC is at least 0.5 m above the required UKC. |
| Tight | UKC is less than 0.5 m above the required UKC. |
| Below your UKC setting | UKC is below the required UKC. |
| No data | No charted depth, or no water level to apply. |

The verdict is the first of these that applies: water level ends before
arrival, below your UKC setting, incomplete data, meets your settings but
tight, meets your settings. A departure window sweeps departures across the
S-104 time range and lists the runs of departures that meet your settings.
For how samples are spaced and what the result reports, see
[Under-keel clearance](mcp-server.md#under-keel-clearance).

## Confidence

Confidence is reported separately from the state. A stretch can meet your
settings and still be lower confidence. The reasons are:

| Reason | Meaning |
|---|---|
| `charted_depth_from_s101` | The depth is from S-101 because no S-102 covers the stretch. |
| `datum_unconfirmed` | The depth or water-level datum isn't declared, or isn't an S-100 code, so they can't be confirmed to share a reference. The water level is applied anyway. |

SoundCharts draws a lower-confidence stretch in its state's colour, dashed.
The verdict follows the clearance; the confidence reasons qualify it and
never replace it.

## Clearance findings

The UKC check turns the stretches behind its verdict and confidence into
findings:

| Code | Group | When |
|---|---|---|
| `clearance.below_setting` | Must review | A stretch where UKC is below the required UKC. |
| `confidence.charted_depth_from_s101` | Check | A stretch where the charted depth is from S-101 because no S-102 covers it. |
| `confidence.datum_unconfirmed` | Check | A stretch where a depth or water-level datum isn't declared, so they can't be confirmed to match. |
| `data.water_level_ends` | Check | The water-level data ends before the vessel reaches a stretch, so the water level isn't applied there. |
| `data.water_level_starts_late` | Check | The water-level data starts after the vessel passes a stretch. |
| `data.no_water_level_coverage` | Check | A stretch outside every water-level dataset's coverage. |
| `data.water_level_no_data` | Check | A stretch inside water-level coverage where the data has no value. |
| `data.datum_mismatch` | Check | The water level's datum differs from the charted depth's, so it isn't applied. |
| `data.no_charted_depth` | Check | A stretch with no charted depth from S-102 or S-101. |

## Received routes and Copy to edit

A route opened from a file is a **received** route. It's read-only, so a check
reports on the route as its author planned it.

- Only your check settings can change: draft, squat, required UKC and
  departure. They're yours, not the route's. SoundCharts keeps them on your
  computer under the route ID and edition (an RTZ route's under its file
  path), and exports them only when you ask.
- Speed and cross-track limits are route values. They stay as the file gives
  them.
- **Copy to edit** makes an owned, editable copy with a new route ID, a
  reference to the source's ID and edition, edition 1 and status Planned.
  What SoundCharts' route can't hold is kept and written back when you export
  the copy in the same format.

`s100 route check` reads the file's values and applies any options you pass
to every leg, so a CI run checks the route as it was received unless you say
otherwise.

## Route file formats

| Format | Versions | Read | Write |
|---|---|---|---|
| S-421 (GML) | 1.0 | SoundCharts, MCP, `s100 route check` | SoundCharts, MCP |
| RTZ (IEC 61174 Annex S) | 1.0, 1.1, 1.2 | SoundCharts, MCP, `s100 route check` | SoundCharts, MCP (1.2 by default) |

Both formats give waypoints, leg geometry (rhumb line or great circle),
cross-track limits, draft, static UKC, speeds and the departure. Neither has a
squat allowance. Speeds and the departure come from the route's planning
schedule: the first calculated schedule, else the first manual one.

RTZ differs from S-421 in a few ways:

- A waypoint's `leg` in RTZ describes the leg arriving at it, so leg `i` is
  read from waypoint `i+1`.
- Cross-track limits are nautical miles in RTZ and metres everywhere else.
- Draft is the deeper of the forward and aft draughts.
- A leg with no speed takes the speed its ETD and ETA imply.
- RTZ has no route ID or edition, so an RTZ route is known by its file.

The `EncDotNet.S100.Routes.Rtz` package reads and writes RTZ, and
`EncDotNet.S100.Datasets.S421` reads and writes S-421.

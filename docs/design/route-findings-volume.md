# Route-check findings at alert-catalogue volume

> Status: **Proposed** (2026-10-30), for
> [#1127](https://github.com/philliphoff/EncDotNet.S100/issues/1127). Nothing
> here is built yet. The #1127 slices should build the model fields in §5 as
> they go, so the hosts can follow without another engine change.

[#1127](https://github.com/philliphoff/EncDotNet.S100/issues/1127) drives the
route checks from the S-101 alert catalogue: every feature whose portrayal
rule adds `AlertReference:NavHazard`, `ProhAre` or `SafetyContour` must be
indicated when it's in or near the corridor. That's right for S-164 Parts 5–7,
but it multiplies the findings on an ordinary route. This note measures by how
much, and proposes how the findings list, the map and the MCP and `s100`
output stay usable without hiding anything the catalogue alerts on.

[Route checks](../route-checks.md) describes the checks as they are, and
[Route check architecture](route-check-architecture.md) the engine. The
findings list and map follow the #958 design round (decisions 6 and 7, the
`HANDOFF.md` on branch `claude/route-checks-design`), shipped in #959. The
mocks for this round are the **Route Checks · Volume (round 2)** artboard
beside round 1's on the *Navigation & time* page of the SoundCharts design
canvas (<https://claude.ai/artifact/2hWbgQ9wKM1gC2gM51mSCs>): the list and map
at Solent volume (3a, 3b), the check settings (3c), the counts (3d) and the
row and map-mark components (3e).

## 1. Problem

Today a finding is one row, and the list's only volume control is the #959
rule that Check and Info findings with the same code merge into one row
("×N"). Must review never merges. The map draws a stemmed marker per point
finding and clusters markers within 24 px.

With #1127:

- **NavHazard** grows from three feature types (`Wreck`, `Obstruction`,
  `UnderwaterAwashRock`) to 44 rule files. Most are aids to navigation and
  fixed structures, which a buoyed channel passes by the dozen. `SOUNDG03`
  also alerts on **every sounding** no deeper than the safety contour.
- **ProhAre** grows from eight hand-picked kinds to the catalogue's 13
  (viewing groups 53012–53024), including point-charted areas, with per-kind
  selection.
- **SafetyContour** adds floating docks, hulks, pontoons, shoreline
  constructions, unsurveyed areas, and land charted as points and curves.
- Since #1151, a danger inside water already shallower than the safety contour
  is its own finding next to the crossing.

S-98 (from MSC.530(106)) rates all three alerts as **Indication** in route
planning. In route monitoring the catalogue makes the safety contour an Alarm,
and NavHazard and ProhAre Warning or Caution by the mariner's choice
(`AlertCatalog-S101.xml`). SoundCharts only plans, so every one of these is an
indication: something to show, not something to interrupt with.

## 2. How many

A scratch harness ran the S-101 portrayal with a tap on `AlertReference`
(outside the repository), and counted alerted features by their distance from
the track. Where cells overlap, only the finest cell at a feature counts. The
safety contour and safety depth are 10 m. "Near" is the 0.1 NM band beyond the
limits that `danger_near_route` uses. The routes are rough: the Solent and
Charleston tracks cut a few corners over land, which inflates the crossings
but not the counts of buoys and structures.

| Route | Today: findings (Must review / Check / Info) | Alerted features, 185 m half-width: in / near | Same at 0.5 NM half-width: in |
|---|---|---|---|
| S-164 NAVHZ (98 NM, 17 legs, test cell) | 50 (47 / 3 / 0) | 97 / 0 | 97 |
| Solent, Nab to Southampton (24 NM, 8 legs, UKHO S-101 trial) | 95 (41 / 40 / 14) | 371 / 306 | 1,865 |
| Charleston entrance (17 NM, 6 legs, NOAA S-57 translated) | 43 (21 / 16 / 6) | 86 / 26 | 197 |

By alert and kind, at a 185 m half-width (in + near):

| | Solent | Charleston |
|---|---|---|
| NavHazard: soundings ≤ safety contour | 222 + 202 | 12 + 7 |
| NavHazard: aids to navigation (buoys, beacons, light floats, AIS) | 22 + 20 | 5 + 7 |
| NavHazard: fixed structures (piles, dolphins, oil barriers) | 5 + 34 | 0 |
| NavHazard: bridges and spans | 0 | 2 + 0 |
| NavHazard: wrecks, obstructions, rocks (today's dangers) | 1 + 2 | 5 + 3 |
| ProhAre areas (restricted, caution, military, anchorage, mooring) | 46 + 11 | 17 + 0 |
| SafetyContour features (depth, dredged, shoreline, pontoon, land) | 75 + 37 | 45 + 9 |

What this says:

1. **Soundings dominate.** They're 83 % of the NavHazard alerts on the
   Solent route and 46 % at Charleston. A sounding no deeper than the safety contour almost always lies in a
   depth area that is itself shallower than the contour, so it sits inside a
   stretch the safety-contour check already reports.
2. **Aids and structures are the real growth.** On the Solent route they add
   about 80 features within 0.1 NM of the limits, and over 200 at 0.5 NM.
   Today none of them is a finding.
3. **Today's dangers barely move** on real routes: 1–5 in the corridor. They
   stay the items that most need individual attention.
4. **Areas are already numerous.** Solent has 56 area findings today (21 of
   them caution areas). The catalogue adds kinds more than it adds features,
   so per-kind selection is the lever there.
5. **Safety-contour features are spans, not points.** Shoreline
   constructions and pontoons join the depth areas the crossing and
   in-corridor spans already merge.

One row per alerted feature would take the Solent list from 95 findings to
over 600 at a 185 m half-width. If every NavHazard inside the limits were Must
review, as dangers are today, Must review would go from 41 to nearly 300. That's
not usable, and it would bury the one danger that matters among the buoys
marking the channel.

## 3. Constraints

- **Every alerted feature is indicated.** S-164 Parts 5–7 compare the
  indicated features with reference plots. Each alerted feature in or near the
  corridor must be highlighted on the chart and reachable from the findings,
  with its feature reference. A list row can stand for many features. A
  feature can't be dropped.
- **Only the catalogue can turn an alert off.** The catalogue lets the mariner
  turn off highlighting for the safety contour and NavHazard as a whole, and
  for each ProhAre viewing group. Each off state has a message to show
  ("Indication of navigational hazards is Off" and the like). Anything else
  SoundCharts offers to reduce volume is presentation: it can fold rows, never
  stop indicating.
- **One engine, three hosts.** SoundCharts, MCP and `s100 route check` report
  the same findings and codes ([Route check
  architecture](route-check-architecture.md)). Grouping must come from the
  engine, not be rebuilt in each host.
- **Codes are stable.** New behaviour gets new codes or fields, not changed
  meanings for old codes.
- **Must review stays short and specific.** The verdict names the worst item
  (#959 decision 3). Nothing new should reach Must review unless the mariner
  must decide something about it.

## 4. Proposal

Separate **indications** from **rows**. The engine reports one finding per
indication, as today, and the same engine groups them into rows by rules all
hosts share. The list shows rows; the map shows every indication.

### 4.1 Hazard classes

Split NavHazard by feature type into classes, because what the mariner does
about a buoy differs from what they do about a wreck:

| Class | Feature types | What it asks |
|---|---|---|
| Danger | `Wreck`, `Obstruction`, `UnderwaterAwashRock`, and soundings | Is there water over it for my keel? |
| Aid to navigation | Lateral, cardinal, isolated danger, safe water, special purpose, installation, mooring and emergency wreck marking buoys and beacons; `Daymark`, `LightFloat`, `LightVessel`, `PhysicalAISAidToNavigation`, `VirtualAISAidToNavigation` | Do I pass it on the right side? |
| Structure | `Pile`, `Dolphin`, `Bollard`, `PylonBridgeSupport`, `OffshorePlatform`, `WindTurbine`, `OilBarrier`, `FishingFacility`, `LogPond`, `IceArea`, `CableSubmarine`, and `Landmark`, `Building`, `SiloTank`, `Crane`, `FortifiedStructure`, `StructureOverNavigableWater` when in the water | Do I keep clear of it? |
| Overhead | `Bridge`, `SpanFixed`, `SpanOpening`, `CableOverhead`, `PipelineOverhead`, `Conveyor` | Do I fit under it? |

The class comes from the feature type, in a table the engine owns. A type
the table doesn't know is a Structure, so a new catalogue rule is never lost.

### 4.2 Groups by class and position

| Class | On the track | Inside the limits | Near (0.1 NM band) |
|---|---|---|---|
| Danger | Must review | Must review | Check |
| Aid to navigation | Check | Check | Info |
| Structure | Must review | Check | Info |
| Overhead | Check, or Must review below the air draft (later) | Check | Info |

"On the track" is a new, narrow test: the feature lies within the **track
clearance** (a setting, default 30 m) of the track line. A buoy in the
corridor is normal; a buoy the track runs over isn't. Dangers keep today's
depth rules and #1151.

Overhead features stay Check until the passage has an air draft. With one,
a bridge or cable whose vertical clearance is below air draft + margin, or
unknown, becomes Must review. That's a separate issue; the class makes room
for it.

### 4.3 Fold into spans

- **Soundings** no deeper than the safety contour become members of the
  safety-contour span (crossing or in corridor) they lie in. A sounding
  outside every such span makes or joins a `soundings_below_safety_contour`
  stretch, merged within 100 m as soundings are today. The list never shows a
  row per sounding.
- **Dangers inside a safety-contour crossing** (#1151) become members of that
  crossing. The crossing row says what's inside it: "Crosses the 10 m safety
  contour · 1.2 NM · 3 dangers, 41 soundings". Each member is still a
  finding, selectable from the expanded row and highlighted on the map. The
  verdict counts the crossing once.
- **SafetyContour features** (shoreline constructions, pontoons, hulks, docks,
  unsurveyed areas, land points and curves) join the crossing and in-corridor
  spans as land and depth areas do. The span's meta line names the kinds.

Folding into a Must review span is honest: the span already asks for a
review of that stretch, and expanding it lists every member.

### 4.4 Stretch rows

Check and Info findings of one class (or one code, for the existing checks)
on one leg, less than the **stretch gap** apart along the route (default
0.5 NM), make one stretch row: "9 aids to navigation · leg 3 · 1.4 NM". This
replaces #959's whole-route merge by code, which on a long route merges
features 20 NM apart into one row.

Must review rows don't merge, except the folding in §4.3 and Structures: two
or more structures on the track within the stretch gap make one Must review
row ("3 piles on the track · leg 6"), titled by the nearest.

### 4.5 ProhAre kinds

The area check reads the 13 catalogue kinds. Each is on by default and can be
turned off in **Your check settings** (the S-164 "UserSelectionProhibited
Areas" test). The kind decides the group; the existing restriction rules stay
on top of it:

| Kind (viewing group) | Track enters | Only inside the limits |
|---|---|---|
| Restricted area, entry prohibited or restricted (53014) | Must review | Check |
| Area to be avoided (53015) | Must review | Check |
| Traffic separation zone (53012) | Must review | Check |
| Any other kind | Check | Info |

The traffic separation zone and area to be avoided move up from Check: the
track entering either is a decision the mariner has to make. Not in force
during the passage stays Info (`area_not_in_force`). Overlapping areas of one
kind on one stretch make one stretch row, as in §4.4.

When any kind is off, the verdict card's footer and the top of the findings
list show the catalogue's message, "Indication of some prohibited areas or
areas with special conditions is Off", with a link to the setting. MCP and
`s100` report the kinds that are off.

### 4.6 The map

Markers stay for rows. Indications move to the features:

- Every indicated feature gets a **highlight** in its group's tone drawn on
  the feature itself: a ring round a point symbol, a cased outline on a line
  or area. This is the S-164 graphical highlight, and it's what makes the
  list's folding acceptable: nothing is only in the list.
- **Stemmed markers** (#959 C4) are drawn for rows, not indications: one per
  Must review row, one per Check stretch row at its nearest point. A stretch
  row's sleeve covers the stretch.
- Info indications are highlighted only while their row is selected, as
  Info markers are today.
- Selecting a row highlights its members more strongly and fits them.

Highlighted aids to navigation stay readable because the ring is drawn
behind the chart symbol, not over it.

### 4.7 Filters

The source chips (All, Clearance, Route, Warnings) stay. The Route chip
gains a second row of class chips when it's selected: **Dangers**, **Aids**,
**Structures**, **Overhead**, **Areas**, **Depths**. They filter rows only.
A filtered-out class still draws its highlights, and the list ends with
"N rows hidden by filters · Show all".

No class is filtered out by default. Aids and structures are already Check
stretch rows, which keeps the default list short without a default filter
that a new user wouldn't know was on.

### 4.8 What the Solent list would look like

At a 185 m half-width the Solent route has 95 findings and about 50 rows
today, after the #959 merge. With this proposal it has over 600 findings
(indications), and roughly:

- Must review: the same crossings and prohibited areas as today, each with
  its folded dangers and soundings, plus a few structure-on-track rows: about
  45 rows.
- Check: one aids-to-navigation stretch row and at most one structure row
  per leg, plus today's area and in-corridor rows as stretch rows: about 30
  rows.
- Info: folded, as today.

The list grows by about half while the indications grow sixfold, and every
indication is on the map.

## 5. Engine and model

All of this lives in `EncDotNet.S100.Navigation`, so MCP and `s100` group
exactly as SoundCharts does.

### 5.1 New fields on `RouteFinding`

| Field | Type | Meaning |
|---|---|---|
| `Alert` | `RouteFindingAlert?` (`NavHazard`, `ProhAre`, `SafetyContour`) | The catalogue alert behind the finding, or `null` for checks the catalogue doesn't cover (UKC, S-124, data). |
| `HazardClass` | `RouteHazardClass?` (`Danger`, `AidToNavigation`, `Structure`, `Overhead`) | §4.1, for NavHazard findings. |
| `AreaKind` | `int?` | The ProhAre viewing group (53012–53024). |
| `Position` | `RouteFindingPosition` (`OnTrack`, `InCorridor`, `Near`) | Where it lies relative to the track; what §4.2 and §4.5 key on. |
| `ParentId` | `string?` | The span finding it's folded into (§4.3), or `null`. |
| `RowId` | `string` | The row it's shown in (§4.4); its own id when it's a row by itself. |

`Feature` stays required for every indication, so a pick and S-164 tests can
match each one.

### 5.2 Rows

`RouteFindingRows.Build(findings, options)` returns `RouteFindingRow`s: id,
group, code or class, title and meta from the `RouteFindingFormatter`, the
leg and distance span, the member ids, and the counts by feature type for the
meta line. `RouteCheckReport` gains `Rows` next to `Findings`. The viewer's
`FindingsEntry` merge and `merged.{group}.{code}` ids are replaced by these
rows; the `merged.` ids become row ids.

The verdict and summary counts ("N to review") count rows, not findings.
Both are reported, so a host can say "3 to review (12 features)".

### 5.3 New codes

| Code | Group | When |
|---|---|---|
| `route_check.aid_to_navigation` | Check (Info when near) | An aid to navigation in or near the corridor. |
| `route_check.structure_on_track` | Must review | A structure within the track clearance. |
| `route_check.structure_in_corridor` | Check (Info when near) | A structure in or near the corridor. |
| `route_check.overhead_on_route` | Check | A bridge, span, overhead cable, pipeline or conveyor the track passes under or that reaches the corridor. |
| `route_check.soundings_below_safety_contour` | Check | Soundings no deeper than the safety contour outside every safety-contour span. |
| `route_check.area_kind_off` | Info | The route meets an area of a ProhAre kind that's turned off. Carries no feature highlight; it says the indication is off. |

`danger_in_corridor` and `danger_near_route` keep their meaning, for the
Danger class. `soundings_below_safety_depth` is retired: `SOUNDG03` alerts on
the safety contour, not the safety depth. The existing area codes stay, with
`AreaKind` set; a new kind uses the code of its nearest existing kind
(`restricted_area` for offshore production, marine farm and seaplane areas).

### 5.4 Settings

On `RouteCheckSettings`, all with defaults so the CLI and MCP keep working
unchanged:

| Setting | Default | Notes |
|---|---|---|
| `NearbyDistance` | 185.2 m | The "near" band (#1127 asks to expose it). |
| `TrackClearance` | 30 m | "On the track" for aids and structures. |
| `StretchGap` | 926 m | The §4.4 gap. |
| `ProhibitedAreaKinds` | All 13 | Per-kind selection. |
| `NavHazardIndication` | On | The catalogue's NavHazards highlight. Off reports `NavHazardsHighlightOffMsg` and skips the aids, structures and overhead classes. Dangers stay: they're also judged against the keel. |

SoundCharts stores them with the other check settings (#958 decision 16).
MCP `check_route` and `s100 route check` take them as optional arguments.

### 5.5 MCP and `s100`

- `check_route` and `get_findings` return `rows` by default, with member ids
  and per-row counts, and `findings` (every indication) only with
  `detail: "all"`. A long route's full list is too big for a model's context,
  and the rows carry the ids to fetch any member.
- Both report `alertsOff` (the off messages, if any) and counts by alert and
  class.
- `s100 route check` prints rows, and every finding with `--all`. Its JSON
  output always holds both, since CI runs want the complete set.

## 6. Alternatives considered

- **Filter aids to navigation out by default.** It cuts the list most, but
  it's a hidden setting that changes what "checked" means, and S-164 expects
  the indications. Folding into stretch rows gets most of the reduction
  without hiding anything.
- **Map-only indication for aids to navigation** (no rows at all). Simpler,
  but the list would no longer say that a leg runs past a line of buoys, and
  MCP and `s100` have no map. Stretch rows keep them in every host at low
  cost.
- **Group by alert type only** (Check for NavHazard, Must review for
  SafetyContour). Too coarse: a wreck on the track and a buoy beside it are
  both NavHazard.
- **Per-leg rows for everything.** Readable for short routes, but it loses
  the distance order the list and verdict rely on, and long legs need
  stretches anyway.
- **Keep one finding per row and page the list.** Doesn't make the list
  readable, only shorter per screen.
- **Per-class NavHazard selection** (like ProhAre kinds). The catalogue has no
  per-class switch for NavHazard, only on or off for all. Class chips filter
  the list instead (§4.7), so the check stays conformant.

## 7. Open questions

1. Should a traffic separation zone entered at a right angle (a crossing,
   which COLREG Rule 10 allows) stay Must review? Telling a crossing from
   joining needs the zone's lanes and the track's heading.
2. Is 30 m right for the track clearance? It could scale with the vessel's
   beam once the passage has one.
3. Should soundings use the safety contour (as `SOUNDG03` alerts) and also
   keep a safety-depth stretch for display parity with S-52?
4. Is the 0.5 NM stretch gap right in harbours, where a leg can be shorter
   than that?

## 8. Slicing with #1127

| #1127 slice | Builds here |
|---|---|
| 1. `AlertReference` in drawing instructions; read the alert catalogue | The off messages; `RouteFindingAlert`. |
| 2. Corridor portrayal with the check's context parameters, alerts per feature | `HazardClass` table, `Position`, `TrackClearance`, `NearbyDistance`. |
| 3. NavHazard findings | Danger, aid, structure and overhead codes and groups (§4.1, §4.2); `NavHazardIndication`. |
| 4. SafetyContour from alerts | Folding into spans, `ParentId`, `soundings_below_safety_contour` (§4.3). |
| 5. ProhAre areas, per-kind selection, nearby band | `AreaKind`, `ProhibitedAreaKinds`, `area_kind_off`, the area table in §4.5. |
| Follow-up (viewer) | `RouteFindingRows` and `Rows`, then the list, map and filter changes (§4.4, §4.6, §4.7), MCP `detail`. |

Slices 3–5 can emit findings with the new fields before rows exist: until
the follow-up lands, the viewer's #959 merge keeps working on codes, and the
new codes merge like any other.

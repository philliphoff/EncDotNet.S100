# Design brief: routes and under-keel clearance in SoundCharts

Status: pre-design (2026-10-11). This brief is input for a design pass that should produce mocks of the route experience, with alternatives where noted. It belongs to issue [#887](https://github.com/philliphoff/EncDotNet.S100/issues/887) (UKC along a route). [#888](https://github.com/philliphoff/EncDotNet.S100/issues/888) (route checking against dangers and S-124 warnings, and RTZ exchange) complements it, and the design should leave room for that issue's results (§4 F).

The screenshots in this folder come from the current build. They show the UKHO Solent trial cells (S-101, S-102, S-104 and S-111) and a 9-waypoint route from the Nab Tower approach to Southampton Docks. Every leg has draft 11.5 m, 12 kn, a 15 m safety contour and depth, and a 150 m cross-track limit. All of those values are set through MCP, because the UI can't edit them yet. The window is 1100×700, the default size.

## 1. Why now

Recent industry discussion of S-100 (UKHO, IHO, IC-ENC, Teledyne Raymarine, CAIM, the Nautical Institute, October 2026) comes back to one point: **S-100's value is turning combined layers into a decision, not adding more layers to the screen.**

- **UKHO simulator trials.** Crews using S-57 alone validated 5 of 20 planned waypoints. With S-102 + S-104 + S-111 they validated all 20. Crews valued not having to cross-reference tide tables, and asked to switch layers on and off for the task at hand.
- **CAIM's demo at SMM.** It asked one question: "can my vessel follow this route, at this speed and departure time, while keeping the UKC my company requires?" The answer was red or green along the route. The user changed departure time and speed until it passed.
- **"More data doesn't automatically mean safer navigation."** Information overload, nuisance alarms, and S-57/S-101 data side by side during the transition were the most repeated worries.

SoundCharts already loads every input: routes with per-leg draft, speed and UKC margins; S-102 depth; S-104 water levels; S-111 currents; and a global view time. Nothing combines them. The product promise for this work: **plan a route, set the vessel and departure, see where it's tight and when it's safe.** All of this stays clearly marked "not for navigation". SoundCharts is a tool for exploring and familiarising with S-100.

## 2. What exists today (measured from code)

| Area | Today | Where |
|---|---|---|
| Entry points | **Routes** activity tab in the left dock. **Edit route** button in the map's top-right tool stack (Pick / Measure / Route / Display). **Route from measurement** and **+** buttons in the panel header. Turning on edit mode creates a route automatically if none is active. No keyboard shortcut and no menu item. | `App.axaml.cs`, `MainWindow.axaml`, `Views/RoutesView.axaml` |
| Route list | One row per route: icon, name, "9 WP · 24.0 NM", and a ⋯ menu (Rename / Reverse / Remove). Selecting a row makes that route active. The list is capped at 200 px. | `RoutesView.axaml` |
| Active route | Name with inline rename; "9 WP · 24.0 NM · Rhumb". Below that, a vertical list alternating **waypoint rows** (number, name, lat/lon in decimal degrees, all read-only; ⋯ Insert after / Delete) and **leg rows** ("2.5 NM · 320°T" plus a clickable Rhumb/Great circle chip). Footer: Insert, Reverse, Done. | `RoutesView.axaml`, `ViewModels/RoutesPanelViewModel.cs` |
| Editing on the map | Click water to append. Click within 6 px of a leg to insert a waypoint that splits it. Drag a waypoint to move it. Right-click a waypoint to delete it. Enter commits, Backspace removes the last waypoint, Esc leaves. The status bar shows "Leg n: X NM @ B°T \| Total". The Delete key is documented but not wired up. There is no undo. | `Tools/RouteEditTool.cs` |
| Fields with UI | Route name, waypoint position (by dragging only), leg geometry. Distance and bearing are derived, always in NM and °T. | |
| **Fields with no UI** (model, `routes.json` and MCP only) | **Leg:** XTD port/starboard, channel limits, safety contour, safety depth, SOG min/max, STW min/max, **draft**, static UKC, dynamic UKC, safety margin, note. **Waypoint:** name (shown but not editable), number, Fixed, turn radius. **Route info:** author, description, departure/arrival port, **validity start/end**. **Vessel:** name, MMSI, IMO, callsign, length, beam. | `Routing/RouteLeg.cs`, `RouteWaypoint.cs`, `RouteInfo.cs`; MCP `set_leg_attributes`, `set_route_info` |
| Map drawing | Every route is always drawn. Active route: 4 px line in the accent colour at 0.85 opacity. Inactive routes: desaturated accent at 0.4. Waypoints are numbered discs; the selected one is larger. Each active leg has a label "X NM @ B°T" at its midpoint. **Not drawn:** XTD corridors, hover state, a distinct editing style, turn radii. Labels collide (screenshot 1). The Dusk/Night chart palettes get no route-specific treatment. | `Tools/RouteOverlayLayer.cs` |
| Time | A route has **no departure time, ETA or schedule** in the UI. The Timeline and Time HUD know nothing about routes. | — |
| Depth at a point | The pick report's **Depth** section shows S-102 depth (or S-101 depth if there's no S-102), adjusted by S-104 water level over time. It has a LiveCharts depth-vs-time chart with a safety-depth band and an S-102 uncertainty band. It knows nothing about routes. | `Views/PickReportView.axaml`, `ViewModels/DepthOverTimeViewModel.cs` |
| Engine pieces | `sample_coverage_along` (MCP) samples S-102/104/111 at polyline vertices. `EncDotNet.S100.Datasets.S129.Fusion` reads an *existing* S-129 UKC plan with its S-102/104/421 data. It isn't in the viewer. | `Mcp.Tools/SampleCoverageAlongTool.cs`; `Datasets.S129.Fusion` |
| Persistence | `routes.json` in the data directory; saved after a 750 ms debounce; atomic writes; every field round-trips. | `Services/RoutePersistenceService.cs` |

### What the screenshots show

1. **`today-01-route-overview.png`.** The route panel and route at about 1:210 000. Leg labels overlap each other and the waypoint discs around Calshot (WP 6–7). The panel's leg rows can show only distance, bearing and geometry, even though every leg carries a draft, a speed and a safety contour.
2. **`today-02-route-crosses-spit.png`.** Zoomed to about 1:31 000 at Calshot. **Waypoint 7 sits on Calshot Spit and leg 6–7 crosses land and drying ground.** Nothing in the UI says so. That is [#888](https://github.com/philliphoff/EncDotNet.S100/issues/888)'s problem, but it shows how a route can look fine while being wrong.
3. **`today-03-pick-depth-timeline.png`.** A pick on leg 5–6 reports **4.0 m of water** where the leg's draft is **11.5 m**. Nothing joins the two facts. The note "No tide data covers this location" is partly a data problem: this S-104 station file loads with latitude and longitude swapped (#650). The UX still needs a good answer for "no water-level data here". With the Routes, Object Information and Timeline docks all open at the default window size, the map is squeezed to about 650×680 px. **Where does a UKC profile go?**

## 3. Vocabulary the design should settle

| Term | Meaning | Notes for UI |
|---|---|---|
| Draft (draught) | Depth of the keel below the waterline. | Per route, with an override per leg? Static draft vs draft in a seaway? |
| Squat | Extra sinkage when a vessel moves in shallow water. It depends on speed and depth. | Offer either a fixed allowance or a simple model. Use plain words. |
| Charted depth | Depth below chart datum, from S-102 (or S-101). | Make the source visible: S-102 (high resolution) vs S-101 (area or sounding, lower confidence). |
| Water level / height of tide | Height of the water above or below chart datum at a time, from S-104. | Time-dependent. May be a forecast or an astronomical prediction. |
| Datum | The vertical reference for depth and for water level. | If they don't match, say so; don't fold it into a number. |
| UKC (under-keel clearance) | Charted depth + water level − draft − squat. | The main number. |
| UKC policy / required UKC | The minimum clearance a company requires, static or dynamic (`StaticUnderKeelClearanceMeters`, `DynamicUnderKeelClearanceMeters`, `SafetyMarginMeters`). | Pass or fail is measured against this. |
| Safety contour / safety depth | Mariner settings, and per-leg values in S-421. | Already in Mariner Settings. How do the per-leg values relate to the global ones? |
| ETA | When the vessel reaches each point, from departure time and speed (± S-111 current). | Every UKC value is "at this ETA". |
| Window | A span of departure times for which the whole route passes. | Candidate headline answer: "Safe departures 06:10–08:40". |
| S-129 plan / control point | A UKC plan *published by someone else* (e.g. a port), with control points along a route. | Display it. We don't author it, though exporting one is optional. |

## 4. Problem areas, with ideas (each is a mock target)

### A. The route editor itself

Today's editor exposes only the geometry, and every UKC input lives in fields with no UI. Before UKC can work, the panel needs:

- **Route-level passage settings with per-leg overrides:**
  - vessel (name, draft, length/beam if we model squat)
  - default speed
  - departure time
  - UKC policy
  - default XTD

  Most legs inherit these. A leg row shows a small marker when it overrides one.
- **A leg detail view:** expand a leg row, or open an inspector, to see speed, draft override, XTD, safety contour/depth, UKC margins and a note. Decide inline expansion vs a separate inspector pane vs a popover from the map.
- **Waypoint editing:** editable name; type in or paste coordinates in the app's lat/lon format, not raw decimals; Fixed; turn radius.
- **Basics that are missing:**
  - undo/redo
  - Delete key
  - zoom to route or leg
  - a keyboard shortcut for edit mode
  - a distinct "editing" style on the map compared with simply "active"
- **Leg labels:**
  - Fix the collisions: hide labels below a size, or put them along the line.
  - The label could show **leg time / ETA** once departure is known: "2.5 NM · 320° · 12 min · 06:42".
- Units should follow the user's preferences: the depth unit is already in Mariner Settings, and NM/km is a question to settle.

### B. Passage inputs: draft, speed, departure, policy

- Where do they live? Options include:
  - a "Passage" header at the top of the active route
  - a separate **Plan** step ("Check passage…") that opens a small form
  - controls on the timeline (departure scrubber)
- Departure time interacts with **view time** (see E). Is departure its own value, or does "view time = departure" act as a mode?
- Speed: a single SOG, or STW + S-111 current giving SOG (ETAs then vary by leg)? Show the effect of current ("+0.8 kn fair tide on leg 4").
- Policy presets, for example "Company: 10% of draft" or "Port of Southampton: 1.0 m static". Is that in scope?

### C. The UKC answer on the map

- **Route coloured by clearance:** green (meets policy), amber (within X m of the limit), red (below policy), and **hatched or grey for no data** (outside S-102/S-104 extent or time range). Colours must work on Day, Dusk and Night chart palettes, and must not clash with S-101 colour tokens (red and magenta already mean things on a chart).
- **A minimum-clearance marker** on the map: "Min UKC 0.4 m · 07:18 · Thorn Channel".
- An optional **corridor** (XTD) shaded by the worst clearance across its width, not only along the centreline. Is that worth the visual weight?
- The route has to stay readable over dense layers. In screenshot 1, the S-111 current arrows crowd the line and its labels. Consider a halo or casing, or dimming other layers along the route while checking.
- A legend that appears only when UKC colouring is on.
- Should the route show its **colouring all the time**, or only while a "Check" panel is open? Consider stale results after an edit: grey them out until recomputed, or recompute live.

### D. Clearance profile

A strip chart along the route, styled like the existing pick Depth chart:

- **X axis:** distance along the route (NM) or time (ETA). Toggle between them.
- **Series:**
  - seabed (S-102, shaded by source/uncertainty; S-101 shown in a lower-confidence style)
  - water level at the ETA
  - keel line (water level − draft − squat)
  - required-UKC band
- **Markers:** waypoints, leg boundaries, and the minimum-clearance point.
- **Hover links to the map:** a cursor on the profile moves a marker on the route, and hovering the route moves the profile cursor.
- **Where it lives** is the main layout question:
  - a tab in the bottom dock next to Timeline
  - the bottom of the Routes panel (narrow, 320 px)
  - a floating sheet over the map
  - or replacing the right dock while checking

  Remember the window can be 800×560 (screenshot 3).

### E. Time: departure, ETA and the timeline

- **The route on the Timeline:** a lane or band showing the passage window (departure → arrival ETA), with a marker at the min-UKC time.
- **"Where am I at view time?":** as the view time moves through the passage, show a ghost vessel marker at the ETA position. The S-104/S-111 layers already follow view time, so the map then shows the conditions the ship meets at that point.
- **Departure scrubber:** drag the departure on the timeline, and the route colours and profile update live.
- **Earliest safe departure / window finder:** sweep departures across the S-104 time range. Show the result as a band on the timeline ("passes 06:10–08:40, fails otherwise"). It could be the headline answer.
- **Interaction with Live/Pinned mode** (Time HUD): a passage in the past (like these 2021 trial data) vs a future passage, and expired forecast runs (the S-111 lane shows **Expired** in screenshot 3).

### F. Findings, shared with #888

UKC problems ("leg 6 below policy for 4 min") and #888's static checks (dangers, land, S-124 warnings, MPAs) should land in **one findings list per route**. Clicking a finding zooms the map and selects the place on the profile. The existing Validation tab's findings list (severity icon, click to zoom, map markers) is the pattern to reuse. Mock both kinds together so the list, the severity colours and the route colouring stay consistent.

### G. Provenance, confidence and "no data"

Users must be able to see *why* a number is what it is:

- which S-102 cell (or the S-101 fallback), and which S-104 station or grid and time step
- datum mismatch
- uncertainty from S-102 and from quality of bathymetric data (the pick report already shows "Quality of Bathymetric Data")
- gaps in coverage, in space or time

Ideas: a "Sources" disclosure on the profile, provenance in the hover card, a "Low confidence" state distinct from "no data", and a clear message when S-104 is missing ("Clearance uses charted depth only; tide not applied"). Avoid making a fallback look as authoritative as the real thing.

### H. Loaded S-129 plans

When an S-129 dataset is loaded, show its control points on the map and its UKC margins over time, through the same profile and timeline pieces. It's read-only: "Port's plan". Decide whether a plan and a user route can be compared on one profile.

### I. Layout and density

- A three-dock layout at the default window size (screenshot 3) leaves little map. Propose how the route workflow uses the space:
  - Does checking a passage collapse the Object Information dock?
  - Does the Timeline collapse to its strip?
- A narrow window (800×560) must still work.
- Keep MCP parity in mind. Every state the design adds should be reachable and readable through tools, for agent use and UI tests.

### J. Trust and disclaimers

"Not for navigation" has to show where it matters, at the moment a verdict appears, without nagging. Do the green states need softer language ("meets your settings") rather than "Safe"?

## 5. Constraints

- **Stack:** Avalonia with the ShadUI component library, a light/dark app theme, and Day/Dusk/Night chart palettes that apply to the map only. Chart colours follow S-101 conventions.
- **Docks:** left 320 px default (200–600 range); right 360 px (240–600); bottom 260 px (minimum 120). Window minimum 800×560.
- **Strings and IDs:** strings live in `Resources/Strings.resx`; every interactive element needs an AutomationId (#776). Charts use LiveCharts2, as in the pick report.
- **Engine shape (planned, #887):** results are per sample and per leg, with clearance, source and time step for each, plus a minimum, gaps and a verdict. Recomputation is fast enough for live scrubbing on a typical port approach. Design for a brief "computing" state on long routes.
- **Out of scope:** route optimisation or auto-routing; real-time own-ship monitoring as an ECDIS would do it; S-129 authoring beyond an optional export.

## 6. What we'd like back

1. Mocks for §4 A–E at the default window size, and C–E at 800×560 as well, in light and dark app themes, with at least one Night chart palette view.
2. A clear recommendation for **where the clearance profile lives** (§4 D/I), with one alternative.
3. The **headline-answer pattern** (§4 C/E): what the user sees first after "Check passage". A verdict card, a window band, or both?
4. The combined **findings list** with #888 (§4 F).
5. Words: settle the §3 vocabulary and the tone of verdicts and disclaimers (§4 J).
6. A suggested slicing into PRs, as earlier handoffs did, starting with what's useful without UKC (A).

## 7. Open questions

- Is departure time its own value, or tied to the global view time?
- Is squat modelled at all, or only a fixed allowance?
- Does SOG come from STW + S-111 current, or is it simply what the user enters?
- Should UKC colouring be always on for the active route, or only while checking?
- Do we offer S-129 export of a checked passage, or keep it for later?
- Do we need per-leg values for safety contour and depth, or do they collapse into the global Mariner Settings?

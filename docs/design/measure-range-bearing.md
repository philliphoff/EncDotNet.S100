# Measure and range & bearing design pass

> Status: **Built** (2026-10-31), for
> [#1130](https://github.com/philliphoff/EncDotNet.S100/issues/1130): slices
> 1–3 in #1197, #1201 and #1202; slice 4's results are in §6.5 of the
> [S-164 conformance review](s164-conformance.md), whose finding 5 this
> follows.

The S-164 Part 4 accuracy tests (Accuracy1–15) ask an ECDIS to measure
between typed positions, to give the position at a typed range and bearing,
and to draw long great circles, rhumb lines and geodesic circles. SoundCharts
has a measure tool, but it only measures rhumb lines by clicking, and nothing
turns a range and bearing into a position. This note audits what's there,
proposes numbered decisions for a better measure tool and a new range &
bearing tool, and splits the work into slices.

On 2026-10-31 #1130 was rescoped to the tools only. They use the spherical
geodesy the viewer already has. Moving to the WGS-84 ellipsoid is
[#1189](https://github.com/philliphoff/EncDotNet.S100/issues/1189), with its
own licensing decision, so no decision here depends on it. The sphere is
already within the S-164 tolerance: 1% or 30 m, whichever is greater, for
distance and 1° for bearing. It's off by 0.04–0.12% on distance and about 0.2°
on bearing across Accuracy1–9. (#1189 has since moved the tools to the
ellipsoid; see §6.5 of the [S-164 conformance review](s164-conformance.md).)

The mock for this round is the **Measure & range/bearing · design pass**
artboard on the *Navigation & time* page of the SoundCharts design canvas
(<https://claude.ai/artifact/2hWbgQ9wKM1gC2gM51mSCs>): today's measure tool,
the measure card in great-circle mode in light and dark, the range & bearing
tool with a 2,000,000 m ring, and parts for the fields, locks and toolbar.

## 1. What the tests ask

Paraphrased from S-164 2.0.1 Part 4. The manual is IHO copyright, so it isn't
quoted here and mustn't be committed.

| Tests | The mariner… | Reads |
|---|---|---|
| Accuracy1–3 | measures a great circle between two given positions, about 33 km apart in Accuracy1 | distance, forward bearing, reverse bearing |
| Accuracy4–6 | enters a start position, a distance and a bearing | the end position |
| Accuracy7–9 | measures a rhumb line between two given positions, about 10.7 km in one of them | distance, forward bearing, reverse bearing |
| Accuracy10–12 | draws great circles of about 3,000 km, and a geodesic circle of 2,000,000 m radius | the line against the chart; the circle against range rings at 2,000,000 m intervals |
| Accuracy13–15 | draws rhumb lines of about 3,000 km | the line against the chart |

Accuracy1 on the sphere, from 32°35.224'S 061°17.710'E to 32°27.436'S
060°58.609'E: 33,154 m (17.90 NM), forward 295.7°, reverse 115.9°. The test
expects about 33.19 km, so the sphere is 36 m short, well inside the 332 m the
tolerance allows.

## 2. What's there today

| # | Observation | Where |
|---|---|---|
| A1 | The measure tool only measures rhumb lines. Its tooltip says so. | `MeasurePathState.ComputeLegs`, `Tooltip_MeasureMode` |
| A2 | It shows the forward bearing only. There's no reverse bearing, which differs from forward + 180° on a great circle. | `MeasureLeg` |
| A3 | Distances show to 0.1 NM and bearings to the whole degree (`{0:0.0} NM @ {1:000}°T`). Rounding alone can use up to 93 m of the 107 m the 10.7 km test allows. | `Status_MeasureLeg`, `Status_MeasureLegLabel` |
| A4 | Distances are always NM. The **Distance unit** setting (NM, metric, miles) only changes the scale bar. The tests give their values in km and m. | `DistanceUnit`, `ScaleBarView` |
| A5 | Results only appear in the status bar and as labels on the map. Waypoint positions are never shown, and nothing can be copied. | `MeasureTool.PushSummary` |
| A6 | Points can only be clicked, so a test position can't be placed exactly. The route editor's waypoint rows already accept typed and pasted positions in many forms (`LatLonFormatter.TryParse`). | `RoutesPanelViewModel.CommitWaypointPosition` |
| A7 | No tool gives a position from a range and bearing. `GeodesicHelper.DirectProblem` in Core does it for great circles, but nothing in the UI calls it, and there's no rhumb-line version. | `GeodesicHelper` |
| A8 | No geodesic circle or range rings. `GeodesicHelper.TessellateArc` makes circles for S-100 `ArcByRadius` (72 segments a circle). | `GeodesicHelper` |
| A9 | Great-circle route legs are drawn in 24 segments however long they are. A 3,000 km leg gets 125 km chords, which bow away from the great circle at large scales. | `RouteOverlayScene.GeodesicSegments` |
| A10 | There are two earth radii: 6,371,000.4 m in `MarineGeodesy` and 6,371,008.8 m in `GeodesicHelper`. They differ by 1.3 ppm, which is harmless, but measuring a path and then running the range & bearing tool back along it should agree to the metre. | both |
| A11 | No MCP tool drives or reads the measure tool, so an agent can only check results from a screenshot. | `McpTools/` |
| A12 | Promoting a measurement to a route makes rhumb legs, whatever was measured. | `MainViewModel.PromoteMeasurementToRoute` |

Keys today: `Cmd/Ctrl+M` toggles Measure, `I` toggles Pick, `Cmd/Ctrl+E`
toggles the route editor, `Esc` leaves the active tool, `Enter` finishes and
`Backspace` removes the last point. `Cmd/Ctrl+R` and `G` aren't bound
anywhere. Only one map tool is active at a time (`MapToolController`).

## 3. Decisions

### The measure tool

**D1. A measure card.** While Measure is on, a card sits at the map's top
left, under the time stamp (12, 44), in the slot the route editor's chip uses.
The two tools can't be on together, so they never collide. The card is 296 px
wide on `MapOverlaySurfaceBrush` with the usual border and radius. Its header
holds the title, the geometry control (D2), a copy button (D8) and close. Its
rows are **From**, **To**, **Distance**, **Bearing** and **Reverse**, then
**Total** when the path has more than one leg. Before the first click the
card shows the existing hint ("Click on the map to start measuring…").

**D2. Rhumb or great circle.** A two-part segmented control, *Rhumb* |
*Great circle*, sets the geometry of the whole path. `G` switches it while
the map has focus. The map draws great-circle legs as curves, and the leg
labels and card follow at once. The tooltip loses "(rhumb line)".

**D3. Default geometry: remember the last choice, starting at Rhumb.** Rhumb
is what the route editor makes by default and what an ECDIS shows by
default. Over the short distances people usually measure, the two differ by
less than the display precision. The choice persists in settings
(`MeasureGeometry`), so someone working through the great-circle tests sets
it once. *Needs the user*: whether great circle should be the first-run
default instead.

**D4. Forward and reverse bearings.** *Bearing* is the initial true bearing
at From. *Reverse* is the initial true bearing at To back towards From. On a
rhumb line that's always forward ± 180°. On a great circle it isn't, and the
tooltip says why ("The bearing from To back to From. On a great circle this
isn't the forward bearing plus 180°."). Both are labelled °T.

**D5. Precision.** Distance to 0.01 NM, 1 m, or 0.001 km (D6). Bearings to
0.1°. Positions in the app's degrees and decimal minutes to 0.001'
(`LatLonFormatter`), about 2 m. Map leg labels and the status bar use the same
precision: `17.90 NM · 295.7°`. At this precision display rounding takes
at most 9 m and 0.05° of the tolerance.

**D6. Units follow the Distance unit setting, with a second line.** The
primary distance uses the existing **Distance unit** setting: NM, km (m
below 1 km) or statute miles. Under it, in muted text, is the other system:
km and m when the primary is NM or miles, NM when it's metric. The tests give
values in km and m and mariners think in NM, so both are always on screen.
There's no new setting. The scale bar is unchanged.

**D7. Typed positions.** From and To are text fields. They show the position
in the app's format and accept anything `LatLonFormatter.TryParse` does:
degrees and decimal minutes, degrees, minutes and seconds, signed decimal
degrees, and hemisphere letters before or after. That's the same parser the
route editor's waypoint rows use. `Enter` or leaving the field commits. A
position that doesn't parse gets a destructive border and an inline hint,
"Not a position. Try 32°35.224'S 061°17.710'E or -32.587, 61.295", and the
leg keeps its old point. `Esc` reverts the field. Typing From with an empty
path places the first point. Typing To then makes the leg. On a longer path,
From and To are the ends of the selected leg (D9). A committed point off
screen pans the map to it, without zooming.

**D8. Copy.** The header's copy button puts the measurement on the clipboard
as text: positions, geometry, distance in both unit systems, bearings and
the earth model (D17). It's the evidence a test run records.
`Cmd/Ctrl+C` with the map focused does the same.

**D9. Multi-leg paths stay.** Clicking keeps adding legs, as today, and the
card shows the last leg. Clicking a leg's label on the map selects that leg,
and the card's leg rows follow it (`Leg 2 of 3`, with ‹ › buttons). Total
always covers the whole path.

**D10. Promoting keeps the geometry.** Promote to route gives each new leg
the measure geometry: a great-circle measurement makes geodesic legs.

### The range & bearing tool

**D11. A new map tool next to Measure.** The toolbar gains **Range &
bearing** (`Map.RangeBearingMode`) between Measure and Route edit, with a
compass-style icon. `Cmd/Ctrl+R` toggles it; nothing uses it today. View ›
Appearance gets a matching menu item. It's the ECDIS EBL/VRM pair with an
origin you choose: a bearing line and a range ring from one point.

**D12. Clicking.** The first click sets the origin. After that the bearing
line and range ring follow the cursor, and the card's Range, Bearing and End
follow too. A second click fixes the end. Another click after that starts
again from a new origin. `Enter` fixes the end at the cursor, `Backspace`
returns to placing the origin, and `Esc` leaves the tool. Dragging still pans.

**D13. A range & bearing card with locks.** The card takes the same slot and
style as the measure card. Its rows are **Origin** (a position field, D7),
**Range** and **Bearing** (number fields), **End** (read-only, copyable) and
**Reverse**. Typing a range locks it: a lock icon appears in the field, and
from then on the cursor only swings the bearing, like a VRM set to a range.
Typing a bearing locks the bearing in the same way, and the cursor only
changes the range, like an EBL. With both locked, the end is fixed and the
cursor does nothing. Clicking a lock, or clearing its field, unlocks it.
Accuracy4–6 is then: type Origin, Range and Bearing, read End.

**D14. Range entry.** A number on its own is in the primary unit (D6).
A unit suffix overrides it: `2000000 m`, `2,000 km`, `1079.9 NM`, `1.2 mi`.
`NM` is case-insensitive. A bare `M` is refused with a hint, because charts use
it for nautical miles and SI uses it for metres. The field shows the
value back in the primary unit, with the other system underneath, as in D6.
Bearing accepts 0–360 with optional `°` or `°T`. `↑`/`↓` nudge the bearing by
1° (10° with Shift) and the range by one unit of its last digit.

**D15. Geometry, and the ring is always a geodesic circle.** The card has the
same Rhumb | Great circle control and `G` key as Measure (D2, D3; its own
remembered setting). It decides the bearing line and how the end is computed:
along a great circle (`GeodesicHelper.DirectProblem`) or along a rhumb line
(a new rhumb direct helper). The range ring is always a geodesic circle: every
point on it is the range from the origin along a great circle. On a
rhumb-line bearing the ring passes through the end only approximately, and
the tooltip on Range says so.

**D16. Range rings.** A **Rings** row, off by default, adds rings at a set
interval (`every [2,000,000 m] × [3]`), drawn as geodesic circles like the
VRM. Accuracy12 sets the range to 2,000,000 m and rings every 2,000,000 m: the
VRM and the first ring must lie on each other. Each ring is labelled with its
range at its northern point.

### Both tools

**D17. Name the earth model.** Each card's footer says, in muted text,
`Sphere · R 6,371 km`, with a tooltip: "Distances and bearings use a
spherical earth. That's within the S-164 accuracy tolerance. WGS-84 is
tracked in #1189." It changes to `WGS-84` when #1189 lands. One radius
constant replaces the two (A10). *Since #1189 the footer reads
`WGS-84 ellipsoid`, and both tools measure on it.*

**D18. Long lines and circles are densified by length.** Great-circle lines,
geodesic circles and the bearing line are split into segments of at most
50 km, and at least 64 per circle. Each line is cut at the antimeridian
(`MarineGeodesy.SplitAtAntimeridian`) and drawn at world copies like other
chart data. A circle round a pole is drawn as an open line, never filled.
Rhumb lines are straight in Mercator and need cutting only. Great-circle
route legs get the same densification (A9).

**D19. Coexistence.** One map tool at a time, as today: turning on Measure,
Range & bearing, Pick or the route editor turns the others off, and a card
only exists while its tool is on. Switching away clears the tool, as today.
The pick panel and the route panel are unaffected. With the route editor on,
its chip takes the slot instead. Neither card hides the overscale or warning
notes: they move down below the card while it's open.

**D20. Keys.**

| Key | Measure | Range & bearing |
|---|---|---|
| `Cmd/Ctrl+M` | toggles the tool | switches to Measure |
| `Cmd/Ctrl+R` | switches to Range & bearing | toggles the tool |
| `G` | switches the geometry | switches the geometry |
| `Enter` | finishes the path | fixes the end at the cursor |
| `Backspace` | removes the last point | back to placing the origin |
| `Delete` | clears the path | clears the origin |
| `Cmd/Ctrl+C` | copies the measurement | copies the result |
| `Esc` | leaves a field (reverting it), then the tool | the same |
| `Tab` | moves through the card's fields | the same |

`G`, `Enter`, `Backspace` and `Delete` act on the tool only while the map or
the card's buttons have focus. In a text field they edit the text
(`RouteEditorKeys.IsTextInputFocused` already decides this for ⌘E).

**D21. Automation IDs.** Measure: `Measure.Card`, `Measure.Geometry`
(with `Measure.Geometry.Rhumb`, `Measure.Geometry.GreatCircle`),
`Measure.From`, `Measure.To`, `Measure.Distance`, `Measure.DistanceOther`,
`Measure.Bearing`, `Measure.ReverseBearing`, `Measure.Total`,
`Measure.LegPrevious`, `Measure.LegNext`, `Measure.Copy`, `Measure.Close`,
`Measure.Model`. Range & bearing: `Map.RangeBearingMode`, `RangeBearing.Card`,
`RangeBearing.Geometry` (`.Rhumb`, `.GreatCircle`), `RangeBearing.Origin`,
`RangeBearing.Range`, `RangeBearing.RangeLock`, `RangeBearing.Bearing`,
`RangeBearing.BearingLock`, `RangeBearing.End`, `RangeBearing.ReverseBearing`,
`RangeBearing.Rings`, `RangeBearing.RingInterval`, `RangeBearing.RingCount`,
`RangeBearing.Copy`, `RangeBearing.Close`, `RangeBearing.Model`. Each value
field's accessible name includes its value and unit ("Distance 17.90 nautical
miles").

**D22. Light and dark.** The cards use the theme tokens
(`MapOverlaySurfaceBrush`, `BorderColor`, the foreground brushes) and
`MonoFontFamily` with tabular figures for every number and position. On the
map, lines are the accent colour as today. The measure path and the bearing
line are solid. The rubber band is dashed as today. The VRM is solid, extra
rings are thinner at 60% opacity, and the origin and end are discs like
measure waypoints. Labels use the measure tool's existing light and dark
chips. An invalid field uses the destructive token in both themes.

**D23. MCP tools.** Four tools, named like the rest:

- `set_measure` takes `points` (each a position string the card accepts, or
  `{lat, lon}`), `geometry` (`rhumb` or `greatCircle`) and optional
  `selectedLeg`. It turns the tool on and returns what `get_measure` does.
  An empty `points` clears the path.
- `get_measure` returns the geometry, the earth model, and per leg the from
  and to positions (decimal degrees and the app's text), the distance in
  metres and NM, the forward and reverse bearings, and the total.
- `set_range_bearing` takes `origin`, `range` (metres as a number, or a
  string with a unit as in D14), `bearing`, `geometry` and optional
  `rings: {interval, count}`. It turns the tool on, locks what it's given,
  and returns what `get_range_bearing` does.
- `get_range_bearing` returns the origin, range, bearing, locks, end,
  reverse bearing, rings and earth model.

Values are unrounded in the results, so a test checks the maths and a
screenshot checks the display. `set_viewport` then frames the result for a
capture.

## 4. Out of scope

- The WGS-84 ellipsoid: [#1189](https://github.com/philliphoff/EncDotNet.S100/issues/1189).
  D17 is the only place the UI mentions it.
- EBL/VRM from own ship, relative bearings and floating EBLs. Those belong
  with route monitoring ([#1131](https://github.com/philliphoff/EncDotNet.S100/issues/1131)).
  D13's origin can later be "own ship" without changing the card.
- More than one range & bearing at a time. ECDIS usually has two pairs. One
  covers the tests; add a second when someone asks.
- Parallel index lines, magnetic bearings and measuring along an existing
  route.
- Keeping measurements across sessions or exporting them other than by copy.
- A `s100` CLI command for the same maths. The MCP tools cover the
  automation the tests need.

## 5. Slices

1. **Geometry core.** One earth radius (D17, A10). A rhumb direct problem
   and reverse bearings (D4, D15). Densifying lines and geodesic circles by
   length, cut at the antimeridian, also for great-circle route legs (D18,
   A9). A small formatter for D5 and D6 and a range parser for D14. Unit
   tests with the Accuracy1–9 positions and tolerances.
2. **Measure card.** D1–D10, D17, D19–D22 for Measure. `set_measure` and
   `get_measure` (D23).
3. **Range & bearing tool.** D11–D16 and the rest of D19–D22. Toolbar
   button, `Cmd/Ctrl+R`, the overlay with rings, and `set_range_bearing`
   and `get_range_bearing`.
4. **Conformance.** Run Accuracy1–15 through the MCP tools with captures,
   update finding 5 and the Accuracy rows of
   [the conformance review](s164-conformance.md), and close #1130.

Slices 2 and 3 both depend on 1 and can then go in parallel.

## 6. Decisions on the open questions

1. Great circle isn't the first-run default. Measure remembers the last
   choice, starting at Rhumb (D3).
2. `Cmd/Ctrl+R` opens Range & bearing (D11).
3. The second unit line always shows, on the cards only (D6). Map labels and
   the status bar show the primary unit, and Copy includes both (D8).

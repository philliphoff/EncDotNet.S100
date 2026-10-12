# Routes & under-keel clearance — design handoff

Repo: `philliphoff/EncDotNet.S100`. Scope: `src/EncDotNet.S100.Viewer`. Issues: #887 (UKC along a route), with room left for #888 (route checks, S-124, RTZ).
Round: **1** · Issued: 2026-10-12. Input: `route-ukc-brief.md` (in this folder), and the current-build screenshots in `screenshots/`.

Mock: `Route UKC.dc.html` (open in a browser; `support.js` must sit beside it). All three screens are 1100×700, the default window size.
- **1a — Plan.** Edit mode, with the Passage card and an expanded leg. Light theme, Day palette.
- **1b — Check passage.** The verdict card, the coloured route, and the Clearance tab in the bottom dock. Light theme, Day palette. **It's live:** drag the departure band under the profile, or press *Depart HH:MM*, and every screen recomputes. Hover the profile or a leg row to move the cursor and ghost vessel. The profile has a Distance/Time toggle.
- **1c — Findings and Timeline.** The combined findings list (#887 and #888) and the passage lanes on the Timeline. Dark theme, Night palette.

The mock is a design reference, not code to port. Build it with the existing Avalonia + ShadUI controls, theme resources and LiveCharts2. All strings go in `Resources/Strings.resx`. Every interactive element gets an AutomationId (#776). Depths, tide curve, times, findings and S-102 cell names are fixtures. The mock's model is UKC = charted depth + water level − draft − squat, with a synthetic tide.

## Decisions (answers to the brief)

1. **Casual first, S-421 capable.** A route needs only waypoints. Every Passage field is optional. Each field unlocks a feature: **draft → Check passage**, **speed → ETAs**. Unset fields aren't written to `routes.json` or RTZ.
2. **Departure is a route value.** It isn't tied to view time. Until it's set, ETAs and checks use the current view time. *Follow passage* on the Timeline pins view time to the ETA under the cursor (§4 E).
3. **Squat** is an optional fixed allowance per vessel in v1. It isn't modelled from speed.
4. **SOG is the entered speed** in v1. S-111 is displayed, not applied to ETAs.
5. **Checking is a mode.** UKC colouring shows only while it's on.
   - **Edit** and **Check passage** are toggles that stay in fixed places in the Routes panel footer, so you turn a mode off where you turned it on. Both can be on at once.
   - Edits recompute live. While a result is computing, the route draws grey and dashed.
6. **Where the profile lives:** a **Clearance** tab in the bottom dock, next to Timeline. The two tabs share the time axis and the departure. *Alternative (not mocked):* a sheet docked to the map's lower edge when the bottom dock is closed.
7. **Headline answer: both.** The verdict card answers "this departure?" and the departure-window band under the profile answers "which departures?".
8. **Safety contour and depth stay in Mariner Settings.** The leg detail says so. Per-leg S-421 values round-trip but aren't edited in v1.
9. **Narrow windows (800×560) are deferred** to a whole-app form-factor review. Don't regress them, but no new layout is designed for them yet.
10. **S-129 display and export are deferred** to a later round.

## Vocabulary

- **Verdicts and states:**
  - Meets your settings
  - Tight (within 0,5 m of required)
  - Below your UKC setting
  - Low confidence (charted depth from S-101)
  - No data
- **Never use "Safe".** The green state is "Meets your settings", even on the departure window.
- The disclaimer "Not for navigation · computed from {sources} for your settings" appears **once**, in the verdict card's footer.
- Terms used in the UI: Draft · squat, Speed, Departure, Required UKC, Cross-track, Charted depth, Water level, UKC, Min UKC, Departure window, Follow passage.
- Numbers use the app's locale; the mock shows a decimal comma. Coordinates use the app's degrees-minutes format.

## Open items

### Part A — Editor basics (no UKC needed)

- [ ] **A1.** Add undo and redo for every geometry and attribute edit: Ctrl+Z and Ctrl+Shift+Z, plus toolbar buttons under the route name.
- [ ] **A2.** Wire up the Delete key to remove the selected waypoint. Keep right-click delete.
- [ ] **A3.** Add a *Zoom to route* button. Add *Reverse* to the toolbar (it stays in the ⋯ menu too).
- [ ] **A4.** **Ctrl+E (⌘E)** toggles edit mode. Esc leaves it. Show the hint "Ctrl+E edit · Esc done" in the toolbar, and the shortcut in the Route map tool's tooltip. **No bare-letter shortcut.**
- [ ] **A5. Footer:** an **Edit** toggle on the left and a **Check passage** toggle on the right, both 32 px. *Done* is removed: turning Edit off is "done". When a toggle is on, it's filled `--selected`; when off, it's outlined.
- [ ] **A6. Editing style on the map:**
  - White casing (black on Night), 9 px under a 3.5 px `--selected` line.
  - Numbered waypoint discs, r 10.5.
  - An insert handle (a ⊕ disc) at each leg midpoint, on legs longer than 40 px on screen.
  - Other routes dimmed (inactive colour, 0.6 opacity).
  - A selected leg gets a 12 px `--selected` halo at 0.25 opacity.
  - A map chip at top left: "Editing route · Click water to add · drag to move · Del removes · Esc done".
- [ ] **A7. Leg labels** run along the line and are flipped so they're never upside down. A label is dropped when the leg is shorter on screen than the label; the selected leg's label always shows. Format: `2,5 NM · 320° · 12 min`. The minutes appear only when speed is known.
- [ ] **A8. Waypoint rows:** a number disc, an editable name, and coordinates in the app's degrees-minutes format. Coordinates are editable, and you can paste them. The ETA shows on the right when it's known. Keep ⋯ Insert after / Delete.
- [ ] **A9.** The route list keeps its current rows, using the app's selected-row pattern: a 3 px `--selected` bar and a 9 % tint.

### Part B — Passage card and leg overrides

- [ ] **B1. Passage card** sits under the route header, with the title "Passage", an **Optional** pill and a collapse chevron. It's a two-column grid of 26 px fields:
  - **Draft · squat.** Squat is optional; with no squat, show only the draft.
  - **Speed** (kn).
  - **Departure.** Unset shows a clock icon and "View time".
  - **Required UKC.** Static in v1, labelled "static" in the field.
  - **Vessel** (name, MMSI and the rest live in a vessel popover).
  - **Cross-track** (m; port and starboard are the same unless overridden per leg).
- [ ] **B2. Field states:**
  - A set field has a solid border and a mono value.
  - An unset field has a dashed border and reads "+ Add", muted. "View time" is the exception for Departure.
  - Help line under the fields: "Draft enables Check passage. Speed gives ETAs. Everything else is optional."
- [ ] **B3. Collapsed state** on a new route: one line reading "Passage · Add draft and speed to check clearance" with a chevron. Once fields are set, the collapsed line summarises them instead.
- [ ] **B4. Inheritance:** legs inherit from Passage. A leg row shows a **blue dot plus the overridden value** (for example "● 8 kn") after its distance, bearing and duration.
- [ ] **B5. Leg detail** opens inline, by clicking a leg row. It's a tinted card with the `--selected` bar. Header: "Leg 6 → 7" and its distance, bearing and duration. Body:
  - Fields for Speed, Draft, Cross-track port / stbd and Required UKC. An overridden field gets a blue label with a dot ("Speed · override"), a `--selected` border and a *Reset* link. An inherited field is dashed, showing the inherited value.
  - A Rhumb / Great circle segmented control, and "+ Note".
  - Footnote: "Dashed = inherited from Passage. Safety contour and depth come from Mariner settings."
  - Only one leg is open at a time.
- [ ] **B6.** The route header sub-line reads `9 WP · 24,1 NM · {duration} · arrives {HH:MM}`. The duration and arrival appear only when speed is known.
- [ ] **B7. Required UKC unset:** the check runs against 0 m, and the verdict says "no margin set". *(Not mocked.)*
- [ ] **B8. No draft:** Check passage is disabled, with the tooltip "Add a draft to check clearance". *(Not mocked.)*
- [ ] **B9. Persistence:** only set fields and overrides are written. A leg stores an override only when it differs from Passage. MCP `set_leg_attributes` and `set_route_info` keep working; add `set_passage` for the route-level values.

### Part C — Check passage mode

- [ ] **C1. Entering the mode:**
  - The map gets a 30 % wash (white on Day, black on Night) under the route, so the route reads over S-111 arrows.
  - The Object Information dock folds to a 26 px edge tab with the vertical label "OBJECT INFORMATION". The dock restores when the mode is turned off.
  - The bottom dock opens on **Clearance**.
- [ ] **C2. Panel header** in this mode: the route name, then a sub-line `{vessel} · {draft} · dep {HH:MM} · arr {HH:MM}`, truncated with an ellipsis.
- [ ] **C3. Verdict card**, with a tone border at 30 % and a tone fill at 6 %:
  - **Title**, by priority:
    - "Arrival is after the water-level data ends" (neutral, ban icon)
    - "Below your UKC setting in N place(s)" (destructive, octagon)
    - "Meets your settings · tight" (warning, triangle)
    - "Meets your settings" (success, check)
  - The min UKC, 26 px mono in the tone colour, followed by "min UKC · required {x} m".
  - Where: `Leg 6 → 7 · Thorn Channel · off Calshot · 07:52 · N min below`.
  - Window line: "Departures 05:35–06:40 meet your settings", or "This departure is inside the window. …". When the current departure is outside, add a primary button **Depart {HH:MM}** for the nearest passing departure.
  - Footer (10.5, muted): "Not for navigation · computed from S-102, S-104 for your settings".
- [ ] **C4.** A **Legs / Findings {N}** segmented control sits under the verdict card.
- [ ] **C5. Leg rows** in check mode are 34 px: the leg number (mono), the location name, `{ETA from}–{ETA to} · {NM}`, and a **min-UKC chip** on the right. The chip is tinted with the worst state on the leg. For low confidence, the chip gets a dashed muted border. With no tide data, it reads "no tide". Hovering a row moves the profile cursor to the middle of that leg.
- [ ] **C6. Route colouring at ETA**, drawn on a 10 px casing:

| State | Day | Night | Stroke |
|---|---|---|---|
| Meets your settings | `#13895B` | `#2E8A5E` | 5 px solid |
| Tight | `#E0A008` | `#A27A1C` | 5 px solid |
| Below your UKC setting | `#D42A3A` | `#C03A46` | **7 px** solid (so it doesn't rely on colour alone) |
| Low confidence | `#5E6B7E` | `#56606E` | 5 px dashed 6/4 |
| No data | `#8C95A3` | `#4A515C` | 5 px dashed 6/4 |

  Dusk isn't mocked. Interpolate between Day and Night, and check contrast against the S-101 Dusk tokens.
- [ ] **C7. Legend:** "UKC AT ETA" with four swatches, at the map's **bottom left**. It shows only in check mode.
- [ ] **C8. Min-UKC marker:**
  - A ring around the point, in the tone colour.
  - A pill label: "Min UKC 0,4 m · 07:52".
- [ ] **C9. Ghost vessel:** an arrow at the profile cursor, pointing along the leg bearing.
- [ ] **C10. Stale results:** while recomputing after an edit, draw the route grey and dashed. Keep the last verdict, dimmed, until the new result lands.

### Part D — Clearance tab (bottom dock)

- [ ] **D1. Tab bar:** TIMELINE | CLEARANCE (underline `--selected`). On the right: a Distance / Time segmented control, an ⓘ Sources disclosure, and a collapse chevron.
- [ ] **D2. Profile** (LiveCharts2), on a datum axis labelled CD, −5, −10, −15 m:
  - Series:
    - seabed S-102 (sand fill with a darker line)
    - S-101 fallback stretch (hatched)
    - required-UKC band above the seabed (amber 30 %)
    - water level (blue line)
    - keel = water level − draft − squat (dark line)
  - Where the keel is within the band or below it, the gap between the keel and the seabed fills with the warning or destructive colour at 35 %.
  - Waypoint numbers along the top, with dotted leg boundaries.
  - A state strip (4 px) under the axis.
  - A min-UKC dot with its value.
  - A vertical cursor that follows the mouse.
- [ ] **D3. X axis:** Distance (NM) or Time (ETA, half-hour ticks). The cursor and the map ghost stay linked in both.
- [ ] **D4. Readout card** (196 px, right of the profile), at the cursor:
  - `{NM} · {ETA}` and the leg name.
  - Charted depth, with its source (`S-102 · {cell} · ±0,3 m`, or "S-101 DepthArea · lower confidence").
  - Water level, with its source (`S-104 · {station} · {step}`, or "No S-104 at this ETA · tide not applied").
  - Draft + squat.
  - The UKC total, in the tone colour, and the verdict words.
- [ ] **D5. Departure band** under the profile: a sweep of departures across the S-104 range, in 5-minute steps.
  - Passing departures are success-coloured, failing ones destructive at 35 %, and departures whose arrival falls past the data are hatched.
  - A knob marks the current departure.
  - Axis labels at the start, middle and end, ending with "S-104 ends".
  - Dragging the band sets Departure and recomputes live.
- [ ] **D6. Sources disclosure:** lists the S-102 cells, S-101 fallback stretches, the S-104 station or grid with its time step, S-102 uncertainty, any datum mismatch, and gaps. *(Contents not mocked.)*

### Part E — Timeline and time

- [ ] **E1. Passage lane:** a bar from departure to arrival, `--selected` at 35 % with a border, plus a tick at the min-UKC time in the verdict colour.
- [ ] **E2. Departure window lane:** the same segments as D5, plus a departure tick.
- [ ] **E3. Follow passage** toggle in the Timeline header, next to the Pinned/Live HUD. When it's on, view time is pinned to the ETA at the profile cursor, so S-104 and S-111 show what the ship meets. When it's off, view time is untouched.
- [ ] **E4.** Expired forecast runs keep their **Expired** tag on their lane. The S-111 info finding says it isn't used in ETAs.

### Part F — Findings (shared with #888)

- [ ] **F1.** One list per route, on the Findings tab. It's grouped by what to do: **MUST REVIEW**, **CHECK** and **INFO**.
- [ ] **F2. Row:**
  - A severity icon (octagon / triangle / info, or a waves icon for confidence findings).
  - A title (12/600).
  - A meta line (11, muted).
  - A right-aligned tag naming the check: `Clearance`, `Route check`, `S-124`, `Confidence` or `Data`.
  - The selected row uses the selected-row pattern.
- [ ] **F3.** Clicking a finding zooms the map to it and moves the profile cursor there. The map outlines the area, dashed in the finding's tone, and shows a status chip: "Zoomed to finding · leg 6 → 7 · 1:31 300".
- [ ] **F4. Fixtures in the mock:**
  - Must review: "Leg 6 → 7 crosses drying ground"; "Below your UKC setting for N min".
  - Check: "Navigational warning within 0,3 NM"; "Charted depth from S-101 only".
  - Info: "S-111 forecast run has expired".
- [ ] **F5.** Red is reserved for "Below your UKC setting" and must-review findings.

## Suggested PR slicing

1. **Editor basics** (A1–A9): useful without any UKC work.
2. **Passage settings and leg overrides** (B1–B9), with the model and persistence changes and the MCP `set_passage` tool.
3. **ETAs:** waypoint ETAs, leg durations, label minutes and the header arrival (B6, A7, A8).
4. **The #887 engine and Check passage mode:** C1–C10, with leg chips and the verdict, but no window yet.
5. **The Clearance tab:** profile, readout and Sources (D1–D4, D6).
6. **Departure sweep:** the band, the verdict window line and *Depart HH:MM* (D5, C3 window).
7. **Timeline lanes and Follow passage** (E1–E4).
8. **Findings list:** clearance findings first; #888 checks plug in later (F1–F5).
9. **Palette pass:** tune Dusk and Night, and run contrast checks.

## MCP parity

Every new state must be readable and settable through tools, for agent use and UI tests:
- `set_passage` / `get_passage`
- `set_check_mode(on|off)`
- `get_passage_result`: per-sample and per-leg UKC, state, sources, the minimum, gaps, the verdict and the window
- `set_departure`
- `get_findings`

## Tests

- **No passage fields:** a route with only waypoints saves and loads with no passage keys in `routes.json`. Check passage is disabled with its tooltip.
- **Speed only:** ETAs and durations appear, and Check passage is still disabled.
- **Draft and speed, no Required UKC:** the check runs against 0 m and the verdict says no margin is set.
- **Overrides:** a leg override persists. *Reset* removes the key. Changing the Passage speed changes inherited legs only.
- **Undo:** reverts a drag, an insert, a delete and an attribute change, each one step at a time.
- **Ctrl+E:** toggles edit, and doesn't fire while a text field has focus. A bare E types normally.
- **Edit toggle off:** commits and leaves edit mode. **Check toggle off:** removes the colouring and the wash, and restores the Object Information dock.
- **Fixture route at 07:10:**
  - "Below your UKC setting", with the min on leg 6 → 7.
  - *Depart 06:40* moves the departure into the window, and the verdict becomes "Meets your settings".
- **Arrival past S-104 end:** the neutral verdict appears, and the tail of the route is No data (dashed).
- **The first 1,5 NM outside S-102:** low confidence, a dashed segment, the chip's dashed border, and "S-101 DepthArea · lower confidence" in the readout.
- **Follow passage on:** view time equals the cursor's ETA, and the S-104 layer updates. **Off:** view time doesn't move.
- **Findings:** clicking a finding zooms to it and moves the profile cursor there.

## Acceptance

- A non-professional can draw, name, edit and undo a route without filling in any passage field.
- With draft and speed set, one toggle answers "does this departure meet my settings, and where is it tightest?", and the window shows which departures do.
- Every clearance number can be traced to its S-102/S-101 cell and S-104 station and time step.
- "Safe" never appears, and "Not for navigation" appears once, on the verdict.

## Out of scope

- Route optimisation and auto-routing.
- Own-ship monitoring.
- S-129 display and export.
- STW + S-111 → SOG.
- Modelled squat.
- Narrow and mobile layouts (whole-app review).
- Editing per-leg safety contour and depth.

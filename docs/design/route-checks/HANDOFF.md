# Route checks in Check mode — design handoff

Repo: `philliphoff/EncDotNet.S100`. Scope: `src/EncDotNet.S100.Viewer` (plus the MCP tools it hosts). Issues: #958 (this round) → #959 (UX), with #953–#957 (producers), #960/#961 (exchange) and #934 (palettes). Part of #888.
Round: **1** · Issued: 2026-10-17. Input: [route-checks-brief.md](route-checks-brief.md) and `screenshots/` (main 53ae46fc).

Mock: **Route Checks** artboard in the *SoundCharts S-100 Viewer* design canvas, <https://claude.ai/artifact/2hWbgQ9wKM1gC2gM51mSCs> (page *Navigation & time*, `project/Route-Checks.dc.html`), built with the *EncDotNet.S100* design system (<https://claude.ai/artifact/UywKszqyKDATtEERvBqWZT>). Frames are 1100×700, the default window.
- **2a** No draft · warnings and an MPA on the route. Light, Day. Generated `warnings-on-route`. Live: rows select, filter chips filter, legend folds.
- **2b** Draft set · clearance and route checks in one verdict. Light, Day. UKHO Solent, #887 route at 07:10.
- **2c** A warning near the route, read in place. Dark, Night (proposed tones).
- **2d** Map treatment on Day / Dusk / Night, and the tone table.
- **2e** Copy to edit (what the copy keeps). **2f** Export dialog and ⋯ menu.
- **2h** A received S-421 route checked without a draft: read-only badge, Planning while checking, check settings split from route values, Copy to edit.
- **2g** Components: verdict variants, leg rows, merged row, warnings panel on a route, markers.

The mock is a reference, not code to port. Build it with Avalonia + ShadUI, the theme resources, the existing `RouteOverlayRenderer` and the #976 `NavigationalWarningCardViewModel`. Findings, depths, times and warning texts are fixtures.

## Decisions

1. **One mode: Check route.** The footer toggle is renamed from "Check passage" to **Check route** and is enabled for any route with two or more waypoints. Clearance is one part of the check and runs only with a draft. (`set_check_mode` keeps its name.)
2. **No draft** (#953): no Clearance tab, no profile, no departure band, no UKC colouring. The verdict card's Clearance row reads "Not checked · + Add draft"; the link expands the Passage card and focuses Draft. Speed and departure, when set, still give ETAs for time-dependent checks; otherwise view time is used.
3. **One verdict, worst group wins.** Must review (any source) → arrival after water-level data ends (neutral; only when nothing is Must review) → Check → clear. Within Must review the title names the worst item by this order: crosses land, crosses drying ground, enters a prohibited area, danger in the corridor, below your UKC setting, warning in force on the route, any other Must review.
4. **Verdict card layout:** title + sub-line, then up to three summary rows (Clearance, Route, Warnings; a row shows only when its checks ran), then the window line (draft only), then the one footer. The big min-UKC number of #887 C3 is replaced by the Clearance row's value and chip.
5. **The line keeps UKC colours only.** Route findings draw on top: sleeves for spans, shaped markers for points. Without a draft the line keeps the normal active-route style (accent) under the wash. Legs are never coloured by their worst finding.
6. **Quiet map:** corridor edges always (dotted), corridor fill only on the selected finding's leg; markers for Must review and Check, Info only when selected; one pill, on the selected finding; markers within 24 px cluster.
7. **Findings list:** source filter chips; same-code Check/Info findings merge with a count; Must review never merges; Info folds by default.
8. **Warnings are a filter, not a view.** "Warnings affecting this route" = the *Warnings* chip. No viewport-scoped warnings list. The Navigational warnings panel gains an "On {route}" scope while Check route is on.
9. **Warning text in place.** Selecting an S-124 finding expands its row into the #976 Warning card (clip at 5 lines, *Show all*), since Object Information is folded in this mode.
10. **S-124 severity** (for #957): crosses the corridor and in force at ETA → Must review; within 0,5 NM (setting) and in force → Check; not in force during the passage, or cancelled → Info with the reason.
11. **Received routes are read-only** (the #391 model, now #960). *Import route…* (a Routes header button) opens a file as a **received route**; the format comes from the file's content. An S-421 file also loads as a dataset and its route starts in **Native** mode; an RTZ file has no native portrayal and opens in **Planning** mode. An S-421 dataset you open explicitly (Datasets, Library, drag and drop) becomes a received route the same way, so there's no "Open as route" action. Datasets loaded by panning a Library source never become routes.
12. **Copy to edit** makes an owned, editable route. Its dialog lists what the copy keeps and what it can't hold (kept for re-export) before anything changes, and the copy is one undoable step. The summary that round 1 put on import lives here, because nothing is lost by opening a received route.
13. **Export…** is in the route ⋯ menu for both kinds of route. One dialog chooses RTZ (1.2 default; 1.1, 1.0) or S-421 (1.0), says how the route is identified in the file (decision 17), and names any field the chosen format can't hold. Your check settings are written only on request (decision 16). Findings are never exported.
14. **Tones** (with #934): findings reuse state tones (Must review = Below, Check = Tight, Info = Low confidence). Night drops to ~0,10 luminance, Day Tight darkens, Dusk keeps #933, No data becomes dotted, Night editing uses a fixed colour (table in Part G).
15. **Checking a received route** works as for an owned route. Turning Check route on switches a Native-mode route to Planning for as long as the mode is on, and back afterwards. The S-421 action-point layer stays on while checking, because the route renderer doesn't draw action points. The footer's Edit becomes **Copy to edit**. Closing the dataset ends Check route and Follow passage, clears the findings and says why.
16. **Check settings are local** (#391 concern 2; **needs your confirmation**). Draft, squat, required UKC and departure are *your* settings for checking a route, not route content. They're stored per route in SoundCharts, editable on read-only routes, and not exported unless you ask. Values in the file (leg draft and UKC, a schedule's departure) are the defaults, marked "from file". The Passage card shows them as a group, **Your check settings**, above the route's own values (speed, cross-track), which are read-only on a received route.
17. **A copy is a new route** (#391 concern 9; **needs your confirmation**). It gets a new route ID, a previous-route reference to the source's route ID and edition, edition 1, and status Planned. Exporting a received route unchanged (for example to convert RTZ to S-421) keeps its route ID, edition and status.
18. **Deferred:** a route-check sweep over departures (route findings don't vary with departure except warnings and S-122 periods; the window line says so); S-411 ice; narrow windows (as in #887).

## Vocabulary

- Toggle: **Check route**. Tags: Clearance · Route check · S-124 · Confidence · Data. Filters: All · Clearance · Route · Warnings.
- Verdict titles (draft): "Crosses land", "Crosses drying ground", "Enters {area kind}", "{Danger kind} in the corridor", "Below your UKC setting in N place(s)", "Inside navigational warning {number}", "Arrival is after the water-level data ends", "Meets your settings · N to check", "Meets your settings".
- Verdict titles (no draft): "N to review", "N to check", "No findings for your settings".
- Never "Safe". "Not for navigation · computed from {sources} for your settings" appears **once**, in the verdict card footer. {sources} lists every product used (S-101, S-102, S-104, S-122, S-124…).
- Times follow the app rule (machine zone + Local/UTC preference). Decimal comma per locale in the mock.

## Open items

### Part A — Mode and no-draft state

- [ ] **A1.** Rename the footer toggle to **Check route**; enable it with ≥ 2 waypoints. Tooltip when disabled: "Add two waypoints to check the route".
- [ ] **A2.** No draft: hide the Clearance tab (it's not in the bottom dock's tab strip while this route is checked without a draft). The dock opens on Timeline if it was open.
- [ ] **A3.** No draft: the line keeps the active-route accent at 4 px on the casing; wash and corridor as in C1/C3.
- [ ] **A4.** Panel header sub-line without a draft: "No draft · {speed} · dep {HH:MM} · arr {HH:MM}" (parts drop when unset).
- [ ] **A5.** "+ Add draft" expands the Passage card and focuses the Draft field in **Your check settings**; check results update when it's set. On a received route this edits the local layer, never the route.
- [ ] **A6. Received route header:** a "Received · read-only" badge after the name; the sub-line adds the source ("S-421 · 421ZZ00WARN ed. 1" or "RTZ 1.2 · file name"). In the route list, the row's meta line ends with "· received".
- [ ] **A7. Display mode:** a Native / Planning segmented control under the header for S-421 routes (any route later, #391 concern 11). While Check route is on it shows Planning, disabled, with the tooltip "Check route draws the route in Planning mode".
- [ ] **A8. Footer on a received route:** **Copy to edit** replaces Edit (same place, same size). The waypoint context menu and Ctrl+E also offer Copy to edit instead of editing.
- [ ] **A9. Passage card on any route:** two groups. **Your check settings** (Draft · squat, Required UKC, Departure) with the note "Used to check the route. Not part of the route." and "from file" on values taken from the file; then **Route** (Speed, Cross-track), read-only and labelled "from file" on a received route.
- [ ] **A10. Dataset closed while checking:** Check route turns off, Follow passage stops, the findings clear, and a toast says "Check route ended: {route} was closed with its dataset".

### Part B — Verdict card

- [ ] **B1. Title and tone** per decisions 3 and the vocabulary. Icons: octagon (destructive), triangle (warning), check (success), ban (neutral).
- [ ] **B2. Sub-line** (11,5, muted): Must review → "{where} · and N more to review"; Check → names the first check item; clear → "Land, drying, dangers, areas and warnings checked" (no draft) or the #887 "where" line.
- [ ] **B3. Summary rows** (24 px, label column 66 px, value, optional chip). Each row is a button that sets the Findings filter and switches to the Findings tab.
  - Clearance: "min {x} m · required {y} m" + chip ("N below" destructive / "tight" warning / "meets" success / "no tide" muted); no draft: "Not checked · + Add draft".
  - Route: "N to review · M to check" + count chip in the worst tone, or "Nothing found" + success "0".
  - Warnings: "N in force on route · M near" + count chip; or "Nothing on route".
- [ ] **B4. Window line** (draft only): as #887 C3, plus "Route-check findings don't change with departure." when route findings exist.
- [ ] **B5. Footer:** the single "Not for navigation · computed from {sources} for your settings".
- [ ] **B6.** Stale results (C10 of #887) dim the whole card, rows included.

### Part C — Map

- [ ] **C1. Corridor edges:** two lines at the port and starboard cross-track limits for every leg, 1 px, dotted 2/3, ink at 45 % (Day) / 55 % (Dusk) / 60 % (Night). Under the route casing.
- [ ] **C2. Sleeves** for span findings: a band along the route between the span's start and end, 16 px at 24 % in the group tone; 20 px at 38 % when selected. Over the UKC line, under markers.
- [ ] **C3. Corridor fill** on the selected finding's leg only: accent at 8 % (Day) / 12 % (Dusk, Night).
- [ ] **C4. Markers** for point findings and at each span's start: 18 px; octagon (Must review), triangle (Check), circle (Info); fill = group tone; 1,6 px ring in the casing colour; glyph "!" (white on Day, black on Dusk/Night). Placed 18–20 px above the route at the nearest point on a 1 px stem. Info markers only when selected.
- [ ] **C5. Leader:** for a feature off the centreline (beyond the corridor or > 40 m), a 1 px dotted line from the stem's foot to the feature.
- [ ] **C6. Clusters:** markers within 24 px merge into one in the worst group's shape, glyph = count. Clicking zooms in two steps or to fit the cluster.
- [ ] **C7. Selected finding:** 15 px ring in the tone and one pill ("{short title} · {HH:MM}") offset up-right; replaces the #887 F3 dashed outline for route findings. The status chip "Zoomed to finding · …" stays.
- [ ] **C8. Legend:** one box, "UKC AT ETA" (draft only; No data drawn dotted) and "FINDINGS" (three shapes, a sleeve, the corridor edge). Header row folds it to a 28 px "Legend" chip; folded state persists per user.
- [ ] **C9.** Warnings and S-122 areas keep their own portrayal; the check adds nothing else to them.

### Part D — Findings list and legs

- [ ] **D1. Filter chips** under the Legs / Findings control: All N · Clearance N · Route N · Warnings N (a chip shows only when its count > 0; Clearance only with a draft). 22 px pills; selected = filled `--foreground`.
- [ ] **D2. Merging:** in Check and Info, findings with the same `code` become one row with "×N" before the tag and a meta line that summarises ("Legs 7 → 9 · 9 stretches · 2,5 NM outside S-102"). Clicking expands sub-rows (indented 24 px), each selectable. Must review never merges.
- [ ] **D3. Info folds** by default: "INFO N ›"; state persists per route.
- [ ] **D4. Leg rows** (Legs tab): add a badge before the min-UKC chip, worst route-check group icon + count, tinted at 10 %. No draft: badge only.
- [ ] **D5. Route-check rows** use the F2 row: meta lines name the leg, the feature and its key value, and the threshold that fired ("shallower than draft + squat + UKC (12,5 m)" or "shallower than safety depth 15 m") and the ETA when known.

### Part E — Warnings

- [ ] **E1. Inline card:** selecting an S-124 finding expands the row with the #976 Warning card (clipAt 5, *Show all*, Copy, Zoom to), plus a link "Open in Navigational warnings". One expanded at a time; Esc or selecting another collapses it.
- [ ] **E2. Status pill** in the inline card is judged at the ETA, not view time: "In force at 06:08" / "Not in force at 06:15" / "Cancelled".
- [ ] **E3. Navigational warnings panel:** while Check route is on, a segmented "All N | On {route name} M" under the search; affected rows get a line "On route · {HH:MM}" or "Near route · {HH:MM}" in `--selected`. Off when the mode is off.
- [ ] **E4. Provider "not for navigation" label** on the Warning card stays hidden inside the findings list (the verdict footer carries the app's disclaimer). Confirm with #932's remaining slice.

### Part F — Received routes, copy and export (for #960 and #961)

- [ ] **F1. Entry points:** a Routes header icon button **Import route…** (download icon, before +); route ⋯ menu **Copy to edit** (received routes) and **Export…** (both kinds), between Reverse and Remove; the footer's Copy to edit (A8). Received routes have no Rename, Reverse or Remove; **Close** in ⋯ (or closing the dataset) removes them.
- [ ] **F2. Picker:** filter "Routes (*.rtz, *.gml, *.xml)". Sniff RTZ (namespace + version 1.0/1.1/1.2) or S-421 GML. No dialog on success: the received route appears in the list, becomes active, and the map fits it.
- [ ] **F3. Open errors:** a dialog with one line and Close: "This file isn't an RTZ or S-421 route." / "RTZ {v} isn't supported yet." / "The route has fewer than two waypoints." An S-421 dataset that isn't a valid route stays a dataset, and its Datasets row says why (#391 concern 12).
- [ ] **F4. Duplicates:** opening the same file again selects the existing received route. Two files with the same route ID and edition give one route; a newer edition replaces the older one's route and tells copies "Source has a newer edition (ed. 4)" on their header (#391 concern 5).
- [ ] **F5. Copy to edit dialog** (520 px): title "Copy to edit", sub "Makes an editable copy. The received route stays as it is."; source card (route name, file name · format + version, "N WP · X NM"); **THE COPY KEEPS** list (check icons, "what → where"); **NOT IN THE COPY** list (info icons, with reasons; preserved content says "kept and written back on export"); identity line "New route ID · references {route ID} ed. {n} · status Planned" (decision 17); Cancel / **Make copy**. The copy is named "{name} (copy)", becomes active and opens in Edit mode; one undo removes it.
- [ ] **F6. Preserved content that goes stale:** after a waypoint insert or delete, preserved schedules no longer line up. The copy's header shows "2 schedules no longer match the route" with a menu: Drop them / Keep and export as they are (#391 concern 8). Default on export: drop, and say so in the export note.
- [ ] **F7. Export dialog** (500 px): title "Export route" + route summary; FORMAT radio cards — RTZ "IEC 61174 route exchange · For ECDIS and route planning tools." with a version select (1.2, 1.1, 1.0), and S-421 "IHO route plan, edition 1.0 · For S-100 systems."; identity line (owned copy: "New route · references {source} ed. {n} · status Planned"; received route: "Route ID {id} ed. {n} kept"); checkbox **Also write my check settings** (off; "Draft, required UKC and departure. RTZ writes them as a calculated schedule and the UKC extension."); a muted note listing only the route's fields the format can't hold; Cancel / **Export…** (save dialog with "{route name}.rtz|.gml"). Remember the last format and version.
- [ ] **F8. Persistence:** received routes aren't written to `routes.json`. An S-421 received route returns after a restart only if its dataset does; an RTZ received route is remembered as an open file and reopened if the file still exists. Your check settings for a received route are kept by route ID + edition.

### Part G — Tones (folds in #934)

| Element | Stroke | Day | Dusk | Night |
|---|---|---|---|---|
| Meets your settings | 5 px solid | `#13895B` | `#20895C` | **`#226746`** (was `#2E8A5E`) |
| Tight / Check | 5 px solid | **`#B07D00`** (was `#E0A008`) | `#C18D12` | **`#735614`** (was `#A27A1C`) |
| Below / Must review | 7 px solid | `#D42A3A` | `#CA3240` | **`#A4323C`** (was `#C03A46`) |
| Low confidence / Info | 5 px dash 6/4 | `#5E6B7E` | `#6A7584` | **`#57606F`** |
| No data | 5 px **dot 1,5/3,5** | `#8C95A3` | `#58616E` | **`#515A65`** |
| Route, no clearance / editing | 4 px | user accent | `#4F7BD0` | **`#3F64A6`** fixed |
| Casing | 10 px | `#FFFFFF` | `#000000` | `#000000` |

- [ ] **G1.** Apply the table to `RouteCheckColors` / `RouteOverlayColors`; drop `ColorContrast.AccentFor` for Night editing.
- [ ] **G2.** Keep `RoutePaletteTests`' rules; add: every Night state ≤ 0,12 relative luminance; Day Tight ≥ 3:1 on white; No data uses the dotted pattern; markers and sleeves use the state tones.
- [ ] **G3.** Regenerate the Day/Dusk/Night goldens; add goldens for 2a, 2b, 2c states.

## AutomationId hints

| Element | AutomationId |
|---|---|
| Check route toggle | `Routes.CheckRouteToggle` (rename of `Routes.CheckPassageToggle`) |
| Verdict summary rows | `Routes.Check.Summary.Clearance`, `.Route`, `.Warnings` |
| Add draft link | `Routes.Check.Summary.AddDraft` |
| Filter chips | `Routes.Check.Filter.All`, `.Clearance`, `.Route`, `.Warnings` |
| Merged row expander | `Routes.Check.Finding.Expand` (sub-rows reuse `Routes.Check.Finding`) |
| Info group fold | `Routes.Check.Findings.InfoToggle` |
| Inline warning card | `Routes.Check.Finding.WarningCard`; link `Routes.Check.Finding.OpenInWarnings` |
| Leg badge | `Routes.Check.Leg.Badge` |
| Legend fold | `Map.RouteCheckLegend.Toggle` |
| Warnings scope | `NavigationalWarnings.Scope.All`, `NavigationalWarnings.Scope.Route` |
| Import button | `Routes.Import` |
| Received badge, display mode | `Routes.Active.ReadOnlyBadge`, `Routes.DisplayMode.Native`, `Routes.DisplayMode.Planning` |
| Copy to edit | `Routes.CopyToEdit` (footer), `Routes.Row.Menu.CopyToEdit`; dialog `RouteCopy.Dialog`, `RouteCopy.Confirm`, `RouteCopy.Cancel`, lists `RouteCopy.Keeps`, `RouteCopy.NotKept` |
| Check settings group | `Routes.Passage.CheckSettings`, `Routes.Passage.RouteValues` |
| Open error dialog | `RouteImport.Error`, `RouteImport.Close` |
| Export menu item | `Routes.Row.Menu.Export` |
| Export dialog | `RouteExport.Dialog`, `RouteExport.Format.Rtz`, `RouteExport.Format.S421`, `RouteExport.RtzVersion`, `RouteExport.IncludeCheckSettings`, `RouteExport.Confirm` |

## MCP parity

- `get_check_state`: add `hasDraft`, `clearanceChecked`, `verdictKind` (`must_review`, `data_ends`, `check`, `clear`), `verdictTitle`, `verdictSubtitle`, and `summary` rows (`clearance`, `route`, `warnings`, each with `text` and `chip`).
- `get_findings`: add `filter`, per-source counts, `merged` groups (`code`, `count`, member ids), and for S-124 findings `warning` (number, status at ETA, validity, text).
- `set_findings_view {filter?, infoOpen?, expandedId?}`: sets the chips, the Info fold and the expanded merged/warning row.
- `select_finding` keeps its contract; the result says which marker or sleeve is highlighted.
- `set_map_legend {open}` and `get_map_legend`.
- Navigational warnings: `list_navigational_warnings` gains `scope` (`all`, `route`) and per-warning `route` (`on`, `near`, ETA).
- Received routes (#960/#961, #391): `import_route {path|content}` opens a received route and returns it; `list_routes`/`get_route` report `readOnly`, `source` (format, version, file, route ID, edition, status) and `displayMode`; `set_route_display_mode {routeId, mode}`; `copy_route {routeId, dryRun?}` returns the keeps / not-kept lists and the new identity (dry run = the dialog); editing a read-only route fails with `route_read_only`, naming `copy_route`; `export_route {format, version?, includeCheckSettings?, path?}` returns the identity and the "can't hold" list.
- Check settings: `get_passage`/`set_passage` mark each value `checkSetting` or `route`, and `fromFile`; `set_passage` on a received route accepts check settings only.

## Tests

- **No draft (2a):** the toggle is enabled with two waypoints; the Clearance tab is absent; the line uses the accent; the card shows "2 to review" and "Not checked · + Add draft"; the generator's `RouteWarnings.For` oracle gives the Must review / Check / Info split in the mock.
- **Priority:** a fixture with drying ground and below-UKC → title "Crosses drying ground"; remove drying → "Below your UKC setting in N places"; arrival after data ends with no Must review → neutral; with a Must review → Must review wins.
- **Filters and merging:** chips filter; same-code Check findings merge with the right count; Must review never merges; MCP returns every member id.
- **Warnings:** selecting an S-124 finding expands the card with status at ETA; changing departure moves a warning between Must review and Info; the panel's "On route" scope lists exactly the warnings with findings.
- **Map:** markers only for Must review and Check; Info shows when selected; clustering at 24 px; corridor fill follows the selected finding's leg.
- **Received routes:** opening the RTE-TEST S-421 fixtures and the generator's `421ZZ00WARN` gives read-only routes in Native mode; RTZ samples open in Planning mode; edits fail with `route_read_only`; Check route switches to Planning and back; closing the dataset ends the check cleanly (A10).
- **Copy and export:** `copy_route` dry-run lists match the fixtures; cancel changes nothing; the copy is one undo step and gets a new route ID with a previous-route reference; check settings are exported only when asked; export notes list only fields present.
- **Palettes:** `RoutePaletteTests` rules plus G2; goldens on Day/Dusk/Night.
- **Strings:** "Safe" never appears; "Not for navigation" appears once in the visual tree in check mode.

## Suggested slicing for #959

1. **Verdict and no-draft state** (A1–A5, B1–B6) with `get_check_state` additions. Needs #953.
2. **Findings list** (D1–D5) with `set_findings_view` and `get_findings` additions.
3. **Map layer** (C1–C9) in `RouteOverlayRenderer`, with goldens.
4. **Warnings in place** (E1–E4); needs #957.
5. **Tones** (G1–G3), closing #934.

Part F, A6–A10 and the check-settings split (decision 16) belong to #960 (S-421, received routes, Copy to edit, shared dialogs) and #961 (RTZ). #959's A1–A5 work on owned routes first; A7 and A8 need #960.

## Decisions waiting for confirmation

- **Decision 16** (#391 concern 2): check settings are a local, per-route layer, not exported by default, with file values as defaults.
- **Decision 17** (#391 concern 9): a copy is a new route (new route ID, previous-route reference, edition 1, status Planned); an unchanged received route keeps its identity on export.

## Acceptance

- A route with no draft can be checked, and the first thing shown answers "what on this route needs my attention?".
- With a draft, one card answers both clearance and route checks, worst first, with the min UKC still one glance away.
- At 1:40 000 on the generated scenario, every warning and area the route meets is visible on the map without a label other than the selected one.
- An S-124 warning on the route can be read in full without leaving Check route.
- "Safe" never appears; "Not for navigation" appears once, on the verdict.

## Out of scope

- Route optimisation, auto-routing, own-ship monitoring.
- A departure sweep for route-check findings.
- S-411 ice checks.
- Narrow and mobile layouts.
- Editing per-leg safety contour and depth (still from Mariner settings).

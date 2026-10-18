# Route checks in Check mode — design handoff

Repo: `philliphoff/EncDotNet.S100`. Scope: `src/EncDotNet.S100.Viewer` (plus the MCP tools it hosts). Issues: #958 (this round) → #959 (UX), with #953–#957 (producers), #960/#961 (exchange) and #934 (palettes). Part of #888.
Round: **1** · Issued: 2026-10-17. Input: [route-checks-brief.md](route-checks-brief.md) and `screenshots/` (main 53ae46fc).

Mock: **Route Checks** artboard in the *SoundCharts S-100 Viewer* design canvas, <https://claude.ai/artifact/2hWbgQ9wKM1gC2gM51mSCs> (page *Navigation & time*, `project/Route-Checks.dc.html`), built with the *EncDotNet.S100* design system (<https://claude.ai/artifact/UywKszqyKDATtEERvBqWZT>). Frames are 1100×700, the default window.
- **2a** No draft · warnings and an MPA on the route. Light, Day. Generated `warnings-on-route`. Live: rows select, filter chips filter, legend folds.
- **2b** Draft set · clearance and route checks in one verdict. Light, Day. UKHO Solent, #887 route at 07:10.
- **2c** A warning near the route, read in place. Dark, Night (proposed tones).
- **2d** Map treatment on Day / Dusk / Night, and the tone table.
- **2e** Import summary. **2f** Export dialog and ⋯ menu.
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
11. **Import route…** is a Routes header button; the format comes from the file's content; a summary lists what's imported and what isn't before anything changes; the result is a new active route, one undoable step. A loaded S-421 dataset offers *Open as route* (read-only until copied, per #391).
12. **Export…** is in the route ⋯ menu. One dialog chooses RTZ (1.2 default; 1.1, 1.0) or S-421 (1.0), with *Include passage settings*, and names any field the chosen format can't hold. Findings are never exported.
13. **Tones** (with #934): findings reuse state tones (Must review = Below, Check = Tight, Info = Low confidence). Night drops to ~0,10 luminance, Day Tight darkens, Dusk keeps #933, No data becomes dotted, Night editing uses a fixed colour (table in Part G).
14. **Deferred:** a route-check sweep over departures (route findings don't vary with departure except warnings and S-122 periods; the window line says so); S-411 ice; narrow windows (as in #887).

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
- [ ] **A5.** "+ Add draft" expands the Passage card and focuses the Draft field; check results update when it's set.

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

### Part F — Import and export

- [ ] **F1. Entry points:** Routes header icon button **Import route…** (download icon, before +); route ⋯ menu **Export…** (between Reverse and Remove); S-421 dataset row **Open as route**.
- [ ] **F2. Picker:** filter "Routes (*.rtz, *.gml, *.xml)". Sniff RTZ (namespace + version 1.0/1.1/1.2) or S-421 GML.
- [ ] **F3. Import dialog** (520 px): title "Import route", sub "Nothing changes until you choose Import."; file card (route name, file name · format + version, "N WP · X NM"); **IMPORTED** list (check icons, "what → where"); **NOT IMPORTED** list (info icons, with reasons; extensions say "kept and written back on RTZ export"); note "Imported as a new route. Undo removes it."; Cancel / **Import**.
- [ ] **F4. Import errors:** one line replacing both lists, Close only: "This file isn't an RTZ or S-421 route." / "RTZ {v} isn't supported yet." / "The route has fewer than two waypoints."
- [ ] **F5. Export dialog** (500 px): title "Export route" + route summary; FORMAT radio cards — RTZ "IEC 61174 route exchange · For ECDIS and route planning tools." with a version select (1.2, 1.1, 1.0), and S-421 "IHO route plan, edition 1.0 · For S-100 systems."; checkbox "Include passage settings" (on); a muted note listing only the route's fields the format can't hold; Cancel / **Export…** (opens the save dialog with "{route name}.rtz|.gml"). Remember the last format and version.

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
| Import dialog | `RouteImport.Dialog`, `RouteImport.Confirm`, `RouteImport.Cancel`, lists `RouteImport.Imported`, `RouteImport.NotImported` |
| Export menu item | `Routes.Row.Menu.Export` |
| Export dialog | `RouteExport.Dialog`, `RouteExport.Format.Rtz`, `RouteExport.Format.S421`, `RouteExport.RtzVersion`, `RouteExport.IncludePassage`, `RouteExport.Confirm` |
| S-421 dataset action | `Datasets.Row.OpenAsRoute` |

## MCP parity

- `get_check_state`: add `hasDraft`, `clearanceChecked`, `verdictKind` (`must_review`, `data_ends`, `check`, `clear`), `verdictTitle`, `verdictSubtitle`, and `summary` rows (`clearance`, `route`, `warnings`, each with `text` and `chip`).
- `get_findings`: add `filter`, per-source counts, `merged` groups (`code`, `count`, member ids), and for S-124 findings `warning` (number, status at ETA, validity, text).
- `set_findings_view {filter?, infoOpen?, expandedId?}`: sets the chips, the Info fold and the expanded merged/warning row.
- `select_finding` keeps its contract; the result says which marker or sleeve is highlighted.
- `set_map_legend {open}` and `get_map_legend`.
- Navigational warnings: `list_navigational_warnings` gains `scope` (`all`, `route`) and per-warning `route` (`on`, `near`, ETA).
- Exchange (#960/#961): `import_route {path|content, dryRun?}` returns the summary lists (dry run = the dialog); `export_route {format, version?, includePassage?, path?}` returns the "can't hold" list.

## Tests

- **No draft (2a):** the toggle is enabled with two waypoints; the Clearance tab is absent; the line uses the accent; the card shows "2 to review" and "Not checked · + Add draft"; the generator's `RouteWarnings.For` oracle gives the Must review / Check / Info split in the mock.
- **Priority:** a fixture with drying ground and below-UKC → title "Crosses drying ground"; remove drying → "Below your UKC setting in N places"; arrival after data ends with no Must review → neutral; with a Must review → Must review wins.
- **Filters and merging:** chips filter; same-code Check findings merge with the right count; Must review never merges; MCP returns every member id.
- **Warnings:** selecting an S-124 finding expands the card with status at ETA; changing departure moves a warning between Must review and Info; the panel's "On route" scope lists exactly the warnings with findings.
- **Map:** markers only for Must review and Check; Info shows when selected; clustering at 24 px; corridor fill follows the selected finding's leg.
- **Import/export:** dry-run summary lists match the fixture RTZ/S-421 files; cancel changes nothing; import is one undo step; export notes list only fields present.
- **Palettes:** `RoutePaletteTests` rules plus G2; goldens on Day/Dusk/Night.
- **Strings:** "Safe" never appears; "Not for navigation" appears once in the visual tree in check mode.

## Suggested slicing for #959

1. **Verdict and no-draft state** (A1–A5, B1–B6) with `get_check_state` additions. Needs #953.
2. **Findings list** (D1–D5) with `set_findings_view` and `get_findings` additions.
3. **Map layer** (C1–C9) in `RouteOverlayRenderer`, with goldens.
4. **Warnings in place** (E1–E4); needs #957.
5. **Tones** (G1–G3), closing #934.

Import/export (Part F) belongs to #960 (S-421 + shared dialogs) and #961 (RTZ version list and sniffing).

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

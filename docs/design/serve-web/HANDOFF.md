# The `s100 tiles serve` web page — design handoff

Repo: `philliphoff/EncDotNet.S100`. Scope: `tools/EncDotNet.S100.Cli` (the serve host and its Razor components). Issue: #1574, part of epic #1573. Foundation work (hosting, the `--token` prefix, the data interface, bundled MapLibre) is #1575.
Round: **1** · Issued: 2026-11-18. Input: [serve-web-brief.md](serve-web-brief.md), [serve-webapp-blazor-spike.md](../serve-webapp-blazor-spike.md).

Mock: the **Serve web · design pass** artboard in the *SoundCharts S-100 Viewer* design canvas, <https://claude.ai/artifact/2hWbgQ9wKM1gC2gM51mSCs>, on a new page **Serve web** (`project/Serve-Web-Design-Pass.dc.html`). It gets its own page because it isn't the desktop app, and later web passes (legend, mariner settings, managing datasets) can go next to it.

The mock is a reference, not code to port. Build it with Razor components, plain CSS on the tokens below, and MapLibre GL.

**What's real and what isn't.** The map tiles are real `s100 tiles serve` output: the three IC-ENC Solent S-101 cells the spike used, the UKHO S-111 Solent trial forecast (`111UK_20210401T000000Z_SolentAndAppr_dcf2.h5`), and 279 NOAA ENC cells for Washington. The coverage outlines come from `/datasets`. The pick card shows what the server returned at North East Ryde Middle buoy, with names and values decoded by hand the way the page should decode them. The basemap is OpenStreetMap, greyed for the mock as the page would grey it. Fixtures: S-57 editions, the two problem datasets, the no-coverage dataset, the tile set's name and file, the LAN address and the token. The browser's time zone is Europe/London in every frame.

## Frames

Captures are in the `screenshots/` folder, named `10-<frame>-….png`. Frames without a capture are on the artboard only.

| Frame | Brief | What it shows | Capture |
|---|---|---|---|
| **A1** | B1–B3 | Desktop, light: header, rail, Datasets panel with 3 S-101 cells, hover ↔ outline, overlay buttons, scale bar, readout. | [A1](screenshots/10-A1-shell-light.png) |
| **A2** | B2 | Desktop, dark (browser dark, Day chart): dataset details with the selected coverage outlined. | [A2](screenshots/10-A2-details-dark.png) |
| **A3–A5** | B1, B2 | Phone: sheet lowered (summary), half open (list), dark full height (details). | [A4](screenshots/10-A4-phone-sheet.png) |
| **Q2a–c** | Q2 | Rail + docked panel (recommended), fixed tabbed panel, floating cards. | — |
| **Q1a–c, states** | B1, B7, Q1 | Header with three names; the server-state pill in light, dark and night; phone headers. | — |
| **C1** | B2 | 279 cells: search, filter chips, groups by layer → product · purpose, all coverages outlined zoomed out. | [C1](screenshots/10-C1-300-datasets-light.png) |
| **C2, C3** | B2 | Dark: search filters a flat list with a count, only matches outlined. Phone with groups. | — |
| **D1** | B3 | Zoomed out below the minimum zoom: outlines and a "the chart draws from zoom 13" chip. | [D1](screenshots/10-D1-zoomed-out.png) |
| **D2** | B3 | Past the maximum zoom: overzoomed tiles, an amber zoom readout and a "Back to 16" chip. | — |
| **B3a–c** | B3 | Outline visibility: always, zoomed out + hover + selection (recommended), hover only. | — |
| **Q4a–d** | Q4 | Basemap: muted OSM (recommended), OSM, none, night (muted and dimmed). | — |
| **E1** | B4 | Display panel (palette, layers, basemap, times, page appearance, Mariner settings "Later") and the time bar over S-111. | [E1](screenshots/10-E1-display-time-light.png) |
| **E2** | B4, Q3 | Night palette switches the page to night chrome. | [E2](screenshots/10-E2-night-chrome.png) |
| **E3, E4** | B4 | Phone: time bar above the sheet; the Display sheet in night. | — |
| **Q3a–c** | Q3 | Day follows the browser, Dusk forces dark, Night forces night. | — |
| **B4a, B4b** | B4 | Time control: bottom bar (recommended) vs header button and popover. | — |
| **B8d** | B8 | All time steps in the past (the S-111 file is from April 2021). | — |
| **F1** | B5 | Pick card beside the marker (recommended). | [F1](screenshots/10-F1-pick-card.png) |
| **F2** | B5 | Alternative: Object Information in the side panel, feature highlighted on hover (dark). | — |
| **F3, F4** | B5 | Phone: pick in the sheet with the S-111 current at the point; dark full height. | [F3](screenshots/10-F3-phone-pick.png) |
| **B5h** | B5 | Highlight of the hovered or expanded feature. | — |
| **G1** | B6 | Use in GIS panel: layer and palette pickers, four links with Copy, per-client steps. | — |
| **G2** | B6 | With `--token` on a LAN (dark): masked token, warning, "Link copied, with the token" toast. | [G2](screenshots/10-G2-gis-token-dark.png) |
| **G3, Q6a–c** | B6, Q6 | Phone; header button + panel (recommended) vs dialog vs URL bar on the map. | — |
| **H1** | B7 | A dataset added: toast, New tag, outline. | — |
| **H2** | B7 | Disconnected banner, a row Reloading, Not served list with reasons, rail badge (dark). | [H2](screenshots/10-H2-disconnected-problems.png) |
| **H3, Q7a–c** | B7, Q7 | Phone with the live link dropped (amber); pill + toasts (recommended) vs activity log vs pill only. | — |
| **I1–I4** | B8 | No datasets left; a dataset with no coverage; wrong token (404), desktop and phone. | [I3](screenshots/10-I3-wrong-token.png) |
| **J1–J3** | §2 | Built tile set: metadata panel, no list, no pick; zoomed out with the bounds shaded outside; phone. | [J1](screenshots/10-J1-tileset.png) |
| **K** | — | Tokens (light, dark, night) and components. | — |

## Answers to §8

| # | Answer |
|---|---|
| **Q1** | **"s100 serve"**, the command people ran, with the map glyph the viewer's title bar uses. The header puts what's served next to it ("3 datasets · S-101", or the tile set's TileJSON name). The browser tab reads "3 datasets · S-101 — s100 serve". "SoundCharts Server" makes people expect the app's tools. **Left for the user** as a branding call. |
| **Q2** | **The viewer's rail with one docked panel.** Datasets, Display and Use in GIS, one panel at a time, collapsible, and the rail has room for Legend, Search and Mariner settings later. At phone width the rail becomes the tab bar of a bottom sheet with three heights. |
| **Q3** | **Day follows the browser; Dusk forces dark chrome; Night forces a new night chrome** (darker than dark, dimmed text, no white). A Page setting (Auto, Light, Dark) overrides Auto for Day only. |
| **Q4** | **A muted OpenStreetMap by default**, greyed in the browser with MapLibre raster paint, with OpenStreetMap and None as choices. With Night it's inverted and dimmed. Whether to keep OSM's own tile servers as the source is **left for the user** (open question 1). |
| **Q5** | **Yes.** The view, palette, time, layer, selected dataset and pick go in the URL hash. A link opens what you saw. The header's link button copies it and warns when it contains the token. |
| **Q6** | **Behind one button.** "Use in GIS" in the header and in the rail opens a panel. The main screen stays a map. |
| **Q7** | **A header pill** (Live, Updating, Reconnecting, Disconnected, Server stopped), a toast and a row tag for each change, problems pinned at the top of the list. Quiet when nothing changes. |

## Decisions

### Shell and header (B1)

1. **Layout.** A 52 px header, a 56 px icon rail, a 340 px docked panel that pushes the map, then the map (A1). The panel closes from its chevron or from the active rail item, and the rail stays. Under 768 px the rail becomes a bottom sheet (A3–A5).
2. **Phone sheet.** Three heights: lowered (about 106 px: handle, "3 datasets · S-101", the three tabs), half (about 430 px), and full (the map shows 60 px above it). The tabs are the rail's items. A pick, Use in GIS and details open it to half or full height.
3. **Header.** On the left: the map glyph, **s100 serve**, a divider, and the served summary as a button that opens Datasets. On the right: **Not for navigation**, the server-state pill, a link button ("Copy a link to this view") and **Use in GIS**. The Not for navigation chip is amber and always visible, at every width and in every state (B, A3).
4. **Phone header.** Name, Not for navigation and a round state button (a dot, or a spinner, or a crossed signal). The served summary moves to the sheet (A3).
5. **Overlay buttons.** The viewer's column at the top right: zoom in and zoom out, a gap, zoom to the data, and the compass (north up). They're 40 px rounded squares on a translucent card, with a round compass. There's no pick button: a click on the map always picks (decision 21). On a phone only zoom to the data and the compass remain, because pinch zooms.
6. **Map furniture.** A scale bar at the top centre (as in the viewer), a readout at the bottom left with the cursor position and zoom ("50°46.5′N 001°13.9′W z13.0"), and the attribution at the bottom right. The zoom in the readout is for the GIS people who need zoom levels.

### What's served (B2)

7. **Dataset rows** follow the Library rows: a product tag, the dataset id in mono, then edition and producer to the right, with the file name below in mono caption. Time-varying data adds a third line: dynamicity, time range and step count (E1). State tags (New, Reloading, No coverage) go before the edition.
8. **Grouping.** By layer when there are layers (`--layers-by product`). Within a layer, or with no layers: by product, and for S-57 by navigational purpose (Overview … Berthing, from the cell name). With one layer, that layer is a summary line ("279 datasets · layer chart") rather than a group. Groups start collapsed when there are more than 30 datasets, except the first non-empty one (C1).
9. **Search and filters.** One search box over id and file name. Typing turns the groups into a flat list with "11 of 279". Filters are chips: Product, Purpose, Problems. While searching or filtering, only the matches are outlined on the map (C2).
10. **Details.** Selecting a row opens its details in the panel (a back arrow returns): tags, **Zoom to**, **Copy link**, then File, Product (with its name), Edition and update, Producer, Layer, Bounds (degrees and minutes), Coverage (data coverage with the area count, or "Bounds only"), Display scale for S-101, Dynamicity, Issued, Time steps. When the dataset is in a layer, the layer's TileJSON and XYZ links follow with Copy (A2 shows a dataset without layers, so the links are absent there).
11. **Large lists** render the visible rows only. At 300 rows the server-side component can render them all, but a folder of 7,000 NOAA cells can't.

### Map (B3)

12. **Start view.** Fit the data's bounds, then clamp to at least the minimum zoom, centred on the TileJSON centre. The page opens with the chart drawn. This fixes today's blank start.
13. **Below the minimum zoom** every coverage is outlined and a chip says "The chart draws from zoom 13. Outlines show where the data is." with **Zoom to the data** (D1).
14. **Past the maximum zoom** MapLibre overzooms. A chip says "Past zoom 16, the last the server renders. Tiles are enlarged." with **Back to 16**, and the zoom in the readout turns amber (D2). It's the same idea as the viewer's overscale indication.
15. **Outlines (B3b).** Outlined: everything when zoomed out below the tiles, the row under the pointer, and the selected dataset. Otherwise none, so the chart's own limits stay readable. The styles are the viewer's Library styles (*Coverage Outline Styles* 1c): 1 px served green `#3D8A5A` on a 2 px casing in the page background at 60%. Hovered and selected outlines are 2 px in the accent with a 6% fill (8% when selected). While one is selected, the others fade to 50%.
16. **Outside the tiles.** A built tile set's bounds stand in for coverage: a dashed rectangle with the outside shaded (J2). Datasets don't need this, because their outlines already say where the data is.

### Display (B4)

17. **One Display panel**: Chart palette (Day, Dusk, Night, with icons), Layers (All layers, or one, with counts), Basemap (Muted, OpenStreetMap, None), Times (Local, UTC) when anything has times, Page (Auto, Light, Dark), and a dashed **Mariner settings · Later** row as a placeholder (E1). A built tile set keeps Basemap and Page only.
18. **Chrome and palette (Q3).** Day plus Auto follows `prefers-color-scheme`. Dusk forces dark. Night forces night chrome: background `#000`, text `#A1A1AA`, dimmed accent, the basemap inverted and dimmed (E2). The Page setting applies with Day (see open question 3 for Dusk) and never overrides Night.
19. **Time bar.** It appears only when something served has time steps. It floats at the bottom centre of the map: step back, play, step forward, the date and time (time in mono, large), a track with day ticks, and Local/UTC. A second line reads "S-111 · Forecast · issued … · step 41 of 288, every 15 min" (E1). On a phone it's two rows above the lowered sheet, and it docks into the sheet's top when the sheet rises (E3). Play advances one step a second and stops at the end.
20. **Times.** Always in the browser's time zone with its abbreviation ("11:00 BST"), or in UTC. Never in the data area's zone. The Times row says which zone Local is ("Europe/London (BST)"). When every step is in the past or the future, the bar says so and offers **Go to latest** or the first step, and the tiles keep the server's first-step default (B8d).

### Features at a point (B5)

21. **A click picks.** There's no pick mode. A click drops a marker (accent ring and dot) and opens the card. Esc or the card's close button clears it. Dragging the map doesn't pick.
22. **Desktop: a card beside the marker** with a pointer, as the viewer's pick report popover (F1). It flips sides to stay on the map. **Phone: the sheet** at half height, with the marker kept in view above it (F3).
23. **Card content.** OBJECT INFORMATION, the position in degrees and minutes, and the feature count. Coverage values first when there are any ("S-111 · Surface current · Forecast", "0.3 kn → 110° T at 11:00 BST"). Then features with catalogue names ("Buoy Lateral") and a one-line identity (name, light description or depth range). Only the first is expanded. Attributes have catalogue names and decoded values ("Category of lateral mark: Port-hand lateral mark"), plus a provenance line with the product tag, dataset id and geometry. Meta and area features that rarely matter (caution areas, quality of survey, data coverage) are behind "Show 9 more…".
24. **Highlight is in the basics.** The hovered or expanded feature is drawn in the accent from the geometry the query already returns: a ring for a point, a 2 px line for a curve, an outline with a 10% fill for a surface (B5h, F2).
25. **The side-panel alternative (F2) is rejected** for the basics: it swaps out the dataset list and takes the eye away from the pick. It may come back for long lists or a pinned pick.

### Use in your GIS (B6)

26. **Use in GIS panel** (G1). An intro line, **Layer** and **Palette** pickers that rewrite every link, then four links with **Copy**: XYZ tiles (QGIS, Leaflet), TileJSON (MapLibre), WMTS (QGIS, ArcGIS), WMS (GetFeatureInfo too). A note covers `@2x` and, when there are times, `?t=`. Below that, tabs for QGIS, ArcGIS, MapLibre and Leaflet with three steps or a snippet each.
27. **Token.** With `--token` the panel masks it (`…/••••••••/tiles.json`) and shows a warning: "These links contain the access token. Anyone who has one can see these charts." with **Show token**. Copy always copies the full link, and its toast says "Link copied, with the token" (G2). The header's link button warns the same way.

### Server state and live changes (B7)

28. **The pill.** Live (green dot), Updating… (spinner, amber: datasets reloading or the revision changing), Reconnecting… (amber: the live connection dropped but `/healthz` answers), Disconnected (red: `/healthz` fails too), Server stopped (red: the server closed the connection cleanly). The tooltip gives the revision and the refresh interval.
29. **Changes.** A dataset added: its row gets a **New** tag for 60 s, its outline flashes once, and a toast names it with **Show** (zoom to it). Removed: a toast "Dataset removed: …" and the row goes. Reloaded: the row shows **Reloading** while it reloads and a toast follows if it fails. The map refetches tiles on a revision change, as the current page does.
30. **Lost connection.** Disconnected adds a red banner under the header ("Can't reach the server. The map keeps what it has. Trying again in 8 s." with **Try now**) and dims the map. When only the live link drops, the banner is amber ("Live updates paused. Reconnecting…"), because tiles still load over plain HTTP (H2, H3). This replaces Blazor's default reconnect dialog.
31. **Problems.** Datasets that failed to load or were skipped are pinned above the list in a **Not served · N** block, each with its reason in one sentence (H2). The rail's Datasets icon gets a red count badge. The Problems filter chip shows only them.

### Edge states (B8)

32. **No datasets left.** The page stays up and the map keeps the last view. The panel says "No datasets are being served" and how to add one (I1). The header reads "No datasets".
33. **No coverage.** The row gets a **No coverage** tag. Details say why it can't be outlined or framed, and **Zoom to** is disabled (I2).
34. **Wrong or missing token.** The server returns a small static 404 for every unknown path: "Nothing here", plus one sentence saying that links to a server started with a token include it in the path. No name, no data, no live connection (I3, I4).

### Built tile set

35. **Tile set variant.** The header shows the TileJSON name and the format ("Solent approaches · PMTiles"). The rail's first item is **Tile set**: name, tags (container, format, @2x), File, Format, High-DPI, Zoom, Bounds, Centre, and "Made by" when the TileJSON has an `s100` block, plus **Zoom to** and a note that there's nothing to list or pick. A click shows a toast, "No feature information in a tile set" (J1–J3).

### URLs (Q5)

36. **State in the hash**, so it never reaches the server: `#13/50.775/-1.235&palette=night&t=2021-04-01T10:00Z&layer=S111&dataset=101GB0040242B&pick=50.7705,-1.1980`. The view part is MapLibre's own hash format. Defaults are left out. Changes use `history.replaceState`, so Back leaves the page rather than stepping through pans.

## Tokens and components

**Tokens.** The EncDotNet.S100 design-system tokens (ShadUI zinc), as CSS custom properties on `:root`, with `[data-theme="dark"]` and `[data-theme="night"]` sets. The night set and two map tokens are new.

| Token | Light | Dark | Night (new) |
|---|---|---|---|
| `--background` | `#FFFFFF` | `#09090B` | `#000000` |
| `--card` | `#FFFFFF` | `#18181B` | `#0C0C0E` |
| `--foreground` | `#09090B` | `#FAFAFA` | `#A1A1AA` |
| `--muted-foreground` | `#71717A` | `#A1A1AA` | `#7C7C86` |
| `--muted` | `#F4F4F5` | `#27272A` | `#18181B` |
| `--border` | `#E4E4E7` | `#27272A` | `#1F1F23` |
| `--selected` | `#2563EB` | `#3B82F6` | `#2B4C9B` |
| `--success` | `#16A34A` | `#22C55E` | `#2F7A47` |
| `--warning` | `#D97706` | `#F59E0B` | `#8A6516` |
| `--destructive` | `#DC2626` | `#DC2626` | `#7F1D1D` |
| `--outline-served` (new) | `#3D8A5A` | `#4FA571` | `#3E6E50` |
| `--map-ground` (new) | `#E4E4E7` | `#1E1E21` | `#0B0B0C` |

Warning and error text uses darker shades for 4.5:1 (`#B45309` and `#B91C1C` in light). Spacing is on a 4 px grid. Radii are 6 px for controls and 8–10 px for cards. The shadows are the design system's `shadow` (floating map controls) and `shadow-lg` (cards, toasts).

**Type.** The system stack, `Inter, system-ui, -apple-system, "Segoe UI", sans-serif`, with no web font download; `ui-monospace, "SF Mono", Menlo, Consolas` for ids, coordinates, times and URLs. Labels are 11 px, 600 weight, uppercase, tracked. Body text in panels is 13 px; dataset ids are 13 px mono, 600 weight.

**Icons.** Inline SVG, 24 px grid, 1.6–2 px stroke (the viewer's Lucide-style set): map, layers, sliders, plug, info, plus, minus, fit, compass, search, copy, link, check, x, chevrons, back, play, step back, step forward, clock, sun, sunset, moon, warn, lock, refresh, wifi-off, loader.

**Components** (one Razor component each unless noted):

- `ServeHeader` (name, served summary, Not for navigation chip, `StatePill`, link button, Use in GIS button), with a phone variant.
- `Rail` and `Panel` (header with label, back and close; body; optional footer). `Sheet` for phones (three heights, tab bar).
- `DatasetRow`, `GroupHeader`, `SearchBox`, `FilterChip`, `DatasetDetails`, `NotServedBlock`.
- `Tag` (product, neutral, outline, new, warn, err).
- `MapOverlayButtons`, `ScaleBar`, `MapReadout`, `MapChip` (the centred notice with an action).
- `DisplayPanel`, `Segmented`, `RadioList`.
- `TimeBar` (desktop and phone).
- `ObjectInformation` (card body, shared by the floating card and the sheet), `FloatingCard`, `PickMarker` (JS-drawn).
- `UseInGisPanel`, `CopyField`.
- `Toast`, `Banner`.
- `TileSetPanel`, `EmptyState`, and the static 404 page.

Map overlays (coverage outlines, the pick marker, the highlight, the tile-set bounds) are MapLibre GeoJSON layers fed from the component through the small JS module. They aren't DOM.

## Proposed slices for the basics

Each slice assumes #1575 (hosting, the token prefix, the data interface, bundled MapLibre). The suggested order is SW1, then SW2, SW3 and SW6 in parallel, then SW4, SW5 and SW7, then SW8.

| Slice | Scope | Frames | Server work |
|---|---|---|---|
| **SW1 · Shell, header and map** | Tokens (light, dark, night) and the theme switch; header; rail and docked panel, open and close; phone sheet with three heights; overlay buttons, scale bar, readout, attribution; start view clamped to the minimum zoom (D12); below-minimum chip and outlines (D13); past-maximum chip (D14); view in the URL hash (D36). Not for navigation everywhere. Replaces `/` (keep the old page behind `--no-viewer` until this lands). | A1, A3, Q1a, B (states only drawn), D1, D2 | None beyond #1575. |
| **SW2 · Datasets panel** | Rows, grouping by layer → product → purpose, collapsed groups, search with a count, filter chips, details, hover and selection tied to outlines, `dataset=` in the URL, virtualised list. | A1, A2, A4, A5, C1–C3, I2 | Edition and update number per dataset (S-101 update applied; S-57 EDTN/UPDN). S-101 display scale range from DataCoverage. S-57 navigational purpose (from the cell name, can be in the page). |
| **SW3 · Display** | Palette, layers, basemap (muted via raster paint, OSM, none, with a fallback to none when it fails to load), page appearance, night chrome rules (D18), Mariner settings placeholder, `palette=` and `layer=` in the URL. | E1, E2, E4, Q3, Q4 | None (palettes and layers exist). |
| **SW4 · Time bar** | The bar on desktop and phone, play, Local/UTC, the browser's zone, all-past and all-future states, `t=` in the URL. | E1, E3, B8d | None for tiles (`?t=` exists). The page needs the browser's time zone: read `Intl.DateTimeFormat().resolvedOptions().timeZone` through JS interop when the circuit starts, because server rendering can't know it. |
| **SW5 · Features at a point** | Click to pick, marker, floating card with flip, phone sheet, coverage values, "Show N more", highlight from the query geometry, `pick=` in the URL. | F1, F3, F4, B5h | Decode `DatasetFeatureQuery` results with the feature catalogue: type, attribute and enumeration names, units, and order. A "minor" flag (or a list) for the meta and area types folded behind "Show more". |
| **SW6 · Use in GIS and sharing** | The panel, layer and palette pickers, Copy, per-client steps, token masking and warnings, the header link button. | G1–G3, Q6a | The page needs to know whether a token is set, and the public base URL (honour `X-Forwarded-*` the same way the TileJSON does). |
| **SW7 · Server state and live changes** | The state pill, toasts, row tags (New, Reloading), the outline flash, the red and amber banners, the Not served block and the rail badge; replaces Blazor's reconnect dialog. | H1–H3, Q7a, B states | **New:** a list of datasets that failed to load or were skipped, with a reason, kept by the serve session. Today these are console warnings only. **New:** change events with ids (added, removed, reloaded, failed) pushed to the component, not just a revision bump. A clean-shutdown signal for "Server stopped". |
| **SW8 · Edge states and the built tile set** | No datasets left, the no-coverage tag and disabled Zoom to, the static 404, the tile-set variant (panel, bounds shading, no pick, toast). | I1–I4, J1–J3 | **New:** a static 404 body for unknown paths, including a wrong token (today it's an empty 404). The tile set's TileJSON name and `s100` block reach the page through the data interface. |

## Server work, in one list

1. Per dataset: edition and update number (S-101 and S-57), and the S-101 display scale range. (SW2)
2. Decoded feature query: catalogue names for types, attributes and enumerations, with units, plus a "minor" grouping. The viewer's Object Information already does this decoding; share it. (SW5)
3. Datasets not served: failed and skipped, with reasons. (SW7)
4. Change events with dataset ids, and a clean-shutdown signal. (SW7)
5. A static 404 page for unknown paths. (SW8)
6. Whether a token is set, and the public base URL, available to the page. (SW6)
7. Browser time zone through JS interop (client side, but every time display depends on it). (SW4)

## Open questions

1. **The basemap source.** Muted OSM uses `tile.openstreetmap.org`. Its usage policy is fine for one person looking at a page, but many servers on many LANs all fetching it isn't what the policy is for. Options: keep it, with attribution and a clear "None" choice; add `--basemap-url` so people point it at their own tiles; or default to None when the server draws its own `--basemap offline`. Recommendation: keep OSM for the basics and add `--basemap-url` later. **For the user.**
2. **The name (Q1).** "s100 serve" is the recommendation; it's a branding call. **For the user.**
3. **Dusk forcing dark chrome.** The viewer darkens on Night only. Forcing dark on Dusk too avoids a white panel beside a dim chart, but someone at a bright desk can't get light chrome with Dusk. Recommendation: force it, and let Page: Light override it for Dusk (never for Night). **For the user.**
4. **Mask the token by default?** Masked is recommended; someone who started the server with a token on their own machine may find it fussy.
5. **Navigational purpose for S-101 cells.** The mock groups S-57 by the cell name's purpose digit. S-101 has no such digit; group S-101 by display scale band instead, or not at all?
6. **How many datasets before the list virtualises,** and is the 30-dataset "start collapsed" threshold right for exchange sets with mixed products?

## Not done in this round

- The legend, mariner settings, adding and removing datasets from the page, search by place or feature, measure and route (brief §7). The rail and the Display panel leave room for them.
- Keyboard and screen-reader behaviour beyond what the frames show: the rail and panels are real buttons and landmarks, and the card and the sheet need focus management in SW1 and SW5.
- No interactive prototype; every frame is static.

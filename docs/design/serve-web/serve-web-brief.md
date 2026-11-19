# Design brief: the `s100 tiles serve` web page

Status: pre-design (2026-11-18). This is input for a design pass. Put the mocks on a new
**Serve web · design pass** artboard in the SoundCharts design canvas
(<https://claude.ai/artifact/2hWbgQ9wKM1gC2gM51mSCs>), with alternatives where this brief
asks for them. Return a `HANDOFF.md` with numbered decisions (D1, D2, …) and any open
questions, as earlier passes did.

Related: the epic for this work (linked from the issue that carries this brief) and the
feasibility note [`serve-webapp-blazor-spike.md`](../serve-webapp-blazor-spike.md).

## 1. What `tiles serve` is

`s100 tiles serve` is a command in the `s100` CLI. It serves map tiles from one of two
kinds of input:

- **Datasets**, rendered on demand: S-101, S-57, S-102, S-104, S-111, S-124 and the
  other products SoundCharts reads. The input is a cell, an exchange set or a folder.
- **A built tile set**, already rendered: a `{z}/{x}/{y}` folder, PMTiles, MBTiles or
  GeoPackage.

Other software reads the tiles: QGIS, ArcGIS, MapLibre and Leaflet pages, and anything
else that speaks XYZ, TileJSON, WMTS or WMS. The server also renders a **web page** at
its root URL, so a person can see what's being served. That page is what this brief is
about.

Who opens the page:

| Who | Why they open it |
|---|---|
| A developer or GIS analyst | Before wiring the server into QGIS, ArcGIS or a web map: is it up, what does it serve, what URL do I paste? |
| Someone checking how charts render | To see how the server draws a cell, in each palette and at each time step. |
| A team on a LAN (`--host 0.0.0.0`) | To look at charts in a browser without installing SoundCharts. |
| Whoever runs a long-lived server (`--manage-datasets`, `--refresh`) | To see what's loaded now, after datasets were added or removed or files changed on disk. |

It is **not** an ECDIS, and it is not a web version of SoundCharts. The page must say
**Not for navigation**, as the `/datasets` document does.

## 2. What the server knows (the data the page can show)

The page renders inside the server, so it can show anything below directly, without a
round trip through the HTTP API.

| Data | Detail | Where it comes from today |
|---|---|---|
| Datasets | Per dataset: id, file name, product (S-101…), edition, producer code, WGS-84 bounds, a coverage polygon (`dataCoverage` or the bounds), time steps, issue time, data dynamicity (observation, forecast and so on), and the layer it's in. | `GET /datasets` |
| Layers | With `--layers-by product`, one layer per product group: "chart" (S-57/S-101/S-102), S104, S111, S124… Each has its own extent and time steps, besides the layer of all datasets. | `/layers/{layer}/…` |
| Tile set | Name, zoom range, bounds, centre, format (PNG, JPEG, WebP), tile size, whether @2x high-DPI tiles are available. | `GET /tiles.json` |
| Palettes | Day, dusk, night, plus mariner-settings styles registered by clients as `{palette}-{hash}`. | `/{palette}/tiles.json`, `/settings/{hash}` |
| Time | Time steps (UTC) for time-varying data (S-104 water levels, S-111 currents, weather). | `?t=` |
| Features at a point | The features at a point, their attributes, and coverage values (depth, water level, current) sampled there. | WMS GetFeatureInfo; in-process `DatasetFeatureQuery` |
| Legend | A legend graphic per style. | WMS GetLegendGraphic |
| Service URLs | XYZ, TileJSON, WMTS capabilities and REST, WMS capabilities. The token is in the path when there is one. | |
| Server state | Revision (it changes when datasets reload), health, the refresh interval, whether datasets can be added or removed. | `/healthz`, `S100-Revision` header |
| Changes | Datasets added or removed since start-up. Source files that changed on disk and were reloaded. | `--manage-datasets`, `--refresh` |

When the input is a **built tile set**, there are no datasets, no features to pick and
usually no palettes or times. The page then shows the tile set's metadata only. Mock
this variant.

## 3. What exists today

| | Today | Screenshot |
|---|---|---|
| Root page `/` | A full-window MapLibre map over OpenStreetMap. A small box top-left shows "s100 tiles · zoom 13–16", plus a palette menu and a time menu when they apply. Nothing else: no dataset list, no URLs, no feature info. At start-up the map frames all the bounds, often below the minimum zoom, so **no chart is visible at first** (as in the screenshot). | `screenshots/01-serve-today.png` |
| Spike `/ui` | A server-rendered prototype: a side panel with datasets (click to zoom), palette, and features at a click from the in-process query. Unstyled; the feature attributes are raw records. It proves the technology, not the design. | `screenshots/02-spike-ui-pick.jpg`, `03-spike-ui-night.jpg` |

## 4. Technology the design can rely on

- **Rendered on the server with Razor components.** Interactive parts update over a
  live connection, so the page can change when the server does (a dataset is added, a
  file is reloaded). Show those live updates in the mocks.
- **The map is MapLibre GL.** The page can draw vector overlays on it (coverage
  outlines, a pick marker, a highlight) as well as the chart tiles. The basemap is
  OpenStreetMap today. Basemap choice is open (see the questions in §8).
- **About 60 KB** for the page script, so it should open almost at once. Icons and
  fonts should stay light too: the viewer's icon set rendered as SVG, and the system
  font stack unless you argue for a web font.
- **Any browser width.** The page must work at desktop widths and on a phone. People do
  open a LAN server on a tablet.
- No accounts or sign-in. A URL token (`--token`) is the only access control.

## 5. Style: complement the SoundCharts viewer

The page should look like it belongs to SoundCharts without pretending to be the
desktop app. References: the viewer boards already on the design canvas, and the site
captures `site/src/assets/shots/H1.png` (dark) and `F1a.light.png` (light).

What to carry over:

- **Map first.** The map fills the window, with chrome around its edges: the viewer's
  left icon rail and its column of round overlay buttons on the right (pick, measure,
  route, zoom, recentre, compass).
- **Light and dark chrome**, following the browser's colour scheme. The viewer's tokens
  and its ShadUI-style surfaces, borders and type scale.
- **Chart palette.** Day, Dusk and Night chart palettes, with night chrome dark enough
  for the bridge. Should choosing the Night palette darken the chrome, as in the viewer?
- **The viewer's components**, where a web page has an equivalent:
  - Library rows and tags (product, edition, state) for datasets
  - the Object Information card for picked features
  - the Legend tab, from the legend placement design

What differs from the desktop app:

- **No title bar or window chrome.** A page header has to carry the product name, what
  is being served and the not-for-navigation notice.
- **Fewer tools.** Leave room for later ones without mocking them (see §7).
- **A browser page people share.** URLs should name the state where that's useful: the
  view, the palette, the time, a selected dataset.

## 6. Scope: "the basics"

Mock these. Each is a candidate slice of the epic.

### B1. Shell and header

- **Layout:** map, side panel (or rail plus panel), overlay buttons. How the panel
  opens and closes, and how it behaves at phone width (bottom sheet?).
- **Header:** the product name (Q1), what is served (for example "3 datasets · S-101"
  or the tile set's name), Not for navigation, and a server-state indicator (B7).

### B2. What's served

- **Dataset list.** Rows with product, edition, producer, file name, and for
  time-varying data the time range and dynamicity.
  - Group by layer when there are layers, else by product.
  - Mock 3 datasets and also about 300 (a folder of ENCs). The large case needs search
    or filter, and collapsed groups.
- **Dataset details** on selecting a row:
  - every field in §2
  - the coverage on the map
  - Zoom to
  - the dataset's own URLs, when it is a layer
- **Built tile set variant:** name, format, zoom range, bounds, high-DPI, and no
  dataset list.

### B3. Map

- **Chart tiles over a basemap.** Fix today's "nothing visible at start" problem: frame
  the data at a zoom where it draws, or show where the data is when zoomed out.
- **Coverage outlines** per dataset, with hover and selection tied to the list. Should
  they stay visible when the chart is drawn, or only when zoomed out or on hover? Keep
  them distinct from the chart's own boundaries.
- **Scale bar, zoom, compass/north-up** in the viewer's overlay style.
- Should the map show **where tiles stop** (zoom range, bounds) when you go beyond them?

### B4. Display controls

- **Palette:** day, dusk, night.
- **Time:** for time-varying data, a time control in the spirit of the viewer's timeline
  (step back and forward, play?). Times are shown in the browser's time zone, with a
  Local/UTC switch, as in the viewer. Never in the data area's local time.
- **Layer:** when there are several, which ones are drawn (all, or one layer).
- **Mariner settings** (safety contour, display category and so on) are later, not in
  the basics. Leave a place for them.

### B5. Features at a point

- **Click the map:** a pick marker and the features there, in the spirit of the
  viewer's Object Information:
  - feature type names, not codes
  - attributes with catalogue names and decoded values
  - the dataset each feature comes from
  - coverage values (depth, water level, current) at the point
- **Where the card goes:** side panel, or a floating card anchored to the click. Mock
  both, including at phone width.
- **Highlight** the selected feature on the map. Is this needed in the basics?

### B6. Use in your GIS

The developer's main question is "what URL do I paste?". Make this obvious:

- **The service URLs** (XYZ template, TileJSON, WMTS capabilities, WMS capabilities),
  per palette and per layer where they differ, each with **Copy**.
- **Short hints** per client: QGIS (WMTS or XYZ), ArcGIS (WMTS), MapLibre/Leaflet (XYZ
  or TileJSON).
- **The token:** with `--token`, the URLs contain it. Warn before sharing a URL. Should
  the token be masked in the UI?

### B7. Server state and live changes

- **State:** running, reloading (datasets changed on disk, revision bumped), lost
  connection (the server stopped or the live connection dropped), reconnecting.
- **Live changes:** a dataset added or removed (`--manage-datasets`) or reloaded shows
  in the list and on the map without a page refresh. How is the change announced?
- **Problems:** datasets that failed to load, or were skipped (protected,
  unsupported), with the reason.

### B8. Empty and edge states

- No datasets left (all removed).
- A dataset with no coverage or bounds.
- The token is wrong, so the page itself is a 404.
- Time-varying data whose time steps are all in the past or the future.

## 7. Later (leave room; don't mock in detail)

- Adding and removing datasets from the page (`--manage-datasets` already accepts
  `POST`/`DELETE /datasets`).
- Mariner settings, as a registered style.
- The legend (the server already renders legend graphics).
- Search by place or feature, measure, route.
- Opening a local file in the browser (a reason to switch to WebAssembly later).

## 8. Questions for the design pass

| # | Question |
|---|---|
| Q1 | What is the page called? "s100 serve", "SoundCharts Server", the tile set's name? The CLI is `s100`; the desktop app is SoundCharts. |
| Q2 | Side panel, or the viewer's rail with flyout panels? Which works better at phone width? |
| Q3 | Should the chrome follow the chart palette (night chart, dark chrome) or the browser's colour scheme, and what if they disagree? |
| Q4 | Basemap: OSM as now, a muted basemap that suits charts, none, or a choice? |
| Q5 | Should the page have one URL per state (view, palette, time, selection) so a link reopens what you saw? |
| Q6 | How much of B6 is on the main screen, and how much behind a "Connect" or "Use in GIS" button? |
| Q7 | How should the page show that it's live (the server pushes changes)? |

## 9. Deliverables

- Frames on the **Serve web · design pass** artboard. Desktop and phone widths for:
  - B1–B5 with three S-101 cells, light and dark chrome
  - B2 with about 300 datasets
  - B4 with time-varying data (S-104 or S-111)
  - B5 open
  - B6
  - B7 states
  - B8 states
  - the built-tile-set variant
- `HANDOFF.md`: decisions D1…, the answers to Q1–Q7, the tokens and components used,
  and anything that needs server work (new data or endpoints).

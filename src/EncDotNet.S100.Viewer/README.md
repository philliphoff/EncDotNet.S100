# EncDotNet.S100.Viewer

Cross-platform desktop viewer for IHO S-100 nautical chart data,
built on Avalonia 12 + Mapsui 5. Runs on macOS (Apple Silicon),
Windows, and Linux out of the box, with no native HDF5 dependencies
and no commercial S-52 assets.

This README describes the viewer's **user-visible features**. Internal
implementation, type names, and per-XAML wiring live in the library
READMEs linked from [For developers](#for-developers).

## Quickstart

Pre-built per-platform binaries are attached to every release at
[github.com/philliphoff/EncDotNet.S100/releases](https://github.com/philliphoff/EncDotNet.S100/releases).
The macOS DMG is Developer-ID signed and Apple-notarized; Windows and
Linux ship as architecture-tagged `tar.gz` archives. To run from
source:

```sh
dotnet run --project src/EncDotNet.S100.Viewer
```

User settings (recent files, panel layout, ECDIS overrides, vessel
geometry, …) are persisted per user; the viewer ignores or migrates
older settings shapes silently rather than refusing to start. Editable
[routes](#routes) are persisted separately in `routes.json` (see that
section).

## Linux runtime prerequisites

The published `linux-x64` / `linux-arm64` archives are self-contained —
they bundle the .NET runtime and SkiaSharp's native `libSkiaSharp.so`
(the self-contained *NoDependencies* build, so Skia itself needs no
system fontconfig/freetype; see issue #23). They do **not** bundle the
system libraries the .NET runtime and the Avalonia/X11 windowing stack
load from the OS. Desktop distributions usually already have these;
minimal/server or container images do not. On Debian/Ubuntu:

```bash
sudo apt-get update
sudo apt-get install -y \
  libicu74 \
  fontconfig fonts-dejavu-core \
  libx11-6 libice6 libsm6 libxext6 libxrender1 libxi6 libxcursor1 libxrandr2 \
  libgl1 libegl1
# Debian 12 / older Ubuntu: use the matching libicu (e.g. libicu72 / libicu70).
```

| Group | Packages | Why |
|---|---|---|
| Globalization | `libicu` | .NET requires ICU at startup; without it the app aborts with `Couldn't find a valid ICU package`. |
| Fonts | `fontconfig`, a font package (e.g. `fonts-dejavu-core`) | Text/label rendering. The bundled Skia does not *require* fontconfig to load, but without installed fonts labels render blank. |
| Windowing (X11) | `libx11-6 libice6 libsm6 libxext6 libxrender1 libxi6 libxcursor1 libxrandr2` | Avalonia creates its window via X11. |
| GPU / GL | `libgl1 libegl1` | OpenGL/EGL acceleration; the viewer falls back to software rendering if unavailable, but the loader still resolves these. |

A working display server (X11, or Wayland via XWayland) is required —
the viewer is a GUI application and has no headless mode.

## Supported products

| Standard | Subject | Encoding | Portrayal | Validation pack |
|---|---|---|---|---|
| **S-101** | Electronic Navigational Charts | ISO 8211 | Lua (S-100 Part 9A) | ✅ |
| **S-102** | Bathymetric Surfaces | HDF5 | Coverage | ✅ |
| **S-104** | Water Level Information | HDF5 | Coverage | ✅ |
| **S-111** | Surface Currents | HDF5 | Arrow symbology | ✅ |
| **S-122** | Marine Protected Areas | GML | XSLT | ✅ |
| **S-124** | Navigational Warnings | GML | XSLT | ✅ |
| **S-125** | Marine Aids to Navigation | GML | XSLT | ✅ |
| **S-127** | Marine Resources & Services | GML | XSLT | ✅ |
| **S-128** | Catalogue of Nautical Products | GML | XSLT | ✅ |
| **S-129** | Under Keel Clearance Management | GML | XSLT | ✅ |
| **S-131** | Marine Harbour Infrastructure | GML | Lua | ✅ |
| **S-201** | Aids to Navigation Information (IALA) | GML | XSLT | ✅ |
| **S-411** | Sea Ice Information | GML | XSLT | ✅ |
| **S-421** | Route Plans | GML | XSLT | ✅ |
| **S-57** *(legacy)* | Electronic Navigational Charts (Ed 3.1) | ISO 8211 | via S-101 | ✅ (delegated) |

Any combination of these can be loaded at once and rendered
time-aligned on a single interactive map.

## Loading data

The viewer accepts:

- **S-100 Exchange Sets** — point it at a directory containing a
  `CATALOG.XML` or at a `.zip` exchange-set archive, and it will
  load every dataset entry the catalogue lists. The canonical
  `CATALOG.XML` name and the `catalogue.xml` spelling used by some
  products (e.g. JCOMM/IHO S-411 sample sets) are both recognised.
- **S-57 / S-63 Exchange Sets** — point it at a directory containing a
  `CATALOG.031` (or drop the `CATALOG.031` file itself) and it will
  enumerate every base cell the catalogue lists, apply each cell's
  in-set sequential updates (`.001`, `.002`, …), and render them
  through the same S-101 translation path used for loose `.000` cells.
  The exchange-set header surfaces the same signature/integrity badge
  as S-100 sets, driven by the S-57 verifier. (Directory/`CATALOG.031`
  only — zipped S-57 sets are not supported, matching the
  directory-rooted S-57 verifier used by the `s100 validate` CLI.)
- **Catalogue-less ENC cell folders** — point it at a directory that
  holds loose base cells (`.000`) with no `CATALOG.031`/`CATALOG.XML`
  and it loads every base cell, sniffing S-57 vs S-101 per cell and
  applying each cell's sibling filesystem updates (`.001`, `.002`, …).
  Each S-101 cell's extent is read once via the cross-session
  dataset-metadata sidecar cache and unioned, so the map frames the
  folder up front (subsequent opens reuse the cached extents with no
  re-parse); S-57 cells, which have no cheap extent reader, are skipped
  from that union and framed when they load.
  A dropped folder that yields no renderable cells raises a
  notification instead of being silently ignored.
- **Loose datasets** — drop an individual `.h5` (S-102 / S-104 /
  S-111), `.gml` (any of the GML-encoded products), `.000` S-101
  ENC cell, or `.000` S-57 ENC cell onto the window. A dropped `.000`
  base cell also picks up any sibling `.001`/`.002`… updates that sit
  next to it on disk.
- **Recent files** — the **File → Recent** submenu replays previous
  loads in order; entries that no longer exist on disk are skipped.

S-57 base cells are auto-detected by inspecting the ISO 8211 header
and translated to the in-memory S-101 model so they render through
the S-101 portrayal pipeline. This is best-effort, not an S-52
implementation.

### Lazy loading of very large exchange sets

Opening a large S-57 exchange set (thousands of cells) no longer loads
every cell up front. Above a threshold (default 50 cells) the viewer
**registers** every cell — the Datasets panel lists them immediately
(dimmed) and their footprints are drawn as extent outlines — but
**defers** reading and portraying a cell's bytes until it actually
enters the viewport at a relevant scale. Cells are selected by
viewport intersection plus a usage-band → scale gate, loaded through a
bounded-concurrency gate, and evicted (LRU, beyond a retention budget)
when they leave the view. This keeps the open non-blocking and bounds
peak memory regardless of set size (issue #458). The relevant types
live under `Services/LazyLoading/`
(`ExchangeSetLazyLoadCoordinator`, `LazyCellGate`, `LruEvictionPolicy`,
`CellUsageBand`). Cells are registered in a single batch via
`DatasetsViewModel.AddRangeFromExchangeSet(IReadOnlyList<ExchangeSetCellRegistration>)`
(backed by `BulkObservableCollection`) so a set of thousands of cells
registers with one collection notification rather than one per cell.

The base-cell descriptor list a set registers from is read once from the
binary `CATALOG.031` and then cached across sessions (keyed by that
catalogue file's modified-time and size, under `caches/S57CatalogCache`).
Re-opening the same large set — the common case when it is closed and
reopened, or reopened in a later session — replays the cached descriptors
straight into lazy registration, skipping the ISO 8211 catalogue parse
entirely; regenerating the catalogue (which changes its mtime/size)
transparently invalidates the entry. The cache is a pure descriptor
store: cell bytes are always read fresh on demand, so it never serves
stale chart content (`Services/Caching/DiskS57CatalogCache`).

## The Datasets panel

Loaded data is organised in the **Datasets** activity panel as two
tabs above a pinned **inspector**:

- **Exchange sets** — a two-level tree that nests each dataset under
  the exchange set (its *source*) it came from. Each source row carries
  a member count, the set's signature/integrity badge, a per-source
  show/hide toggle (which fans out to every member dataset), and a
  close button that unloads the whole set. There is no reordering here —
  render order is a cross-source concern and lives in the Datasets tab.
  Loose, individually-loaded files do not appear on this tab.
- **Datasets** — the flat render-order list of *every* loaded dataset
  (exchange-set-backed and loose alike), with per-row visibility,
  reorder (drag, the ▲/▼ buttons, or the context menu), isolate, and
  remove, plus a bulk show-all / hide-all / reset-opacity toolbar.

The **inspector** below the tabs reflects whichever item is selected.
Selecting a dataset (in either tab) shows its **DATASET / LAYERS /
VALIDATION** sub-tabs; selecting an exchange-set source row instead
shows that set's metadata (producer, issue date, dataset count,
signature, source path). The split between the tabs and the inspector
is draggable and persisted. The panel opens on the Exchange sets tab,
unless only loose datasets are loaded, in which case it opens on the
Datasets tab.

Internally, loaded rows project the renderer-neutral `MapDataset` and
`MapDatasetSubLayer` snapshots; commands, localized labels, selection, and
exchange-set registration details remain Viewer-only. Palette, ECDIS, scale,
and mariner inputs are likewise consolidated into the current
`MapPresentationState` before rendering. This keeps active and visible state
independent while giving every loaded dataset one reusable state identity.

**Double-click a dataset row to reveal it** — the viewer ensures the
dataset is loaded and then flies the map to that dataset's extent. This
is the quickest way to locate a member of a wide-spread exchange set,
especially one that has zoomed out of scale (see *Out-of-scale extent
indicators* below).

## The map view

A Mapsui-backed map fills the centre of the window with a basemap
underlay (bundled offline Natural Earth land by default; see
**Settings → Map**). Standard pan / zoom gestures work
out of the box (mouse wheel, trackpad, touch). A **scale bar** at the
bottom of the map updates with the viewport and respects the
mariner's distance-unit choice (metres / kilometres, feet, nautical
miles).

The viewer renders directly in WGS-84 latitude/longitude internally
and projects to EPSG:3857 (Web Mercator) for display. Coverage
grids tagged with UTM-band CRSs (typical for S-102) are reprojected
on the fly via ProjNet.

### Out-of-scale extent indicators

S-101 datasets stop drawing once the map is zoomed out past their
coarsest intended display scale. When an exchange set spans far-apart
areas, framing its union extent can zoom out far enough that *every*
member disappears — leaving an empty map with nothing to aim at. To keep
those datasets discoverable, the viewer draws a thin dotted accent-colour
border around the extent of any loaded, visible dataset **exactly when
its content has zoomed out of scale**. The border marks where the
dataset is so you have a target to zoom in on (or double-click its row in
the Datasets panel to fly straight to it). Zooming back in past the
dataset's display-scale limit hides the border and restores its content.

The indicators can be turned off via **Settings → Map → Out-of-scale
dataset outlines** (on by default). They have no effect when "ignore
scale minima" is enabled, since datasets then never drop out on
zoom-out.

## Routes

The viewer can hold a collection of editable **routes** — ordered
waypoints joined by legs — that persist on the map independently of
the transient **Measure** ruler. Routes are first-class objects an
agent can also create and manipulate over the
[MCP server](#optional-mcp-server) so the *compose → inspect →
refine* loop against loaded datasets stays in one place.

- **Route Edit Mode** (route button in the top-right map toolbar):
  click on the water to append a waypoint, drag a waypoint to move
  it, click an existing leg to insert a waypoint that splits it,
  and right-click (or select + `Delete`) to remove one. `Backspace`
  drops the last waypoint. The status bar shows the latest leg's
  distance/bearing and the route total.
- **Routes panel** (route icon in the activity bar): lists every
  route (add / remove / rename / select the active one) and the
  active route's waypoints with per-leg distance, initial bearing,
  and geometry type. Each leg can be toggled between a **rhumb line**
  (loxodrome, the ECDIS default) and a **great circle** (geodesic),
  which the overlay densifies into a curved arc in Mercator space.
- **Promote a measurement**: the *From measurement* button copies the
  current Measure Mode path into a new route, then switches to Route
  Edit Mode so it can be refined.

The active route is emphasised on the map; inactive routes recede so
several routes can coexist legibly. The model mirrors the in-repo
S-421 route schema (`Route` → `RouteWaypoint` → `RouteLeg`), keeping a
future S-421 export a near-mechanical projection.

Routes are **persisted across restarts**: the collection is saved to a
`routes.json` file in the viewer data directory (alongside
`settings.json`, and re-rooted under `--data-dir` / `S100_DATA_DIR`
when one is in use). Saved routes load at startup and changes — from
either the editor or an agent over MCP — are written back (debounced),
with a final flush on exit. An `--ephemeral` run still loads any
existing routes but never writes, leaving the persisted file untouched.

## Layer stack

The **Layer Stack** panel collects every visible layer into the
S-98 display planes (**Under Radar**, **Standard**, **Over Radar**,
**Dynamic Arrows**), grouped within each plane by S-98 within-plane
priority. The basemap stays pinned to the bottom; map-overlay tools
(the measure tool, the validation-finding overlay) stay pinned to
the top.

Each row carries an inline visibility toggle. Rows include:

- Dataset rows for every loaded product, named after the dataset
  with their assigned plane in parentheses.
- Sub-layer rows when a product paints more than one layer
  (e.g. S-111 currents, which paints an arrow layer).
- Dynamic-source rows for each registered live overlay (today:
  **Own Ship**; planned: AIS targets).

Cross-product stacking is **driven by the S-98 interoperability
authority**, not by load order — when an S-101 ENC and an S-102
bathymetric grid both cover the same area, the bathymetric grid is
plumbed onto a plane that paints beneath the chart, regardless of
the order they were loaded. See the design note
[`docs/design/s98-interoperability.md`](../../docs/design/s98-interoperability.md).

## Picking and identifying features

Toggle **Pick Mode** (the cross-hair toolbar button or the
**Appearance → Pick Mode** menu item) and click any feature to open
the **Object Information** panel on the right. Each pick report
shows:

- The **location** (latitude/longitude, in degrees-decimal-minutes)
  of the click point, with a copy button that places the coordinate
  on the clipboard as signed decimal degrees — handy when capturing a
  point for debugging.
- A **hit list** of every overlapping feature — select a row to
  switch the attribute view.
- The selected feature's class, identifier, source dataset, and
  human-readable type name.
- A decoded **Attributes** section. Feature-catalogue codes (e.g.
  `CATPLE`) are shown as friendly names ("Category of pile") and
  enumerated values are shown with their FC labels. Complex
  attribute groups can be collapsed.
- An **open-in-eXaminer** link on the feature heading and on each
  attribute row that opens the matching entry on the
  [S-100 Feature Catalogue eXaminer](https://s100examiner.com/) in
  your browser, so you can read the full FC definition. The links
  appear only for product specs the eXaminer hosts; they can be turned
  off (or pointed at a mirror) under **Settings → S-100 Feature
  Catalogue eXaminer**. The **Feature Catalogues** panel offers the
  same catalogue-level link per built-in/loaded catalogue.
- A **References** section listing every `xlink:href` the feature
  carries. Clicking a row resolves the reference through the same
  processor and re-targets the pick report — particularly useful
  for S-125 AtoN status bindings and S-421 route topology.
- A **Time-series chart** when the picked feature is a fixed-station
  observation (S-104 / S-111 data-coding-format-8 stations).
- An **Egg code** diagram when the picked feature is an S-411 sea-ice
  or lake-ice area. The WMO / SIGRID-3 "egg" draws the total
  concentration on top of the oval with the partial-concentration,
  stage-of-development and form-of-ice rows beneath it (a single ice
  type folds the partial row away, and open water omits the oval).
  Thinner fourth / fifth ice classes are flanked to the right of their
  row outside the oval; snow depth appears as a caption beneath it.
  Hovering any cell shows its Feature-Catalogue meaning (e.g.
  "Grey Ice") alongside its role in the egg.

A standard one-shot pick gesture (platform-specific click modifier,
or a press-and-hold of about half a second) works outside Pick Mode
too. Coverage products (S-102, S-104, S-111) participate fully: a
click that misses every vector feature falls through to a per-cell
coverage sample reporting the underlying gridded value (depth +
uncertainty, water level + trend, current speed + direction).

Every pick is **highlighted on the map** so it stays anchored as you
pan and zoom away from the click point. The highlight has two parts: a
screen-constant **position marker** (an accent ring with a centre dot,
echoing the cursor pick) at the click location, and — when the picked
feature has resolvable geometry — an **object outline** tracing the
feature (area outline with a faint fill, curve stroke, or point ring).
The highlight tracks your accent colour and the light/dark theme, and
clears when the pick is dismissed. Because the highlight follows the
shared pick state, an MCP agent that calls `pick_features` with
`select: true` drives the same panel and highlight as a user click —
letting an automated agent show a human operator exactly what it
picked (see [docs/mcp-server.md](../../docs/mcp-server.md)).

A **Search** field above the Datasets panel finds any feature
across every loaded dataset by feature class, FC-resolved name, or
identifier. Selecting a result jumps the pick report to the
corresponding feature, even when producer datasets reuse `gml:id`s
across distinct features.

A **Vessels** activity-bar panel lists the live AIS targets,
nearest to the own ship first, in a master/detail layout. The
panel's activity-bar icon is shown only while the AIS overlay is
enabled (Settings → AIS); disabling the overlay hides the icon.
Each compact list row shows the vessel name, a ship-type pictogram
tinted by class, its navigation state, and the range and bearing
from the own ship. Selecting a row recentres the map on that
vessel while preserving the current zoom and reveals a properties
sub-pane below the list with the vessel's full detail: identity
(type, status, MMSI, call sign, IMO), motion (speed, course,
heading, rate of turn), range/bearing relative to the own ship,
voyage (destination, ETA), and dimensions (length × beam and
draught). Each detail field appears only once the
corresponding AIS report has been received. The split between the
list and the properties sub-pane is draggable and persisted.

When the simulated own-ship overlay is enabled, the own ship also
appears in the list as a **top-pinned row** (marked with a distinct
own-ship pictogram and the name *Own ship*), so it stays visible and
selectable alongside the AIS targets. While own-ship is *helming* a
live AIS target (see *Pirate mode* below), its row shows a
**"Helming &lt;target&gt;"** subtitle — or **"Waiting for &lt;target&gt;"**
while armed but before the target's first report has been adopted —
so it is always clear which vessel you are driving. From the detail
pane you can **Take the helm** of a selected AIS target, or
**Release the helm** from the own-ship row to revert to simulated
control (own-ship keeps the target's last position and course; no
snap-back). Engaging the helm hides the followed target and
auto-selects the own-ship row so the release control stays reachable.

Range and bearing are shown only when the own-ship overlay is
enabled (**Settings → simulated own-ship**); with it off, the list
still shows vessels but omits the per-row range/bearing line and
the detail pane's *Relative to own ship* section, and orders the
list nearest-first relative to the current map viewport centre
instead (so the vessels you are looking at sort to the top, and a
selected vessel — which recentres the map — floats to the top).
When the list is empty the panel shows a
placeholder that distinguishes the two reasons: the AIS overlay
being switched off (prompting the user to enable it in Settings)
versus the overlay being on but not yet populated — which is the
normal state until the AIS zoom gate opens (see *AIS zoom-gated
subscription* below).

## Display category and palette

Standard ECDIS-style controls are available from the **View** menu
and from a pair of compact pill buttons on the map toolbar:

- **Display category** — Display Base, Standard, Other Information,
  or All. Switching category propagates through the ECDIS display
  state and re-renders every vector dataset.
- **Display planes** — Under Radar / Over Radar plane toggles
  (S-100 Part 9 §11.6).
- **Text groups** — quick toggles for the three S-101 text
  viewing-group layers (Important Text / Other Text / All Other
  Chart Text).
- **Ice display mode** *(S-411 sea ice)* — a per-dataset selector,
  shown only when an S-411 dataset is loaded, that switches the
  sea-ice portrayal between **Concentration** (total concentration),
  **Stage of development**, and a **Navigational** preview
  (S-100 Part 9 §11.7). This axis is independent of the ECDIS display
  category above. The navigational option is *provisional* — a
  concentration-derived preview, not a POLARIS/RIO navigational-risk
  product — and is labelled as such in its tooltip. The selection
  persists between sessions.
- **Per-spec viewing groups** — the **ECDIS** activity-bar panel
  lists each loaded vector product's viewing groups individually so
  power users can hide or reveal specific symbol families. Labels
  come from the IHO-authored portrayal catalogues, supplemented by
  the viewer's curated label overrides where the upstream names
  are inconsistent (e.g. bare numeric IDs in S-127 and S-421). For
  S-101 the curated overrides also group the checkboxes into readable
  subsections (Depths, Aids to navigation, Alert highlights, Mariner
  selectors, …); any viewing group without a curated section falls
  into a trailing **Other** subsection. The noisy S-101 mariner
  selectors — shallow water pattern (90000), survey accuracy /
  quality (90010), and the low-accuracy marker (90011) — are hidden
  by default (including in **All**) for readability and pan/zoom
  performance, and can be switched back on from this panel.
- **Day / Dusk / Night** palettes — switch between the three S-100
  Part 9 mariner moods; coverage products (S-102, S-104, S-111)
  switch their palette in lockstep.
- **Mariner settings** — safety contour, shallow / safety / deep
  depth contours, four-shades toggle, simplified symbols, radar
  overlay, national-language preference (S-100 Part 9 §4.2).

Per-spec and global "Reset overrides" buttons clear any
user-hidden viewing groups. All overrides persist between sessions.

## Time-varying data

S-104 water levels, S-111 surface currents, and S-411 sea ice all
carry timestamps. When at least one such dataset is loaded the
viewer reveals a **global timeline** at the bottom of the map. The
timeline aggregates every time sample across the loaded datasets
into a single slider; scrubbing the slider re-renders every
participating dataset at the timestep nearest the global clock
(nearest-absolute for the time-series HDF5 products, last-known for
S-411 snapshots). When all loaded datasets share the same set of
timestamps the slider exposes discrete stops at each one; otherwise
it shows evenly-spaced guide ticks across the aggregate range.
Previous / next buttons step exactly one sample at a time and are
always available, which is handy for nudging within a dense cluster.
The panel hides automatically when no time-varying dataset is loaded.

To keep clusters selectable when data is sparse, the slider uses a
**gap-collapsing (focus + context) axis**: ranges that contain data
are laid out proportionally to their real duration, while empty gaps
between them are compressed to a thin fixed width. A contiguous
single-window dataset therefore maps linearly (unchanged), but an
exchange set whose clusters are separated by long empty stretches —
e.g. the Rotterdam NL S-111 set, which spans months but is dominated
by two multi-month gaps — has each cluster expanded so it can be
landed on with the pointer.

Under the slider a thin **data-coverage band** highlights the time
ranges that actually contain data — the merged union of every
dataset's covered window (for S-111 this is its gated forecast
window; for S-411 snapshots, from the issue time onward). It is
painted on the same gap-collapsing axis as the slider, so filled
bands line up with the slider's data clusters and the compressed
gaps show through as the faint "no data" track. Scrubbing across a
gap shows nothing. This matters most for S-111 exchange sets that
bundle many non-overlapping forecast files over the same grid: only
the file covering the current clock draws.

Some S-111 / S-104 exchange sets instead bundle several **product
variants of the same cell** — for example the Rotterdam NL set, which
publishes the same grid as separate neap / spring tidal-regime and
depth-band products (`S111-neap 0-5`, `S111-neap 0-10`, …) under one
dataset name. These variants cover the same area and the same time, so
left unchecked their current arrows stack on identical locations and
look like several timesteps drawn at once. The viewer keeps the first
loaded variant visible and loads the rest **hidden** (the Datasets-list
row dims and its eye icon shows the hidden state); re-enable any of them
from the list to compare variants.

## Validation

Every supported product ships a normative **validation rule pack**
keyed to the relevant IHO product specification. The **Validation**
activity-bar panel surfaces the findings for the selected dataset:

- Each row shows the **rule id**, **severity** (Error / Warning /
  Info), **message**, and **related feature id** (FOID for vector
  features; HDF5 group path for coverage records).
- Findings with a `GeoPosition` or `BoundingBox` are clickable —
  selecting a row zooms the map to the offending feature.
- Geographic findings are also surfaced as an overlay on the map
  (with severity-tinted markers) so the user can spot clusters
  without scrolling the list.

S-57 datasets get two passes — a pre-translation pass against the
raw S-57 record (DSID / DSPM presence, `M_COVR` coverage), then the
standard S-101 pack against the translated S-101 document. Findings
from the second pass are rebadged `S101-as-S57/<rule-id>` so the
user can tell whether a problem originated in the raw S-57 input or
in the translated projection.

## Reporting feedback

To make it easy to file consistent, well-formed bug reports, the
viewer has a built-in **Report Feedback** experience:

- A feedback button sits in the application title bar next to the
  theme toggle; there is also a **Help → Report Feedback…** menu entry.
- Both open a modal dialog that explains exactly what is collected,
  lets you type a free-form description, and previews an optional
  screenshot of the application window (falling back to a render of
  the current map view). Untick the screenshot checkbox to exclude it.
- Automatically-collected diagnostics include app/build and runtime
  info, the current viewport (centre, zoom, CRS, rotation), per-dataset
  stats (product, visibility, validation error/warning counts), and the
  most recently encountered error (type, message, stack trace) captured
  by the global exception handlers.
- An **expandable raw-data section** shows the full JSON payload, so
  you always see precisely what will be sent before submitting.
- On submit the viewer writes a local **bundle** — a zip containing
  `diagnostics.json`, your `feedback.txt`, and (if included) the
  `screenshot.png` — under your temp folder. When a screenshot is
  included it also writes a **standalone `…-screenshot.png`** next to
  the bundle and reveals *that* file in the file manager, then opens a
  **prefilled GitHub new-issue page** in your browser.
- The screenshot is copied to your clipboard so you can paste it into
  the issue with ⌘V / Ctrl+V. GitHub's clipboard paste-to-upload is
  unreliable, though — it often reports *"failed to upload image.png"*
  even for a valid PNG. If that happens, **drag the revealed
  `…-screenshot.png` file into the form's Screenshot field instead**;
  file uploads succeed where pasting fails. The prefilled form and the
  confirmation toast both spell this out.

No data leaves your machine until you choose to create the GitHub
issue, and nothing is uploaded by the viewer itself.

## About and software updates

The **Help → About EncDotNet.S100 Viewer** menu opens an About dialog
that shows the running **version** (and the build's informational
version + commit SHA + build date) and checks GitHub for a newer
release:

- The version comes from the assembly's informational version, which is
  injected from the release git tag at build time (local/dev builds
  report `0.0.0-dev`).
- When opened, the dialog checks the repository's
  [latest GitHub release](https://github.com/philliphoff/EncDotNet.S100/releases)
  and reports either **"You're up to date"** or **"Update available —
  X.Y.Z"** with release highlights, the publish date, and the asset
  size. The check is non-blocking and fails silently when offline.
- **Update now** opens the release page so you can download the build
  for your platform (the viewer does not self-update). **Release notes**
  opens the full notes on GitHub.
- **Skip** mutes *proactive* notifications (toasts) for that release only
  — the About dialog still truthfully shows the update so you can install
  it later, and you're still notified of *later* releases. The skipped
  version is remembered in your settings (`SkippedUpdateVersion`).
- After startup, a non-modal notification announces a newly available,
  unskipped release. **View release** opens GitHub, **Remind me later**
  dismisses the card until the next scheduled check, **Skip this version**
  suppresses that release but not later ones, and **Stop checking**
  disables automatic checks.
- Update checks are throttled to roughly once per day across Viewer
  launches and can be turned off entirely. Offline failures, throttled
  checks, skipped releases, and development builds remain silent at
  startup; `0.0.0-dev` shows a neutral "update checks unavailable" panel
  in About. To exercise the live check against the real GitHub API from a
  dev build, launch with `S100_UPDATE_FORCE=1` — it bypasses the dev-build
  gate for manual/agent verification only and must never be set in shipped
  builds. The Viewer links to GitHub but does not download or install an
  update itself.

### Crash recovery (next-startup reporting)

Some crashes — a native fault in the GPU/SkiaSharp stack, an
`Environment.FailFast`, a stack overflow, an out-of-memory kill, or an
external `kill -9` — terminate the process *before* the managed global
exception handlers can run, so they leave no toast and no obvious trace.
To catch these, the viewer drops a small **per-process session marker**
file (`viewer-session-{pid}.lock`, in a `crash-markers` folder next to
your settings) on startup and deletes its own marker on a clean
shutdown. On the next launch, any marker whose owning process is no
longer alive means that session terminated abnormally:

- A **"Viewer recovered from an unexpected shutdown"** notification
  appears, with a one-click **Send feedback** action.
- The crash context — every detected session's start time, PID, and
  version plus a tail of the `viewer-crash.log` — is attached to the
  feedback report automatically in a dedicated **`PreviousCrashes`**
  channel. This is deliberately separate from the single-slot
  last-error tracker: a crash is a far stronger signal than an
  exception the app recovered from, so it is captured once at startup
  and **never evicted** by a later, non-fatal runtime error before you
  send feedback. **All** detected crashes are reported, not just the
  most recent.

Because markers are **per process**, this is correct when several
viewers run **side by side** (e.g. comparing charts in two windows): a
live instance's marker is left untouched by the others, and one
instance's clean exit never erases another's crash evidence. Liveness
is decided the same way on **Windows, macOS, and Linux** — by process
id *plus the OS process start time*, so a recycled PID is never
mistaken for a still-running session.

The markers are per-user and are **not** written for `--ephemeral` or
one-shot `--exit-after-screenshot` automation runs, so agent harnesses
never pollute them or surface a stale crash.


A live "own ship" overlay sits alongside the static datasets,
publishing a single moving point through the
[dynamic-feature-source](../../docs/design/dynamic-feature-source.md)
abstraction. The position is driven by a **steerable** dead-reckoning
provider seeded at the Solent (course 090° T, 5 m/s) that you can drive
from the **Helm** panel, the `set_own_ship` MCP tool, and CLI flags.
The abstraction is shaped so a future NMEA / GPS adapter can plug in
without renderer changes. See
[`docs/design/steerable-own-ship.md`](../../docs/design/steerable-own-ship.md).

Because this position is simulated, the overlay is **disabled by
default**. Tick **Show simulated own-ship position** in the **Own
Vessel** section of the Settings panel to display it; the choice is
persisted and takes effect immediately (no restart). This gate is
authoritative for the synthetic source — when it is off, no own-ship
feature is published regardless of the layer-visibility toggle.

When the overlay is enabled and a fix is available, an interactive
launch **frames the map on the own ship** (centre + harbour-scale
zoom) instead of opening at the whole-world default. An explicit
`--bbox` / `--center` / `--zoom` on the command line still wins.

The own-ship glyph adapts to zoom:

- **Zoomed in** (when the vessel is ≥ ~6 mm on screen) — a
  true-scale 5-vertex hull outline plus a CCRP cross at the GPS
  antenna position, with a heading vector and filled-triangle
  arrowhead.
- **Zoomed out** — a coloured disc with the same heading vector
  and arrowhead.

Vessel dimensions and the four CCRP / GPS-antenna offsets
(length, beam, bow offset, port offset — matching IEC 62388 / AIS
Type 5 `dimA`/`dimB`/`dimC`/`dimD`) are editable in the **Own
Vessel** section of the Settings panel. Edits take effect
immediately. See
[`docs/design/own-ship-symbology.md`](../../docs/design/own-ship-symbology.md).

Own-ship visibility (once the simulated overlay is enabled) is
controlled by its row in the **Dynamic Arrows** plane of the Layer
Stack panel and is persisted between sessions.

### Pirate mode (follow an AIS target)

You can make own-ship **impersonate a live AIS target**: pick a vessel
and choose **Take the helm** — either from the Pick Report or from the
**Vessels** panel's detail pane. Own-ship then adopts the target's
position, course, speed, heading, and dimensions, dead-reckoning
smoothly between the target's reports. While helming, the own-ship row
in the Vessels list shows a *Helming &lt;target&gt;* label, and a
**Release the helm** button there (or turning the own-ship overlay off)
disengages. The followed target is hidden from the AIS overlay (no
double-draw); turning the own-ship overlay off disengages pirate mode
so the vessel can't disappear. The selection is persisted and re-armed
at the next launch. Switching back to the simulated source leaves
own-ship at the last adopted fix (no teleport). See
[`docs/design/steerable-own-ship.md`](../../docs/design/steerable-own-ship.md).

### Picking dynamic features

Click (or long-press, on touch) on any dynamic-source target
— own-ship, an AIS vessel pictogram — to identify it. The
**Pick Report** panel renders a *Dynamic sources* section above
the dataset hits showing the source display name, feature kind,
last-updated relative time, position, course / heading / speed
when available, and the full attribute snapshot (MMSI, vessel
name, call sign, etc. for AIS). Dataset and dynamic hits stack in
one panel so a single click reveals everything under the
crosshair. For AIS hits a **Take the helm** button engages pirate
mode (see above). The hit-test radius is 12 device pixels (matches the
AIS pictogram outer disc). See
[`docs/design/dynamic-source-pick.md`](../../docs/design/dynamic-source-pick.md).

### AIS zoom-gated subscription

The AIS overlay is **gated by viewport span** at viewer startup:
the aisstream.io subscription is not opened until the visible
viewport's lat-span and lon-span have both fallen to or below a
configurable threshold (default `50°`). On a fresh launch the
camera looks at the whole world, so the gate is closed and no
features stream — once the user zooms in the gate trips, the
subscription opens with the live viewport bounding box, and
subsequent pans / zooms keep the bbox in sync via debounced
`UpdateArea` calls. Activation is one-shot: the subscription
stays alive for the rest of the session even if the user zooms
back out. Set the threshold (or clear it for the legacy
"subscribe immediately" behaviour) under **Settings → AIS
overlay**. See
[`docs/design/ais-zoom-gated-subscription.md`](../../docs/design/ais-zoom-gated-subscription.md).

## Optional MCP server

The viewer can optionally host a Model Context Protocol server
exposing the loaded datasets to AI agents. The server is **off by
default**, bound to `127.0.0.1`, and has no authentication; the
toggle lives in the Settings panel. While on, the standard MCP
tools surface (`list_datasets`, `describe_feature`,
`sample_coverage`) is joined by a viewer-injected `render_to_image`
tool that snapshots the current map view.

See [`docs/mcp-server.md`](../../docs/mcp-server.md) for the full
tool catalogue and an agent walkthrough.

## Automation / agent control

The viewer accepts command-line flags that let an automation agent
launch it with a dataset, drive it to a known state, talk to it over
MCP, and capture diagnostics — without touching the GUI or the user's
persisted profile. The bare `viewer <datasets>` invocation continues
to work unchanged; all flags below are additive.

```sh
dotnet run --project src/EncDotNet.S100.Viewer -- \
  --ephemeral --mcp --mcp-port-file /tmp/run/mcp.url \
  --bbox 47.5,-122.5,47.7,-122.1 --palette Night \
  --screenshot /tmp/run/map.png --close-after-screenshot --exit-after-screenshot \
  --log-file /tmp/run/viewer.log -v \
  path/to/dataset.h5
```

**MCP over the CLI.** `--mcp` starts the embedded MCP server for the
run, overriding the persisted toggle. `--mcp-port <PORT>` chooses a
port (`0`, the default, picks an ephemeral one); `--mcp-bind <ADDR>`
sets the bind address (loopback recommended). Any MCP flag implies
`--mcp`. Because an ephemeral port is not known ahead of time,
`--mcp-port-file <PATH>` writes the bound endpoint URI to a file once
the server is listening (the endpoint is also echoed to stdout as
`[MCP] listening on …`). A CLI-driven MCP run never persists the
bound port back to the user's `settings.json`. A family of viewer-only
tools are injected when the server starts: `render_to_image` (read-only —
captures a PNG snapshot from a clone of the live map),
`capture_app_screenshot` (read-only — captures a PNG of the whole
application window: chart plus docks, panels, timeline, and status bar),
`set_viewport` (mutating — drives the live navigator to a bbox or
centre+zoom), `set_palette` (mutating — Day / Dusk / Night),
`set_display_category` (mutating — DisplayBase / Standard /
OtherInformation / All), `set_display_mode` (mutating — explicit
per-spec S-100 Part 9 §11.7 mode; today only S-411 sea ice, switching
`ice-concentration` / `ice-sod` / provisional `ice-navigational`),
`set_time_step` (mutating — drives the
global time clock to a sample by index or timestamp),
`set_own_ship` (mutating — positions and steers the simulated
own-ship: WGS-84 `lat`/`lon`, `cog`, `sog`, `heading`, and
`hold`/resume; works independently of the overlay's visibility so it
can pre-position before a screenshot),
`await_render_idle` (read-only — blocks
until the live map settles so a following `render_to_image` is
deterministic instead of racing the render pass),
`get_render_stats` (read-only — reports the cost of the last
on-screen paint: frame duration, interval, and per-style draw-call
breakdown, for measuring rendering performance), `open_dataset`
(mutating — loads a file or exchange set through the viewer's own
open code path and reports the resulting catalog id(s), bbox, and
load duration), `close_dataset` (mutating — unloads a dataset by
catalog id, tolerating unknown ids gracefully),
`close_all_datasets` (mutating — unloads every currently-loaded
dataset in one call), and the **route-editing family** that lets an
agent build and refine persistent routes shown live in the **Routes**
panel and route overlay: `create_route`, `list_routes` (read-only),
`get_route` (read-only), `delete_route`, `append_waypoint`,
`insert_waypoint`, `move_waypoint`, `delete_waypoint`,
`set_leg_attributes`, and `set_route_info` (all mutating; fields mirror
the in-repo S-421 model). Beyond the map, the **activity-panel tools**
let an agent drive and verify the viewer's non-render UX:
`list_panels` (read-only — snapshots the left / right / bottom dock
tabs and their `available` / `selected` / `dockOpen` / `showing`
state) and `set_panel` (mutating — shows or hides a panel by id, e.g.
`Datasets`, `LayerStack`, `PickReport`, `Timeline`, so a code / run /
verify loop can assert panel behaviour without the GUI).
`capture_app_screenshot` (read-only) completes that loop visually: it
returns a PNG of the **whole application window** — chart plus the
surrounding chrome (docks, panels, timeline, status bar) — so an agent
can *see* the non-render UX, where `render_to_image` captures only the
map surface. See `docs/mcp-server.md` for the full catalogue and the
read-only / mutating split.

**Settings isolation.** `--settings <PATH>` points the run at an
alternate settings file instead of the per-user default.
`--ephemeral` goes further: it runs against a throwaway settings file
that is **never written back**, so CLI/MCP-port write-back and
palette/category overrides cannot pollute the real profile and
parallel agent runs do not collide.

**Full data-directory redirect.** `--data-dir <PATH>` (also honoured
via the `S100_DATA_DIR` environment variable) re-roots **everything**
the viewer writes — the settings file, crash markers, and all three
disk caches (pattern-clip, portrayal-instruction, warm tile cache, the
cross-session dataset-metadata sidecar cache, and the S-57 exchange-set
catalogue descriptor cache) —
underneath one folder. Point it at an empty temp directory for a
guaranteed-fresh instance whose entire footprint can be deleted in one
`rm -rf`, or pre-seed the folder to launch with mocked-up settings or
caches. `--settings <PATH>` still overrides just the settings-file
location (caches stay under the data directory), and `--ephemeral`
still combines (the redirected settings are loaded read-only). An
explicit `S100_VECTOR_TILE_DISK_DIR` continues to win for the tile
cache. The same locations can be wiped at runtime from
**Settings → Maintenance** ("Clear caches" / "Reset all settings").

**Deterministic viewport.** `--center <LAT,LON> --zoom <LEVEL>` or
`--bbox <SOUTH,WEST,NORTH,EAST>` frame the map after datasets load.
Supplying an explicit viewport suppresses the automatic
zoom-to-extent so the framing is reproducible.

**Render state.** `--palette Day|Dusk|Night`,
`--display-category DisplayBase|Standard|OtherInformation|All`, and
`--time-step <index|ISO-8601-timestamp>` set the exact condition to
capture before a screenshot. These override the persisted values for
the run only.

**Basemap.** `--basemap None|Offline|Online` selects the basemap for
the run, overriding the persisted setting (also exposed as a selector
in **Settings → Map**; default **Offline**). **Offline** draws bundled
Natural Earth 1:10m land (public domain) with zero network access;
**Online** uses OpenStreetMap tiles with a persistent on-disk cache;
**None** shows only the ENC water background. Legacy `true`/`false`
map to Online/None. Use None or Offline for offline operation, or for
performance runs that want to measure only dataset rendering without
basemap tile fetch / raster activity (issue #295).

The **Offline** basemap repeats its Natural Earth land across the
immediately-adjacent world copies (one circumference east and west), so
a dataset kept in a continuous longitude frame across the ±180°
antimeridian — e.g. the US NWS S-411 sea-ice product (~175°E → ~225°E) —
has land beneath it instead of floating over empty water. The layer
still reports a single-world extent, so "zoom to extent" is unaffected.
The **Online** OpenStreetMap tiles are *not* world-copied (the XYZ tile
schema spans one world and Mapsui's tiling does not wrap), so such a
dataset shows no online tiles beneath the portion east of +180°; use the
Offline basemap for antimeridian datasets. Wrapping the online tile
source is a possible future enhancement.

**Own-ship.** `--own-ship-pos <LAT,LON>` places the simulated
own-ship at a WGS-84 position, `--own-ship-cog <DEG>` sets its course
over ground (degrees true `[0, 360)`), and `--own-ship-sog <MS>` sets
its speed over ground (metres per second, `>= 0`). Applied after the
datasets load and before any screenshot, independent of whether the
own-ship overlay is currently shown.

**Screenshots.** `--screenshot <PATH>` captures the map after the
render has quiesced (rather than a fixed delay).
`--close-after-screenshot` unloads all currently-loaded datasets right
after capture (retention-test mode, keeps the app running unless
`--exit-after-screenshot` is also set).
`--exit-after-screenshot` makes it a one-shot capture-then-quit;
`--full-window` captures the whole window (panels, toolbars, status
bar) instead of just the map control; `--window-size <WIDTHxHEIGHT>`
(e.g. `1280x800`) forces a fixed window size so captures are
reproducible across machines.

**Logging / diagnostics.** `--log-file <PATH>` appends structured
logs to a file, `-v` / `--verbose` raises the level to Debug, and
`--crash-log <PATH>` relocates the crash log (default: a
`viewer-crash.log` file in the system temp directory).

| Flag | Purpose |
|---|---|
| `--mcp` | Start the embedded MCP server for this run |
| `--mcp-port <PORT>` | MCP port (`0` = ephemeral); implies `--mcp` |
| `--mcp-bind <ADDR>` | MCP bind address; implies `--mcp` |
| `--mcp-port-file <PATH>` | Write the bound MCP endpoint URI here |
| `--settings <PATH>` | Use an alternate settings file |
| `--data-dir <PATH>` | Redirect all settings + caches under one folder (or `S100_DATA_DIR`) |
| `--ephemeral` | Throwaway settings, never persisted |
| `--center <LAT,LON>` | Center the map (needs `--zoom`) |
| `--zoom <LEVEL>` | Web-mercator zoom level (with `--center`) |
| `--bbox <S,W,N,E>` | Zoom to a WGS-84 bounding box |
| `--palette Day\|Dusk\|Night` | Override the palette |
| `--display-category <CAT>` | Override the ECDIS display category |
| `--time-step <idx\|ts>` | Jump to a time step (index or timestamp) |
| `--own-ship-pos <LAT,LON>` | Place the simulated own-ship at a WGS-84 position |
| `--own-ship-cog <DEG>` | Set own-ship course over ground (degrees true) |
| `--own-ship-sog <MS>` | Set own-ship speed over ground (metres/second) |
| `--screenshot <PATH>` | Capture the map after render quiesces |
| `--close-after-screenshot` | Unload all datasets after the screenshot |
| `--exit-after-screenshot` | Quit after the screenshot (one-shot) |
| `--full-window` | Capture the whole window, not just the map |
| `--window-size <WxH>` | Force a fixed window size |
| `--log-file <PATH>` | Append structured logs to a file |
| `--crash-log <PATH>` | Relocate the crash log |
| `-v`, `--verbose` | Debug-level logging |

### End-to-end agent walkthrough

1. Launch with an isolated profile and an ephemeral MCP port,
   recording where the endpoint lands:

   ```sh
   dotnet run --project src/EncDotNet.S100.Viewer -- \
     --ephemeral --mcp --mcp-port-file /tmp/run/mcp.url \
     --bbox 47.5,-122.5,47.7,-122.1 \
     path/to/dataset.h5
   ```

2. Wait for `/tmp/run/mcp.url` to appear, then read the endpoint URI
   from it (or parse the `[MCP] listening on …` stdout line).
3. Connect an MCP client to that endpoint and call `list_datasets`,
   `describe_feature`, `sample_coverage`, or `render_to_image` to
   inspect features/properties and snapshot the current view.
4. For a pure capture loop (no MCP), drop `--mcp*` and add
   `--screenshot /tmp/run/map.png --exit-after-screenshot`; the
   process loads the data, frames the requested viewport, captures
   once the render settles, and exits.

## Settings persistence

User settings are stored as JSON in the platform's per-user
application-data location. Persisted across sessions:

- Recent files.
- Panel layout (which activity-bar panels are docked where, and
  splitter positions).
- Day / Dusk / Night palette and ECDIS display category.
- Per-spec viewing-group overrides and display-plane toggles.
- Mariner depth / distance units and contour values.
- Simulated own-ship overlay enable, visibility, and vessel geometry.
- Map rendering optimizations (raster snapshot, off-thread snapshot
  prebuild, vector path cache, line simplification) — all default on
  (**Settings → Map → Rendering optimizations**).
- MCP server enable / disable.

Older settings shapes are migrated forward silently; missing values
fall back to documented defaults.

## For developers

This viewer is one consumer of the EncDotNet.S100 library suite.
The libraries do all the spec-aware work; the viewer is mainly
glue + Avalonia views. To understand or extend any of it, start
with the matching library README:

- Pipeline framework and shared types — [`EncDotNet.S100.Core`](../EncDotNet.S100.Core/README.md)
- Per-spec processors and the S-98 interop authority — [`EncDotNet.S100.Datasets.Pipelines`](../EncDotNet.S100.Datasets.Pipelines/README.md)
- Per-product readers and validation rule packs — `EncDotNet.S100.Datasets.S*/README.md`
- Vector + coverage + dynamic-feature renderers — [`EncDotNet.S100.Renderers.Mapsui`](../EncDotNet.S100.Renderers.Mapsui/README.md)
- MCP server foundation — [`EncDotNet.S100.Mcp.Tools`](../EncDotNet.S100.Mcp.Tools/README.md) and [`EncDotNet.S100.Mcp`](../EncDotNet.S100.Mcp/README.md)

Design notes for cross-cutting features:

- [Dynamic feature sources](../../docs/design/dynamic-feature-source.md)
- [Own-ship vessel symbology](../../docs/design/own-ship-symbology.md)
- [S-98 interoperability](../../docs/design/s98-interoperability.md)

Internationalization conventions, the IActivityTab / activity-bar
contract, the `IDynamicFeatureSource` abstraction, the
`ICoveragePortrayalCatalogue` / `IVectorPortrayalCatalogue`
contracts, and the resx string-resource convention are all
documented in `.github/instructions/viewer.instructions.md`.

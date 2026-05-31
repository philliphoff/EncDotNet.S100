# EncDotNet.S100.Viewer

Cross-platform desktop viewer for IHO S-100 nautical chart data,
built on Avalonia 11 + Mapsui 5. Runs on macOS (Apple Silicon),
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
older settings shapes silently rather than refusing to start.

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
  load every dataset entry the catalogue lists.
- **Loose datasets** — drop an individual `.h5` (S-102 / S-104 /
  S-111), `.gml` (any of the GML-encoded products), `.000` S-101
  ENC cell, or `.000` S-57 ENC cell onto the window.
- **Recent files** — the **File → Recent** submenu replays previous
  loads in order; entries that no longer exist on disk are skipped.

S-57 base cells are auto-detected by inspecting the ISO 8211 header
and translated to the in-memory S-101 model so they render through
the S-101 portrayal pipeline. This is best-effort, not an S-52
implementation.

## The map view

A Mapsui-backed map fills the centre of the window with an
OpenStreetMap basemap underlay. Standard pan / zoom gestures work
out of the box (mouse wheel, trackpad, touch). A **scale bar** at the
bottom of the map updates with the viewport and respects the
mariner's distance-unit choice (metres / kilometres, feet, nautical
miles).

The viewer renders directly in WGS-84 latitude/longitude internally
and projects to EPSG:3857 (Web Mercator) for display. Coverage
grids tagged with UTM-band CRSs (typical for S-102) are reprojected
on the fly via ProjNet.

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

- A **hit list** of every overlapping feature — select a row to
  switch the attribute view.
- The selected feature's class, identifier, source dataset, and
  human-readable type name.
- A decoded **Attributes** section. Feature-catalogue codes (e.g.
  `CATPLE`) are shown as friendly names ("Category of pile") and
  enumerated values are shown with their FC labels. Complex
  attribute groups can be collapsed.
- A **References** section listing every `xlink:href` the feature
  carries. Clicking a row resolves the reference through the same
  processor and re-targets the pick report — particularly useful
  for S-125 AtoN status bindings and S-421 route topology.
- A **Time-series chart** when the picked feature is a fixed-station
  observation (S-104 / S-111 data-coding-format-8 stations).

A standard one-shot pick gesture (platform-specific click modifier,
or a press-and-hold of about half a second) works outside Pick Mode
too. Coverage products (S-102, S-104, S-111) participate fully: a
click that misses every vector feature falls through to a per-cell
coverage sample reporting the underlying gridded value (depth +
uncertainty, water level + trend, current speed + direction).

A **Search** field above the Datasets panel finds any feature
across every loaded dataset by feature class, FC-resolved name, or
identifier. Selecting a result jumps the pick report to the
corresponding feature, even when producer datasets reuse `gml:id`s
across distinct features.

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
- **Per-spec viewing groups** — the **ECDIS** activity-bar panel
  lists each loaded vector product's viewing groups individually so
  power users can hide or reveal specific symbol families. Labels
  come from the IHO-authored portrayal catalogues, supplemented by
  the viewer's curated label overrides where the upstream names
  are inconsistent (e.g. bare numeric IDs in S-127 and S-421).
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
timestamps the slider exposes discrete stops at each one, with
previous / next buttons; otherwise it shows evenly-spaced guide
ticks across the aggregate range. The panel hides automatically
when no time-varying dataset is loaded.

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

## Own-ship and dynamic overlays

A live "own ship" overlay sits alongside the static datasets,
publishing a single moving point through the
[dynamic-feature-source](../../docs/design/dynamic-feature-source.md)
abstraction. Today the position is driven by a synthetic
dead-reckoning provider (Solent, course 090° T, 5 m/s). The
abstraction is shaped so a future NMEA / AIS adapter can plug in
without renderer changes.

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

Own-ship visibility is controlled by its row in the **Dynamic
Arrows** plane of the Layer Stack panel and is persisted between
sessions.

### Picking dynamic features

Click (or long-press, on touch) on any dynamic-source target
— own-ship, an AIS vessel pictogram — to identify it. The
**Pick Report** panel renders a *Dynamic sources* section above
the dataset hits showing the source display name, feature kind,
last-updated relative time, position, course / heading / speed
when available, and the full attribute snapshot (MMSI, vessel
name, call sign, etc. for AIS). Dataset and dynamic hits stack in
one panel so a single click reveals everything under the
crosshair. The hit-test radius is 12 device pixels (matches the
AIS pictogram outer disc). See
[`docs/design/dynamic-source-pick.md`](../../docs/design/dynamic-source-pick.md).

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

## Settings persistence

User settings are stored as JSON in the platform's per-user
application-data location. Persisted across sessions:

- Recent files.
- Panel layout (which activity-bar panels are docked where, and
  splitter positions).
- Day / Dusk / Night palette and ECDIS display category.
- Per-spec viewing-group overrides and display-plane toggles.
- Mariner depth / distance units and contour values.
- Own-ship visibility and vessel geometry.
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

# EncDotNet.S100.Viewer

Cross-platform desktop viewer for IHO S-100 nautical chart data,
built on Avalonia 11 + Mapsui 5.

## Installation

Each release attaches per-platform builds at
[github.com/philliphoff/EncDotNet.S100/releases](https://github.com/philliphoff/EncDotNet.S100/releases).

- **macOS (Apple Silicon)** — download
  `EncDotNet.S100.Viewer-<version>.dmg`, open it, and drag
  **S-100 Viewer** to **Applications**. The DMG, the bundled `.app`,
  and the embedded executable are all signed with a Developer ID
  certificate and notarized by Apple, so Gatekeeper will accept them
  without a manual override. A legacy
  `Viewer-osx-arm64.tar.gz` is also published during a transition
  period; expanding it produces the same `.app`.
- **Windows / Linux** — download `Viewer-<rid>.tar.gz` for your
  architecture and extract it; run `EncDotNet.S100.Viewer` from the
  extracted directory.

## Pick / Object Information panel

Toggle **Pick Mode** (cross-hair toolbar button, **View → Appearance →
Pick Mode**, or `I`) and click a feature to open the **Object
Information** panel. Each pick report shows:

- A **hit list** of every feature overlapping the click — click a row
  to switch the attribute view to that feature.
- The selected feature's class, `gml:id` (or ISO 8211 record id), and
  source dataset, kept sticky at the top of the scrollable panel.
- An **Attributes** section with FC-decoded names (e.g. `CATPLE` →
  "Category of pile") and enum-decoded values. Top-level attributes
  start expanded; complex/sub-attribute groups can be collapsed.
- A **References** section listing every `xlink:href` the feature
  carries (role + target id). Clicking a row resolves the reference
  through the same processor and re-targets the pick report —
  particularly useful for S-125 AtoN status bindings and S-421 route
  topology where the pickable geometry only carries an id pointer.

Coverage products (S-102, S-104, S-111) participate in the same
pipeline: a click that misses every vector feature falls through to
the active coverage processor's `GetCoverageInfo(lat, lon)`, which
samples the underlying grid and returns a synthesised feature
(depth + uncertainty for S-102, water level + trend for S-104,
current speed + direction for S-111).

## Feature search

The **Search** field above the Datasets panel scans every loaded
dataset for features matching a free-text query. Matching is
case-insensitive across feature type, FC-resolved type name, and
`gml:id`; results are debounced 250 ms and capped to a configurable
limit. Selecting a result opens the corresponding feature in the
pick report.

Each hit carries an **ordinal** — the feature's enumeration index
within its processor — which is used as the open key. This is a
deliberate workaround for producer datasets that reuse `gml:id`s
across distinct features (a real-world S-122 issue): the search
index correctly distinguishes the duplicates, and routing through
`IPickService.OpenFeatureAt(processor, ordinal, …)` ensures the
correct feature is opened.

## Datasets panel — layer controls

Loaded datasets appear in the **Datasets** panel on the left. Each
entry exposes an inline visibility toggle and **up / down** buttons
to reorder the paint stack; the basemap stays pinned to the bottom
and map overlays (e.g. the measure tool) stay pinned to the top.

Selecting an entry reveals a **Properties** sub-panel with two
tabs:

- **Dataset** — read-only metadata (product spec, current timestamp
  for time-varying datasets, loader status) plus a whole-dataset
  opacity slider.
- **Layers** — for multi-layer products (today S-111 currents,
  which paints a colour band and an arrow layer) this tab lists
  each sub-layer with its own visibility toggle and opacity slider.
  Single-layer products show a short empty-state message instead.

Above the list, a small toolbar offers **Show all**, **Hide all**,
**Isolate selected**, and **Reset opacity** bulk actions.

All visibility / opacity / order changes drive Mapsui's per-layer
`Enabled`, `Opacity`, and stack position directly — they apply
immediately and do not re-run the dataset pipeline.

## ECDIS Display Controls

The viewer exposes S-100 Part 9A display-category filtering,
per-spec viewing-group overrides, display-plane toggles, and
quick text-group toggles through several UI surfaces:

### Display toolbar pill

A compact pill button in the map toolbar shows the active
**display category** (Display Base, Standard, Other Information,
or All). Clicking the pill opens a flyout with radio buttons to
switch categories; the change propagates through `EcdisDisplayState`
and triggers a re-render of all vector datasets.

### Text toolbar pill

A "Text ▾" pill button next to the display pill opens a flyout
with checkboxes for the three S-101 text viewing-group layers:
**Important Text**, **Other Text**, and **All Other Chart Text**.
Unchecking a group hides the corresponding viewing groups. The
pill is disabled when no S-101 data (or another spec with text
layers) is loaded.

### ECDIS panel (activity bar)

A dedicated activity-bar entry opens the ECDIS Display Controls
panel. It lists each loaded **vector** product specification
(S-101, S-122, S-124, S-125, S-127, S-128, S-129, S-131, S-411, S-421)
with a flat checkbox list of viewing groups sourced from the spec's
portrayal catalogue. Unchecking a viewing group hides its features
from the rendered output.

#### Display planes

Below the display-category radios, the panel shows checkboxes for
the two S-100 display planes — **Under Radar** and **Over Radar**
(S-100 Part 9 §11.6). Unchecking a plane hides all drawing
instructions assigned to it across all loaded vector datasets.
Plane visibility is persisted in `settings.json`.

Coverage products (S-102, S-104, S-111) have no viewing-group
concept and are excluded from the panel.

Per-spec and global "Reset overrides" buttons clear any
user-hidden viewing groups. Override state is persisted in
`settings.json` and restored on launch.

## Time-varying datasets and the global time slider

Three product specifications carry a time dimension:

- **S-104** (Water Levels) — many samples per file (one per HDF5
  `Group_NNN`).
- **S-111** (Surface Currents) — many samples per file (same
  `Group_NNN` shape).
- **S-411** (Sea Ice) — one snapshot per file, identified by the
  dataset's *issue date* (`<S100:datasetReferenceDate>` in the
  IHO 1.2.1 sample shape; probed JCOMM/CIS attributes otherwise).

When at least one such dataset is loaded, the viewer reveals a
**global time slider** at the bottom of the map area. Scrubbing the
slider re-renders every participating dataset at the timestep nearest
to the global clock:

- S-104 / S-111: nearest absolute sample.
- S-411: most recent snapshot whose issue date is at-or-before the
  slider value (the dataset is hidden if the slider is earlier than
  any of its snapshots).

The panel is hidden automatically when no time-varying dataset is
loaded. Per-dataset prev/next time-step controls are no longer
shown — each dataset entry instead displays the timestamp it is
currently rendered at.

### Implementation notes

- `GlobalTimeService` aggregates timelines across registered
  `ITimeAwareDataset` adapters and exposes
  `MinTime`/`MaxTime`/`CurrentTime`/`AllSamples`/`IsActive`.
- `DatasetLoaderService.ReRenderAtTimeAsync(DateTime, CancellationToken)`
  is invoked when the slider moves; it applies a 100 ms trailing
  debounce and cancels in-flight renders.
- Animation (play/pause/speed) is intentionally out of scope;
  `GlobalTimeService.SetCurrentTime` is the obvious seam for a
  future timer-driven driver.

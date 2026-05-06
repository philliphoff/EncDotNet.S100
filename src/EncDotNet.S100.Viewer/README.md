# EncDotNet.S100.Viewer

Cross-platform desktop viewer for IHO S-100 nautical chart data,
built on Avalonia 11 + Mapsui 5.

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

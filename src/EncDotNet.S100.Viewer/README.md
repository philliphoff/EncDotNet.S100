# EncDotNet.S100.Viewer

Cross-platform desktop viewer for IHO S-100 nautical chart data,
built on Avalonia 11 + Mapsui 5.

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

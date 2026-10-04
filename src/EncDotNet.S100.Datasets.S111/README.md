# EncDotNet.S100.Datasets.S111

Reader and coverage portrayal pipeline for S-111 Surface Current datasets.

## Overview

This library reads S-111 datasets from HDF5 files and provides gridded and positioned-node current time series for the portrayal pipeline. Key types include:

- **`S111Dataset`** — root model containing horizontal CRS, depth, data coding format, and time-step coverages.
- **`S111DatasetReader`** — reads DCF1, DCF2, DCF3, and DCF8 datasets. DCF1 uses each `Group_NNN/timePoint` as the authoritative timestamp and transposes the time-major matrix into positioned node series; this deliberately ignores conflicting instance cadence metadata found in production IC-ENC files. `ReadAny(file, new S111ReadOptions { DeferValueReads = true })` lazily decodes DCF2 values; station-series formats are materialized eagerly.
- **`S111ReadOptions`** — opt-in read options; `DeferValueReads` enables lazy per-time-step value decoding for dcf2 (regular-grid) datasets.
- **`S111CoverageSource`** — `ICoverageSource` adapter for the coverage pipeline.
- **`S111PortrayalCatalogue`** — coverage portrayal catalogue for current arrow rendering (see *Portrayal* below).
- **`S111SpeedBandReader`** — parses the 9 surface-current speed bands and the three scale constants from the bundled `Rules/select_arrow.xsl`.
- **`SurfaceCurrentCoverage`**, **`SurfaceCurrentValue`**, **`SurfaceCurrentStation`** — surface current data models. Speeds are in knots on every path (`SurfaceCurrentValue.Speed` for gridded data, `SurfaceCurrentStation.SpeedsKnots` for station series), matching the S-111 unit of `surfaceCurrentSpeed`; nothing is converted on read.

## Portrayal

`S111PortrayalCatalogue` is driven by the bundled IHO portrayal catalogue under
`EncDotNet.S100.Specifications`' `content/S111/pc/` tree:

- **Speed bands & scale constants** — parsed from `Rules/select_arrow.xsl` on
  first use by `S111SpeedBandReader` and cached per-catalogue instance. The
  XSLT supplies the 9 `SurfaceCurrentSpeedBand{N}` ranges (mapped to colour
  tokens `SCBN{N}` and SVG symbols `SCAROW0{N}`) plus the `scaleFloor`,
  `scaleCeiling` and `scaleFactorIntermediate` variables — no values are
  hard-coded in C#.
- **Day / Dusk / Night palettes** — read from
  `ColorProfiles/colorProfile.xml`. `SwitchPalette(PaletteType)` activates
  the chosen palette; `ResolveSymbolScheme` and `ActivePalette` reflect the
  change immediately.

### No coverage colour fill

`ResolveColorScheme` returns `null`. The bundled portrayal catalogue
(`content/S111/pc/Rules/select_arrow.xsl`) defines arrow symbology only —
there is no `<coverageFill>` instruction on `surfaceCurrentSpeed`. Synthesising
a continuous heatmap from the speed-band table (as an earlier viewer prototype
did) actively obscured the underlying S-101 chart, so the colour-band sub-layer
has been removed. Per-band colour now travels with the arrow SVG itself via its
`fSCBN{N}` CSS class; `MapsuiCoverageArrowRenderer` resolves that token via the
active palette.

### Arrow rendering

Every data coding format is drawn with the catalogue's SCAROW arrows. That
covers the dcf2 regular grid and the dcf1/dcf3/dcf8 station series and
ungeorectified meshes alike: the grid path goes through
`MapsuiCoverageArrowRenderer` (viewer) / `SkiaCoverageArrowRenderer` (headless
`s100 render`), the station/mesh path through `S111DatasetProcessor`'s glyph
sub-layer. Both draw one point symbol per kept node, re-rasterised at screen
DPI so arrows stay sharp and keep a stable on-screen size at every zoom
(S-111 §9.2.4 sizes arrows in millimetres on the display). Opening an S-111
dataset requires the S-111 portrayal catalogue; there is no catalogue-less
fallback symbology.

### Spec conformance

What S-111 Edition 2.0.0, its portrayal catalogue and S-98 Edition 2.0.0
define, and how this library follows them:

| Rule | Source | Implementation |
|---|---|---|
| Arrow shape, pivot at the symbol's centre, black border | §9.2.1, Figure 9-1, Annex H Rule 1 | Bundled `SCAROW01`–`09.svg`, drawn centred on the node. |
| Direction: towards which the current flows, clockwise from true north | §9.2.2, Rule 7 | Rotation = `surfaceCurrentDirection` (clockwise) on a north-up Mercator display; turns with a rotated map. |
| 9 speed bands, one symbol and colour token `SCBN1`–`9` per band | §9.2.3, Rules 3 and 5, `select_arrow.xsl` | Bands parsed from `select_arrow.xsl`; colours from the active palette in `colorProfile.xml`. |
| Day / dusk / night colours from the portrayal catalogue | Rule 4, Annex F | `SwitchPaletteAsync`; the composite (`--layer`) path now passes the palette too. Before, it drew every arrow black. |
| Size `H = Href · min(max(Slow, S), Shigh) / Sref` (Href 10 mm, Sref 5 kn, Slow 2 kn, Shigh 13 kn), the same for every data source | §9.2.4, Eqn 9.1, Rule 6 | Catalogue scale factors 0.40 / 0.20 × S / 2.60 times the 10 mm arrow, for every DCF. Station series used to clamp the scale to 0.2–2.0 and multiply it by 0.6. |
| No arrow for null speed or direction | Rule 2 | Fill values and NaN are skipped on every path. |
| Thinning must reduce symbol density when zooming out | S-98 §13.1, S-111 §9.3.2 | Zoom-dependent thinning on every path (below). |
| Regular grids: every n-th row and column, `n = 1 + fix(Lsmax / (D · Rmax))`, seeded so the maximum vector is drawn | §9.3.2 Eqn 9.2/9.3, Annex H Rule 11, S-98 Appendix G-1.1 | `SymbolThinning.ThinGrid` with `D` the on-screen cell diagonal and `Lsmax` the largest arrow in the displayed field. |
| Irregular data and ungeorectified grids: point-by-point overlap elimination (or an implementer heuristic) | §9.3.2, §9.3.3 | `SymbolThinning.ThinPoints`. |
| No spatial interpolation when zoomed in | §9.3.1 | Arrows are drawn only at nodes; zooming in shows fewer arrows. |
| Viewing group 33060, display plane UnderRadar, drawing priority 10 | `SurfaceCurrent.xsl` | Arrows for every DCF (grid, station series, mesh) go to the `DynamicArrows` plane, priority 10. S-98 names no arrow plane (Main §9.2.1 layer 6 only ranks surface currents as on-demand data); the plane is this library's choice. |

### Implementation choices

Where the specifications leave a choice to the implementer, we chose:

- **`Rmax` = 0.5** (`SymbolThinning.DefaultMaxSymbolToSpacingRatio`) for
  grids and points: S-111's and S-98's recommended value. The drawn grid-cell
  diagonal is then at least twice the largest arrow, so arrows never overlap.
- **Seed point.** The lattice is seeded at the fastest current among the
  largest arrows in view, not at the first largest arrow in row-major order.
  The two differ only in tie-breaking: below 2 kn every arrow has the `Slow`
  size, so a row-major seed would make the drawn lattice jump whenever
  panning changed the top-left cell. S-98 G-1.1 allows adapting the seed.
- **Displayed field.** `Lsmax` and the seed are taken over the view grown by
  half the largest arrow, so arrows whose pivot is just off screen still show.
- **Point-by-point clearance.** S-111 §9.3.2 does not define "overlap". A kept
  arrow of length `L` (the longer of the pair) clears a radius of
  `L / (Rmax · √2)`: the nearest-neighbour spacing of a thinned square grid.
  Meshes and grids therefore thin to the same minimum spacing, about 1.41
  arrow lengths, and drawn arrows never overlap. Points are visited fastest
  first. Thinning runs over every node, not just the visible ones, so the
  selection changes only with zoom and stays put while panning.
- **Millimetres to pixels.** Arrow lengths are converted at 96 DPI
  (`SymbolThinning.PixelsPerMillimetre`), the scale at which Mapsui and
  Svg.Skia rasterise the millimetre-dimensioned SVGs.
- **User symbol scale.** `RenderContext.SymbolScale` (the viewer's Symbol
  Scale slider, `--symbol-scale` on the CLI) multiplies `Href`. Annex H
  Rule 12 suggests making `Href`/`Sref` user-selectable. Thinning uses the
  scaled length, so bigger arrows are also spaced further apart.
- **No extra transparency.** §9.2.6 asks for alpha 0.4 (dusk) and 0.2 (night)
  over an ENC. The catalogue's dusk and night colours already carry Annex F's
  luminance reduction, and Rule 4 says to use the catalogue colours, so no
  further alpha is applied. Doing both would make night arrows all but
  invisible.
- **Speed 0.** Band 1 is `[0.00, 0.50)` in `select_arrow.xsl`, so a node with
  speed exactly 0 is drawn with the band-1 arrow (direction as encoded).

## Lazy reads

`S111DatasetReader.ReadAny(file, new S111ReadOptions { DeferValueReads = true })`
reads only instance metadata up front and defers each time step's
`values` compound until first access. Per-step time points are derived
arithmetically from `dateTimeOfFirstRecord` + i × `timeRecordInterval`
(S-111 Edition 2.0.0 §10.2.6) instead of opening every `Group_NNN`, so
opening a dataset with hundreds of steps is fast. `SurfaceCurrentCoverage.Values`
caches the decoded array under a lock on first read.

`S111DatasetProcessor` opts into deferral for dcf2 (regular-grid) datasets
and therefore implements `IDisposable`: it retains the underlying HDF5
file/stream for the processor's lifetime so deferred reads can resolve.
Callers that create an `S111DatasetProcessor` (the viewer's
`DatasetLoaderService` does this) must dispose it to release the file.
dcf1/dcf3/dcf8 station-series datasets are materialized fully and close their
file immediately.

## Validation

A bundled rule pack
(`EncDotNet.S100.Datasets.S111.Validation.S111SurfaceCurrentRules.Default`)
evaluates a typed `S111Dataset` against the S-111 Edition 2.0.0
checklist and emits a `ValidationReport` of findings. The pack is
invoked automatically by `S111DatasetProcessor.Validate()` and is
also runnable directly:

```csharp
var report = S111SurfaceCurrentRules.Default.Run(dataset);
foreach (var finding in report.Findings)
    Console.WriteLine($"{finding.RuleId} {finding.Severity}: {finding.Message}");
```

| Rule id                  | Severity | Checks                                                                                                                  |
|--------------------------|----------|-------------------------------------------------------------------------------------------------------------------------|
| `S111-R-1.1`             | Error    | Each coverage's `Values.Length` equals `NumPointsLatitudinal × NumPointsLongitudinal`.                                  |
| `S111-R-2.1`             | Warning  | `Coverages` are strictly increasing by `TimePoint` and successive deltas vary by no more than ±10% of the median delta. |
| `S111-R-3.1`             | Warning  | `SurfaceCurrentDepth`, when set, has magnitude ≤ 1500 m (signed per `depthTypeIndex`: below the sea surface is negative). |
| `S111-R-3.2`             | Warning  | `TypeOfCurrentData`, when set, is a member of the S-111 enumerated set `{1..6}`.                                        |
| `S111-R-4.1`             | Warning  | Non-NODATA current speeds lie in the plausible range `[0, 15]` m/s; fill / NaN / ±Infinity skipped.                     |
| `S111-R-4.2`             | Error    | Non-NODATA current directions lie in the half-open range `[0, 360)` degrees true.                                       |
| `S111-PROJ-SCHEMA`       | Error    | Defensive surrogate: emitted when the underlying HDF5 dataset fails schema-level parsing inside `Validate()`.           |
| `S111-STATION-SHAPE`     | Error    | Station timestamps, speeds, directions, and declared sample count disagree.                                            |
| `S111-STATION-TIME`      | Error    | Explicit station timestamps are not strictly increasing.                                                              |
| `S111-STATION-SPEED`     | Error    | A station contains a negative or non-finite speed.                                                                     |
| `S111-STATION-DIRECTION` | Error    | A station contains a direction outside 0–360 degrees.                                                                  |

R-2.1 reuses the time-axis rule template established by S-104 (V-2),
keeping monotonicity / cadence checks consistent across the two
time-varying coverage products.

## Installation

```sh
dotnet add package EncDotNet.S100.Datasets.S111
```

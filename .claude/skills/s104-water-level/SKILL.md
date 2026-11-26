---
name: s104-water-level
description: |
  Expert knowledge of IHO S-104 Water Level Information for Surface
  Navigation Product Specification. Covers HDF5-encoded water-level
  grids per time step and station time series, data coding formats,
  waterLevelTrend codes, uncertainty, vertical datum, time-step
  semantics, station-series writing (tide predictions), the SoundCharts
  water-level portrayal (station gauges, isolines) and chart-wide
  water-level adjustment. USE FOR: work in
  src/EncDotNet.S100.Datasets.S104, tests/EncDotNet.S100.Datasets.S104.Tests,
  S104* files in src/EncDotNet.S100.Datasets.Pipelines and
  tests/EncDotNet.S100.Pipelines.Tests, S104RenderingTests,
  S104DatasetReader, S104CoverageSource, S104CoverageStyle,
  S104StationSeriesWriter, S104TidePredictions, S104TimeSeriesSampler,
  WaterLevelCoverage, WaterLevelStation, WaterLevelValue. DO NOT USE FOR:
  S-102 bathymetry (use s102-bathymetry), S-111 currents (use
  s111-surface-currents), generic HDF5 (use s100-framework).
---

# S-104 Water Level expert

## Where the code lives
- `src/EncDotNet.S100.Datasets.S104/`: reader, `S104Dataset`,
  `S104StationSeriesDataset`, `WaterLevelCoverage`/`WaterLevelStation`/
  `WaterLevelValue`, `S104TimeSeriesSampler`, `S104CoverageSource`,
  `S104CoverageStyle` + `Symbols/`, `Writing/` (`S104StationSeriesWriter`,
  `S104TidePredictions`), `Validation/S104DatasetRules`. Its README is the
  reference, especially the Portrayal section.
- `src/EncDotNet.S100.Datasets.Pipelines/`: `S104DatasetProcessor`,
  `Sampling/S104CoverageSamplers`, `S104FeatureDescriber`.
- Tests: `tests/EncDotNet.S100.Datasets.S104.Tests/` (reader, writer,
  sampler); `tests/EncDotNet.S100.Pipelines.Tests/S104*.cs` (processor,
  `S104CoverageStyleTests`); `tests/EncDotNet.S100.VisualRegression.Tests/
  S104RenderingTests.cs` (baselines). Real data: `tests/datasets/S104/`.
- Design notes: `docs/design/chart-wide-water-level-adjustment.md` (S-104
  applied to S-101/S-102 depths and the safety contour),
  `docs/design/tide-predictions/`.

## Spec anchors
- Canonical: **S-104 Edition 2.0.0**
  (`docs/S-104_Water_Level_Information_For_Surface_Navigation_Ed_2.0.0.pdf`);
  older declared editions still read.
- S-100 Part 10c (HDF5, data coding formats); S-98 Annex C (depth
  adjustment).

## Review checklist
1. HDF5 names match the spec exactly (case-sensitive): values at
   `/WaterLevel/WaterLevel.NN/Group_NNN/values`; root carries
   `horizontalCRS`, `verticalDatum`, `issueDate`, `productSpecification`;
   the `WaterLevel` group / instances carry `dataCodingFormat`,
   `methodWaterLevelProduct`, `dataDynamicity`, `waterLevelTrendThreshold`,
   `numberOfTimes`, `timeRecordInterval`, `dateTimeOfFirstRecord`, grid
   georeferencing, and station `numberOfStations`/`stationIdentification`.
2. `dataCodingFormat` (S-100 Part 10c): 1 = fixed stations, time-major;
   2 = regular grid; 8 = fixed stations, station-major. The reader handles
   1, 2 and 8 (`ReadAny` → gridded or station series) and must reject
   others with `S100DatasetNotSupportedException`. Don't assume a grid.
3. Times come from each `Group_NNN/timePoint` (uneven axes occur), not
   from `dateTimeOfFirstRecord + i × timeRecordInterval`.
4. `waterLevelHeight` is float32 metres above the dataset's
   `verticalDatum` (surface it; don't assume MLLW); fill value `-9999`.
   `waterLevelTrend` is an enum (0 unknown/fill, 1 decreasing,
   2 increasing, 3 steady); accept any integer width (IC-ENC uses Int16).
   Never recompute trend client-side; preserve producer values.
5. `uncertainty`: per-record value overrides the instance dataset; −1.0
   means none (§10.2.2.4, Table 10-3).
6. Time-step semantics: `S104DatasetProcessor.WaterLevelTimeSemantics`
   (forecast, at-or-before) via `ITimeAwareDatasetProcessor`.
7. Station-series writing: `S104StationSeriesWriter` emits DCF 8; tide
   predictions use producer `ZZ01`, astronomical-prediction
   `dataDynamicity`, and a "not for navigation" `methodWaterLevelProduct`.
   A datum without an S-100 code throws.

## Portrayal is SoundCharts' own, by design
IHO publishes no S-104 Ed 2.0.0 portrayal: the spec treats water levels
as input to ECDIS depth adjustment, not a visual layer.
`S104CoverageStyle` defines station gauges (rounded square with trend
chevron/dash/"?", plus signed height at 1:100 000 and larger) and dashed
surface isolines (0.5 m, or 0.25 m when the range is under 1 m) in one
ink colour per Day/Dusk/Night palette, labelled as SoundCharts
portrayal (`S100Products.S104Portrayal`).
- Do **not** invent an IHO-shaped PC: `src/EncDotNet.S100.Specifications/
  content/S104/pc/` stays `.gitkeep`-only until IHO publishes one.
- No colour fills, no red, green, magenta or depth blues: they compete
  with the ENC. Colour encodes neither trend nor height.
- Built with the `EncDotNet.S100.Portrayals.Application` toolkit: colours
  in `S104CoverageStyle.Palette` (`ApplicationPalette`), symbols as SVGs
  in `Symbols/` (`SymbolSet`), instructions from `StationSymbols` /
  `StationLabels` (`InstructionBuilder`). Don't add palette dictionaries
  or inline SVG strings back.
- Bump `S104CoverageStyle.StyleVersion` on any symbol or colour change so
  the tile disk cache drops old tiles; update `S104CoverageStyleTests`
  and the `S104RenderingTests` baselines.

## Pitfalls
- A file may hold several `WaterLevel.NN` instances; iterate.
- `S104TimeSeriesSampler` works on DCF 2 in geographic coordinates
  without reprojecting.

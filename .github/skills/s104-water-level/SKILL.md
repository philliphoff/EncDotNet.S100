---
name: s104-water-level
description: |
  Expert knowledge of IHO S-104 Water Level Information for Surface
  Navigation Product Specification. Covers HDF5-encoded water-level
  time-step grids, WaterLevel feature, Group_NNN time-series groups,
  data coding formats (regular grid, ungeorectified grid, time series
  at fixed stations, etc.), trend/quality flags, and tide-station
  metadata. USE FOR: S-104 datasets, water level forecasts/observations,
  tide grids, S104DatasetReader, S104CoverageSource,
  S104PortrayalCatalogue, WaterLevelCoverage, WaterLevelValue,
  time-step iteration, adding S-104 features, S-104 tests.
  DO NOT USE FOR: S-102 bathymetry (use s102-bathymetry), S-111
  currents (use s111-surface-currents), generic HDF5 (use
  s100-framework).
---

# S-104 Water Level expert

## When engaged
- Tasks touching `src/EncDotNet.S100.Datasets.S104/**` or
  `tests/EncDotNet.S100.Datasets.S104.Tests/**`
- Changes to `CoveragePipeline`/`ICoverageSource` affecting S-104
- Time-step/time-series semantics for water levels

## Spec anchors
- Canonical: **S-104 Edition 2.0.0** Water Level Information PS
- S-100 Part 8: Coverages
- S-100 Part 10c: HDF5 encoding
- S-104 Annex A: Feature Catalogue
- S-104 Annex B: Portrayal Catalogue

## Review checklist
1. HDF5 layout under `/WaterLevel/WaterLevel.NN/Group_NNN/values`
   matches spec; root group carries `dataCodingFormat`,
   `interpolationType`, `commonPointRule`, `dimension`,
   `methodCurrentsProduct`/`typeOfWaterLevelData`, `numberOfTimes`,
   `timeRecordInterval`, `dateTimeOfFirstRecord`,
   `dateTimeOfLastRecord`.
2. `dataCodingFormat` selects the spatial layout — regular grid (2),
   ungeorectified grid (3), time series at fixed stations (4),
   stationwise (1), etc. Do not assume regular grid.
3. Compound dataset values: `waterLevelHeight` (float32) and
   `waterLevelTrend` (uint8 enum: nodata=0, decreasing=1,
   increasing=2, steady=3). Preserve trend codes.
4. Times are derived from `dateTimeOfFirstRecord` +
   `timeRecordInterval` * index unless explicit time coordinates are
   present.
5. Fill values come from spec/file attributes — do not hard-code NaN.
6. Public API changes have xunit tests using `SkippableFact`.

## Known pitfalls in this repo
- The same coverage container can hold multiple `WaterLevel.NN`
  sub-groups (forecast vs. analysis); iterate.
- Trend is an enumeration, not a delta — never compute it client-side.
- Vertical datum (`verticalDatum`) varies by producer; surface it
  through the source rather than assuming MLLW.

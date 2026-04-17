---
name: s111-surface-currents
description: |
  Expert knowledge of IHO S-111 Surface Currents Product Specification.
  Covers HDF5-encoded current speed/direction time-step grids,
  SurfaceCurrent feature, Group_NNN time-series groups, data coding
  formats (regular grid, ungeorectified grid, time series at fixed
  stations, moving platform, etc.), and current portrayal (arrows,
  colour bands). USE FOR: S-111 datasets, surface currents, current
  speed and direction, S111DatasetReader, S111CoverageSource,
  S111PortrayalCatalogue, time-step iteration of currents, adding
  S-111 features, S-111 tests. DO NOT USE FOR: S-104 water levels
  (use s104-water-level), S-102 bathymetry (use s102-bathymetry),
  generic HDF5 (use s100-framework).
---

# S-111 Surface Currents expert

## When engaged
- Tasks touching `src/EncDotNet.S100.Datasets.S111/**` or
  `tests/EncDotNet.S100.Datasets.S111.Tests/**`
- Changes to `CoveragePipeline`/`ICoverageSource` affecting S-111
- Current vector portrayal (arrow rendering, colour scaling)

## Spec anchors
- Canonical: **S-111 Edition 2.0.0** Surface Currents PS
- S-100 Part 8: Coverages
- S-100 Part 10c: HDF5 encoding
- S-111 Annex A: Feature Catalogue
- S-111 Annex B: Portrayal Catalogue

## Review checklist
1. HDF5 layout under `/SurfaceCurrent/SurfaceCurrent.NN/Group_NNN/values`
   matches spec; root attributes include `dataCodingFormat`,
   `methodCurrentsProduct`, `typeOfCurrentData`, `numberOfTimes`,
   `timeRecordInterval`, `dateTimeOfFirstRecord`,
   `dateTimeOfLastRecord`, `commonPointRule`.
2. `dataCodingFormat` selects layout — regular grid (2),
   ungeorectified grid (3), time series at fixed stations (4),
   stationwise (1), moving platform (5). Do not assume regular grid.
3. Compound dataset values: `surfaceCurrentSpeed` (float32, knots) and
   `surfaceCurrentDirection` (float32, degrees true, 0–360).
4. Direction convention is **degrees true, "going to"** (oceanographic),
   not "coming from". Document/cite this when surfacing it.
5. Times derived from `dateTimeOfFirstRecord` + `timeRecordInterval`
   unless explicit time dataset is present.
6. Public API changes have xunit tests using `SkippableFact`.

## Known pitfalls in this repo
- Speed units vary by producer — spec says knots, but always honor the
  file's `surfaceCurrentSpeedUom` attribute.
- Wrap-around at 360° must be handled when interpolating directions.
- Multiple `SurfaceCurrent.NN` sub-groups can coexist (analysis vs.
  forecast); iterate.

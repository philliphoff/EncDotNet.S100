---
name: s111-surface-currents
description: |
  Expert knowledge of IHO S-111 Surface Currents Product Specification.
  Covers HDF5-encoded current speed/direction grids per time step and
  station/mesh time series, data coding formats, deferred value reads,
  station-series writing (current predictions), and the IHO arrow
  portrayal (speed bands, arrow sizing, thinning). USE FOR: work in
  src/EncDotNet.S100.Datasets.S111, tests/EncDotNet.S100.Datasets.S111.Tests,
  S111* files in src/EncDotNet.S100.Datasets.Pipelines and
  tests/EncDotNet.S100.Pipelines.Tests, coverage arrow renderers,
  S111DatasetReader, S111CoverageSource, S111PortrayalCatalogue,
  S111SpeedBandReader, S111StationSeriesWriter, S111CurrentPredictions,
  SymbolThinning. DO NOT USE FOR: S-104 water levels (use
  s104-water-level), S-102 bathymetry (use s102-bathymetry), generic
  HDF5 (use s100-framework).
---

# S-111 Surface Currents expert

## Where the code lives
- `src/EncDotNet.S100.Datasets.S111/`: `S111DatasetReader`,
  `S111ReadOptions`, `S111Dataset`, `S111StationSeriesDataset`,
  `SurfaceCurrentCoverage`/`Value`/`Station`, `S111CoverageSource`,
  `S111PortrayalCatalogue`, `S111SpeedBandReader`, `Writing/`
  (`S111StationSeriesWriter`, `S111CurrentPredictions`),
  `Validation/S111SurfaceCurrentRules`. Its README is the reference,
  including the spec conformance table for arrows.
- `src/EncDotNet.S100.Datasets.Pipelines/`: `S111DatasetProcessor`,
  `Sampling/S111CoverageSamplers`, `S111FeatureDescriber`.
- Arrow renderers: `MapsuiCoverageArrowRenderer` (viewer),
  `SkiaCoverageArrowRenderer` (headless); `SymbolThinning`.
- Portrayal catalogue: `src/EncDotNet.S100.Specifications/content/S111/pc/`
  (`Rules/select_arrow.xsl`, `ColorProfiles/colorProfile.xml`, `SCAROW` SVGs).
- Tests: `tests/EncDotNet.S100.Datasets.S111.Tests/`;
  `tests/EncDotNet.S100.Pipelines.Tests/S111*.cs`. Real data:
  `tests/datasets/S111/`.

## Spec anchors
- Canonical: **S-111 Edition 2.0.0**
  (`docs/S-111_Surface_Currents_Ed_2.0.0.pdf`), §9 portrayal, Annex H
  rules; S-98 §13.1 / Appendix G-1.1 (thinning); S-100 Part 10c (HDF5).

## Review checklist
1. HDF5 names match the spec exactly (case-sensitive): values at
   `/SurfaceCurrent/SurfaceCurrent.NN/Group_NNN/values`; root carries
   `horizontalCRS` (older `horizontalDatumValue` fallback), `issueDate`,
   `productSpecification`; the group / instances carry
   `dataCodingFormat`, `methodCurrentsProduct`, `typeOfCurrentData`,
   `surfaceCurrentDepth`, `numberOfTimes`, `timeRecordInterval`,
   `dateTimeOfFirstRecord`, grid georeferencing, station metadata.
2. `dataCodingFormat` (S-100 Part 10c): 1 = fixed stations, time-major;
   2 = regular grid; 3 = ungeorectified grid; 8 = fixed stations,
   station-major. The reader handles 1, 2, 3 and 8 and must reject
   others with `S100DatasetNotSupportedException`. Don't assume a grid.
3. `surfaceCurrentSpeed` is float32 **knots** on every path (nothing
   converted on read); `surfaceCurrentDirection` is float32 degrees true,
   **"going to"** (oceanographic, §9.2.2). Say so in XML docs where the
   value is exposed. Fill value `-9999`.
4. Times: DCF 2 uses `dateTimeOfFirstRecord + i × timeRecordInterval`
   when both are present (so deferred reads needn't open every group),
   otherwise each `Group_NNN/timePoint`; DCF 1 always uses `timePoint`,
   ignoring conflicting instance cadence (seen in IC-ENC files).
5. `S111ReadOptions.DeferValueReads` (DCF 2 only) keeps the HDF5 file
   open; `S111DatasetProcessor` is then `IDisposable`, so dispose it.
6. Portrayal is the IHO catalogue: speed bands and scale constants come
   from `select_arrow.xsl`, never C# constants; `ResolveColorScheme`
   returns `null` (arrows only, no speed-band fill over the ENC). No
   extra dusk/night alpha (catalogue colours already include it).
7. Handle 0/360° wrap-around when interpolating or aggregating directions.

## Pitfalls
- A file may hold several `SurfaceCurrent.NN` instances; iterate.
- Speed 0 is still drawn with the band-1 arrow in its encoded direction.
- Predicted datasets use producer `ZZ01`, `typeOfCurrentData` 3, and a
  "not for navigation" `methodCurrentsProduct`; depth is negative below
  the surface (`depthTypeIndex` 2).

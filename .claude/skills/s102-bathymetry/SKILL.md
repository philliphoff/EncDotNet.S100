---
name: s102-bathymetry
description: |
  Expert knowledge of IHO S-102 Bathymetric Surface Product Specification
  (HDF5-encoded depth/uncertainty grids). Covers BathymetryCoverage
  instances, depth/uncertainty compound values, the 1,000,000f fill
  value, grid georeferencing, CRS handling (often UTM), the Lua-driven
  depth-band portrayal, safety contour / coverage limit lines, relief
  shading and water-level adjustment of the surface. USE FOR: work in
  src/EncDotNet.S100.Datasets.S102, S102* files in
  src/EncDotNet.S100.Datasets.Pipelines (S102DatasetProcessor,
  S102DepthLines, S102Relief, S102DepthAdjuster, S102CoverageSampler),
  S102* tests in tests/EncDotNet.S100.Pipelines.Tests, tools/RenderS102,
  S102DatasetReader, S102CoverageSource, S102PortrayalCatalogue, depth
  shading, reviewing for S-102 spec compliance. DO NOT USE FOR: S-104
  water levels (use s104-water-level), S-111 currents (use
  s111-surface-currents), generic HDF5 access (use s100-framework).
---

# S-102 Bathymetric Surface expert

## Where the code lives
- `src/EncDotNet.S100.Datasets.S102/`: `S102DatasetReader`, `S102Dataset`,
  `BathymetryCoverage`, `BathymetryValue`, `S102CoverageSource`,
  `S102PortrayalCatalogue`, `Validation/S102DatasetRules`. Its README is
  the reference for portrayal, downsampling and validation rules.
- `src/EncDotNet.S100.Datasets.Pipelines/`: `S102DatasetProcessor`,
  `S102DepthProbe`, `Portrayal/S102DepthLines` (safety contour and
  coverage limit), `Portrayal/S102Relief`, `Portrayal/S102DepthAdjuster`,
  `Sampling/S102CoverageSampler`.
- Generic coverage pipeline: `src/EncDotNet.S100.Core/Pipelines/Coverage/`
  (`CoveragePipeline`, `ICoverageRenderer`, `ReliefShading`); rasterising
  and reprojection in `src/EncDotNet.S100.Renderers.Skia/`
  (`SkiaCoverageRenderer`, `Scene/CoverageLayerLowering`).
- Portrayal catalogue: `src/EncDotNet.S100.Specifications/content/S102/pc/`
  (`BathymetryCoverage.lua`, `ColorProfiles/colorProfile.xml`).
- Tests: no dedicated project; `tests/EncDotNet.S100.Pipelines.Tests/S102*.cs`.
  Real data: `tests/datasets/S102/`.
- CLI tool: `tools/RenderS102/`.
- Design notes: `docs/design/s102-relief/`,
  `docs/design/chart-wide-water-level-adjustment.md`.

## Spec anchors
- Canonical: **S-102 Edition 3.0.0**
  (`docs/S-102_Bathymetric_Surface_Ed_3.0.0.pdf`); Edition 2.1 files must
  still read (`S102Edition21Tests`).
- S-100 Part 8 (coverages), Part 10c (HDF5), S-102 Annex B (portrayal).

## Review checklist
1. HDF5 names match the spec exactly (case-sensitive). Values live at
   `/BathymetryCoverage/BathymetryCoverage.NN/Group_NNN/values`. Bounding
   box (`westBoundLongitude` …) and `horizontalCRS`, `verticalDatum`,
   `issueDate`, `productSpecification` are root attributes; grid
   georeferencing (`gridOriginLatitude`, `gridOriginLongitude`,
   `gridSpacingLatitudinal`, `gridSpacingLongitudinal`,
   `numPointsLatitudinal`, `numPointsLongitudinal`) is on each
   `BathymetryCoverage.NN` instance (S-100 Part 10c §10.2.1.2).
2. CRS: read `horizontalCRS`, falling back to Edition 2.1's
   `horizontalDatumValue`. Grids are often UTM (EPSG:326xx): reproject
   per pixel; never stretch a native-CRS bitmap into an EPSG:3857 bbox.
3. `depth` and `uncertainty` are float32 (`BathymetryValue` is an explicit
   8-byte layout); don't widen to float64 in the public API. Field order
   must match the file's compound type, not the spec table.
4. Fill value is **1,000,000f** (not NaN). Honour it via
   `S102CoverageSource.FillValue` → `CoverageMetadata.NoDataValue` →
   `CoverageColorScheme.NoDataColor` (palette `NODTA` token), so every
   renderer paints fill cells the same; `RenderNoDataFill = false` leaves
   them transparent over an ENC.
5. Grid origin is the **node** of the first grid point, not a cell edge:
   bitmap extents need a half-cell pad.
6. Row 0 is the **southernmost** row; bitmaps draw row 0 at the top, so
   flip with `rows - 1 - row`.
7. Downsampling keeps the shallowest depth (min) and largest uncertainty
   (max) per block, so shoals never fall between samples. Don't replace
   with mean or nearest.
8. Relief shading and the safety contour / coverage-limit lines are
   SoundCharts additions, not S-102 portrayal. Relief is a display aid,
   off by default (`S102Relief.LayerKey`). Water-level adjustment
   (`S102DepthAdjuster`) changes only the sampled copy, never the grid.

## Pitfalls
- Several `BathymetryCoverage.NN` instances (tiles) may exist; iterate.
- Call `S102PortrayalCatalogue.SwitchPaletteAsync` before
  `ResolveColorScheme` (which throws until the Lua rule is loaded).
  Out-of-order contours are clamped, not thrown.

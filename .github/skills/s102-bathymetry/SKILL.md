---
name: s102-bathymetry
description: |
  Expert knowledge of IHO S-102 Bathymetric Surface Product Specification
  (HDF5-encoded depth/uncertainty grids). Covers BathymetryCoverage
  groups, Group_F feature codes, depth/uncertainty dataset shapes,
  NODATA fill value handling (1,000,000f), tiling, georeferencing
  attributes, CRS conventions (often UTM), and the depth-shading
  portrayal palette. USE FOR: S-102 datasets, bathymetric grids,
  depth surfaces, uncertainty grids, HDF5 bathymetry,
  BathymetryCoverage, CoverageCell layout, depth shading,
  S102DatasetReader, S102CoverageSource, S102PortrayalCatalogue,
  CoverageLayerBuilder, adding S-102 features, reviewing for S-102
  spec compliance, RenderS102 tool changes. DO NOT USE FOR: S-104
  water levels (use s104-water-level), S-111 currents (use
  s111-surface-currents), generic HDF5 access (use s100-framework).
---

# S-102 Bathymetric Surface expert

## When engaged
- Tasks touching `src/EncDotNet.S100.Datasets.S102/**`,
  `tests/EncDotNet.S100.Pipelines.Tests/**` for S-102 cases, or
  `tools/RenderS102/**`
- Changes to `CoveragePipeline` or `ICoverageRenderer<T>` that affect S-102
- New depth-shading palettes or coverage portrayal tweaks
- Map projection / CRS handling for S-102 grids

## Spec anchors
- Canonical: **S-102 Edition 3.0.0** Bathymetric Surface Product Spec
- S-100 Part 8: Coverages
- S-100 Part 10c: HDF5 encoding
- S-102 Annex A: Feature Catalogue
- S-102 Annex B: Portrayal Catalogue

## Review checklist
1. HDF5 group/attribute names match the spec exactly (case-sensitive):
   `/BathymetryCoverage/BathymetryCoverage.NN/Group_NNN/values`,
   `Group_F` feature info, root attributes (`eastBoundLongitude`,
   `westBoundLongitude`, `northBoundLatitude`, `southBoundLatitude`,
   `gridOriginLatitude`, `gridOriginLongitude`, `gridSpacingLatitudinal`,
   `gridSpacingLongitudinal`, `numPointsLatitudinal`,
   `numPointsLongitudinal`, etc.).
2. `depth` and `uncertainty` are float32 compound dataset members; do
   not silently widen to float64 in the public API.
3. NODATA fill value is **1,000,000f** (not NaN). Honor it in
   `CoverageLayerBuilder` and any renderer.
4. Grid origin is the **node position of the first grid point**, not a
   cell edge; bitmap extents need a half-cell pad.
5. Grid row 0 is the **southernmost** row; bitmaps draw row 0 at top —
   flip with `rows - 1 - row` when rasterising.
6. CRS may be UTM (EPSG:326xx); per-pixel reprojection is required for
   correct overlay on EPSG:3857. Do not stretch a native-CRS bitmap
   into a Mercator bbox.
7. Public API changes have xunit tests using `SkippableFact` for real
   HDF5 data.

## Known pitfalls in this repo
- See `/memories/repo/s102-viewer-notes.md` for hard-won viewer
  alignment lessons (UTM reprojection, half-cell padding, fill value,
  row flip, ProjNet `Transform` return shape).
- Compound HDF5 datatypes via PureHDF need explicit struct mapping;
  field order must match the file, not the spec table.
- Multiple `BathymetryCoverage.NN` instances may exist (tiles); iterate
  rather than assuming a single coverage.

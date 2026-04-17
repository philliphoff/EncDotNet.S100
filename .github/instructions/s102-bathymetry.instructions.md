---
applyTo: "src/EncDotNet.S100.Datasets.S102/**,tools/RenderS102/**,tests/**/S102*.cs"
---

# S-102 editing rules

When modifying S-102 code:

- Load the `s102-bathymetry` skill before proposing non-trivial changes.
- Consult `/memories/repo/s102-viewer-notes.md` for viewer alignment
  pitfalls (UTM reprojection, half-cell padding, fill value, row flip).
- Preserve HDF5 group/attribute names exactly as specified in the
  S-102 Edition 3.0.0 product spec (case-sensitive).
- NODATA fill value is **1,000,000f** (not NaN). Honor it in
  `CoverageLayerBuilder` and all renderers.
- Grid row 0 is the southernmost row; flip with `rows - 1 - row` when
  rasterising to bitmaps.
- S-102 grids are often in UTM (EPSG:326xx); do not stretch a
  native-CRS bitmap into EPSG:3857 — reproject per pixel.
- Cite the S-102 section number in XML doc comments when adding
  spec-derived constants, attribute names, or group paths.
- Any new public API requires a matching xunit test (use `SkippableFact`
  when a real HDF5 file is required).

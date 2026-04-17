---
applyTo: "src/EncDotNet.S100.Datasets.S129/**"
---

# S-129 editing rules

When modifying S-129 code:

- Load the `s129-ukc` skill before proposing non-trivial changes.
- GML parsing uses the S-129 application schema namespaces; verify
  against the published XSD before adding attributes.
- Coordinate ordering in GML (`pos`, `posList`) follows the declared
  CRS (often lat,lon for EPSG:4326) — do not assume lon,lat.
- Vessel parameters (draft, squat, safety margin) are producer inputs;
  surface them as metadata and never recompute UKC values.
- Preserve temporal ordering of time-stepped UKC values along a route.
- S-129 consumes S-102/S-104 data — when adding integration tests, mock
  those sources rather than requiring real datasets.
- Cite the S-129 section number in XML doc comments when adding
  spec-derived constants or element names.
- Any new public API requires a matching xunit test with synthetic GML
  fixtures.

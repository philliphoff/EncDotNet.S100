---
name: s129-ukc
description: |
  Expert knowledge of IHO S-129 Under Keel Clearance Management Product
  Specification. Covers GML encoding (S-100 Part 10b), the S-129
  application schema, UKC plan/route geometries, time-step UKC values,
  and dynamic UKC representations. USE FOR: S-129 datasets, under keel
  clearance, UKC plans, GML parsing for S-129, S-129 reader/source
  code, S-129 tests. DO NOT USE FOR: S-124 nav warnings (use
  s124-nav-warnings), S-104 water levels which UKC depends on (use
  s104-water-level), generic GML (use s100-framework).
---

# S-129 Under Keel Clearance expert

## When engaged
- Tasks touching `src/EncDotNet.S100.Datasets.S129/**`
- GML schema interpretation for UKC
- Cross-spec work where S-129 consumes S-102 bathymetry and S-104
  water levels

## Spec anchors
- Canonical: **S-129 Edition 1.x** UKCM Product Specification
- S-100 Part 10b: GML encoding
- S-129 Annex A: Feature Catalogue

## Review checklist
1. GML parsing uses S-129 application schema namespaces; verify against
   the published XSD before adding attributes.
2. UKC values are time-stepped along a route; preserve the temporal
   ordering and per-segment geometry.
3. Vessel-specific parameters (draft, squat, safety margin) are inputs
   to the producer — surface them as metadata, do not recompute.
4. Coordinate ordering in GML (`pos`, `posList`) follows the CRS
   declaration (often lat,lon for EPSG:4326); do not assume lon,lat.
5. Public API changes have xunit tests with synthetic GML fixtures.

## Known pitfalls in this repo
- S-129 is closely coupled to S-102/S-104 source data; integration tests
  should mock those rather than depending on real datasets.
- UKC time series may be sparse; don't interpolate across explicit gaps
  declared by the producer.

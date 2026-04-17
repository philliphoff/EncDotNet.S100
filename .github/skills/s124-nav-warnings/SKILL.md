---
name: s124-nav-warnings
description: |
  Expert knowledge of IHO S-124 Navigational Warnings Product
  Specification. Covers GML encoding (S-100 Part 10b), the S-124
  application schema, warning types (NAVAREA, coastal, local, NAVTEX),
  in-force/cancellation lifecycle, geometric primitives in GML, and
  XSLT-based portrayal. USE FOR: S-124 datasets, navigational warnings,
  GML parsing for S-124, XSLT portrayal of warnings, vector pipeline
  changes affecting S-124, S-124 reader/source code, S-124 tests.
  DO NOT USE FOR: S-129 UKC (use s129-ukc), S-101 ENC (use s101-enc),
  generic GML (use s100-framework).
---

# S-124 Navigational Warnings expert

## When engaged
- Tasks touching `src/EncDotNet.S100.Datasets.S124/**` or
  `tests/EncDotNet.S100.Datasets.S124.Tests/**`
- GML/XSLT portrayal changes for warnings
- Vector pipeline changes (`IVectorSource`, `VectorPipeline`)
  affecting S-124

## Spec anchors
- Canonical: **S-124 Edition 1.x** Navigational Warnings PS
- S-100 Part 10b: GML encoding
- S-100 Part 9: Portrayal (XSLT path)
- S-124 Annex A: Feature/Information Catalogue
- S-124 Annex B: Portrayal Catalogue (XSLT)

## Review checklist
1. GML parsing uses the S-124 application schema namespaces; do not
   assume generic GML 3.2 attribute names without verifying.
2. Warning lifecycle: `NAVWARNPreamble` references, in-force/cancel
   relationships, and `messageSeriesIdentifier` must round-trip.
3. Geometry primitives map to the renderer's `DrawingInstruction` set
   the same way S-101 vectors do — keep the abstraction shared.
4. Portrayal goes through XSLT (not Lua); inputs to the transform
   must be the canonical GML, not a normalised intermediate form.
5. Time validity (`fixedDateRange`, `dailyDateRange`) is interpreted
   in UTC; do not coerce to local time at the source boundary.
6. Public API changes have xunit tests; small synthetic GML fixtures
   live under `tests/datasets/`.

## Known pitfalls in this repo
- S-124 GML uses XLink references between features — resolve with the
  exchange set's other files, not just the dataset under inspection.
- Cancellation messages may omit geometry; renderer must tolerate
  geometry-less features.
- XSLT processors differ in EXSLT support — keep transforms to plain
  XSLT 1.0/2.0 features that .NET's `XslCompiledTransform` /
  `XslTransform` supports.

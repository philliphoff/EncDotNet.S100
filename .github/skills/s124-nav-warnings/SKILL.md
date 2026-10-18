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
- Canonical: **S-124 Edition 2.0.0** Navigational Warnings PS
  (`docs/S-124_Navigational_Warnings_Product_Specification_Ed_2.0.0.pdf`).
  The reader and typed model also accept Edition 1.0.0.
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
5. Time validity is interpreted in UTC; do not coerce to local time at
   the source boundary. Ed 2.0.0 has `fixedDateRange` only (a date with an
   optional `timeOfDayStart` / `timeOfDayEnd`, always UTC, §4 Figure 4-5)
   and `NavwarnPreamble.cancellationDate`; there is no `dailyDateRange` or
   periodic range. Convert to the Local/UTC preference only for display.
6. A warning stays in force until its `cancellationDate`, or until a
   cancellation dataset or the latest in-force bulletin drops it
   (§9.2.1). `NavigationalWarningInfo.StatusAt` judges one warning alone
   (date ranges and cancellation date); anything that shows or uses a
   status must go through `NavigationalWarningSet.StatusAt`, which also
   applies cancellations and bulletins. A bulletin is `navwarnTypeGeneral`
   8 (its own `warningType` 9–12) with a `References` of
   `referenceCategory` 3 listing `messageSeriesIdentifier`s, or
   `noMessageOnHand` (Table 8-1, §8.1.3–8.1.4); categories 9–12 are
   `warningType` codes, not reference categories.
7. Language-bearing complex attributes (`warningInformation/information`,
   `generalArea/locationName`, `navwarnTitle`) carry one language per
   instance, repeated for each language (§4 Figure 4-5). Read every
   instance (`S124ComplexAttribute.Groups`, `S124NavwarnPart.Information`),
   not only the first.
8. Public API changes have xunit tests; small synthetic GML fixtures
   live under `tests/datasets/`.

## Known pitfalls in this repo
- S-124 GML uses XLink references between features — resolve with the
  exchange set's other files, not just the dataset under inspection.
- Cancellation messages and text-only warnings (such as weekly
  "warnings in force" bulletins) have no geometry. The renderer must
  tolerate geometry-less parts, and portrayal draws nothing for them; the
  viewer's Navigational warnings panel is where they are found.
- Object information shows a warning through
  `FeatureInfo.NavigationalWarning` (`NavigationalWarningInfo`, built by
  `S124DatasetProcessor`), not attribute rows; keep new warning fields there
  so the card, the panel and MCP stay in step.
- XSLT processors differ in EXSLT support — keep transforms to plain
  XSLT 1.0/2.0 features that .NET's `XslCompiledTransform` /
  `XslTransform` supports.

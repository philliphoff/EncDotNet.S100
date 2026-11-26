---
name: s122-marine-protected-areas
description: |
  S-122 Marine Protected Areas (Edition 2.0.0, GML) specifics for this
  repo: the S-123-mislabelled official sample and productIdentifier
  detection, the UKHO lon-lat trial dataset, and the Day-only upstream
  portrayal catalogue. USE FOR: changes under
  src/EncDotNet.S100.Datasets.S122/, tests/EncDotNet.S100.Datasets.S122.Tests/,
  src/EncDotNet.S100.Specifications/content/S122/,
  S122DatasetProcessor, or the S-122 product registration; marine
  protected, restricted or regulated areas. Load s100-gml-products too.
  DO NOT USE FOR: S-127 restricted/regulated areas (s127-marine-services),
  S-124 (s124-nav-warnings), S-125 (s125-aton), S-101 ENC (s101-enc).
---

# S-122 Marine Protected Areas

Load `s100-gml-products` too; this skill records only S-122 specifics.

## Code map
- Reader/model/catalogue: `src/EncDotNet.S100.Datasets.S122/`
  (`S122DatasetReader`, `S122Dataset`, `S122PortrayalCatalogue` — a
  plain `GmlPortrayalCatalogueBase` subclass, no adapter).
- Processor: `src/EncDotNet.S100.Datasets.Pipelines/S122DatasetProcessor.cs`.
- Product detection: `S100Products.S122` in
  `src/EncDotNet.S100.Datasets.Pipelines/Registry/S100Products.cs`.
- Bundled FC 2.0.0 + PC 2.0.0: `src/EncDotNet.S100.Specifications/content/S122/`.
- Official sample: `tests/datasets/S122/122TESTDATASET.gml`.

## Spec anchors
- Canonical: **S-122 Edition 2.0.0** (FC 2.0.0 + published XSD; verify
  attributes against both before adding them). Only the Ed 1.0.0 PDF is
  in `docs/`.
- Upstream PC: iho-ohi/S-122-Product-Specification-Development.

## S-122 quirks
1. **Detection is identifier-driven.** The official 2.0.0 sample's
   root is `<S123:DataSet xmlns:S123="http://www.iho.int/S123/gml/1.0">`
   — the S-123 namespace. `S100Products.S122` therefore also matches on
   `<productIdentifier>` containing `S-122`; keep that fallback.
   Because the reader keys on the root's own namespace, it accepts
   whatever namespace the root declares.
2. **UKHO trial dataset (GBNPI12200002045) is lon-lat** in `posList`
   while its `gml:Envelope` is correctly lat-lon. The shared
   `AssessAxisOrder` catches it; this dataset is the reason that check
   exists (#650).
3. **Comma-separated `posList` tuples** occur in real S-122 data.
4. The reader matches features and information types against an
   FC 2.0.0 type-code list in `S122DatasetReader`
   (`FeatureTypeCodes` / `InformationTypeCodes`); update those lists if
   the FC changes.
5. `TextPlacement` may attach to a referenced feature instead of
   carrying its own geometry.

## Portrayal
- `content/S122/pc/` is upstream PC 2.0.0, byte-identical; rule set is
  `main.xsl` dispatching per feature, with `Default.xsl` fallback.
- `colorProfile.xml` has only a `Day` palette, so Dusk/Night is a
  no-op for S-122.

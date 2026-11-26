---
name: s127-marine-services
description: |
  S-127 Marine Traffic Management / Marine Resources and Services
  (Edition 2.0.0, GML) specifics for this repo: namespaces, the
  namespace-driven reader, the information-type gap, and the
  case-mismatched upstream XSLT includes. USE FOR: changes under
  src/EncDotNet.S100.Datasets.S127/, tests/EncDotNet.S100.Datasets.S127.Tests/,
  src/EncDotNet.S100.Specifications/content/S127/, S127DatasetProcessor;
  pilot boarding places, routeing measures, VTS areas, signal stations,
  restricted/military/caution areas in S-127 data. Load
  s100-gml-products too. DO NOT USE FOR: S-122 protected areas
  (s122-marine-protected-areas), S-124 (s124-nav-warnings), S-129 UKC
  (s129-ukc), S-101 ENC (s101-enc).
---

# S-127 Marine Traffic Management

Load `s100-gml-products` too; this skill records only S-127 specifics.

## Code map
- Reader/model/catalogue: `src/EncDotNet.S100.Datasets.S127/`
  (`S127DatasetReader`, `S127Dataset`, typed model
  `DataModel/S127MarineServicesDataset` with `S127Authority`,
  `S127OtherFeature`, …; `S127PortrayalCatalogue`).
- Processor: `src/EncDotNet.S100.Datasets.Pipelines/S127DatasetProcessor.cs`.
- Bundled FC 2.0.0 / PC 2.0: `src/EncDotNet.S100.Specifications/content/S127/`.
- Fixtures: `tests/datasets/S127/` — all synthetic; no upstream sample
  data is published.

## Spec anchors
- Canonical: **S-127 Edition 2.0.0**; upstream
  iho-ohi/S-127-Product-Specification-Development.
- App namespace `http://www.iho.int/S127/2.0`; root `<S127:Dataset>`.
- s100gml: the reader accepts `http://www.iho.int/s100gml/5.0`
  (canonical) and `http://www.iho.int/S100/profile/s100gml/1.0`
  (legacy, as in S-124 Ed 1.0.0). When copying S-124 code, check the
  namespace constants.

## S-127 quirks and gaps
1. The reader is the namespace-driven reference: any `member` /
   `imember` child not in the GML or s100gml namespace is taken
   (`IsApplicationSchema`). Keep it that way.
2. **Only per-item `<member>` / `<imember>` wrappers are read** —
   `EnumerateChildren` looks at root children only; `<members>` /
   `<imembers>` containers are not supported yet.
3. **Information types are a known gap.** FC 2.0.0 defines information
   types (`Authority`, `ContactDetails`, `Applicability`,
   `Regulations`, `Restrictions`, `Recommendations`,
   `NauticalInformation`, `ServiceHours`, `NonStandardWorkingDay`,
   `ShipReport`, `SpatialQuality`). The reader parses `imember`, but the
   typed model's remarks still say Ed 2.0.0 has none and ignores them,
   and the fixtures encode `Authority` as a geometry-less `member`
   feature. Treat that as a deviation to fix, not as spec truth.

## Portrayal
- `content/S127/pc/` is upstream, byte-identical: `main.xsl` includes
  per-feature templates plus a `Default.xsl` fallback. Day, Dusk and
  Night palettes ship.
- Upstream `main.xsl` includes some files with the wrong case
  (`ConcentrationOfShippingHazardArea.xsl` vs
  `ConcentrationofShippingHazardArea.xsl`, `ISPSCodeSecurityLevel.xsl`
  vs `IspsCodeSecurityLevel.xsl`). `GmlPortrayalCatalogueBase`
  pre-fetches rule files into an `OrdinalIgnoreCase` dictionary that
  `AssetSourceXmlResolver` serves includes from, so this works on
  case-sensitive filesystems too — don't rename the upstream files.

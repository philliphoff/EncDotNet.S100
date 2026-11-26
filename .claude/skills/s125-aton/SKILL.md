---
name: s125-aton
description: |
  S-125 Marine Aids to Navigation (Edition 1.0.0, GML) specifics for
  this repo: the AtoN feature/information model, AtoNStatus information
  bindings, the status-only upstream XSLT catalogue, and the
  library's S-101 Lua portrayal of the aids themselves
  (S125AtonPortrayalProjection / S125AtonLuaRuleExecutor). USE FOR:
  changes under src/EncDotNet.S100.Datasets.S125/,
  tests/EncDotNet.S100.Datasets.S125.Tests/,
  src/EncDotNet.S100.Specifications/content/S125/, S125DatasetProcessor;
  buoys, beacons, lights, AIS AtoN in S-125 data. Load
  s100-gml-products too (and s101-enc when touching the S-101 rule
  mapping). DO NOT USE FOR: S-101 ENC AtoN feature classes on their own
  (s101-enc), S-201 AtoN information (s201-aton-information), S-124
  (s124-nav-warnings).
---

# S-125 Marine Aids to Navigation

Load `s100-gml-products` too; this skill records only S-125 specifics.

## Code map
- Reader/model/FeatureXML/catalogue: `src/EncDotNet.S100.Datasets.S125/`
  (`S125DatasetReader`, `S125Dataset`, `S125FeatureXmlSource`,
  `S125PortrayalCatalogue` with `FetchRuleFallbackXmlResolver`).
- S-101 AtoN portrayal: `S125AtonPortrayalProjection`,
  `S125AtonLuaDataProvider`, `S125AtonLuaRuleExecutor` (same project).
- Processor: `src/EncDotNet.S100.Datasets.Pipelines/S125DatasetProcessor.cs`.
- Bundled FC/PC 1.0.0: `src/EncDotNet.S100.Specifications/content/S125/`.
- Fixtures: `tests/datasets/S125/` (`derive-s125-from-enc.cs` builds
  the ENC-derived ones).
- The project README's portrayal section explains the S-101 choice.

## Spec anchors
- Canonical: **S-125 Edition 1.0.0** (Dec 2025, release `ed1.0.0` of
  iho-ohi/S-125-Product-Specification-Development). Annex A FC,
  Annex D PC.
- App namespace `http://www.iho.int/S125/1.0`; root `<S125:Dataset>`.

## Model (FC 1.0.0)
- Buoys (`LateralBuoy`, `CardinalBuoy`, …), beacons, lights
  (`LightAllAround`, `LightSectored`, …), structures (`Landmark`,
  `Daymark`, `Topmark`, …), signals/equipment, AIS
  (`Physical`/`Synthetic`/`VirtualAISAidToNavigation`), lines/areas,
  meta features. The reader's `FeatureTypeCodes` list in
  `S125DatasetReader` is authoritative.
- Containers `AtonAggregation`, `AtonAssociation` and
  `AtonStatusIndication` are geometry-less.
- Information types: `AtonStatusInformation`, `SpatialQuality`.

## S-125 quirks
1. **Role-name casing.** Datasets bind status with an empty
   `<AtoNStatus xlink:href="#…"/>` (capital N); the FC info-type code is
   `AtonStatusInformation` (lowercase n). Round-trip names as written;
   never normalise.
2. The reader classifies a child with an `xlink:href` (or
   `informationRef`) and no content as an information reference, not a
   simple attribute.
3. `S125FeatureXmlSource` emits each reference as
   `<RoleName informationRef="…"/>` under the feature and writes
   `<Dataset><InformationTypes>`; upstream `AtoNStatusInformation.xsl`
   walks `/Dataset/InformationTypes/AtoNStatusInformation[@id=$ref]`.

## Portrayal: two stages merged into one layer
1. **S-125 XSLT PC (upstream, unchanged).** `main.xsl` includes only
   `AtoNStatusIndication.xsl`, `AtoNStatusInformation.xsl`,
   `DataCoverage.xsl`; symbols are the CHNG* change symbols and
   `QUESMRK1`. Day palette only. By design S-125 overlays an ENC's own
   AtoN symbols; it defines no aid symbology.
2. **S-101 Lua rules for the aids (library choice).** When a Lua
   engine and the S-101 PC/FC are available, `S125DatasetProcessor`
   runs `S125AtonLuaRuleExecutor` as supplemental portrayal through the
   vector pipeline, with S-101 palette and ECDIS display state. Without
   them only stage 1 renders, so a buoy/light dataset draws almost
   nothing — not a parser bug.
   `S125AtonPortrayalProjection` maps S-125 onto S-101: lifts `Topmark`
   features onto their structure (via `StructureEquipment`, else a
   co-located buoy/beacon), renames `SyntheticAISAidToNavigation` →
   `PhysicalAISAidToNavigation`, renames attributes
   (`maximalPermittedDraught`, `orientation`, draft `objectName` /
   `MMSICode`), and excludes status, coverage, aggregation and meta
   types. Extend the projection, not the S-101 rules.

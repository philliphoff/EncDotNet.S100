---
name: s201-aton-information
description: |
  IALA S-201 Aids to Navigation Information (Edition 2.0.0, May 2025;
  GML on the S-100 GML 5.0 profile): the authority-to-authority AtoN
  exchange product — 62 feature types, 4 information types, three
  application-namespace forms, two container shapes, information vs
  feature references, the Structure/Equipment `parent`/`child` roles,
  the typed AtoN inventory, and XSLT portrayal via
  `main_PaperChart.xsl`. Load `s100-gml-products` with it.
  USE FOR: `src/EncDotNet.S100.Datasets.S201/**`,
  `tests/EncDotNet.S100.Datasets.S201.Tests/**`, `S201DatasetProcessor`,
  `content/S201/**`, `tests/datasets/S201/**`; AtoN-authority data.
  DO NOT USE FOR: ECDIS-facing AtoN portrayal (s125-aton — that is
  **S-125, not S-201**), S-101 AtoN feature classes (s101-enc), S-124
  (s124-nav-warnings), rules shared by all GML products
  (s100-gml-products).
---

# S-201 Aids to Navigation Information

Load `s100-gml-products` too; this skill records only S-201 specifics.

## S-201 vs S-125
S-125 is the ECDIS-facing AtoN product; S-201 is the IALA
authority-to-authority exchange product with a richer FC (operational
metadata, equipment lifecycle, AIS-AtoN). Same physical objects, but
separate FC, XSD, PC and dataset projects. Share nothing beyond the
framework abstractions.

## Code map
- `src/EncDotNet.S100.Datasets.S201/` — `S201DatasetReader`,
  `S201Dataset` (`ResolveReferencedFeatures` /
  `ResolveReferencedInformationTypes`), `S201FeatureXmlSource`,
  `S201PortrayalCatalogue`, `S201ListedValueIndex`, typed
  `DataModel/S201AtonInventory` + `S201AtonObject` hierarchy,
  `Validation/S201AtonRules`.
- Processor: `src/EncDotNet.S100.Datasets.Pipelines/S201DatasetProcessor.cs`.
- Catalogue: `content/S201/` from `IALA-IGO/S-201_AtoN-Information`
  (commit and renames in `content/S201/README.md`). The upstream zip's
  `7. S-201 Portrayal Catalogue - Annex D/` wrapper folder is stripped;
  otherwise byte-identical. PC internal `version="1.0"` is the PC
  version, not the spec edition. Day-only palette.
- Spec anchors: Annex A DCEG, Annex B XSD, Annex C2 FC
  (`versionNumber=2.0.0`), Annex D PC.

## Feature catalogue (2.0.0)
- 62 feature types: buoys (`LateralBuoy`, `CardinalBuoy`, …), beacons,
  lights (`Light`, `LightSectored`, `LightVessel`, …), structures
  (`Landmark`, `Daymark`, `Pile`, `WindTurbine`, …), equipment
  (`FogSignal`, `RadarTransponderBeacon`, `PowerSource`, …), AIS
  (`Physical`/`Synthetic`/`VirtualAISAidToNavigation`), lines/areas
  (`NavigationLine`, `RecommendedTrack`, `DataCoverage`, …),
  aggregations (`AtonAggregation`, `AtonAssociation`). Abstract
  supertypes `AidsToNavigation`, `StructureObject`, `Equipment`.
  No allow-list of the 62 — recognition is namespace-driven.
- Exactly 4 information types, a closed set in
  `S201DatasetReader.InformationTypeCodes`: `AtoNFixingMethod`,
  `AtonStatusInformation`, `PositioningInformation`, `SpatialQuality`.

## Namespaces and dataset shapes — tolerate all
- Application namespace in three forms:
  `http://www.iho.int/S-201/gml/cs0/1.0` (Annex B XSD),
  `http://www.iho.int/S-201/gml/cs0/2.0` (current published data: NLB,
  TH, CIL, IALA sample 0227), and legacy `http://www.iho.int/201/gml/1.0`
  (no `S-`, no `/cs0/`).
- Two container shapes:
  1. XSD shape: root `<Dataset>`, `<member>` / `<imember>` wrappers.
  2. Real-world shape: root `<DataSet>` (camelCase; casing varies by
     encoder), one unified `<members>` holding features and info types
     as direct children, feature elements usually in the **empty
     default namespace** (counts as application schema).
- Real datasets do not match the XSD shape; never "fix" the reader to
  accept only the XSD form.

## References
- **Information references** — xlink to one of the 4 info types
  (`AtoNStatus` → `AtonStatusInformation`, `Positioning` →
  `PositioningInformation`, `AtonFixingMethodUsed` → `AtoNFixingMethod`,
  `SpatialAccuracy` → `SpatialQuality`).
- **Feature references** — everything else. FC 2.0.0's
  `StructureEquipment` association uses roles `parent` / `child`
  (`xlink:title="StructureEquipment"`); other aggregations use `peer`.
  Older drafts (and the synthetic fixtures) use `theParentFeature` /
  `theSubordinateFeature`; the typed model accepts both. Preserve role
  names verbatim; strip the leading `#` from targets.
- The reader classifies after parsing against known info-type ids;
  unknown targets default to feature references. FeatureXML emits
  `<Role informationRef="…"/>` vs `<Role featureRef="…"/>` so XSLT can
  tell them apart.

## Portrayal
XSLT; top-level `main_PaperChart.xsl` (discovered via
`ruleType="TopLevelTemplate"`), sub-templates `*_PAPER_CHART.xsl` via
`xsl:include`.

## Pitfalls
- Real datasets carry **listed-value labels**
  (`Starboard-Hand Lateral Mark`), but the upstream XSLT predicates
  match **numeric codes** (`buoyShape=1`). Keep the label in the
  dataset model; `S201FeatureXmlSource` normalises label → code via
  `S201ListedValueIndex` (FC-derived, unknowns pass through) only when
  writing FeatureXML. Without it, features fall back to generic
  symbols such as `BOYGEN03`.
- Real-dataset tests are gated by `S201_REAL_DATASET_PATH`
  (`S201RealDatasetTests`, `Assert.SkipUnless`). Never commit samples
  from `s-201tool.gla-rad.org` (unclear licence); fixtures are
  hand-authored from FC + XSD.

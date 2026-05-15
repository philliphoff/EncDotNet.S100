---
name: s201-aton-information
description: |
  Expert knowledge of IALA S-201 Aids to Navigation Information
  Product Specification (Edition 2.0.0, May 2025; aligned with
  S-100 Ed 5.2.0). Covers GML encoding (S-100 Part 10b) on the
  S-100 GML 5.0 profile, the S-201 application schema (62 feature
  types — buoys, beacons, lights, AIS aids, structures,
  equipment, aggregations), the 4 information types
  (AtoNFixingMethod, AtonStatusInformation, PositioningInformation,
  SpatialQuality), xlink-based information bindings, the
  equipment-on-structure `parent`/`child` xlink relationship, and
  the XSLT-based portrayal pipeline (`main_PaperChart.xsl`).
  USE FOR: S-201 datasets, AtoN-authority data exchange, GML
  parsing for S-201, XSLT portrayal of S-201, vector pipeline
  changes affecting S-201, S-201 reader/source code, S-201 tests.
  DO NOT USE FOR: ECDIS-facing AtoN portrayal (use s125-aton — the
  ECDIS-facing AtoN spec is **S-125, not S-201**), S-101 ENC AtoN
  feature classes (use s101-enc), S-124 navigational warnings
  (use s124-nav-warnings), generic GML (use s100-framework).
---

# S-201 Aids to Navigation Information expert

## When engaged
- Tasks touching `src/EncDotNet.S100.Datasets.S201/**` or
  `tests/EncDotNet.S100.Datasets.S201.Tests/**`
- GML / XSLT portrayal changes for S-201 (AtoN-authority exchange)
- Bundled S-201 spec asset updates under
  `src/EncDotNet.S100.Specifications/content/S201/`
- Pipeline / viewer wiring changes that touch `S201DatasetProcessor`

## S-201 vs S-125 (mandatory disambiguation)
- **S-125** is the **ECDIS-facing** AtoN portrayal product. It is the
  right answer for "show AtoN on an ECDIS chart". Already supported
  in this repo via `EncDotNet.S100.Datasets.S125`.
- **S-201** is the **authority-to-authority exchange** product. Same
  physical objects (buoys, beacons, lights, AIS-AtoN) but a richer
  feature catalogue (operational metadata, equipment lifecycle,
  AIS-AtoN routing), distinct schema, distinct PC, distinct intended
  audience (AtoN authorities, not ECDIS).
- **They are independent.** Separate FC, separate XSD, separate PC,
  separate dataset projects. Do not share AtoN code between S-125
  and S-201 beyond the shared GML pipeline abstractions.

## Spec anchors
- Canonical: **S-201 Edition 2.0.0**, May 2025
  (`IALA-IGO/S-201_AtoN-Information` upstream; bundled commit SHA
  recorded in `content/S201/README.md`)
- S-100 Part 10b: GML encoding
- S-100 Part 9: Portrayal (XSLT path)
- S-201 Annex A: Data Classification and Encoding Guide (DCEG)
- S-201 Annex B: Application Schema (XSD)
- S-201 Annex C2: Feature Catalogue (S100FC/5.0;
  `productId=S-201`, `versionNumber=2.0.0`,
  `versionDate=2025-05-19`)
- S-201 Annex D: Portrayal Catalogue (top-level rule
  `main_PaperChart.xsl` discovered by `ruleType="TopLevelTemplate"`)

## Concrete feature classes (FC Edition 2.0.0, 62 types)
- Buoys: `LateralBuoy`, `CardinalBuoy`, `IsolatedDangerBuoy`,
  `SafeWaterBuoy`, `SpecialPurposeGeneralBuoy`,
  `EmergencyWreckMarkingBuoy`, `MooringBuoy`, `InstallationBuoy`
- Beacons: `LateralBeacon`, `CardinalBeacon`, `IsolatedDangerBeacon`,
  `SafeWaterBeacon`, `SpecialPurposeGeneralBeacon`
- Lights: `Light`, `LightAllAround`, `LightSectored`,
  `LightAirObstruction`, `LightFloat`, `LightVessel`,
  `LightFogDetector`
- Structures: `Landmark`, `Daymark`, `OffshorePlatform`, `Pile`,
  `SiloTank`, `WindTurbine`, `BuildingSingle`, `Topmark`
- Equipment: `FogSignal`, `RadarReflector`, `Retroreflector`,
  `RadarTransponderBeacon`, `RadioStation`, `Generator`,
  `PowerSource`
- AIS: `PhysicalAISAidToNavigation`, `SyntheticAISAidToNavigation`,
  `VirtualAISAidToNavigation`
- Lines/areas: `NavigationLine`, `RecommendedTrack`,
  `LocalDirectionOfBuoyage`, `NavigationalSystemOfMarks`,
  `DataCoverage`
- Aggregations: `AtonAggregation`, `AtonAssociation`
- Abstract supertypes (do not appear in concrete datasets):
  `AidsToNavigation`, `StructureObject`, `Equipment`

## Concrete information types (FC Edition 2.0.0 — exactly 4)
- `AtoNFixingMethod` — survey/fixing method metadata
- `AtonStatusInformation` — AtoN change / status payload
- `PositioningInformation` — positional information
- `SpatialQuality` — positional accuracy metadata

Hard-coded as a closed set in `S201DatasetReader.InformationTypeCodes`;
used to discriminate information types from features inside the
unified `<members>` container shape (see below).

## Two real-world dataset shapes — the reader handles both
1. **XSD-canonical shape** (Annex B): root `<Dataset>` in namespace
   `http://www.iho.int/S-201/gml/cs0/1.0`. Features wrapped in
   `<member>`; information types wrapped in `<imember>`. Same shape
   as S-125 / S-127.
2. **Real-world published shape** (NLB, TH, CIL, IALA sample
   datasets): root `<DataSet>` (note camelCase) in namespace
   `http://www.iho.int/S-201/gml/cs0/2.0` **or** the legacy
   `http://www.iho.int/201/gml/1.0` (no `S-` prefix, no `/cs0/`
   path segment). A **unified `<members>` container** holds both
   features and information types as direct children — the S-131
   pattern. Feature elements typically appear in the **default
   (empty) namespace**, not the application-schema prefix.

The `<DataSet>` vs `<Dataset>` casing also varies between encoders.
Treat both as the dataset root.

## xlink relationships
- Equipment ↔ host structure surfaces as `<child>` / `<parent>`
  elements with `xlink:href="#…"` and `xlink:title="StructureEquipment"`.
  Roles are preserved verbatim by the reader; do **not** normalise
  them to the older `theParentFeature` / `theSubordinateFeature`
  names that appeared in earlier drafts.
- Information references (e.g. status, positioning, fixing method)
  appear as element children of features with `xlink:href` and no
  body content. The reader's two-pass classification splits
  references into `InformationReferences` (target is one of the 4
  information-type ids) and `FeatureReferences` (everything else,
  defaulting to feature-to-feature when the target is unknown).

## Review checklist
1. GML parsing tolerates both S-100 GML 5.0 and 1.0 / profile
   namespaces.
2. GML parsing tolerates `cs0/1.0`, `cs0/2.0`, and the legacy
   `/201/gml/1.0` application namespaces, plus the empty default
   namespace for feature elements inside `<members>`.
3. GML parsing tolerates both `<member>`/`<imember>` (XSD shape) and
   unified `<members>` (real-world shape).
4. Coordinate ordering in `<gml:pos>` / `<gml:posList>` is **lat lon**
   for EPSG:4326 (S-100 Part 10b §6.2).
5. xlink role names are preserved verbatim; reference targets have
   their leading `#` stripped before storage.
6. Container objects without geometry (`AtonAggregation`,
   `AtonAssociation`) parse without error and emit a feature with
   `GeometryType.None`.
7. Portrayal goes through XSLT (no Lua). Top-level template is
   `main_PaperChart.xsl` (auto-discovered by `GmlPortrayalCatalogueBase`
   via `ruleType="TopLevelTemplate"`); sub-templates load via
   `xsl:include`.
8. Public API changes have xunit tests; synthetic GML fixtures live
   under `tests/datasets/S201/`. Real-world dataset coverage is
   gated by the `S201_REAL_DATASET_PATH` env var (SkippableFact).

## Known pitfalls in this repo
- **Real datasets do NOT match the bundled XSD shape.** The
  upstream Annex B XSD declares `cs0/1.0` with `<member>`/`<imember>`;
  every real published dataset I've seen uses `cs0/2.0` with
  `<members>`, default-ns features, and `<DataSet>` casing. The
  reader supports both — do not "fix" the reader to only accept
  the XSD shape.
- Attribute values in real datasets are **human-readable strings**
  (e.g. `<categoryOfLateralMark>Starboard-Hand Lateral Mark</…>`)
  not the numeric listed-value codes the FC implies. Portrayal
  rules expect strings; do not coerce.
- The PC `productId="S-201"` internal `version="1.0"` is the PC
  version, not the spec version. The spec is Edition 2.0.0.
- The upstream PC zip wraps everything in an extra
  `7. S-201 Portrayal Catalogue - Annex D/` folder. The bundled
  layout strips that wrapper but is otherwise byte-identical.
- Sample datasets from `s-201tool.gla-rad.org` may have unclear
  licensing — do **not** commit any of them as test fixtures.
  Test fixtures must be synthetic and hand-authored from FC+XSD.

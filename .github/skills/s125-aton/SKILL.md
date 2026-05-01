---
name: s125-aton
description: |
  Expert knowledge of IHO S-125 Marine Aids to Navigation Product
  Specification. Covers GML encoding (S-100 Part 10b) using the
  S-100 GML 5.0 profile, the S-125 application schema (lights, buoys,
  beacons, daymarks, AIS aids), AtoN status indication and status
  information types, xlink-based information bindings, and the
  XSLT-based portrayal pipeline. USE FOR: S-125 datasets, marine
  aids to navigation, GML parsing for S-125, XSLT portrayal of AtoN,
  vector pipeline changes affecting S-125, S-125 reader/source
  code, S-125 tests. DO NOT USE FOR: S-101 ENC AtoN feature classes
  (use s101-enc), S-124 navigational warnings (use s124-nav-warnings),
  generic GML (use s100-framework).
---

# S-125 Marine Aids to Navigation expert

## When engaged
- Tasks touching `src/EncDotNet.S100.Datasets.S125/**` or
  `tests/EncDotNet.S100.Datasets.S125.Tests/**`
- GML/XSLT portrayal changes for AtoN
- Vector pipeline changes affecting S-125
- Bundled S-125 spec asset updates under
  `src/EncDotNet.S100.Specifications/content/S125/`

## Spec anchors
- Canonical: **S-125 Edition 1.0.0** Marine Aids to Navigation PS
  (development repo: <https://github.com/iho-ohi/S-125-Product-Specification-Development>)
- S-100 Part 10b: GML encoding (S-100 GML 5.0 profile)
- S-100 Part 9: Portrayal (XSLT path)
- S-125 Annex A: Feature Catalogue
- S-125 Annex B: Portrayal Catalogue

## Concrete feature classes (Edition 1.0.0)
- Buoys: `LateralBuoy`, `CardinalBuoy`, `IsolatedDangerBuoy`,
  `SafeWaterBuoy`, `SpecialPurposeGeneralBuoy`,
  `EmergencyWreckMarkingBuoy`, `MooringBuoy`, `InstallationBuoy`
- Beacons: `LateralBeacon`, `CardinalBeacon`, `IsolatedDangerBeacon`,
  `SafeWaterBeacon`, `SpecialPurposeGeneralBeacon`
- Lights: `LightAllAround`, `LightSectored`, `LightAirObstruction`,
  `LightFloat`, `LightVessel`, `LightFogDetector`
- Structures: `Landmark`, `Daymark`, `OffshorePlatform`, `Pile`,
  `SiloTank`, `WindTurbine`, `Topmark`
- Signals & equipment: `FogSignal`, `RadarReflector`, `Retroreflector`,
  `RadarTransponderBeacon`, `RadioStation`
- AIS: `PhysicalAISAidToNavigation`, `SyntheticAISAidToNavigation`,
  `VirtualAISAidToNavigation`
- Lines/areas: `NavigationLine`, `RecommendedTrack`,
  `LocalDirectionOfBuoyage`, `NavigationalSystemOfMarks`,
  `DangerousFeature`, `DataCoverage`, `QualityOfBathymetricData`,
  `SoundingDatum`, `VerticalDatumOfData`
- Aggregations: `AtonAggregation`, `AtonAssociation`,
  `AtonStatusIndication`

## Concrete information types
- `AtonStatusInformation` — AtoN change/status payload
- `SpatialQuality` — positional accuracy metadata

## Review checklist
1. GML parsing tolerates both `s100gml/5.0` and the older
   `s100gml/1.0` (lower-case and `S100/profile/s100gml/1.0`)
   namespaces; do not hard-code one.
2. Coordinate ordering in `<gml:pos>` / `<gml:posList>` is **lat lon**
   for EPSG:4326 (S-100 Part 10b convention).
3. Information references on features (e.g. `<AtoNStatus
   xlink:href="#info1"/>`) preserve role name + trimmed identifier so
   the bundled XSLT can resolve them with `id($ref)` semantics.
4. The synthesised FeatureXML neutral form must include both
   `<Dataset><Features>` and `<Dataset><InformationTypes>` blocks,
   with information references emitted as `<RoleName
   informationRef="…"/>` children of the feature.
5. Container objects (`AtonAggregation`, `AtonAssociation`) carry no
   geometry — renderer must tolerate geometry-less features.
6. Portrayal goes through XSLT (no Lua). Top-level template is
   `main.xsl`; sub-templates load via `xsl:include` (resolved by
   the catalogue's `XmlResolver`).
7. Time validity (`fixedDateRange`, `periodicDateRange`) is UTC;
   do not coerce to local time at the source boundary.
8. Public API changes have xunit tests; synthetic GML fixtures live
   under `tests/datasets/S125/`.

## Known pitfalls in this repo
- The bundled S-125 1.0.0 development PC is **preliminary**. It
  ships only AtoN status indication, AtoN status information, and
  DataCoverage rules. Most concrete feature classes therefore render
  as the `QUESMRK1` question-mark fallback. Don't treat that as a
  parser bug.
- The XSLT references the association role with capital N
  (`AtoNStatus`) while the FC information-type code uses lowercase n
  (`AtonStatusInformation`). The reader and feature-XML projection
  must round-trip the role name as written in the dataset, not
  normalise it.
- The S-125 schema uses `<Dataset>` (single capital) as the root
  element, not `<DataSet>` as in S-124.
- `AtoNStatus` references appear as empty elements with `xlink:href`;
  the reader filters child-less elements with no text content as
  information references rather than simple attributes.

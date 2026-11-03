# EncDotNet.S100.Datasets.S57

This package reads IHO S-57 (Edition 3.1) Electronic Navigational Chart (ENC)
cells and translates them into the S-101 document model, so the S-101
Part 9A Lua portrayal in
[`EncDotNet.S100.Datasets.S101`](../EncDotNet.S100.Datasets.S101/README.md)
can draw them. An inland ENC cell is translated into S-401 instead. The package
also validates S-57 cells and reads and verifies S-57 exchange sets. Reference
it when you need the translation itself, its diagnostics, or the S-57 records.
To open and render an S-57 cell like any other dataset, use the
[`EncDotNet.S100`](../EncDotNet.S100/README.md) package.

This isn't an S-52 implementation. A translated cell is drawn with whatever the
S-101 portrayal catalogue produces for it. The mappings follow the IHO *S-57 to
S-101 Conversion Guidance* (S-101PT6 INF02A, draft 2021) and S-65 Annex B.

The S-57 records are read by the
[`EncDotNet.S57`](https://www.nuget.org/packages/EncDotNet.S57) package, which
is built on `EncDotNet.Iso8211`.

## Install

```bash
dotnet add package EncDotNet.S100.Datasets.S57
```

## Translate a cell

```csharp
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Datasets.S57;

S57Dataset s57 = S57Dataset.Open("path/to/cell.000");

// S-401 for an inland ENC, otherwise S-101.
var translator = S57ToS101Translator.ForTarget(s57.TranslationTarget);
S101Document document = translator.Translate(s57);

// From here, use the S-101 pipeline.
S101Dataset cell = S101Dataset.FromDocument(document);
foreach (var feature in new S101VectorSource(cell).GetFeatures().Take(5))
    Console.WriteLine($"{feature.Id} {feature.FeatureType} ({feature.GeometryType})");
```

`new S57ToS101Translator()` always targets S-101.

To convert a cell from the command line, use `s100 s57 convert`. See
[Bringing S-57 into the pipeline](../../docs/s57-to-s101.md).

## Main types

- **`S57Dataset`**: opens a `.000` cell and exposes the parsed
  `EncDotNet.S57.S57Document`, the S-57 product specification it declares
  (`DeclaredProductSpecification`) and the `TranslationTarget` that follows
  from it. The static `TranslationTargetFor(document)` does the same for a
  document you've already parsed. The static `IsS57File(path)` tells an S-57
  `.000` file from an S-101 one. `ReadMetadata` reads a cell's extent and
  display scale window without translating or portraying it. See
  [Read metadata](#read-metadata).
- **`S57ToS101Translator`**: translates an `S57Document` into an
  `S101Document`. It maps object and attribute codes, splits multipoint
  soundings, builds the `information` complex attribute from textual
  attributes, and converts nodes, edges and area rings into S-101 spatial
  records. `ForTarget(target)` builds a translator for S-101 or S-401 from the
  bundled mapping and catalogues; `ForTarget(target, mapping)` takes your own
  mapping.
- **`S57TranslationTarget`**: the S-100 product a translation produces: the
  bundled feature catalogue the output is checked against, and the product
  specification and edition the translated document declares. `S101` is the
  default. `S401` (edition 1.3.0) is for inland ENCs. Both share the S-101
  document model. A target whose catalogue has no `RangeSystem` class (S-401)
  skips the `C_AGGR` → `RangeSystem` step and reports those aggregations as
  unmapped.
- **`S57ProductSpecification`**: the codes a cell can declare in its
  `DSID`/`PRSP` subfield: `1` (maritime ENC), `2` (Object Catalogue Data
  Dictionary) and `10` (inland ENC, as declared by IENC producers such as
  USACE). `TryParse` parses the raw subfield value.
- **`S57S101Mapping`**: the code mapping table, embedded from the IHO S-57 to
  S-101 conversion guidance. `ForSpec(spec)` returns the table for a target
  product: `Default` for S-101, or for S-401, `Default` restricted to the
  feature classes and attributes the bundled S-401 catalogue defines. See
  [Inland ENCs](#inland-encs).
- **`S101AllowedEnumValues`**: drops enumerated attribute values that the
  destination feature catalogue doesn't permit. `Default` reads the S-101
  feature catalogue; `ForSpec(spec)` reads another bundled catalogue, such as
  S-401, once per product. It loads on first use.
- **`S101FeatureAttributeBindings`**: has the same `Default` and `ForSpec`
  shape. It answers `DefinesFeatureType(code)` and `DefinesAttribute(code)`.
  `Binds` and `IsSingleValued` cover information types too, and
  `BindsInformationType(feature, association, informationType)` tells you
  whether a feature class can reference an information type (for example,
  S-401 `LockBasin` → `AdditionalInformation` → `TimeScheduleInGeneral`).
- **`S57TranslationDiagnostics`**: counts what a translation dropped. See
  [Translation diagnostics](#translation-diagnostics).
- **`S57ExchangeSetVerification`** and **`S57ExchangeSetCatalog`**: S-57
  exchange sets. See
  [Exchange-set integrity verification](#exchange-set-integrity-verification)
  and [Exchange-set cell enumeration](#exchange-set-cell-enumeration).

The `S57DatasetProcessor` that renders, picks and validates an S-57 cell is in
[`EncDotNet.S100.Datasets.Pipelines`](../EncDotNet.S100.Datasets.Pipelines/README.md).

## Read metadata

`S57Dataset.ReadMetadata` returns a `DatasetMetadata` without translating or
portraying the cell. It calculates the WGS 84 extent from the raw spatial
coordinates, using the `DSPM` coordinate multiplication factor, and reads the
display scale window for the whole cell. A host can use it to frame a view over
a folder of S-57 cells before loading each one in full.

The window comes from `ResolveCellMinimumDisplayScale(document)`: the larger
of the compilation scale (`CSCL`) and the largest feature `SCAMIN` in the
cell. `CSCL` is the largest scale the cell is meant to be viewed at (S-52
§3.1.7; S-65 Annex B §2.1.6 maps it to the S-101 optimum display scale). On its
own it would hide content that producers encode to stay visible further out.
For example, USACE inland cells are compiled at 1:5,000 with `SCAMIN` up to
1:300,000. `ResolveCompilationScale(document)` returns `CSCL` alone, which
ranks the cell against overlapping cells.

## Apply update files

S-57 cells come as a base cell (`.000`) and update files (`.001`, `.002`, …).

- `S57Dataset.Open(string)` reads a base cell only.
- `S57Dataset.Open(Stream, IReadOnlyList<Stream>)` reads a base cell and
  applies the update streams in order.
- In an exchange set, `S57DatasetProcessor` applies each base cell's updates
  from the same set with `S57Document.ApplyChanges` before translation.
- `s100 s57 convert` finds update files next to a loose base cell and applies
  them. It then writes the product that the updated cell's
  `TranslationTarget` names (S-401 for an inland ENC, otherwise S-101), unless
  `--target` overrides it.

## Translation behaviour

| Aspect | Translation |
|---|---|
| Object/attribute codes | Looked up via the embedded `S57S101Mapping` rules (S-57 numeric code → S-101 acronym/code). Unknown S-57 attribute codes pass through; unknown enumerated values are dropped if the S-101 FC declares an allowable list. |
| Multi-point soundings (`SOUNDG`) | Exploded into S-101 `MultiPoint` spatial records and a `Sounding` feature so each depth value is independently portrayable. |
| `INFORM` / `TXTDSC` (English) | S-101: carried on a shared `NauticalInformation` **information type** (not on the feature) as a single `information` complex instance with `text` and/or `fileReference` and `language = "eng"`; the feature binds it via an `AdditionalInformation` / `theInformation` information association. S-401: an `information` instance on the feature itself, one for `INFORM` (`text`) and one for `TXTDSC` (`fileReference`), where the class binds `information`; otherwise as for S-101. |
| `NINFOM` / `NTXTDS` (national language) | S-101: carried as a second `information` instance on the same `NauticalInformation` record with empty `language` (S-57 has no language tag; Data Producers can populate it post-conversion). S-401: further inline instances, with the same empty `language`. |
| `OBJNAM` (English) | Carried as an S-101 `featureName` complex attribute instance with `name` and `language = "eng"`. |
| `NOBJNM` (national language) | Carried as a separate `featureName` instance with empty `language`. |
| `LITCHR` / `SIGGRP` / `SIGPER` (light features) | On feature classes that bind it (`LightAllAround`, `LightFogDetector`, `LightAirObstruction`), assembled into a single `rhythmOfLight` complex attribute instance: `lightCharacteristic` (mandatory), plus `signalGroup` / `signalPeriod` when present, and any `SIGSEQ`-derived `signalSequence` phases nested inside (see the `C_AGGR` rows). An out-of-range `LITCHR` code drops the instance (its mandatory sub-attribute would be missing). On non-light features (`FogSignal`, `RadarTransponderBeacon`) `SIGGRP` / `SIGPER` remain directly-bound simple attributes. |
| `DATSTA` / `DATEND` (date ranges) | On feature classes that bind it, assembled into a `fixedDateRange` complex attribute instance with `dateStart` and/or `dateEnd` (both optional). |
| `PERSTA` / `PEREND` (periodic date ranges) | On feature classes that bind it, assembled into a `periodicDateRange` complex attribute instance. Both `dateStart` and `dateEnd` are mandatory in the S-101 FC, so the instance is emitted only when both endpoints are present; otherwise the lone endpoint is dropped (and reported by the translation diagnostics). |
| `SURSTA` / `SUREND` (survey date ranges) | On feature classes that bind it (`QualityOfBathymetricData`, `QualityOfNonBathymetricData`, `QualityOfSurvey`), assembled into a `surveyDateRange` complex attribute instance. `dateEnd` (`SUREND`) is mandatory, so the instance is emitted only when it is present; `dateStart` (`SURSTA`) is optional. |
| `CATZOC` (zone of confidence) | On `QualityOfBathymetricData` (the sole feature class binding the complex), carried as the `categoryOfZoneOfConfidenceInData` sub-attribute of a `zoneOfConfidence` complex attribute instance. The S-57 and S-101 enumerations are identical (1=A1, 2=A2, 3=B, 4=C, 5=D, 6=U); an out-of-range code drops the instance. For quantified zones (A1–C) the nested `horizontalPositionUncertainty` and `verticalUncertainty` complexes are populated from the CATZOC-implied accuracy values of the IHO CATZOC table (IHO S-4 §B-290), each as an `uncertaintyFixed` fixed-metre term plus, where the table defines one, an `uncertaintyVariableFactor` percentage-of-depth term (e.g. A1 → ±5 m + 5% horizontal, 0.5 m + 1% vertical). ZOC D and U are unquantified, so only the category is emitted. `fixedDateRange` has no `CATZOC`-side source and is left unpopulated. |
| `CATPRA` (category of production area) | Feature-dependent. On `ProductionStorageArea` (`PRDARE`) it passes through to `categoryOfProductionArea`, whose enumeration shares codes 1–12 with S-57 `CATPRA`. On `OffshoreProductionArea` (`OSPARE`) the FC binds the *distinct* `categoryOfOffshoreProductionArea` enumeration, so the value is redirected and remapped (8 Tank Farm → 4, 9 Wind Farm → 1, 12 Solar Farm → 6); S-57 production categories with no offshore equivalent (quarry, mine, stockpile, power station, refinery, timber yard, factory, slag/spoil, production plant) are dropped. |
| `MORFAC` (mooring/warping facility) + `CATMOR` | S-101 has **no** `MooringWarpingFacility` class (it survives only in the sister product S-131); the S-101 portrayal catalogue wires the MORFAC symbols to `Dolphin.lua` (point) and `ShorelineConstruction.lua` (line/area), which sets the geometry default: **point → `Dolphin`, line/area → `ShorelineConstruction`**. `CATMOR` (a near-universal ~99.98% of corpus instances) refines specific point facilities that have their own S-101 class — **3 bollard → `Bollard`, 5 post/pile → `Pile`, 7 mooring buoy → `MooringBuoy`** — which redirect regardless of the geometry default (all such instances are points in practice). On the remaining point `Dolphin`s, `CATMOR` maps to `categoryOfDolphin`, but the S-57 and S-101 enumerations diverge (S-101: Mooring/Deviation/Berthing/Fender; S-57: dolphin/deviation-dolphin/bollard/tie-up-wall/pile/chain/mooring-buoy), so only the two coincident meanings are carried (1 dolphin → 1 Mooring Dolphin, 2 deviation dolphin → 2 Deviation Dolphin); 4 tie-up wall and 6 chain/wire/cable leave a `Dolphin` with no category. `CATMOR` is dropped wherever `MORFAC` redirects to a class that does not bind `categoryOfDolphin` (`Bollard`, `Pile`, `MooringBuoy`, `ShorelineConstruction`). |
| `M_NSYS` (navigational system of marks) + `ORIENT` | S-65 Annex B § 12.2: an `M_NSYS` with a value in `ORIENT` converts to **`LocalDirectionOfBuoyage`**, which binds `marksNavigationalSystemOf` (`MARSYS`) and `orientationValue` (`ORIENT`) directly. Any other `M_NSYS` stays `NavigationalSystemOfMarks`, which binds no orientation, so an empty (unknown) `ORIENT` is dropped there (a rule drop in the diagnostics). The inland twin `m_nsys` (17018) splits the same way for S-401 (IEHG conversion guidance 3.77 / 3.88). |
| `NATSUR` / `NATQUA` (seabed) | On `SeabedArea` (`SBDARE`, the sole feature class binding the complex), assembled into one or more `surfaceCharacteristics` complex attribute instances. The two S-57 lists are paired positionally: position *i* yields an instance carrying `natureOfSurface` = `NATSUR[i]` and `natureOfSurfaceQualifyingTerms` = `NATQUA[i]`. Both sub-attributes are optional, so a position with only one populated (e.g. `NATQUA` without `NATSUR`, the most common corpus case) still forms a valid instance; out-of-range enumerate codes are dropped individually. `SeabedArea` does **not** bind a top-level `natureOfSurface`, so on that feature `NATSUR` is routed exclusively into the complex; on other feature classes (`Coastline`, `LandRegion`, …) `NATSUR` remains a directly-bound simple `natureOfSurface`. |
| `SIGSEQ` (signal sequence) | Parsed into `signalSequence` complex attribute instances. The S-57 value is a `+`-separated list of phase durations in seconds where a parenthesised duration denotes an eclipse/silence phase; each phase becomes one instance carrying `signalDuration` (real seconds) and `signalStatus` (`1` = Lit/Sound for a bare duration, `2` = Eclipsed/Silent for a parenthesised one). On the light feature classes that bind `rhythmOfLight` the phases are **nested** inside that complex (its last sub-attribute per the FC), and are therefore emitted only when a `rhythmOfLight` instance is present (a valid `LITCHR`); on `FogSignal` / `RadarTransponderBeacon` `signalSequence` binds at the top level. Phases that do not parse as a real duration are dropped and reported. |
| Sector lights (`SECTR1` / `SECTR2` / `COLOUR` / `VALNMR` / `LITVIS` / `LITCHR` / `SIGGRP` / `SIGPER` / `SIGSEQ`) | A `LIGHTS` object carrying a sector arc (`SECTR1`/`SECTR2` present) is redirected from `LightAllAround` to **`LightSectored`**, and its light attributes are assembled into the mandatory `sectorCharacteristics` [1..*] complex: `lightCharacteristic` (from `LITCHR`, mandatory) plus optional `signalGroup`/`signalPeriod`/`signalSequence`, and one `lightSector` carrying `colour` (from the `COLOUR` list), `valueOfNominalRange` (`VALNMR`), `lightVisibility` (from the `LITVIS` list), and a `sectorLimit` whose `sectorLimitOne`/`sectorLimitTwo` `sectorBearing` come from `SECTR1`/`SECTR2` (three levels of nesting). Since each S-57 `LIGHTS` object encodes a single sector, one `LightSectored` feature is emitted per S-57 sector-light with one `lightSector` (conformant, as `lightSector` is [1..*]); **co-located sector arcs of one physical light — several `LIGHTS` objects sharing the same S-101 point — are merged into a single `LightSectored` feature carrying one `sectorCharacteristics` instance per arc** (see [Limitations](#limitations)). `LightSectored` binds none of these attributes at the top level, so all are diverted into the complex and none pass through as flat simple attributes. |
| Directional lights (`CATLIT` 1 / 16 + `ORIENT`) | A `LIGHTS` object whose `CATLIT` list contains 1 (directional function) or 16 (moiré effect) is also redirected to **`LightSectored`** (S-65 Annex B §12.8.6.1; the IEHG S-401 guidance matches). Its `ORIENT` becomes `lightSector` → `directionalCharacter` → `orientation` → `orientationValue`, with `moireEffect` = true for `CATLIT` 16. `orientation` is mandatory in `directionalCharacter`, so the complex is only emitted when `ORIENT` has a value. A directional light without `SECTR1`/`SECTR2` gets no `sectorLimit`, so the portrayal draws the direction line and oriented light flare. With a sector arc, it keeps its `sectorLimit`, which the portrayal draws in preference. `ORIENT` on a light that is not directional stays unconverted. `CATLIT` 1 and 16 have no `categoryOfLight` value and are still reported as dropped enumerate values. |
| `TOPSHP` / `COLOUR` / `COLPAT` (topmark) | A `TOPMAR` object is not an S-101 feature; it is a **slave** of a buoy/beacon/`LightFloat` master (linked by the master's `FFPT` with the master/slave relationship indicator). Its attributes are folded into the master's `topmark` [0..1] complex: `topmarkDaymarkShape` (from `TOPSHP`, the mandatory [1..1] sub-attribute), `colour` (from the `COLOUR` list, split into occurrences), and `colourPattern` (from `COLPAT`). The fold happens only when the master resolves to one of the 12 S-101 classes binding `topmark`; a `TOPMAR` referenced by a non-binding master (or a non-slave relationship) stays unmapped. An instance whose mandatory `topmarkDaymarkShape` is missing or fails S-101 enumeration validation is rolled back and reported. The absorbed `TOPMAR` emits no feature of its own; the `TopmarksAbsorbed` diagnostic counts every `TOPMAR` consumed by a topmark-binding master (a rolled-back instance is still counted as absorbed even though no `topmark` is emitted). |
| `HORCLR` (horizontal clearance) | Assembled into the mandatory `horizontalClearanceValue` [1..1] sub-attribute of a horizontal-clearance complex, selected per feature by the S-101 FC binding: `horizontalClearanceOpen` on `Gate` (`GATCON`) or `horizontalClearanceFixed` on the fixed-span classes that bind it (`SpanFixed`, `SpanOpening`, `Tunnel`, `ShorelineConstruction`, `StructureOverNavigableWater`, `Canal`, `DockArea`, `LockBasin`). The value is a real carried verbatim; the optional `horizontalDistanceUncertainty` has no S-57 source and is left unpopulated. On a feature binding neither complex `HORCLR` has no conformant home and stays unmapped — except on `BRIDGE`, where it moves to the bridge's span (see the `BRIDGE` row). |
| `VERCLR` / `VERCCL` / `VERCOP` / `VERCSA` (vertical clearances) | The rules target the complexes `verticalClearanceFixed` / `Closed` / `Open` / `Safe`. The translator assembles one where the resolved class binds it (e.g. `CableOverhead`, `PipelineOverhead`, `Conveyor`, `Crane`, `Tunnel`). The S-57 value goes to `verticalClearanceValue`; an empty (unknown) value stays empty, except on `verticalClearanceOpen`, whose value is optional. `verticalClearanceOpen` also gets `verticalClearanceUnlimited = false`. `VERACC` nests in each instance with a known value as `verticalUncertainty`/`uncertaintyFixed` (S-65 Annex B §2.2.4.3). A clearance whose complex the class doesn't bind is rule-dropped. S-401 sends a gate's `VERCLR` to `verticalClearanceOpen` (IEHG conversion guidance clause 3.55). S-65 has no such rule, so the S-101 translation drops it. Bridges are handled separately (see the `BRIDGE` row). |
| `ORIENT` (orientation) | On directional lights it goes into `directionalCharacter` (see the directional lights row). Otherwise it stays the top-level `orientationValue` on the classes that bind it directly (e.g. `RecommendedTrack`, `RadarLine`). On `NavigationLine`, `CurrentNonGravitational`, `TidalStreamFloodEbb` and `Crane` (and S-401 `Daymark`) it is assembled into the `orientation` complex, the only place those classes bind it. That lets the portrayal rotate current arrows and label navigation-line bearings (S-65 Annex B §3.3.1, §3.4, §10.1.1; IEHG conversion guidance clauses 3.27, 3.28, 3.32). |
| `SORDAT` (source date) | Mapped to the top-level `reportedDate` simple attribute (an `S100_TruncatedDate`), the S-57 `YYYYMMDD` value carried verbatim (matching the fidelity of `dateStart`/`dateEnd`). Because `SORDAT` is a near-universal S-57 attribute but the FC binds `reportedDate` on only ~50 feature classes, the mapping is gated on the resolved feature actually binding `reportedDate`; on a feature that does not (e.g. `Coastline`, `SeabedArea`, `LandRegion`, `DepthContour`, the light classes) `SORDAT` has no conformant home and stays unmapped. `SORIND` (source indication) has no general S-101 equivalent (the FC's `source` attribute binds only `UpdateInformation`) and is intentionally not mapped. On S-401, which binds a `sourceIndication` complex on most classes, `SORIND` goes verbatim into its `source` sub-attribute (IEHG conversion guidance). |
| `VALLMA` (value of local magnetic anomaly) | Assembled into the `valueOfLocalMagneticAnomaly` complex on `LocalMagneticAnomaly` (`LOCMAG`, the only feature that binds it). The value feeds the mandatory `magneticAnomalyValue` [1..1] real sub-attribute verbatim; the optional `referenceDirection` enum has no S-57 source and is left unpopulated. |
| `RADWAL` (radar wave length) | Assembled into the `radarWaveLength` complex on `RadarTransponderBeacon` (`RTPBCN`, binding it [0..2]). The list-typed S-57 value is a set of `value-band` pairs (e.g. `0.03-X` or `0.03-X,0.10-S`); each pair yields one complex instance carrying the mandatory `waveLengthValue` (real) and `radarBand` (text). A pair that does not split into both parts is dropped. |
| `CURVEL` (current velocity) | Assembled into the `speed` complex on `CurrentNonGravitational` (`CURENT`) and `TidalStreamFloodEbb` (`TS_FEB`). The value feeds the mandatory `speedMaximum` [1..1] real sub-attribute verbatim; the optional `speedMinimum` has no S-57 source and is left unpopulated. `speed` is mandatory on both classes, so without CURVEL the complex is still written with `speedMaximum` empty (null). |
| Primitives the S-101 class does not permit | S-65 Annex B (Ed 2.0.0). A point `DAMCON` becomes a `Landmark` with `categoryOfLandmark` 27 (dam) and, when `CONVIS` is absent, `visualProminence` 2, because `Dam` permits only curves and surfaces (§4.8.5); the dam-only attributes `Landmark` does not bind are dropped. A point `PIPSOL` becomes an `Obstruction` when a `DEPARE` or `DRGARE` covers it and a `Landmark` (with `categoryOfLandmark` empty (null), since Annex B names none, and `visualProminence` 2) when none does, because `PipelineSubmarineOnLand` permits only curves (§11.6.1); the pipeline attributes neither class binds are dropped. An area `RECTRC` is not converted, because `RecommendedTrack` permits only curves (§10.1.1); it is counted under `RuleDroppedObjectClasses`. A rule lists such primitives in `S57FeatureRule.DroppedPrimitives`. Before this, the S-101 rules drew both with the "?" default symbol. The NOAA base corpus has 1 point `DAMCON` and 96 area `RECTRC`. |
| Underwater hazard depths (`surroundingDepth`, `defaultClearanceDepth`) | S-52 derived these at display time (DEPVAL02, OBSTRN07, WRECKS05). The S-101 portrayal catalogue reads them as attributes instead, so the translator adds them to every class that binds them (`Obstruction`, `UnderwaterAwashRock`, `Wreck`). `surroundingDepth` is the least `DRVAL1` of the `DEPARE` / `DRGARE` objects under the hazard: containing a point, or overlapping a curve or surface. It is left out when no depth area lies under the hazard. `defaultClearanceDepth` is added only when the hazard has no `VALSOU`, from the tables in S-101 DCEG clause 30.1. It is the surrounding depth for a hazard that is always under water (`WATLEV` 3) with a sounding within or deeper than the surrounding range (`EXPSOU` 1 or 3). Otherwise a wreck takes 20.1 if non-dangerous (`CATWRK` 1, or the surrounding depth less 66 if larger), -15 for any other category, and 0 uncategorised and always under water or awash (`WATLEV` 3 or 5); an obstruction or rock takes 0.1 for a foul area (`CATOBS` 6) or always under water, 0 awash. Anything else is -15. Without either value the S-101 rules raise an error and the hazard is drawn with the "?" default symbol. Without `surroundingDepth` they treat every hazard shallower than the safety contour as an isolated danger. |
| `MLTYLT` (multiplicity of lights) | Assembled into the `multiplicityOfFeatures` complex on the light classes that bind it (`LightAllAround`, `LightSectored`, `LightAirObstruction`). The S-57 count feeds the optional `numberOfFeatures` integer verbatim and the mandatory `multiplicityKnown` boolean is set `true`. |
| `CATBRG` (category of bridge) | S-101 has no `categoryOfBridge`; per S-65 Annex B (Ed 1.2.0) § 4.8.10 the S-57 list is split across the enumerations and Boolean that `Bridge` binds. Each listed value is mapped: **1 fixed → `openingBridge` = false**; **2 opening → `openingBridge` = true**; **3 swing / 4 lifting / 5 bascule / 7 draw → `categoryOfOpeningBridge`** (same codes); **6 pontoon → `bridgeConstruction` 3**, **8 transporter → `bridgeConstruction` 5**, **10 viaduct → `bridgeConstruction` 2**, **12 suspension → `bridgeConstruction` 4**; **9 footbridge → `bridgeFunction` 3 (pedestrian)**, **11 aqueduct → `bridgeFunction` 4**. S-65 does not rule on **13 bridge arch**, which is an IENC extension of the attribute (IENC Feature Catalogue Ed. 2.4); the IEHG *S-57 ENC to S-401 Conversion Guidance* (Ed 1.3.0 draft 2) clauses 3.7 and 3.144 map it to **`bridgeConstruction` 1 (arch)**, and that rule is applied for both targets because `bridgeConstruction` 1 is “Arch” in the S-101 and the S-401 catalogue alike. `openingBridge` is emitted once for the whole list: true if any value is 2 or an opening-bridge category (so `CATBRG = 2,6` is an opening pontoon bridge), otherwise false. `bridgeFunction` is multi-valued; `bridgeConstruction` and `categoryOfOpeningBridge` are [0..1], so only the first such value is kept and later ones are reported under `RuleDroppedAttributes`. Values outside the list are dropped and reported under `DroppedEnumValues`. The value table lives in the `BRIDGE` feature rule (`DefaultRules`); `S101FeatureAttributeBindings.IsSingleValued` supplies the multiplicity. |
| `BRIDGE` → `Bridge` + `SpanFixed` / `SpanOpening` + `BridgeAggregation` | Follows S-65 Annex B (Ed 1.2.0) §4.8.10 and S-101 DCEG 6.6–6.8 / 25.4. A curve/surface `BRIDGE` becomes a `Bridge`. If it crosses navigable water it also gets a span component. That span is a **`SpanOpening`** when `CATBRG` contains 2, 3, 4, 5 or 7 (opening, swing, lifting, bascule, draw), otherwise a **`SpanFixed`**. The span shares the bridge's geometry and S-57 identity, and the `Bridge` links to it with a `BridgeAggregation` / `theComponent` feature association. The translator can't tell "navigable water" from one object, so it keys on the clearance the span class makes mandatory. A fixed span needs `VERCLR`, which feeds `verticalClearanceFixed` [1..1]. An opening span needs `VERCCL`, which feeds `verticalClearanceClosed` [1..1]. Presence is what counts: an S-57 empty (unknown) value still yields the span, with `verticalClearanceValue` populated as empty (null). A bridge without that clearance converts to a `Bridge` alone. The clearance attributes bind only on the spans, so they never appear on `Bridge`: `VERCLR`/`VERCCL`/`VERCOP` go to the `verticalClearanceValue` of `verticalClearanceFixed`/`Closed`/`Open`, and `HORCLR` goes to `horizontalClearanceFixed`. `HORACC` becomes that complex's `horizontalDistanceUncertainty` (§2.2.4.2). `VERACC` becomes the nested `verticalUncertainty`/`uncertaintyFixed` of each vertical clearance that has a known value (§2.2.4.3). `VERDAT` becomes the span's `verticalDatum` (§2.1.2). `SCAMIN` and `DATSTA`/`DATEND` are copied to the span so it displays and date-filters with its bridge. On an opening span, `verticalClearanceOpen` gets `verticalClearanceUnlimited = true` when `VERCOP` is absent, and `false` when `VERCOP` is present, even with an empty value. Clearance values with no home (e.g. `VERCLR` on an opening bridge, or any clearance on a bridge with no span) are recorded as rule-dropped. A **point** `BRIDGE` becomes a **`Landmark`**, because `Bridge` permits no point geometry. It gets `categoryOfLandmark = 26` (bridge) and, when `CONVIS` is absent, `visualProminence = 2` (not visually conspicuous). These are the defaults S-65 gives for the analogous point `DAMCON` → `Landmark` conversion (§4.8.5/§4.8.15). `CATBRG` and the vertical-clearance/datum attributes are dropped there. The `BridgeSpansEmitted` diagnostic counts spans. Across the 7,184-cell NOAA base corpus, the 20,413 `BRIDGE` objects (none of them points) yield 20,248 spans: 18,557 fixed and 1,691 opening. |
| `C_AGGR` of `BRIDGE` (+ `PYLONS` / `PONTON`, lights) → one `Bridge` | S-65 Annex B §4.8.10 asks producers to encode each span of a bridge over navigable water as its own `BRIDGE` object and to group them, plus any pylons or pontoons, with a `C_AGGR`. The converter should then build a single `Bridge` from them. A `C_AGGR` qualifies when it has at least one curve/surface `BRIDGE` member, every member resolves by LNAM, no `BRIDGE` member is a point or already claimed (each `BRIDGE` belongs to at most one collection; the first in document order wins), and no member is a navigation line or track (that makes it a track grouping through the bridge, e.g. NOAA US5WI3FK, left to the range-system handling). Any other member does not disqualify it: IENC collects fenders, notice marks, signal stations, mooring facilities and more with a bridge (IENC Encoding Guide 2.4.1, bridge clause H), and those convert on their own. Each member `BRIDGE` emits only its span. One aggregated `Bridge` is then emitted, carrying the `C_AGGR`'s feature identity, with a `BridgeAggregation` / `theComponent` association to every emitted span, pylon and pontoon, and a `StructureEquipment` / `theEquipment` association to every emitted light and signal station member (IENC collects with a bridge its vertical clearance indicators, `sistaw` `catsiw` 16, Encoding Guide clause I.3.3, and its bridge-passage traffic signal stations, `sistat` `catsit` 8, clause R.2.1). IENC places bridge lights on the navigable span and the piers bounding it with no master object (bridge light clause C), so collection membership is all that ties a light to its bridge: it is linked to the `Bridge`, the only S-401 structure that binds more than one light. Each link is gated on the target Feature Catalogue binding it, and a light listed by two collections is linked by the first only (S-101 binds a light to at most one structure); `BridgeEquipmentLinked` counts them. Its Bridge-level attributes come from a representative member, chosen in this order: the first opening member (S-101 requires an opening bridge when any span opens), then the first named member, then the first member. Any attribute carried on the `C_AGGR` itself (typically `OBJNAM`) takes precedence. If neither the `C_AGGR` nor the representative is named, the first named member supplies the name. Members' own names that differ from the one the `Bridge` carries are kept as further `featureName` instances that are not for chart display: `nameUsage` 3 (No Chart Display) in S-401, and no `nameUsage` in S-101, which lists only 1 and 2 (spans bind no `featureName` in either). The geometry is the members' geometry merged into one: curves are chained by shared nodes, and adjacent surfaces lose their shared edges. If the members don't join into a single curve or a single exterior ring (twin or dual bridges, parts separated by gaps), the `Bridge` is **multi-part** (S-100 Part 10a `SPAS` `0..*`): one chain of curve segments per connected run, walked from a free end, or one surface per exterior ring with the holes that lie inside it. An outline ring that lies inside another is a hole of the union (members around a gap), not a surface of its own. The renderers and query tools treat each part on its own. Only when the members mix curves and surfaces is the collection **not aggregated**: its members convert one by one, each as its own `Bridge` with its own geometry and name, and each light is linked to the nearest member `Bridge` (by S-57 geometry). A `Bridge` without geometry would have its name left undrawn by the S-101/S-401 portrayal. `BridgeCollectionsUnjoined` counts these collections. The `BridgeAggregationsEmitted` diagnostic counts these bridges. A `C_AGGR` that does not qualify falls through to the range-system handling in the next row. None of the local NOAA, French or IC-ENC cells contain a qualifying collection; of the 744 bridge collections in the 109 USACE inland cells, 743 aggregate (111 of them as multi-surface bridges) and 1 mixes curves and surfaces, and all 4,246 of their lights are linked. |
| `C_AGGR` (aggregation) → `RangeSystem` + `RangeSystemAggregation` | An S-57 `C_AGGR` collection whose members (resolved via its `FFPT` peer feature-pointers, keyed by LNAM) are all permitted `RangeSystemAggregation` components **and** include at least one navigational track (`NavigationLine`, `RecommendedTrack`, or `RecommendedRouteCentreline`) is mapped to a synthesised **geometry-less `RangeSystem`** collection feature (FC binds `theCollection` to a dedicated `RangeSystem` class, not to a member). One `RangeSystemAggregation` / `theComponent` feature association is emitted from the `RangeSystem` to each resolved member (this fills the `FeatureAssociationCatalogue` and `RoleCatalogue`). A two-phase pass allocates and registers every qualifying `RangeSystem`'s record before wiring associations, so nested `C_AGGR` members (`RangeSystem` is itself a permitted component) resolve without dangling references. A `C_AGGR` that has no track member or any non-permitted member has no S-101 home and stays unmapped (counted under `UnmappedObjectClasses`). `C_ASSO` (whose NOAA-corpus majority is `LandArea`-partition groupings) has no S-101 named-association equivalent and is left unmapped. |
| Spatial relationships | S-57 vector pointer records (`VRPT`, `FSPT`) translated to S-101 spatial associations and ring orientation. |
| Complex-attribute nesting | Every attribute row carries its ISO 8211 `PAIX` (S-100 Part 10a): the 1-based position of the complex attribute row it belongs to, or 0 for a row bound directly to the feature. The translator writes complexes in pre-order and sets `PAIX` from the Feature Catalogue's sub-attribute bindings in a final pass, so `zoneOfConfidence` → `categoryOfZoneOfConfidenceInData`, `featureName` → `name` / `language` and the deeper nests (`sectorCharacteristics` → `lightSector` → `sectorLimit` → …) are explicit in the output. |
| Feature identifiers (FOID) | Each S-101 feature keeps the agency, `FIDN` and `FIDS` of its S-57 object. A feature the translator derives from an object that also yields another feature (the `SpanFixed` / `SpanOpening` of a `BRIDGE`) keeps the agency and `FIDN` and takes the next `FIDS` (wrapping from 65535 to 0) that no feature in the dataset uses; the primary feature (`Bridge`) keeps the S-57 identifier. Distinct S-57 objects that share a FOID in the source keep it, so `S101-R-2.1` still reports the source duplicate. `DerivedFeatureIdentifiersAssigned` counts the re-identified features. |
| List-valued enumerations (`COLOUR`, `NATSUR`, `CATLIT`, …) | S-57 encodes multiple enumerate selections as a comma-separated string (e.g. `COLOUR = "3,1"`). Each code is split into a separate S-101 attribute occurrence (distinct `ATIX`) and validated independently, so one invalid code no longer discards the whole attribute. |

## Inland ENCs

An S-57 cell that declares the inland ENC product specification
(`DSID`/`PRSP` = 10) is translated into S-401. `S57S101Mapping.ForSpec` builds
the S-401 table from the S-101 table:

- **Restricted to S-401.** `RestrictToFeatureTypes` limits the table to the
  feature classes the bundled S-401 catalogue defines. That clears 15
  deep-sea and natural-feature targets (for example `RAPIDS` → `Rapids` and
  `LITFLT` → `LightFloat`), which S-401 translation reports as rule-dropped.
  `RestrictToAttributes` also drops attribute targets that the S-401 catalogue
  doesn't define, such as `CATICE`. `CATBRG` stays, because its bridge
  category targets exist in both catalogues.
- **Inland rules** (internal `InlandRules`). These cover the 53 object
  classes and 91 attributes of the IEHG Inland ENC Feature Catalogue 2.4
  (codes 17000 and up). Rules are resolved by attribute code
  (`ResolveAttribute(ushort, …)`), so an inland code and its upper-case twin
  never hide each other.
- **Anchorage rules** (internal `S401AnchorageRules`, applied last). These
  replace the `ACHARE`/`achare` and `CATACH`/`catach` rules for S-401 only, so
  the S-101 table is unchanged.

The inland rules follow the IEHG *S-57 ENC to S-401 Conversion Guidance*:

- Most inland codes re-register a standard acronym in lower case (`bridge` =
  17011 for `BRIDGE` = 11) and reuse the standard rule, as the guidance
  prescribes. The rest map to the S-401 class or attribute whose alias is the
  IENC acronym (`notmrk` → `NoticeMark`, `wtwdis` → `waterwayDistance`).
- `hunits`, the unit of `wtwdis`, becomes `distanceUnitOfMeasurement` with the
  guidance's value changes: hectometres 4 → 7, statute miles 5 → 4, nautical
  miles 6 → 5. Feet has no S-401 code and is dropped. It's only emitted on the
  classes that bind it.
- A time schedule (`tisdge`) becomes an S-401 `TimeScheduleInGeneral`
  information record carrying `cattab`, `schref`, `shptyp`, `useshp`,
  `aptref`, `dirimp` and `SORDAT`. Every feature linked to it, through a
  `C_ASSO` or a feature pointer, references it with `AdditionalInformation`
  if its class can. The `C_ASSO` emits nothing itself. Every link is emitted,
  even beyond S-401's one `AdditionalInformation` per feature, because IENC
  encodes one schedule per ship type or period. A schedule that no emitted
  feature can carry (for example one on a `Bridge`, which only takes
  `ServiceHours`) is reported as rule-dropped, and the schedule attributes are
  dropped on features.
- The usable lock and dock dimensions `horcll` and `horclw` become
  `horizontalClearanceLength` and `horizontalClearanceWidth` where the class
  binds them. On `LockBasin`, which binds no width, `horclw` fills
  `horizontalClearanceFixed` unless `HORCLR` already does.
- The shore-power attributes of a bunker station (`catvol`, `catfrq`,
  `amoamp`, `catplg`, `shrnum`, `allcon`) become the sub-attributes of S-401's
  `powerCharacteristics`. The translator builds one instance per listed
  voltage and frequency on `BunkerStation`, and drops them elsewhere.
- The bridge arch collection `c_brga` emits no feature. The `SpanFixed` of its
  first member bridge links the other members' fixed spans with S-401's
  `BridgeArchAssociation`. A `c_brga` with fewer than two fixed spans is
  reported as rule-dropped.
- Inland bridges convert the same way as maritime `BRIDGE`: `CATBRG`
  categories on the `Bridge`, a `SpanFixed` or `SpanOpening` carrying the
  clearances, and point bridges as `Landmark`. The S-401 catalogue defines all
  of these. The IENC-only `CATBRG` value 13 (bridge arch) becomes
  `bridgeConstruction` 1 (arch), which keeps the arch a fixed span.
- The ship category, assembly and cargo ranges of a maximum permitted ship
  dimensions area (`lc_csi`/`lc_cse`, `lc_asi`/`lc_ase`, `lc_cci`/`lc_cce`)
  all convert. `lc_csi` is the one whose S-401 target has no `alias`, so its
  rule comes from conversion guidance clause 3.82 instead of from the
  catalogue.
- Four codes have no S-401 equivalent and are reported as rule-dropped:
  `NEWOBJ` and its three definition attributes (`CLSDEF`, `CLSNAM`,
  `SYMINS`).

The anchorage rules:

- Anchorage and anchor berth `CATACH` 10 (IENC "anchorage for
  pushing-navigation vessels") becomes `categoryOfAnchorage` 16 (conversion
  guidance clauses 3.3 and 3.4).
- An anchorage area whose `CATACH` is exactly 8 (small craft mooring area)
  becomes `MooringArea`, with `categoryOfMooringArea` 1 (clause 3.85).
- Clause 3.85's title reads "catach=1, 2, 3". Those are the S-401
  `categoryOfMooringArea` codes, not S-57 values: `CATACH` 1–3 are
  unrestricted, deep-water and tanker anchorages. So `CATACH` 1–3 stay on
  `AnchorageArea`.
- A list such as `7,8` also stays on `AnchorageArea` and keeps every code,
  because S-401 `categoryOfAnchorage` binds 8 too.

## Limitations

- **`M_COVR` areas with no coverage are dropped.** An `M_COVR` with
  `CATCOV = 2` ("no coverage available") isn't translated. S-101's
  `DataCoverage` feature always asserts coverage, so a `DataCoverage` for a
  no-data area would claim data over the cell's hole. Overlap suppression
  between cells of different scales would then blank the coarser cell there.
  `M_COVR` with `CATCOV = 1`, or with no `CATCOV`, translates to
  `DataCoverage` as usual. The `RuleDroppedObjectClasses` diagnostic counts the
  dropped ones.
- **Feature coverage.** Feature class coverage comes from the `<S100FC:alias>`
  (S-57 acronym) entries in the bundled S-101 feature catalogue. Across a
  3,636-cell NOAA ENC corpus, about 99% of feature instances translate. The
  classes left unmapped are collections with no S-101 named association
  (`C_ASSO`, and any `C_AGGR` that is neither a range system nor a bridge
  collection) and meta objects with no S-101 equivalent (`M_CSCL`). Objects
  that become S-101 attributes are folded into their host: `TOPMAR` folds into
  the master buoy or beacon's `topmark` complex through the S-57 master/slave
  relationship. Splits by geometry are handled where S-101 needs them
  (`MORFAC` → `Dolphin`/`ShorelineConstruction` by primitive, refined by
  `CATMOR`; point `BRIDGE` → `Landmark`). Bridges are split into a `Bridge`
  and its spans, a bridge `C_AGGR` becomes one `Bridge` with its span, pylon
  and pontoon components, and range-system `C_AGGR` collections become
  `RangeSystem` features. See the table above.
- **Attributes.** Most attribute rules map an S-57 attribute to a simple S-101
  attribute bound directly to the feature. A simple attribute passes through
  only when the resolved class binds it in the target feature catalogue.
  Otherwise it's counted in `RuleDroppedAttributes`, which matches the
  conversion guidance's "not converted" exceptions (for example `WATLEV` and
  `NATCON` on `Pile`, `COLOUR` on `SeabedArea`, and `CATTSS` on the parts of a
  traffic separation scheme). The complex attributes in the table above are
  assembled when the class binds them. Their sub-attributes are checked
  against the destination attribute's global enumeration, not the narrower
  `permittedValues` some complexes declare, in line with the rest of the
  pipeline. Sub-attributes of the other S-101 complex attributes are left
  unmapped. `SORDAT` → `reportedDate` applies only on the 50 or so classes
  that bind `reportedDate`. `SORIND` has no general S-101 equivalent and isn't
  mapped; S-401 carries it in `sourceIndication` > `source`.
- **Enumerated values.** Some S-57 enumerated values have different codes in
  S-101. Values the destination feature catalogue doesn't permit are dropped
  and reported in the diagnostics. List values are split into separate S-101
  occurrences first, so only the invalid codes are dropped, not the whole
  attribute.
- **Sector lights.** Co-located sector arcs of one physical light are several
  S-57 `LIGHTS` objects, each with one sector (`SECTR1`/`SECTR2`), that resolve
  to the same S-101 point. They're merged into one `LightSectored` feature with
  one `sectorCharacteristics` instance per arc (the catalogue allows
  `[1..*]`). The light that comes first in the document is the primary; the
  others emit no feature of their own, and `SectorLightsMerged` counts them.
  Attributes outside the sector (height, status, name) come from the primary,
  as they're the same for every arc of a light in the NOAA data.
- **Zone of confidence uncertainty.** The nested uncertainty complexes of
  `zoneOfConfidence` (`horizontalPositionUncertainty`,
  `verticalUncertainty`) are filled in for zones A1–C from the IHO CATZOC
  accuracy table. Zones D and U have no quantified accuracy and carry only the
  category.
- **Textual information.** For S-101, `INFORM`, `TXTDSC`, `NINFOM` and
  `NTXTDS` become a `NauticalInformation` information type linked to the
  feature by an `AdditionalInformation` / `theInformation` association (the
  "fuller path" in the conversion guidance), not an inline `information`
  complex. A feature with any of the four attributes gets one
  `NauticalInformation` record, with an English instance, a national-language
  instance, or both. `NauticalInformationTypesEmitted` counts the records. The
  S-101 portrayal reads the linked record (`ProcessNauticalInformation`), so
  text and picture notes render as they did with the inline form. For S-401
  (`S57TranslationTarget.InlineTextualInformation`), each of the four becomes
  its own `information` instance on the feature, the IEHG conversion
  guidance's default. Some S-401 rules read the text from there, such as the
  `NoticeMark` text panel, and some classes, `NoticeMark` among them, bind no
  `AdditionalInformation` association. A class that doesn't bind
  `information` still gets the `NauticalInformation` record.
- **Area rings.** An S-57 area's boundary edges (`FSPT`) are chained into
  rings by shared begin and end nodes, reversing edges where needed, not by
  their order in `FSPT`. S-57 lists all interior edges together (`USAG` =
  interior) whichever hole they belong to, so grouping by `USAG` would merge
  all of an area's holes into one boundary. That ring would jump between holes
  and draw long spikes. Chaining gives one closed ring per hole, and each
  becomes its own S-101 interior ring.
- **Bridge spans depend on the clearance attributes.** S-57 can't say whether
  a bridge crosses navigable water, so a `BRIDGE` gets a `SpanFixed` or
  `SpanOpening` only when it carries the clearance attribute that span class
  makes mandatory (`VERCLR` or `VERCCL`). A navigable bridge encoded without it
  converts to a `Bridge` alone, and its other clearances are dropped. So does
  an opening bridge encoded with `VERCLR` instead of `VERCCL`. The span type
  uses the same `CATBRG` test as the `Bridge`'s `openingBridge`, so a
  `SpanOpening` is only attached to an opening `Bridge`. Separate `BRIDGE`
  spans that no `C_AGGR` groups each convert to their own `Bridge` and span.
- **Collections.** Range-system and bridge `C_AGGR` collections are converted;
  other collections aren't. Across the 7,184-cell NOAA base corpus, the
  translator creates 2,083 `RangeSystem` features in 931 cells with 6,690
  component associations, and no collection is left without components or with
  dangling references. `RangeSystemsEmitted` counts them. Other `C_AGGR`
  groupings and all `C_ASSO` associations (mostly `LandArea` partition
  groupings) have no S-101 named association and stay unmapped. For S-401, a
  `C_ASSO` that links an inland time schedule is used by the time schedule
  conversion.

## Translation diagnostics

`S57ToS101Translator.Translate` has an overload that takes an
`S57TranslationDiagnostics`. When you pass one, the translator counts what it
dropped and why. Use it, for example, to audit a corpus for gaps in
`S57S101Mapping`:

```csharp
using EncDotNet.S100.Datasets.S57;

var diagnostics = new S57TranslationDiagnostics();
var document = new S57ToS101Translator().Translate(s57, diagnostics);

// Object classes in the data that have no mapping rule.
foreach (var (objl, count) in diagnostics.UnmappedObjectClasses)
    Console.WriteLine($"OBJL {objl}: {count}");
```

Pass `null`, or use an overload without the parameter, and nothing is
collected. The counters are:

- **Gaps**, where no rule exists: `UnmappedObjectClasses` and
  `UnmappedAttributes`.
- **Drops by design**, where a rule exists but produces nothing:
  `RuleDroppedObjectClasses` and `RuleDroppedAttributes`.
- **Other losses**: `DroppedEnumValues` (values the feature catalogue
  rejects) and `FeaturesDroppedForNoGeometry`.
- **Totals**: `FeatureRecordsRead`, `FeaturesEmitted`, and the sounding
  counts (`SoundingFeaturesRead`, `SoundingFeaturesEmitted`,
  `SoundingPointsEmitted`, `SoundingFeaturesWithoutPoints`).
- **Synthesised features and links**:
  - `SectorLightsMerged`: co-located sector arcs merged into one
    `LightSectored`.
  - `TopmarksAbsorbed`: `TOPMAR` objects folded into a master that binds
    `topmark`.
  - `NauticalInformationTypesEmitted`: `NauticalInformation` records created
    for `INFORM`, `TXTDSC`, `NINFOM` and `NTXTDS`.
  - `RangeSystemsEmitted`: `RangeSystem` features created from range-system
    `C_AGGR` collections.
  - `TimeSchedulesEmitted`: S-401 `TimeScheduleInGeneral` records created for
    inland `tisdge` schedules.
  - `BridgeSpansEmitted`, `BridgeAggregationsEmitted`,
    `BridgeEquipmentLinked` and `BridgeCollectionsUnjoined`: bridge spans,
    aggregated bridges, linked lights and signal stations, and bridge
    collections that couldn't be joined.
  - `DerivedFeatureIdentifiersAssigned`: derived features, such as bridge
    spans, given their own `FIDS`.

`s100 s57 convert` prints these counters as a summary and writes them as JSON
with `--report`. See [Bringing S-57 into the pipeline](../../docs/s57-to-s101.md).

## Validation

`S57DatasetProcessor.Validate()` validates in two passes:

1. **Before translation**, `S57PreTranslationRules.Default` checks the raw
   `EncDotNet.S57.S57Document` for things that don't survive translation.
2. **After translation**, the translated S-101 document is checked with
   `S101DatasetRules.Default`, the same pack that runs on native S-101 cells.
   Its rule IDs get the prefix `S101-as-S57/`, so you can tell whether a
   problem is in the S-57 source or in the translated S-101. This pass only
   runs for a maritime cell. An inland cell is translated into S-401, which has
   no rule pack, so its report holds the first pass only.

The translation itself passes the S-101 pack: across the 7,184 cells of the
NOAA ENC corpus, the only findings on the translated S-101 are the duplicate
feature identifiers in the source data. Findings about a feature or spatial
record carry its location (a point, or the envelope of its geometry), so the
viewer's validation overlay can mark them.

The rules in `S57PreTranslationRules.Default`:

| Rule ID | Severity | Checks |
|---------|----------|--------|
| `S57-R-1.1` | Error | The `DSID` record is present, and the `DSPM` compilation scale (`CSCL`) is greater than 0. |
| `S57-R-1.2` | Warning | The cell has at least one `M_COVR` meta object. |
| `S57-PROJ-PARSE` | — | Reserved for future parser findings. It has no checks yet. |

The two reports are joined in
[`EncDotNet.S100.Datasets.Pipelines`](../EncDotNet.S100.Datasets.Pipelines/README.md#validate-s-57-cells)
and cached on the processor.

## Exchange-set integrity verification

`S57ExchangeSetVerification` verifies the integrity of an S-57 or S-63
exchange set (a folder with a `CATALOG.031`). It returns the same
[`ExchangeSetVerificationResult`](../EncDotNet.S100.ExchangeSets/README.md)
as S-100 exchange-set verification, so `s100 validate` reports both the same
way.

```csharp
using EncDotNet.S100.Datasets.S57;
using EncDotNet.S100.ExchangeSets;

ExchangeSetVerificationResult result =
    await S57ExchangeSetVerification.VerifyAsync("path/to/exchange-set");

bool intact = result.IntegrityVerified;   // no CRC mismatches or missing files
bool signed = result.AllValid;            // every file signature is valid
```

The upstream S-57 verifier (`EncDotNet.S57` 0.5.0 or later) works on a
directory (`CATALOG.031` and the files it lists), while the S-100 verifier
works on an `IAssetSource`. So this is an adapter that maps the result, not a
shared interface.

How the result is mapped:

- Outcomes are mapped by name, from `S57VerificationOutcome` to
  [`VerificationOutcome`](../EncDotNet.S100.ExchangeSets/README.md). The two
  enums have the same members; mapping by name means a future difference fails
  with an error instead of mapping to the wrong value.
- The signature outcome (`Outcome`) and the checksum outcome
  (`ChecksumOutcome`) are kept separately.
- S-57 integrity uses a CRC-32 (from `CATALOG.031`), not the SHA-256 digest of
  the S-100 model. The CRC values are in `FileVerificationResult.Detail`, and
  `ComputedSha256` is `null`.
- The verdict follows S-100. `NoChecksum` and `NotSigned` don't fail: an
  unsigned set that's intact has `IntegrityVerified == true` and
  `AllValid == false`. Only `ChecksumMismatch`, `FileMissing`, `Error` and
  invalid signatures fail integrity. This matches the upstream S-57
  `AllValid`.
- The upstream S-57 verifier reports every file as `NotSigned`, because it
  doesn't verify S-63 signatures yet. CRC checking works.

## Exchange-set cell enumeration

`S57ExchangeSetCatalog` reads a `CATALOG.031` (with `EncDotNet.S57`'s
`S57CatalogReader`) and groups the catalogued files into base cells, each with
its updates from the same set and, if known, its extent:

```csharp
using EncDotNet.S100.Datasets.S57;

IReadOnlyList<S57ExchangeSetCell> cells =
    S57ExchangeSetCatalog.ReadBaseCells("path/to/exchange-set");

foreach (S57ExchangeSetCell cell in cells)
{
    Console.WriteLine(cell.RelativePath);          // the .000 base cell
    foreach (string update in cell.UpdateRelativePaths)
        Console.WriteLine($"  {update}");          // .001, .002, … in order
    // cell.BoundingBox is the EPSG:4326 extent, or null.
}

var union = S57ExchangeSetCatalog.UnionBoundingBox(cells);
```

Files are grouped by their 8-character cell name. The `.000` file is the base,
and `.001`, `.002`, … are its updates in the order they apply. Other entries
(`.TXT` files, certificates, the catalogue itself) are ignored, and update files
with no matching base are skipped. Paths use the platform's separators.
`SelectBaseCells(S57Catalog)` does the grouping on a catalogue you've already
read, which is useful in tests.

The viewer uses this with a `FileSystemAssetSource` rooted at the exchange set,
so each cell goes through the same `S57DatasetProcessor` as a single `.000`
file, with its updates applied before translation. Like the verification
adapter, this adapts the directory-based S-57 model rather than sharing an
interface.

## Reverse translation (preview)

`S101ToS57Translator` translates an `S101Document` back into an S-57
`EncDotNet.S57.S57Document`, following S-65 Annex C (*S-101 to S-57 ENC
Conversion Guidance*, Ed 1.0.0). Its main use today is as a round-trip check of
the forward translation, not as a producer of S-57 cells for distribution:
there's no S-57 writer yet, and Annex C expects the producer to clean up the
output by hand.

```csharp
var s101 = new S57ToS101Translator().Translate(S57Dataset.Open("cell.000"));

var diagnostics = new S101ToS57TranslationDiagnostics();
var options = new S101ToS57TranslationOptions { InformSeparator = "; ", NationalLanguage = "fra" };
EncDotNet.S57.S57Document s57 = new S101ToS57Translator(options).Translate(s101, diagnostics);
```

What it covers:

- **The general rules (Annex C §2).** A multi-valued enumeration joins with
  commas on an S-57 List attribute and keeps its first value on an Enumerated
  one. A mandatory S-57 attribute with no source value is written empty.
  Booleans become enumerations (`radarConspicuous` → `CONRAD` 1/2). Truncated
  dates lose their trailing dashes. Time, URI and URN attributes and Text
  Placement are dropped. `featureName` becomes `OBJNAM`/`NOBJNM` by
  `nameUsage`. `fileReference` becomes `TXTDSC`/`NTXTDS` by language, and only
  the first `pictorialRepresentation` is kept as `PICREP`. Text from
  `information`, Nautical Information and `multiplicityOfFeatures` folds into
  `INFORM`/`NINFOM`.
- **Meta features (§3).** Data Coverage becomes `M_COVR` with `CATCOV` 1, and
  `CATCOV` 2 objects fill the rest of the bounding box so that the `M_COVR`
  objects tile it without overlap. Adjoining coverages merge into one
  `CATCOV` 1 object. The largest optimum display scale becomes the
  compilation scale (`DSPM` `CSCL`), and a coverage at another scale gets an
  `M_CSCL`. The commonest `SoundingDatum` and `VerticalDatumOfData` go to the
  dataset parameters and the rest to `M_SDAT`/`M_VDAT`. Update Information is
  dropped.
- **Feature and attribute classes (§4–§30).** The rules (`S101ToS57Rules`)
  invert the forward mapping, with an Annex C override layer, and cite an
  Annex C clause each. The override layer holds the classes S-57 → S-101
  never produces, the class changes Annex C makes by attribute value
  (`S101ToS57FeatureRule.Redirects`, e.g. `categoryOfLandmark` 26 → `BRIDGE`,
  a radar-conspicuous `Pile` → `BCNSPP`), the objects it adds
  (`Companions`, e.g. a `PILPNT` for a structure in the water), and the
  enumeration differences of Table A-2: value remaps, and values S-57 lacks
  folding into `INFORM` as text (`S101ToS57AttributeRule.InformText`). The
  attributes each feature clause says are not converted on its class are
  generated from Annex C (`tools/annexc-clauses`).
- **Information types (§24).** Nautical Information and Contact Details fold
  into the feature (`INFORM`, `CALSGN`, `COMCHA`, …); Service Hours and
  Non-standard Working Day are dropped.
- **Structural inverses.** A `Bridge` with spans becomes one `BRIDGE` per
  `SpanFixed`/`SpanOpening`, each carrying the bridge's attributes and the
  span's clearances; one span keeps the bridge's FOID, several are tied by a
  `C_AGGR` (§6.6–§6.8). Range systems, traffic separation schemes, routes,
  fairway systems, island groups and mooring trots become `C_AGGR` (§25), and
  the aids-to-navigation and fairway-auxiliary associations `C_ASSO`.
  Structure/Equipment becomes master and slave feature pointers (§18.2). A
  `LightSectored` becomes one `LIGHTS` per sector (§19.3), a `topmark` a slave
  `TOPMAR` (§18.1), and `surfaceCharacteristics` positional
  `NATSUR`/`NATQUA` lists, with `/` before an underlying layer (§12.1).
- **Geometry and topology.** Points become isolated nodes, and S-101 curves
  become shared edges between connected nodes. Where S-57 doesn't allow the
  S-101 primitive (Table 2-1), a curve becomes an area 0.3 mm wide at the
  compilation scale and a surface becomes a point inside it. The S-101 skin of
  the earth is cut around `FLODOC`, `HULKES` and `PONTON` areas, which are
  skin of the earth in S-57 (§2.5.1.1). Both that cut and the `M_COVR`
  tiling node the edges involved (snap-rounded to the coordinate grid) and
  split each edge for every object that shares it, so the topology stays
  chain-node. `S57TopologyCheck` checks the result: every edge ends at
  connected nodes, every area closes, the skin of the earth has no gaps or
  overlaps, and `M_COVR` tiles its bounding box.
- **Feature identifiers (§2.5.6).** An object converted one to one keeps its
  S-101 FOID; further objects from the same feature take the next free
  subdivision.

`S101ToS57TranslationOptions` holds the choices Annex C leaves to the
converter: the `INFORM` separator, the 300-character policy, the preferred
national language, what to do with a name that has no `nameUsage`, and whether
to make the optional `INFORM` conversions. `S101ToS57TranslationDiagnostics`
counts the features read and written, and the features and attributes dropped
by rule (with the clause). It also counts the values folded into `INFORM`, the
values truncated to the first, the mandatory attributes written empty, and the
classes and attributes with no rule.

Not covered: spatial-record attributes from Spatial Quality (`QUAPOS`, and
`POSACC`/`SOUACC` on vector records, §24.5), because `S101Document` doesn't
read spatial-to-information associations; the optional `M_QUAL` `CATZOC` 4/5
fill (§3.8); and the S-401 product.

The opt-in round-trip test `S57RoundTripCorpusTests` runs S-57 → S-101 → S-57
over a folder of cells (`ENCDOTNET_S57_CORPUS`) and sorts each difference as an
expected loss, an expected restructure, unexplained or a reverse bug, in a JSON
report. It compares areas as polygons as well as attributes, and runs
`S57TopologyCheck` on both cells: a topology issue the result has and the
source doesn't is counted as a reverse bug.

### Annex C coverage

Every clause of Annex C §3–§30, with the rule that converts it or the reason
it's dropped (`S101ToS57Coverage.Clauses`; a test keeps this table current):

<!-- annex-c-coverage:start -->
<details>
<summary>Feature types and general clauses (§3–§23): 218 clauses</summary>

| Clause | S-101 | Treatment | S-57 |
|---|---|---|---|
| 3.1 | horizontal uncertainty | assembled | POSACC from horizontalPositionUncertainty; QUAPOS on spatial records isn't read (§24.5) |
| 3.2 | vertical uncertainty | assembled | SOUACC from verticalUncertainty |
| 3.3 | hierarchy of metadata | general | no conversion |
| 3.4 | QualityOfNonBathymetricData | rule | M_ACCY |
| 3.5 | DataCoverage | rule | M_COVR |
| 3.6 | NavigationalSystemOfMarks | rule | M_NSYS |
| 3.7 | LocalDirectionOfBuoyage | rule | M_NSYS |
| 3.8 | QualityOfBathymetricData | rule | M_QUAL |
| 3.9 | SoundingDatum | structural | DSPM SDAT for the commonest datum, M_SDAT for the others (§3.9) |
| 3.10 | VerticalDatumOfData | structural | DSPM VDAT for the commonest datum, M_VDAT for the others (§3.10) |
| 3.11 | QualityOfSurvey | rule | M_SREL |
| 3.12 | UpdateInformation | dropped | not converted (§3.12) |
| 4.1 | MagneticVariation | rule | MAGVAR |
| 4.2 | LocalMagneticAnomaly | rule | LOCMAG |
| 5.1 | cliffs | general | guidance; the features convert by their own clauses |
| 5.2 | cuttings and embankments | general | guidance; the features convert by their own clauses |
| 5.3 | Coastline | rule | COALNE |
| 5.4 | LandArea | rule | LNDARE |
| 5.5 | IslandGroup | rule | C_AGGR |
| 5.6 | LandElevation | rule | LNDELV |
| 5.7 | River | rule | RIVERS |
| 5.8 | Rapids | rule | RAPIDS |
| 5.9 | Waterfall | rule | WATFAL |
| 5.10 | Lake | rule | LAKARE; RIVERS when status 5 |
| 5.11 | LandRegion | rule | LNDRGN; a curve is dropped |
| 5.12 | Vegetation | rule | VEGATN |
| 5.13 | IceArea | rule | ICEARE |
| 5.14 | SlopingGround | rule | SLOGRD |
| 5.15 | SlopeTopline | rule | SLOTOP |
| 5.16 | Tideway | rule | TIDEWY |
| 6.1 | BuiltUpArea | rule | BUAARE |
| 6.2 | Building | rule | BUISGL; plus PILPNT when inTheWater, point |
| 6.3 | AirportAirfield | rule | AIRARE |
| 6.4 | Runway | rule | RUNWAY |
| 6.5 | Helipad | rule | RUNWAY CATRUN 2; plus LNDMRK when point |
| 6.6 | Bridge | structural | BRIDGE, one per span when it has spans, tied by C_AGGR (§6.6) |
| 6.7 | SpanFixed | rule | BRIDGE |
| 6.8 | SpanOpening | rule | BRIDGE |
| 6.9 | Conveyor | rule | CONVYR |
| 6.10 | CableOverhead | rule | CBLOHD |
| 6.11 | PipelineOverhead | rule | PIPOHD |
| 6.12 | PylonBridgeSupport | rule | PYLONS |
| 6.13 | FenceWall | rule | FNCLNE |
| 6.14 | Railway | rule | RAILWY |
| 6.15 | Road | rule | ROADWY |
| 6.16 | Tunnel | rule | TUNNEL |
| 7.1 | landmark encoding combinations | general | guidance; the features convert by their own clauses |
| 7.2 | Landmark | rule | LNDMRK; CTRPNT when categoryOfLandmark 22 or categoryOfLandmark 23; BRIDGE when categoryOfLandmark 26; DAMCON when categoryOfLandmark 27; plus PILPNT when inTheWater, point |
| 7.3 | SiloTank | rule | SILTNK; plus PILPNT when inTheWater, point |
| 7.4 | WindTurbine | rule | LNDMRK CATLMK 19, CONVIS 2; plus PILPNT when inTheWater, point |
| 7.5 | FortifiedStructure | rule | FORSTC; plus PILPNT when inTheWater, point |
| 7.6 | ProductionStorageArea | rule | PRDARE |
| 8.1 | works in progress | general | guidance; the features convert by their own clauses |
| 8.2 | Checkpoint | rule | CHKPNT |
| 8.3 | Hulk | rule | HULKES |
| 8.4 | Pile | rule | PILPNT; CTNARE when categoryOfPile 5, area; MORFAC when line/area or categoryOfPile 8; BCNSPP when radarConspicuous, point |
| 8.5 | Dyke | rule | DYKCON |
| 8.6 | ShorelineConstruction | rule | SLCONS; MORFAC when categoryOfShorelineConstruction 23 |
| 8.7 | StructureOverNavigableWater | rule | BUISGL; plus HRBFAC when categoryOfStructure 2 |
| 8.8 | Causeway | rule | CAUSWY |
| 8.9 | Canal | rule | CANALS |
| 8.10 | DistanceMark | rule | DISMAR |
| 8.11 | Gate | rule | GATCON |
| 8.12 | Dam | rule | DAMCON |
| 8.13 | Crane | rule | CRANES; plus PILPNT when inTheWater, point |
| 8.14 | Berth | rule | BERTHS |
| 8.15 | Dolphin | rule | MORFAC |
| 8.16 | Bollard | rule | MORFAC CATMOR 3 |
| 8.17 | DryDock | rule | DRYDOC |
| 8.18 | FloatingDock | rule | FLODOC |
| 8.19 | Pontoon | rule | PONTON |
| 8.20 | DockArea | rule | DOCARE |
| 8.21 | Gridiron | rule | GRIDRN |
| 8.22 | LockBasin | rule | LOKBSN |
| 8.23 | MooringTrot | rule | C_AGGR |
| 9.1 | SeaAreaNamedWaterArea | rule | SEAARE |
| 10.1 | tidal data | general | guidance; the features convert by their own clauses |
| 10.2 | TidalStreamFloodEbb | rule | TS_FEB |
| 10.3 | CurrentNonGravitational | rule | CURENT |
| 10.4 | WaterTurbulence | rule | WATTUR |
| 10.5 | TidalStreamPanelData | rule | TS_PAD |
| 11.1 | generalisation of depth portrayal | general | no conversion |
| 11.2 | representation of depth | general | no conversion |
| 11.3 | Sounding | rule | SOUNDG |
| 11.4 | DredgedArea | rule | DRGARE |
| 11.5 | SweptArea | rule | SWPARE |
| 11.6 | DepthContour | rule | DEPCNT |
| 11.7 | DepthArea | rule | DEPARE |
| 11.8 | DepthNoBottomFound | rule | SOUNDG QUASOU 5 |
| 11.9 | areas with inadequate depth information | general | guidance; the features convert by their own clauses |
| 11.10 | UnsurveyedArea | rule | UNSARE |
| 12.1 | SeabedArea | rule | SBDARE |
| 12.2 | WeedKelp | rule | WEDKLP |
| 12.3 | Seagrass | rule | WEDKLP CATWED 3 |
| 12.4 | Sandwave | rule | SNDWAV |
| 12.5 | Spring | rule | SPRING |
| 13.1 | danger lines | general | guidance; the features convert by their own clauses |
| 13.3 | doubtful dangers | general | guidance; the features convert by their own clauses |
| 13.4 | UnderwaterAwashRock | rule | UWTROC |
| 13.5 | Wreck | rule | WRECKS |
| 13.6 | Obstruction | rule | OBSTRN; VEGATN when categoryOfObstruction 23, area |
| 13.7 | FoulGround | rule | OBSTRN CATOBS 7, WATLEV 3 |
| 13.8 | DiscolouredWater | rule | CTNARE "Discoloured water" |
| 13.9 | FishingFacility | rule | FSHFAC |
| 13.10 | MarineFarmCulture | rule | MARCUL |
| 14.1 | OffshorePlatform | rule | OFSPLF; plus LNDMRK when flareStack |
| 14.2 | CableSubmarine | rule | CBLSUB; MORFAC when categoryOfCable 9 |
| 14.3 | CableArea | rule | CBLARE |
| 14.4 | PipelineSubmarineOnLand | rule | PIPSOL |
| 14.5 | SubmarinePipelineArea | rule | PIPARE |
| 14.6 | OffshoreProductionArea | rule | OSPARE |
| 15.2 | traffic lanes | general | guidance; the features convert by their own clauses |
| 15.3 | traffic separation schemes | general | guidance; the features convert by their own clauses |
| 15.4 | NavigationLine | rule | NAVLNE |
| 15.5 | RecommendedTrack | rule | RECTRC |
| 15.6 | RangeSystem | rule | C_AGGR |
| 15.7 | Fairway | rule | FAIRWY |
| 15.8 | FairwaySystem | rule | C_AGGR |
| 15.9 | RecommendedRouteCentreline | rule | RCRTCL |
| 15.10 | TwoWayRoutePart | rule | TWRTPT |
| 15.11 | TwoWayRoute | rule | C_AGGR |
| 15.12 | RecommendedTrafficLanePart | rule | RCTLPT |
| 15.13 | DeepWaterRouteCentreline | rule | DWRTCL |
| 15.14 | DeepWaterRoutePart | rule | DWRTPT |
| 15.15 | DeepWaterRoute | rule | C_AGGR |
| 15.16 | InshoreTrafficZone | rule | ISTZNE |
| 15.17 | PrecautionaryArea | rule | PRCARE |
| 15.18 | TrafficSeparationSchemeLanePart | rule | TSSLPT |
| 15.19 | SeparationZoneOrLine | rule | TSEZNE; TSELNE when line |
| 15.20 | TrafficSeparationSchemeBoundary | rule | TSSBND |
| 15.21 | TrafficSeparationSchemeCrossing | rule | TSSCRS |
| 15.22 | TrafficSeparationSchemeRoundabout | rule | TSSRON |
| 15.23 | TrafficSeparationScheme | rule | C_AGGR |
| 15.24 | ArchipelagicSeaLaneArea | rule | ARCSLN |
| 15.25 | ArchipelagicSeaLaneAxis | rule | ASLXIS |
| 15.26 | ArchipelagicSeaLane | rule | C_AGGR |
| 15.27 | RadioCallingInPoint | rule | RDOCAL |
| 15.28 | FerryRoute | rule | FERYRT |
| 15.29 | RadarLine | rule | RADLNE |
| 15.30 | RadarRange | rule | RADRNG |
| 15.31 | RadarStation | rule | RADSTA |
| 16.1 | boundaries and limits | general | guidance; the features convert by their own clauses |
| 16.2 | maritime jurisdiction areas | structural | a CTNARE "In dispute" for a disputed area; curves widened to areas |
| 16.3 | AnchorageArea | rule | ACHARE |
| 16.4 | MooringArea | rule | ACHARE |
| 16.5 | AnchorBerth | rule | ACHBRT |
| 16.6 | SeaplaneLandingArea | rule | SPLARE |
| 16.7 | DumpingGround | rule | DMPGRD |
| 16.8 | MilitaryPracticeArea | rule | MIPARE |
| 16.9 | AdministrationArea | rule | ADMARE; plus CTNARE when inDispute; a curve becomes a 0.3 mm area |
| 16.10 | CargoTranshipmentArea | rule | CTSARE |
| 16.11 | CautionArea | rule | CTNARE |
| 16.12 | InformationArea | rule | M_NPUB |
| 16.13 | ContiguousZone | rule | CONZNE; plus CTNARE when inDispute; a curve becomes a 0.3 mm area |
| 16.14 | ContinentalShelfArea | rule | COSARE; plus CTNARE when inDispute; a curve becomes a 0.3 mm area |
| 16.15 | CustomZone | rule | CUSZNE; plus CTNARE when inDispute |
| 16.16 | ExclusiveEconomicZone | rule | EXEZNE; plus CTNARE when inDispute; a curve becomes a 0.3 mm area |
| 16.17 | FisheryZone | rule | FSHZNE; plus CTNARE when inDispute |
| 16.18 | FishingGround | rule | FSHGRD |
| 16.19 | FreePortArea | rule | FRPARE |
| 16.20 | HarbourAreaAdministrative | rule | HRBARE |
| 16.21 | LogPond | rule | LOGPON |
| 16.22 | OilBarrier | rule | OILBAR |
| 16.23 | StraightTerritorialSeaBaseline | rule | STSLNE |
| 16.24 | TerritorialSeaArea | rule | TESARE; plus CTNARE when inDispute; a curve becomes a 0.3 mm area |
| 16.25 | SubmarineTransitLane | rule | SUBTLN |
| 16.26 | PilotageDistrict | rule | ADMARE JRSDTN 3 |
| 16.27 | CollisionRegulationsLimit | rule | CTNARE "Collision regulations limit"; a curve becomes a 0.3 mm area |
| 16.28 | MarinePollutionRegulationsArea | rule | ADMARE JRSDTN 3 |
| 17.1 | minefields | general | guidance; RESARE by §17.8 |
| 17.2 | degaussing ranges | general | guidance; RESARE by §17.8 |
| 17.3 | nature reserves | general | guidance; RESARE by §17.8 |
| 17.4 | speed limits | assembled | INFORM "Speed limit is n units" (vesselSpeedLimit) |
| 17.5 | anchoring restricted | general | guidance; RESARE by §17.8 |
| 17.6 | areas to be avoided | general | guidance; RESARE by §17.8 |
| 17.7 | environmentally sensitive sea areas | general | guidance; RESARE by §17.8 |
| 17.8 | RestrictedArea | rule | RESARE |
| 18.1 | parts of navigational aids | assembled | topmark → TOPMAR, light sectors → LIGHTS |
| 18.2 | relationships | structural | Structure/Equipment → master and slaves (FFPT) |
| 18.3 | buoyage systems | general | M_NSYS by §3.6 / §3.7 |
| 19.1 | lights | general | guidance; the light classes convert by their own clauses |
| 19.2 | LightAllAround | rule | LIGHTS |
| 19.3 | LightSectored | rule | LIGHTS |
| 19.4 | LightFogDetector | rule | LIGHTS CATLIT 7 |
| 19.5 | LightAirObstruction | rule | LIGHTS CATLIT 6 |
| 20.1 | LateralBuoy | rule | BOYLAT |
| 20.2 | CardinalBuoy | rule | BOYCAR |
| 20.3 | IsolatedDangerBuoy | rule | BOYISD |
| 20.4 | SafeWaterBuoy | rule | BOYSAW |
| 20.5 | SpecialPurposeGeneralBuoy | rule | BOYSPP |
| 20.6 | EmergencyWreckMarkingBuoy | rule | BOYSPP CATSPM 27 |
| 20.7 | InstallationBuoy | rule | BOYINB |
| 20.8 | MooringBuoy | rule | MORFAC CATMOR 7; SMCFAC when visitorsMooring |
| 20.9 | LateralBeacon | rule | BCNLAT |
| 20.10 | CardinalBeacon | rule | BCNCAR |
| 20.11 | IsolatedDangerBeacon | rule | BCNISD |
| 20.12 | SafeWaterBeacon | rule | BCNSAW |
| 20.13 | SpecialPurposeGeneralBeacon | rule | BCNSPP |
| 20.14 | Daymark | rule | DAYMAR |
| 20.15 | LightFloat | rule | LITFLT |
| 20.16 | LightVessel | rule | LITVES |
| 20.17 | Retroreflector | rule | RETRFL |
| 20.18 | RadarReflector | rule | RADRFL |
| 20.19 | FogSignal | rule | FOGSIG |
| 21.1 | AIS aids to navigation | general | guidance; §21.2 and §21.3 |
| 21.2 | PhysicalAISAidToNavigation | structural | INFORM text on the structure it equips (§21.2) |
| 21.3 | VirtualAISAidToNavigation | rule | NEWOBJ, its attributes by virtualAISAidToNavigationType |
| 21.4 | RadioStation | rule | RDOSTA |
| 21.5 | RadarTransponderBeacon | rule | RTPBCN |
| 22.1 | PilotBoardingPlace | rule | PILBOP |
| 22.2 | VesselTrafficServiceArea | rule | ADMARE JRSDTN 3 |
| 22.3 | CoastGuardStation | rule | CGUSTA; a surface becomes a point |
| 22.4 | SignalStationWarning | rule | SISTAW; a surface becomes a point |
| 22.5 | SignalStationTraffic | rule | SISTAT; a surface becomes a point |
| 22.6 | RescueStation | rule | RSCSTA; a surface becomes a point |
| 22.7 | HarbourFacility | rule | HRBFAC |
| 22.8 | SmallCraftFacility | rule | SMCFAC |
| 23.1 | TextPlacement | dropped | cartographic features are prohibited in S-57 (§2.5.8.1, §23.1) |

</details>

<details>
<summary>Information types and associations (§24–§25): 26 clauses</summary>

| Clause | S-101 | Treatment | S-57 |
|---|---|---|---|
| 24.1 | ContactDetails | assembled | CALSGN, COMCHA, SIGFRQ and INFORM on the feature |
| 24.2 | ServiceHours | dropped | no S-57 encoding |
| 24.3 | NonStandardWorkingDay | dropped | no S-57 encoding |
| 24.4 | NauticalInformation | assembled | INFORM / NINFOM, TXTDSC / NTXTDS, PICREP on the feature |
| 24.5 | SpatialQuality | dropped | on spatial records, which the S-101 reader doesn't associate (§24.5) |
| 25.1 | AdditionalInformation | assembled | information types fold into the feature's attributes (§24) |
| 25.2 | AidsToNavigationAssociation | structural | C_ASSO of the feature and its aids |
| 25.3 | ASLAggregation | structural | C_AGGR |
| 25.4 | BridgeAggregation | structural | C_AGGR of the bridge's BRIDGE, PYLONS and PONTON objects |
| 25.5 | CautionAreaAssociation | dropped | not converted |
| 25.6 | DeepWaterRouteAggregation | structural | C_AGGR |
| 25.7 | FairwayAggregation | structural | C_AGGR |
| 25.8 | FairwayAuxiliary | structural | C_ASSO of the fairway and its auxiliary features |
| 25.9 | IslandAggregation | structural | C_AGGR |
| 25.10 | MooringTrotAggregation | structural | C_AGGR |
| 25.11 | PilotageDistrictAssociation | structural | C_AGGR of the ADMARE and its PILBOP, which take PILDST / NPLDST |
| 25.12 | QualityOfBathymetricDataComposition | dropped | not converted (§24.5) |
| 25.13 | RangeSystemAggregation | structural | C_AGGR |
| 25.14 | RoofedStructureAggregation | structural | C_AGGR of the PYLONS and BUISGL |
| 25.15 | SpatialAssociation | dropped | not converted (§24.5) |
| 25.16 | StructureEquipment | structural | master and slaves (FFPT) |
| 25.17 | TextAssociation | dropped | not converted (§23.1) |
| 25.18 | TrafficSeparationSchemeAggregation | structural | C_AGGR |
| 25.19 | TwoWayRouteAggregation | structural | C_AGGR |
| 25.20 | UpdateAggregation | dropped | not converted (§3.12) |
| 25.21 | UpdatedInformation | dropped | not converted (§3.12) |

</details>

<details>
<summary>Simple attributes (§27, §28, §30): 235 clauses</summary>

| Clause | S-101 | Treatment | S-57 |
|---|---|---|---|
| 27.1 | basedOnFixedMarks | rule | CATTRK |
| 27.2 | beaconShape | rule | BCNSHP |
| 27.3 | bridgeConstruction | rule | CATBRG on Bridge |
| 27.4 | bridgeFunction | rule | CATBRG on Bridge |
| 27.5 | buildingShape | rule | BUISHP |
| 27.6 | buoyShape | rule | BOYSHP |
| 27.7 | buriedDepth | rule | BURDEP |
| 27.8 | callSign | rule | CALSGN |
| 27.9 | categoryOfAirportAirfield | rule | CATAIR |
| 27.10 | categoryOfAnchorage | rule | CATACH |
| 27.11 | categoryOfBuiltUpArea | rule | CATBUA |
| 27.12 | categoryOfCable | rule | CATCBL |
| 27.13 | categoryOfCanal | rule | CATCAN |
| 27.14 | categoryOfCardinalMark | rule | CATCAM |
| 27.15 | categoryOfCargo | rule | INFORM text for some values |
| 27.16 | categoryOfCheckpoint | rule | CATCHP |
| 27.17 | categoryOfCoastline | rule | CATCOA |
| 27.18 | categoryOfConveyor | rule | CATCON |
| 27.19 | categoryOfCrane | rule | CATCRN |
| 27.20 | categoryOfDam | rule | CATDAM |
| 27.21 | categoryOfDock | rule | CATDOC |
| 27.22 | categoryOfDolphin | rule | CATMOR |
| 27.23 | categoryOfDumpingGround | rule | CATDPG |
| 27.24 | categoryOfFence | rule | CATFNC |
| 27.25 | categoryOfFerry | rule | CATFRY |
| 27.26 | categoryOfFishingFacility | rule | CATFIF |
| 27.27 | categoryOfFogSignal | rule | CATFOG |
| 27.28 | categoryOfFortifiedStructure | rule | CATFOR |
| 27.29 | categoryOfGate | rule | CATGAT |
| 27.30 | categoryOfHarbourFacility | rule | CATHAF |
| 27.31 | categoryOfHulk | rule | CATHLK |
| 27.32 | categoryOfIce | rule | CATICE |
| 27.33 | categoryOfInstallationBuoy | rule | CATINB |
| 27.34 | categoryOfLandRegion | rule | CATLND |
| 27.35 | categoryOfLandmark | rule | CATLMK |
| 27.36 | categoryOfLateralMark | rule | CATLAM |
| 27.37 | categoryOfLight | rule | CATLIT |
| 27.38 | categoryOfMarineFarmCulture | rule | CATMFA |
| 27.39 | categoryOfMilitaryPracticeArea | rule | CATMPA |
| 27.40 | categoryOfMooringArea | rule | CATACH |
| 27.41 | categoryOfNavigationLine | rule | CATNAV |
| 27.42 | categoryOfObstruction | rule | CATOBS |
| 27.43 | categoryOfOffshorePlatform | rule | CATOFP |
| 27.44 | categoryOfOffshoreProductionArea | rule | CATPRA on OffshoreProductionArea |
| 27.45 | categoryOfOilBarrier | rule | CATOLB |
| 27.46 | categoryOfOpeningBridge | rule | CATBRG |
| 27.47 | categoryOfPile | rule | CATPLE |
| 27.48 | categoryOfPilotBoardingPlace | rule | CATPIL |
| 27.49 | categoryOfPipelinePipe | rule | CATPIP |
| 27.50 | categoryOfPreference | dropped | S-101 enhancement (§27.50) |
| 27.51 | categoryOfProductionArea | rule | CATPRA |
| 27.52 | categoryOfPylon | rule | CATPYL |
| 27.53 | categoryOfRadarStation | rule | CATRAS |
| 27.54 | categoryOfRadarTransponderBeacon | rule | CATRTB |
| 27.55 | categoryOfRadioStation | rule | CATROS |
| 27.56 | categoryOfRescueStation | rule | CATRSC |
| 27.57 | categoryOfRestrictedArea | rule | CATREA |
| 27.58 | categoryOfRoad | rule | CATROD |
| 27.59 | categoryOfSchedule | dropped | S-101 enhancement (§27.59) |
| 27.60 | categoryOfSeaArea | rule | CATSEA |
| 27.61 | categoryOfShorelineConstruction | rule | CATSLC |
| 27.62 | categoryOfSignalStationTraffic | rule | CATSIT |
| 27.63 | categoryOfSignalStationWarning | rule | CATSIW |
| 27.64 | categoryOfSiloTank | rule | CATSIL |
| 27.65 | categoryOfSlope | rule | CATSLO |
| 27.66 | categoryOfSmallCraftFacility | rule | CATSCF |
| 27.67 | categoryOfSpecialPurposeMark | rule | CATSPM |
| 27.68 | categoryOfStructure | rule | INFORM text for some values |
| 27.69 | categoryOfTidalStream | rule | CAT_TS |
| 27.70 | categoryOfVegetation | rule | CATVEG |
| 27.71 | categoryOfWaterTurbulence | rule | CATWAT |
| 27.72 | categoryOfWeedKelp | rule | CATWED |
| 27.73 | categoryOfWreck | rule | CATWRK |
| 27.74 | categoryOfZoneOfConfidenceInData | rule | CATZOC |
| 27.75 | colour | rule | COLOUR |
| 27.76 | colourPattern | rule | COLPAT |
| 27.77 | communicationChannel | rule | COMCHA |
| 27.78 | condition | rule | CONDTN |
| 27.79 | contactInstructions | rule | INFORM text |
| 27.80 | dateDisused | dropped | S-101 enhancement (§27.80) |
| 27.81 | dateEnd | assembled | DATEND / PEREND / SUREND |
| 27.82 | dateFixed | dropped | S-101 enhancement (§27.82) |
| 27.83 | dateStart | assembled | DATSTA / PERSTA / SURSTA |
| 27.84 | dateVariable | dropped | S-101 enhancement (§27.84) |
| 27.85 | dayOfWeek | dropped | S-101 enhancement (§27.85) |
| 27.86 | dayOfWeekIsRange | dropped | S-101 enhancement (§27.86) |
| 27.87 | depthRangeMaximumValue | rule | DRVAL2 |
| 27.88 | depthRangeMinimumValue | rule | DRVAL1 |
| 27.89 | destination | dropped | S-101 enhancement (§27.89) |
| 27.90 | distanceMarkVisible | rule | CATDIS |
| 27.91 | distanceUnitOfMeasurement | assembled | INFORM (measuredDistanceValue) |
| 27.92 | dredgedDate | rule | SORDAT |
| 27.93 | elevation | rule | ELEVAT |
| 27.94 | estimatedRangeOfTransmission | rule | ESTRNG |
| 27.95 | exhibitionConditionOfLight | rule | EXCLIT |
| 27.96 | expositionOfSounding | rule | EXPSOU |
| 27.97 | fileLocator | dropped | S-101 enhancement (§27.97) |
| 27.98 | fileReference | assembled | TXTDSC / NTXTDS (information) |
| 27.99 | flareBearing | dropped | S-101 enhancement (§27.99) |
| 27.100 | flareStack | assembled | a LNDMRK CATLMK 6 |
| 27.101 | frequencyShoreStationReceives | dropped | S-101 enhancement (§27.101) |
| 27.102 | frequencyShoreStationTransmits | rule | SIGFRQ |
| 27.103 | function | rule | FUNCTN |
| 27.104 | headline | dropped | S-101 enhancement (§27.104) |
| 27.105 | height | rule | HEIGHT |
| 27.106 | horizontalClearanceLength | dropped | S-101 enhancement (§27.106) |
| 27.107 | horizontalClearanceValue | assembled | HORCLR |
| 27.108 | horizontalClearanceWidth | dropped | S-101 enhancement (§27.108) |
| 27.109 | horizontalLength | rule | HORLEN |
| 27.110 | horizontalWidth | rule | HORWID |
| 27.111 | iceFactor | rule | ICEFAC |
| 27.112 | iMOAdopted | rule | CATTSS |
| 27.113 | inDispute | assembled | a CTNARE "In dispute" |
| 27.114 | interoperabilityIdentifier | dropped | S-101 enhancement (§27.114) |
| 27.115 | isMRCC | rule | INFORM text |
| 27.116 | jurisdiction | rule | JRSDTN |
| 27.117 | language | dropped | S-101 enhancement (§27.117) |
| 27.118 | liftingCapacity | rule | LIFCAP |
| 27.119 | lightCharacteristic | assembled | LITCHR (rhythmOfLight, sectorCharacteristics) |
| 27.120 | lightVisibility | rule | LITVIS |
| 27.121 | linkage | dropped | S-101 enhancement (§27.121) |
| 27.122 | magneticAnomalyValue | assembled | VALLMA |
| 27.123 | majorLight | dropped | S-101 enhancement (§27.123) |
| 27.124 | marksNavigationalSystemOf | rule | MARSYS |
| 27.125 | maximumPermittedDraught | rule | INFORM text |
| 27.126 | maximumPermittedVesselLength | rule | INFORM text |
| 27.127 | measuredDistance | rule | INFORM text |
| 27.128 | minimumBerthDepth | rule | DRVAL1 |
| 27.129 | mMSICode | assembled | OBJNAM "name; MMSI n" |
| 27.130 | moireEffect | assembled | CATLIT 16 |
| 27.131 | multiplicityKnown | assembled | INFORM (multiplicityOfFeatures) |
| 27.132 | name | assembled | OBJNAM / NOBJNM (featureName) |
| 27.133 | nameOfResource | dropped | S-101 enhancement (§27.133) |
| 27.134 | nameUsage | dropped | S-101 enhancement (§27.134) |
| 27.135 | nationality | rule | NATION |
| 27.136 | natureOfConstruction | rule | NATCON |
| 27.137 | natureOfSurface | rule | NATSUR |
| 27.138 | natureOfSurfaceQualifyingTerms | assembled | NATQUA (surfaceCharacteristics) |
| 27.139 | numberOfFeatures | assembled | INFORM (multiplicityOfFeatures) |
| 27.140 | openingBridge | rule | CATBRG |
| 27.141 | orientationValue | rule | ORIENT |
| 27.142 | pictorialRepresentation | rule | PICREP |
| 27.143 | pilotMovement | dropped | S-101 enhancement (§27.143) |
| 27.144 | product | rule | PRODCT |
| 27.145 | radarBand | assembled | RADWAL |
| 27.146 | radarConspicuous | rule | CONRAD |
| 27.147 | radius | rule | RADIUS |
| 27.148 | referenceDirection | dropped | S-101 enhancement (§27.148) |
| 27.149 | referenceLocation | rule | INFORM text |
| 27.150 | referenceTide | assembled | TS_TSP HW / LW |
| 27.151 | referenceTideType | dropped | S-101 enhancement (§27.151) |
| 27.152 | referenceYearForMagneticVariation | rule | RYRMGV |
| 27.153 | regulationCitation | dropped | S-101 enhancement (§27.153) |
| 27.154 | reportedDate | rule | SORDAT |
| 27.155 | restriction | rule | RESTRN |
| 27.156 | scaleMinimum | rule | SCAMIN |
| 27.157 | sectorBearing | rule | SECTR1 |
| 27.158 | sectorLineLength | dropped | S-101 enhancement (§27.158) |
| 27.159 | signalDuration | assembled | SIGSEQ (signalSequence) |
| 27.160 | signalFrequency | rule | SIGFRQ |
| 27.161 | signalGeneration | rule | SIGGEN |
| 27.162 | signalGroup | rule | SIGGRP |
| 27.163 | signalPeriod | rule | SIGPER |
| 27.164 | signalStatus | assembled | SIGSEQ brackets (signalSequence) |
| 27.165 | speedLimit | assembled | INFORM (vesselSpeedLimit) |
| 27.166 | speedMaximum | assembled | CURVEL (speed) |
| 27.167 | speedMinimum | dropped | S-101 enhancement (§27.167) |
| 27.168 | speedUnits | assembled | INFORM (vesselSpeedLimit) |
| 27.169 | stationName | assembled | TS_TSP |
| 27.170 | stationNumber | assembled | TS_TSP |
| 27.171 | status | rule | STATUS |
| 27.172 | streamDepth | dropped | S-101 enhancement (§27.172) |
| 27.173 | sweptDate | rule | SORDAT |
| 27.174 | techniqueOfVerticalMeasurement | rule | TECSOU |
| 27.175 | telecommunicationIdentifier | dropped | S-101 enhancement (§27.175) |
| 27.176 | telecommunicationService | dropped | S-101 enhancement (§27.176) |
| 27.177 | text | assembled | INFORM / NINFOM (information) |
| 27.178 | textOffsetBearing | dropped | S-101 enhancement (§27.178) |
| 27.179 | textOffsetDistance | dropped | S-101 enhancement (§27.179) |
| 27.180 | textRotation | dropped | S-101 enhancement (§27.180) |
| 27.181 | textType | dropped | S-101 enhancement (§27.181) |
| 27.182 | timeOfDayEnd | dropped | S-101 enhancement (§27.182) |
| 27.183 | timeOfDayStart | dropped | S-101 enhancement (§27.183) |
| 27.184 | timeRelativeToTide | assembled | TS_TSP hour |
| 27.185 | topmarkDaymarkShape | rule | TOPSHP |
| 27.186 | trafficFlow | rule | TRAFIC |
| 27.187 | underlyingLayer | assembled | the / separator in NATSUR / NATQUA |
| 27.188 | valueOfAnnualChangeInMagneticVariation | rule | VALACM |
| 27.189 | valueOfDepthContour | rule | VALDCO |
| 27.190 | valueOfMagneticVariation | rule | VALMAG |
| 27.191 | valueOfMaximumRange | rule | VALMXR |
| 27.192 | valueOfNominalRange | rule | VALNMR |
| 27.193 | valueOfSounding | rule | VALSOU |
| 27.194 | verticalClearanceUnlimited | dropped | S-101 enhancement (§27.194) |
| 27.195 | verticalClearanceValue | assembled | VERCLR / VERCCL / VERCOP / VERCSA |
| 27.196 | verticalDatum | rule | VERDAT |
| 27.197 | verticalLength | rule | VERLEN |
| 27.198 | vesselClass | dropped | S-101 enhancement (§27.198) |
| 27.199 | virtualAISAidToNavigationType | assembled | NEWOBJ CLSDEF, CLSNAM, INFORM, SYMINS |
| 27.200 | visitorsMooring | assembled | SMCFAC CATSCF 29 |
| 27.201 | visualProminence | rule | CONVIS |
| 27.202 | waterLevelEffect | rule | WATLEV |
| 27.203 | waterwayDistance | assembled | INFORM (measuredDistanceValue) |
| 27.204 | waveLengthValue | assembled | RADWAL |
| 28.1 | categoryOfTemporalVariation | dropped | S-101 enhancement (§28.1) |
| 28.2 | dataAssessment | dropped | S-101 enhancement (§28.2) |
| 28.3 | drawingIndex | dropped | S-101 enhancement (§28.3) |
| 28.4 | fullSeafloorCoverageAchieved | dropped | S-101 enhancement (§28.4) |
| 28.5 | horizontalDistanceUncertainty | assembled | HORACC |
| 28.6 | leastDepthOfDetectedFeaturesMeasured | dropped | S-101 enhancement (§28.6) |
| 28.7 | lineSpacingMaximum | dropped | S-101 enhancement (§28.7) |
| 28.8 | lineSpacingMinimum | dropped | S-101 enhancement (§28.8) |
| 28.9 | maximumDisplayScale | dropped | S-101 enhancement (§28.9) |
| 28.10 | measurementDistanceMaximum | rule | SDISMX |
| 28.11 | measurementDistanceMinimum | rule | SDISMN |
| 28.12 | minimumDisplayScale | dropped | S-101 enhancement (§28.12) |
| 28.13 | optimumDisplayScale | assembled | DSPM CSCL and M_CSCL CSCALE (§3.5) |
| 28.14 | orientationUncertainty | dropped | S-101 enhancement (§28.14) |
| 28.15 | qualityOfHorizontalMeasurement | rule | QUAPOS |
| 28.16 | qualityOfVerticalMeasurement | rule | QUASOU |
| 28.17 | scaleValueMaximum | rule | SCVAL1 |
| 28.18 | scaleValueMinimum | rule | SCVAL2 |
| 28.19 | significantFeaturesDetected | dropped | S-101 enhancement (§28.19) |
| 28.20 | sizeOfFeaturesDetected | dropped | S-101 enhancement (§28.20) |
| 28.21 | source | assembled | SORIND |
| 28.22 | surveyAuthority | rule | SURATH |
| 28.23 | surveyType | rule | SURTYP |
| 28.24 | updateNumber | dropped | S-101 enhancement (§28.24) |
| 28.25 | updateType | dropped | S-101 enhancement (§28.25) |
| 28.26 | uncertaintyFixed | assembled | POSACC / SOUACC / VERACC |
| 28.27 | uncertaintyVariableFactor | dropped | no depth or length to scale it by on the feature (§28.27) |
| 30.1 | defaultClearanceDepth | dropped | portrayal only |
| 30.2 | inTheWater | structural | a PILPNT where a structure stands in the water |
| 30.3 | sectorArcExtension | dropped | portrayal only |
| 30.4 | surroundingDepth | dropped | portrayal only |

</details>

<details>
<summary>Complex attributes (§29): 42 clauses</summary>

| Clause | S-101 | Treatment | S-57 |
|---|---|---|---|
| 29.1 | directionalCharacter | assembled | CATLIT 1 (and 16 for a moiré effect), ORIENT |
| 29.2 | featureName | assembled | OBJNAM / NOBJNM by name usage and language |
| 29.3 | featuresDetected | dropped | S-101 enhancement |
| 29.4 | fixedDateRange | assembled | DATSTA / DATEND |
| 29.5 | frequencyPair | assembled | SIGFRQ |
| 29.6 | horizontalClearanceFixed | assembled | HORCLR, HORACC |
| 29.7 | horizontalClearanceOpen | assembled | HORCLR, HORACC |
| 29.8 | horizontalPositionUncertainty | assembled | POSACC |
| 29.9 | information | assembled | INFORM / NINFOM, TXTDSC / NTXTDS by language |
| 29.10 | lightSector | assembled | LIGHTS, one per light sector |
| 29.11 | measuredDistanceValue | assembled | INFORM "n units" |
| 29.12 | multiplicityOfFeatures | assembled | INFORM "n features" or "Multiple features" |
| 29.13 | onlineResource | dropped | S-101 enhancement |
| 29.14 | orientation | assembled | ORIENT |
| 29.15 | periodicDateRange | assembled | PERSTA / PEREND |
| 29.16 | radarWaveLength | assembled | RADWAL "wavelength-band" pairs |
| 29.17 | rhythmOfLight | assembled | LITCHR, SIGGRP, SIGPER, SIGSEQ |
| 29.18 | scheduleByDayOfWeek | dropped | S-101 enhancement |
| 29.19 | sectorCharacteristics | assembled | LIGHTS, one per light sector |
| 29.20 | sectorInformation | assembled | INFORM / NINFOM on the sector's LIGHTS |
| 29.21 | sectorLimit | assembled | SECTR1 / SECTR2 |
| 29.22 | sectorLimitOne | assembled | SECTR1 |
| 29.23 | sectorLimitTwo | assembled | SECTR2 |
| 29.24 | shapeInformation | assembled | INFORM / NINFOM on the TOPMAR |
| 29.25 | signalSequence | assembled | SIGSEQ, phases joined by +, eclipses in brackets |
| 29.26 | spatialAccuracy | dropped | on spatial records, which the S-101 reader doesn't associate with Spatial Quality (§24.5) |
| 29.27 | speed | assembled | CURVEL |
| 29.28 | surfaceCharacteristics | assembled | positional NATSUR / NATQUA, / before an underlying layer |
| 29.29 | surveyDateRange | assembled | SURSTA / SUREND |
| 29.30 | telecommunications | dropped | S-101 enhancement |
| 29.31 | tidalStreamPanelValues | assembled | TS_TSP |
| 29.32 | tidalStreamValue | assembled | TS_TSP direction and rate per hour |
| 29.33 | timeIntervalsByDayOfWeek | dropped | S-101 enhancement |
| 29.34 | topmark | assembled | TOPMAR, slave of the structure |
| 29.35 | valueOfLocalMagneticAnomaly | assembled | VALLMA, degrees to minutes |
| 29.36 | verticalClearanceClosed | assembled | VERCCL, VERACC |
| 29.37 | verticalClearanceFixed | assembled | VERCLR, VERACC |
| 29.38 | verticalClearanceOpen | assembled | VERCOP, VERACC |
| 29.39 | verticalClearanceSafe | assembled | VERCSA, VERACC |
| 29.40 | verticalUncertainty | assembled | SOUACC, or VERACC on a clearance |
| 29.41 | vesselSpeedLimit | assembled | INFORM "Speed limit is n units", first instance |
| 29.42 | zoneOfConfidence | assembled | CATZOC, POSACC, SOUACC |

</details>

<!-- annex-c-coverage:end -->

## See also

- [Bringing S-57 into the pipeline](../../docs/s57-to-s101.md): convert, view
  and validate S-57 cells with `s100` and SoundCharts.
- [S-401 bundled catalogues](../EncDotNet.S100.Specifications/content/S401/README.md):
  the catalogues inland cells are translated for.

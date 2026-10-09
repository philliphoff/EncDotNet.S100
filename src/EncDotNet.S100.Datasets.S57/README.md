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
| `INFORM` / `TXTDSC` (English) | Carried on a shared `NauticalInformation` **information type** (not on the feature) as a single `information` complex instance with `text` and/or `fileReference` and `language = "eng"`; the feature binds it via an `AdditionalInformation` / `theInformation` information association. |
| `NINFOM` / `NTXTDS` (national language) | Carried as a second `information` instance on the same `NauticalInformation` record with empty `language` (S-57 has no language tag; Data Producers can populate it post-conversion). |
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
| `SORDAT` (source date) | Mapped to the top-level `reportedDate` simple attribute (an `S100_TruncatedDate`), the S-57 `YYYYMMDD` value carried verbatim (matching the fidelity of `dateStart`/`dateEnd`). Because `SORDAT` is a near-universal S-57 attribute but the FC binds `reportedDate` on only ~50 feature classes, the mapping is gated on the resolved feature actually binding `reportedDate`; on a feature that does not (e.g. `Coastline`, `SeabedArea`, `LandRegion`, `DepthContour`, the light classes) `SORDAT` has no conformant home and stays unmapped. `SORIND` (source indication) has no general S-101 equivalent (the FC's `source` attribute binds only `UpdateInformation`) and is intentionally not mapped. |
| `VALLMA` (value of local magnetic anomaly) | Assembled into the `valueOfLocalMagneticAnomaly` complex on `LocalMagneticAnomaly` (`LOCMAG`, the only feature that binds it). The value feeds the mandatory `magneticAnomalyValue` [1..1] real sub-attribute verbatim; the optional `referenceDirection` enum has no S-57 source and is left unpopulated. |
| `RADWAL` (radar wave length) | Assembled into the `radarWaveLength` complex on `RadarTransponderBeacon` (`RTPBCN`, binding it [0..2]). The list-typed S-57 value is a set of `value-band` pairs (e.g. `0.03-X` or `0.03-X,0.10-S`); each pair yields one complex instance carrying the mandatory `waveLengthValue` (real) and `radarBand` (text). A pair that does not split into both parts is dropped. |
| `CURVEL` (current velocity) | Assembled into the `speed` complex on `CurrentNonGravitational` (`CURENT`) and `TidalStreamFloodEbb` (`TS_FEB`). The value feeds the mandatory `speedMaximum` [1..1] real sub-attribute verbatim; the optional `speedMinimum` has no S-57 source and is left unpopulated. |
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
  mapped.
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
- **Textual information.** `INFORM`, `TXTDSC`, `NINFOM` and `NTXTDS` become a
  `NauticalInformation` information type linked to the feature by an
  `AdditionalInformation` / `theInformation` association (the "fuller path" in
  the conversion guidance), not an inline `information` complex. A feature with
  any of the four attributes gets one `NauticalInformation` record, with an
  English instance, a national-language instance, or both.
  `NauticalInformationTypesEmitted` counts the records. The S-101 portrayal
  reads the linked record (`ProcessNauticalInformation`), so text and picture
  notes render as they did with the inline form.
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

## See also

- [Bringing S-57 into the pipeline](../../docs/s57-to-s101.md): convert, view
  and validate S-57 cells with `s100` and SoundCharts.
- [S-401 bundled catalogues](../EncDotNet.S100.Specifications/content/S401/README.md):
  the catalogues inland cells are translated for.

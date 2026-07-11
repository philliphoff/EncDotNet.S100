# EncDotNet.S100.Datasets.S57

Reader and S-101 translator for IHO S-57 Electronic Navigational Chart (ENC) datasets.

## Overview

This library reads S-57 (Edition 3.1) ENC base cells via the
[`EncDotNet.S57`](https://www.nuget.org/packages/EncDotNet.S57) NuGet package
(itself layered on top of `EncDotNet.Iso8211`) and translates the parsed
records into the S-101 in-memory document model so the existing S-100 Part 9A
Lua portrayal pipeline (provided by `EncDotNet.S100.Datasets.S101`) can render
them. This is **not** an S-52 implementation — symbology is whatever the S-101
portrayal catalogue produces when fed the translated data.

Mappings follow the IHO *S-57 to S-101 Conversion Guidance* (S-101PT6 INF02A, draft 2021).

Key types:

- **`S57Dataset`** — entry point; opens a `.000` file and exposes the parsed
  `EncDotNet.S57.S57Document` from the upstream package, plus a static
  `IsS57File(path)` discriminator used by `EncDotNet.S100.Datasets.Pipelines`
  to disambiguate `.000` files that could otherwise be S-101.
- **`S57ToS101Translator`** — translates an `S57Document` (package type) into
  an `S101Document` by remapping object/attribute codes, exploding multi-point
  soundings, synthesising the `information` complex attribute from textual
  fields, and converting nodes/edges/area-rings into S-101 spatial primitives.
- **`S57S101Mapping`** — embedded code-mapping table sourced from IHO's S-57 →
  S-101 conversion guidance.
- **`S101AllowedEnumValues`** — lazy-loaded helper that consults the bundled
  S-101 Feature Catalogue to drop emitted enumerated attribute values that
  aren't permitted by the destination FC binding.

## Translation behaviour

| Aspect | Notes |
|---|---|
| Object/attribute codes | Looked up via the embedded `S57S101Mapping` rules (S-57 numeric code → S-101 acronym/code). Unknown S-57 attribute codes pass through; unknown enumerated values are dropped if the S-101 FC declares an allowable list. |
| Multi-point soundings (`SOUNDG`) | Exploded into S-101 `MultiPoint` spatial records and a `Sounding` feature so each depth value is independently portrayable. |
| `INFORM` / `TXTDSC` (English) | Carried as a single S-101 `information` complex attribute instance with `text` and/or `fileReference` and `language = "eng"`. |
| `NINFOM` / `NTXTDS` (national language) | Carried as a separate `information` instance with empty `language` (S-57 has no language tag; Data Producers can populate it post-conversion). |
| `OBJNAM` (English) | Carried as an S-101 `featureName` complex attribute instance with `name` and `language = "eng"`. |
| `NOBJNM` (national language) | Carried as a separate `featureName` instance with empty `language`. |
| `LITCHR` / `SIGGRP` / `SIGPER` (light features) | On feature classes that bind it (`LightAllAround`, `LightFogDetector`, `LightAirObstruction`), assembled into a single `rhythmOfLight` complex attribute instance: `lightCharacteristic` (mandatory), plus `signalGroup` / `signalPeriod` when present, and any `SIGSEQ`-derived `signalSequence` phases nested inside (see below). An out-of-range `LITCHR` code drops the instance (its mandatory sub-attribute would be missing). On non-light features (`FogSignal`, `RadarTransponderBeacon`) `SIGGRP` / `SIGPER` remain directly-bound simple attributes. |
| `DATSTA` / `DATEND` (date ranges) | On feature classes that bind it, assembled into a `fixedDateRange` complex attribute instance with `dateStart` and/or `dateEnd` (both optional). |
| `PERSTA` / `PEREND` (periodic date ranges) | On feature classes that bind it, assembled into a `periodicDateRange` complex attribute instance. Both `dateStart` and `dateEnd` are mandatory in the S-101 FC, so the instance is emitted only when both endpoints are present; otherwise the lone endpoint is dropped (and reported by the translation diagnostics). |
| `SURSTA` / `SUREND` (survey date ranges) | On feature classes that bind it (`QualityOfBathymetricData`, `QualityOfNonBathymetricData`, `QualityOfSurvey`), assembled into a `surveyDateRange` complex attribute instance. `dateEnd` (`SUREND`) is mandatory, so the instance is emitted only when it is present; `dateStart` (`SURSTA`) is optional. |
| `CATZOC` (zone of confidence) | On `QualityOfBathymetricData` (the sole feature class binding the complex), carried as the `categoryOfZoneOfConfidenceInData` sub-attribute of a `zoneOfConfidence` complex attribute instance. The S-57 and S-101 enumerations are identical (1=A1, 2=A2, 3=B, 4=C, 5=D, 6=U); an out-of-range code drops the instance. The complex's other sub-attributes (`fixedDateRange`, `horizontalPositionUncertainty`, `verticalUncertainty`) have no `CATZOC`-side source and are left unpopulated. |
| `CATPRA` (category of production area) | Feature-dependent. On `ProductionStorageArea` (`PRDARE`) it passes through to `categoryOfProductionArea`, whose enumeration shares codes 1–12 with S-57 `CATPRA`. On `OffshoreProductionArea` (`OSPARE`) the FC binds the *distinct* `categoryOfOffshoreProductionArea` enumeration, so the value is redirected and remapped (8 Tank Farm → 4, 9 Wind Farm → 1, 12 Solar Farm → 6); S-57 production categories with no offshore equivalent (quarry, mine, stockpile, power station, refinery, timber yard, factory, slag/spoil, production plant) are dropped. |
| `NATSUR` / `NATQUA` (seabed) | On `SeabedArea` (`SBDARE`, the sole feature class binding the complex), assembled into one or more `surfaceCharacteristics` complex attribute instances. The two S-57 lists are paired positionally: position *i* yields an instance carrying `natureOfSurface` = `NATSUR[i]` and `natureOfSurfaceQualifyingTerms` = `NATQUA[i]`. Both sub-attributes are optional, so a position with only one populated (e.g. `NATQUA` without `NATSUR`, the most common corpus case) still forms a valid instance; out-of-range enumerate codes are dropped individually. `SeabedArea` does **not** bind a top-level `natureOfSurface`, so on that feature `NATSUR` is routed exclusively into the complex; on other feature classes (`Coastline`, `LandRegion`, …) `NATSUR` remains a directly-bound simple `natureOfSurface`. |
| `SIGSEQ` (signal sequence) | Parsed into `signalSequence` complex attribute instances. The S-57 value is a `+`-separated list of phase durations in seconds where a parenthesised duration denotes an eclipse/silence phase; each phase becomes one instance carrying `signalDuration` (real seconds) and `signalStatus` (`1` = Lit/Sound for a bare duration, `2` = Eclipsed/Silent for a parenthesised one). On the light feature classes that bind `rhythmOfLight` the phases are **nested** inside that complex (its last sub-attribute per the FC), and are therefore emitted only when a `rhythmOfLight` instance is present (a valid `LITCHR`); on `FogSignal` / `RadarTransponderBeacon` `signalSequence` binds at the top level. Phases that do not parse as a real duration are dropped and reported. |
| Sector lights (`SECTR1` / `SECTR2` / `COLOUR` / `VALNMR` / `LITVIS` / `LITCHR` / `SIGGRP` / `SIGPER` / `SIGSEQ`) | A `LIGHTS` object carrying a sector arc (`SECTR1`/`SECTR2` present) is redirected from `LightAllAround` to **`LightSectored`**, and its light attributes are assembled into the mandatory `sectorCharacteristics` [1..*] complex: `lightCharacteristic` (from `LITCHR`, mandatory) plus optional `signalGroup`/`signalPeriod`/`signalSequence`, and one `lightSector` carrying `colour` (from the `COLOUR` list), `valueOfNominalRange` (`VALNMR`), `lightVisibility` (from the `LITVIS` list), and a `sectorLimit` whose `sectorLimitOne`/`sectorLimitTwo` `sectorBearing` come from `SECTR1`/`SECTR2` (three levels of nesting). Since each S-57 `LIGHTS` object encodes a single sector, one `LightSectored` feature is emitted per S-57 sector-light with one `lightSector` (conformant, as `lightSector` is [1..*]); co-located sectors of one physical light remain distinct features. `LightSectored` binds none of these attributes at the top level, so all are diverted into the complex and none pass through as flat simple attributes. |
| `HORCLR` (horizontal clearance) | Assembled into the mandatory `horizontalClearanceValue` [1..1] sub-attribute of a horizontal-clearance complex, selected per feature by the S-101 FC binding: `horizontalClearanceOpen` on `Gate` (`GATCON`) or `horizontalClearanceFixed` on the fixed-span classes that bind it (`SpanFixed`, `SpanOpening`, `Tunnel`, `ShorelineConstruction`, `StructureOverNavigableWater`, `Canal`, `DockArea`, `LockBasin`). The value is a real carried verbatim; the optional `horizontalDistanceUncertainty` has no S-57 source and is left unpopulated. On a feature binding neither complex (e.g. `Bridge`, which S-101 decomposes into spans that carry the clearance) `HORCLR` has no conformant home and stays unmapped. |
| Spatial relationships | S-57 vector pointer records (`VRPT`, `FSPT`) translated to S-101 spatial associations and ring orientation. |
| List-valued enumerations (`COLOUR`, `NATSUR`, `CATLIT`, …) | S-57 encodes multiple enumerate selections as a comma-separated string (e.g. `COLOUR = "3,1"`). Each code is split into a separate S-101 attribute occurrence (distinct `ATIX`) and validated independently, so one invalid code no longer discards the whole attribute. |

## Limitations

- **Base cells only.** Update files (`.001`, `.002`, …) are not applied; the reader rejects them with a clear error.
- **Breadth-first feature coverage.** Feature-class coverage is derived from the bundled S-101 Feature Catalogue's `<S100FC:alias>` (S-57 acronym) bridge and validated against a 3,636-cell NOAA ENC corpus: ~99% of feature instances now translate. The classes that remain unmapped are ones that need conditional splits (`MORFAC` by `CATMOR`), become S-101 *attributes* rather than features (`TOPMAR`), are aggregation/collection objects (`C_AGGR`, `C_ASSO`), or are meta objects with no S-101 equivalent (`M_CSCL`).
- **Simple attributes plus selected complex attributes.** Most attribute rules cover S-57 attributes that map to a *directly feature-bound simple* S-101 attribute. The `information` (`INFORM`/`NINFOM`/`TXTDSC`/`NTXTDS`), `featureName` (`OBJNAM`/`NOBJNM`), `rhythmOfLight` (`LITCHR`/`SIGGRP`/`SIGPER`) with its **nested** `signalSequence` (`SIGSEQ`), the three date-range complex attributes — `fixedDateRange` (`DATSTA`/`DATEND`), `periodicDateRange` (`PERSTA`/`PEREND`), and `surveyDateRange` (`SURSTA`/`SUREND`) — `zoneOfConfidence` (`CATZOC`), `surfaceCharacteristics` (`NATSUR`/`NATQUA`), the three-level `sectorCharacteristics`/`lightSector`/`sectorLimit` sector geometry on `LightSectored` (`SECTR1`/`SECTR2`/`COLOUR`/`VALNMR`/`LITVIS` plus the light-characteristic attributes), and the `horizontalClearanceOpen`/`horizontalClearanceFixed` complexes (`HORCLR`) are assembled, gated on the S-101 Feature Catalogue's per-feature attribute bindings. `signalSequence` also binds at the top level on `FogSignal` / `RadarTransponderBeacon`. Assembled complex sub-attributes are validated against the destination simple attribute's *global* enumeration rather than the (sometimes narrower) per-complex `permittedValues` subset, matching the fidelity of the rest of the pipeline. Attributes that are sub-attributes of the *remaining* S-101 **complex** attributes — chiefly the nested uncertainty complexes of `zoneOfConfidence` — are still left unmapped pending further complex-attribute assembly. `SORDAT`→`reportedDate` is deferred (it binds to only a subset of feature types and the translator has no per-feature binding filter); `SORIND` has no general S-101 equivalent and is intentionally not mapped.
- **Listed-value remapping is best-effort.** Some S-57 enumerated attribute values have different numeric codes in S-101; values that aren't permitted by the destination S-101 FC binding are dropped (and reported by the translation diagnostics). Multi-valued (list-type) S-57 enumerations are split into individual S-101 occurrences first, so only the specific invalid codes are dropped rather than the whole attribute.
- **Complex-attribute synthesis covers the common cases.** The most frequent S-101 complex attributes are assembled (see the table above), including sectored-light geometry (`LightSectored`). One S-101 feature is emitted per S-57 sector-light, so co-located sectors of a single physical light are not merged into one multi-sector feature; the nested uncertainty complexes of `zoneOfConfidence` are also not yet populated.
- **`information` is emitted directly on the feature** (per the "generally" path in the conversion guidance) rather than via a separate `NauticalInformation` information type with an `Additional Information` association.

## Translation diagnostics

`S57ToS101Translator.Translate` has an optional overload that accepts an
`S57TranslationDiagnostics` sink. When supplied, the translator records — as
compact aggregate counters — exactly what it dropped and why, so a caller (for
example a corpus-wide conversion audit) can quantify coverage gaps in the
embedded `S57S101Mapping` without re-deriving the translator's logic:

```csharp
var diagnostics = new S57TranslationDiagnostics();
var s101 = new S57ToS101Translator().Translate(s57, diagnostics);

// Object classes present in the data with no mapping rule (a coverage gap):
foreach (var (objl, count) in diagnostics.UnmappedObjectClasses) { /* … */ }
```

Passing `null` (or using the parameterless `Translate` overloads) disables
collection with zero overhead. The counters distinguish a genuine **gap** (no
rule — `UnmappedObjectClasses` / `UnmappedAttributes`) from a **by-design drop**
(a rule exists but resolves to nothing — `RuleDroppedObjectClasses` /
`RuleDroppedAttributes`), and separately report FC-rejected enumerate values
(`DroppedEnumValues`), geometry loss (`FeaturesDroppedForNoGeometry`), and
sounding point accounting.

## Validation

`S57DatasetProcessor.Validate()` runs validation in two phases:

1. **Pre-translation** — `S57PreTranslationRules.Default` checks the
   raw `EncDotNet.S57.S57Document` for things that don't survive
   translation (DSID / DSPM presence and a positive compilation scale;
   the presence of an `M_COVR` meta-feature).
2. **Post-translation** — the translated S-101 document is fed into
   `S101DatasetRules.Default` (the same pack that runs against native
   S-101 datasets) and the resulting findings are **rebadged** with
   the prefix `S101-as-S57/` so the user can tell whether a problem
   originated in the raw S-57 input or in the translated S-101
   projection.

Pre-translation rules shipped by `S57PreTranslationRules.Default`:

| Rule id            | Severity | Checks                                                                              |
|--------------------|----------|-------------------------------------------------------------------------------------|
| `S57-R-1.1`        | Error    | DSID record present AND DSPM compilation scale denominator (CSCL) > 0.              |
| `S57-R-1.2`        | Warning  | At least one `M_COVR` meta-feature is present in the cell.                          |
| `S57-PROJ-PARSE`   | —        | Placeholder reserving the namespace for future parser-diagnostic findings.          |

The two reports are concatenated by `ConcatReports.Concat(pre, post,
rebadgePrefix: "S101-as-S57/")` in
[`EncDotNet.S100.Datasets.Pipelines`](../EncDotNet.S100.Datasets.Pipelines/README.md)
and cached on the processor.

## Exchange-set integrity verification

`S57ExchangeSetVerification` integrity-verifies an S-57 / S-63 exchange
set (a folder containing a `CATALOG.031`) and **maps the result onto the
same S-100 [`ExchangeSetVerificationResult`](../EncDotNet.S100.ExchangeSets/README.md)
model** used for native S-100 exchange sets, so both products surface
uniformly through the CLI (`s100 validate`) and any future viewer wiring.

```csharp
using EncDotNet.S100.Datasets.S57;
using EncDotNet.S100.ExchangeSets;

ExchangeSetVerificationResult result =
    await S57ExchangeSetVerification.VerifyAsync("/path/to/s57set");

bool integrity = result.IntegrityVerified;   // no CRC mismatches / missing files
bool signed    = result.AllValid;            // strict: every file signature Ok
```

It is a deliberately thin **adapter**, not a shared interface: the
upstream S-57 verifier (`EncDotNet.S57` 0.5.0+) is rooted at a directory
path (`CATALOG.031` plus the files it references), whereas the S-100
verifier is `IAssetSource`-based. Forcing both behind one interface would
distort both, so only the *result* is mapped.

Mapping notes:

- Outcomes are mapped **by name** (`S57VerificationOutcome` →
  [`VerificationOutcome`](../EncDotNet.S100.ExchangeSets/README.md)). The
  two enums are kept byte-identical; mapping by name means a future
  divergence fails loudly rather than silently mis-mapping.
- Both the **signature** dimension (`Outcome`) and the **checksum**
  dimension (`ChecksumOutcome`) are preserved independently.
- S-57 integrity uses a CRC-32 (the `CATALOG.031` field), not the SHA-256
  digest the S-100 model carries, so the per-file CRC values are surfaced
  through `FileVerificationResult.Detail` and `ComputedSha256` is left
  `null`.
- The verdict semantics match S-100: `NoChecksum` / `NotSigned` are
  **non-failing** (an unsigned-but-intact set has `IntegrityVerified ==
  true` but `AllValid == false`); only `ChecksumMismatch` / `FileMissing`
  / `Error` / invalid signatures fail integrity. This is identical to the
  upstream S-57 `AllValid`.
- Today the upstream S-57 verifier reports every file as `NotSigned`
  (S-63 signature verification is a seam); CRC checking is fully wired.

## Exchange-set cell enumeration

`S57ExchangeSetCatalog` is the loading-side companion to the verification
adapter above. It reads a `CATALOG.031` (via `EncDotNet.S57`'s
`S57CatalogReader`) and groups the catalogued `CATD` files into renderable
**base cells**, each with its in-set sequential updates and (optionally) its
extent:

```csharp
using EncDotNet.S100.Datasets.S57;

IReadOnlyList<S57ExchangeSetCell> cells =
    S57ExchangeSetCatalog.ReadBaseCells("/path/to/s57set");

foreach (S57ExchangeSetCell cell in cells)
{
    // cell.RelativePath          → "…/US5MA1BO.000" (platform-normalised)
    // cell.UpdateRelativePaths   → ["…/US5MA1BO.001", "…/US5MA1BO.002"]
    // cell.BoundingBox           → EPSG:4326 extent, or null
}

BoundingBox? union = S57ExchangeSetCatalog.UnionBoundingBox(cells);
```

Files are grouped by their 8-character cell name; the `.000` file is the base
and `.001`, `.002`, … are its updates in application order. Non-dataset
entries (`.TXT`, certificates, the catalogue itself) are ignored, and update
files with no matching base are skipped. The pure grouping logic is exposed as
`SelectBaseCells(S57Catalog)` for unit testing without a real catalogue on
disk. The viewer pairs this enumeration with a `FileSystemAssetSource` rooted
at the exchange-set directory so each cell flows through the same
`S57DatasetProcessor` code path as a single dropped `.000` file (with its
in-set updates folded in via `S57Document.ApplyChanges` before translation to
S-101).

Like the verification adapter, this is a deliberately thin adapter over the
directory-rooted S-57 model rather than a shared interface.
## Usage

```csharp
using EncDotNet.S100.Datasets.S57;
using EncDotNet.S100.Datasets.S101;

var s57 = S57Dataset.Open("US5NY16M.000");
var s101Document = new S57ToS101Translator().Translate(s57);
var dataset = S101Dataset.FromDocument(s101Document);
// Use the existing S-101 pipeline from here:
// S101LuaRuleExecutor, FeatureGeometryProvider&lt;Feature&gt;, etc.
```

## Installation

```sh
dotnet add package EncDotNet.S100.Datasets.S57
```

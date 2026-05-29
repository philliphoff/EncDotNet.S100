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
| Spatial relationships | S-57 vector pointer records (`VRPT`, `FSPT`) translated to S-101 spatial associations and ring orientation. |

## Limitations

- **Base cells only.** Update files (`.001`, `.002`, …) are not applied; the reader rejects them with a clear error.
- **Breadth-first feature coverage.** Most common feature classes map to S-101 acronyms; uncommon classes pass through unmapped and may not portray.
- **Listed-value remapping is best-effort.** Some S-57 enumerated attribute values have different numeric codes in S-101; values that aren't permitted by the destination S-101 FC binding are silently dropped.
- **Complex-attribute synthesis is minimal.** Sector lights and similar features that require S-101 nested complex attributes may not portray correctly.
- **`information` is emitted directly on the feature** (per the "generally" path in the conversion guidance) rather than via a separate `NauticalInformation` information type with an `Additional Information` association.

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

## Usage

```csharp
using EncDotNet.S100.Datasets.S57;
using EncDotNet.S100.Datasets.S101;

var s57 = S57Dataset.Open("US5NY16M.000");
var s101Document = new S57ToS101Translator().Translate(s57);
var dataset = S101Dataset.FromDocument(s101Document);
// Use the existing S-101 pipeline from here:
// S101LuaRuleExecutor, S101FeatureGeometryProvider, etc.
```

## Installation

```sh
dotnet add package EncDotNet.S100.Datasets.S57
```

# EncDotNet.S100.Datasets.S102

Reader and coverage portrayal pipeline for S-102 Bathymetric Surface datasets.

## Overview

This library reads S-102 datasets from HDF5 files and provides coverage data (depth and uncertainty grids) for the portrayal pipeline. Key types include:

- **`S102Dataset`** — root model containing horizontal CRS and bathymetric coverages.
- **`S102DatasetReader`** — reads an S-102 dataset from an `IHdf5File`.
- **`S102CoverageSource`** — `ICoverageSource` adapter for the coverage pipeline.
- **`S102PortrayalCatalogue`** — coverage portrayal catalogue for depth shading.
- **`BathymetryCoverage`**, **`BathymetryValue`** — bathymetric data models.

## Portrayal

Portrayal is **PC-driven**: `S102PortrayalCatalogue` executes the
bundled `BathymetryCoverage.lua` rule (S-102 Edition 3.0.0, Annex B)
through an `ILuaEngine` and resolves the emitted `CoverageColor`
tokens (`DEPDW`, `DEPMD`, `DEPMS`, `DEPVS`, `DEPIT`, `NODTA`) against
the bundled `ColorProfiles/colorProfile.xml`. The Day, Dusk, and
Night palettes are all loaded; `SwitchPalette` selects the active
mood.

The four S-102 context parameters flow in via `MarinerSettings`:

- `FourShades` — toggle between two-band (DEPVS / DEPDW split at
  `SafetyContour`) and four-band (DEPVS / DEPMS / DEPMD / DEPDW split
  at `ShallowContour` / `SafetyContour` / `DeepContour`) shading.
- `SafetyContour`, `ShallowContour`, `DeepContour` — depth boundaries
  in metres.

Invariants (`ShallowContour ≤ SafetyContour ≤ DeepContour`) are
clamped (with a diagnostic) rather than throwing — the viewer
settings panel surface area means transient out-of-order slider
values must not crash the render pipeline.

Cells whose depth equals `S102CoverageSource.FillValue` (1,000,000f)
are painted with the active palette's `NODTA` colour via
`CoverageColorScheme.NoDataColor`.

## Validation

A bundled rule pack
(`EncDotNet.S100.Datasets.S102.Validation.S102DatasetRules.Default`)
evaluates a typed `S102Dataset` against the S-102 Edition 3.0.0
checklist and emits a `ValidationReport` of findings. The pack is
invoked automatically by `S102DatasetProcessor.Validate()` and is
also runnable directly:

```csharp
var report = S102DatasetRules.Default.Run(dataset);
foreach (var finding in report.Findings)
    Console.WriteLine($"{finding.RuleId} {finding.Severity}: {finding.Message}");
```

| Rule id              | Severity | Checks                                                                                                                   |
|----------------------|----------|--------------------------------------------------------------------------------------------------------------------------|
| `S102-R-1.1`         | Error    | Each coverage's `Values.Length` equals `NumPointsLatitudinal × NumPointsLongitudinal`.                                   |
| `S102-R-2.1`         | Error    | NODATA fill in `Depth`/`Uncertainty` is exactly `1_000_000f`; flags `NaN` / ±`Infinity` as illegal sentinel substitutes. |
| `S102-R-3.1`         | Warning  | `HorizontalCRS`, when set, is a recognised EPSG code (4326, 4269, or WGS-84 UTM band).                                   |
| `S102-R-3.2`         | Warning  | `IssueDate`, when set, parses as ISO 8601.                                                                               |
| `S102-R-4.1`         | Error    | Each coverage's `OriginLatitude` ∈ [-90, 90] and `OriginLongitude` ∈ [-180, 180].                                        |
| `S102-R-4.2`         | Error    | Each coverage's extent stays inside WGS-84 bounds and does not wrap the antimeridian.                                    |
| `S102-R-5.1`         | Warning  | Non-NODATA depth values fall within [-50, 12 000] m (one finding per offending coverage).                                |
| `S102-PROJ-SCHEMA`   | Error    | Defensive surrogate: emitted when the underlying HDF5 dataset fails schema-level parsing inside `Validate()`.            |

## Installation

```sh
dotnet add package EncDotNet.S100.Datasets.S102
```

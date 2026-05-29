# EncDotNet.S100.Datasets.S104

Reader and coverage pipeline for S-104 Water Level Information for Surface Navigation datasets.

## Overview

This library reads S-104 datasets from HDF5 files and provides time-series water level height and trend grids for the portrayal pipeline. Key types include:

- **`S104Dataset`** — root model containing horizontal CRS, data coding format, and time-step coverages.
- **`S104DatasetReader`** — reads an S-104 dataset from an `IHdf5File` (regular grid format).
- **`S104CoverageSource`** — `ICoverageSource` adapter for the coverage pipeline.
- **`S104PortrayalCatalogue`** — viewer-parity heatmap catalogue with hand-coded Day / Dusk / Night band tables (see *Portrayal* below).
- **`WaterLevelCoverage`**, **`WaterLevelValue`** — water level data models. `WaterLevelCoverage.GroupPath` carries the HDF5 instance path (e.g. `/WaterLevel/WaterLevel.01`) and is used by the validation rule pack as the per-coverage `RelatedFeatureId`.

## Portrayal

S-104 Edition 2.0.0 **does not define an official portrayal catalogue** — the spec
treats water-level data as input to ECDIS depth adjustment rather than a visual
layer. `S104PortrayalCatalogue` therefore ships **hand-coded** Day / Dusk / Night
band tables synthesised for viewer parity with the other coverage products
(S-102, S-111):

| Palette | Band styling                                                                                  | NoData fill                |
|---------|-----------------------------------------------------------------------------------------------|----------------------------|
| Day     | ColorBrewer-style diverging blue (below datum) → green (above datum), preserved byte-for-byte | transparent (`#00000000`)  |
| Dusk    | Day with saturation × 0.70 and lightness × 0.85                                               | dim cool grey (`#4A4A4AFF`)|
| Night   | ECDIS night-mode dark navy / olive, all luminance < 0.2                                       | darker dim grey (`#1A1A1AFF`)|

`SwitchPalette(PaletteType)` actually swaps the active band table (the pre-PR-H
implementation was a no-op). `ResolveColorScheme` populates
`CoverageColorScheme.NoDataColor` so the renderer paints S-104 fill cells
(`S104CoverageSource.FillValue`, `-9999f`) with the active palette's no-data
colour rather than leaving them transparent.

If IHO publishes an official S-104 portrayal catalogue, the bundled
`content/S104/pc/` directory (today `.gitkeep`-only by design) will be the
landing point and this catalogue will be re-wired against it.

## Validation

A bundled rule pack
(`EncDotNet.S100.Datasets.S104.Validation.S104DatasetRules.Default`)
evaluates a typed `S104Dataset` against the S-104 Edition 2.0.0
checklist and emits a `ValidationReport` of findings. The pack is
invoked automatically by `S104DatasetProcessor.Validate()` and is
also runnable directly:

```csharp
var report = S104DatasetRules.Default.Run(dataset);
foreach (var finding in report.Findings)
    Console.WriteLine($"{finding.RuleId} {finding.Severity}: {finding.Message}");
```

| Rule id                  | Severity | Checks                                                                                                                  |
|--------------------------|----------|-------------------------------------------------------------------------------------------------------------------------|
| `S104-R-1.1`             | Error    | Each coverage's `Values.Length` equals `NumPointsLatitudinal × NumPointsLongitudinal`.                                  |
| `S104-R-1.2`             | Error    | `DataCodingFormat` is in the supported gridded set `{2, 3}`.                                                            |
| `S104-R-2.1`             | Warning  | `Coverages` are strictly increasing by `TimePoint` (one finding at first violation; cascade suppression).               |
| `S104-R-2.2`             | Warning  | Successive `TimePoint` deltas vary by no more than ±10% of the median delta (skipped when `Coverages.Count < 3`).       |
| `S104-R-3.1`             | Warning  | `MethodWaterLevelProduct` is set when `Coverages.Count > 1`.                                                            |
| `S104-R-4.1`             | Warning  | Non-NODATA water-level values lie in `[-15, 15]` m (one finding per offending coverage; `-9999f`/NaN/±Infinity skipped).|
| `S104-R-4.2`             | Error    | Each coverage's origin and `origin + (numPoints - 1) × spacing` extent stay in WGS-84 ranges without antimeridian wrap. |
| `S104-PROJ-SCHEMA`       | Error    | Defensive surrogate: emitted when the underlying HDF5 dataset fails schema-level parsing inside `Validate()`.           |
| `S104-PROJ-UNSUPPORTED`  | Error    | Emitted when the loaded dataset uses an unsupported data coding format (e.g. dcf 8 station series).                     |

R-2.1 and R-2.2 are the **time-axis rule patterns**; they are the
template the S-111 (V-3) rule pack reuses against
`SurfaceCurrentCoverage`.

## Installation

```sh
dotnet add package EncDotNet.S100.Datasets.S104
```

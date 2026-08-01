# EncDotNet.S100.Datasets.S104

Reader and coverage pipeline for S-104 Water Level Information for Surface Navigation datasets.

## Overview

This library reads S-104 datasets from HDF5 files and provides gridded and positioned-station water-level time series for the portrayal pipeline. Key types include:

- **`S104Dataset`** — root model containing horizontal CRS, vertical datum, data coding format, and time-step coverages.
- **`S104DatasetReader`** — reads regular-grid DCF2 plus existing time-major DCF1 and station-major DCF8 datasets. DCF1 uses each `Group_NNN/timePoint` as the authoritative timestamp, including non-uniform axes, and transposes time-major values into the shared station-series model. The reader targets the Edition 2.0.0 HDF5 layout while retaining compatibility with older declared editions; schema failures raise `S100DatasetSchemaException`. The `waterLevelTrend` compound member accepts the signed/unsigned integer widths encountered in production, including IC-ENC's Int16 representation.
- **`S104CoverageSource`** — `ICoverageSource` adapter for the coverage pipeline.
- **`S104PortrayalCatalogue`** — viewer-parity heatmap catalogue with hand-coded Day / Dusk / Night band tables (see *Portrayal* below).
- **`WaterLevelCoverage`**, **`WaterLevelValue`** — water level data models. `WaterLevelCoverage.GroupPath` carries the HDF5 instance path (e.g. `/WaterLevel/WaterLevel.01`) and is used by the validation rule pack as the per-coverage `RelatedFeatureId`.
- **`S104TimeSeriesSampler`** — samples a regular-grid (dcf=2) dataset at an arbitrary geographic point across its time steps, returning an `S104TimeSeries` (nearest cell + a per-step `S104TimeSeriesPoint` list of height/trend, ordered by time and optionally windowed to a `from`/`to` range). This is the shared kernel for depth-over-time visualisations and tide reconciliation; the MCP `sample_coverage` windowed path delegates its nearest-cell and containment math to it. Nearest-cell selection operates in the coverage's geographic space (EPSG:4326 per S-104 Ed 2.0.0) without reprojection; NoData cells (`FillValue`, `-9999f`) report a `null` height.

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

### Visibility and water-area clipping (issue #483)

Because the heatmap is non-normative, the **gridded surface** (data coding
format 2) loads **hidden by default** in the viewer — the user can reveal it
from the layer controls. Discrete **positioned-station glyphs** (data coding formats 1 and 8)
remain visible by default; they are point features and are unaffected.
`S104DatasetProcessor.IsGriddedSurface` distinguishes the two so the loader only
defaults the surface hidden.

When the surface is shown alongside an S-101 ENC, the S-98 interoperability rule
`R-101-104-B` (`S98DefaultRules.R_101_104_B_ClipSurfaceToWater`) attaches the
ENC's `LandArea` geometry to the surface sub-layer (`GridCoverageSubLayer.LandAreaMask`).
The coverage renderers (`MapsuiCoverageRenderer` and the headless
`CoverageHeadlessRenderer`) then clip the rasterised surface to water at
**output-pixel resolution**: `CoverageLandClip.BuildLandPath` projects the land
polygons (honouring interior water rings via even–odd fill) into the destination
pixel space and the surface is drawn under an antialiased
`SKClipOperation.Difference` clip. Pixel-accurate clipping is essential because
real S-104 grids are often very coarse (e.g. the Rotterdam sample is only 5×6
cells ≈ 1 km each); an earlier per-cell mask could only toggle whole grid cells
and so straddled piers and basins. The surface is thus layered like S-102
bathymetry — beneath ENC line work and clipped to water — so it never bleeds
over land.

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
| `S104-STATION-SHAPE`     | Error    | Station timestamps, heights, trends, and declared sample count disagree.                                               |
| `S104-STATION-TIME`      | Error    | Explicit station timestamps are not strictly increasing.                                                              |
| `S104-STATION-TREND`     | Error    | A station contains a trend code outside 0–3.                                                                           |

R-2.1 and R-2.2 are the **time-axis rule patterns**; they are the
template the S-111 (V-3) rule pack reuses against
`SurfaceCurrentCoverage`.

## Installation

```sh
dotnet add package EncDotNet.S100.Datasets.S104
```

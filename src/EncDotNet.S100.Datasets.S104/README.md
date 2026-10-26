# EncDotNet.S100.Datasets.S104

This package reads S-104 Water Level Information for Surface Navigation
datasets from HDF5 files: water-level grids per time step, and time series at
fixed stations. It also draws them, as station gauges and surface isolines, and validates
them against the S-104 Edition 2.0.0 checklist. Reference it when you need the
water levels directly, for example to sample a tide curve at a position. To
open and render a dataset without wiring catalogues yourself, use the
[`EncDotNet.S100`](../EncDotNet.S100/README.md) package instead.

## Install

```bash
dotnet add package EncDotNet.S100.Datasets.S104
dotnet add package EncDotNet.S100.Hdf5.PureHdf
```

`EncDotNet.S100.Hdf5.PureHdf` provides the HDF5 reader the example uses.

## Read a dataset

A file holds either a regular grid per time step or a series of values per
station. `ReadAny` returns whichever the file contains:

```csharp
using EncDotNet.S100.Datasets.S104;
using EncDotNet.S100.Hdf5.PureHdf;

using var file = PureHdfFile.Open("path/to/dataset.h5");

switch (S104DatasetReader.ReadAny(file))
{
    case S104DatasetData.GriddedCoverage gridded:
        foreach (WaterLevelCoverage step in gridded.Dataset.Coverages.Take(3))
            Console.WriteLine($"{step.TimePoint:u}: {step.Values.Length} cells");
        break;

    case S104DatasetData.StationSeries stations:
        foreach (WaterLevelStation station in stations.Dataset.Stations)
            Console.WriteLine($"{station.Identifier}: {station.NumberOfTimes} samples");
        break;
}
```

Each `WaterLevelValue` has a `Height` in metres relative to the dataset's
vertical datum and a `Trend`: 1 (decreasing), 2 (increasing), 3 (steady) or
0 (unknown). Grid cells without data hold the fill value `-9999`. For more,
see [Reading product data](../../docs/reading-product-data.md).

## Main types

- **`S104Dataset`**: a gridded dataset, with its horizontal CRS, vertical
  datum, data coding format and one coverage per time step.
- **`S104DatasetReader`**: reads data coding formats 2 (regular grid), 1
  (time-major station series) and 8 (station-major station series).
  - For format 1, each `Group_NNN/timePoint` is the timestamp, including on
    uneven time axes. The time-major values are transposed into the same
    per-station model as format 8.
  - The reader targets the Edition 2.0.0 HDF5 layout and still reads datasets
    that declare older editions. Schema errors throw
    `S100DatasetSchemaException`.
  - The `waterLevelTrend` compound member accepts the signed and unsigned
    integer widths found in production data, including IC-ENC's `Int16`.
- **`WaterLevelCoverage`** and **`WaterLevelValue`**: a grid at one time step
  and its values. `WaterLevelCoverage.GroupPath` is the HDF5 path of the
  coverage, such as `/WaterLevel/WaterLevel.01`. Validation findings use it as
  `RelatedFeatureId`.
- **`S104StationSeriesDataset`** and **`WaterLevelStation`**: a station series
  and one station's heights and trends.
- **`S104TimeSeriesSampler`**: samples a regular-grid (format 2) dataset at a
  geographic position across its time steps. It returns an `S104TimeSeries`:
  the nearest cell and an `S104TimeSeriesPoint` per step with height and
  trend, in time order and optionally limited to a `from`/`to` range. It finds
  the nearest cell in the coverage's geographic coordinates (EPSG:4326 in
  S-104 Ed 2.0.0) without reprojecting. Cells with the fill value report a
  `null` height. The MCP server's `sample_coverage` tool uses it for time
  windows.
- **`S104CoverageSource`**: the `ICoverageSource` for the coverage pipeline.
- **`S104CoverageStyle`**: the SoundCharts portrayal of water level: the
  palette, station symbols, instruction builders, isoline interval and label
  formats. See [Portrayal](#portrayal).

The `S104DatasetProcessor` that renders, samples and validates a dataset is in
[`EncDotNet.S100.Datasets.Pipelines`](../EncDotNet.S100.Datasets.Pipelines/README.md).

## Portrayal

S-104 Edition 2.0.0 has no portrayal. Clause 9 says the data is used to adjust
depths in the ENC or bathymetry (S-98 Annex C), and the edition removed the
earlier portrayal catalogue. The `content/S104/pc/` folder in
`EncDotNet.S100.Specifications` is empty.

Both renderings below are SoundCharts' own, not IHO symbology.
`S104CoverageStyle` describes them and `S104DatasetProcessor` builds them.
Wherever a user meets them, they're labelled "SoundCharts portrayal: S-104 Ed
2.0.0 defines no symbology" (`S100Products.S104Portrayal`, the S-104 registration's `Portrayal` trait): in SoundCharts'
Object information panel for a station or grid cell and its dataset inspector,
on the **Portrayal** row of `s100 info`, and as `portrayalNote` in the MCP
`list_specs` and `describe_feature` results. `s100 render` and
`render_to_image` draw them unlabelled.
They use one ink colour per palette, draw no colour fill over the ENC, and
avoid magenta (mariner information), red and amber (danger) and blue and green
(depth). Colour doesn't encode the trend or the height.

| Palette | Ink | Square fill and value halo | Isoline label halo |
|---------|-----|----------------------------|--------------------|
| Day | `#0F5E73` | `#FFFFFF` | `#F6FBFE` |
| Dusk | `#9FD3DE` | `#3B4650` | `#3B4650` |
| Night | `#3E7C88` | `#0B0F14` | `#0B0F14` |

Night colours stay under luminance 0.2. `S104CoverageStyle.Palette` holds
them as the colour tokens `S104INK`, `S104FILL`, `S104HALO` and `S104ISOHALO`
(an `ApplicationPalette`); `Palette.Resolve(token, palette)` returns one colour
and `Palette.GetColorPalette(palette)` the `ColorPalette` the station gauges
resolve against.

The style is built with the code-defined portrayal toolkit in
[`EncDotNet.S100.Portrayals`](../EncDotNet.S100.Portrayals/README.md#code-defined-portrayal)
(`EncDotNet.S100.Portrayals.Application`).

### Stations

Fixed-station series (data coding formats 1 and 8) are drawn as a gauge at
each station, on the `s104.stations` sub-layer:

- A 16 px rounded square. The mark inside shows `waterLevelTrend` at the
  selected time step: a chevron up for increasing, a chevron down for
  decreasing, a dash for steady and "?" for unknown. Steady means the change
  is under the dataset's `waterLevelTrendThreshold`.
- The height at the selected step, to the right of the square, in metres above
  the dataset's vertical datum: sign always shown, two decimals, no unit, for
  example `+1.42` or `−0.12` (`FormatStationValue`). It's drawn at
  1:100 000 and larger scales (`StationLabelMaxScaleDenominator`); at smaller
  scales only the square is drawn.
- A station whose height at the step is the fill value (`-9999`) draws "—" in
  the square and no value.

The gauges are ordinary S-100 symbols and text instructions. The symbols are
SVG files in this project's `Symbols/` folder, embedded and served by
`S104CoverageStyle.Symbols` (a `SymbolSet`); the instructions are built by
`S104CoverageStyle.StationSymbols` and `StationLabels` (`InstructionBuilder`s)
with `StationLabelStyle`. They're carried on a
`VectorCoverageSubLayer` and drawn by the same renderer as S-101. So they follow
the same rules as S-101 symbols and labels: the mariner's symbol and text
scale, the label's scale minimum (SCAMIN), label decluttering (a label that
overlaps a higher-priority one is dropped; symbols always draw), upright
symbols and text on a rotated display, and picking. The value label uses the
S-100 Part 9 font characteristics `MonoSpaced` and `Bold`, with a halo
(`TextInstruction.HaloColor`).

The gauges use the warm tile disk cache. Their content scope is a hash of the
dataset's bytes plus `S104CoverageStyle.ContentScope`: the style name, its
`StyleVersion` and the toolkit's version. Bump `StyleVersion` when you change a
symbol or a colour, so tiles drawn with the old style aren't reused.

### Surface

A regular grid (data coding format 2) is drawn as dashed water-level isolines
for the selected step, on the `s104.isolines` sub-layer, with no fill:

- The interval is 0.5 m, or 0.25 m when the dataset's whole range (all steps,
  fill values excluded) is under 1 m (`SelectIsolineInterval`). It's chosen
  once per dataset, so the line density doesn't change while you scrub through
  time.
- Each line is labelled with its level in metres, about every 250 px of line:
  one decimal (two for 0.25 m steps), "−" for negatives and no "+", for example
  `1.0`, `0.5`, `0` or `−0.5` (`FormatIsolineLabel`).
- Lines stop at fill-value nodes. A coarse grid is subdivided bilinearly before
  tracing (`IsolineTracer`) so the lines aren't angular.
- Labels are drawn upright on a rotated display, scale with the mariner's text
  scale, and are dropped where they would overlap another isoline label.

`s100 render` draws the same stations and isolines.

### Visibility and clipping to water

SoundCharts loads a gridded surface hidden; you can show it from the layer
controls. Station gauges are shown by default. `S104DatasetProcessor.IsGriddedSurface`
tells the two apart.

When the surface is shown with an S-101 ENC, the S-98 interoperability rule
`R-101-104-B` (`S98DefaultRules.R_101_104_B_ClipSurfaceToWater`) attaches the
ENC's `LandArea` geometry to the isoline sub-layer
(`IsolineCoverageSubLayer.LandAreaMask`). The isolines are then clipped to
water at output-pixel resolution, in the viewer and in headless renders, so
they never cross land however coarse the grid is. The surface is layered like
S-102 bathymetry, under the ENC's line work.

## Validation

The bundled rule pack, `EncDotNet.S100.Datasets.S104.Validation.S104DatasetRules.Default`,
checks an `S104Dataset` against the S-104 Edition 2.0.0 checklist and returns a
`ValidationReport`. `S104DatasetProcessor.Validate()` runs it for you. To run it
directly:

```csharp
using EncDotNet.S100.Datasets.S104.Validation;

var report = S104DatasetRules.Default.Run(dataset);
foreach (var finding in report.Findings)
    Console.WriteLine($"{finding.RuleId} {finding.Severity}: {finding.Message}");
```

| Rule ID | Severity | Checks |
|---------|----------|--------|
| `S104-R-1.1` | Error | Each coverage's `Values.Length` equals `NumPointsLatitudinal × NumPointsLongitudinal`. |
| `S104-R-1.2` | Error | `DataCodingFormat` is a supported gridded format: 2 or 3. |
| `S104-R-2.1` | Warning | `Coverages` are in strictly increasing `TimePoint` order. One finding at the first violation. |
| `S104-R-2.2` | Warning | The intervals between `TimePoint`s are within ±10% of the median interval. Skipped when there are fewer than three coverages. |
| `S104-R-3.1` | Warning | `MethodWaterLevelProduct` is set when there's more than one coverage. |
| `S104-R-4.1` | Warning | Water levels other than the fill value are within [-15, 15] m. There's one finding per coverage with values outside the range. `-9999`, `NaN` and ±`Infinity` are skipped. |
| `S104-R-4.2` | Error | Each coverage's origin and far corner (`origin + (numPoints - 1) × spacing`) are valid in `HorizontalCRS`: within WGS 84 ranges and not crossing the antimeridian when geographic; within UTM bounds, and reprojectable to WGS 84, when projected. |
| `S104-PROJ-SCHEMA` | Error | The HDF5 dataset failed schema parsing inside `Validate()`. |
| `S104-PROJ-UNSUPPORTED` | Error | The reader raised `S100DatasetNotSupportedException` inside `Validate()`. |
| `S104-STATION-SHAPE` | Error | A station's timestamps, heights, trends and declared sample count disagree. |
| `S104-STATION-TIME` | Error | A station's explicit timestamps aren't strictly increasing. |
| `S104-STATION-TREND` | Error | A station has a trend code outside 0–3. |

`S104DatasetProcessor` checks station series itself with the `S104-STATION-*`
rules, because the rule pack works on gridded `S104Dataset`s. The S-111 rule
pack reuses the time-axis checks in `S104-R-2.1` and `S104-R-2.2`.

For the validation API and your own rules, see
[Custom catalogues and validation](../../docs/catalogues-and-validation.md).

## See also

- [Reading product data](../../docs/reading-product-data.md): read grids and
  station series for S-104 and S-111.
- [S-98 interoperability](../../docs/design/s98-interoperability.md): how
  S-104 is layered with other products.

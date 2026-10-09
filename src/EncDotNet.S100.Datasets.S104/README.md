# EncDotNet.S100.Datasets.S104

This package reads S-104 Water Level Information for Surface Navigation
datasets from HDF5 files: water-level grids per time step, and time series at
fixed stations. It also draws them as a colour-banded surface and validates
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
- **`S104PortrayalCatalogue`**: the colour bands for the water-level surface.
  See [Portrayal](#portrayal).

The `S104DatasetProcessor` that renders, samples and validates a dataset is in
[`EncDotNet.S100.Datasets.Pipelines`](../EncDotNet.S100.Datasets.Pipelines/README.md).

## Portrayal

S-104 Edition 2.0.0 has no portrayal catalogue. The specification treats water
levels as input for ECDIS depth adjustment rather than as a layer to draw. So
that S-104 can be shown like the other coverage products (S-102, S-111),
`S104PortrayalCatalogue` defines its own Day, Dusk and Night colour bands:

| Palette | Bands | No-data fill |
|---------|-------|--------------|
| Day | A diverging scale from blue (below datum) to green (above datum), in the style of ColorBrewer | Transparent (`#00000000`) |
| Dusk | Day, with saturation × 0.70 and lightness × 0.85 | Dim grey (`#4A4A4AFF`) |
| Night | Dark navy to olive, in the ECDIS night style, all with luminance below 0.2 | Darker grey (`#1A1A1AFF`) |

`SwitchPaletteAsync(PaletteType)` switches the active band table. `ResolveColorScheme`
sets `CoverageColorScheme.NoDataColor`, so the renderer paints fill-value cells
(`S104CoverageSource.FillValue`, `-9999`) in the active palette's no-data
colour.

The `content/S104/pc/` folder in `EncDotNet.S100.Specifications` is empty. If
the IHO publishes an S-104 portrayal catalogue, it goes there and this
catalogue will use it.

### Visibility and clipping to water

The colour bands aren't defined by the specification, so SoundCharts loads the
gridded surface (data coding format 2) hidden. You can show it from the layer
controls. Station glyphs (formats 1 and 8) are point features and are shown by
default. `S104DatasetProcessor.IsGriddedSurface` tells the two apart.

When the surface is shown with an S-101 ENC, the S-98 interoperability rule
`R-101-104-B` (`S98DefaultRules.R_101_104_B_ClipSurfaceToWater`) attaches the
ENC's `LandArea` geometry to the surface sub-layer
(`GridCoverageSubLayer.LandAreaMask`). The coverage renderers
(`MapsuiCoverageRenderer` and the headless `CoverageHeadlessRenderer`) then
clip the surface to water at output-pixel resolution.
`CoverageLandClip.BuildLandPath` projects the land polygons into pixel space,
keeping interior water rings with even-odd fill, and the surface is drawn under
an antialiased `SKClipOperation.Difference` clip. Clipping per pixel rather
than per grid cell matters because S-104 grids are often coarse: the Rotterdam
sample is 5×6 cells of about 1 km each. The surface is layered like S-102
bathymetry, under the ENC's line work and clipped to water, so it doesn't cover
land.

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

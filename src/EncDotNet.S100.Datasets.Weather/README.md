# EncDotNet.S100.Datasets.Weather

`EncDotNet.S100.Datasets.Weather` is the shared in-memory model for wind and
wave grids from formats that predate S-100: GRIB2 and NetCDF-CF. The readers
(separate packages: [`EncDotNet.S100.Datasets.NetCdf`](../EncDotNet.S100.Datasets.NetCdf/README.md),
and GRIB2 still to come) produce a `WeatherGridSet`,
and everything after the reader is shared: normalisation to the coverage
pipeline's grid order, the canonical parameter table, and an `ICoverageSource`
for drawing, sampling and picking, and the coverage styles that draw it. It
depends on [`EncDotNet.S100.Core`](../EncDotNet.S100.Core/README.md) and
[`EncDotNet.S100.Portrayals`](../EncDotNet.S100.Portrayals/README.md) and
contains no GRIB2 or NetCDF code.

> **Not for navigation.** Weather model data isn't official S-100 or ENC data.
> Hosts label it so wherever it appears.

The design is in [Wind and wave data](../../docs/design/weather-sources.md),
§3.3 to §3.5 (the model) and §4 and §5 (drawing and layer order).

## Install

```bash
dotnet add package EncDotNet.S100.Datasets.Weather
```

## Build a grid set

A reader describes the grid as the file stores it, and gives each time step a
callback that decodes its values when they're needed:

```csharp
using EncDotNet.S100.Datasets.Weather;
using EncDotNet.S100.Quantities;

// A 0.25° grid from 60°N to 30°N (rows running south), 0–360 longitudes.
var native = new WeatherNativeGrid(
    Rows: 121, Columns: 241,
    FirstLatitude: 60, FirstLongitude: 300,
    LatitudeIncrement: -0.25, LongitudeIncrement: 0.25);

var tenMetres = WeatherLevel.AboveGround(Length.FromMetres(10));
var u = new WeatherParameter(
    WeatherParameters.Find(WeatherParameters.WindU)!,
    tenMetres,
    [new WeatherTimeStep(validTime, ct => DecodeMessage(uMessage, ct))]);
var v = new WeatherParameter(
    WeatherParameters.Find(WeatherParameters.WindV)!,
    tenMetres,
    [new WeatherTimeStep(validTime, ct => DecodeMessage(vMessage, ct))]);

var set = new WeatherGridSet(
    new WeatherOrigin("GFS", runTime, Centre: "NCEP"), native, [u, v]);

// set.Grid: origin 30°N 60°W, rows running north, longitudes −60 to 0.
```

Then serve it to the coverage pipeline, one source per parameter group:

```csharp
foreach (var source in WeatherCoverageSource.CreateForGroups(set))
{
    source.SelectTime(mapTime);
    var sample = source.Sample(GridRegion.Full);
    var speed = sample.GetField("wind.speed");             // derived from U and V
    var from = sample.GetField("wind.from_direction");     // degrees true, "from"
}
```

## Main types

| Type | Description |
|---|---|
| `WeatherGridSet` | The model: `Origin` (model or centre, run time), the normalised `Grid`, and the `Parameters`. `ReadStep` decodes and normalises one step. |
| `WeatherOrigin` | Model or centre name, and the run (reference) time. |
| `WeatherNativeGrid` | A regular latitude/longitude grid as the file stores it: first point, signed increments, rows and columns. |
| `WeatherGrid` | The normalised grid: south-west origin, rows running north, columns running east, longitudes in −180 to 180. |
| `WeatherGridLayout` | How the native grid maps onto the normalised one, and `Normalize`, which reorders a decoded step and turns missing values into `NaN`. |
| `WeatherParameter` | One variable at one level: canonical id (or the reader's id for an unknown parameter), unit, level and time steps. |
| `WeatherTimeStep` | A valid time and a `WeatherStepDecoder` callback. `FromValues` wraps values already in memory. |
| `WeatherParameters` | The canonical parameter table, with lookups by id, GRIB2 code and CF standard name. |
| `WeatherParameterDefinition` | One row of the table. `ToSpeed`, `ToLength`, `ToAngle` and `ToPeriod` read a stored value as a quantity type. |
| `WeatherLevel` | The vertical level: surface, a height above ground, mean sea level or another level the reader describes. |
| `WeatherCoverageSource` | An `ICoverageSource` over one parameter group. |
| `WindComponents` | Wind speed and "from" direction from U and V. |
| `WeatherCoverageStyle` | The base of the four styles: `WaveHeightStyle`, `WindBarbStyle`, `WindSpeedStyle` and `WaveDirectionStyle`. Each is an `ICoverageStyle` with its S-98 plane, priority, opacity and default visibility. |
| `WeatherPalette` | The styles' Day, Dusk and Night colours, as an `ApplicationPalette`. |
| `WindBarb` | The pennants, full barbs and half barbs of a wind speed, rounded to 5 kn. |
| `WeatherPortrayal` | Builds the coverage sub-layers of a source and its styles. |
| `UnsupportedWeatherGridException` | Thrown for a grid the model can't hold, such as one that crosses the antimeridian. |

## Parameters

Readers map their codes to these canonical ids. Anything else is an unknown
parameter: it's kept in the set (for `info` output) but not drawn. Where a row
lists several codes or names, they're alternatives in order of preference.

| Canonical id | Group | Level | GRIB2 (discipline, category, number) | CF `standard_name` | Unit stored |
|---|---|---|---|---|---|
| `wind.u` | `wind` | 10 m | 0,2,2 | `eastward_wind` | m/s |
| `wind.v` | `wind` | 10 m | 0,2,3 | `northward_wind` | m/s |
| `wind.speed` | `wind` | 10 m | 0,2,1 | `wind_speed` | m/s |
| `wind.from_direction` | `wind` | 10 m | 0,2,0 | `wind_from_direction` | degrees true |
| `wind.gust` | `wind.gust` | any | 0,2,22 | `wind_speed_of_gust` | m/s |
| `wave.hs` | `wave` | any | 10,0,3 | `sea_surface_wave_significant_height` | m |
| `wave.dir` | `wave` | any | 10,0,14 (combined), 10,0,10 (primary) | `sea_surface_wave_from_direction`, `sea_surface_wave_from_direction_at_variance_spectral_density_maximum` | degrees true |
| `wave.period` | `wave` | any | 10,0,34 (peak), 10,0,15 (mean, combined), 10,0,11 (primary wave mean) | `sea_surface_wave_period_at_variance_spectral_density_maximum`, `sea_surface_wave_mean_period` | s |
| `wave.wind_sea.hs` | `wave.wind_sea` | any | 10,0,5 | `sea_surface_wind_wave_significant_height` | m |
| `wave.wind_sea.dir` | `wave.wind_sea` | any | 10,0,4 (NCEP's WVDIR) | `sea_surface_wind_wave_from_direction` | degrees true |
| `wave.swell.hs` | `wave.swell` | any | 10,0,8 | `sea_surface_swell_wave_significant_height` | m |
| `pressure.msl` | `pressure` | mean sea level | 0,3,1 (NCEP's PRMSL), 0,3,0 (ECMWF's `msl`; plain pressure, so only at mean sea level) | `air_pressure_at_mean_sea_level`, `air_pressure_at_sea_level` | Pa |

Directions are "from", in degrees clockwise from true north. Readers convert
values to the stored unit; values cross the public API as `Speed`, `Length`
and `Angle` through `WeatherParameterDefinition`, and stay `float` in the bulk
grids.

## Normalisation

`WeatherGridLayout.Create` turns the file's grid into the order `GridMetadata`
expects:

- Rows running north to south are flipped; so are columns running west.
- Longitudes move into −180 to 180. A grid on one side of 180° shifts as a
  whole: 185°E to 225°E becomes 175°W to 135°W. A global grid has its columns
  at 180° and above moved to the west, and a repeated 0°/360° column is
  dropped.
- A regional grid that crosses the antimeridian (for example 175°E to 225°E)
  has no contiguous −180 to 180 form, so it's rejected with an
  `UnsupportedWeatherGridException` naming its longitudes. Splitting it is
  open question §10.3 of the design note.
- Missing values (a GRIB2 bitmap, or the sentinels in
  `WeatherParameter.MissingValues`, such as a CF `_FillValue`) become `NaN`.

## Coverage source

`WeatherCoverageSource` serves one parameter group (`wind`, `wave`,
`pressure`, …). `CreateForGroups` makes one per group and level.

- **Fields.** Each parameter is a value field named by its id. When a group
  has `wind.u` and `wind.v` but no stored speed or direction, each sample adds
  `wind.speed` and `wind.from_direction` as computed fields:
  `√(u² + v²)` and `atan2(−u, −v)` in degrees, in [0, 360).
- **Time.** `AvailableTimes` is the union of the parameters' valid times.
  `SelectTime` picks the exact match, or else the nearest.
- **Lazy decoding.** A step is decoded the first time `Sample` needs it, and
  the most recently used steps are kept (`CacheCapacity`, default 2).
- **Overview levels.** Levels 1 and 2 decimate the grid 2× and 4× per axis
  (keeping every second or fourth point), offered while both axes keep at
  least two points.
- **No data.** `NoDataValue` and every field's `FillValue` are `NaN`.

`CoverageMetadata.Spec` is the `SpecRef` the reader passes: the file format
and its version, such as `NetCDF/4.0.0`, `NetCDF/3.0.0` or `GRIB2/2.0.0`.
A grid built in memory with no file behind it can leave it as
`default(SpecRef)`.

## Styles

None of these formats has a portrayal catalogue, so the package draws them
with its own code-defined styles, built with the portrayal toolkit in
`EncDotNet.S100.Portrayals.Application`. They're ordinary `ICoverageStyle`s,
so the coverage pipeline and both renderers draw them with no weather code.

| Style | Draws | Value field | Rotation | Default |
|---|---|---|---|---|
| `WaveHeightStyle` | Colour fill, 15 bands at 0.5, 1, 1.5, 2, 2.5, 3, 4, 5, 6, 7, 8, 9, 12 and 14 m | `wave.hs` | — | On |
| `WindBarbStyle` | WMO wind barbs, one per 5 kn from 5 to 200 kn; a calm circle below 2.5 kn | `wind.speed` | `wind.from_direction` | On |
| `WindSpeedStyle` | Colour fill in Beaufort bands, force 0 to 12 | `wind.speed` | — | Off |
| `WaveDirectionStyle` | One arrow, half size below 1.5 m, growing to double size at 6 m | `wave.hs` | `wave.dir` + 180° | Off |
| `PressureIsobarStyle` | Isobars every 4 hPa, labelled in hPa (`1012`) | `pressure.msl` | — | Off |

- **Knots.** `wind.speed` stays in m/s, its stored unit. The barb and
  Beaufort bands are defined in knots and their limits converted to m/s, so
  the grid isn't copied into knots and sampling still reports m/s. Legend
  labels are in knots.
- **Direction.** The barb symbols are drawn with the staff running up from
  the station, and the renderers rotate symbols clockwise by the rotation
  field, so a barb's staff points into the wind: a westerly (270°) points
  west. The feathers are on the clockwise side in the northern hemisphere
  and on the anticlockwise side, the mirror image, south of the equator: the
  style's `SouthernHemisphereBands` pick the `WXBARBS` set by each cell's
  latitude. `WindBarbSymbols.For(knots, latitude)` applies the same rule to a
  single barb (an observation, an own-ship wind). The wave arrow points
  up, and its style adds 180° to the "from" direction so it points the way
  the waves travel.
- **Layer order.** Fills go on the `OnDemandSurface` plane at priority 0,
  under the S-104 surface, at `Opacity` 0.5. Barbs and arrows go on the
  `DynamicArrows` plane at priority 20, above the S-111 arrows (10). Isobars
  go on `OtherChartOverlays` at priority 90, under the observations (100).
- **Land.** The wave height fill sets `ClipToWater`, so when an S-101 chart
  is loaded the S-98 rule `R-101-WX-B` clips it to water with the chart's
  `LandArea` geometry, as `R-101-104-B` clips the S-104 surface. The wind
  fill and isobars aren't clipped.
- **Legends.** Each fill's `GridCoverageSubLayer.Legend` lists its band
  colours and labels, in the palette it was drawn in; the viewer shows it
  under the fill's layer stack entry. The headless renderer draws no legend.
- **Isobars.** `PressureIsobarStyle.Contours` are value-based
  `ContourStyle`s in pascals, every 4 hPa from 860 to 1100 hPa; only the
  levels a grid reaches are traced (`CoverageContours`, marching squares)
  into an `IsolineCoverageSubLayer`, solid and labelled.
- **Colours.** `WeatherPalette` keeps clear of the S-101 depth (blue,
  green), danger (red, amber) and mariner (magenta) hues and the S-104 teal:
  wave height is a violet ramp, wind speed a low-chroma sepia and the
  isobars a neutral grey-blue. Night
  colours stay under relative luminance 0.2.
- **Symbols.** The 40 northern barbs (`WXBARBN005` to `WXBARBN200`), their
  40 mirrored southern barbs (`WXBARBS005` to `WXBARBS200`), the calm circle
  (`WXCALM`) and the wave arrow (`WXWAVE01`) are S-100 SVGs embedded from
  `Symbols/`, served by `WeatherCoverageStyle.Symbols`. They're generated by
  `tools/GenerateWeatherSymbols` and checked in.
- **Tile cache.** `WeatherCoverageStyle.ContentScope` carries
  `StyleVersion`; bump it when the symbols, colours or bands change.

`WeatherPortrayal.BuildAsync` turns a source and its styles into a
`CoveragePortrayalResult`, a `GridCoverageSubLayer` per fill, an
`ArrowCoverageSubLayer` per symbol style and an `IsolineCoverageSubLayer` for
the isobars, for the Mapsui renderer or the headless compositor:

```csharp
var source = WeatherCoverageSource.CreateForGroups(set)[0];
var result = await WeatherPortrayal.BuildAsync(
    source,
    WeatherCoverageStyle.CreateFor(source),     // the styles whose fields it has
    "gfs.t06z.grb2",
    new RenderContext { TimeStep = mapTime, Palette = PaletteType.Dusk });
```

## Dependencies

- `EncDotNet.S100.Core`: the coverage pipeline interfaces and the quantity
  types.
- `EncDotNet.S100.Portrayals`: the portrayal toolkit (`ApplicationPalette`,
  `SymbolSet`, `ApplicationContentScope`).

## See also

- [Wind and wave data — Design Note](../../docs/design/weather-sources.md)
- [Quantity types](../../docs/design/quantity-types.md)

# EncDotNet.S100.Datasets.Weather

`EncDotNet.S100.Datasets.Weather` is the shared in-memory model for wind and
wave grids from formats that predate S-100: GRIB2 and NetCDF-CF. The GRIB2 and
NetCDF readers (separate packages, still to come) produce a `WeatherGridSet`,
and everything after the reader is shared: normalisation to the coverage
pipeline's grid order, the canonical parameter table, and an `ICoverageSource`
for drawing, sampling and picking. It depends only on
[`EncDotNet.S100.Core`](../EncDotNet.S100.Core/README.md) and contains no
GRIB2 or NetCDF code.

> **Not for navigation.** Weather model data isn't official S-100 or ENC data.
> Hosts label it so wherever it appears.

The design is in [Wind and wave data](../../docs/design/weather-sources.md),
§3.3 to §3.5.

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
| `wave.swell.hs` | `wave.swell` | any | 10,0,8 | `sea_surface_swell_wave_significant_height` | m |
| `pressure.msl` | `pressure` | mean sea level | 0,3,1 | `air_pressure_at_mean_sea_level`, `air_pressure_at_sea_level` | Pa |

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

`CoverageMetadata.Spec` needs a `SpecRef`, which accepts only S-100 names
until issue #1061 adds `GRIB2` and `NetCDF`. Until then a source reports
`default(SpecRef)` unless the reader passes one.

## Dependencies

- `EncDotNet.S100.Core`: the coverage pipeline interfaces and the quantity
  types.

## See also

- [Wind and wave data — Design Note](../../docs/design/weather-sources.md)
- [Quantity types](../../docs/design/quantity-types.md)

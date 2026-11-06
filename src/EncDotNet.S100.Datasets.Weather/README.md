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
| `CurrentComponents` | Current speed and "to" direction from U and V. |
| `WeatherCoverageStyle` | The base of the eleven styles: `SeaTemperatureStyle`, `WaveHeightStyle`, `WindSpeedStyle`, `VisibilityStyle`, `FreezingSprayStyle`, `SeaTemperatureIsothermStyle`, `AirTemperatureIsothermStyle`, `PressureIsobarStyle`, `CurrentArrowStyle`, `WindBarbStyle` and `WaveDirectionStyle`. Each is an `ICoverageStyle` with its S-98 plane, priority, opacity and default visibility. |
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
| `sea.temperature` | `sea.temperature` | any | 10,3,0 (NCEP's WTMP) | `sea_surface_temperature`, `sea_surface_foundation_temperature`, `sea_surface_subskin_temperature`, `sea_surface_skin_temperature` | K |
| `current.u` | `current` | any | 10,1,2 (UOGRD) | `eastward_sea_water_velocity`, `surface_eastward_sea_water_velocity` | m/s |
| `current.v` | `current` | any | 10,1,3 (VOGRD) | `northward_sea_water_velocity`, `surface_northward_sea_water_velocity` | m/s |
| `current.speed` | `current` | any | 10,1,1 (SPC) | `sea_water_speed` | m/s |
| `current.to_direction` | `current` | any | 10,1,0 (DIRC) | `sea_water_velocity_to_direction`, `direction_of_sea_water_velocity` | degrees true |
| `visibility` | `visibility` | any | 0,19,0 (VIS) | `visibility_in_air` | m |
| `air.temperature` | `air.temperature` | 2 m above ground | 0,0,0 (TMP) | `air_temperature` | K |
| `surface.temperature` | `surface.temperature` | surface | 0,0,0 (TMP) | `surface_temperature` | K |
| `land.cover` | `land.cover` | surface | 2,0,0 (LAND) | `land_binary_mask` | proportion |

Wind and wave directions are "from", and current directions "to" (the
oceanographic convention, as in S-111), in degrees clockwise from true north.
The current rows take any level, because models give currents at their top
layer, whatever its depth. Readers convert
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
  `√(u² + v²)` and `atan2(−u, −v)` in degrees, in [0, 360). The `current`
  group gets `current.speed` and `current.to_direction` the same way, with
  `atan2(u, v)`.
- **Freezing spray.** `CreateForGroups` adds a combined source, `spray`,
  when the set has the wind (speed, or U and V), `air.temperature`, and
  `sea.temperature` or else `surface.temperature` (with `land.cover` to leave
  land out). It derives `spray.predictor`, Overland's icing predictor
  (`FreezingSpray.Predictor`), and lists only that field in `FieldNames`
  (`IsCombined`), so only `FreezingSprayStyle` draws it. Water colder than
  −2 °C is ice or land and has no predictor.
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
| `PressureIsobarStyle` | Isobars every 4 hPa, labelled in hPa (`1012`) | `pressure.msl` | — | On |
| `SeaTemperatureStyle` | Colour fill, 32 bands: below 0 °C, each degree to 30 °C, and 30 °C and higher | `sea.temperature` | — | On |
| `SeaTemperatureIsothermStyle` | Isotherms every 2 °C, labelled in °C (`18°`) | `sea.temperature` | — | Off |
| `CurrentArrowStyle` | Arrows in S-111's nine speed bands, colours and sizes (0.5, 1, 2, 3, 5, 7, 10 and 13 kn) | `current.speed` | `current.to_direction` | On |
| `VisibilityStyle` | Grey fill in the marine forecast's three restricted bands: under 1000 m, to 2 nm and to 5 nm; good visibility isn't drawn | `visibility` | — | On |
| `FreezingSprayStyle` | Plum fill in Overland's icing classes: light, moderate (22.4), heavy (53.3) and extreme (83.0); no icing isn't drawn | `spray.predictor` (derived) | — | On |
| `AirTemperatureIsothermStyle` | Dashed isotherms every 5 °C, labelled in °C (`-5°`) | `air.temperature` | — | Off |

- **Default and switches.** The Default column is `IsVisibleByDefault`.
  `IsDrawnWith(display)` applies the map-wide switches in
  `EcdisDisplaySettings.LayerVisibility`, keyed by the style's `LayerKey`, and
  falls back to the default when a key is unset or there are no display
  settings. The GRIB2 and NetCDF processors offer every style that applies
  and draw those `IsDrawnWith` the render context, so the viewer's Display
  controls Weather section switches them for every weather dataset, and
  `s100 render`, which passes no switches, draws the defaults.
- **Knots.** `wind.speed` stays in m/s, its stored unit. The barb and
  Beaufort bands are defined in knots and their limits converted to m/s, so
  the grid isn't copied into knots and sampling still reports m/s. Legend
  labels are in knots. In the same way `sea.temperature` stays in kelvin, its
  bands and isotherms are defined in degrees Celsius, and its labels and pick
  values are in degrees Celsius.
- **Direction.** The barb symbols are drawn with the staff running up from
  the station, and the renderers rotate symbols clockwise by the rotation
  field, so a barb's staff points into the wind: a westerly (270°) points
  west. The feathers are on the clockwise side in the northern hemisphere
  and on the anticlockwise side, the mirror image, south of the equator: the
  style's `SouthernHemisphereBands` pick the `WXBARBS` set by each cell's
  latitude. `WindBarbSymbols.For(knots, latitude)` applies the same rule to a
  single barb (an observation, an own-ship wind). The wave arrow points
  up, and its style adds 180° to the "from" direction so it points the way
  the waves travel. The current arrows point up too, and `current.to_direction`
  is already the way the water flows.
- **S-111.** The current arrows set `ArrowCoverageSubLayer.YieldsToS111`.
  When an S-111 grid is loaded and active, the S-98 rule `R-111-WX-B` blanks
  their speed inside the S-111 dataset's `CoverageExtent`, so only S-111's
  arrows are drawn there. S-111 station and mesh datasets report no extent
  and hide nothing.
- **Layer order.** Fills go on the `OnDemandSurface` plane at priority 0,
  under the S-104 surface, at `Opacity` 0.5. Barbs and arrows go on the
  `DynamicArrows` plane at priority 20, above the S-111 arrows (10). Isobars
  and isotherms go on `OtherChartOverlays` at priority 90, under the
  observations (100).
- **Land.** The wave height and sea temperature fills set `ClipToWater`, so when an S-101 chart
  is loaded the S-98 rule `R-101-WX-B` clips it to water with the chart's
  `LandArea` geometry, as `R-101-104-B` clips the S-104 surface. The wind
  fill and isobars aren't clipped.
- **Legends.** Each fill's `GridCoverageSubLayer.Legend` lists its band
  colours and labels as `SwatchLegendEntry` rows (`LegendEntry.FromScheme`),
  in the palette it was drawn in; the viewer shows it under the fill's layer
  stack entry. The headless renderer draws no legend.
- **Isobars.** `PressureIsobarStyle.Contours` are value-based
  `ContourStyle`s in pascals, every 4 hPa from 860 to 1100 hPa; only the
  levels a grid reaches are traced (`CoverageContours`, marching squares)
  into an `IsolineCoverageSubLayer`, solid and labelled.
- **Colours.** `WeatherPalette` keeps clear of the S-101 depth (blue,
  green), danger (red, amber) and mariner (magenta) hues and the S-104 teal:
  wave height is a violet ramp, wind speed a low-chroma sepia and the
  isobars a neutral grey-blue. Sea temperature is the exception: it's
  diverging, as temperature maps are, from slate blue for cold water through
  near white at 15 °C to pale yellow, orange and brick for warm water, so
  each degree of a front is a different colour; like every fill it's drawn
  at half opacity. The isotherms are a dark neutral grey. Night
  colours stay under relative luminance 0.2. The current arrows take S-111's
  own band colours (`SCBN1` to `SCBN9` of its colour profile) and outline.
- **Symbols.** The 40 northern barbs (`WXBARBN005` to `WXBARBN200`), their
  40 mirrored southern barbs (`WXBARBS005` to `WXBARBS200`), the calm circle
  (`WXCALM`), the wave arrow (`WXWAVE01`) and the nine current arrows
  (`WXCURR1` to `WXCURR9`, S-111's arrow outline) are S-100 SVGs embedded from
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

## Surface analyses

The package also holds the surface analysis model, fronts and pressure
centres, and a reader for the National Weather Service Weather Prediction
Center's (WPC) coded surface bulletins (issue
[#1233](https://github.com/philliphoff/EncDotNet.S100/issues/1233), design note
`vector-weather.md` §5, §8.3), and `SurfaceAnalysisStyle`, which draws them
(issue [#1300](https://github.com/philliphoff/EncDotNet.S100/issues/1300)).
`SurfaceAnalysisDatasetProcessor` in
[`Datasets.Pipelines`](../EncDotNet.S100.Datasets.Pipelines/README.md)
registers the format as `CODSUS` and draws the analysis at the view time:
each analysis or forecast from its valid time for 12 hours, at half opacity
with its age on the highs and lows after 6 (issue
[#1301](https://github.com/philliphoff/EncDotNet.S100/issues/1301)). `s100
info` describes a bulletin.

```csharp
using EncDotNet.S100.Datasets.Weather;

var bulletin = CodedSurfaceBulletinReader.Read("codsus_hr.txt");

foreach (var analysis in bulletin.Analyses)        // one, or one per forecast time
{
    Console.WriteLine($"{analysis.ValidTime:u} ({analysis.ForecastHours?.ToString() ?? "analysis"})");
    foreach (var front in analysis.Fronts)
        Console.WriteLine($"  {front.Code} {front.Strength}: {front.Points.Count} points");
    foreach (var centre in analysis.PressureCentres)
        Console.WriteLine($"  {centre.Kind} {centre.PressureHectopascals} hPa at {centre.Position}");
}
```

What it reads:

- **The coded surface analysis** (`CODSUS`), every 3 hours, at standard
  resolution (whole degrees, `ASUS01 KWBC`) and high resolution (tenths of a
  degree, `ASUS02 KWBC`, the canonical input).
- **The coded forecast bulletins**, such as the short-range `CODSRP`
  (`FSUS02 KWBC`): one `SurfaceAnalysis` per forecast time (`12HR PROG VALID
  051200Z`), with `ForecastHours` set.
- `HIGHS` and `LOWS` (central pressure and position), and `COLD`, `WARM`,
  `OCFNT`, `STNRY` and `TROF` with an optional intensity (`WK`, `MDT`, `STG`)
  and their points.
- Lines that wrap anywhere, even between a pressure and its position, codes
  that start mid-line, and the `$$`, `**` and `....` end markers. A code the
  reader doesn't know is skipped and listed in `SkippedCodes`.
- Longitudes past 180°W, which are east longitudes: a front that crosses 180°
  has points on both sides (`SurfaceFront.CrossesAntimeridian`).

The short valid times (`VALID 110400Z` is month, day and hour) are dated from
the bulletin's issuance line. WPC's own examples have none, so for those
`Read(path)` uses the file's last-write time and `Parse(text, referenceTime)`
the time you give.

**Which side the pips go on.** A front's points run in the bulletin's order,
and its symbols go on the **left**, walking from the first point to the last:
that's the side it moves towards. WPC's format pages don't say so; the
evidence from 15 real analyses is in the `CodedSurfaceBulletinReader` remarks
(66 of 68 cold fronts leaving a low have their left facing east, 26 of 30
warm fronts north), and a test checks it against a committed bulletin. Which
of a stationary front's two symbols goes on which side isn't in the data;
the style puts the triangles on the left and the semicircles on the right.

**Drawing.** `SurfaceAnalysisStyle` builds the drawing instructions with the
portrayal toolkit, in one token, `WXFRONT` (Day `#1A2030`, Dusk `#E3E6EE`,
Night `#646A76`), darker and twice as wide as the isobars:

| Kind | Line style | Pips |
|---|---|---|
| Cold | `WXFRONT_COLD`, solid 0.7 mm | `WXFRONTTRI` triangles, left |
| Warm | `WXFRONT_WARM`, solid 0.7 mm | `WXFRONTSEMI` semicircles, left |
| Occluded | `WXFRONT_OCCLUDED`, solid 0.7 mm | Triangle and semicircle in turn, left |
| Stationary | `WXFRONT_STATIONARY`, solid 0.7 mm | Triangle left, then semicircle right, in turn |
| Trough | `WXFRONT_TROUGH`, dashed 1.5 / 1 mm, 0.5 mm | None; SCAMIN 1:40,000,000 |

The pips are 4 mm wide, one every 9 mm from 4 mm in, so their spacing is the
same on screen at every scale (`ComplexLineStyle`). Each line has a halo in
the isobar label halo colour. Points are smoothed with three Chaikin passes
(`SurfaceAnalysisStyle.Smooth`), which keeps the ends, drops repeats and
takes the short way across 180°. A high or low is a bold `H` or `L` above a
cross (`WXPCENTRE`) at the centre; from 1:60,000,000 the pressure is drawn
below it, underlined (`WXPCENTRERULE`). `BuildLegend(palette)` gives the
Layer stack legend: the five lines and "Pressure centre, hPa". The sub-layer
is `weather.fronts` on Other chart overlays at priority 92, above the
isobars at 90.

```csharp
var instructions = new List<DrawingInstruction>();
foreach (var front in analysis.Fronts)      // feature geometry: SurfaceAnalysisStyle.Smooth(front.Points, 3)
    instructions.AddRange(SurfaceAnalysisStyle.BuildFront(id, front.Type));
foreach (var centre in analysis.PressureCentres)
    instructions.AddRange(SurfaceAnalysisStyle.BuildCentre(id, centre));
// LineStyleProvider = SurfaceAnalysisStyle.LineStyles.Provider,
// SymbolProvider = WeatherCoverageStyle.Symbols.Provider, palette WeatherPalette.Palette.
```

**Coverage.** The bulletins cover WPC's domain only, the Northern Hemisphere
from 180° to 0° (`CodedSurfaceBulletin.WpcDomain`). Outside it, nothing is
known; the dataset's extent is the domain, not the features' bounding box.

| Type | Description |
|---|---|
| `CodedSurfaceBulletinReader` | `Read(path)`, `Read(stream, referenceTime)`, `Parse(text, referenceTime)` and `LooksLikeCodedSurfaceBulletin(leadingBytes)`. |
| `CodedSurfaceBulletin` | `Kind` (analysis or forecast), `Resolution`, `Header`, `Analyses`, `SkippedCodes` and `Domain`. |
| `CodedSurfaceBulletinHeader` | WMO heading, product id, title, issuing office and issue time; each optional. |
| `SurfaceAnalysis` | Valid time, forecast lead time, issuing centre, `Fronts` and `PressureCentres`. |
| `SurfaceFront` | `Type` (`SurfaceFrontType`), `Strength` (`SurfaceFrontStrength`) and `Points` in the bulletin's order. |
| `PressureCentre` | `Kind` (high or low), `PressureHectopascals` and `Position`. |
| `CodedSurfaceBulletinFormatException` | Thrown with the line number for a bulletin that can't be decoded. |
| `SurfaceAnalysisStyle` | The drawing: `BuildFront`, `BuildCentre`, `LineStyles`, `LineStyleOf`, `BuildLegend`, `Smooth`, and the layer key, priority and scale limits. |

> **Not for navigation.** Surface analyses are weather analyses, not S-100
> data.

## Tropical cyclones

`TropicalCycloneStyle` draws a tropical cyclone advisory, the model read by
[`EncDotNet.S100.Datasets.TropicalCyclones`](../EncDotNet.S100.Datasets.TropicalCyclones/README.md),
in one plum-grey storm ink. `TropicalCycloneDatasetProcessor` in
`EncDotNet.S100.Datasets.Pipelines` uses it. The design is in the vector
weather design note (`docs/design/vector-weather.md`, §3).

- **Colours.** `WeatherPalette` has `WXTCINK` for every line, symbol and
  label, `WXTCHALO` under them, and fills for the cone (`WXTCCONE`) and the
  34, 50 and 64 kt radii (`WXTC34`, `WXTC50`, `WXTC64`).
  `WeatherPalette.TropicalCycloneFillOpacity` gives each fill's opacity in each
  palette; the area instructions carry it as their transparency, because
  palette colours have no alpha. No colour is red, amber, magenta, blue or
  green.
- **Planes.** The fills go in the `weather.tc.areas` sub-layer, on the
  On-demand surface plane at priority 10 (`AreasPriority`). The lines,
  symbols and labels go in `weather.tc.track`, on the Other chart overlays
  plane at priority 95 (`TrackPriority`).
- **Lines.** The cone outline is dashed 1.2 / 0.8 mm, the 34 kt radius dashed
  0.6 / 0.6 mm, the 50 kt radius solid and the 64 kt radius heavy. The
  forecast track is solid and the past track dotted. The cone beyond 72 hours
  is stippled (`StippleFill`, an area fill served by `ResolveAreaFill`).
- **Symbols.** `SymbolFor(class, category, latitude)` names the WMO class
  glyph: `WXTCD` (depression), `WXTCS` (storm), `WXTCH` (hurricane or
  typhoon), `WXTCM` (major, category 3 and above, or super typhoon), `WXTCSD`
  and `WXTCSS` (subtropical) and `WXTCPT` (post-tropical, remnant low or
  disturbance), with `N` for the northern hemisphere or `S` for the southern.
  A northern glyph's arms leave the circle clockwise, the top arm to the
  east, as the spiral bands of a storm turning anticlockwise trail; the
  southern set is its mirror image. `WXTCRING` and the dashed `WXTCRINGEST`
  ring the storm, and `WXTCDOT` marks a best-track point. All are in
  `WeatherCoverageStyle.Symbols`, generated by `tools/GenerateWeatherSymbols`.
- **Labels.** `StormLabel` (`HURRICANE IAN · 105 kt · 952 hPa`),
  `AdvisoryLabel` (`Adv 20 · issued Wed 03:00 · NNE 9 kt`) and
  `ForecastLabel` (`Thu 00:00 · 110 kt`) take the zone and culture to write
  times in. Forecast times are drawn at 1:20,000,000 and larger, and the
  storm's second line at 1:40,000,000 and larger.
- **Legends.** `BuildTrackLegend` has a symbol per class, the southern mirror
  and both tracks; `BuildAreasLegend` has the cone's two parts and the three
  radii.

## Dependencies

- `EncDotNet.S100.Core`: the coverage pipeline interfaces and the quantity
  types.
- `EncDotNet.S100.Portrayals`: the portrayal toolkit (`ApplicationPalette`,
  `SymbolSet`, `ApplicationContentScope`).
- `EncDotNet.S100.Datasets.TropicalCyclones`: the tropical cyclone advisory
  model `TropicalCycloneStyle` draws.

## See also

- [Wind and wave data — Design Note](../../docs/design/weather-sources.md)
- [Quantity types](../../docs/design/quantity-types.md)

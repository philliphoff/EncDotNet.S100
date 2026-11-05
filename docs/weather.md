# Show wind and waves on the chart

SoundCharts can draw forecast wind and waves, sea surface temperature,
surface currents, restricted visibility, air temperature, freezing spray, marine weather warnings and live observations over your
charts, with surface analyses (fronts and pressure centres). This page covers which files it reads,
where to get them, how to show them in SoundCharts and `s100`, and what isn't
supported.

> [!WARNING]
> Weather data isn't S-100 data and isn't for navigation. SoundCharts says so
> in the Datasets panel, in every pick report and in `s100 info` and
> `s100 identify` output. Use official forecasts and warnings to plan and make
> a passage.

## Supported data

| Data | Formats | What's drawn |
|---|---|---|
| Forecast and analysis grids | GRIB2, NetCDF-3 and NetCDF-4 with CF conventions | Wave height fill, wind barbs, isobars, a sea surface temperature fill, current arrows, a restricted visibility fill and a freezing spray fill; optionally a wind speed fill, wave arrows, sea isotherms and air isotherms |
| Surface analyses | WPC coded surface bulletin (`CODSUS`), standard and high resolution, and WPC's coded forecast bulletins (such as `CODSRP`) | Fronts, troughs, highs and lows in the symbols of a printed weather map, in one dark ink |
| Marine weather warnings | CAP 1.2 XML (one alert or an Atom feed of them), NWS alerts GeoJSON | Warning areas, hatched by severity and labelled with the event, while they're in force |
| Buoy and coastal station observations | NOAA NDBC, fetched live | A wind barb and wave height at each station |
| Own-ship wind | NMEA 0183 from your instruments | True wind as a barb beside own ship, with water and air temperature |

### Grids

Both grid formats must use a regular latitude/longitude grid. The parameters
drawn or sampled are:

| Parameter | GRIB2 (NCEP name) | CF `standard_name` |
|---|---|---|
| Wind at 10 m: U, V, speed, direction | UGRD, VGRD, WIND, WDIR | `eastward_wind`, `northward_wind`, `wind_speed`, `wind_from_direction` |
| Wind gust | GUST | `wind_speed_of_gust` |
| Significant wave height | HTSGW | `sea_surface_wave_significant_height` |
| Wave direction | DIRPW, MWD | `sea_surface_wave_from_direction` |
| Wave period | PERPW, MWP, PP1D | `sea_surface_wave_period_at_variance_spectral_density_maximum`, `sea_surface_wave_mean_period` |
| Wind sea and swell height | WVHGT, SWELL (first partition) | `sea_surface_wind_wave_significant_height`, `sea_surface_swell_wave_significant_height` |
| Mean sea level pressure | PRMSL, MSL | `air_pressure_at_mean_sea_level` |
| Sea surface temperature | WTMP | `sea_surface_temperature`, `sea_surface_foundation_temperature`, `sea_surface_subskin_temperature`, `sea_surface_skin_temperature` |
| Surface current: U, V, speed, direction | UOGRD, VOGRD, SPC, DIRC | `eastward_sea_water_velocity`, `northward_sea_water_velocity` (or their `surface_` forms), `sea_water_speed`, `sea_water_velocity_to_direction` |
| Visibility | VIS | `visibility_in_air` |
| Air temperature at 2 m | TMP at 2 m above ground | `air_temperature` |
| Surface (skin) temperature, for freezing spray | TMP at the surface | `surface_temperature` |
| Land mask, for freezing spray | LAND | `land_binary_mask` |

When a file has only U and V, wind or current speed and direction are worked
out from them. Wind and wave directions are the direction they come from;
current directions are the direction the water flows to, as in S-111.
Currents are read at whatever depth the file gives them, which is the
model's top layer: 0 m for HYCOM, about 0.5 m for Copernicus Marine. Sea surface
temperature can be in kelvin, degrees Celsius or degrees Fahrenheit. GFS's
surface temperature (TMP at the surface) isn't drawn as sea temperature,
because over land it's the temperature of the ground; it's read only for the
freezing spray risk. Other
parameters are listed by `s100 info` but not drawn.

GRIB2 files can hold many messages, and you can append several files, such as
one per forecast hour, into one. These data packings are read: simple (5.0),
complex (5.2), complex with spatial differencing (5.3), JPEG 2000 (5.40) and
PNG (5.41). GFS-Wave is served as JPEG 2000. The
[GRIB2 reader's README](../src/EncDotNet.S100.Datasets.Grib/README.md#whats-supported)
lists the templates in full.

NetCDF-4 files may use deflate and shuffle compression. Each variable must be
on time, latitude and longitude, with at most one height or depth level. The
[NetCDF reader's README](../src/EncDotNet.S100.Datasets.NetCdf/README.md#cf-rules)
gives the CF rules.

### Warnings

A CAP warning area is drawn from its polygon or circle. An area that gives
only NWS zone codes (UGC codes such as `ANZ535`) is drawn from the NWS
coastal and offshore marine zones, which SoundCharts includes. A warning is
drawn from its onset until its hazard ends, and an update or cancellation in
the same file replaces it.

### Surface analyses

The National Weather Service Weather Prediction Center (WPC) issues a coded
surface analysis every 3 hours: highs and lows with their central pressure,
and cold, warm, occluded and stationary fronts and troughs as lines of
points. The high-resolution bulletin gives positions to a tenth of a degree,
the standard one to whole degrees. The coded forecast bulletins give the same
features at forecast times, 12 and 24 hours ahead and later. They cover
WPC's area only: the Northern Hemisphere from the Date Line to the Greenwich
meridian, mostly North America and the waters around it. Elsewhere there's
no analysis, not an absence of fronts.

## Get the data

These sources are public. Check each provider's terms before you share images
made from its data. The Library can fetch some of these sources for you.

### GRIB2

- **NOAA NOMADS GRIB filter.** The GRIB filter pages on
  [nomads.ncep.noaa.gov](https://nomads.ncep.noaa.gov/) cut GFS and GFS-Wave
  to the variables, area and forecast hours you choose, and return one file.
  For waves choose HTSGW, PERPW and DIRPW; for wind and pressure choose UGRD
  and VGRD at 10 m above ground and PRMSL; for visibility choose VIS at the
  surface; for freezing spray choose TMP at 2 m above ground and at the
  surface, LAND at the surface, and the 10 m wind. The download has no file
  extension. The Library's two GFS sources offer these too; tick them when
  you choose parameters.
- **NOAA on AWS.** The `noaa-gfs-bdp-pds` bucket holds every GFS and
  GFS-Wave run as whole files, for example
  `gfs.YYYYMMDD/HH/wave/gridded/gfswave.tHHz.atlocn.0p16.f003.grib2` for the
  Atlantic wave grid. A GFS atmosphere file
  (`gfs.YYYYMMDD/HH/atmos/gfs.tHHz.pgrb2.0p25.f003`) is about 500 MB; its
  `.idx` file gives the byte range of each field, so you can download only
  the wind and pressure.
- **ECMWF Open Data.** [data.ecmwf.int](https://data.ecmwf.int/) publishes
  the IFS and wave model at 0.25° under CC BY 4.0. Its files use CCSDS
  packing, which SoundCharts can't read yet, so repack them first, for
  example with ecCodes:

  ```bash
  grib_set -r -s packingType=grid_simple ecmwf-wave.grib2 ecmwf-wave-simple.grib2
  ```

- **DWD Open Data.** [opendata.dwd.de](https://opendata.dwd.de/) publishes
  ICON and wave model output as GRIB2. Use the regular latitude/longitude
  grids, not the icosahedral ones.

### NetCDF

- **Copernicus Marine.** The [Copernicus Marine Service](https://marine.copernicus.eu/)
  publishes global and regional wave forecasts as NetCDF-4. It needs a free
  account. Its toolbox's `copernicusmarine subset` command downloads an area
  and time range; the wave variables `VHM0`, `VMDR` and `VTPK` are drawn.
- **NOAA ERDDAP.** ERDDAP servers, such as NOAA CoastWatch's, subset gridded
  datasets by URL. Choose the `.nc` file type, which is NetCDF-3, and a
  dataset whose variables have CF standard names. The Library's **Sea surface
  temperature — NOAA ERDDAP** source fetches the latest daily analysis for
  the map view from one of these CoastWatch datasets:

  | Dataset | Resolution | ERDDAP id |
  |---|---|---|
  | NASA JPL MUR SST (default) | 1 km | `jplMURSST41` |
  | NOAA OISST v2.1, near real time | 0.25° | `ncdcOisst21NrtAgg_LonPM180` |
  | NOAA Geo-polar Blended SST | 5 km | `nesdisBLENDEDsstDNDaily` |

  These are analyses of the day before, not forecasts. CoastWatch serves the
  Blended analysis from its central ERDDAP at `coastwatch.noaa.gov`, so it's
  unavailable when that server is. Over a large area the
  Library asks ERDDAP for every second or third point, so that no download
  has more than 1500 points across or down.

  Two more Library sources fetch surface currents for the map view:

  | Source | Data | ERDDAP id |
  |---|---|---|
  | **Surface currents, altimetry — NOAA AOML ERDDAP** | Geostrophic currents from satellite altimetry, 0.2°, daily, the latest day | `miamicurrents` |
  | **Surface currents, HF radar — NOAA ERDDAP** | Currents measured by the US coastal HF radars, hourly, the last day | `ucsdHfrE2` (US East Coast and Gulf of Mexico, 2 km, the default), `ucsdHfrE6` (the same coasts at 6 km, with a longer range), `ucsdHfrW6` (West Coast), `ucsdHfrH1` (Hawaiʻi), `ucsdHfrP6` (Puerto Rico) |

  Geostrophic currents show the large ocean currents and eddies, such as the
  Gulf Stream, but not wind-driven or tidal flow. HF radar sees the top metre
  of the sea out to about 150 km from the radars, and its coverage changes
  from hour to hour. Neither is a forecast.
- **HYCOM.** The global HYCOM ocean model's 8-day current forecast is on
  [ncss.hycom.org](https://ncss.hycom.org/thredds/catalogs/ESPC-D-V02_uv3z.xml).
  Its NetCDF Subset Service cuts an area, time range and depth by URL, for
  example
  `…/ncss/grid/FMRC_ESPC-D-V02_uv3z/FMRC_ESPC-D-V02_uv3z_best.ncd?var=water_u&var=water_v&north=40&south=33&west=-76&east=-68&vertCoord=0&time_start=…&time_end=…&accept=netcdf4`.
  Requests can take a minute.

### Warnings and observations

- **CAP warnings.** The National Weather Service API returns active alerts,
  for example
  `https://api.weather.gov/alerts/active?area=AN` for the western North
  Atlantic marine area. Save the GeoJSON it returns, or request
  `application/cap+xml` for a CAP Atom feed. The API asks callers to send a
  `User-Agent` header that identifies them.
- **Surface analyses.** The latest bulletins are plain text on the NWS
  server: the high-resolution analysis at
  `https://tgftp.nws.noaa.gov/data/raw/as/asus02.kwbc.cod.sus.txt`, the
  standard one at `…/raw/as/asus01.kwbc.cod.sus.txt`, and the short-range
  forecast at `…/raw/fs/fsus02.kwbc.cod.srp.txt`. Save them with any name;
  SoundCharts and `s100` recognise the contents.
- **NDBC buoys.** There's nothing to download. SoundCharts fetches the
  latest observations from [NDBC](https://www.ndbc.noaa.gov/) for the
  stations in view; see [Show buoy observations](#show-buoy-observations).

## Show a forecast in SoundCharts

1. Choose **File** > **Open Dataset...** and pick the file. Choose the
   **Weather Data (GRIB2, NetCDF, CAP, CODSUS)** file type to see weather
   files, or **All files** for a GRIB2 file or bulletin with no extension.

   The Datasets panel lists it, and the Layer stack lists the layers it
   draws; the wave height fill's entry shows a colour legend.
2. Open **Display controls**. While a GRIB2 or NetCDF dataset is loaded, its
   **Weather** section has six switches for every weather dataset:

   | Switch | Default | Draws |
   |---|---|---|
   | **Isobars** | On | Mean sea level pressure every 4 hPa |
   | **Wind speed fill** | Off | Wind speed in Beaufort bands |
   | **Wave arrows** | Off | Arrows the way the waves travel, sized by height |
   | **Isotherms** | Off | Sea surface temperature every 2 °C |
   | **Currents** | On | Surface current arrows, coloured by speed |
   | **Air temperature** | Off | Dashed air temperature isotherms every 5 °C |

   The wave height, sea surface temperature, visibility and freezing spray
   fills and the wind barbs are always drawn; hide them for one dataset with its eye in the Datasets
   panel. The sea surface temperature fill has a band for each degree from
   0 °C to 30 °C, so fronts such as the edge of the Gulf Stream show as
   closely spaced bands.
3. Use the **Timeline** to step through the forecast. Each time step is a
   valid time of the run.
4. Click the map to pick. Object information starts with "Weather model
   data, not for navigation." and lists each parameter at the nearest grid
   point and time step, in knots, metres and degrees Celsius, visibility in
   nautical miles, and the freezing spray class.

Current arrows point the way the water flows. They use S-111's nine speed
bands, colours and sizes, from purple under 0.5 kn to red at 13 kn and
faster, so a current reads the same as in an S-111 dataset. Where an S-111
grid is loaded and shown, only its arrows are drawn inside its area; S-111
station and mesh datasets don't hide model currents.

Restricted visibility is drawn in grey in the three bands marine forecasts
use: very poor (fog, under 1000 m), poor (1000 m to 2 nautical miles) and
moderate (2 to 5 nautical miles). Good visibility isn't drawn, so most of
the chart stays clear. Its entry in the Layer stack shows the legend.

Freezing spray (vessel icing) is worked out from the 10 m wind, the 2 m air
temperature and the sea temperature with Overland's icing predictor, the
one NOAA's Ocean Prediction Center uses. It's drawn in plum in four classes:
light (under about 0.7 cm of ice an hour), moderate (to 2 cm), heavy (to
4 cm) and extreme. Where there's no icing nothing is drawn. It needs all
three inputs in one file. When a file has no sea temperature, as GFS doesn't,
the surface temperature stands in for it, with the land mask (LAND) to leave
out land; without the mask, only surface temperatures under −2 °C, which are
ice or land, are left out. The rates are for a fishing vessel of 20 to 75 m;
judge your own vessel's exposure from them.

The wave height, sea surface temperature, visibility and freezing spray
fills are clipped to water when an S-101 chart is loaded.
Wind barbs south of the equator are drawn mirrored, as the convention is.

## Show warnings

Open a CAP or NWS GeoJSON file as you would a grid. The **Timeline** steps
are when warnings start and end, so moving the time shows what's in force
then. Pick an area to see its headline, severity, times, sender and
description.

## Show a surface analysis

Open a coded surface bulletin as you would a grid. It draws over the isobars
of any grid you have open, in the symbols mariners know from radiofax
charts:

| Feature | Drawn as |
|---|---|
| Cold front | A solid line with filled triangles on the side it's moving towards |
| Warm front | A solid line with filled semicircles on the side it's moving towards |
| Occluded front | Triangles and semicircles in turn, on the same side |
| Stationary front | Triangles and semicircles in turn, on opposite sides |
| Trough | A dashed line, from 1:40,000,000 and larger |
| High or low | A bold **H** or **L** above a cross at the centre, with the pressure in hectopascals underlined below it from 1:60,000,000 and larger |

Everything is in one dark ink, heavier than the isobars, so a front reads
where it lies along an isobar. The symbols stay the same size and spacing
on screen at every zoom, and the bulletin's points are smoothed into a
curve as a forecaster would draw it. The bulletin doesn't say which side of
a stationary front each symbol faces, so the triangles go on the left of the
line as WPC lists its points and the semicircles on the right.

The Layer stack lists the analysis on **Other chart overlays**, above the
isobars, with a legend of the five line types and the pressure centre. Pick
a front or centre to see it in Object information: "Weather analysis, not
for navigation.", then the analysis time, when the next one is due (3 hours
later), its intensity if the bulletin gives one, and the bulletin's own
points as the count and the two ends. A centre shows its pressure and
position.

Only the bulletin's first analysis is drawn: for a forecast bulletin, its
first forecast time. Moving the Timeline doesn't change it yet.

## Show buoy observations

Turn on **NDBC buoy observations** in the Layer stack. It's off when you
first run SoundCharts. Observations are fetched only while the layer is on
and the view is less than 25° wide, and are refreshed every 10 minutes. A
station's symbol turns grey when its observation is more than 3 hours old.

## Show own-ship wind

SoundCharts reads wind, heading, motion and temperature sentences (`MWV`,
`MWD`, `HDT`, `HDG`, `VHW`, `RMC`, `VTG`, `MTW`, `XDR`) from an NMEA 0183
stream. Set the `SOUNDCHARTS_NMEA_INPUT` environment variable before you
start SoundCharts:

| Value | Input |
|---|---|
| `udp` or `udp:10110` | UDP broadcast on a port (10110 by default) |
| `tcp://host:10110` | A TCP server, such as a multiplexer |
| `path/to/recording.nmea` | A recorded file, replayed in a loop |

**Own-ship wind and weather (NMEA)** then appears in the Layer stack. True
wind is drawn as a barb beside own ship. Without a heading, the relative
wind is shown in grey. See the
[DynamicSources.Nmea README](../src/EncDotNet.S100.DynamicSources.Nmea/README.md)
for how true wind is found.

## Use the command line

`s100` reads the same files. `info` lists a grid's model, run, grid,
parameters, levels and time steps, a CAP file's warnings, or a surface
analysis's valid times and how many fronts and centres of each kind it has:

```bash
s100 info filter_gfswave.pl
s100 info alerts.atom
s100 info asus02.kwbc.cod.sus.txt
```

`render` draws the default layers (wave height fill, wind barbs and isobars)
at a time step, alone or over a chart:

```bash
s100 render gfswave.grib2 waves.png --time-step 2
s100 render --layer chart.000 --layer gfswave.grib2 --layer gfs.grib2 chart.png --time-step 1
```

A surface analysis renders the same way, alone or over a grid's isobars:

```bash
s100 render asus02.kwbc.cod.sus.txt fronts.png
s100 render --layer gfs.grib2 --layer asus02.kwbc.cod.sus.txt fronts.png --bbox -100,25,-50,55
```

`identify` samples the grids and finds the warning areas at a point:

```bash
s100 identify --layer gfswave.grib2 --layer alerts.atom --lat 38.5 --lon -74.5 --time 2026-10-25T03:00Z
```

See [info](cli.md#info), [render](cli.md#render) and
[identify](cli.md#identify) for their options. Over MCP, `list_time_steps`,
`sample_coverage` and `sample_coverage_along` work on the grids, and
`identify_features` on the warnings and on a surface analysis's fronts and
centres; see the [MCP server](mcp-server.md).

## What isn't supported

- **Other grids.** Lambert conformal (such as NWS NDFD), polar stereographic
  (such as GFS-Wave `arctic.9km`), Gaussian, rotated and curvilinear grids,
  and regional grids that cross 180° longitude. The file fails with a message
  naming the grid.
- **GRIB edition 1**, and GRIB2 CCSDS packing (5.42), which ECMWF Open Data
  uses. Repack those files as shown above.
- **Ensembles and probabilities** (GRIB2 product templates 4.1, 4.2, 4.11 and
  4.12).
- **NetCDF** files whose variables are in groups, CDF-5 files, variables with
  several levels, and compression filters other than deflate and shuffle
  (such as Zstandard).
- **BUFR** observations.
- **Surface analyses outside WPC's area**, and fronts in WPC's colours (blue
  cold, red warm). Fronts are drawn in one ink only. The Library doesn't
  fetch bulletins yet, and the Timeline doesn't choose between the times of a
  forecast bulletin.
- **Weather in route checks.** A route check doesn't consider wind, waves or
  model currents.
- **Ocean model servers.** The Library fetches currents only from ERDDAP.
  NOAA RTOFS is published only as whole-globe files on a curvilinear grid,
  and HYCOM's subset service isn't a Library source yet; download from it as
  shown above.
- **The S-100 weather products** S-412, S-413 and S-414 aren't published
  yet.

The [design note](design/weather-sources.md) explains these choices.

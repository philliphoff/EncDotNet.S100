# Show wind and waves on the chart

SoundCharts can draw forecast wind and waves, marine weather warnings and
live observations over your charts. This page covers which files it reads,
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
| Forecast and analysis grids | GRIB2, NetCDF-3 and NetCDF-4 with CF conventions | Wave height fill, wind barbs and isobars; optionally a wind speed fill and wave arrows |
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

When a file has only U and V, wind speed and direction are worked out from
them. Directions are the direction the wind or waves come from. Other
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

## Get the data

These sources are public. Check each provider's terms before you share images
made from its data. The Library can fetch some of these sources for you.

### GRIB2

- **NOAA NOMADS GRIB filter.** The GRIB filter pages on
  [nomads.ncep.noaa.gov](https://nomads.ncep.noaa.gov/) cut GFS and GFS-Wave
  to the variables, area and forecast hours you choose, and return one file.
  For waves choose HTSGW, PERPW and DIRPW; for wind and pressure choose UGRD
  and VGRD at 10 m above ground and PRMSL. The download has no file
  extension.
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
  dataset whose variables have CF standard names.

### Warnings and observations

- **CAP warnings.** The National Weather Service API returns active alerts,
  for example
  `https://api.weather.gov/alerts/active?area=AN` for the western North
  Atlantic marine area. Save the GeoJSON it returns, or request
  `application/cap+xml` for a CAP Atom feed. The API asks callers to send a
  `User-Agent` header that identifies them.
- **NDBC buoys.** There's nothing to download. SoundCharts fetches the
  latest observations from [NDBC](https://www.ndbc.noaa.gov/) for the
  stations in view; see [Show buoy observations](#show-buoy-observations).

## Show a forecast in SoundCharts

1. Choose **File** > **Open Dataset...** and pick the file. Choose the
   **Weather Data (GRIB2, NetCDF, CAP)** file type to see weather files, or
   **All files** for a GRIB2 file with no extension.

   The Datasets panel lists it with a **Not an S-100 product** badge. The
   Layer stack lists the layers it draws, and the wave height fill's entry
   shows a colour legend.
2. Open **Display controls**. While a GRIB2 or NetCDF dataset is loaded, its
   **Weather** section has three switches for every weather dataset:

   | Switch | Default | Draws |
   |---|---|---|
   | **Isobars** | On | Mean sea level pressure every 4 hPa |
   | **Wind speed fill** | Off | Wind speed in Beaufort bands |
   | **Wave arrows** | Off | Arrows the way the waves travel, sized by height |

   The wave height fill and wind barbs are always drawn; hide them for one
   dataset with its eye in the Datasets panel.
3. Use the **Timeline** to step through the forecast. Each time step is a
   valid time of the run.
4. Click the map to pick. Object information starts with "Weather model
   data, not for navigation." and lists each parameter at the nearest grid
   point and time step, in knots and metres.

The wave height fill is clipped to water when an S-101 chart is loaded.
Wind barbs south of the equator are drawn mirrored, as the convention is.

## Show warnings

Open a CAP or NWS GeoJSON file as you would a grid. The **Timeline** steps
are when warnings start and end, so moving the time shows what's in force
then. Pick an area to see its headline, severity, times, sender and
description.

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
parameters, levels and time steps, or a CAP file's warnings:

```bash
s100 info filter_gfswave.pl
s100 info alerts.atom
```

`render` draws the default layers (wave height fill, wind barbs and isobars)
at a time step, alone or over a chart:

```bash
s100 render gfswave.grib2 waves.png --time-step 2
s100 render --layer chart.000 --layer gfswave.grib2 --layer gfs.grib2 chart.png --time-step 1
```

`identify` samples the grids and finds the warning areas at a point:

```bash
s100 identify --layer gfswave.grib2 --layer alerts.atom --lat 38.5 --lon -74.5 --time 2026-10-25T03:00Z
```

See [info](cli.md#info), [render](cli.md#render) and
[identify](cli.md#identify) for their options. Over MCP, `list_time_steps`,
`sample_coverage` and `sample_coverage_along` work on the grids, and
`identify_features` on the warnings; see the [MCP server](mcp-server.md).

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
- **Weather in route checks.** A route check doesn't consider wind or waves.
- **The S-100 weather products** S-412, S-413 and S-414 aren't published
  yet.

The [design note](design/weather-sources.md) explains these choices.

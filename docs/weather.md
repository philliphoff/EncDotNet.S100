# Show wind and waves on the chart

SoundCharts can draw forecast wind and waves, sea surface temperature,
surface currents, restricted visibility, air temperature, freezing spray, precipitation, thunderstorm risk, cloud cover, marine weather warnings and live observations over your
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
| Forecast and analysis grids | GRIB2, NetCDF-3 and NetCDF-4 with CF conventions | Wave height fill, wind barbs, isobars, a sea surface temperature fill, current arrows, a restricted visibility fill and a freezing spray fill; optionally a wind speed fill, wave arrows, sea isotherms, air isotherms, a precipitation fill, a thunderstorm risk fill and a cloud cover veil |
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
| Precipitation rate | PRATE at the surface | `precipitation_flux` |
| Thunderstorm risk (CAPE) | CAPE at the surface | `atmosphere_convective_available_potential_energy` |
| Total cloud cover | TCDC for the entire atmosphere | `cloud_area_fraction` |

When a file has only U and V, wind or current speed and direction are worked
out from them. Wind and wave directions are the direction they come from;
current directions are the direction the water flows to, as in S-111.
Currents are read at whatever depth the file gives them, which is the
model's top layer: 0 m for HYCOM, about 0.5 m for Copernicus Marine. Sea surface
temperature can be in kelvin, degrees Celsius or degrees Fahrenheit. GFS's
surface temperature (TMP at the surface) isn't drawn as sea temperature,
because over land it's the temperature of the ground; it's read only for the
freezing spray risk. GFS gives the precipitation rate and cloud cover twice
at each forecast hour, as the value at that time and as an average over the
hours before it; the value at that time is read. CAPE for layers above the
ground (such as GFS's 180–0 hPa) and cloud cover for single levels or cloud
layers aren't read. Accumulated precipitation (APCP) isn't drawn: GFS
accumulates it over periods that change from one forecast hour to the next.
Other parameters are listed by `s100 info` but not drawn.

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
  surface, LAND at the surface, and the 10 m wind; for precipitation,
  thunderstorm risk and cloud cover choose PRATE and CAPE at the surface and
  TCDC for the entire atmosphere. The download has no file
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
  SoundCharts and `s100` recognise the contents. In SoundCharts, the
  Library's **Surface analysis — WPC** source fetches them for you; see
  [Show a surface analysis](#show-a-surface-analysis).
- **NDBC buoys.** There's nothing to download. SoundCharts fetches the
  latest observations from [NDBC](https://www.ndbc.noaa.gov/) for the
  stations in view; see [Show buoy observations](#show-buoy-observations).

## Show a forecast in SoundCharts

1. Choose **File** > **Open Dataset...** and pick the file. Choose the
   **Weather Data (GRIB2, NetCDF, CAP, CODSUS)** file type to see weather
   files, or **All files** for a GRIB2 file or bulletin with no extension.

   The Datasets panel lists it, and the Layer stack lists the layers it
   draws. The wave height fill's colour legend is in the **Legend** tab of
   the right dock: choose **View** > **Legend**, or **Legend** under the
   fill's Layer stack entry.
2. Open **Display controls**. While a weather dataset is loaded (a GRIB2 or
   NetCDF grid, a tropical cyclone or a surface analysis), its **Weather**
   section has nine switches for every weather grid, and three for tropical
   cyclones ([Show tropical cyclones](#show-tropical-cyclones)):

   | Switch | Default | Draws |
   |---|---|---|
   | **Isobars** | On | Mean sea level pressure every 4 hPa |
   | **Wind speed fill** | Off | Wind speed in Beaufort bands |
   | **Wave arrows** | Off | Arrows the way the waves travel, sized by height |
   | **Isotherms** | Off | Sea surface temperature every 2 °C |
   | **Currents** | On | Surface current arrows, coloured by speed |
   | **Air temperature** | Off | Dashed air temperature isotherms every 5 °C |
   | **Precipitation** | Off | The precipitation rate in rain radar colours |
   | **Thunderstorm risk** | Off | Where CAPE is 1000 J/kg or more |
   | **Cloud cover** | Off | A light grey veil from 25 % cloud cover |

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
   nautical miles, the freezing spray class, the precipitation rate in
   millimetres an hour, CAPE in J/kg and cloud cover in percent.

Current arrows point the way the water flows. They use S-111's nine speed
bands, colours and sizes, from purple under 0.5 kn to red at 13 kn and
faster, so a current reads the same as in an S-111 dataset. Where an S-111
grid is loaded and shown, only its arrows are drawn inside its area; S-111
station and mesh datasets don't hide model currents.

Restricted visibility is drawn in grey in the three bands marine forecasts
use: very poor (fog, under 1000 m), poor (1000 m to 2 nautical miles) and
moderate (2 to 5 nautical miles). Good visibility isn't drawn, so most of
the chart stays clear. Its legend is in the Legend tab.

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

Precipitation is drawn in rain radar colours: pale green for light rain from
0.1 mm/h, then green to 2.5 mm/h, yellow for moderate rain to 7.5 mm/h,
orange for heavy rain to 15 mm/h, red for very heavy rain to 50 mm/h, and
violet beyond. Drier areas aren't drawn.

Thunderstorm risk is drawn from CAPE, the energy a parcel of air rising from
the surface can gain, in ochre in three classes: moderate (1000 to
2500 J/kg), high (2500 to 4000 J/kg) and extreme (4000 J/kg and more). Under
1000 J/kg nothing is drawn. CAPE says how strong a thunderstorm or squall
could be if one forms, not that one will; over warm tropical seas moderate
CAPE is common. Read it with the precipitation and the official forecast.

Cloud cover is a light grey veil, drawn more faintly than the other fills,
in three bands: partly cloudy (25 to 50 %), mostly cloudy (50 to 75 %) and
cloudy (75 % and more). Precipitation, thunderstorm risk and cloud cover are
off until you switch them on, and each has its legend in the Legend tab.

The wave height, sea surface temperature, visibility, freezing spray,
precipitation, thunderstorm risk and cloud cover fills are clipped to water
when an S-101 chart is loaded.
Wind barbs south of the equator are drawn mirrored, as the convention is.

## Show warnings

Open a CAP or NWS GeoJSON file as you would a grid. The **Timeline** steps
are when warnings start and end, so moving the time shows what's in force
then. Pick an area to see its headline, severity, times, sender and
description.

## Show tropical cyclones

Add the storms NHC is issuing advisories for from the Library
([Get active storms from the Library](#get-active-storms-from-the-library)).
Or download an advisory's zips from NHC's
[GIS page](https://www.nhc.noaa.gov/gis/) into one folder: the forecast track
and cone (`al092022_5day_020.zip`), the wind radii (`al092022_fcst_020.zip`)
and the past track (`al092022_best_track.zip`). Then open the forecast zip,
or the folder, as you would a grid. SoundCharts draws the whole advisory in
one plum-grey storm ink, so it doesn't look like a chart colour or a warning:

- **Cone.** NHC's cone, tinted. The part beyond 72 hours (days 4 and 5) is
  also stippled. The outline is dashed. The storm's centre stays inside the
  cone only about two-thirds of the time, and its winds reach well outside it.
- **Wind radii.** The 34, 50 and 64 knot areas at the view time, nested, with
  the 64 knot core the strongest. Their outlines are dashed, solid and heavy.
- **Tracks.** The forecast track is a solid line, the past track a dotted
  line with a dot at each best-track position.
- **Symbols.** Each forecast point has its class's weather-map symbol: an
  open circle for a depression, arms for a storm, a filled circle for a
  hurricane or typhoon, and an outer ring for a major hurricane (category 3
  and above). A square centre is subtropical, and a circled × is
  post-tropical, a remnant low or a disturbance. South of the equator the
  symbols are mirrored, because storms there turn the other way.
- **Labels.** The storm is drawn larger, in a ring, with its class, name,
  wind and pressure, and under that the advisory number, its issue time and
  how the storm is moving. Forecast points at 12 hours, every
  24 hours and the last show their time and wind. Times follow the Local or
  UTC setting.

In the **Layer stack**, a storm has two entries: its lines and symbols under
**Other chart overlays**, and its cone and radii under **On-demand surface**.
Both legends are in one **Tropical cyclones** section of the Legend tab. Pick any part of the storm to see it in **Object
information**: the advisory, its issue time and when the next is due, the
position, wind, pressure and movement, the part you picked, and the radii in
each quadrant.

While a storm is loaded, three switches end the **Weather** section of
**Display controls**. They apply to every storm and are kept after a restart:

| Switch | Default | Draws |
|---|---|---|
| **Wind radii** | On | The 34, 50 and 64 knot areas |
| **Past track** | On | The best-track line and points |
| **Forecast times** | On | The time and wind beside forecast points |

The cone, the forecast track and points, and the storm have no switch. To
hide a whole storm, use its eye in the Datasets panel.

### Storm at the view time

The cone, the tracks and the forecast points are always drawn, whatever the
view time. The storm and its wind radii are drawn at the view time, so
scrubbing the Timeline moves them along the track:

| View time | Storm | Radii | Datasets row |
|---|---|---|---|
| On a forecast point | The forecast point, drawn large in a solid ring | The point's forecast radii | `18:00Z · T+24 h` |
| Between two forecast points | An estimate, in a dashed ring, labelled `est. Wed 9:00 PM · Adv 17 · NW 9 kt` | Each quadrant between the two points' | `est. 20:00Z · T+2 h` |
| Before the advisory, on the past track | The best-track position, in a solid ring, labelled `best track …` | The observed radii, where the best track has them | `12:00Z · best track` |
| After the last forecast point | None; the cone and tracks stay | None | A **Beyond forecast** tag |
| Before the first best-track point | None; the cone and tracks stay | None | A **Before track** tag |

Between two points the position follows the great circle, and the wind,
pressure and each radius change evenly with time. A radius that's zero at
one end shrinks to nothing; NHC forecasts no radii beyond 72 hours, so none
are drawn there. The estimate keeps the class of the earlier point. NHC
doesn't forecast these in-between positions, and Object information says so:
its **At view time** row gives the time, and **Basis** reads "Estimated
between forecast points; NHC doesn't forecast this position." Click a tagged
row's time to jump to the nearest end of the storm's track.

In Live mode the view time follows the clock, so the storm creeps along the
track between advisories.

The Timeline lists storms under **Tropical cyclones**, in a lane named for
the storm, such as **IAN**, with "adv 20 · 6 h" under it. Its band runs from
the first best-track point, faded, to the last forecast point. A tick marks
each forecast point, a solid diamond the advisory's issue time and a hollow
diamond when NHC's next regular advisory is due (03, 09, 15 and 21 UTC;
intermediate advisories can come sooner). Choose the storm in the step menu
and ‹ › step through its forecast points. The lane is grey with the
**Expired** tag once the last forecast point has passed.

### Get active storms from the Library

In the Library, choose **Add** and pick **Tropical cyclones — NHC** from the
weather sources. It has the `FEED` tag and nothing to choose: it lists every
storm NHC is issuing advisories for in the Atlantic, East and Central Pacific,
one item each, from NHC's active-storm list. Between storms the list is
empty.

Each item is named by the storm, such as **HURRICANE ZELDA · AL14**, with its
class symbol beside it. Under the name are the advisory, when it was issued
and when the next is due, then where the forecast ends and the storm's wind;
the bar shows how much of the forecast has passed. Downloading an item fetches
the advisory's forecast, wind radii and best track zips into one folder.

NHC usually issues an advisory every 6 hours. A newer advisory makes the
download outdated, as a newer model run does: the item gets the **New run**
tag and is listed under **Updates**, and downloading it again replaces the
storm on the chart in place. **Pin this run** keeps an advisory beside the
latest.

While a storm is loaded, the Library reads NHC's list every hour. When NHC
issues a new advisory for it, a notification says so ("HURRICANE ZELDA:
advisory 18 is available"), with **Load latest run** to download and load it.
Nothing is loaded without you, and a storm that isn't loaded, or one that
newly becomes active, doesn't notify; it just appears in the Library.

### Stale and final advisories

An advisory is stale 7 hours after it was issued, when a newer one is likely.
Its age is counted up to the view time, or up to now when you look ahead, so
stepping through a forecast doesn't make it older. A stale storm isn't faded:

- on the chart, the second line of its label says its age, such as
  `Adv 17 · 9 h old · newer advisory likely`;
- in **Object information**, a warning line under the notice says
  "Advisory 17 is 9 h old. NHC has probably issued a newer one.";
- in the Datasets panel, its row has the **Stale** tag;
- in the Library, the item has the **Stale** tag, its details say
  "Advisory 17 is over 7 hours old", and **Load latest run** fetches the
  newest.

When NHC stops listing a loaded storm, its last advisory was its final one:
the storm dissipated, became post-tropical or was handed over. The item
leaves the Library, but the storm stays loaded and is drawn as a final
advisory: its past track, and the storm with the post-tropical symbol and
"Final advisory issued" and the time on its label, with no cone, forecast or
radii. SoundCharts marks it with a file beside its zips
(`al142026_final.txt`), so it's drawn the same way after a restart.

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
isobars. Its Legend tab section shows the five line types and the pressure
centre. Pick
a front or centre to see it in Object information: "Weather analysis, not
for navigation.", then the analysis time, when the next one is due (3 hours
later), its intensity if the bulletin gives one, and the bulletin's own
points as the count and the two ends. A centre shows its pressure and
position.

### Get the latest analysis from the Library

In the Library, choose **Add** and pick **Surface analysis — WPC** from the
weather sources. It has the `FEED` tag. The latest high-resolution analysis
is ticked; tick **Forecast fronts · WPC** too to add WPC's coded forecast,
12 and 24 hours ahead, as a second item. Both cover WPC's area only, North
America and adjacent waters, which each item says.

Download and load an item as any other. While the analysis is loaded, the
Library reads the source again every 3 hours, as WPC issues a new analysis,
and loads the newer one in its place. **Load latest run** does it at once;
**Pin this run** keeps an analysis beside the latest.

### Time

An analysis or forecast is drawn from its valid time until 12 hours later,
then nothing is drawn. The Datasets row says "No analysis at view time" and
the Timeline lane hides; click the row's time to jump to the nearest one.

| Age at the view time | Chart | Panels |
|---|---|---|
| Up to 6 hours | Drawn as above | The analysis time, and how far it is from the view time |
| 6 to 12 hours | Lines and labels at half opacity, and each **H** and **L** says its age, such as `9 h old` | A **Stale** tag on the Datasets row, and a warning line in Object information: "This analysis is 9 h old. A newer one is likely." |
| Over 12 hours | Not drawn | "No analysis at view time" |

When several bulletins are loaded, one draws at a time: the one valid
latest at or before the view time. A newer analysis takes over from an
older one, and a forecast valid nearer the view time takes over from the
analysis. For the same valid time, the analysis wins over the forecast.
Hidden bulletins show "newer analysis drawn" on their Datasets row.

The Timeline lists bulletins under **Surface analysis**, in a lane named
**WPC** with "analysis · 3 h" or "forecast" under it. Step with ‹ › through
the forecast's valid times.

In the Library, an analysis more than 6 hours old has the **Stale** tag and
"Analysis … is over 6 hours old" in its details, with **Load latest run**.
WPC issues the forecast fronts about every 12 hours, so they are stale after
13.

## Show buoy observations

In the **Datasets** panel, choose **Add a data source** (the plug button)
and then **NDBC observations**. It's listed with your datasets and added
again each time SoundCharts starts, until you remove it. Observations are
fetched only while it's shown and the view is less than 25° wide, and are
refreshed every 10 minutes. A
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
parameters, levels and time steps, a CAP file's warnings, a surface
analysis's valid times and how many fronts and centres of each kind it has,
or a tropical cyclone advisory's storm and forecast:

```bash
s100 info filter_gfswave.pl
s100 info alerts.atom
s100 info asus02.kwbc.cod.sus.txt
s100 info al092022_5day_020.zip
```

For a tropical cyclone, download the advisory's zips from NHC's
[GIS page](https://www.nhc.noaa.gov/gis/) into one folder: the forecast track
and cone (`al092022_5day_020.zip`), and, for the wind radii and the storm's
past track, `al092022_fcst_020.zip` and `al092022_best_track.zip`. Then pass
the forecast zip. `render` draws the advisory as SoundCharts does
([Show tropical cyclones](#show-tropical-cyclones)).

`render` draws the default layers (wave height fill, wind barbs and isobars)
at a time step, alone or over a chart:

```bash
s100 render gfswave.grib2 waves.png --time-step 2
s100 render --layer chart.000 --layer gfswave.grib2 --layer gfs.grib2 chart.png --time-step 1
```

Layers that are off by default, such as precipitation, thunderstorm risk
and cloud cover, are drawn with `--show-layer` and their keys, which
`s100 info` lists:

```bash
s100 render gfs.grib2 storms.png --bbox -100,5,-60,32 --show-layer weather.precipitation,weather.cape
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
  cold, red warm). Fronts are drawn in one ink only. `s100 render` draws a
  stale analysis at full opacity.
- **Weather in route checks.** A route check doesn't consider wind, waves or
  model currents.
- **Ocean model servers.** The Library fetches currents only from ERDDAP.
  NOAA RTOFS is published only as whole-globe files on a curvilinear grid,
  and HYCOM's subset service isn't a Library source yet; download from it as
  shown above.
- **The S-100 weather products** S-412, S-413 and S-414 aren't published
  yet.

The [design note](design/weather-sources.md) explains these choices.

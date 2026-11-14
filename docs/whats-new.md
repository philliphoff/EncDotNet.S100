# What's new

This page lists notable changes to the libraries, tools and documentation, by
month. For package versions and release assets, see
[GitHub releases](https://github.com/philliphoff/EncDotNet.S100/releases).

## November 2026

- **`s100` as a container image.** `ghcr.io/philliphoff/s100` runs `s100`
  on Linux x64 and arm64 with nothing else to install. It runs as a non-root
  user, reads datasets from `/data`, and keeps tiles in `/cache` and the tide
  station catalogue and MCP Library in `/state`. Releases publish the version
  tags and `latest`; nightly builds publish `nightly`. `tiles serve`,
  `feed serve` and `mcp serve --transport http` answer `GET /healthz` without
  a token, and the new `s100 health` command checks it, for health checks in
  an image with no shell. `S100_NO_UPDATE_CHECK` turns off the update check.
  See [Run s100 in Docker](docker.md).
- **Serve and export a folder of charts.** `s100 tiles serve` and
  `s100 tiles export` take a folder and render every dataset in it and its
  subfolders: S-57 cells, an S-57 `ENC_ROOT` with its `CATALOG.031`, S-100
  exchange sets and loose datasets. `--only` keeps some product
  specifications. A folder is a built tile set only when it holds a
  `tiles.json` or numbered `{z}/{x}` subfolders. A cell copied into a served
  folder, or removed from it, is picked up at the next check.
- **`--bbox` portrays less.** `s100 tiles export` and `s100 tiles serve` leave
  out datasets that can't draw in the `--bbox` tiles before portraying them,
  so a small area of a large folder needs much less memory. The tiles written
  are the same.
- **Re-export a served archive.** `tiles export` writes a PMTiles or MBTiles
  archive beside the old one and moves it into place when it's finished, so
  `tiles serve` keeps serving the old tiles during an export, and a failed
  export leaves the old archive as it was. A Compose example renders tiles in
  advance for a small computer; see
  [Serve prebuilt tiles](docker.md#serve-prebuilt-tiles).
- **MCP over HTTP.** `s100 mcp serve --transport http` serves the MCP tools
  at `http://127.0.0.1:8300/mcp`, so agents on other computers or in other
  containers can connect. On an address other than loopback it requires a
  bearer token, it refuses requests from web pages on other origins, and it
  leaves out the tools that open files by path or change the Library unless
  you pass `--allow-changes`. See [MCP over HTTP](cli.md#mcp-over-http).
- **Serve charts to a web map or GIS.** A new task page walks through
  `tiles serve` with QGIS, MapLibre and Leaflet; see
  [Serve charts to a web map or GIS](scenarios/serve-chart-tiles.md).
- **A display plane for vessels.** `S98DisplayPlane.Vessels` (75) sits
  between the mariner's data and ECDIS alerts, so AIS targets drawn as a
  dataset stay above the chart. It's a stopgap until the layer order is
  derived from information categories. The Layer stack lists it as
  **Vessels**.
- **Own-ship weather in the Datasets panel.** With `SOUNDCHARTS_NMEA_INPUT`
  set, SoundCharts adds **Own-ship weather (NMEA)** to the Datasets panel at
  each start, instead of a Layer stack row; the barb still follows the own
  ship while it's shown.
- **API keys in the secret store.** SoundCharts saves the aisstream.io key
  in the computer's secret store (Credential Manager, the macOS Keychain or
  the Linux Secret Service) instead of `settings.json`, which keeps only a
  reference; a key already in `settings.json` moves there at the next start.
  Sources mark secret options with `[Secret]`, and
  `SourceCreationContext.ResolveSecret` reads the environment variable, then
  the store.
- **Data sources in the Datasets panel.** SoundCharts lists a live source
  with your datasets: **Add a data source** in the Datasets panel adds it,
  and you show, hide, reorder and remove it like a file. Sources you add are
  added again the next time SoundCharts starts. NDBC observations are the
  first: they've moved from the Layer stack to the Datasets panel, and if you
  had them turned on they're added for you. A source runs only while it's
  shown, and fetches only the area in view.
- **Sources that aren't files.** A `SourceRegistration` describes a live feed
  or polled service: its id, name, spec, options and when a host runs it,
  and `Create` builds its processor from the saved options, loggers, a clock
  and an HTTP client factory. `SourceRegistry.Default` holds the NDBC
  observations (`ndbc.realtime`) and own-ship weather from NMEA
  (`nmea.weather`). A host starts, pauses and points the processor at the
  area in view through `IHostedDatasetProcessor`, and disposing it stops the
  source. `NmeaInput.Parse` turns an input spec such as `udp:10110` or
  `tcp://host:10110` into an NMEA input.
- **Vessel symbology as a portrayal.** Own-ship and AIS targets are drawn by
  `VesselPortrayal`, a code-defined portrayal of S-100 Part 9 drawing
  instructions, so they render headless and in any backend, not only on the
  Mapsui map. The symbology is the same: course vector and arrowhead, and a
  disc or the true-scale outline with the antenna cross. The outline is now a
  line without the translucent fill, as S-52 draws it, and it switches with
  the disc at the same scale at every latitude. `InstructionBuilder` gains
  `PointAt` and `Path` for augmented geometry. `OwnShipRenderer` and
  `AisVesselRenderer` are obsolete.
- **Live sources as datasets.** `DynamicSourceProcessor` wraps any
  `IDynamicFeatureSource` and its `IDynamicFeaturePortrayal` as a dataset
  processor, so a live source can be picked, described, rendered headless and
  ordered by S-98 plane like a file, and is drawn again when it changes. The
  NDBC observations and own-ship weather from NMEA work through it, under the
  new `SpecName.Ndbc` and `SpecName.Nmea` format names. SoundCharts still
  draws them as overlays for now.
- **Datasets that change after loading.** A processor that implements
  `ILiveDatasetProcessor` raises `Changed` when its data changes, such as a
  feed polled on a timer, and the Mapsui map session draws the dataset again:
  it waits 250 ms for more changes, picks up new available times for the
  timeline, and renders with the presentation the dataset was last drawn
  with. Failures are reported through `DatasetRenderFailed` with the new
  `MapSessionRenderKind.LiveRefresh`.
- **Station series and warnings are pick sections.** An S-104 or S-111
  station's time series is now a `StationSeriesSection`, and an S-124
  warning part's message a `NavigationalWarningSection`, both in
  `FeatureInfo.Sections`. SoundCharts chooses a station's chart from the
  series' channels rather than the product name, so a station from another
  source with a water level height, or a current speed and direction, gets
  the same chart, and a series with other channels shows its rows.
  `FeatureInfo.StationSeries` and `FeatureInfo.NavigationalWarning` are
  obsolete and read the sections.
- **GeoPackage tiles.** `s100 tiles export -o chart.gpkg` writes the tiles as
  an OGC GeoPackage 1.4 tile pyramid in Web Mercator, a single file that QGIS,
  ArcGIS Pro, GDAL and field apps such as QField open offline.
  `s100 tiles serve` serves a GeoPackage too, including Web Mercator ones that
  GDAL writes. See [Tiles export](cli.md#tiles-export).
- **GeoTIFF export.** `s100 tiles export -o chart.tif` writes one
  Cloud-Optimized GeoTIFF in Web Mercator for tools that take a single
  georeferenced image: the maximum zoom at full resolution, lower zooms as
  overviews. See [Tiles export](cli.md#tiles-export).
- **`s100 tiles serve` is a WMS service.** `tiles serve` now also answers
  OGC WMS 1.3.0 (and 1.1.1) requests at `wms`. GetMap draws any area in
  EPSG:3857, EPSG:4326 or CRS:84 from the served tiles. GetFeatureInfo
  reports the features and coverage values at a clicked point, as
  `s100 identify` does, in HTML, GeoJSON or plain text, so the QGIS
  Identify Features tool works on the chart. See [WMS](cli.md#wms).
- **Pick sections.** Object Information content that isn't attribute rows
  now comes through one extension point. `FeatureInfo.Sections` holds a
  feature's sections; `IPickContributor`s registered in
  `PickContributorRegistry` add them for any product or source, and each kind
  is registered in `PickSectionKindRegistry` so MCP can serialize it. The ice
  egg code is the first: `IceEggCodeContributor` gives an egg code to an S-411
  sea-ice or lake-ice feature, or to a feature from any other source that
  carries S-411 concentration codes. `identify_features`, `pick_features` and
  `describe_feature` now report a feature's `sections`, so agents get the egg
  code too. `FeatureInfo.EggCode` is obsolete, and
  `GmlDatasetProcessorBase.BuildEggCode` is gone.
- **Tide stations in SoundCharts.** A **Tide stations · predicted** row
  under *Other chart overlays* draws NOAA's tide stations from 1:500,000 as
  S-104 gauges with a dashed frame: rising, falling or turning at the view
  time, with the height from 1:150,000 and the name from 1:50,000. Stations
  are thinned to 40 px and hidden where S-104 covers them. Their key is a
  section in the Legend tab. A station's Object Information card has the
  predicted height now, the curve 12 hours each side of the view time, the
  last high or low and the next four, the reference station and offsets of a
  subordinate station, whether depths use it, and the "not a forecast" note.
  The station list's date and **Refresh** sit under the tide predictions
  setting, and it's downloaded from NOAA on first use. The MCP tools
  `get_tide_display` and `set_tide_display` read and set it, and
  `pick_features` with `select` now shows live overlay features such as a
  station.
- **Current stations in SoundCharts.** A **Current stations · predicted**
  row draws NOAA's tidal current stations from 1:500,000: S-111's arrow in
  its speed band's colour rising from a dashed station frame, with the speed,
  or a slack bar and the turn time. Stations are hidden where S-111 covers
  them, and the Legend tab has their speed bands. A station's card has the
  current now, the speed 12 hours each side of the view time, the slacks and
  maxima, the flood and ebb directions and the depth; currents are for
  display only, not route checks. `set_tide_display` turns them on with
  `currentStations`.
- **Predicted water level on the chart and the Timeline.** The DEPTHS pill
  says when depths are adjusted by a prediction ("+ water level 14:20 ·
  predicted", or "partly predicted" beside S-104), its flyout lists the
  predicted stations, offers **Use tide predictions** where no S-104 meets the
  view, and says when NOAA has revised the station list. The adjusted-area
  edge and the new seam where S-104 hands over to predictions are clipped to
  the loaded charts' water, and a picked station outlines its cell. With
  predictions on, the Timeline and its time HUD show even with nothing timed
  loaded: a **Tide predictions** lane follows the station nearest the map
  centre, and *All loaded* shows the next 48 hours. `get_depth_basis` reports
  `predicted` and the stations.
- **`s100 tiles serve` is a WMTS service.** Besides its XYZ URLs and
  TileJSON, `tiles serve` now answers OGC WMTS 1.0.0 requests: a
  capabilities document at `wmts/1.0.0/WMTSCapabilities.xml`, and GetTile in
  the KVP and RESTful encodings, with palettes as styles and S-104 and S-111
  time steps as a `Time` dimension. QGIS, ArcGIS Pro and GDAL can add the
  tiles from that one URL. See [WMTS](cli.md#wmts).
- **Tide predictions from the shell and for agents.** `s100 tides stations`
  lists tide stations near a point, `s100 tides predict` predicts a station's
  heights or highs and lows as a table, CSV, JSON or an S-104 file, and
  `s100 tides update` downloads NOAA's stations into an offline catalogue.
  The MCP tools `list_tide_stations` and `predict_tide` do the same for
  agents, in SoundCharts and `s100 mcp serve`, and with tide predictions on,
  `sample_coverage` fills S-104 gaps with a prediction whose `source` names
  the station and carries the caveat. See
  [`tides predict`](cli.md#tides-predict) and
  [Find when the tide is above a height](scenarios/tide-above-a-height.md).
- **Tidal current predictions from the shell and for agents.**
  `s100 tides currents stations` lists current stations near a point, and
  `s100 tides currents predict` predicts a station's speeds and directions or
  its slacks and maxima as a table, CSV, JSON or an S-111 file. The MCP tools
  `list_current_stations` and `predict_current` do the same for agents, and
  with tide predictions on, `sample_coverage` fills S-111 gaps with a
  predicted current. See
  [`tides currents predict`](cli.md#tides-currents-predict) and
  [Find slack water in a pass](scenarios/slack-water-in-a-pass.md).
- **Tidal current predictions.** `EncDotNet.S100.Tides` predicts tidal
  currents: `CurrentPredictor` for a harmonic current station (major axis,
  or both axes for a rotary current) and `SubordinateCurrentPredictor` for a
  subordinate one by NOAA's method. Each gives the current at a time, a
  velocity series, and slack waters and maximum floods and ebbs, matching
  NOAA's own 2026 predictions to a minute and a few mm/s. The
  `EncDotNet.S100.Tides.Stations` catalogue now holds NOAA's current
  stations too, per depth bin, with subordinate stations' differences and
  ratios. See the [Tides README](../src/EncDotNet.S100.Tides/README.md#predict-tidal-currents).
- **Current predictions as S-111.** `EncDotNet.S100.Datasets.S111` writes
  station series as S-111 Edition 2.0.0 files (data coding format 8), and
  `S111CurrentPredictions` turns a station's current predictions into one,
  marked as a non-authoritative astronomical prediction (producer `ZZ01`).
  See the [S-111 README](../src/EncDotNet.S100.Datasets.S111/README.md#write-a-station-series).
- **Tide predictions as S-104.** `EncDotNet.S100.Datasets.S104` writes
  station series as S-104 Edition 2.0.0 files (data coding format 8), and
  `S104TidePredictions` turns a station's harmonic predictions into one, on
  the station's chart datum. Predicted files carry the producer code `ZZ01`
  and say they're astronomical predictions, not an official product and not
  for navigation. They read back through `S104DatasetReader` unchanged and
  pass the S-104 validation pack. The HDF5 tree they're built from,
  `Hdf5WriteGroup`, and `PureHdfWriter` are the project's first HDF5 writers.
  See the [S-104 README](../src/EncDotNet.S100.Datasets.S104/README.md#write-a-station-series).
- **Tide predictions.** The new `EncDotNet.S100.Tides` package predicts the
  astronomical tide at a station from its harmonic constituents: the height
  at a time, a series at a fixed step without allocating per sample, and the
  high and low waters in a period. It knows NOAA's 37 standard constituents
  and the 83 further ones NOAA lists at some stations, and gives heights on
  mean sea level or a chart datum such as MLLW. Against NOAA's published
  predictions at five stations it agrees to under a millimetre RMS. See the
  [package README](../src/EncDotNet.S100.Tides/README.md).
- **Tide stations.** The new `EncDotNet.S100.Tides.Stations` package is the
  catalogue the predictions come from. It reads NOAA CO-OPS's tide stations,
  harmonic constants, datums and subordinate-station offsets, keeps them in
  a JSON file that works offline, finds stations near a point or in a box,
  and predicts a station on a chart datum such as MLLW or LAT with its
  provenance ("from Seattle 9447130, with offsets"). A cheap check says when
  NOAA has newer data, such as a new tidal datum epoch. The same JSON format
  lets you bring your own stations. `EncDotNet.S100.Tides` gains
  `SubordinateTidePredictor`, which predicts a subordinate station by NOAA's
  method; its highs and lows match NOAA's at three Puget Sound stations to
  under a minute and a millimetre. See the
  [package README](../src/EncDotNet.S100.Tides.Stations/README.md).
- **Relief shading strength.** The S-102 Relief shading switch in Display
  controls gains a **Strength** control while it is on: Subtle, Medium (the
  default) or Strong. It is kept with the display settings and saved with
  display combinations, and the MCP tool `set_bathymetry_display` takes
  `reliefStrength`. At Dusk and Night, slopes facing the light are lightened
  less (half and 30 %), so they don't glare on a dark display. The new
  `EcdisDisplaySettings.LayerOptions` carries map-wide layer settings that
  are more than on or off.
- **Relief shading is marked on the map.** While S-102 relief shading is on
  and S-102 is in view, the status bar's `DEPTHS` pill reads
  `Charted · Relief`, and its popup says relief is on, as a display aid that
  leaves depth colours and values unchanged, with an **Off** button. The
  S-102 legend ends with a *Relief shading (display aid)* row.
- **Neighbouring S-102 cells join up.** Where two loaded S-102 cells meet,
  the dashed "limit of S-102 data" line no longer runs through the water
  between them, and SoundCharts no longer shows a light seam along the
  join. The limit line stays wherever S-102 data ends and S-101 takes over.
  A new S-98 rule, R-102-102-A, does this. It reads where each cell has
  data from the new `CoveragePortrayalResult.DataArea`.
- **Legend tab.** SoundCharts' right dock gains a tab strip, **Object
  information** · **Legend**, and the legends of what the map draws move to
  the Legend tab: one collapsible section per product or overlay (weather
  fields, tropical cyclones, fronts, AML), in draw order, with **In view |
  All loaded**, a filter and **Collapse all**. Swatches sit on the chart
  background of the current palette. Open it with **View** > **Legend**
  (Ctrl+Shift+L) or the **Legend** link under a Layer stack entry, which
  replaces the entry's inline legend. It never opens by itself, and a pick
  switches the dock back to Object information. When the dock is too narrow
  for the tab labels, the tabs show icons only.
- **Route check in the Legend tab.** While Check route is on, the route
  check's legend (UKC at ETA, findings, storm at ETA) is the top section of
  the Legend tab. The compact card at the map's bottom left shows the same
  rows while the tab isn't shown, and hides when it is. The MCP tool
  `set_legend` opens or closes the tab, sets In view and expands or collapses
  a section; it and `get_legend` replace `get_map_legend` and
  `set_map_legend`, which are removed.
- **Hover to find in the Legend tab.** Resting the pointer on a Legend row,
  or moving keyboard focus to it, rings the features that row describes on
  the map, in the pick highlight's accent: a catalogue symbol (S-101, S-57),
  a front, a warning area or an AML class, up to 100 features drawn in the
  current view. A band of a colour or arrow ramp (S-102 depths, a weather
  field, S-111 speeds) tints its area in view instead
  ([#1433](https://github.com/philliphoff/EncDotNet.S100/issues/1433)). The
  rings and tint clear when you leave the row or move the map. The MCP tool
  `set_legend` takes `findRow` to ring a row and returns what is ringed, or
  tinted, as `find`.
- **S-102 relief shading.** An optional hillshade over the S-102 depth bands
  shows the shape of the seabed (dredged pits, sand waves, berth toes) in
  high-resolution bathymetry. It lights the depth surface from the north-west
  and darkens or lightens each band colour by slope, so the bands and the
  safety contour keep their meaning. It isn't part of the S-102 portrayal, so
  it's off by default. In SoundCharts, switch it on in the new **Bathymetry**
  section of Display controls, shown while an S-102 dataset is loaded, or with
  the `get_bathymetry_display` and `set_bathymetry_display` MCP tools.
  `s100 render --show-layer s102.relief` draws it. `ReliefShading` and
  `S102Relief` are the library types, and `StyledCoverageLayer.ReliefShade`
  carries the shading to the coverage renderers. Zoomed out, SoundCharts draws
  the shaded surface with mipmaps (`SmoothRasterStyle`), so the shading
  averages out smoothly instead of turning into a speckled pattern. The shaded
  surface goes to the map as an image, without a PNG round trip, so relief
  costs about the same as the flat surface: on six 1 m BSH cells, portrayal
  takes 7.1 s with relief against 9.2 s without (it was 16.8 s).
- **Additional Military Layers portrayal.** The new
  `EncDotNet.S100.Datasets.Aml` package draws NATO AML cells with
  SoundCharts' own portrayal (not the restricted official one): dash-dot lines
  on a halo, unfilled areas with inward ticks, framed point symbols and italic
  labels, in air, operational, seabed and environmental colour families for
  Day, Dusk and Night. It covers the RAL air and operational classes, which
  the AML 3.0 air trials share; airspace shows its limits as a stacked
  fraction. Styles are data in modules, and the legend is built from them. A
  new S-98 plane, `S98DisplayPlane.AdditionalMilitaryLayers` (45), sits
  between the chart overlays and the cautions. Text instructions gain
  `FontSlant` for italic labels. See the
  [Datasets.Aml README](../src/EncDotNet.S100.Datasets.Aml/README.md).
- **Additional Military Layers display controls.** SoundCharts' display
  panel gains an **Additional Military Layers** section while an AML cell is
  loaded: a switch per AML product with its cell count, **Follow AML scale
  bands** (on by default) and a **Legend** flyout built from the AML styles.
  With the bands followed, each AML cell shows out to its scale band's
  coarsest display scale, and a finer band of the same product wins where its
  cells cover a coarser cell (`AmlScaleBandVisibility`). A corner chip says
  CLB isn't used for depth checks while CLB is drawn. The switches are kept
  after a restart, and the `get_aml_display` and `set_aml_display` MCP tools
  read and set them.
- **S-412 Marine Weather Warnings (draft).** The new
  `EncDotNet.S100.Datasets.S412` package reads S-412 GML datasets (wind,
  significant wave height, restricted visibility, thunderstorm and ice
  accretion warnings) and portrays them with the draft WMO portrayal
  catalogue, now bundled with the feature catalogue in
  `EncDotNet.S100.Specifications`. `s100 render` and the other
  hosts detect S-412 and draw it on the S-98 cautions and warnings plane, with
  S-124. `S412MarineWeatherWarnings` is the typed model. S-412 isn't
  approved yet, so expect its catalogues to change. Its validation pack
  (`S412MarineWeatherWarningRules`, 14 rules, all warnings while the
  specification is a draft) checks the feature catalogue's mandatory
  attributes and listed values, times, references and geometry.
  Warnings follow the Timeline: each is drawn only from the start to the end
  of its date range and until its cancellation date, by the rule S-124 and
  CAP warnings use, and `s100 render --time-step` draws a step.
  `describe_feature` decodes a warning's threshold, Beaufort force and
  message in each language. See the
  [S-412 README](../src/EncDotNet.S100.Datasets.S412/README.md).
- **Picks match the map's time.** MCP `identify_features` takes an optional
  `time` and returns only the S-412 and CAP warnings in force then, the
  surface analysis valid then and a tropical cyclone's position then. In
  SoundCharts, `identify_features` without a time and `pick_features` follow
  the map: its view time and the S-124 parts it hides. Hosts supply what
  their map draws through `IMapDrawState` (`S100McpServerOptions.MapDrawState`),
  and `FeatureAccessor.GetFeatures` has an overload that takes the time.
  `PickFeaturesTool` now takes the `IdentifyFeaturesTool` it uses.
- **Faster multi-cell `s100 render` and `s100 tiles`, and HiDPI captures.**
  The CLI and `s100 mcp serve` now render through the chart engine
  (`EncDotNet.S100.Rendering.Engine`): cells parse, portray and build their
  scenes in parallel, so a 17-cell composite renders in about 10 s instead of
  14 s, and tile blocks render as scheduled jobs in the order they're written.
  `render_to_image`'s `pixelDensity` now applies in `mcp serve`, as in
  SoundCharts. Finer cells hide coarser ones by their coverage footprint, not
  its bounding box, so composites where a coarse cell's coverage isn't
  rectangular can change slightly. Breaking changes: `HeadlessCompositor`,
  `HeadlessCompositeScene`, `HeadlessCompositeInput`, `HeadlessCompositeOptions`,
  `HeadlessTileRenderer`, `HeadlessTileOptions`, `HeadlessTile` and `TileBlock`
  leave `EncDotNet.S100.Datasets.Pipelines`; use `ChartCompositor`,
  `ChartComposition`, `ChartCompositeInput`, `ChartCompositeOptions`,
  `ChartTileRenderer`, `ChartTileOptions`, `ChartTile` and `TileBlock` in
  `EncDotNet.S100.Rendering.Engine`, where `ChartCompositor` now takes its S-98
  authority explicitly. `S100CompositeOptions.PixelDensity` is new. See the
  [Rendering.Engine README](../src/EncDotNet.S100.Rendering.Engine/README.md#render-images-and-tile-sets).
- **Tropical cyclones on a route.** With an NHC advisory loaded, Check
  route, `check_route` and `s100 route check` test the route against the
  storm at each ETA: stretches inside its 34, 50 or 64 kt wind radius (in the
  quadrant the vessel is in) and inside the forecast cone, the closest
  approach to its centre, and stretches reached after the forecast ends (not
  checked). By default the 34 kt radius is tested, the 50 and 64 kt radii are
  Must review, and the 34 kt radius and the cone are Check; SoundCharts sets
  these in **Settings** > **Chart display** > **Mariner Settings** >
  **Tropical cyclones on a route**, MCP with `set_route_weather_settings` and
  `check_route`'s `cyclone*` arguments, and the CLI with `--cyclone-radius`,
  `--cyclone-must-review` and `--no-cyclone-cpa`. The verdict card's
  **Weather** row counts the storm's findings and gives the closest approach,
  and the map draws the storm at that ETA. In the libraries,
  `RouteCheckSources.Cyclones` (`IRouteCyclone`),
  `RouteCheckSettings.TropicalCyclones` (`TropicalCycloneCheckSettings`),
  `WeatherCheckResult.Cyclones`, `TropicalCycloneDetails` and the
  `weather.tc_*` codes are new. Breaking changes: `WeatherVerdict` gains
  `InsideCycloneRadius` as its first value, `CycloneCheckStorm` and
  `WeatherSample.Cyclones` are new, and the weather check runs, rather than
  being skipped, when only an advisory is loaded. See
  [Tropical cyclones](route-checks.md#tropical-cyclones).

- **Weather along a route.** Check route, `check_route` and
  `s100 route check` gain a weather check: with a GRIB2 or NetCDF forecast
  loaded, they sample the route every nautical mile at the ETA and compare
  the wind at 10 m and the significant wave height with the vessel's limits.
  Stretches above a limit are Check findings with their time window ("Wind
  above 25 kn for 3 h", `weather.wind_above_limit`,
  `weather.waves_above_limit`); where the forecast ends before the vessel
  gets there, or a grid doesn't cover it, the stretch is reported as not
  checked (`weather.forecast_ends` and others), never passed. Each sample
  reads the nearest grid node and time step, and the finest grid with the
  newest run wins where grids overlap. In SoundCharts the limits are in
  **Settings** > **Chart display** > **Mariner Settings** > **Weather on a
  route** (`get_route_weather_settings`, `set_route_weather_settings`), the
  verdict card gains a **Weather** row whose tooltip names the limits in use,
  and **Findings** gains a **Weather** chip. An RTZ route's `vesselMaxWind`
  (knots) and `vesselMaxWave` are used where no limit is set. `check_route`
  takes `maxWindKnots` and `maxWaveHeightMeters` and returns a `weather`
  section with an optional departure sweep; the CLI takes `--max-wind` and
  `--max-wave`. In the libraries, `WeatherCheck` joins the built-in checks,
  with `IRouteWeatherSource` (on `RouteCheckSources.Weather`), `WeatherLimits`
  on `RouteCheckSettings` and `PassagePlan.VesselLimits`, `WeatherDetails`,
  `WeatherFindings`, `RouteFindingCategory.Weather` and
  `RtzPassagePlan.VesselLimits`. Breaking changes: `RouteChecker` runs five
  built-in checks, so `RouteCheckReport.Checks` has a `weather` entry; the
  `PassageRouteSnapshot` record has a new last positional parameter. See
  [Weather along the route](route-checks.md#weather-along-the-route).

- **Precipitation, thunderstorm risk and cloud cover on the chart.** GRIB2
  and NetCDF files with a precipitation rate, CAPE or total cloud cover,
  such as GFS, can now draw them: precipitation in rain radar colours from
  0.1 mm/h, thunderstorm risk in ochre where CAPE is 1000 J/kg or more
  (moderate, high and extreme), and cloud cover as a light grey veil from
  25 %. All three are off by default, behind new **Precipitation**,
  **Thunderstorm risk** and **Cloud cover** switches in Display controls
  (and in `set_weather_display`), and each has a legend. Picks give the rate
  in mm/h, CAPE in J/kg with its risk, and cloud cover in percent, and the
  Library's GFS sources can fetch PRATE, CAPE and TCDC. `s100 render` gains
  `--show-layer` to draw layers that are off by default. In the libraries,
  `WeatherParameters` gains `PrecipitationRate`, `Cape` and `CloudCover`,
  `WeatherUnit` gains `KilogramsPerSquareMetrePerSecond`, `JoulesPerKilogram`
  and `Percent`, `WeatherLevel` gains `EntireAtmosphere` (GRIB2 fixed
  surface 10), and the new styles are `PrecipitationStyle`,
  `ThunderstormRiskStyle` and `CloudCoverStyle`. `WeatherCoverageStyle.Opacity`
  is now set per style, and `StyleVersion` is 7. See
  [Show wind and waves on the chart](weather.md).

- **Tropical cyclones from the Library.** A new Library source,
  **Tropical cyclones — NHC** (`FEED`), lists every storm NHC is issuing
  advisories for in the Atlantic, East and Central Pacific, one item each,
  named by the storm with its class symbol. An item downloads the advisory's
  forecast, wind radii and best track zips, and NHC's next advisory replaces
  it in place. While a storm is loaded, the Library reads NHC's list every
  hour and announces a new advisory for it, with **Load latest run**; nothing
  is loaded on its own, and a storm newly active doesn't notify. An advisory
  is stale 7 hours after it's issued ("Advisory 17 is over 7 hours old"); a
  stale storm isn't faded but says its age on its label and in Object
  information. A loaded storm NHC stops listing is drawn as its final
  advisory: past track and a post-tropical storm, "Final advisory issued".
  **Display controls** shows its **Weather** section for cyclones and surface
  analyses too, and ends it with **Wind radii**, **Past track** and
  **Forecast times** switches while a cyclone is loaded; the new
  `get_weather_display` and `set_weather_display` MCP tools read and set every
  Weather switch. In the libraries, `WeatherServiceKind` gains `NhcStorms`
  (`NhcActiveStorms` reads `CurrentStorms.json`), `RemotePart` gains
  `FileName` (a download's parts saved side by side), `TimeSemantics` gains
  `StaleAfterIssue`, `TropicalCycloneAdvisory` gains `IsFinal`,
  `NhcAdvisoryFiles` gains `IsFinal` and `FinalMarkerName`, and
  `TropicalCycloneStyle` gains the switch keys. Breaking changes:
  `MapsuiMapTimedDataset.StaleAgeAt` takes an optional clock, and
  `TimeSemantics` has a new last positional parameter. See
  [Get active storms from the Library](weather.md#get-active-storms-from-the-library).

- **Tropical cyclones follow the Timeline.** The cone and forecast always
  draw; the storm and its wind radii are drawn at the view time, so
  scrubbing moves them smoothly along the track. Between forecast points the
  storm is an estimate, in a dashed ring labelled "est.", with its position
  on the great circle and its wind and radii in between; before the advisory
  it follows the best track; beyond the forecast the cone stays and the
  Datasets row has a **Beyond forecast** tag. Object information gives the storm at the view
  time and says when it's an estimate. The Timeline groups storms under
  **Tropical cyclones**, in a lane named for the storm with a tick at each
  forecast point and diamonds at the issue time and the next advisory due;
  the step menu lists the storm, so ‹ › step its forecast points. In Live
  mode the storm follows the clock. In the libraries:
  `TropicalCycloneAdvisory.StormAt` and `TropicalCycloneStormAt`,
  `TimeStepSelection.Continuous` (and `MapsuiTimeSelectionKind.Continuous`),
  `ITimeAwareDatasetProcessor.NextIssueTime`, `TimelineLabel` and
  `TimelineDetail` (on `MapsuiMapTimedDataset` too), and
  `TropicalCycloneStyle.StormDetailLabel`; `TropicalCycloneDatasetProcessor`
  is time-aware. `get_timeline_state` reports a lane's `sub`, `issued`,
  `nextIssue` and `stepTicks` and a layer's warning `tag`. Breaking
  change: a storm's own parts (the storm and its radii) now come after the
  advisory's other parts in `EnumerateFeatures`. See
  [Show tropical cyclones](weather.md#storm-at-the-view-time).

- **Surface analyses follow the Timeline, from the Library.** A WPC
  bulletin's analysis or forecast is now drawn from its valid time for 12
  hours: after 6 it's stale, at half opacity with its age on each **H** and
  **L**, a **Stale** tag in the Datasets panel and a warning line in Object
  information; beyond 12 hours the Datasets row says "No analysis at view
  time". Of several loaded bulletins, the one valid latest at or before the
  view time draws, so a newer analysis or a nearer forecast takes over. The
  Timeline groups them under **Surface analysis** in a **WPC** lane. A new
  Library source, **Surface analysis — WPC** (`FEED`), lists the latest
  analysis and, when chosen, the forecast fronts; a loaded analysis is
  replaced by the next every 3 hours, and is stale after 6. In the
  libraries, `TimeSemantics` gains `StaleAfter` and `Supersession`,
  `RenderContext` gains `TimeStepAge`, `VectorSubLayer` gains `Opacity`,
  `PickAttribute` gains `StaleNotice`, and `WeatherServiceKind` gains
  `WpcBulletins`, with `WeatherService.StaleAfterHours` and `RefreshHours`
  and `WeatherServiceDataset.File`. `get_timeline_state` reports a stale
  layer's `staleHours`. Breaking changes:
  `SurfaceAnalysisDatasetProcessor.DrawnAnalysisOf` takes a time step, and
  `SurfaceAnalysisStyle.BuildCentre` takes the analysis's stale age. See
  [Show a surface analysis](weather.md#show-a-surface-analysis).

- **Tropical cyclones on the chart.** SoundCharts and `s100 render` now draw
  an NHC advisory in one plum-grey storm ink: the cone, tinted and stippled
  beyond 72 hours, the 34, 50 and 64 kt wind radii, the forecast and past
  tracks, a WMO weather-map symbol at each forecast point (mirrored south of
  the equator), and the storm with its name, wind, pressure, advisory and
  motion. Times on the chart follow the Local or UTC setting. In the Layer
  stack a storm has two entries, one for its lines and symbols and one for
  its cone and radii, each with a legend. Picking any part shows one storm in
  Object information, with the advisory, the next advisory due, the
  intensity, the part picked and the radii by quadrant. Not for navigation.
  New in the libraries: `TropicalCycloneStyle` and the `WXTC*` tokens in
  `EncDotNet.S100.Datasets.Weather`, `RenderContext.DisplayTimeZone`,
  `MapPresentationState.DisplayTimeZone`, `InstructionBuilder.Point`'s
  `symbolScale` and `TropicalCycloneWindRadii.QuadrantSurface`. Breaking
  changes: `EncDotNet.S100.Datasets.Weather` now depends on
  `EncDotNet.S100.Datasets.TropicalCyclones`, and
  `TropicalCycloneDatasetProcessor.NotForNavigationNotice` reads "Tropical
  cyclone forecast, not for navigation. Follow official NHC advisories." See
  [Show tropical cyclones](weather.md#show-tropical-cyclones).

- **Fronts and pressure centres on the chart.** A WPC coded surface
  bulletin now draws its cold, warm, occluded and stationary fronts and its
  troughs with the triangles and semicircles of a printed weather map, on
  the side each front moves towards, and its highs and lows as a bold **H**
  or **L** with the pressure. Everything is in one dark ink (`WXFRONT`) over
  the isobars, with a Layer stack legend and a pick in Object information.
  `s100 render` draws bulletins too. Feature ids are now the bulletin's code
  and number, such as `COLD 3`, and the pick notice is "Weather analysis,
  not for navigation." See
  [Show a surface analysis](weather.md#show-a-surface-analysis).
- **Tropical cyclone advisories.** A new
  `EncDotNet.S100.Datasets.TropicalCyclones` package reads the National
  Hurricane Center's GIS shapefile zips for an advisory: the forecast track
  and cone (`al092022_5day_020.zip`), the wind radii (`_fcst_`) and the best
  track (`_best_track`). `NhcAdvisoryReader` gives a `TropicalCycloneAdvisory`
  with the storm, the advisory number and issue time, every forecast point
  with its wind, gusts, pressure and class, the 34, 50 and 64 kt radii by
  quadrant, the cone split at 72 hours, and the past track with observed
  radii. The default registry recognises a forecast zip, or a folder of one
  storm's zips, as `TC/1.0.0`, and `s100 info` describes the storm, the
  advisory, the forecast and every forecast point. Tropical cyclones aren't
  drawn on the chart yet. Not for navigation. Breaking changes:
  `SpecName.NonS100Formats`, `S100Products.CreateDefaultRegistry()` (and so
  `ProductRegistry.Default`) and `WeatherProducts.All` now also hold `TC`.
  See the
  [Datasets.TropicalCyclones README](../src/EncDotNet.S100.Datasets.TropicalCyclones/README.md).

- **Lines with symbols along them, and legends with pictures.** Code-defined
  portrayals can now draw a line with a symbol repeated along it, such as a
  weather front's triangles: `ComplexLineStyle` sets the pen, the symbols,
  their spacing and the side of the line they go on, optionally alternating,
  and `InstructionBuilder.ComplexLine` draws it. The spacing is in
  millimetres on the display, so it holds at every zoom. Layer stack legends
  can now show a symbol, a line sample or an outlined area beside each label,
  built with `LegendBuilder`. `CoverageLegendEntry` is replaced by
  `LegendEntry` and its `SwatchLegendEntry`, `SymbolLegendEntry` and
  `LineLegendEntry` rows. See
  [EncDotNet.S100.Portrayals](../src/EncDotNet.S100.Portrayals/README.md#code-defined-portrayal).

- **Surface analyses: fronts and pressure centres.** `s100` and the
  libraries now read the Weather Prediction Center's coded surface
  bulletins, the 3-hourly analysis at standard and high resolution and the
  coded forecast bulletins, into a surface analysis model of fronts, troughs,
  highs and lows. `s100 info` gives each bulletin's valid times and what it
  holds. Drawing them on the chart comes next. See
  [Show wind and waves on the chart](weather.md).

- **Air temperature and freezing spray on the chart.** GRIB2 and NetCDF
  files with 2 m air temperature now draw it as dashed isotherms every 5 °C,
  off by default, behind a new **Air temperature** switch in Display
  controls. Where a file also has the 10 m wind and a sea temperature, or
  GFS's surface temperature and land mask, SoundCharts works out the
  freezing spray (vessel icing) risk with Overland's predictor and draws it
  in four classes, from light to extreme. Picks give the air temperature and
  the icing class, and the Library's GFS sources can fetch the inputs. See
  [Show wind and waves on the chart](weather.md).

- **Restricted visibility on the chart.** GRIB2 and NetCDF files with
  visibility, such as GFS, now draw fog and poor visibility as a grey fill
  in the marine forecast bands: very poor (under 1000 m), poor (to 2 nautical
  miles) and moderate (to 5 nautical miles). Good visibility isn't drawn.
  The Library's GFS sources can now fetch visibility, and picks give it in
  nautical miles. See [Show wind and waves on the chart](weather.md).

- **Charts get ready for panning in the background.** When a Library source
  or a large exchange set loads as you pan, SoundCharts now portrays its
  not-yet-loaded cells one at a time while the map is idle, so panning to
  them later takes about a second instead of many seconds. It stops whenever
  the map is loading or drawing, picks up where it left off after a restart,
  and shows its progress under the Library's bulk bar. Turn it off with
  **Prepare charts in the background** in Settings → Advanced. The portrayal
  cache is now capped at 1 GB, and background work fills at most three
  quarters of it.

- **S-57 updates are checked before they apply.** An S-57 update now applies
  only when it's for the cell's edition and follows on from the updates
  before it, record by record. Updates a re-issue already holds are skipped,
  and an update of edition 0 is reported as cancelling the cell. SoundCharts
  warns when an update is refused or cancels a cell, `s100 s57 convert` prints
  what applied, and `S57Dataset.UpdateReport` gives the details. Exchange sets
  that hold only updates are now CRC-checked too. See
  [Apply update cells](s57-to-s101.md#apply-update-cells).

- **.NET 10 only.** The packages no longer target .NET 8, which reached end
  of support in November 2026; they target .NET 10 and run on later
  versions. CI also runs the tests on .NET 11. The packages now build on
  EncDotNet 0.11.0, which made the same change.

- **Surface currents on the chart.** GRIB2 and NetCDF files with currents
  now draw them as arrows in S-111's speed bands and colours, from ocean
  models such as HYCOM and Copernicus Marine, satellite altimetry or HF
  radar. Where an S-111 grid is loaded, only its arrows are drawn inside its
  area. A **Currents** switch in Display controls turns them off. The Library
  has two new sources: **Surface currents, altimetry — NOAA AOML ERDDAP**
  (worldwide, daily) and **Surface currents, HF radar — NOAA ERDDAP** (US
  coasts, hourly). Picks and `sample_coverage` give speed in knots and the
  direction the current sets to. See
  [Show wind and waves on the chart](weather.md).

- **Sea surface temperature on the chart.** GRIB2 and NetCDF files with sea
  surface temperature now draw it as a colour fill with a band for each
  degree from 0 °C to 30 °C, so fronts such as the edge of the Gulf Stream
  stand out; the fill is clipped to water. An **Isotherms** switch in
  Display controls adds labelled lines every 2 °C. The Library has a new
  **Sea surface temperature — NOAA ERDDAP** source with the NASA MUR, NOAA
  OISST and NOAA Geo-polar Blended daily analyses, and picks and
  `sample_coverage` give the temperature in degrees Celsius. See
  [Show wind and waves on the chart](weather.md).

- **Convert S-101 to S-57.** `s100 s101 convert --to s57` writes an S-101
  dataset as an S-57 Edition 3.1 base cell, following S-65 Annex C, and
  prints what Annex C dropped or folded into `INFORM`. `--exchange-set`
  writes an `ENC_ROOT` with a `CATALOG.031`, and options cover the choices
  Annex C leaves to the converter. It's for testing the S-57 → S-101
  translation and for tools that read only S-57, not for producing ENCs. See
  [Converting S-101 to S-57](s101-to-s57.md).

## October 2026

- **One time readout on the map, and step buttons that stay put.**
  SoundCharts shows the view time once on the map: the stamp at the top left
  while the dock shows the **Timeline** tab, otherwise the time bar at the
  bottom. With the dock on **Logs** or **Clearance**, both used to show. The
  time bar and the Timeline's own readout now have fixed-width slots and an
  always-present **Live** / **Go live** button, so ‹ and › no longer move
  when you step and a second click can't land on the button that undoes it.
  The words LIVE and PINNED are gone: a green dot and a muted "now" mean
  live, an accent dot and an offset such as "in 23 min" mean you chose a
  time. With navigational warnings or dated chart features loaded, the time
  bar says "Display Not Real Time" on its own line. The step buttons' tooltips
  name the step ("Next step: 1 h (→)"), screen readers hear the time in full
  ("…, in 23 min, pinned"), the time bar's dock button says **Show timeline**
  when the dock is open on another tab and is hidden in a narrow window, and
  its message colours follow the dark theme.

- **A measure card.** SoundCharts' Measure tool now shows a card at the
  map's top left. It measures along rhumb lines or great circles (press `G`
  to switch; the choice is remembered), takes typed or pasted From and To
  positions, and shows the distance in your Distance unit with the other
  unit system under it, the forward and reverse bearings to 0.1°, and the
  total. Great-circle legs draw as curves, the earth model is named, and Copy
  (Ctrl+C or ⌘C) puts the measurement on the clipboard. A great-circle
  measurement promoted to a route keeps geodesic legs. The `set_measure` and
  `get_measure` MCP tools drive and read it with unrounded values (#1130).
- **Range & bearing tool.** SoundCharts has a new map tool next to Measure:
  an EBL/VRM pair from an origin you choose (⌘R or Ctrl+R). Click the
  origin, then the end, or type the origin, range and bearing on its card to
  read the end position and the reverse bearing; a typed range or bearing is
  locked. It works along a rhumb line or a great circle (`G` switches), draws
  the range as a geodesic circle and can add range rings. MCP's
  `set_range_bearing` and `get_range_bearing` drive and read it with
  unrounded values, for the S-164 Accuracy4–6 and Accuracy12 tests (#1130).
- **What the chart's depths are relative to.** SoundCharts' status bar now
  shows **DEPTHS** next to **OVERSCALE** whenever an S-101, S-57 or S-102
  dataset is loaded: **Charted**, or **Charted · water level not applied**
  when S-104 water level covers the view but isn't added to the chart. Select
  it for the vertical datum and sources in view, the S-104 datasets, and where
  water level is applied (the Depth card and the route check). On a narrow
  window it shortens to whole words, never an ellipsis. The Depth card, the
  Clearance readout and route findings now share one vocabulary: **Charted
  depth**, **Water level**, **Adjusted depth** and "water level not applied"
  replace Base, Tide, "tide-adjusted" and "tide not applied", in the CLI's
  route-check findings too. The Depth card reads the S-101 `SoundingDatum` at
  the point, so it warns when an S-101 depth and the water level can't be
  confirmed on the same datum. A new MCP tool, `get_depth_basis`, returns the
  indicator's state, and `get_check_state` adds it as `depthBasis` (#1191).
- **S-101 features follow their dates.** A buoy, traffic separation scheme or
  other S-101 feature with a fixed or periodic date range is now drawn only
  at the dates it applies, as S-100 Part 9 asks. The parser turns the
  portrayal's `Date`, `Time`, `DateTime`, `TimeValid` and `ClearTime`
  commands into `DrawingInstruction.TimeValid` intervals, and S-101 and S-401
  draw at `RenderContext.PortrayalTime` (an instant, or a viewing range)
  without running the portrayal rules again. With no time set, every feature
  is drawn as before. SoundCharts draws them at the view time, with dates
  read in your Local/UTC setting, and a pinned view time says "Display Not
  Real Time" for these charts as it does for S-124 warnings. The S-164
  FixedDateRange and PeriodicDateRange tests now pass (#1126).
- **Why an HDF5 file wasn't opened.** An HDF5 file is detected only from its
  root `productSpecification` attribute, which S-100 Part 10c requires, and
  one that doesn't have it is no longer read as S-102. When no product
  recognises an HDF5 file, SoundCharts' warning, `s100 info`, `validate`,
  `render`, `tiles` and `identify`, and MCP's `open_dataset` now say why: the
  file has no `productSpecification`, it names a product that isn't
  supported, or it can't be read as HDF5 (for example an encrypted S-100
  dataset opened outside its exchange set). The new
  `DatasetPipelineFactory.DescribeUnrecognizedDataset` gives the same sentence
  to library hosts, and `CreateProcessor` and `S100Dataset.Open` include it in
  their errors. To open a file that omits the attribute, pass `open_dataset`'s
  `spec`. Every real S-102 file checked, including NOAA's published tiles,
  declares it (#1092).
- **One not-for-navigation notice per pick.** SoundCharts' pick report on a
  GRIB2 or NetCDF weather grid or a CAP warning no longer repeats the notice
  it leads with as the first attribute row. The processors still put the
  notice first in their feature info for other callers (MCP, the Mapsui pick
  query), now marked by `PickAttribute.IsNavigationNotice`; build one with
  `PickAttribute.NavigationNotice(text)`.
- **Wind and waves in the CLI, MCP and docs.** A new guide,
  [Show wind and waves on the chart](weather.md), covers the GRIB2, NetCDF
  and CAP formats, where to get the data, NDBC buoys, own-ship wind and what
  isn't supported. `s100 identify` now samples GRIB2 and NetCDF grids (a
  `fields` sample with units) and finds CAP warning areas, and prints a
  not-for-navigation notice (`notices` in JSON) for weather data; `--spec`
  accepts `grib2`, `netcdf` and `cap`. `s100 info` adds the model, run, grid,
  extent and layers of a weather grid, the alert count of a CAP file and a
  **Notice** row, and `s100 list-specs` lists the weather formats. Over MCP,
  `list_time_steps` lists GRIB2 and NetCDF valid times and CAP warning starts
  and ends (through a new `ITimeSteppedDatasetData` on the catalog payload),
  `list_specs` flags GRIB2 and NetCDF as sampleable and CAP as queryable, and
  `identify_features`, `query_features` and `describe_feature` read CAP
  warning areas, whose features now carry the pick report's attributes.
  SoundCharts' **About** box and the site footer credit the weather data
  providers (NOAA, ECMWF under CC BY 4.0, Copernicus Marine, DWD), and
  **Open Dataset...** has a **Weather Data (GRIB2, NetCDF, CAP)** file type.
- **Weather sources in the Library.** The known catalogues list six weather
  services, not S-100 data and not for navigation: GFS-Wave and GFS from the
  NOMADS GRIB filter (cut to the map view) and from NOAA's GFS bucket on AWS
  (the whole globe, only the chosen fields read from each file by byte range,
  driven by its `.idx` inventory), `api.weather.gov` marine alerts for the
  map view's marine zones, and WAVEWATCH III from NOAA's ERDDAP as a NetCDF
  subset. A new `WeatherFeedSource` and `WeatherFeedIndexer` in
  `EncDotNet.S100.Collections` list each service's latest run as one item
  that downloads as one file (`RemoteItemLocation.Parts`). A run more than 12
  hours old is tagged **Stale**, with **Load latest run**
  (`LibraryOperations.LoadLatestRunAsync`); a newer run replaces the last
  unless it's pinned (`LibraryDownloads.Pin`). Each source's licence and
  attribution go with its items, the loaded layer in the **Datasets** panel
  and its pick reports. Every request sends an identifying `User-Agent`, as
  the NWS API asks. A folder holding one model run in hourly GRIB2 files is
  listed and loads as one dataset (`GribRunFolder`,
  `ProductRegistration.MatchFolder`). See the
  [Collections README](../src/EncDotNet.S100.Collections/README.md#weather-services)
  and the [SoundCharts README](../src/EncDotNet.S100.Viewer/README.md#weather-services).
- **Own-ship wind from NMEA 0183.** A new `EncDotNet.S100.Nmea` library is a
  minimal NMEA 0183 reader, kept apart so an own-ship NMEA driver can share
  it: checksum validation, talker-agnostic decoding of `MWV`, `MWD`, `MTW`,
  `XDR` (air temperature and pressure), `HDT`, `HDG`, `VHW`, `RMC` and
  `VTG`, true wind from apparent wind, and line sources over a stream, a
  replay file, TCP and UDP. A new `EncDotNet.S100.DynamicSources.Nmea`
  library builds `NmeaWeatherSource` on it: one feature at own ship with
  true wind from `MWD`, or derived from relative wind, heading and motion
  over the ground (falling back to `MWV` T and to motion through the water),
  drawn as the weather wind barb (southern-hemisphere barbs south of the
  equator, by `WindBarbSymbols.For`) next to the own-ship symbol on S-98
  `OtherChartOverlays` at priority 110. Without a heading or speed it shows
  the relative wind, grey and labelled so. A pick lists true and relative
  wind, how true wind was found, heading, water and air temperature and
  pressure. In the viewer, set `SOUNDCHARTS_NMEA_INPUT` (`udp`,
  `tcp://host:port` or a recording's path) to get its Layer Stack row. Not
  for navigation. See the
  [DynamicSources.Nmea README](../src/EncDotNet.S100.DynamicSources.Nmea/README.md).
- **Weather drawing polish.** Wind barbs south of the equator are now the
  mirrored southern-hemisphere barbs (`WXBARBS005` to `WXBARBS200`), chosen
  by each cell's latitude; `WindBarbSymbols.For(knots, latitude)` applies the
  same rule to a single barb, and NDBC stations south of the equator use it.
  The wave height fill is clipped to water when an S-101 chart is loaded, by
  a new S-98 rule, `R-101-WX-B`, that shares its effect with the S-104
  `R-101-104-B` clip and applies to any `GridCoverageSubLayer` with the new
  `ClipToWater` flag; wind isn't clipped. A new `PressureIsobarStyle` draws
  `pressure.msl` as isobars every 4 hPa, labelled in hPa, on
  `OtherChartOverlays` at priority 90, on by default wherever the data has
  pressure: `CoverageContours` traces an `ICoverageStyle`'s value-based
  `ContourStyle`s into an `IsolineCoverageSubLayer`, which both renderers
  already draw. In the viewer, each weather fill in the **Layer stack** shows
  a colour legend of its bands (`GridCoverageSubLayer.Legend`); the headless
  renderer doesn't draw legends. A new **Weather** section in **Display
  controls**, shown while a GRIB2 or NetCDF dataset is loaded, switches
  **Isobars** (on), **Wind speed fill** and **Wave arrows** (off) for every
  weather dataset and keeps them between sessions; the switches reach the
  processors as the new `EcdisDisplaySettings.LayerVisibility`
  (`WeatherCoverageStyle.IsDrawnWith`), and `s100 render` draws the defaults.
  Not for navigation. Breaking changes: the GRIB2 and NetCDF processors now
  draw isobars for a file with `pressure.msl` and offer every applicable
  style in `LayerKeys`, and their content scope names the drawn layers;
  `WeatherLayerKind` gains `Isolines`; `WeatherCoverageStyle.CreateAll()` and,
  for a pressure grid, `CreateFor` also return `PressureIsobarStyle`;
  `WeatherCoverageStyle.Contours` is virtual; `WeatherPalette.Palette` has two
  more tokens; `WeatherCoverageStyle.StyleVersion` is 2, so cached weather
  tiles are redrawn; `S98DefaultRules.Default` has a seventh rule; an
  `Isoline` with an empty label is drawn unlabelled. See the
  [Datasets.Weather README](../src/EncDotNet.S100.Datasets.Weather/README.md).
- **Headless renders frame small coverages on their true extent.** With no
  viewport or `--bbox`, `HeadlessCompositor` (and so `s100 render`) now fits
  the view to each coverage layer's own extent plus its usual 10 % margin.
  Before, `SeamAwareBoundsAccumulator.AddLonLatBox` widened a coverage's
  longitudes out to its 0.5° occupancy bins, so a 0.2° grid composited with a
  chart cell drew as a small patch. The bins still drive antimeridian
  detection, and a box whose eastern edge runs past +180° (a 0°–360° grid) is
  now split across the seam instead of clamped.
- **NDBC buoy observations.** A new `EncDotNet.S100.DynamicSources.Ndbc`
  library shows NOAA National Data Buoy Center observations as a dynamic
  feature source. `NdbcObservationSource` reads the `activestations.xml`
  station table and the head of each `realtime2` standard meteorological
  file for the stations in view plus a margin, every 10 minutes, with
  backoff on errors, and nothing while the view is wider than 25° or the
  layer is hidden. Observations older than 3 hours are drawn grey and
  dropped after 12. Each station is a station-model glyph (the weather
  wind barb into the wind and the wave height) built with the portrayal
  toolkit, on S-98 `OtherChartOverlays` at priority 100; a pick lists every
  value with its unit and the observation time. In the viewer, NDBC buoy
  observations is a Layer Stack row, off by default. Supporting additions:
  `DynamicSourceMetadata.Plane` and `WithinPlanePriority` (the Layer Stack
  now groups a dynamic source by its plane), `IDynamicFeatureDescriber`,
  `DynamicFeatureGeometryProvider`, `IDynamicFeaturePortrayal`,
  `PortrayedDynamicFeatureRenderer` and
  `MapsuiDisplayListRenderer.RenderFeatures`. Not for navigation. See the
  [DynamicSources.Ndbc README](../src/EncDotNet.S100.DynamicSources.Ndbc/README.md).
- **CAP marine weather warnings.** A new `EncDotNet.S100.Datasets.Cap`
  package reads CAP 1.2 alerts: one CAP XML alert, an Atom feed of them, or
  the NWS GeoJSON from `api.weather.gov/alerts`. Polygons are drawn as given,
  circles as 64-vertex polygons, and an area with only NWS zone codes (such
  as `ANZ535`) as the zones from a bundled, simplified copy of the NWS coastal
  and offshore marine zone layer; codes it doesn't have are listed in the pick
  report and `s100 info`, not dropped. `CapDatasetProcessor` draws each
  warning only while it's in force at the view time, by the rule S-124 warnings
  use, hatched and outlined by severity and labelled with its event, on the
  cautions and warnings plane above S-124, in the viewer, `s100 render` and
  `s100 info` (which lists every alert), and the MCP tools. The default
  registry recognises CAP files as `CAP/1.2.0` by their first bytes, whatever
  their extension. CAP alerts aren't listed in the Navigational warnings panel.
  Not for navigation. Breaking changes: `S100Products.CreateDefaultRegistry()`
  (and so `ProductRegistry.Default`) and `WeatherProducts.All` now also hold
  `CAP`; the S-124 in-force rule moved from `NavigationalWarningInfo.StatusAt`
  to the new `WarningValidity` (same behaviour); `InstructionBuilder` gains
  `AreaPattern`. See the
  [Datasets.Cap README](../src/EncDotNet.S100.Datasets.Cap/README.md).
- **GRIB2 wind and wave files.** `GribDatasetProcessor` draws GRIB2 grids
  from NOAA's GFS and GFS-Wave and ECMWF open data: the wave height fill and
  wind barbs, time steps, picks, `sample_coverage` and headless rendering, in
  the viewer, `s100 render` and the MCP tools. `GribWeatherDataset` (in
  `EncDotNet.S100.Datasets.Grib`) reads one file of many messages or several
  appended, such as a NOMADS GRIB filter download, orders steps by valid time,
  decodes a field only when its step is needed, and lists fields it can't use
  (CCSDS, another grid, an older run) and parameters it doesn't draw, with
  why; `s100 info` prints them. The model name comes from NCEP's generating
  process or the file name. The default registry recognises GRIB2 files as
  `GRIB2/2.0.0` by their first bytes, whatever their extension. The parameter
  table gains `wave.wind_sea.dir` (NCEP's WVDIR) and ECMWF's `msl` code for
  `pressure.msl`. Not for navigation. Breaking changes:
  `S100Products.CreateDefaultRegistry()` (and so `ProductRegistry.Default`)
  now also holds `GRIB2`, and `WeatherParameters.All` has a twelfth row. See
  the [Datasets.Pipelines README](../src/EncDotNet.S100.Datasets.Pipelines/README.md#weather-grids).
- **JPEG 2000 in the GRIB2 decoder.** `EncDotNet.S100.Datasets.Grib` now
  decodes data template 5.40, so GFS-Wave as NOAA serves it on AWS reads
  directly. It has its own managed JPEG 2000 Part 1 codestream decoder,
  written from ITU-T T.800 and limited to one greyscale component: all
  progression orders, tiles and tile-parts, precincts, quality layers, every
  code-block style, and the 5/3 and 9/7 transforms. Regions of interest,
  progression order changes, packed packet headers, several components,
  Part 2 and HTJ2K fail with a message naming them.
  `GribDataRepresentation` gains `CompressionType` and
  `TargetCompressionRatio`. A field packed in 0 bits now decodes to its
  reference value unscaled, as ecCodes reads it, in every template.
- **NetCDF wind and wave files.** A new `EncDotNet.S100.Datasets.NetCdf`
  package reads NetCDF-CF grids with no native code: NetCDF-3 (classic and
  64-bit offset, ERDDAP's `.nc` output) with a managed reader, and NetCDF-4
  on PureHDF (deflate and shuffle). It finds the coordinates, decodes CF time
  units, applies packing and missing values, and matches variables to the
  weather parameter table by `standard_name`; files it can't use fail with a
  message listing their variables or naming the grid type.
  `NetCdfDatasetProcessor` (on a new `WeatherGridProcessorBase` that the
  GRIB2 processor will share) draws the wave height fill and wind barbs,
  steps through time, picks and samples with units, and renders headlessly,
  and the default registry recognises the files as `NetCDF/4.0.0` or
  `NetCDF/3.0.0`, so they open in the viewer, `s100 render` and the MCP
  `sample_coverage` tools. Not for navigation. Breaking changes:
  `S100Products.CreateDefaultRegistry()` (and so `ProductRegistry.Default`)
  now also holds `NetCDF`, which `AddAllS100Products()` leaves out (use
  `AddWeatherProducts()`), and `UnsupportedWeatherGridException` now derives
  from `NotSupportedException`. See its
  [README](../src/EncDotNet.S100.Datasets.NetCdf/README.md).
- **GRIB2 decoder.** A new `EncDotNet.S100.Datasets.Grib` package decodes
  GRIB2 in managed code, with no native dependencies: several messages per
  file and repeated sections, the regular latitude/longitude grid (template
  3.0) in any scanning mode, analysis, forecast and time-interval products
  (4.0 and 4.8), and simple, complex, spatially differenced and PNG packing
  (5.0, 5.2, 5.3 and 5.41) with bitmaps. `GribFile` indexes the fields when
  it opens and decodes values only when asked, and each `GribField` maps onto
  the weather grid model (native grid, level, canonical parameter and a lazy
  `WeatherStepDecoder`). GRIB1, JPEG 2000 (5.40), CCSDS (5.42) and other
  templates fail with a message naming them. See its
  [README](../src/EncDotNet.S100.Datasets.Grib/README.md).
- **Coverage sampling is a processor capability.** `ICoverageSampler` (in
  `EncDotNet.S100.Core`) finds where a dataset can answer a point, and
  `CoverageSampling` chooses the dataset and applies the time handling
  (nearest step, one step of tolerance, `time_out_of_range` or the nearest
  step, range and series windows) for every product. The S-102, S-104 and
  S-111 processors and catalog payloads implement it, and
  `SampleCoverageService` no longer names a product: any coverage whose
  catalog payload samples (a `ProductDatasetData` that implements
  `ICoverageSampler`, for example) answers `sample_coverage` and `sample_coverage_along`, with its
  values as `$kind` `"fields"` entries that carry their quantity and unit.
  The S-102, S-104 and S-111 results are unchanged. Breaking changes:
  `SampledValue` and `SampleTimeStatus` moved to `EncDotNet.S100.Core` (same
  namespace), and `SampledValue` declares the abstract `GetFields()`, so a
  subtype outside the library must implement it. See the
  [Datasets.Pipelines README](../src/EncDotNet.S100.Datasets.Pipelines/README.md#coverage-sampling).
- **Weather grid model.** A new `EncDotNet.S100.Datasets.Weather` package
  holds the in-memory model the GRIB2 and NetCDF readers will share: a
  `WeatherGridSet` (model and run time, a regular latitude/longitude grid,
  and parameters with time steps), the canonical parameter table with its
  GRIB2 codes and CF standard names, normalisation to the coverage grid order
  (rows running north, longitudes in −180 to 180, missing values as `NaN`),
  and `WeatherCoverageSource`, which decodes time steps only when they're
  sampled, offers 2× and 4× overview levels, and derives wind speed and
  direction from U and V. See its
  [README](../src/EncDotNet.S100.Datasets.Weather/README.md).
- **Weather styles.** `EncDotNet.S100.Datasets.Weather` now draws its
  grids with code-defined coverage styles built on the portrayal toolkit: a
  wave height fill and WMO wind barbs (on by default), and a Beaufort wind
  speed fill and wave direction arrows (off by default), in Day, Dusk and
  Night palettes. Fills go on the on-demand surface plane under S-104 at
  half opacity; barbs and arrows go on the dynamic arrows plane above S-111.
  `WeatherPortrayal` builds the coverage sub-layers from a
  `WeatherCoverageSource`, so the Skia and Mapsui renderers draw them with no
  weather-specific code. The package now depends on
  `EncDotNet.S100.Portrayals` too.
- **One `RenderContext` for every product.** `RenderContext` is now a sealed
  record with a `TimeStep` property, and its per-product subclasses
  (`S101RenderContext`, `S102RenderContext`, `S104RenderContext`,
  `S111RenderContext`, `S122RenderContext`, `S124RenderContext`,
  `S125RenderContext`, `S127RenderContext`, `S129RenderContext`,
  `S201RenderContext` and `S411RenderContext`) are gone (a breaking change).
  Write `new RenderContext { TimeStep = t }` where you wrote
  `new S104RenderContext(t)`. `MapPresentationState.CreateRenderContext` no
  longer picks a type by product, so a new time-aware product sees the
  selected time without a context of its own.
- **Route checks: the engines are the checks.** In
  `EncDotNet.S100.Navigation`, `RouteCheckEngine`, `RouteAreaCheck`,
  `RouteWarningCheck` and `PassageCheckEngine` are gone (a breaking
  change): `ChartCheck`, `AreaCheck`, `WarningCheck` and `ClearanceCheck`
  hold their code. Run one check with `Run` on a `RouteCheckContext`, or
  all of them with `RouteChecker`. Each check is an `IRouteCheck<TDetail>`
  whose `RouteCheckOutcome<TDetail>.Detail` is typed: `ChartCheckResult`
  (was `RouteCheckResult`), `AreaCheckResult` (was `RouteAreaResult`),
  `WarningCheckResult` (was `RouteWarningResult`) and `ClearanceCheckResult`
  (was `ClearanceOutcome`). The findings are on the outcome only. The
  incremental clearance API (`SampleDepths`, `Evaluate`, `SweepDepartures`,
  `SweepWaterLevelRange` and `PassageDepthProfile`) is internal to
  `ClearanceCheck`; departure sweeps come from
  `RouteCheckSettings.DepartureWindow`. The per-check `DefaultHalfWidthMeters`
  and `Formatter` options are gone; set `RouteCheckOptions.DefaultLimit` and
  `Formatter` once. Codes and finding ids are unchanged. See
  [Run one check](../src/EncDotNet.S100.Navigation/README.md#run-one-check).
- **How a route check works.** A new page,
  [How a route check works](how-a-route-check-works.md), explains which
  dataset feeds each check, the time and keel rules, the verdict and each
  check's status. [Route checks and under-keel clearance](route-checks.md)
  now lists every finding code, the clearance ones included. The
  [route check sample](../samples/EncDotNet.S100.Samples.RouteCheck/README.md)
  checks a passage end to end on synthetic data.
- **One `check_route` for the whole route check.** The MCP `check_route`
  tool now runs `RouteChecker` and returns the whole report: the `verdict`
  (`must_review`, `water_level_ends_before_arrival`, `check` or `clear`,
  with the finding that names it), each check's status (`checks`), every
  finding, the clearance ones included, and the under-keel `clearance`.
  `includeSamples` adds per-leg results and samples, and `includeWindow`
  (with `windowFrom` and `windowTo`, or the S-104 range) adds the departure
  sweep. `get_passage_result` is gone (a breaking change): its arguments
  and result are now `check_route`'s, and `time` is now `departure`.
  `s100 route check` runs `RouteChecker` itself: its JSON `check` is the
  `check_route` result and `passage` is gone, and the exit code follows the
  verdict, so `--fail-on check` also fails on a tight clearance. See
  [Check a route](mcp-server.md#check-a-route).
- **Route checks: one plan for every check.** In
  `EncDotNet.S100.Navigation`, the chart, area, S-124 and clearance checks
  all take a `PassagePlan` of `PassageLeg`s and a `RouteCheckSettings`.
  `RouteCheckLeg`, `RouteCheckDepths` and `RouteWarningTiming` are gone.
  Legs carry `Length`, `Depth` and `Speed` values, and a leg without a draft
  has none rather than 0. Each leg's dangers are judged against that leg's
  own draft + squat + required UKC, rather than the deepest keel on the
  route. Where a leg has no ETA, areas and warnings are judged at the
  route's departure, and only without one at the view time (SoundCharts)
  or `time` (MCP).
- **Route checks: one set of sources.** `CatalogRouteCheckSources` builds a
  `RouteCheckSources` bundle (charts, depth, water level, areas and
  warnings) that every check reads, and `RouteCheckSourceCache` keeps one
  per set of loaded datasets. It replaces `RouteCheckDataSources` and
  `PassageDataSources`. One depth source serves both the clearance check
  and the chart check's S-102 test (`SampleBathymetry`). The warning check
  reads an `IRouteWarningSource`; `RouteWarningList` wraps a list. The
  source interfaces take `GeoPosition` and `BoundingBox`, and every source,
  and `ICrsTransform`, is safe to call from several threads at once.
  SoundCharts, the MCP tools and `s100 route check` each share one cache.
- **Route checks: structured findings.** Every finding code is in one
  catalogue, `RouteFindingCodes`, as a `RouteFindingCode` with its
  category, default group and headline rank; it replaces `RouteCheckCodes`,
  `RouteAreaCodes` and `RouteWarningCodes`. A finding's `Category` (`Chart`,
  `Depth`, `Area`, `Warning`, `Clearance`, `Confidence` or `Data`) replaces
  `Source`, and its typed `Details` (`DangerDetails`, `ShoalDetails`,
  `AreaDetails`, `WarningDetails`, `ClearanceDetails`) carry the numbers
  behind it. A `RouteFindingFormatter` renders titles and meta lines in one
  culture, depth unit and time zone, and replaces `RouteCheckText`,
  `RouteAreaText` and `RouteWarningText`. `ClearanceFindings.Build` turns a
  clearance result into findings. `check_route`, `get_findings` and
  `s100 route check --format json` add `details` to each finding and report its
  `category` instead of `source`. Codes and ids are unchanged. The expired
  S-111 forecast notice (`data.s111_forecast_expired`) is gone: currents
  aren't applied to ETAs (#1033).

- **From a warning to its route finding.** While SoundCharts checks a
  route, the Warning card of a warning that crosses or is near it, in
  **Object Information** or opened from the **Navigational warnings**
  panel, shows where: "Crosses route · leg 3 → 4", "Near route · leg 1 →
  2", or "Crosses route on 2 legs". Selecting the row selects that finding
  in the Routes panel's **Findings** list. `get_warning_cards` reports the
  row as each card's `route`, and `list_navigational_warnings` adds the
  legs to each warning's `route`.

- **S-421 datasets become routes only when you open them as routes.**
  Loading an S-421 dataset in SoundCharts no longer adds it to the
  **Routes** panel, however it's loaded, so the panel lists only the routes
  you chose. Select an S-421 dataset in the **Library** and choose **Open as
  route** (it loads the dataset if it isn't loaded), or use **Import route…**
  for a file. An explicit Library load now also keeps a dataset that was
  loaded as you panned, rather than unloading it when you pan away.
  `import_route` gains `datasetId`, which opens a loaded S-421 dataset as a
  route.

- **Route check tones for Day, Dusk and Night.** In SoundCharts' Check
  route, the findings' markers, sleeves and legend use the route's state
  tones: Must review in the Below red, Check in the Tight amber and Info in
  the low-confidence grey. Red is used only for Below and Must review. On
  Night, every state is dimmed to about 0.10 relative luminance (from
  0.15–0.22) so it doesn't spoil night vision, while keeping 3:1 against its
  black casing. Day's Tight is darker (`#B07D00`, 3.6:1 on its white
  casing), No data is now dotted rather than dashed, so it can't be mistaken
  for lower confidence, and Night edits routes in a fixed blue (`#3F64A6`)
  instead of a toned-down accent. Dusk keeps its colours.

- **Warnings in place while checking a route.** In SoundCharts' Check
  route, selecting an S-124 finding opens the warning's card right in the
  findings list, with its status judged when you get there ("In force at
  06:08", "Not in force at 06:15" or "Cancelled") and a link to **Open in
  Navigational warnings**. Moving the departure or changing speed re-judges
  the open card. While checking, the **Navigational warnings** panel offers
  **All | On {route}**, and each warning on or near the route shows "On
  route · 06:08" or "Near route · 06:19". `get_findings` reports the open
  card and each warning's status text, and `list_navigational_warnings`
  gains a `scope` and each warning's `route` relation.

- **Classify navigational warnings on chart, off chart or information.**
  S-98 Ed 2.0.0 §12.12.2 asks that the mariner can classify each warning's
  relevance to the route. In SoundCharts' **Navigational warnings** panel,
  each row now ends with a glyph you select to choose **On chart** (the
  default), **Off chart** or **Information**; the Warning card has the same
  choice. Off-chart and information warnings aren't drawn, nor are their
  affected areas and texts, except while their card is open. They stay in
  the list, marked, and route checks still report them, tagged with the
  classification. **Classification** in the order menu lists on-chart
  warnings first. The classification is kept across restarts and refreshes,
  keyed by series and number with the year. `list_navigational_warnings`
  and `get_warning_cards` report `classification`, and the new
  `set_navigational_warning_classification` tool sets it.

- **Route findings on the map.** In SoundCharts' Check route, the route's
  line keeps its clearance colours and the route checks draw on top: a
  stretch of the route (drying ground, an area, a warning crossed) is a
  translucent sleeve along the route, and a point (a wreck, a nearby warning)
  is a marker above the route, an octagon for Must review, a triangle for
  Check and a circle for Info, with a dotted leader to a feature off the
  track. Info markers show only when selected, markers closer than 24 px
  merge into a counted cluster (click to zoom in), and the cross-track
  corridor's edges are dotted, filled only on the selected finding's leg.
  The selected finding gets a ring and one pill with its ETA. A legend at
  the map's bottom left folds to a chip; `get_map_legend` and
  `set_map_legend` read and set it, and `select_finding` reports what the
  map highlights.

- **Check routes from the command line.** The new `s100 route check` checks
  an RTZ (1.0, 1.1, 1.2) or S-421 route against dataset files, an exchange set
  or a folder, without SoundCharts: land, drying ground, chart coverage, the
  safety contour, charted dangers, regulated and S-122 protected areas, S-124
  warnings and, when the route has a draft, under-keel clearance. Options
  replace the file's draft, squat, speed, departure, required UKC and corridor,
  and set the safety contour and depth. `--format json` gives the
  `check_route` and `get_passage_result` results, and the command exits with
  code 7 on Must review findings (`--fail-on`), for CI. The MCP `check_route`
  tool now also checks S-124 warnings (`s124.on_route`, `s124.near_route`,
  `s124.not_in_force`, `s124.cancelled`, with the warning on each finding). A
  new guide, [Route checks and under-keel clearance](route-checks.md),
  explains every finding, threshold and confidence reason, received routes
  and the route formats. In the libraries, `S421Route.GetPlanningSchedule`
  returns the schedule a passage reads its speeds and departure from.

- **One switch for navigational warnings on the chart.** SoundCharts'
  **Display controls** panel has a new **Overlays** › **Navigational
  warnings** switch, on by default and kept after a restart (S-98 Ed 2.0.0
  §6.1.1, §12.12.2). Off, the map draws no S-124 warning, affected area, text
  or warning pick highlight, even for an open Warning card; each dataset's own
  visibility keeps its state. The Navigational warnings panel, the cards and
  route checks keep working, and the panel says "Not drawn: Navigational
  warnings are off in Display controls" with a **Turn on** button.
  `list_navigational_warnings` reports `showNavigationalWarnings`, and
  `set_navigational_warning_display` sets it; both of its arguments are now
  optional.

- **NEW warnings follow S-98.** In SoundCharts' **Navigational warnings**
  panel, a newly received warning now shows NEW until you open its card or
  24 hours have passed since it was received (S-98 Ed 2.0.0 §12.12.1). A
  later refresh no longer clears it, and NEW expires on its own. New
  **NEW options** limit the indication to warnings received within some
  hours, or within some nautical miles of own ship or the active route (both
  off by default), and **Mark new since** marks everything received in the
  last few hours NEW again for a watch handover. `list_navigational_warnings`
  reports each warning's `receivedAt`, `newUntil`, `openedAt` and
  `newReason`, and the new `mark_navigational_warnings_new` tool does the
  handover. The seen-warnings file moves to format 2: an older file is
  discarded, so each source sets a new baseline.

- **A shorter findings list.** In SoundCharts' Check route, Check and Info
  findings with the same code now share one row with a count ("Charted depth
  from S-101 only ×9 · Legs 7 → 9 · 9 stretches") that expands into each
  finding; Must review findings are always listed one by one. INFO is folded
  until you open it, and the Legs tab marks each leg's route-check findings
  with a badge, with or without a draft. `get_findings` reports the filter,
  per-chip counts, merged rows (with every member id), the INFO fold and the
  expanded row; `set_findings_view` changes them.

- **Open and export RTZ routes.** SoundCharts' **Import route…** now opens
  RTZ route plans (IEC 61174 Annex S, versions 1.0, 1.1 and 1.2) as
  read-only received routes in Planning mode, and **Export…** writes any
  route as RTZ, with a version select (1.2, 1.1, 1.0). Speeds, the
  departure, cross-track limits (nautical miles in RTZ), draughts and UKC map
  onto the route's passage; a copy keeps further schedules, schedule details,
  vendor extensions and unknown attributes and writes them back. Open RTZ
  files reopen after a restart. The MCP tools `import_route` and
  `export_route` take RTZ (`format: "RTZ"`, `version`). The new
  `EncDotNet.S100.Routes.Rtz` package reads and writes RTZ in your own code.

- **The map draws S-124 warnings only while they're in force.** SoundCharts
  used to draw every loaded navigational warning; now the map follows the
  same status as the Navigational warnings panel and the Warning card (S-98
  Edition 2.0.0 §12.12). Ended and cancelled warnings, including those left
  out of the latest in-force bulletin, are never drawn. A new display
  setting, **Show warnings not yet in force** (off by default, kept after a
  restart), also draws a warning from its publication time until it starts.
  Each part is judged on its own dates, and a hidden part takes its affected
  area and text with it. The warning on an open Warning card stays drawn. When
  the view time is pinned with S-124 data loaded, the map's time stamp starts
  "Display Not Real Time" and stays shown (§12.7.2). The panel's **Not in
  force** group says what isn't drawn. MCP: `list_navigational_warnings` adds
  `drawn`, `drawnForCard`, `showNotYetInForce` and `drawTime`, and
  `set_navigational_warning_display` sets the setting. In the libraries,
  `NavigationalWarningSet.IsDrawnAt` holds the rule, and
  `RenderContext.HiddenFeatures` / `MapDataset.HiddenFeatures` let a host
  hide features; S-124 leaves them out before portrayal, and they aren't
  picked.

- **Check route: one verdict for every check.** SoundCharts' footer toggle
  is now **Check route**, on for any route with two or more waypoints. The
  verdict card gives one answer for the clearance check, the route checks and
  S-124 warnings, worst first: its title names the worst finding ("Crosses
  drying ground", "Below your UKC setting in 2 places", "Inside navigational
  warning 001/26"), or without a draft counts them ("2 to review", "1 to
  check"). Under it, Clearance, Route and Warnings rows sum up each check; the
  minimum UKC is in the Clearance row, and clicking a row filters the
  Findings list, which has All, Clearance, Route and Warnings chips. Without a
  draft the Clearance row reads "Not checked · + Add draft", which opens the
  Passage card at Draft; there is no Clearance tab and the route keeps its
  normal colour. `get_check_state` adds `verdictKind`, `verdictSubtitle`, the
  `summary` rows and the `footer`; `verdictTitle` is now the card's overall
  title.

- **Warnings left out of the latest in-force bulletin are cancelled.** S-124
  Edition 2.0.0 §9.2.1 says a warning published before the latest in-force
  bulletin for its series, and not listed in it, is no longer valid; S-98
  §12.12 has ECDIS mark it cancelled. SoundCharts now applies this, and
  cancellation datasets, everywhere a warning's status shows. The Warning
  card adds a reason line ("Cancelled — not in in-force bulletin NAVAREA X
  110/26" or "Cancelled by …"). The Navigational warnings panel's groups,
  summary count and rail badge, MCP `list_navigational_warnings` and
  `get_warning_cards` (new `statusReason`, `endedBy` and `statusReasonText`)
  and route-check findings agree. A series group also says what its latest
  bulletin lists but isn't loaded ("Listed in NAVAREA X 110/26 but not
  received: 103/26, 107/26", §9.2.2); `list_navigational_warnings` returns
  it in `bulletins`. In the libraries, `NavigationalWarningSet` (Core) judges
  all loaded warnings together and reports each series' latest bulletin
  (`LatestBulletins`); `NavigationalWarningInfo` gains `InForceBulletin`,
  `NavigationalWarningReference` an `InteroperabilityIdentifier`, and
  `IDatasetProcessor` a `GetNavigationalWarningMessage` for datasets with no
  parts. `RouteWarningCheck.Check` takes the set, and `StatusOver` now takes
  it and returns the withdrawal too (a breaking change).

- **Routes are checked against the chart.** With **Check route** on,
  SoundCharts' findings list shows where a route's track crosses land or
  drying ground (Must review), where either reaches inside the leg's
  cross-track limits, and where the route leaves the loaded charts (Check).
  It runs with or without a draft, and again as you drag a waypoint. Where
  cells overlap, only the finest one counts. The MCP tool `check_route`
  returns the same findings for a route or a polyline. In the libraries,
  `RouteCheckEngine` (`EncDotNet.S100.Navigation`) and
  `RouteCheckDataSources` (`EncDotNet.S100.Datasets.Pipelines`) do the
  check.

- **Check route on a received route shows only its action points.** While
  Check route is on for a received S-421 route, its dataset now draws only
  its action points (viewing group 52270) under the Planning-mode route line,
  not its own waypoints and legs as well. `set_dataset_state` reports this as
  the dataset's `viewingGroups`. In the libraries, `ViewingGroupFilter` on
  `MapDataset.ViewingGroups` and `RenderContext.ViewingGroups` narrows the
  viewing groups of one dataset; the XSLT (GML) vector products honour it.
  The `MapDataset` constructor has a new optional `viewingGroups` parameter.

- **Read long attribute text in full.** In SoundCharts' Object Information,
  S-101 referenced text files (`TXTDSC` / `NTXTDS`) and any other text value
  with a line break or over 120 characters, such as S-122 and S-127
  regulation text, show as cards under **TEXT** instead of being cut off in
  the attribute table. They share the Warning card's body: the whole text,
  **Show all** past 22 lines, **Copy** and **Examiner**. Cards for data whose
  exchange-set catalogue says `notForNavigation` show **NOT FOR NAVIGATION**
  in the footer; `get_warning_cards` reports it as `notForNavigation`. The
  card's text, Show all, Copy, Zoom to and Examiner controls now have
  `PickReport.TextCard.*` automation ids (`PickReport.WarningCard.ZoomTo` is
  now `PickReport.TextCard.ZoomTo`).

- **See how fresh the navigational warnings are.** In SoundCharts'
  **Navigational warnings** panel, each group header names the SECOM service
  or feed the warnings came from and when it last answered ("AMSA · 07:40").
  **Refresh** asks those sources again through the Library. A source whose
  latest refresh failed says so at the top of its group, with **Retry**. A
  warning that first appeared in the latest refresh shows **NEW** until you
  open its card, the summary counts them, and the Warnings button shows a
  badge. `list_navigational_warnings` reports `new`, `sources` and `stale`,
  and `refresh_navigational_warnings` refreshes. In the libraries,
  `SecomSourceIndexer.HealthOf` reports when a SECOM service last answered,
  as the feed indexers' `HealthOf` does.

- **Warning numbers in a navigational warning link to those warnings.** In a
  Warning card, a number such as "106/26" that names a warning in the
  **Navigational warnings** list opens it, so a weekly "warnings in force"
  bulletin works as an index; the text stays selectable and monospaced. When
  a pick hits several S-124 warnings, they show as Warning cards, points
  before lines before areas, with one open at a time and highlighted on the
  map; pointing at a card previews its warning. **Zoom to** on the card
  frames a warning that has a location. The MCP tools `get_warning_cards`,
  `follow_warning_link` and `preview_warning_card` do the same.

- **Check passage without a draft.** SoundCharts' Check passage now turns on
  for any route with two or more waypoints. Without a draft the clearance
  parts are hidden ("Add a draft to check clearance") and the verdict card is
  titled from the findings. A small marker shows each finding on the map. The
  findings list now merges findings from several checks, ready for the route
  checks in #888; `set_check_mode` and `get_findings` accept routes without a
  draft.

- **Find every navigational warning in one list, even the ones with no
  location.** SoundCharts has a **Navigational warnings** panel beside
  **Library**. It lists each S-124 warning part in the loaded datasets,
  grouped by series with the newest number first, and counts those in force
  and those that are text-only. Search by number, type or area, and read
  status at the view time or now; warnings not in force drop into a dimmed
  group at the bottom. Selecting a row opens its Warning card and frames it on
  the map, or, for a part with no location, says so over the map. The MCP
  tools `list_navigational_warnings` and `select_navigational_warning` do the
  same, and `IDatasetProcessor.GetNavigationalWarnings` lists the parts in the
  libraries.

- **Read a navigational warning's whole message in Object Information.**
  Picking an S-124 warning part in SoundCharts now shows a Warning card: the
  series and number, the warning type, whether it is in force at the view
  time, its validity, area and issue time, and the full message text in a
  monospaced block. A part with text in several languages has a tab per
  language, the UI language first. Messages over 22 lines stop at 20 behind
  **Show all**, and the attribute rows fold beneath the card. In the
  libraries, `S124NavwarnPart.Information` keeps every language,
  `S124ComplexAttribute.Groups` keeps each repeated nested attribute, and
  `FeatureInfo.NavigationalWarning` carries the message for any pick report.

- **Passage checks read quirky S-102 and S-104 grids.** Passage depth and
  water level now read grids whose georeferencing is longitude first, grids
  declaring a geographic CRS other than WGS 84 (such as ETRS89), and grids in
  the 0…360° longitude frame, instead of skipping them and falling back to
  S-101 depths. The S-102, S-104 and S-111 readers now check the axis order
  of a grid in any geographic CRS. `PassageDataSources.FromDatasets` uses its
  transform factory for projected S-104 grids too.
- **The pick report and `sample_coverage` read quirky grids.** They now find
  the cell in S-102, S-104 and S-111 grids that are longitude first, declare
  another geographic CRS, or use the 0…360° frame, using the same lookup as
  passage checks. `sample_coverage` also reprojects projected S-104 and S-111
  grids. For a swapped grid, `CoveragePickHelper.SamplePoint.Row` and `Col`
  run along latitude and longitude, and `Native` is the click in the grid's
  own axes.
- **Producer quirks and protected sets in the scenario generator.** The test
  scenario generator can now reproduce producer quirks per dataset (swapped
  axes, missing or different datums, unusual CRS declarations, 0…360°
  longitudes, inverted display scales, no-data holes, gaps and expired runs,
  conflicting same-band cells, spiked areas), with tests that pin how the
  library handles each one today. It also writes Part 15 protected exchange
  sets deterministically, with a permit outcome for every case the reader
  reports, and five small synthetic sets are checked in under
  `tests/datasets/ExchangeSets/Protected-*`. Two additions support this:
  `S100Cipher.EncryptDataset` takes a fixed prefix block and IV (for test
  data only), and `Part15Signer` takes a signing key separate from its
  certificate. See [Test scenario generator](scenario-generator.md#producer-quirks).

- **Confidence alongside the clearance.** Check passage no longer hides
  "meets" and "tight" where confidence is lower. A stretch on S-101 depth, or
  where the depth and water-level datums can't be confirmed to agree, keeps
  its clearance colour and is drawn dashed; only no data is grey. The verdict
  follows the clearance, with a line that explains the confidence, and
  departures that meet your settings with lower confidence still count, with
  a caveat. In `EncDotNet.S100.Navigation`, `UkcState.LowConfidence` is
  replaced by `ConfidenceReasons` on each sample, leg, minimum, result and
  departure (a breaking change). `get_passage_result`, `get_check_state` and
  `get_clearance_state` report the state and a `confidence` list separately.
- **One axis-order check for every reader.** The GML, S-102, S-104 and
  S-111 readers share one dataset-level check for swapped latitude and
  longitude, anchored on physical bounds and the declared extent. It replaces
  the separate S-122, S-128 and S-411 rules, and it swaps back the UKHO S-104
  Solent station file, which used to draw in the Indian Ocean, so Check
  passage now applies its tide. The result is each dataset's `AxisOrder`, a
  `{product}-PROJ-AXIS` validation finding, an **Axis order** row in
  `s100 info`, and the dataset warning badge in SoundCharts. See
  [Check the axis order](reading-product-data.md#check-the-axis-order).
- **Route colours on Dusk and Night.** The route's check-mode colours, editing
  line, labels and markers are tuned for the Dusk and Night chart palettes
  and checked for contrast against the S-101 depth shades. On Night, the
  editing accent loses most of its colour.
- **Passage on the Timeline.** While Check passage is on, the Timeline shows
  the passage (departure to arrival, with a tick at the minimum UKC) and its
  departure window, which you can drag to set the departure. **Follow
  passage** pins the view time to the ETA at the Clearance cursor, so S-104
  and S-111 show what the ship meets. The MCP tool `set_follow_passage`
  turns it on, and `get_timeline_state` reports the lanes.
- **Departure window.** The Clearance tab has a departure band under the
  profile. It shows which departures across the loaded S-104 range meet your
  settings, and which arrive after the data ends. Drag it to set the
  departure. The verdict card lists the window, such as "Departures
  05:35–06:40 meet your settings", and **Depart {HH:MM}** jumps to the nearest
  departure in it. The new MCP tool `set_departure` sets the departure, and
  `get_check_state`, `get_clearance_state` and `get_passage_result`
  (`includeWindow`) report the window.
- **Findings for a route.** In Check passage mode, the Routes panel's
  **Findings** tab lists what to review along the route, grouped **MUST
  REVIEW**, **CHECK** and **INFO**: stretches below your UKC setting,
  S-101-only depths, water-level gaps and expired S-111 runs. Clicking one
  zooms the map to it and moves the Clearance cursor there. The shared
  `RouteFinding` model in `EncDotNet.S100.Navigation` is ready for the route
  checks in #888. The MCP tools `get_findings` and `select_finding` read and
  select findings.
- **Clearance profile.** While Check passage is on, the bottom dock gains a
  **Clearance** tab next to the Timeline. It shows the seabed, the
  required-UKC band, the water level and the keel along the route, against
  distance or ETA. A readout traces each number to its S-102 cell or S-101
  area, and to its S-104 station and time step. The MCP tools
  `get_clearance_state` and `set_clearance_view` read and set the tab.
- **Check passage.** In SoundCharts, the Routes panel's **Check passage**
  toggle colours the active route by under-keel clearance at ETA. It shows a
  verdict card with the minimum UKC and where it occurs, and a min-UKC chip on
  each leg. Edits recompute live. The MCP tools `set_check_mode`,
  `get_check_state` and `set_passage_cursor` drive it. See the
  [viewer README](../src/EncDotNet.S100.Viewer/README.md) and the
  [route tools](mcp-server.md#route-tools).
- **Under-keel clearance along a route.** The new
  `EncDotNet.S100.Navigation` package computes leg ETAs and checks UKC along a
  passage. It adds S-104 water level to S-102 charted depth at each sample's
  ETA, falls back to S-101 depths as low confidence, and reports datum
  mismatches instead of mixing datums. The new MCP tool `get_passage_result`
  checks a SoundCharts route or a polyline and draft, and can sweep departures
  to find a window. See the
  [package README](../src/EncDotNet.S100.Navigation/README.md) and
  [Under-keel clearance](mcp-server.md#under-keel-clearance).
- **Raster tiles.** The new `s100 tiles export` command renders a dataset, a
  composite or an exchange set as XYZ Web Mercator tiles for MapLibre,
  Leaflet and OpenLayers, written as a `{z}/{x}/{y}` folder, a PMTiles
  archive or an MBTiles database. Each zoom level draws only what's visible at its scale. See
  [`tiles export`](cli.md#tiles-export).
- **Serving tiles.** `s100 tiles serve` serves tiles on XYZ URLs with a
  TileJSON document and a preview page, so a web map on your computer can use
  them as a tile source. It serves a built folder, PMTiles or MBTiles tile set,
  or renders datasets as their tiles are asked for, in the day, dusk and night
  palettes and, for S-104 and S-111, at any time step chosen in the URL. It reopens the datasets when their files change, and with
  `--cache-dir`, rendered tiles are kept on disk across restarts, reused only
  while the data and settings are unchanged. See
  [`tiles serve`](cli.md#tiles-serve).
- **Overlapping chart cells.** In `s100 tiles export`, `s100 tiles serve`,
  composite `s100 render` and the library's headless compositor, a finer
  chart cell now hides the coarser cell it overlaps, as SoundCharts does, so
  overlapping cells of different scales no longer draw on top of each other.
- **Signing and permits.** S-100 Part 15 support now covers the data
  server's side. `Part15Signer` produces ECDSA P-384 signatures, and
  `PermitFileWriter` writes a `PERMIT.XML` and its `PERMIT.SIGN`, using cell
  keys wrapped by `DataPermit.Create`. The existing readers verify everything
  these types write. See
  [Reading protected exchange sets](protected-exchange-sets.md#create-a-test-exchange-set).
- **S-57 validation reports the source data.** Validating a NOAA cell no
  longer reports thousands of `S101-as-S57/*` errors caused by the S-57 to
  S-101 translation. Complex sub-attributes now carry their parent index, ring
  checks follow edge orientation, and bridge spans get their own feature
  identifier. Findings now carry a location, so the viewer's validation
  overlay marks them. See [Bringing S-57 into the pipeline](s57-to-s101.md).
- **Documentation.** [Getting started](getting-started.md) now has a
  quickstart for each of the desktop app, the .NET library and the
  command-line tool, and the [documentation home](index.md) replaces the
  Start here page. A [documentation style guide](docs-style.md) sets the
  structure and tone for these pages.

## September 2026

- **Local collection manifests.** A hand-written `*.s100collection.json`
  file names groups of local folders, such as one per producing country. Add
  it to the viewer's Library, pick groups as you would an online catalogue's
  facets, and browse, load or change each group on its own. See
  [Local collection manifests](local-collection-manifest.md).
- **Developer guides** for the library:
  [Loading datasets](loading-datasets.md),
  [Reading protected exchange sets](protected-exchange-sets.md),
  [Reading product data](reading-product-data.md) and
  [Custom catalogues and validation](catalogues-and-validation.md). Their code
  samples were run against the repository's test data.
- **Facade additions.** The `EncDotNet.S100` package gained
  `S100Dataset.OpenAsync(IAssetSource, …)`, `S100ExchangeSet` (folders,
  `CATALOG.XML` or ZIPs, with S-101 updates applied), and the
  `WithDecryption` and `Validate` extensions.
- **XML documentation.** Every public API in the NuGet packages has XML
  documentation, and the packages include it for IntelliSense.
- **Docs site.** The API reference covers every package, the package READMEs
  are published under **Packages**, and the site is built on every pull
  request.
- **S-401 (IEHG inland ENC).** The IEHG feature and portrayal catalogues are
  bundled, and content-based detection tells S-401, S-101 and S-57 apart
  inside the shared `.000` extension. S-401 datasets support render,
  identify, query and describe. No validation rule pack ships yet, so
  `s100 validate` reports that no rules are available for S-401.
- **S-57 inland ENCs.** Cells that declare `DSID`/`PRSP` = 10, such as USACE
  river charts, are portrayed with the S-401 catalogues and keep their S-57
  identity. Large inland cells that couldn't be opened before now load,
  because of a fix in the ISO 8211 reader in `EncDotNet.Iso8211` 0.6.1. The
  inland object classes and attributes of the IENC Feature Catalogue 2.4 are
  translated to S-401, so inland bridges, distance marks, notice marks and
  waterway gauges appear.
- **`s100 s57 convert` and inland cells.** The command writes an inland ENC
  cell as an S-401 dataset, so its inland features are kept.
  `--target s101|s401` overrides the choice, and the summary and the
  `--report` JSON name the product written.

## July 2026

- **Docs site.** Added the Start here page, the task guides
  ([Render S-102 to PNG](scenarios/render-s102-to-png.md),
  [Inspect S-124 warnings](scenarios/inspect-s124-warnings.md) and
  [Compose S-101 and S-102](scenarios/compose-s101-s102.md)), the
  [Top APIs](top-apis.md) page, architecture and workflow diagrams, and a
  site theme.
- **S-57 guide.** Added [Bringing S-57 into the pipeline](s57-to-s101.md),
  which covers converting, viewing and validating S-57 cells, folding in
  sibling updates, and the `s100 s57 convert` diagnostics summary
  (`--report`).

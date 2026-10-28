# Wind and wave data — Design Note

> Status: **Design / Research**. No production code yet. This note is
> Phase 0 of the epic #1055. It settles the open design questions,
> proposes pipeline refactors that make a product without a feature or
> portrayal catalogue a first-class citizen (§8), and proposes how to
> split the rest of the epic into work items (§9).

## 0. Scope

Wind and wave data on the chart, from formats that predate S-100:

| Data | Format | Where it enters the pipeline |
|---|---|---|
| Forecast and analysis grids | GRIB2, NetCDF-CF (NetCDF-4 and NetCDF-3) | A dataset processor with an `ICoverageSource` |
| Marine weather warnings | CAP 1.2 XML, NWS GeoJSON | A dataset processor that emits polygons |
| Observations | NDBC text, NMEA 0183 | An `IDynamicFeatureSource` |

In scope: how these files are recognised and identified, the
internal grid model, drawing, layer order, time, picking, warnings,
live observations, remote sources, and the CLI and MCP surfaces.

Out of scope:

- The S-100 weather products themselves (S-412, S-413, S-414). They
  aren't published yet. §1.1 explains how this work prepares for them.
- Weather routing, or using weather in route checks.
- BUFR. §6.4 explains why it's last.
- Gaussian, rotated, polar stereographic and Lambert grids. §3.4 says
  how they're rejected.

## 1. Background

### 1.1 The S-100 weather products

The WMO owns the S-411 to S-420 range. S-411 (sea ice) is published and
supported here. The weather products aren't ready:

- **S-412**, renamed from "Weather Overlay" to **Marine Weather
  Warnings**, is in development. NOAA's Ocean Prediction Center leads
  it. It covers polygons where wind, wave or ice accretion meet warning
  criteria in a time window.
- **S-413** (Marine Weather and Wave Conditions) and **S-414** (Marine
  Weather and Wave Observations) start after S-412.
- NOAA's 2026 timeline has S-411 and S-412 reaching new ECDIS builds
  between 2026 and 2029.

This design is shaped so the S-41X products slot in later:

- S-413 will very likely be an S-100 Part 10c HDF5 grid like S-111. It
  will then reuse the grid model (§3), drawing (§4) and layer order (§5)
  from this work, and only need a reader.
- S-412 polygons will reuse the warning layer from §6.1.
- S-414 will reuse the observation features from §6.2.

### 1.2 Data sources

| Source | Format | Licence | Notes |
|---|---|---|---|
| NOAA NOMADS (GFS, GFS-Wave / WAVEWATCH III) | GRIB2 | Public domain | The GRIB filter returns chosen variables, area and hours. |
| NOAA Open Data Dissemination on AWS (`noaa-gfs-bdp-pds`) | GRIB2 | Public domain | Whole files, plus `.idx` indexes for byte-range reads. |
| NWS NDFD | GRIB2 | Public domain | US coastal and offshore grids. Uses Lambert conformal grids, so it's out of scope for now (§3.4). |
| ECMWF Open Data | GRIB2 | CC-BY 4.0 | IFS and wave model at 0.25°. |
| DWD Open Data | GRIB2 | Attribution | ICON and wave models. |
| Copernicus Marine | NetCDF-4 (CF) | Free, attribution, account required | Global and regional wave forecasts. |
| NOAA ERDDAP / THREDDS | NetCDF-3 or NetCDF-4 (CF) | Public domain | Subsets by URL. ERDDAP's `.nc` output is NetCDF-3. |
| `api.weather.gov/alerts` | CAP XML, GeoJSON | Public domain | Marine alerts. Many carry zone codes instead of a polygon. |
| NDBC `realtime2/` | Fixed-width text | Public domain | Buoy and C-MAN observations. |

Verify the current URLs and paths before building against them.

## 2. Constraints

- **MIT licence.** No GPL code or data. ecCodes is Apache-2.0, but see
  §7.2 for why it isn't the plan. Check the licence of any GRIB, NetCDF
  or BUFR dependency.
- **Managed code.** The libraries run on Windows, macOS and Linux, and
  in the headless renderer. A native dependency needs a build per
  platform and complicates packaging, so avoid one.
- **Not for navigation, and labelled so.** None of this is official
  S-100 or ENC data. It must say so wherever it appears (§3.1).
- **The S-100 portrayal engines stay S-100.** The Lua and XSLT engines
  aren't a generic display engine (dynamic-feature-source.md §1.1).
  Drawing for these formats is written in code (§4).
- **Small, committable fixtures.** Public-domain subsets or generated
  files, each well under 1 MB.

## 3. Grids

### 3.1 Identity

Every processor reports a `SpecRef`. `SpecName.Normalize` only accepts
`S-nn` or `S-nnn` names, and throws for anything else. S-57 is already
registered as `"S-57"`, so the registry handles a format that isn't
S-100, but S-57 still matches the name pattern.

**Decision:** add a small, closed set of non-S-100 format names to
`SpecName`: `GRIB2`, `NetCDF`, `CAP`. `SpecName.Normalize` accepts them
as-is, case-insensitively. The edition is the format's version:
`GRIB2/2.0.0`, `NetCDF/4.0.0` or `NetCDF/3.0.0`, `CAP/1.2.0`. Whether a
product is S-100 is a trait on its registration (§8.2), so hosts ask
the registry rather than parse names.

Where non-S-100 data is labelled:

- **Layer stack.** The layer's name carries the format, model and run,
  for example "GFS-Wave · 2026-10-25 06Z", plus a "Not an S-100
  product" badge.
- **Pick reports.** A first line: "Weather model data, not for
  navigation."
- **Headless images.** No change. Captions are the host's job.

Rejected alternatives:

- **A reserved number such as `S-0nn`.** It looks like a real spec and
  would confuse anyone reading a report.
- **A separate "format" property next to `Spec`.** Every consumer of
  `Spec` (MCP, the Library, validation, telemetry tags) would need to
  handle both.

### 3.2 Detection and registration

`DatasetPipelineFactory.DetectProductSpec` dispatches on the file
extension. An HDF5 file with no recognised `productSpecification`
attribute is treated as S-102.

Changes:

- **`.grb`, `.grb2`, `.grib2`, `.grib`:** check that the file starts
  with the bytes `GRIB` and that edition byte 7 is 2. A GRIB1 file
  fails with a clear "GRIB edition 1 isn't supported" message.
- **`.nc`, `.nc4`:** sniff the first bytes. `CDF\x01` or `CDF\x02`
  means NetCDF-3. The HDF5 signature means NetCDF-4.
- **`.h5`/`.hdf5` that are really NetCDF-4.** Some NetCDF-4 files use an
  HDF5 extension. Before the S-102 fallback, check for a root
  `Conventions` attribute containing `CF-`. If it's there, return
  `NetCDF`.
- **Files with no extension.** The NOMADS GRIB filter's download name
  has no useful extension. The Library and CLI should sniff the leading
  bytes when the extension is unknown. That's a small, general
  improvement and not specific to weather.

Each format is a product registration with `CreateFromPath`,
`CreateFromSource` and the traits from §8.2, registered in
`S100Products.RegisterAll`. A host can leave them out of its registry,
as it can any product.

### 3.3 Project layout

| Project | Contents |
|---|---|
| `EncDotNet.S100.Datasets.Grib` | GRIB2 decoder (§7.2) and the GRIB2 → weather grid adapter |
| `EncDotNet.S100.Datasets.NetCdf` | NetCDF-3 reader, NetCDF-4 reader on PureHDF, and the CF → weather grid adapter |
| `EncDotNet.S100.Datasets.Weather` | The shared weather grid model (§3.4), `WeatherCoverageSource`, the parameter table, and the code-defined portrayal (§4) |
| `EncDotNet.S100.Datasets.Pipelines` | `GribDatasetProcessor`, `NetCdfDatasetProcessor` (both thin over a shared `WeatherGridProcessorBase`), and later `CapDatasetProcessor` |

The format projects don't reference each other. Both depend on
`Datasets.Weather`.

### 3.4 The weather grid model

Both readers produce the same in-memory model. Everything after the
reader is shared.

```text
WeatherGridSet
  Origin            model/centre name, run (reference) time
  Grid              regular lat/lon: origin, spacing, rows, columns
  Parameters[]      one per variable × level
    Parameter       canonical id (§3.5), units, level description
    Steps[]         valid time → float[rows × columns]
```

Readers normalise to the order the coverage pipeline already expects
(`GridMetadata`: origin at the south-west corner, rows running north,
columns running east):

- **North-to-south rows** (GRIB scanning mode bit 2 clear, or a
  decreasing CF latitude axis): flip the rows.
- **Longitudes 0 to 360:** move the columns at 180° and above to the
  west, so the grid spans −180 to 180. A regional grid that sits
  entirely east of 180° (for example 175°E to 225°E) gets −360 instead.
  A grid that truly crosses the antimeridian is split at read time into
  two column blocks, but at first it's rejected with a clear message
  (open question §10.3).
- **Missing values** (GRIB bitmap, CF `_FillValue` / `missing_value`)
  become `NaN`, and `CoverageMetadata.NoDataValue` is `NaN`.
- **CF packing** (`scale_factor`, `add_offset`) is applied when the
  values are read.

Grids that aren't regular lat/lon fail with a clear message naming the
grid type: GRIB grid templates other than 3.0, or CF data with 2-D
latitude/longitude or a `grid_mapping` attribute. Projected grids
(Lambert for NDFD, polar stereographic) would need per-cell
reprojection. That's for later.

Steps of one parameter can come from several files. NOMADS and AWS
publish one GRIB2 file per forecast hour, and GRIB2 messages can simply
be concatenated. So a dataset is one of these:

- one file holding many messages or time steps,
- several files concatenated by the user (or by the GRIB filter
  request), or
- later (work item 16), a folder or Library collection holding one run,
  with the files merged by run time and parameter.

Memory: a global 0.25° grid is 1440 × 721 cells, about 4 MB per
parameter and step as `float`. A 5-day, 3-hourly run of six parameters
is about 1 GB. So:

- the reader indexes messages or variables when the file opens, and
  decodes a step only when it's selected, keeping a small cache of
  recently used steps (the `DeferValueReads` idea in `S111ReadOptions`);
- the `ICoverageSource` reports overview levels made by 2× and 4×
  decimation, so zoomed-out views sample a smaller grid.

### 3.5 Parameters and units

The readers map their own parameter codes to a short canonical list.
Unknown parameters are listed in `info` output but not drawn.

| Canonical id | GRIB2 (discipline, category, number) | CF `standard_name` | Unit stored |
|---|---|---|---|
| `wind.u`, `wind.v` (10 m) | 0,2,2 / 0,2,3 at height 10 m | `eastward_wind`, `northward_wind` | m/s |
| `wind.speed` (10 m) | 0,2,1 | `wind_speed` | m/s |
| `wind.from_direction` (10 m) | 0,2,0 | `wind_from_direction` | degrees true |
| `wind.gust` | 0,2,22 | `wind_speed_of_gust` | m/s |
| `wave.hs` (significant height, combined) | 10,0,3 | `sea_surface_wave_significant_height` | m |
| `wave.dir` (primary or mean, from) | 10,0,10 / 10,0,14 | `sea_surface_wave_from_direction` | degrees true |
| `wave.period` (peak or mean) | 10,0,11 / 10,0,15 | `sea_surface_wave_period_at_variance_spectral_density_maximum` / `sea_surface_wave_mean_period` | s |
| `wave.wind_sea.hs`, `wave.swell.hs` | 10,0,5 / 10,0,8 | `sea_surface_wind_wave_significant_height` / `sea_surface_swell_wave_significant_height` | m |
| `wave.wind_sea.dir` (from; added in #1074) | 10,0,4 | `sea_surface_wind_wave_from_direction` | degrees true |
| `pressure.msl` | 0,3,1 / 0,3,0 at mean sea level (ECMWF `msl`, added in #1074) | `air_pressure_at_mean_sea_level` | Pa |

The table is data in `Datasets.Weather`. Check the exact codes against
the WMO GRIB2 code tables and the CF standard name table while
implementing.

Derived fields:

- When only `wind.u` and `wind.v` exist, the coverage source derives
  `wind.speed` and `wind.from_direction` from them, cell by cell, as
  they're sampled.
- **Direction convention:** wind and wave directions are "from".
  S-111 currents are "toward", and `MapsuiCoverageArrowRenderer`
  rotates glyphs by the field value as given. Wave arrows use the
  "from" field with a 180° rotation offset on the symbol scheme
  (§8.6). Wind barbs are drawn with the barb's staff pointing into the
  wind (§4.2), so they use the "from" value with no offset.

Units in the public API use the quantity types (`Speed`, `Length`,
`Angle`). Pick reports show knots and metres by default, following the
mariner settings.

## 4. Drawing

### 4.1 A code-defined coverage portrayal catalogue

The coverage pipeline already takes its styling from an interface:
`CoveragePipeline.ProcessAsync` takes any `ICoveragePortrayalCatalogue`.

- `ResolveColorScheme` returns a `CoverageColorScheme` (colour bands on
  one value field).
- `ResolveSymbolScheme` returns a `CoverageSymbolScheme`. These are
  symbol bands keyed on a value field, rotated by another field, with
  the symbol references resolved to SVG through the sub-layer's
  `SymbolProvider`.
- `ActivePalette` and `SwitchPaletteAsync` cover day, dusk and night.

**Decision:** implement a code-defined coverage style in
`Datasets.Weather`. There's one instance per drawn parameter group:
wind, wave height, wave direction, or pressure. Each instance builds
its bands and palette in code from a small built-in style table. No
XSLT, Lua or catalogue files. Today that means implementing
`ICoveragePortrayalCatalogue`. After the refactor in §8.5 it's an
`ICoverageStyle`, built with the shared toolkit from §8.7, as S-104's
code-defined catalogue will be.

This reuses the coverage pipeline with only the general changes from
§8: viewport subsetting, overview levels, telemetry,
`GridCoverageSubLayer` colour fills, and `ArrowCoverageSubLayer` glyphs
with per-frame thinning (`SymbolThinning`). The Mapsui and Skia
renderers need no weather-specific code.

### 4.2 Styles

| Layer | Sub-layer | Value field | Rotation field | Style |
|---|---|---|---|---|
| Wind barbs | `ArrowCoverageSubLayer` | `wind.speed` (kn) | `wind.from_direction` | WMO barbs. One `SymbolBand` per 5 kn step, 0–200 kn; calm below 2.5 kn. On by default. |
| Wind speed | `GridCoverageSubLayer` | `wind.speed` (kn) | — | Bands at Beaufort boundaries. Off by default, switchable in Display controls. |
| Wave height | `GridCoverageSubLayer` | `wave.hs` (m) | — | Bands at 0.5, 1, 1.5, 2, 2.5, 3, 4, 5, 6, 7, 8, 9, 12, 14 m (the Douglas sea-state boundaries, made finer). On by default. |
| Wave direction | `ArrowCoverageSubLayer` | `wave.hs` | `wave.dir` (offset 180°) | One arrow symbol scaled by height (`SymbolBand.ScaleByValue`). Off by default, switchable in Display controls. |
| Pressure | `IsolineCoverageSubLayer` | `pressure.msl` (hPa) | — | 4 hPa isobars. Needs the contour generalisation below. On by default (decided in #1072; see §10.1). |

Symbols:

- **SVGs live in `Datasets.Weather` as embedded resources**, generated
  once by a test-time script and checked in. There are 41 barb symbols
  plus a calm circle and one wave arrow.
- **Barbs are drawn for the northern hemisphere.** Barbs in the
  southern hemisphere are mirrored by convention. Today a symbol band is
  chosen from the value alone, so the symbol scheme needs to see the
  cell's latitude (§8.6). There are two symbol sets, one per
  hemisphere.

Colours and readability:

- **Palettes.** Day, dusk and night variants of each band set, kept
  dim enough for night viewing. Colours avoid the S-101 depth and
  danger hues, so a wave band is never mistaken for a shallow-water
  band.
- **Opacity.** Colour fills default to `Opacity = 0.5`, which
  `GridCoverageSubLayer` already supports, so the chart stays readable.
- **Land.** Wave fills are clipped to water when an S-101 chart is
  loaded, reusing the S-104 `LandAreaMask` rule. Wind isn't clipped.

**Contour generalisation:** `ContourStyle.Depth` is typed as `Depth`.
Isobars need a plain value with a unit (§8.6). Pressure is the only
isoline layer, so it comes with the drawing polish.

### 4.3 Legends

Colour fills need a legend: band colours plus labels. `ColorBand.Label`
already exists. The viewer shows legends next to the layer stack entry,
not on the map. The headless renderer doesn't draw a legend.

**As built (#1072).** The drawing polish for §4.2, §4.3 and §5:

- **Southern barbs.** `tools/GenerateWeatherSymbols` also writes
  `WXBARBS005` to `WXBARBS200`, the northern barbs mirrored about the staff
  (feathers on the anticlockwise side); the northern SVGs are unchanged.
  `WindBarbStyle` sets `SouthernHemisphereBands` to the same ranges with the
  southern symbols, so `CoverageSymbolScheme.Resolve(value, latitude)` picks
  them for cells below 0° (the equator is northern). The calm circle is
  shared. `WindBarbSymbols.For(knots, latitude)` (and `WindBarb.SymbolNameAt`)
  is the same rule for one barb; `NdbcStationPortrayal` uses it with the
  station's latitude, and the NMEA own-ship barb (#1073) can too.
- **Land clipping.** The S-104 rule wasn't copied. `S98DefaultRules` now has
  one shared effect, `ClipSurfacesToWater`, that gathers the active S-101
  `LandArea` surfaces and attaches them as `LandAreaMask`. `R-101-104-B` uses
  it for S-104, and a new `R-101-WX-B` uses it for any active
  `GridCoverageSubLayer` with `ClipToWater` set. A flag rather than a product
  pair, because GRIB2 and NetCDF aren't S-100 products and only the wave
  height fill should be clipped (`WeatherCoverageStyle.ClipToWater`). The
  renderers' existing output-pixel clip does the rest.
- **Legends.** `GridCoverageSubLayer.Legend` (a list of
  `CoverageLegendEntry(Color, Label)`, from `CoverageLegendEntry.FromScheme`)
  is set by `WeatherPortrayal` for the weather fills only, in the palette the
  fill was drawn in. S-102, whose bands also have labels, gets no legend: it's
  opt-in per sub-layer. The Layer stack entry shows it as a swatch and label
  per band (`LayerStack.Entry.Legend`, `LayerStack.Entry.Legend.Band`).
- **Isobars.** Neither renderer turned `ICoverageStyle.Contours` into lines;
  both already drew `IsolineCoverageSubLayer` (from S-104) with labels. The
  new `CoverageContours.Trace` (in Core) traces a style's value-based
  `ContourStyle`s through the pipeline's sampled grid with the existing
  marching-squares `IsolineTracer`, skipping levels outside the sample's
  range. `PressureIsobarStyle` (`WeatherLayerKind.Isolines`) lists every
  4 hPa from 860 to 1100 hPa in pascals, the stored unit, labelled in whole
  hPa, solid, on `OtherChartOverlays` at priority 90, on by default, not
  clipped. An `IsolineCoverageSubLayer` has one ink and width, so the
  sub-layer takes them from the first contour; per-level styles (a heavier
  1000 hPa line, say) would need per-line appearance.
- **Defaults and the Weather switches.** The decision (§10.1): the wave
  height fill, wind barbs and isobars are on; the wind speed fill and wave
  arrows are off, because they compete with the wave fill and the barbs.
  So `PressureIsobarStyle.IsVisibleByDefault` is true. The optional layers
  are switched globally rather than per dataset:
  - The switches are a new `EcdisDisplaySettings.LayerVisibility`
    (layer key → on/off), so they reach every processor on the
    `RenderContext`, as the S-411 display mode does. A missing key means the
    style's default.
  - `WeatherGridProcessorBase` offers every style that applies (`LayerKeys`)
    and draws those for which `WeatherCoverageStyle.IsDrawnWith(display)` is
    true. Its content scope names the drawn layers, so a tile cache doesn't
    serve stale tiles after a switch.
  - The viewer keeps them in `EcdisDisplayState` (`Get/SetLayerVisible`),
    which raises `Changed`. `ViewerPresentationCoordinator` then re-renders
    every dataset, as for a viewing-group change.
    `EcdisDisplayStatePersistence` saves them as
    `ViewerSettings.EcdisLayerVisibility`, beside
    `EcdisShowNavigationalWarnings`.
  - The Display controls panel gets a Weather section with Isobars, Wind
    speed fill and Wave arrows (`EcdisDisplayPanel.Weather.*`). It's shown
    while a GRIB2 or NetCDF dataset is loaded, as the per-spec sections show
    for loaded specs. The always-on layers get no switch.
  - A layer switched off isn't built, so the Layer stack doesn't list it; the
    stack lists what is drawn. The S-124 switch differs here: it hides
    features inside a layer, so its rows stay. A layer switched on is a
    normal sub-layer, so the dataset's own sub-layer eye in the Datasets
    panel can still hide it. It shows only when both are on.
  - Headless rendering passes no switches, so `s100 render` draws the
    defaults: isobars wherever the file has `pressure.msl`, and no wind speed
    fill or wave arrows.
- **Goldens.** New: southern barbs (Day, Night), the wave fill over the
  S-101 test cell clipped to water, and isobars (Day, Night). The three
  existing composites over that cell (32°S) were re-accepted: their barbs are
  now southern and their wave fill no longer covers land.

## 5. Layer order

`S98DisplayPlane` values are spaced 10 apart. Existing products:

- S-104 water level: `OnDemandSurface`
- S-111 arrows: `DynamicArrows` at priority 10
- S-124 warnings: `CautionsAndWarnings`

**Decision:**

| Weather layer | Plane | Within-plane priority |
|---|---|---|
| Wave height and wind speed fills | `OnDemandSurface` | 0, below the S-104 surface |
| Pressure isobars | `OtherChartOverlays` | 90 |
| Wind barbs, wave arrows | `DynamicArrows` | 20, above S-111 |
| CAP warnings | `CautionsAndWarnings` | after S-124 |
| Observations | `OtherChartOverlays` | 100 |

No new plane in v1. A fill under the ENC's line work, but over its
area fills, is what `OnDemandSurface` means. Add a plane only if a real
conflict appears.

## 6. Warnings, observations and remote sources

### 6.1 CAP warnings

`CapDatasetProcessor` reads:

- CAP 1.2 XML, one `<alert>` or an Atom feed of them;
- the NWS GeoJSON form from `api.weather.gov/alerts`.

How warnings are handled:

- **Shapes.** `<area><polygon>` becomes a polygon. `<circle>` becomes a
  polygon approximated with 64 vertices.
- **Zone codes.** An area with only a zone code (`<geocode>` UGC, for
  example `ANZ535`) is resolved against a marine-zone layer. That layer
  is the NWS marine zone shapefile, converted once to a compact,
  committed GeoJSON with simplified outlines. Its licence is public
  domain. A code with no match is listed in the pick report and
  `info`, not dropped.
- **Validity.** `onset`/`effective` to `expires`. Drawing honours the
  view time, as S-124 warnings do (S-98 §12.12 handling). Reuse that
  logic rather than copying it.
- **Style.** Outlined and hatched by severity: Extreme, Severe,
  Moderate, Minor. Labelled with `event` (for example "Gale Warning").
  This is drawn with ordinary drawing instructions through
  `VectorCoverageSubLayer`, so labels follow scale and declutter rules.
- **Navigational warnings panel.** Out of scope. CAP alerts aren't
  S-124 warnings and don't follow S-124's numbering or cancellation
  rules. Revisit if users want them listed there.

**As built (#1069).**

- **Projects.** The reader, the zone layer and the style are a new package,
  `EncDotNet.S100.Datasets.Cap` (Core and Portrayals only), like the GRIB2
  and NetCDF readers; `CapDatasetProcessor` is in `Datasets.Pipelines` and
  registered in `WeatherProducts` as `CAP/1.2.0`, not S-100, with an
  application portrayal (`CAP 1.2`) and its own pick-report notice.
  Detection is `MatchBytes` on the first kilobyte (a CAP `alert` root, an
  Atom `feed` declaring the CAP namespace, or `api.weather.gov` JSON naming
  an alert) plus `MatchGml` for an exchange set's `.xml`; S-124 and other
  XML or GeoJSON aren't claimed.
- **Atom.** Both entry shapes: a whole `<alert>` inside the entry, and the
  NWS entry whose fields are `cap:` elements (identifier from the entry's
  `id`, headline from `title`, description from `summary`).
- **Zone layer.** All 696 NWS coastal and offshore zones (`mz18mr25`,
  `oz18mr25`), Douglas-Peucker at 0.005° and four decimals, 798 KB, embedded.
  weather.gov was unreachable, so the features came from a mirror that keeps
  the shapefile records unchanged; `tools/cap` has the scripts and the
  provenance. Only `UGC` geocodes are resolved (not marine SAME codes), and
  only for an area with no polygon or circle of its own.
- **Validity.** The NWS sets `expires` to when the message should be
  replaced, often before the hazard starts (a gale warning for tomorrow that
  expires tonight), and gives the hazard's end separately (`ends` in GeoJSON,
  the `eventEndingTime` parameter in CAP XML). An area is in force from
  `onset` (else `effective`, else `sent`) to the later of the two. An `Update`
  or `Cancel` in the same file ends what it references from its `sent`; a
  `Cancel` isn't drawn.
- **In-force logic.** The single-warning rule moved out of
  `NavigationalWarningInfo.StatusAt` into `WarningValidity` (Core), which both
  use. S-124's view-time filtering is host-side (the viewer hides parts
  through `RenderContext.HiddenFeatures`); CAP instead implements
  `ITimeAwareDatasetProcessor`, with a step at each start and at the tick
  after each end (`WarningValidity.Changes`) and `AtOrBefore` selection, so
  the Mapsui session, `s100 render --time-step` and tiles hand it a time that
  judges exactly what's in force, with no viewer change. No time step draws
  every warning not superseded.
- **Drawing.** A vector sub-layer (`IVectorPortrayalSource`), not
  `VectorCoverageSubLayer`: CAP areas are features with ids, so the vector
  path gives picking, hit tests and highlights for free. Hatching is an S-100
  pattern area fill (one SVG tile per severity, a new `AreaPattern` on the
  toolkit's `InstructionBuilder`); the spacing differs by severity as well as
  the colour. Labels have SCAMIN 1:3,000,000.

### 6.2 Observations

Two `IDynamicFeatureSource` implementations, following the AIS source
pattern (ais-source.md):

- **`NdbcObservationSource`** polls `realtime2/` files. The default
  interval is 10 minutes, which is the shortest update NDBC publishes.
  It only fetches stations inside the viewport plus a margin, using the
  NDBC station table, and emits one point feature per station. The
  symbol is a station-model glyph: a wind barb plus a wave-height
  label.
- **`NmeaWeatherSource`** reads `MWV`, `MWD`, `MTW` and `XDR` from an
  existing NMEA stream. It produces one feature at own-ship: true wind
  as a barb next to the own-ship symbol. There's no NMEA reader in the
  repo yet: own-ship's `IOwnShipPositionProvider` only anticipates an
  NMEA driver. So this item adds a minimal sentence reader that an
  own-ship NMEA driver can share later.

**As built (#1070).** `NdbcObservationSource` lives in a new
`EncDotNet.S100.DynamicSources.Ndbc` library, beside the AIS one, and
depends only on Core, Portrayals and Datasets.Weather (for the barb
symbols). Choices made along the way:

- **Station table.** `activestations.xml`, not `station_table.txt`: it has
  decimal positions, the station type and a `met` flag, so only stations
  with a standard meteorological file are fetched. It is reloaded daily.
- **Fetching.** Stations inside the viewport plus 0.5°, nearest the centre
  first, at most 60 per poll and 4 at a time. Like the AIS gate
  (ais-zoom-gated-subscription.md) nothing is fetched while the viewport is
  wider than 25° or before the host sets an area, and also while the host's
  layer is hidden (`IsActive`). A `realtime2` file holds 45 days, so each
  request asks for its first 16 KiB with a `Range` header and the parser
  stops at rows older than 12 hours. A station that leaves the area leaves
  the snapshot but keeps its observation, so panning back needs no fetch.
- **Latest values.** Buoys report waves hourly and wind every 10 minutes,
  so `NdbcObservation.Combine` fills the newest row from rows up to an hour
  older, keeping wind and wave values in groups; a pick says when the wave
  values are older.
- **Polling and errors.** Each station is fetched 10 minutes after its last
  success. A failure (HTTP error, timeout, or a body that isn't the format)
  waits 10, 20, 40 minutes and so on, up to 2 hours; the station table backs
  off from 1 minute. The loop runs on an injected `TimeProvider` and is woken
  by an area change.
- **Ageing.** Stale after 3 hours (feature kind `weather.observation.ndbc.stale`,
  drawn in the grey `NDBCSTALE` ink by re-colouring the same SVGs), dropped
  after 12.
- **Drawing.** `NdbcStationPortrayal` builds a marker, the `WXBARBN*`/`WXCALM`
  barb rotated by the "from" direction in the geographic frame, and a
  wave-height label (SCAMIN 1:2,000,000) with `InstructionBuilder`. It
  implements a new `IDynamicFeaturePortrayal` (Portrayals.Application); the
  Mapsui side is a generic `PortrayedDynamicFeatureRenderer`, which lowers the
  instructions with the vector scene builder into self-styled Mapsui features
  (`MapsuiDisplayListRenderer.RenderFeatures`). Barbs are northern-hemisphere
  everywhere until #1072. The palette follows Day/Dusk/Night on the next
  update, not instantly.
- **Plane.** `DynamicSourceMetadata` gained `Plane` and
  `WithinPlanePriority`; the source says `OtherChartOverlays`, 100, and the
  Layer Stack groups it there. The dynamic-source host still draws every
  dynamic layer in the overlay band above the charts, so the plane orders the
  Layer Stack, not yet the paint order.
- **Picking.** A source may implement the new `IDynamicFeatureDescriber`;
  the viewer's dynamic pick shows its title and labelled values (wind in kn
  and m/s, wave height in m and ft, periods, directions, pressure,
  temperatures, observation time and age) after the position.
- **Viewer.** "NDBC buoy observations" is a Layer Stack row, off on first
  run; its visibility persists like the other dynamic sources'.
- **Fixtures.** NOAA's hosts were blocked from the build environment, so
  the `realtime2` files and station table under `tests/datasets/NDBC` are
  hand-written in the documented format (see its README).

**As built (#1073).** The sentence reader is its own library,
`EncDotNet.S100.Nmea` (Core only), so an own-ship NMEA driver can share it;
`NmeaWeatherSource` lives in `EncDotNet.S100.DynamicSources.Nmea`, beside the
NDBC one, and reuses the weather styles' barbs and palette. Choices made
along the way:

- **Parsing.** A checksum is required by default: a sentence cut off before
  its end has none, so it can't otherwise be told from a whole one. A tag
  block is skipped, and when a cut-off sentence is glued to a whole one the
  last start character wins. Decoding goes by formatter, whatever the
  talker. `XDR` names are the maker's, so air temperature and pressure are
  picked by type, unit and a plausible range, with names such as `ENGINE`,
  `OIL`, `TANK`, `CABIN` or `WATER` ruling a reading out.
- **Input.** `INmeaLineSource` opens its input per enumeration and closes it
  when the enumeration ends (so replays don't hold the file); stream, replay
  file (paced, looped), UDP and TCP (reconnecting) drivers. A host that
  already reads NMEA can call `ProcessLine` instead.
- **Heading and motion.** `IOwnShipPositionProvider` is viewer-internal and,
  in SoundCharts, simulated, so the library doesn't depend on it. The source
  also decodes `HDT`, `HDG` (with variation from `HDG` or `RMC`), `VHW`,
  `RMC` and `VTG` from the same stream, and uses them first: they come from
  the same instruments as the wind and match it in time. The host's own-ship
  fix (`UpdateOwnShip`) fills in what the stream lacks and places the feature.
  Stream motion older than 10 s isn't combined with the wind.
- **True wind,** best first: `MWD`; derived from `MWV` R, heading, SOG and
  COG (**over the ground**, the reference forecasts and buoys use, so the
  barbs compare like with like); `MWV` T turned by the heading; derived from
  `MWV` R, heading and speed through the water. Below 0.2 kn the heading
  stands in for COG. With none of these, the relative wind is kept, drawn
  grey from heading plus the relative angle when the heading is known (no
  barb without one), and labelled `Rel. wind`. Units N, M, K and S are
  converted; `MWV` status `V` and invalid `RMC`/`VTG` fixes are ignored.
- **Ageing and rate.** Values not repeated for 1 minute are dropped; the
  feature is republished at most once a second.
- **Drawing.** `OwnShipWeatherPortrayal` puts a small ring and the barb 8 mm
  right of and above own ship (a local offset, fixed on screen), so the
  glyph sits beside the own-ship symbol rather than on its hull, with wind
  and water-temperature labels. The barb comes from #1072's
  `WindBarbSymbols.For` at own ship's latitude (southern south of the
  equator, northern on and north of it), as NDBC's does; the grey
  relative-wind recolouring covers both sets. Plane
  `OtherChartOverlays`, priority 110, above NDBC observations.
- **Viewer.** Registered only when `SOUNDCHARTS_NMEA_INPUT` names an input;
  then "Own-ship wind and weather (NMEA)" is a Layer Stack row. The own-ship
  fix is forwarded only while the own-ship overlay is shown, so with it
  hidden the glyph follows the stream's `RMC` rather than the invisible
  simulated ship.
- **Fixtures.** No instrument was available, so the recordings under
  `tests/datasets/NMEA` are hand-written from one consistent scene with
  computed checksums (see its README).

### 6.3 Remote sources in the Library

Add entries to `known-sources.json`, following the NOAA AWS pattern
(noaa-s100-aws-brief.md):

- **NOMADS GRIB filter.** For GFS-Wave and GFS, the Library builds a
  filter URL from the viewport, the chosen parameters and the forecast
  hours. It downloads a single concatenated GRIB2 file.
- **AWS `noaa-gfs-bdp-pds`.** Byte-range reads driven by the `.idx`
  files, for larger areas.
- **`api.weather.gov` marine alerts** for the viewport's marine zones.
- **ERDDAP** datasets chosen by dataset ID.

Freshness rules:

- Models run every 6 hours, so a collection records the run time.
- The Library marks a run older than 12 hours as stale, and offers
  "load latest run".
- Old runs aren't kept unless pinned.

### 6.4 Why BUFR is last

BUFR decoding needs the full WMO table set (Tables B and D, plus local
tables). Doing it well is a project of its own. NDBC's text files and
the NMEA stream cover the main observation needs. If WIS 2.0 becomes a
priority, decide then between a managed BUFR decoder and a
pre-converted feed.

## 7. Readers

### 7.1 NetCDF

- **NetCDF-4** is HDF5 underneath. The repo's `IHdf5Group` abstraction
  is shaped for S-100 HDF5. It has no way to list datasets, read
  attributes on a dataset, read a dataset's shape, resolve
  `DIMENSION_LIST` references, or read a slice of a dataset. The
  NetCDF reader therefore uses **PureHDF directly**, inside
  `Datasets.NetCdf`, rather than widening `IHdf5Group` for one
  consumer.
- **Compression.** NetCDF-4 files usually use deflate plus the shuffle
  filter. PureHDF 2.x handles both. Files using other filters (zstd,
  blosc) fail with a clear message naming the filter.
- **NetCDF-3 (classic and 64-bit offset)** is a short, documented
  format: a header, then fixed-size and record variables. A managed
  reader is about 400 lines. It's needed because ERDDAP's `.nc` output
  is NetCDF-3.
- **CF interpretation:**
  - find latitude, longitude and time coordinates by `standard_name`
    or `axis`;
  - decode `time` units such as "hours since 2026-10-25 00:00:00";
  - apply packing;
  - pick variables by `standard_name` (§3.5).

  Non-CF files fail with a message listing the variables found.

As built (#1071):

- **Projects.** `EncDotNet.S100.Datasets.NetCdf` holds the format layer
  (`NetCdfFile`: `NetCdf3File`, a managed reader of the classic and 64-bit
  offset formats, and `NetCdf4File` on PureHDF) and the CF interpretation
  (`NetCdfWeatherDataset`, `CfTimeUnits`). `NetCdfDatasetProcessor` and
  `WeatherGridProcessorBase` are in `Datasets.Pipelines`, as §3.3 says: the
  base implements `IHeadlessImageRenderer` and `ILoadedDatasetProjection`,
  which live there, and `Datasets.Pipelines` already references every format
  project, so the GRIB2 processor reaches the base without referencing NetCDF.
  The registration is `WeatherProducts.NetCdf`, kept out of `S100Products`;
  `S100Products.CreateDefaultRegistry()` adds both.
- **NetCDF-4.** Dimensions are the root dimension scales; a variable's come
  from its `DIMENSION_LIST` references, or by length when it has none. Only
  the root group is read. PureHDF reads deflate, shuffle, Fletcher-32, N-bit
  and scale-offset; any other filter fails naming it (for example
  "Zstandard (id 32015)").
- **NetCDF-3.** About 400 lines, written from the Unidata classic format
  specification: fixed-size and record variables (including the unpadded
  single-record-variable case) and a streaming `numrecs`. CDF-5 fails.
- **CF.** Coordinates by `standard_name`, `axis` or units; time in seconds,
  minutes, hours or days in the standard, gregorian or proleptic_gregorian
  calendar; `_FillValue`, `missing_value` and valid range on the stored
  values, then `scale_factor`/`add_offset`, then conversion to the table's
  unit (feet, knots, km/h, hPa). Length-1 vertical dimensions are allowed, and
  a 10 m height is the wind level. A variable with several levels is listed as
  ignored. 2-D latitude/longitude and a `grid_mapping` other than
  `latitude_longitude` throw `UnsupportedWeatherGridException` naming the grid
  type; uneven spacing does too.
- **Model and run.** The model is the global `source`, `institution` or
  `title`; the run is a `forecast_reference_time` variable or global
  attribute. A file without one is reported as analysis steps with no run time.
- **Detection.** `MatchBytes` claims `CDF\x01`/`CDF\x02`, and an unclaimed HDF5
  file named `.nc`/`.nc4` (so the reader can say what it lacks); `MatchHdf5`
  claims HDF5 with a CF `Conventions` attribute and no S-100
  `productSpecification`.
- **Drawing.** Only the styles on by default (wave height fill, wind barbs)
  are drawn: a coverage sub-layer has no "hidden by default" flag yet, so the
  wind speed fill and wave arrows wait for a host toggle (open question
  §10.2).
- **Fixtures** (§7.3). Generated by a test helper (`tests/_shared/NetCdfFixtures.cs`):
  a Copernicus-style NetCDF-4 wave file with real `DIMENSION_LIST`
  references (PureHDF writes them), a 10 m wind file, and an ERDDAP-style
  NetCDF-3 file from a test-only classic-format writer. The ERDDAP subset
  isn't committed yet: the build environment couldn't reach NOAA's servers;
  `tools/weather-fixtures/fetch-erddap-fixture.sh` fetches one.

### 7.2 GRIB2

**Decision:** write a managed GRIB2 decoder scoped to what the target
sources use.

| Section / template | Needed for |
|---|---|
| Sections 0–8 framing, multiple messages per file, repeated sections 2–7 | All |
| Grid template 3.0 (regular lat/lon) | GFS 0.25°, GFS-Wave global, ECMWF, DWD |
| Product templates 4.0 (analysis/forecast) and 4.8 (time-interval statistics) | Instantaneous fields; gusts and maxima |
| Data templates 5.0 (simple), 5.2 (complex), 5.3 (complex with spatial differencing) | GFS and GFS-Wave from NOMADS; ECMWF |
| Data template 5.41 (PNG) | Some regional products; uses `System.IO.Compression` plus a small PNG reader |
| Bitmap section 6 | Wave grids (land cells masked) |

Not supported, with a clear error:

- **5.40 (JPEG 2000).** It needs a JPEG 2000 codestream decoder. No
  maintained MIT-compatible managed one has been confirmed. If a target
  source turns out to use it, choose then between a dependency and
  asking users to fetch the data in another packing. (GFS-Wave does: see
  §10.6 and the as-built note for #1098 below. 5.40 is now supported.)
- **5.42 (CCSDS/AEC).**

Check which data templates NOMADS, AWS and ECMWF Open Data actually
serve for the chosen parameters before starting work item 10. Record the
results in this note.

Rejected:

- **ecCodes through native interop.** It's complete and well tested,
  but needs a native build per platform, adds a large dependency for
  the headless renderer, and makes the library packages
  platform-specific.
- **GDAL.** Same native problem, plus its size.
- **Existing managed GRIB libraries.** Evaluate them in work item 10 for
  licence, GRIB2 template coverage and maintenance. Use one only if it
  covers the table above under an MIT-compatible licence and is
  maintained. Otherwise, own the decoder. Its scope is small and well
  specified.

**As built (#1064).** `EncDotNet.S100.Datasets.Grib` is the managed
decoder, written from the WMO Manual on Codes (FM 92 GRIB edition 2) and
the PNG specification. It covers the table above. Details are in its
[README](../../src/EncDotNet.S100.Datasets.Grib/README.md).

Templates the sources serve, checked on 26 October 2026 against the
2026-10-25 00Z (and 24th 12Z) runs. Each message was read with an HTTP byte
range from the source's index and described with ecCodes 2.50.0
(`dataRepresentationTemplateNumber`, `productDefinitionTemplateNumber`,
`bitmapPresent` and so on):

| Source | Parameters checked | Grid | Product | Data | Bitmap |
|---|---|---|---|---|---|
| AWS `noaa-gfs-bdp-pds`, GFS `pgrb2.0p25`, `0p50`, `1p00` | UGRD/VGRD 10 m, GUST, PRMSL (f000, f003) | 3.0 | 4.0 (4.8 for TMAX, APCP) | **5.3**, order 2, no missing value management | No |
| AWS `noaa-gfs-bdp-pds`, GFS-Wave `global.0p25`, `global.0p16`, `atlocn`, `epacif`, `wcoast`, `gsouth` | HTSGW, PERPW, DIRPW, WVHGT, SWELL, WVDIR, WIND, UGRD | 3.0 | 4.0 | **5.40 (JPEG 2000)** | Yes |
| AWS `noaa-gfs-bdp-pds`, GFS-Wave `arctic.9km` | as above | **3.20** (polar stereographic) | 4.0 | 5.40 | Yes |
| ECMWF Open Data (the `ecmwf-forecasts` AWS mirror of `data.ecmwf.int`), IFS `0p25` `oper` and `wave` | 10u, 10v, msl, 10fg; swh, mwd, mwp, pp1d, mp2 | 3.0 | 4.0 (4.8 for 10fg) | **5.42 (CCSDS)** | Yes for wave |
| NOMADS GRIB filter (`filter_gfs_0p25.pl`, `filter_gfswave.pl`) | — | — | — | Not checked: `nomads.ncep.noaa.gov` was blocked from the build environment | — |

So only GFS atmosphere is decodable as served. GFS-Wave, the main wave
source, is JPEG 2000 on AWS, and ECMWF is CCSDS throughout. The NOMADS filter
cuts its subsets with `wgrib2`, which by default keeps the input's packing,
so GFS-Wave from the filter is probably JPEG 2000 as well: check this before
building #1074 or #1075 on it. The unsupported templates fail with a message
naming them (`5.40 (JPEG 2000)`, `5.42 (CCSDS/AEC)`), and the field is still
listed. Adding them is open question §10.6.

Existing managed libraries, checked on NuGet and GitHub:

| Library | Licence | Coverage | Maintenance | Verdict |
|---|---|---|---|---|
| NGrib (`nmangue/NGrib`) | LGPL-3.0-or-later | GRIB1 and GRIB2, JPEG 2000 through CSJ2K | 0.12.0, December 2024 | Rejected: LGPL, and it depends on the native `Libaec` package for CCSDS |
| GribSharp (`insanebytes/GribSharp`) | MIT (JPEG 2000 add-on uses CSJ2K, BSD-2-Clause) | Simple, complex and spatially differenced packing, IEEE float; JPEG 2000 as an add-on; no PNG; grid and product coverage not documented | First release June 2026, 1.0.16 in August 2026; 14 commits, 1 star | Not used: no 5.41, parses whole files rather than indexing, and too new to call maintained. Worth watching for JPEG 2000 |
| GribApi.NET (`Grib.Api`) | Apache-2.0 | ecCodes wrapper | Last release 2017 | Rejected: native, unmaintained |

No ecCodes or `wgrib2` code was copied. ecCodes was used only as the
reference tool for the fixtures.

Decisions:

- **Datasets.Grib references Datasets.Weather** (as §3.3 has it), rather than
  standing alone behind an adapter. `GribField` carries the
  `GribParameterCode`, and `ToWeatherNativeGrid`, `ToWeatherLevel` and
  `FindWeatherParameter` map onto the model directly.
  `GribFile.CreateStepDecoder` returns a `WeatherStepDecoder`. The cost is
  that the decoder package pulls in Core and Portrayals through Weather.
- **One index entry per field, not per message.** A message that repeats
  sections 2 to 7 gives several fields. Every NOAA and ECMWF file has one
  field per message, so in practice the two are the same.
- **Unsupported templates don't stop the index.** Only GRIB1 (or another
  edition) fails at open, because its framing can't be walked. A field with
  an unsupported grid, product or data template is listed with
  `UnsupportedReason` set, and fails only when it's decoded. So a mixed file
  still opens, and `info` can show what it holds.
- **Values come back row-major in the grid's own directions.** Scanning
  mode bits 1 and 2 become the signs of the `WeatherNativeGrid` increments.
  Bits 3 and 4 (column-major and boustrophedon) are undone by the decoder.
  Staggered grids (bits 5 to 8) are unsupported.
- **Missing values are NaN**, whether the bitmap marks them or they're
  embedded by complex packing's missing value management.
- **PNG depth.** g2lib writes the image depth in template 5.41, but ecCodes
  writes the field's bit count (for example 11) and stores a 16-bit image.
  So the decoder reads pixels at the image's depth.

For #1074 (the processor):

- ECMWF's mean sea level pressure is `msl`, (0, 3, 0) at fixed surface 101,
  but the parameter table maps `pressure.msl` to (0, 3, 1) (NCEP's PRMSL).
  Add (0, 3, 0) at mean sea level to the table.
- GFS-Wave also carries the model's input wind as UGRD/VGRD/WIND/WDIR at the
  *surface* (fixed surface type 1). `FindWeatherParameter` checks the level,
  so these don't map to the 10 m `wind.*` rows. Decide whether they should.
- NCEP swell partitions (SWELL, SWPER, SWDIR) use the local fixed surface
  type 241 ("1 in sequence", "2 in sequence"…). They map to the table by
  parameter code, at `WeatherLevel.Other`. Keep the first partition, or
  list them all.
- GFS-Wave WVDIR is (10, 0, 4), the wind-wave direction, which isn't in the
  table. The primary wave direction DIRPW (10, 0, 10) is.
- ECMWF's `10fg` gust is a 4.8 maximum. Its `ValidTime` is the end of the
  interval.
- The model name for `WeatherOrigin` isn't in the file. Only the centre
  (`GribCentres`) and the generating process number are, for example NCEP
  96 for GFS. Take it from the registration or the file name.
- Keep the `GribFile` open for the life of the `WeatherGridSet`: the step
  decoders read from it.

**As built (#1098).** Template 5.40 is supported, through a managed JPEG
2000 Part 1 codestream decoder inside `EncDotNet.S100.Datasets.Grib`
(`Internal/Jpeg2000`, internal: nothing else needs it, and keeping it
internal leaves its API free to change). It was written from ITU-T T.800 |
ISO/IEC 15444-1; no OpenJPEG, JasPer or JJ2000 code was used. §10.6 records
why.

What the sources use, from the COD, QCD and SIZ markers of every message in
the GFS-Wave West Coast, Atlantic, East Pacific, South Global and global
0.25° f003 files of the 2026-10-25 00Z run (91 messages): a raw codestream
(no JP2 box) written by JasPer 2.0.25; one component of 7 to 16 bits,
unsigned; the packed values (the points the bitmap keeps) as one row, so a
global field is a 565,732 × 1 image; one tile, no offsets; the 5/3
reversible transform with 5 levels; 64 × 64 code-blocks (so 64 × 1 here)
with no style flags; LRCP order, one layer, default precincts, no SOP or
EPH; QCD with no quantisation and 2 guard bits, a QCC repeating it, and a
COM. Template 5.40 says lossless (type of compression 0, ratio missing).
ecCodes 2.50.0, which encodes with OpenJPEG 2.5.2, writes an `Ni × Nj`
image when there's no bitmap, and its "lossy" mode is the 5/3 transform cut
short by rate control rather than the 9/7.

The decoder covers more than that, as T.800 allows for one component:

- A raw codestream, or the codestream box of a JP2 file.
- One component of 1 to 31 bits, unsigned or signed, with any subsampling.
- Image and tile offsets, any number of tiles, and tile-parts in any order.
- SIZ, COD, COC, QCD, QCC (main and tile-part headers, with T.800's
  precedence), SOT, SOD and EOC; TLM, PLM, PLT, CRG and COM skipped; SOP and
  EPH read.
- The five progression orders, any number of quality layers, default and
  custom precincts.
- Tier-1 with every code-block style: bypass, reset, terminate on each pass,
  vertically causal, predictable termination, segmentation symbols.
- The 5/3 and 9/7 transforms with 0 to 32 levels, for any size and offset;
  no quantisation, and derived and expounded scalar quantisation; midpoint
  reconstruction of truncated code-blocks.

It rejects, with `UnsupportedGribException` naming the feature: several
components (and so colour transforms), regions of interest (RGN),
progression order changes (POC), packed packet headers (PPM, PPT), Part 2
(Rsiz bit 15, or wavelet kernels other than 5/3 and 9/7), HTJ2K (Rsiz bit
14, a CAP marker, or HT code-blocks), samples over 31 bits, and code-blocks
with more than 30 coded bit-planes. None appears in the sources. POC and
packed headers could be added, but no encoder at hand writes them to test
against.

Decisions:

- **A 0-bit field is `R`, unscaled.** ecCodes writes a constant field as
  `R` = the value, leaving `D` as the field had it, and reads it back as
  `R`. The decoder now does the same for 5.0, 5.40 and 5.41, where it used
  to apply `10^−D`. Section 7 may be empty.
- **The 9/7 transform follows T.800's 1 / K.** OpenJPEG uses
  1.625732422 / 2 for the high-pass scaling, 3.3 × 10⁻⁵ off; at 16 bits its
  lossy output differs from T.800's by up to 2. No GRIB2 source uses 9/7.
- **Performance.** On a 4-core build machine (Release, .NET 10, one
  thread), the global 0.25° significant height decodes in about 85 ms and
  the slowest global wave field in about 210 ms, against 70 and 160 ms for
  OpenJPEG 2.5.4. Tier-1 keeps each sample's state in one `int` with its
  neighbours' significance, and allocates per code-block, not per sample.

**As built (#1074).** `GribWeatherDataset` (in `Datasets.Grib`, beside the
decoder, as §3.3 has it and as `NetCdfWeatherDataset` sits in its reader)
reads a whole file into a `WeatherGridSet`. `GribDatasetProcessor` (in
`Datasets.Pipelines`, on `WeatherGridProcessorBase`) only opens the file, and
`WeatherProducts.Grib2` registers it.

- **Files.** One file of many messages, or several appended (a NOMADS GRIB
  filter request for several hours returns them so). Fields are grouped by
  (code, level); each group's steps are ordered by valid time, so the message
  order doesn't matter. A field is decoded only when its step is drawn or
  sampled, through `GribFile.CreateStepDecoder`, and the `GribFile` stays
  open until the processor is disposed.
- **Levels and codes.** Each table group is read at one level, so a drawn group
  never mixes levels: the row's own level (10 m wind, mean sea level pressure);
  for the wind, the surface when the file has no 10 m wind; for rows without a
  level, a proper level before NCEP's partitions, and partition 1 first. Within
  that level, the most preferred code the file has. Everything else (another
  level or partition, a less preferred code, a code the table doesn't have)
  stays in the set as an unknown parameter, `grib2:d,c,n`, with why in
  `UnusedReasons`, and isn't drawn or sampled.
  - *GFS-Wave's surface wind* (UGRD, VGRD, WIND, WDIR at fixed surface 1) is
    accepted as the wind when there's no 10 m wind. It is the GFS 10 m wind
    that forces WAVEWATCH III, labelled as at the surface; refusing it would
    leave a GFS-Wave download without wind. The level stays "surface" in the
    model and the pick. With a 10 m wind in the same file (GFS appended), the
    10 m wind wins and the surface fields are listed.
  - *Swell partitions* (local fixed surface 241, "n in sequence"): partition 1
    is `wave.swell.hs`. WAVEWATCH III orders its swell partitions by energy,
    so the first is normally the dominant swell (in the fixture, partition 1
    averages 1.25 m against 0.32 m and 0.12 m); 2 and 3 are listed. The level
    reads "GRIB2 fixed surface type 241 = 1".
  - *ECMWF*: `msl` is (0,3,0) at mean sea level, now the second code of
    `pressure.msl` (the row's level keeps surface pressure, also (0,3,0), out);
    `10u`/`10v` are the 10 m wind; `swh`, `mwd` (10,0,14), `pp1d` (10,0,34)
    and `mwp` (10,0,15) are at mean sea level, which the wave rows accept, and
    `pp1d` is the period (`mwp` is listed). The `10fg` gust is a 4.8 maximum
    whose valid time is the end of its interval, the same rule as GFS's 4.8
    fields, so it lines up with the instantaneous fields of that step.
  - *WVDIR* (10,0,4) is a new row, `wave.wind_sea.dir`, in the `wave.wind_sea`
    group beside the wind-sea height, following the table's id scheme. It's
    sampled and picked; no style draws it yet.
- **Grid.** The grid of the first readable field the table knows; fields on
  another grid are listed. Scanning-mode row flips and 0 to 360 longitudes are
  `WeatherGridLayout`'s (a global 0 to 360 grid is rotated to −180 to 180; a
  regional 350°E to 10°E grid becomes −10° to 10°). Bitmapped points are NaN.
- **Several reference times.** All analyses (each field valid at its reference
  time, for example appended f000 files of several runs): an analysis series,
  with no run time (`TimeStepKind.Analysis`). Otherwise the latest run is used
  and the older runs' fields are listed. Rejecting the file would refuse a
  common mistake for no gain; merging runs would show steps of different runs
  as one forecast.
- **Left out, with why** (`IgnoredFields`, and `Contents` on the processor,
  which `s100 info` prints): unsupported templates (CCSDS, Lambert),
  another grid, an older run, a repeat of a parameter, level and valid time. A
  file with nothing decodable fails naming the template, prefixed with the
  file name.
- **Model name** (`ForecastRun`, the layer name): NCEP's generating process
  (81, 82 and 96 are the GFS, 11 is GFS-Wave), which holds whatever the file
  is called; else the file name as the sources name files (`gfs`, `gfswave`
  tokens, so `gfs.t00z…` and `filter_gfs_0p25.pl`; ECMWF's `…-oper-fc` or
  `…-scda-fc` is "ECMWF IFS", `…-wave-fc` or `…-scwv-fc` "ECMWF WAM", `aifs`
  "ECMWF AIFS"); else the centre ("ECMWF", "GRIB2 centre 74"). ECMWF's process
  number changes with each IFS cycle, so it isn't used. Several models in one
  file are joined: "GFS-Wave + GFS".
- **Detection.** `MatchBytes` claims a GRIB edition 2 indicator at the start
  of any file, whatever the extension: NCEP's files have none, the filter's
  download is named after the script, and others use `.bin`. A `.grb`,
  `.grb2`, `.grib2` or `.grib` file is also claimed with `GRIB` anywhere in its
  first kilobyte, so a WMO bulletin header is skipped and a GRIB1 file reaches
  the reader's "edition 1 isn't supported". No S-100 encoding starts with
  `GRIB`, and the envelope matchers (ISO 8211, HDF5, GML) run first, so S-100
  and NetCDF files are never claimed; tests check both ways.

### 7.3 Test fixtures

- **GRIB2:** a NOMADS GRIB filter subset of GFS-Wave and of GFS 10 m
  wind. Off the US East Coast, 3 forecast hours, 0.25°. Public domain.
  Also hand-built messages for each data template, written by a
  test-only encoder, so every template has a byte-exact test.
- **NetCDF-4:** a generated CF file, written with PureHDF in a test
  helper, that matches the Copernicus wave variable names. Real
  Copernicus data needs attribution and an account, so don't commit it.
- **NetCDF-3:** a generated file plus one small ERDDAP subset (NOAA
  dataset, public domain).
- **CAP:** two or three archived NWS marine alerts, plus a hand-written
  one using `<circle>`.
- **NDBC:** a captured `realtime2/` file per station type.

The scenario generator (#940) can later add co-located weather to its
fictitious area, for example wind over the S-111 currents.

**As built (#1064).** NOMADS was blocked from the build environment, so the
GRIB2 fixtures come from the same runs on AWS (`noaa-gfs-bdp-pds`, public
domain): GFS 10 m wind and GFS-Wave height, period and direction, off the US
East Coast (40°N to 32°N, 76°W to 68°W, 0.25°), at f000, f003 and f006.
They're cropped and repacked by ecCodes in each supported template (GFS-Wave
because it's served as JPEG 2000). They sit beside one GFS 1° message and one
GFS-Wave JPEG 2000 message, both exactly as NCEP serves them. The total is
about 115 KB. Their README in `tests/datasets/GRIB2` gives the exact subsets,
and `tools/grib-fixtures/make_grib_fixtures.py` rebuilds them and prints the
ecCodes reference values the tests hardcode. Every data template also has
byte-exact round-trip tests through a test-only encoder.

**As built (#1098).** The single GFS-Wave JPEG 2000 message became four,
still exactly as NCEP serves them: West Coast significant height, peak
period and primary direction, and Atlantic significant height. A fifth file
holds GFS and GFS-Wave crops repacked by ecCodes as 5.40 (lossless, lossy,
with a bitmap, and a constant field). The JPEG 2000 decoder also has 51
codestreams written by OpenJPEG through Pillow, imagecodecs and glymur
(`tools/grib-fixtures/make_jpeg2000_fixtures.py`), with their expected
samples. Together they add about 245 KB. The global 0.25° messages (300 to
830 KB each) were only decoded locally, against ecCodes and for timing.

**As built (#1074).** One fixture was added, `gfswave-useast-filter.grib2`
(41.5 KB): what a GRIB filter request for three hours of GFS-Wave returns,
the hours appended (WIND, WDIR, UGRD, VGRD, HTSGW, PERPW, DIRPW, WVHGT, the
three SWELL partitions and WVDIR at f000, f003 and f006), from AWS and
repacked like the others (the filter would keep the JPEG 2000 packing).
The unsupported-template case is a generated CCSDS message. The S-101 test
cells are synthetic (62°E, 32°S), where no NOAA grid reaches, so the golden
over an S-101 cell draws generated GRIB2 messages, written by a small test
helper (`tests/_shared/GribFixtures.cs`) that also writes a global 0 to 360
grid and ECMWF's codes. ECMWF Open Data is CC BY 4.0 and CCSDS-packed, so no
ECMWF bytes are committed.

## 8. Pipeline refactors for products without catalogues

The packages are preview and have no outside consumers yet, so these
refactors don't keep the old APIs.

### 8.1 What already works without a catalogue

The processor contract doesn't depend on catalogues. A processor
returns sub-layers through `IVectorPortrayalSource` or
`ICoveragePortrayalSource`:

- Vector sub-layers carry `DrawingInstruction`s plus symbol, line-style
  and area-fill providers.
- Coverage sub-layers carry a `StyledCoverageLayer`, glyphs or drawing
  instructions.

Nothing downstream asks where the instructions came from. S-104 already
proves this: S-104 Ed 2.0.0 defines no symbology, and
`S104PortrayalCatalogue` builds its gauges, isolines, palettes and SVG
symbols in code.

The coupling is elsewhere. Product traits live in `switch`es on spec
names spread across the host. A new product has to be added to each
one, and the defaults for an unknown name are wrong for weather:

| Where | What it switches on | What an unknown product gets |
|---|---|---|
| `MapPresentationState.CreateRenderContext`, `FacadeRenderContextBuilder` | Which `RenderContext` subclass to build, and whether it carries a time step | `S101RenderContext` with **no time step**, so a time-aware weather processor never sees the selected time |
| `DefaultDisplayPlaneAuthority` | Default S-98 plane per product and layer kind | A catch-all plane |
| `CachingDatasetMetadataReader` | Which reader extracts Library metadata | Nothing: the Library can't index the file |
| `LoadedDatasetProjector` | Which typed data model to open | Nothing |
| `SampleCoverageService` | How to sample a coverage (`sample_coverage`) | "Not supported" |
| `MapsuiDatasetLayerSession` time policy | At-or-before versus nearest, and how long a step stays valid | Nearest, with the typical step as the tolerance |
| `TimelineViewModel`, `ForecastRunNames` | Whether a dataset is a forecast run | S-111 only |
| `SpecConventions.ApplicationPortrayalFor` | The "SoundCharts portrayal" note in `list_specs` | S-104 only |
| `SpecConventions.PortrayalSpecFor` | Which spec's portrayal settings apply | S-57 maps to S-101; everything else to itself |

Adding GRIB2, NetCDF and CAP would touch every row. The refactors below
move each trait to the product's registration or to a processor
capability, so a new product is one registration plus one processor.

### 8.2 Product traits on the registration

Rename `S100ProductRegistration` to `ProductRegistration`, and
`S100ProductRegistry` to `ProductRegistry`. Add the traits the switches
encode today:

```csharp
public sealed record ProductRegistration
{
    public required string Spec { get; init; }
    public required DatasetProcessorFromPath CreateFromPath { get; init; }
    public required DatasetProcessorFromSource CreateFromSource { get; init; }

    // New
    public bool IsS100 { get; init; } = true;
    public PortrayalBasis Portrayal { get; init; } = PortrayalBasis.Catalogue;
    public string? PortrayalSpec { get; init; }          // "S-101" for S-57; null = Spec
    public Func<string, DatasetMetadata?>? ReadMetadata { get; init; }
    public Func<Stream, LoadedDatasetData>? OpenData { get; init; }
    public IReadOnlyDictionary<string, S98DisplayPlane> DefaultPlanes { get; init; }
    // existing matchers (MatchIso8211, MatchGml) stay
}

public abstract record PortrayalBasis
{
    public static PortrayalBasis Catalogue { get; }             // an S-100 portrayal catalogue
    public static PortrayalBasis Application(string basis);     // drawn in code, e.g. "S-104 Ed 2.0.0"
}
```

What changes:

- `DatasetPipelineFactory` detection asks each registration to claim
  the file, extending the existing `MatchIso8211` / `MatchGml` hooks
  with a `MatchBytes` hook (leading bytes plus extension). The fixed
  extension `if` chain and the S-102 fallback for unknown HDF5 go away.
- `CachingDatasetMetadataReader`, `LoadedDatasetProjector`,
  `DefaultDisplayPlaneAuthority`, `SpecConventions.PortrayalSpecFor` and
  `SpecConventions.ApplicationPortrayalFor` read from the registry.
- `list_specs`, the layer-stack badge (§3.1) and pick reports read
  `IsS100` and `Portrayal` from the registry.

The sub-layers already carry their own `Plane`. `DefaultPlanes` is only
the fallback for callers that ask the authority without a sub-layer in
hand.

As built (#1061):

- The HDF5 products claim their files through a fourth hook,
  `MatchHdf5`, which sees the root `productSpecification` and can read
  other root attributes (the NetCDF-4 `Conventions` check above).
- `OpenData` is a `DatasetDataOpener(Stream, externalTextResolver)`; a
  product with no built-in payload returns a `ProductDatasetData`
  carrying its own `DatasetMetadata`. `PickReportNotice` overrides the
  viewer's default not-for-navigation line.
- An unclaimed `.000` file is still S-101; every other unclaimed file
  is unknown. Only the HDF5 signature is sniffed for an unknown
  extension; ISO 8211 update files and XML catalogues are not.
- `SpecConventions` is gone. `DefaultDisplayPlaneAuthority` stays in
  `EncDotNet.S100.Core` over an `IProductPlaneTable` that
  `ProductRegistry` implements; hosts and the static conveniences read
  `ProductRegistry.Default`.

### 8.3 One render context

Replace the eleven `RenderContext` subclasses (`S101RenderContext`,
`S104RenderContext(TimeStep)` and so on) with one sealed
`RenderContext` record that has a `TimeStep` property. The subclasses
carry nothing except the time step on three of them.

- `MapPresentationState.CreateRenderContext` and
  `FacadeRenderContextBuilder` lose their switches.
- Processors read `context.TimeStep` instead of pattern-matching their
  own subclass (five call sites).
- A product-specific setting, if one ever appears, goes in a
  product-keyed bag on the context, not a new subclass.

This is the refactor weather can't do without. Without it, every new
time-aware product has to add a subclass and edit two switches.

### 8.4 Time traits on the processor

Add to `ITimeAwareDatasetProcessor`:

- `TimeSemantics`: whether a step is a forecast, an analysis or an
  observation, plus how long a step stays valid when the map time is
  between steps. This replaces the time-policy switch in
  `MapsuiDatasetLayerSession` (S-104 and S-411 hold until the next
  step; others snap to the nearest).
- `ForecastRun`: the model run's identity and reference time, or null.
  This replaces the S-111 checks in `TimelineViewModel` and
  `ForecastRunNames`. GRIB2 and NetCDF processors fill it from the
  reference time; S-104 and S-111 from `issueTime` and the producer.

### 8.5 Coverage styles separate from catalogues

`CoveragePipeline.ProcessAsync` takes an `ICoveragePortrayalCatalogue`.
That interface inherits `IPortrayalCatalogue`, which needs a `Spec` and
an `Edition` string. A code-defined style has neither in any meaningful
sense.

- Rename `ICoveragePortrayalCatalogue` to `ICoverageStyle`, holding the
  colour scheme, the symbol scheme, the contours, the active palette
  and palette switching.
- Drop the `IPortrayalCatalogue` base from it.
- The S-102 and S-111 catalogue classes implement `ICoverageStyle`
  directly. S-104's code-defined catalogue becomes `S104CoverageStyle`.
- `IPortrayalCatalogue` stays for the real, file-backed catalogues.

### 8.6 Coverage style model

Small generalisations the weather styles need. Each one stands on its
own:

- **Contours by value.** `ContourStyle` takes a `double Value` and a
  unit string instead of a `Depth`. S-102 depth contours convert where
  they're built.
- **Symbol choice with position.** `CoverageSymbolScheme.Resolve`
  takes the cell's latitude as well as its value, so a scheme can pick
  southern-hemisphere barbs. S-111 ignores it.
- **Rotation convention.** `CoverageSymbolScheme` gets a
  `RotationOffsetDegrees` (default 0). A "from" field with an
  arrow-shaped symbol uses 180. This avoids a derived "toward"
  field.
- **Derived fields at sampling time.** `SampledCoverage` gains optional
  computed fields, so wind speed and direction can be derived from U
  and V inside the coverage source, without a second full-grid copy.

### 8.7 A shared toolkit for code-defined portrayal

S-104 already holds a hand-rolled version of what weather and CAP will
need:

- palette tokens per day, dusk and night (`S104Ink`, `GetColorPalette`);
- SVG symbols kept as strings and served through a symbol provider;
- drawing instructions built in code for station gauges and labels.

Extract these into `EncDotNet.S100.Portrayals.Application` (or a folder
in `Portrayals`):

- `ApplicationPalette`: named colour tokens with day, dusk and night
  values, producing a `ColorPalette`.
- `SymbolSet`: SVG symbols as embedded resources, with a `Func<string,
  string?>` provider.
- `InstructionBuilder`: point symbols, text, line and area instructions
  with viewing group, priority, plane and scale minimum, producing
  `DrawingInstruction`s.
- **A content scope** for the tile disk cache. A code-defined portrayal
  has no catalogue files to hash, so its scope is the toolkit
  assembly's version plus a style version constant. This relates to
  #1014.

S-104 moves onto the toolkit first, with no visual change, which
proves it. Then weather and CAP use it.

### 8.8 Coverage sampling as a capability

Add an `ICoverageSampler` capability to processors: sample at a point
and time, and along a polyline, with the windowing that
`SampleCoverageService` does today for S-104 and S-111. S-102, S-104
and S-111 implement it. `SampleCoverageService` dispatches on the
capability, not the spec name. Weather processors then answer
`sample_coverage` and `sample_coverage_along` with no MCP changes. That
matters for route checks later (§10.5).

As built (#1063): `ICoverageSampler.FindSites` returns the grid node or
stations that can answer a point, and `CoverageSampling` (both in
`EncDotNet.S100.Core`) does the choosing and the time handling for every
product. A weather processor returns `CoverageFieldsSample`s, whose
`SampledField`s carry each value's quantity and unit, and reaches the catalog
as a `ProductDatasetData` that implements `ICoverageSampler`.

### 8.9 Left as is

- **`PickService` station time-series views** (S-104, S-111). These are
  viewer UI for station data. Weather has no stations in a grid, and
  NDBC observations go through dynamic sources.
- **`DatasetProcessorServices`** still hands every processor the
  catalogue managers and Lua engine. Weather processors ignore them.
  Making them optional isn't worth the churn.

## 9. Work breakdown

Each item is one PR-sized sub-issue of #1055. The dependencies are
listed after each. Items 1 to 6 are the pipeline refactors from §8.
They're useful without weather, and they make each weather item
smaller.

**Pipeline refactors**

1. (#1060) **One render context** (§8.3). Depends on: nothing.
2. (#1061) **Product traits on the registration** (§8.2), including byte-based
   detection and the non-S-100 names from §3.1. Depends on: nothing.
3. (#1065) **Time traits on the processor** (§8.4). Depends on: 1.
4. (#1062) **Coverage styles separate from catalogues** (§8.5), plus the style
   model changes (§8.6). Depends on: nothing.
5. (#1066) **Shared toolkit for code-defined portrayal** (§8.7), with S-104
   moved onto it. Depends on: 4.
6. (#1063) **Coverage sampling as a capability** (§8.8). Depends on: nothing.

**Weather**

7. (#1067) **Weather grid model and coverage source.** `Datasets.Weather`
   containing:
   - `WeatherGridSet`
   - the parameter table
   - normalisation (row flip, longitude shift, `NaN`)
   - U/V derivation
   - `WeatherCoverageSource : ICoverageSource` with time steps,
     lazy step decoding and overview levels

   Tested with grids built in memory. Depends on: 4 for derived fields.
8. (#1068) **Weather coverage styles.** Wave-height and wind-speed colour bands
   and palettes, barb and wave-arrow SVGs, and the layer order from §5.
   Tested through the headless renderer with in-memory grids. Depends
   on: 5, 7.
9. (#1071) **NetCDF reader and processor.** NetCDF-4 on PureHDF, NetCDF-3, CF
   interpretation, and `NetCdfDatasetProcessor` with time steps,
   sampling, picking and headless rendering. Depends on: 1, 2, 3, 6, 8.
10. (#1064) **GRIB2 decoder.** Sections, templates 3.0, 4.0, 4.8, 5.0, 5.2,
    5.3, 5.41 and the bitmap, plus the test encoder. Start by
    confirming the templates the target sources use (§7.2). Depends
    on: nothing.
11. (#1074) **GRIB2 processor.** The adapter from GRIB2 to the weather grid
    model, plus `GribDatasetProcessor`, the NOMADS fixtures and
    multi-message files. Depends on: 9 (shared processor base), 10.
12. (#1072) **Drawing polish.** Southern-hemisphere barbs, legends in the
    viewer, land clipping for wave fills, and pressure isobars.
    Depends on: 8.
13. (#1069) **CAP warnings.** `CapDatasetProcessor`, the marine-zone layer, the
    in-force logic shared with S-124, and the style built with the
    toolkit. Depends on: 2, 5.
14. (#1070) **NDBC observations source.** Depends on: 5 for the station glyph.
15. (#1073) **NMEA weather source.** Depends on: 14 for the symbol.
16. (#1075) **Library remote sources.** NOMADS GRIB filter, AWS `.idx` reads,
    `api.weather.gov` and ERDDAP entries, runs and staleness, and
    folder-as-run loading. Depends on: 9, 11, 13.
17. (#1076) **Surfaces and docs.**
    - `s100 info`, `render` and `pick` on weather files
    - MCP: weather formats in `list_specs`, and checks that time steps
      and sampling work through the generic paths
    - a guide page under `docs/`
    - a landing-page capture
    - attribution credits in the site footer and the viewer's about box

    Depends on: 9 and 11 at least.

Items 1, 2, 4, 6 and 10 can start in parallel. The first end-to-end
result is item 9: a NetCDF wave file drawn over a chart.

## 10. Open questions

1. **Which variables are on by default?** Proposal: wave height fill
   and wind barbs on; wind speed fill, wave arrows and isobars off.

   **Decided (#1072):** the wave height fill, wind barbs and isobars are on;
   the wind speed fill and wave arrows are off, because they compete with
   the wave fill and the barbs. The three optional layers are set
   globally, for every weather dataset, in the viewer's Display controls
   (a Weather section shown while a weather dataset is loaded), like the
   S-124 Navigational warnings switch, and kept between sessions. The wave
   height fill and wind barbs have no switch; each dataset's sub-layer eye
   can still hide them.
2. **Where does a user choose variables?** Proposal: the layer stack
   shows one entry per drawn parameter group, so visibility toggles
   work as they do for S-101 sub-layers. No separate picker in v1.
3. **Antimeridian-crossing regional grids.** Split into two column
   blocks at read time, or teach `GridGeoreferencer` about wrapping?
   The first version rejects them. Revisit with the Pacific (Alaska, as for the
   S-411 P08 capture).
4. **Ensembles and probabilities.** GRIB product templates 4.1, 4.2,
   4.11 and 4.12. Out of scope for now.
5. **Weather in route checks.** Wind and wave along a route at ETA is
   the obvious next step, after this epic. The `ICoverageSource` design
   makes it possible without new readers.
6. **JPEG 2000 and CCSDS packing.** GFS-Wave on AWS uses 5.40 and ECMWF
   Open Data uses 5.42 (§7.2, as built), so without them the main wave
   source and all of ECMWF can't be read. CCSDS (CCSDS 121.0-B, the
   adaptive Rice coder) is small and well specified, so a managed decoder
   is a contained piece of work. JPEG 2000 is a large codec. Options are
   CSJ2K (managed, BSD-2-Clause, which GribSharp already uses) or asking
   users to repack (`wgrib2 -set_grib_type c3`). Decide before #1074 picks
   its default sources.

   **Decided for JPEG 2000 (#1098): our own managed decoder.** CSJ2K has had
   no commits for about eight years, and its JJ2000 origins carry their own
   licence; OpenJPEG and JasPer are native, which §2 rules out; asking users
   to repack would leave the main wave source unreadable as served. GRIB2
   needs only a single-component Part 1 decoder, which T.800 specifies
   fully, so the decoder is scoped to that and lives inside
   `EncDotNet.S100.Datasets.Grib` (§7.2, as built for #1098). CCSDS (5.42)
   is still open.

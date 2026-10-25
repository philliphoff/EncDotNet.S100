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
| `pressure.msl` | 0,3,1 | `air_pressure_at_mean_sea_level` | Pa |

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
| Wind barbs | `ArrowCoverageSubLayer` | `wind.speed` (kn) | `wind.from_direction` | WMO barbs. One `SymbolBand` per 5 kn step, 0–200 kn; calm below 2.5 kn. |
| Wind speed | `GridCoverageSubLayer` | `wind.speed` (kn) | — | Bands at Beaufort boundaries. Off by default. |
| Wave height | `GridCoverageSubLayer` | `wave.hs` (m) | — | Bands at 0.5, 1, 1.5, 2, 2.5, 3, 4, 5, 6, 7, 8, 9, 12, 14 m (the Douglas sea-state boundaries, made finer). On by default. |
| Wave direction | `ArrowCoverageSubLayer` | `wave.hs` | `wave.dir` (offset 180°) | One arrow symbol scaled by height (`SymbolBand.ScaleByValue`). |
| Pressure | `IsolineCoverageSubLayer` | `pressure.msl` (hPa) | — | 4 hPa isobars. Needs the contour generalisation below. |

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
  asking users to fetch the data in another packing.
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

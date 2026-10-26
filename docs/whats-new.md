# What's new

This page lists notable changes to the libraries, tools and documentation, by
month. For package versions and release assets, see
[GitHub releases](https://github.com/philliphoff/EncDotNet.S100/releases).

## October 2026

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

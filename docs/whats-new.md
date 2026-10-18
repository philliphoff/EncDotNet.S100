# What's new

This page lists notable changes to the libraries, tools and documentation, by
month. For package versions and release assets, see
[GitHub releases](https://github.com/philliphoff/EncDotNet.S100/releases).

## October 2026

- **A shorter findings list.** In SoundCharts' Check route, Check and Info
  findings with the same code now share one row with a count ("Charted depth
  from S-101 only ×9 · Legs 7 → 9 · 9 stretches") that expands into each
  finding; Must review findings are always listed one by one. INFO is folded
  until you open it, and the Legs tab marks each leg's route-check findings
  with a badge, with or without a draft. `get_findings` reports the filter,
  per-chip counts, merged rows (with every member id), the INFO fold and the
  expanded row; `set_findings_view` changes them.

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
  [Check a passage's clearance](mcp-server.md#check-a-passages-clearance).
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

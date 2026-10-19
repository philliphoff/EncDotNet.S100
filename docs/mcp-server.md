# MCP server

EncDotNet.S100 exposes its datasets to AI agents through a Model Context
Protocol (MCP) server. Agents such as `mcp-inspector`, Claude Desktop or an IDE
assistant can list datasets, query features, sample coverages and render
images.

Two hosts run the server:

| Host | Transport | Datasets | Tools |
|---|---|---|---|
| SoundCharts | Streamable HTTP on the loopback address | The datasets loaded in the app | All the tools on this page, except where marked |
| `s100 mcp serve` | Standard input and output | The datasets you name when you start it, plus any `open_dataset` adds | The [query tools](#query-tools) and [session tools](#session-tools) |

To host the tools in your own application, see the
[EncDotNet.S100.Mcp README](../src/EncDotNet.S100.Mcp/README.md).

## Turn on the server in SoundCharts

The server is off by default. When it's on, it listens on `127.0.0.1` only and
has no authentication; see [Security](#security).

1. Choose **Settings** (the gear icon in the activity bar), then
   **Integrations**.
2. Under **MCP Server**, turn on **Enable MCP server**.
3. Optionally set **Port**. Leave it at `0`, the default, to let the system pick
   a free port. **Reset to auto** forgets a saved port.

The status bar shows `MCP :<port> · <n> clients` while the server is
listening. Hover over it to see the endpoint URI to give your agent.

To stop the server, turn off **Enable MCP server**. The status bar indicator
disappears and the port is released.

If the port is already in use, SoundCharts shows **MCP server port
unavailable**, and the server stays off until you choose a free port. Choose
**Find another port**, or set the port to `0`.

### Start the server from the command line

For scripted runs, an agent can start SoundCharts with the server on, without
opening Settings and without changing the saved settings:

```bash
dotnet run --project src/EncDotNet.S100.Viewer -- --ephemeral --mcp --mcp-port-file /tmp/run/mcp.url path/to/dataset.h5
```

| Option | Description |
|---|---|
| `--mcp` | Starts the server for this run, whatever the saved setting is. |
| `--mcp-port <port>` | The port. `0`, the default, picks a free port. Implies `--mcp`. |
| `--mcp-bind <address>` | The address to listen on. Keep the loopback address unless you have a reason to change it; see [Security](#security). Implies `--mcp`. |
| `--mcp-port-file <path>` | Writes the endpoint URI to this file once the server is listening, so an agent can find a port the system picked. Implies `--mcp`. |
| `--mcp-test-hooks` | Also registers the [test tools](#test-tools). Implies `--mcp`. It's never saved, so turning on the server in Settings never adds these tools. |

SoundCharts also prints the endpoint to standard output as
`[MCP] listening on <uri>`.

A run started this way never saves the port to `settings.json`. To keep your
own settings untouched, and to let parallel runs avoid each other, add
`--ephemeral` (throwaway settings), `--settings <path>` (another settings file)
or `--data-dir <path>` (settings and all disk caches under one folder, the most
isolated option).

The **Automation / agent control** section of the
[viewer guide](../src/EncDotNet.S100.Viewer/README.md#automation--agent-control)
lists the other startup options, such as viewport, palette, time step and
logging, and walks through an agent session.

## Run the headless server

`s100 mcp serve` serves a fixed set of datasets over standard input and output,
with no app running. Configure your MCP client to start it, for example:

```json
{
  "command": "s100",
  "args": ["mcp", "serve", "path/to/exchange-set.zip"]
}
```

The exact shape of the configuration depends on your client. For the input
forms and options, see [`mcp serve`](cli.md#mcp-serve).

## Connect with mcp-inspector

1. Start the inspector:

   ```bash
   npx @modelcontextprotocol/inspector
   ```

2. Set the transport to **Streamable HTTP**.
3. Paste the endpoint from the SoundCharts status bar, for example
   `http://127.0.0.1:54321/`, and connect.

The inspector lists the tools described below. If no datasets show up, load
some in SoundCharts first: the server only sees what's loaded. If the agent
can't connect, check the endpoint again. With port `0`, the port changes each
time the server restarts.

## Field conventions

Every tool follows the same conventions in the JSON it returns, so agents don't
need to look up units, axes or casing field by field. A field that differs says
so in its description.

| Concern | Convention |
|---|---|
| Coordinate reference system | WGS-84 (EPSG:4326). |
| Coordinate values | Decimal degrees. Latitude `-90..+90`; longitude `-180..+180`. |
| Bounding boxes | Four labelled values, `southLatitude`, `westLongitude`, `northLatitude`, `eastLongitude`, never a bare pair. |
| Times | UTC, ISO 8601. Time intervals include both ends. |
| Distances | Metres. |
| Depths | Metres, positive down, as in S-102. |
| Water levels | Metres, positive up, as in S-104. |
| Current speeds | S-111 encodes `surfaceCurrentSpeed` in knots in every data coding format. Samples report both `speedKnots`, as encoded, and `speedMetresPerSecond` (`kn × 0.514444`). |
| Bearings | Degrees clockwise from true north, `0..360`. |
| Property names | Lower camelCase in every tool. |
| Discriminated unions | A `$kind` string names the variant, for example `"depth"`, `"water_level"`, `"water_level_station"`, `"surface_current"` or `"surface_current_station"`. |
| Errors | `isError = true` with the payload `{ code, message, details }`. `code` is the stable value to switch on, `message` is for people, and `details` carries the error's typed members. |
| Dataset identifiers | A `datasetId` is a plain string in both directions, for example `"id": "synth-warn-1"`, so an id from one tool's output goes straight into another tool's input. The older `{"value":"…"}` wrapper is still accepted as input. |

Every property of a tool request, result or error has a one-sentence
description of its unit, CRS and meaning.

Every `spec` argument accepts a string (`"S-101"` or `"S-124/1.5.0"`; the
edition is optional) or the object that tools return,
`{"name":…,"edition":{"major":…,"minor":…,"clarification":…}}`, so you can pass
a result's `spec` straight into the next call. In the object form `edition` is
optional, and an all-zero edition means any edition. Any other shape returns an
`invalid_argument` error that names `spec`.

## MCP tools

The **Changes state** column says whether a tool changes the host's state:
loaded datasets, the view, the time, routes, panels or the Library. Tools
marked "No" are safe to call at any time. None of the tools edit source
datasets or write arbitrary files.

### Query tools

Both hosts provide these tools.

| Tool | Changes state | What it does |
|---|---|---|
| `list_datasets` | No | Summarises every loaded dataset. Each entry's `spec` is the dataset's product: an S-57 cell reports `S-57`, with its real cell bounds, even though it's translated to and queried through the S-101 model. The vector query tools (`identify_features`, `query_features`, `count_features`, `search_features` and others) work on S-57 cells too. |
| `list_specs` | No | Lists the product specifications the host can read. A product whose specification defines no portrayal carries a `portrayalNote`: S-104 has `"SoundCharts portrayal: S-104 Ed 2.0.0 defines no symbology"`, because its station gauges and surface isolines are SoundCharts' own ([S-104 portrayal](../src/EncDotNet.S100.Datasets.S104/README.md#portrayal)). |
| `list_time_steps` | No | Lists the time steps of a time-varying dataset (S-104, S-111, S-421). |
| `find_at` | No | Returns every loaded dataset whose declared bounding box contains a point. It checks the bounding box only, not per-cell coverage or no-data masks. To find the features at a point, use `identify_features`. |
| `identify_features` | No | Identifies the vector features at a point, as an ECDIS cursor pick does, ranked most specific first: points before curves before areas, and within a primitive the smaller or nearer feature first. Areas must contain the point (holes count); points and curves match within `radiusMeters` (default 50). Works for every vector product, including S-101. `longitude` can be anywhere in `[-540, 540]`: data kept in a 0…360 or other continuous frame, such as the NWS Alaska S-411 (175°E to 225°E), matches at its own longitude and one world east or west (210 and −150 find the same feature). Each match reports the dataset id, spec, feature id and type, primitive, bounds, `containment` (`inside` or `near`) and approximate `distanceMeters`. `maxResults` (default 20) caps the list and sets `truncated`. For S-101 features with a `fileReference`, `TXTDSC` or `NTXTDS` attribute, each match also reports `referencedTexts`: the name and content of the text files from the dataset's exchange set. |
| `describe_feature` | No | Returns the spec, feature type, attributes and, for S-101, resolved geometry for a feature id in a dataset. Numeric attributes whose feature catalogue declares a unit, such as `depthRangeMinimumValue` or `valueOfSounding`, carry `unit` (for example `"m"`) and `unitName` (`"metre"`); the sounding `depths` in an S-101 `geometry` block carry `depthUnit` (`"metres"`). Supported: S-101, S-401 and S-57 cells (RCID; the result has a `geometry` block with primitive, bounding box and coordinates); S-102 (`BathymetryCoverage[.01]`; `boundingBox` is in WGS-84 degrees, while `gridMetadata` and `nativeExtent` stay in the grid's native CRS, such as metres for a UTM tile); S-104 and S-111 (`WaterLevel` or `SurfaceCurrent[.NN][.Group_KKK]`, or a bare station identifier); S-124 (`gml:id`); and S-129 (`gml:id` of a plan, plan area, control point or non-navigable area). For S-104 the result has the same `portrayalNote` as `list_specs`. |
| `describe_feature_type` | No | Describes a spec's bundled feature catalogue (ISO 19110, S-100 Part 5). With only `spec` (for example `S-101` or `S-124/1.5.0`), lists every feature type with its attribute count. Add `featureType` (code, name or alias) to get that type's attributes: value type, whether mandatory or repeatable, and listed values (`includeListedValues=false` omits large enumerations). Use it to build attribute filters without a loaded dataset. Casing and an edition suffix in `spec` are accepted, and the edition is ignored. A spec with no bundled catalogue returns `feature_catalogue_not_available`, whose `details.acceptedSpecs` lists the specs that have one. |
| `query_features` | No | Returns features whose geometry intersects a spatial query, from every GML vector product and from S-101 and S-401. Intersection uses bounding boxes by default; `precise=true` tests the full geometry, with point-in-polygon containment for areas (holes count) and real segment crossings, for questions such as "which features does this route leg cross?". For S-101, `featureType` matches the acronym (for example `LIGHTS` or `BOYLAT`) and each `featureId` is the record's decimal RCID. `attributes` filters on values: a code-to-value map for equality (`{"categoryOfLateralMark":"1"}`), or an array of predicates (`[{"attribute":"valueOfDepth","op":"ge","value":"10"},{"attribute":"objectName","op":"exists"}]`). Operators are `exists`, `notExists`, `eq`, `ne`, `contains`, `startsWith`, `gt`, `ge`, `lt` and `le`, combined with AND. The result's `typeBreakdown` counts every match by feature type, across all pages, so you can size a result before paging. |
| `count_features` | No | Lists the feature types in the loaded vector datasets and how many features of each there are, to answer "what's in this cell?" before you have any ids. Works for every vector product, including S-101. Filters: `spec`, `datasetId` and a spatial `query`. Each count reports `count` and `withGeometry` (how many have geometry). |
| `nearest_features` | No | Ranks vector features by true distance from a point, for questions such as "nearest light to my position?" or "is this point inside a restricted area?". An area that contains the point is returned at `distanceMeters` 0 with `containment` `inside`. Every other feature reports the distance to the nearest point on its geometry (on a segment, not only a vertex) and `bearingDegrees` toward it. Works for every vector product, including S-101. Filters: `spec`, `datasetId`, `featureType` and `maxDistanceMeters`. `limit` (default 10) caps the list and sets `truncated`. |
| `search_features` | No | Finds vector features by name. Searches the `OBJNAM`, `NOBJNM` and `objectName` attributes (including S-101) and the `name` and `displayName` of the repeatable `featureName` complex attribute (GML products). Matches case-insensitive substrings by default; `exact` matches whole names and `caseSensitive` matches case. Scope with `spec`, `datasetId` and a spatial `query`. Each match reports `matchedName` and `matchedAttribute`. Paged. |
| `sample_coverage` | No | Samples a depth, water level or current at a point from an S-102, S-104 or S-111 dataset. For S-104 and S-111, `time` (or a `times` envelope) picks the nearest time step of a dataset whose range contains it. A time outside every covering dataset's range returns `time_out_of_range` by default; see [Times outside the data](#times-outside-the-data). |
| `sample_coverage_along` | No | Samples a coverage at each vertex of a polyline, with the same time handling as `sample_coverage`. A vertex with no value has a `null` `result` and an `error` (`code`, `message`). When no vertex has a value because of the requested time, the call returns `time_out_of_range`. |
| `get_passage_result` | No | Checks under-keel clearance along a passage. See [Check a passage's clearance](#check-a-passages-clearance). |
| `check_route` | No | Checks a route against the loaded charts: land, drying ground and chart coverage. Needs no draft. See [Check a route against the chart](#check-a-route-against-the-chart). |

### Session tools

Both hosts provide these tools. They change the host's in-memory state only.

| Tool | Changes state | What it does |
|---|---|---|
| `open_dataset` | Yes | Loads a dataset through the host's normal open path. `path` is a file (S-101 `.000`, HDF5 `.h5`, GML and others) or an exchange set (a folder with `CATALOG.XML`, or a `.zip` of one), detected automatically. `spec` optionally forces the product for a single file. Returns the new dataset ids, `spec`, bounding box, `count`, `loadDurationMs`, `timedOut` (for exchange sets) and `skipped` (why listed datasets were skipped). When nothing loads, the `dataset_load_failed` reason quotes the first five problems, such as an unreadable catalogue, unsupported products or orphan updates. |
| `close_dataset` | Yes | Unloads a dataset by its `id` from `list_datasets` or `open_dataset`. An unknown id returns `removed = false`, not an error. Returns `removed`, `count` and `removedDatasets` (`id` and `spec`). |
| `close_all_datasets` | Yes | Unloads every dataset. Useful for loops that load, render and unload without restarting. Returns `removed`, `count` and `removedDatasets`. |
| `set_palette` | Yes | Sets the palette to `Day`, `Dusk` or `Night` (case-insensitive). Calling it with the current palette does nothing. Returns the applied and previous palette. |
| `set_display_category` | Yes | Sets the ECDIS display category to `DisplayBase`, `Standard`, `OtherInformation` or `All` (case-insensitive). Calling it with the current category does nothing. In SoundCharts it matches the `--display-category` startup option. |
| `set_display_mode` | Yes | Sets a product's display mode (S-100 Part 9 §11.7). Only S-411 sea ice has more than one: `ice-concentration` (default), `ice-sod` (stage of development) or `ice-navigational`, a provisional preview derived from concentration that isn't a POLARIS or RIO product. Accepts the same values as `s100 render --display-mode`, and the product's own mode ids. `spec` selects the product (default `S-411`). Returns the applied and previous mode ids and whether the applied mode is `provisional`. Independent of `set_display_category`. |
| `set_time_step` | Yes | Moves the time of time-aware datasets (S-104, S-111, S-411) to a sample. Pass `index` (zero-based, from `list_time_steps`) or `timestamp` (ISO 8601, snapped to the nearest sample). Returns the resolved index and timestamp. In SoundCharts it matches the `--time-step` startup option. |
| `set_viewport` | Yes | Sets the view. The two hosts take different arguments; see [set_viewport](#set_viewport). |
| `render_to_image` | No | Returns a PNG of the current view as an MCP image. In SoundCharts it shows what the user sees; in `s100 mcp serve` it renders the session's datasets. See [Images](#images). S-104 is drawn with SoundCharts' own gauges and isolines, not IHO symbology ([S-104 portrayal](../src/EncDotNet.S100.Datasets.S104/README.md#portrayal)). |

### Map and window tools

SoundCharts only.

| Tool | Changes state | What it does |
|---|---|---|
| `pick_features` | Only with `select: true` | Finds the vector features under a point on the live map. Pass a screen pixel (`x`/`y`) or a geographic point (`latitude`/`longitude`), not both. For a pixel measured on a `render_to_image` capture, also pass `imageWidth`/`imageHeight` set to the `width`/`height` that tool returned, so the pick uses the capture's exact framing. Without them, `x`/`y` are in the on-screen map's pixels. The result has the same shape as `identify_features` (matches, `totalMatched`, `truncated`) plus `source` (`pixel` or `geo`) and the resolved `latitude`/`longitude`. Pixels outside the image or map, or before the map is laid out, are rejected. With `select: true`, the pick is also shown in SoundCharts, as a click does: the features fill the Object Information panel and the map highlights the point and the selected feature's outline. The result then has `selected: true`. Coverage picks (S-102, S-104, S-111) have no outline and aren't shown. |
| `capture_app_screenshot` | No | Captures the whole app window, including panels, timeline and status bar, as a PNG with a JSON block (`imageFormat`, `byteLength`, `width`, `height`). Use it to see the UI, for example to confirm that `set_panel` opened a panel. `scale` (device pixels per logical pixel, `0.5` to `3`, default `1`) gives a sharper image, such as `2` for a high-DPI capture. `panel` crops to one panel: an id from `list_panels` (its dock, while it is showing) or `map` (the map with its overlays, such as the Time HUD). A smaller image costs an agent fewer tokens. Returns `window_not_ready` before the window is shown, `panel_not_found` for an unknown id and `panel_not_showing` for a panel that isn't on screen. |
| `list_panels` | No | Lists the activity panels in the left, right and bottom docks. Each reports `id`, `title`, `dock` (`Left`, `Right` or `Bottom`), `available` (some panels appear only in some states, such as `Vessels` while the AIS overlay is on and `Helm` while own-ship tracking is on), `selected` (the active tab in its dock), `dockOpen`, and `showing` (`available`, `selected` and `dockOpen` together). Use it to find panel ids for `set_panel` and to confirm a change. |
| `set_panel` | Yes | Shows or hides a panel. `panel` is an id from `list_panels` (case-insensitive), such as `Datasets`, `LayerStack`, `PickReport` or `Timeline`. `visible` defaults to `true`, which selects the panel's tab and opens its dock. `false` closes the dock if that panel is the one showing. A panel already in the requested state is left alone. Returns `showing`, `previousShowing` and `changed`. Errors: `panel_not_found` for an unknown id, and `panel_unavailable` for a panel that can't be shown now. |
| `set_own_ship` | Yes | Positions and steers the simulated own ship. Pass any of `lat` and `lon` (together), `cog` (course over ground, degrees true, `[0, 360)`), `sog` (speed over ground, m/s, at least 0), `heading` (degrees true, applied only with `lat`/`lon`) and `hold` (`true` stops the vessel, `false` resumes). At least one is required. Works whether or not the own-ship overlay is visible. Matches the `--own-ship-pos`, `--own-ship-cog` and `--own-ship-sog` startup options. |
| `await_render_idle` | No | Waits until the live map settles, with no paint, refresh request or layer fetch for `quietPeriodMs` (default 250, `0` to `10000`), or until `timeoutMs` (default 5000, `50` to `120000`). Call it between `set_viewport` and `render_to_image` so the capture shows a settled view. It always waits at least the quiet period and watches the on-screen map, not the `render_to_image` copy. A layer that reports busy but stops painting doesn't hold the wait open. Returns `wentIdle`, `timedOut`, `waitedMs` and `paintsObserved`. |
| `get_render_stats` | No | Reports the cost of the latest on-screen map paint: `frameDurationMs`, `intervalMs` since the previous paint, `totalDrawCalls`, and a breakdown by `style` (`calls`, `durationMs`, slowest first). A rolling `window` covers up to 4,096 recent paints, so an expensive frame isn't lost once the view settles: `count`, `firstSequence`/`lastSequence`, the maximum, mean and 95th percentile of frame time (`frameMaxMs`, `frameMeanMs`, `frameP95Ms`) and vector style time (`vectorMaxMs`, `vectorMeanMs`, `vectorP95Ms`), and `maxTotalDrawCalls`. `window.slowestFrame` gives the slowest paint's sequence, time, duration, instrumented and uninstrumented time and draw calls. Pass `resetWindow: true` to clear the window after reading, for example before and after an interaction. Returns `hasData = false` before the first paint. `totalDrawCalls` counts map style draws (basemap, overlays, untiled layers) but not chart content from the tile cache, so a count of 1 or 2 doesn't mean the chart is empty; check with `render_to_image`. |

### Timeline tools

SoundCharts only.

| Tool | Changes state | What it does |
|---|---|---|
| `get_timeline_state` | No | Reads the Timeline as the user sees it: `mode` (`live` while the view time follows now, else `pinned`), `now`, `viewTime`, the loaded range (`minimum`, `maximum`, `sampleCount`), `coverage` windows (gaps lie between them), forecast `runs`, `nowInCoverage`, `forecastEnded`, the displayed `readout`, `offset` and `summary`, the status `message` and `messageAction`, the axis `window`, its `preset`, the `step` and its `stepDriver`, and the collapsed `gaps` with their `length`. For each time-aware layer (`layers`) it gives its `drawnTime` (null when it has no data near the view time and hides), `previousSample` and `nextSample`, its `time` as the Datasets list shows it (`08:00Z · T+20 h`, `no data · last 18:00Z, 6 h earlier`, `drawing…`), and whether it's `hidden` or `drawing`. `inMapView` says whether **In map view** is on, `layout` is `lanes` or `strip`, and `showOnline` says whether lanes also show Library data that isn't loaded. `lanes` lists each lane: `id`, `label`, product `group`, whether it's `listed` or folded away, whether its footprint is `inMapView`, `expired`, its `time`, whether it's a `library` lane, `newRun`, and its Library `windows` (item id, `online`/`on_disk`/`loaded`, start, end, run), whose ids work with `library_action`. While the map draws a new time, `message` reads `Drawing … · N of M layers ready`. While Check route is on and the passage has ETAs, `passage` describes the passage lanes above the product groups: the `departure`, `arrival`, `minUkcTime` and `verdict`, whether it's `stale`, the bar's `start` and `end` and the `minUkcPosition` tick on the axis (0 to 1), `canFollow`, and the departure-window lane's `departurePosition`, `windows` label and `windowSegments` (each with its `kind` and axis `start`/`end`). `followPassage` says whether **Follow passage** is on. Layer times settle after the map refreshes, so call `await_render_idle` after `set_view_time` before reading them. |
| `set_view_time` | Yes | Moves the Timeline as scrubbing or **Now** does. `time` is `now` (the view then follows now), an ISO 8601 time, or an offset from the view time (`+6h`, `-30m`, `+1d`). `snap` is `exact` (default; layers apply their own time limits, so you can test times between samples) or `nearest` (the nearest loaded sample). Choosing a time leaves Live mode. Returns the `get_timeline_state` payload, or `view_time_not_applied` when no time-aware dataset is loaded. `now` works past every loaded window: Live follows the clock and layers without data hide. |
| `step_time` | Yes | Steps the Timeline as its step buttons and arrow keys do. `direction` is `next` or `previous`. `unit` is `10min`, `1h`, `6h` or `1d` (landing on whole units), `sample` (of the step-driver layer), `boundary` (dataset and run starts and ends), `data` (the next or previous cluster, skipping gaps) or `current` (the Timeline's chosen step). `count` repeats it. Pins the time. Returns `view_time_not_applied` when there's nothing further that way. |
| `set_timeline_view` | Yes | Changes what the Timeline shows, as its preset menu, wheel, drag, **In map view** and **Collapse to strip** do. Pass at most one of a `preset` (`now_6h`, `today`, `next_48h`, `this_run`, `in_view` for the data of the layers in the map view, or `all_loaded`), `zoom` (`in` or `out` around the view time), or a custom `start` and `end`. You can also pass `inMapView` (list only the layers whose footprint intersects the map view, which then set the axis; the rest fold into one row), `showOnline` (show Library data that isn't loaded: online dashed, on disk outlined) and `layout` (`lanes` or `strip`), which apply first. Leaves the view time alone. |

### Dataset and notification tools

SoundCharts only.

| Tool | Changes state | What it does |
|---|---|---|
| `set_dataset_state` | Yes | Shows or hides a loaded dataset (`visible`), sets its `opacity` (0 to 1) and moves it in the draw order (`order`: `up`, `down`, `front` or `back`), as the Datasets list's eye icon, opacity control and move buttons do. With none of them, it only reports the state. Use it to show datasets that load hidden, such as gridded S-104 surfaces or duplicate exchange-set variants. Returns the state before and after (`position` and `previousPosition` are rows in the draw order, 0 on top), `changed`, and `viewingGroups`: the dataset's own viewing-group filter (`shownOnly`, `hidden`), or null. Check route sets `shownOnly: [52270]` on a received S-421 route's dataset so only its action points draw. Returns `dataset_not_found` for an unknown id. |
| `select_dataset` | Yes | Selects a loaded dataset in the Datasets panel, as clicking its row does, so the inspector and the map's validation overlay follow it. `tab` (`dataset`, `layers` or `validation`) switches the inspector tab; otherwise the tab is kept. It doesn't open the panel; call `set_panel Datasets` first to see it. Returns `id`, `spec`, `previousId`, the `tab` shown, `deferred`, and a `validation` summary: `state` (`ready`, `no_rule_pack` or `not_loaded`), `total`, `errors`, `warnings`, `infos`, `located` (findings with a location, which the overlay draws) and `message` (the Validation tab's summary or empty-state text). Validation runs when a dataset loads, and `open_dataset` returns after it. For a dataset still loading, the tool waits up to `timeoutMs` (default 10000). An exchange-set cell that isn't loaded until it's in view reports `not_loaded`. Call `await_render_idle` before `capture_app_screenshot` so the overlay has painted. Returns `dataset_not_found` for an unknown id. |
| `list_notifications` | No | Lists the notifications on screen, oldest first: `id`, `severity`, `title`, `message`, `createdUtc`, `persistent` and their action labels. |
| `dismiss_notification` | Yes | Dismisses the notification with `id`, or all of them (`all`, the default), as the close button does. Returns the ids dismissed, or `notification_not_found`. Useful before a screenshot. |

### Navigational warning tools

SoundCharts only.

| Tool | Changes state | What it does |
|---|---|---|
| `list_navigational_warnings` | No | Lists every navigational warning part in the loaded S-124 datasets, as the **Navigational warnings** panel does, including text-only parts that have no location and draw nothing on the map. Returns the panel's `time` (`view_time` or `now`) and `statusTime`, the counts `inForce`, `notInForce` and `textOnly`, `hasSources`, `loading`, the panel's in-map `mapNote`, and `warnings` in dataset order. Each has its `datasetId` and `featureId`, `label` (series and number), `series`, `number`, `type`, `generalType`, `area`, `locality`, `issuer`, `published`, `status` at `statusTime` (`in_force`, `not_yet_in_force` or `ended`, judged with every loaded warning, so cancellations and in-force bulletins count), `statusReason` (`validity`, `cancellation_date`, `cancelled`, `not_in_bulletin` or `bulletin_replaced`; null while in force), `endedBy` (the cancelling warning or the in-force bulletin, such as `NAVAREA X 110/26`) and `statusReasonText` ("Cancelled — not in in-force bulletin NAVAREA X 110/26", as the Warning card says; the panel row shortens it to "Not in bulletin NAVAREA X 110/26"), `geometry` (`point`, `curve`, `surface` or `none`), `textOnly`, `bounds`, `validity` ranges, message `languages`, `selected`, `new` (it first appeared in its source's latest refresh and its card hasn't been opened since; never for a file opened directly) and `sourceId`. Also returns the `new` count (the summary's "N new" and the rail badge), `canRefresh`, and `sources`: each Library source of the listed warnings with its `id`, `name`, `receivedAt` (when it last answered), `stale` (its latest refresh failed), `refreshing` and the group `header` ("AMSA · 07:40" or "AMSA · 07:40, stale"), and `bulletins`: the latest in-force bulletin of each series published by `statusTime`, with its `series`, `label`, `published`, `noMessageOnHand`, the numbers it `listed`, the listed numbers that aren't loaded (`notReceived`, S-124 §9.2.2) and the panel's `notReceivedText`. |
| `select_navigational_warning` | Yes | Selects the part `featureId` of dataset `datasetId` in the **Navigational warnings** panel, as clicking its row does: **Object Information** opens on its Warning card and, when the part has a location and is in force, the map frames and highlights it. A part that isn't in force leaves the map where it is, and a text-only part shows the in-map note "… has no location, so nothing is drawn for it." Clears a search or filter that hides the row. Returns what `list_navigational_warnings` returns. |
| `refresh_navigational_warnings` | Yes | Reads the SECOM services and online feeds that supplied the listed warnings again, as the panel's **Refresh** does, or only `sourceId`, as a stale group's **Retry** does, through the Library's refresh and sync. Returns at once with what `list_navigational_warnings` returns; poll it until the sources stop `refreshing`. Fails when the warnings only come from files opened directly. |
| `get_warning_cards` | No | Lists the Warning cards **Object Information** shows: one card for a lone warning, or one per warning part when a pick hits several (`isList`), points before lines before areas, then by number. Each card has its `datasetId`, `featureId`, `label`, `type`, `status` at the view time, `statusReason` and `statusReasonText` (the reason line under the header, such as "Cancelled by NAVAREA X 104/26"), `open`, `collapsible`, `previewed`, `canZoom`, `new` (the header shows NEW), `notForNavigation` (the footer shows NOT FOR NAVIGATION, from the dataset's exchange-set metadata), and the `links` in its text: each warning number that names a listed warning, with that warning's `datasetId`, `featureId` and `label`. `otherHits` lists the other features shown as rows below the cards. Open a collapsed card with `ui_invoke` on its `PickReport.WarningCard.Header`, and zoom with `PickReport.TextCard.ZoomTo`. |
| `follow_warning_link` | Yes | Follows the link `text` (such as `106/26`; `occurrence` picks among repeats, default 1) in the open card, or in the card `datasetId`/`featureId`, as clicking it does: the linked warning is selected in the **Navigational warnings** panel and its card opens. Returns what `get_warning_cards` returns. |
| `preview_warning_card` | Yes | Previews the part `featureId` of `datasetId` on the map with the pick highlight, as pointing at its card does when a pick hits several warnings; omit `featureId` to end the preview. Returns what `get_warning_cards` returns. |

### Library tools

SoundCharts only.

| Tool | Changes state | What it does |
|---|---|---|
| `list_library_sources` | No | Lists the Library. Each collection (top-level node) has its kind tag (`DIR`, `ZIP`, `WEB`, `AWS`, `LIST`, `FEED`, `SECOM`, `JSON` or `S-128`), item count and status line. Each source has its index state, `indexedAt` (how old a cached online catalogue is), URL (shared-feed tokens masked), item counts by state unless `counts: false`, for a source kept downloaded its last `sync` (objects local and listed, downloaded, pruned, failed, bytes needed when too large), and `showOnMap`. |
| `query_library_items` | No | Finds Library datasets, paged (`page`; `pageSize` 1 to 500, default 50). Filters: `sourceId` (collection or source), `states` (`online`, `local`, `loaded`, `on_pan`, `update`, `expired`, `missing`, `listed`), `validAt` (`view_time`, as the Library's valid-at-view-time toggle does, or an ISO 8601 time: data whose run or time coverage includes it), `spec`, `text` (as the Library filter box), a bounding box (`south`, `west`, `north`, `east`), or a point (`lat`, `lon`: what covers it, most detailed first, as tapping the map does). Each item reports `id` (`<sourceId>:<key>`), spec, state and tags as its row shows them, edition, issue date, size, bounds, local path and, for forecasts, the model, run and `validUntil`. |
| `describe_library_item` | No | Returns one item (`itemId`) with its details pane: groups of labelled fields (Forecast, Product, Coverage, Source and so on). Returns `library_item_not_found` for an unknown id. |
| `list_known_sources` | No | Lists the online catalogue directory: the curated sources and the user's own (`userAdded`), with provider, region, format, URL, edition and size support, product, pilot and not-for-navigation flags, and forecast feeds' models (cadence, horizon). |
| `add_library_source` | Yes | Adds a Library source the way the add dialogs do. Pass a known source (`knownSourceId`), a catalogue or feed `url` (including a SECOM service endpoint), or a local `path` (folder, exchange set, manifest or S-128 catalogue; `kind` overrides the guess). `sync` keeps an online source's items downloaded and current (a SECOM service also prunes). `inMapView` limits a SECOM service to the current map view. For a SECOM service, `identity` sets the MCP identity it presents and signs with (`default`, the default; `none`; or an `sc-ident:…` reference), and `wait: true` adds a service that can't be read yet (it needs a certificate or a signed request, refused the identity, has an untrusted server or can't be reached) with no datasets, scoped to `choices` or the product it is known for. It lists by itself once it can be read; the result's `waitingBecause` says why it couldn't be. `showOnMap` keeps a source's local datasets loading as you pan, under one Datasets row; it's on by default for a synced SECOM service. Call with `preview: true` first to read the catalogue and list its `choices` (NOAA states, districts and regions, USACE rivers, S-111 models, S-102 areas, manifest groups, feed or SECOM products) with sizes, and the forecast `shapes`, S-100 `resolutions` and existing `collections`. Then add with `choices` (values or labels) or `includeAll`, into `collectionId` or a new collection (`collectionName`). It only indexes; nothing is downloaded. Returns the new collection and source ids. |
| `refresh_library_source` | Yes | Re-indexes a collection or source (`id`), or the whole Library, as **Refresh** does. Waits up to `waitMs` (default 60 seconds) and reports items `added` and `removed`, items whose state `changed` (by new state, such as `update` or `expired`) and `counts` by state. |
| `library_action` | Yes | Runs `load`, `load_as_you_pan`, `download` (then load), `download_only`, `update` (newer editions or runs) or `cancel` on `itemIds`, or on items chosen with the `query_library_items` filters. `cancel` with `all: true` cancels every download. Items the action doesn't apply to are `skipped`, by state. `dryRun: true` reports the count and `bytes` without acting. `maxBytes` refuses larger downloads with `library_change_rejected`. Downloads run in the background; see `await_library_idle`. |
| `remove_library_source` | Yes | Removes a collection or source (`id`) and deletes its cached index. Downloaded files stay on disk. Requires `confirm: true`. |
| `set_library_source_options` | Yes | Turns **Keep downloaded** (`sync`) and **Show on map** (`showOnMap`) on or off for a source (`id`), or for every source in a collection, as the Library tree's menu does. For a SECOM source, `identity` sets the MCP identity it presents when the service asks for a client certificate, and signs SECOM 2.0 requests with: `default` (follows the default identity), `none`, or an identity's reference id (`sc-ident:…`). A reference that names no identity presents none; no other identity is ever tried. Options you don't pass are left as they are. `sync` applies to online sources only, and `identity` to SECOM sources only: each is rejected for a single source of another kind and skipped for a collection's other sources. A changed source re-indexes, and syncing downloads in the background (`await_library_idle`). Returns each source's `sync`, `showOnMap`, `canSync`, `changed` and, for a SECOM source, `identity`; `list_library_sources` also shows a SECOM source's `identity`. |
| `await_library_idle` | No | Waits up to `timeoutMs` (default 60 seconds) until no source is indexing, no download is running, and every dataset a `library_action` download or load opens has opened. Returns `idle`, `timedOut`, `indexing`, `loading` (datasets still to open, including those still downloading) and the running batch's progress. |

### SECOM tools

SoundCharts only. These tools work with SECOM (IEC 63173-2) data services.

| Tool | Changes state | What it does |
|---|---|---|
| `list_secom_services` | No | Lists SECOM data services from the MCP service registry: name, organisation, product, released or provisional, endpoint and area. Unusable entries (localhost, bare hosts, deleted, duplicates) are left out. `product` filters, for example `S-124`. `probe: true` checks up to 60 services and reports `Open` (readable without a certificate), `NeedsCertificate`, `OpenWithCertificate` or `CertificateRefused` (with the default identity, from `set_secom_identity`), `NeedsSecom2Search` (lists only through SECOM 2.0 signed requests: no identity is set, or the signed request wasn't accepted), `UntrustedServer` (the TLS certificate was refused: no trusted root, expired, for another host, or revoked) or `Unreachable`. Probed services also report `serverCertificate` (`SystemTrusted`, `AnchorTrusted`, `NotTrusted`, `Expired`, `WrongHost` or `Revoked`) and `serverCertificateAnchor` (for example `MCP MCC`); SECOM requests trust MCP-issued server certificates. For `AnchorTrusted`, `serverCertificateRevocation` is `NotRevoked`, or `NotChecked` when no current revocation list could be read, in which case the connection is still allowed. Add an open service with `add_library_source url=<endpoint>`. |
| `set_secom_identity` | Yes | Sets, clears (`clear: true`) or reports the MCP identity (a client certificate, as PKCS#12 or PEM with its key) that SECOM requests present to services that ask for one: subject, MRN, trust anchor, expiry, and `reference` for a stored identity. A file `path` is used for this session only and never saved. `path` can instead be a stored identity's reference id from **Settings** > **Keys & certificates** (`sc-ident:…`), which makes it the default identity: SECOM sources set to `default` present it. `signatureAlgorithm` sets what SECOM 2.0 request envelopes are signed with (`ecdsa-384-sha2` by default for P-384 keys). Keys and passwords are never returned. An expired identity is refused. After setting one, `list_secom_services` with `probe` reports `OpenWithCertificate` or `CertificateRefused`. |

### Route tools

SoundCharts only. The route tools edit the same route collection that the
**Routes** panel and route overlay show, so changes appear live. Most default to
the active route when you omit `routeId`. Waypoints are addressed by zero-based
`index`, and legs by zero-based `legIndex`; leg `i` joins waypoint `i` to
waypoint `i+1`. The fields follow the S-421 model, so a route maps onto S-421
GML directly.

A route needs only waypoints. Its **passage** holds optional route-level
values: draft, squat allowance, speed, departure, required UKC and cross-track
limits. Each leg inherits these unless it overrides them, so changing a passage
value changes only the legs that inherit it.

Each route keeps its own undo history. Every edit a route tool makes is one
step in it, shared with edits made in the viewer, so `undo_route_edit` can
revert a drag on the map and Ctrl+Z (⌘Z) can revert a tool's edit.

There are two kinds of route:

- **Owned routes** are the ones you create. They're editable and saved
  between sessions.
- **Received routes** come from an S-421 dataset you open (from the Datasets
  or Library panel, by drag and drop, with `open_dataset`, or with
  `import_route`). They're
  read-only and aren't saved; each lasts while its dataset is open. Datasets
  the Library loads as you pan never become routes. `list_routes` and
  `get_route` report `readOnly: true` and the `source` (format, version, file,
  route ID, edition and status). Opening the same route again (the same file,
  or the same route ID and edition) selects the existing route, and a newer
  edition replaces it. Closing the dataset removes the route, and turns Check
  passage off if it was checking it. `delete_route` on a received route closes
  its dataset.

A tool that would edit a received route fails with `route_read_only`, which names
`copy_route`. Only its **check settings** can change: draft, squat, required UKC and departure. These
are your values for checking the route, not route content. Set them with
`set_passage` (or `set_departure`); they apply to every leg, and they're kept
on this computer under the route ID and edition. Values in the file are the
defaults. Speed and cross-track are route values, and they stay as the file
gives them.

To edit a received route, make an owned copy with `copy_route`. The copy is a
new route: it gets a new route ID, a reference to the source's route ID and
edition, edition 1 and status Planned. What the viewer's route can't hold,
such as action points, further schedules and extension attributes, is kept and
written back when the copy is exported. If waypoints are inserted or deleted
afterwards, the preserved schedules no longer match the route, and export
leaves them out unless you keep them in the **Routes** panel. `export_route`
writes any route. A received route exported unchanged keeps its route ID,
edition and status. Your check settings are written only when you ask, and
check results are never exported.

A received S-421 route starts in **Native** display mode: its dataset draws it
with the S-421 portrayal, and the route overlay draws nothing.
`set_route_display_mode` switches it to **Planning**, which hides the dataset
and draws the route on the overlay, without editing handles. While Check
passage is on for the route, it's drawn in Planning mode, and the dataset stays
on with only its action points (S-421 viewing group 52270); `set_dataset_state`
reports this as the dataset's `viewingGroups`.

| Tool | Changes state | What it does |
|---|---|---|
| `create_route` | Yes | Creates an empty route and makes it the active route. Optional `name` and `id` (a GUID is generated when you omit `id`; ids must be unique). Returns the route's full state, as `get_route` does. |
| `list_routes` | No | Lists every route with its `routeId`, `name`, `waypointCount`, `legCount`, `totalDistanceNm`, `isActive`, `readOnly`, `source` (for a received route) and `displayMode` (`native` or `planning`), the collection's `activeRouteId`, `editMode` (whether the route editor is on), and `passageCardExpanded` (whether the **Routes** panel's Passage card is expanded). |
| `get_route` | No | Returns one route: `routeId`, `name`, `isActive`, `info` (name, author, description, ports, validity, vessel), `passage` (as `get_passage` returns it), `waypoints` (`index`, `lat`, `lon`, and optional `number`, `name`, `fixed`, `turnRadiusNm`), `legs`, `totalDistanceNm`, the undo state (`canUndo`, `canRedo`, `undoLabel` and `redoLabel`, for example `move waypoint`), `readOnly`, `source` and `displayMode` (as `list_routes` reports them), `passageFields` (as `get_passage` reports `fields`), and, for the active route, the selection: `selectedWaypointIndex` and `selectedLegIndex`. Each leg has its `index`, `geometryType`, computed `distanceNm` and `initialBearingDegrees`, its passage values twice (`effective`, the values that apply, and `overrides`, only the leg's own values), the rest of its S-421 navigational envelope, and `durationMinutes` when it has a speed. Once any leg has a speed, each waypoint has an `eta` and the route has `times`: `departure` (the passage departure, or the view time when none is set, with `fromViewTime: true`), `arrival`, `durationMinutes`, and `etasStopAtLeg`, the first leg with no speed. ETAs use each leg's entered speed over ground; S-111 currents aren't applied. |
| `delete_route` | Yes | Removes a route. Returns `routeId`, `deleted` and the new `activeRouteId`. |
| `append_waypoint` | Yes | Adds a waypoint (`lat`, `lon`) at the end of a route, with optional `number`, `name`, `fixed` and `turnRadiusNm`. Returns the updated route. |
| `insert_waypoint` | Yes | Inserts a waypoint at `index` (`0` to `waypointCount`; `0` prepends and `waypointCount` appends), splitting the leg there. Takes the same optional fields as `append_waypoint`. Returns the updated route. |
| `move_waypoint` | Yes | Moves the waypoint at `index` (`0` to `waypointCount - 1`) to a new `lat` and `lon`. Returns the updated route. |
| `delete_waypoint` | Yes | Removes the waypoint at `index`, merging the legs either side. Returns the updated route. |
| `set_leg_attributes` | Yes | Updates one leg (`legIndex`): its `geometryType` (`loxodrome` or `geodesic`), its passage overrides (`speedKnots`, `draftMeters`, `portCrossTrackDistanceLimitMeters`, `starboardCrossTrackDistanceLimitMeters`, `staticUnderKeelClearanceMeters`) and the rest of its navigational envelope (channel limits, safety contour and depth, minimum and maximum SOG and STW, dynamic UKC, safety margin, note; metres and knots as in S-421). Values you pass overwrite, and values you omit are unchanged. An override equal to the route's passage value isn't stored, so the leg keeps inheriting. `clear` lists parameter names to unset; clearing an override resets the leg to inherit. Returns the updated route. |
| `set_route_info` | Yes | Updates route metadata (`name`, `author`, `description`, `departurePortId`, `arrivalPortId`, `validityStart`, `validityEnd`) and vessel particulars (`vesselName`, `vesselMmsi`, `vesselImo`, `vesselCallsign`, `vesselLengthMeters`, `vesselBeamMeters`; passing any vessel field creates the vessel block). Values you pass overwrite. Returns the updated route. |
| `get_passage` | No | Returns a route's passage values: `draftMeters`, `squatMeters`, `speedKnots` (SOG), `departure`, `requiredUkcMeters`, `portCrossTrackMeters`, `starboardCrossTrackMeters` and `vessel`. Unset values are left out. With no `departure`, ETAs use the view time. `fields` says what each value is: its `kind` is `checkSetting` (draft, squat, required UKC, departure) or `route` (speed, cross-track); `fromFile` is true when a received route's value comes from its file; and `readOnly` is true for a received route's route values. |
| `set_passage` | Yes | Updates a route's passage values: `draftMeters`, `squatMeters`, `speedKnots`, `departure`, `requiredUkcMeters`, and the cross-track limits (`crossTrackMeters` for both sides, or `portCrossTrackMeters` and `starboardCrossTrackMeters`). Values you pass overwrite, values you omit are unchanged, and names in `clear` are unset. Set the vessel with `set_route_info`. On a received route only the check settings (`draftMeters`, `squatMeters`, `requiredUkcMeters`, `departure`) can be set: each applies to every leg, and clearing one returns to the file's value. A route value fails with `route_read_only`. Returns the updated route. |
| `undo_route_edit` | Yes | Undoes the most recent edit to a route, whether it was made in the viewer or by a route tool. Returns the updated route. Fails with `route_history_empty` when there is nothing to undo. |
| `redo_route_edit` | Yes | Redoes the most recently undone edit. Any new edit clears what can be redone. Fails with `route_history_empty` when there is nothing to redo. |
| `select_route_item` | Yes | Selects a waypoint (`waypointIndex`) or a leg (`legIndex`), as clicking its row in the **Routes** panel does, or clears the selection when you pass neither. The map highlights the selected waypoint, or haloes the selected leg and always shows its label. Selecting makes the route active. Returns the updated route. |
| `set_passage_card` | Yes | Expands (`expanded: true`) or collapses the **Routes** panel's Passage card, as clicking its header does. Switching the active route sets it again: expanded for a route with passage values, collapsed otherwise. To open a leg's detail, select the leg with `select_route_item`. Returns `expanded`. |
| `set_route_edit_mode` | Yes | Turns the route editor on (`enabled: true`) or off, as the Routes panel's **Edit** toggle and Ctrl+E (⌘E) do. While it is on, clicks on the map add waypoints to the active route. Returns `enabled` and `activeRouteId`. Turning it on for a received route fails with `route_read_only`. |
| `import_route` | Yes | Opens a route file (`path`, or its `content`) as a received route, as the **Routes** header's **Import route…** does. The format comes from the file's content. An S-421 file also opens as a dataset, and its route starts in Native mode. A file that's already open, or one with the same route ID and edition, selects the existing route (`alreadyOpen: true`). A newer edition replaces it. Returns the route, now active. Fails with `route_import_failed`, whose `reason` is `not_a_route`, `unsupported_version`, `too_few_waypoints` or `older_edition`. |
| `copy_route` | Yes | **Copy to edit**: makes an owned, editable copy of a received route (`routeId`, or the active route), named "{name} (copy)". The copy becomes active and opens in Edit mode, and one `undo_route_edit` removes it. Returns `keeps` and `notKept` (the Copy to edit dialog's two lists), the copy's `identity` (`routeId`, `edition`, `status`, `previousRouteId`, `previousEdition`, `kept`) and the `copy`. With `dryRun: true` it changes nothing and returns the lists and identity, with `identity.routeId` null. On an owned route it fails with `invalid_argument`. |
| `export_route` | No | **Export…**: writes a route in a `format` (`S-421`, version `1.0`). Pass `includeCheckSettings: true` to also write your draft, required UKC and departure. Writes to `path`, or returns the file's `content` when you omit it. Returns the `identity`, `cannotHold` (the fields of this route the format can't hold, empty when nothing is lost) and the dialog's `suggestedFileName`. |
| `set_route_display_mode` | Yes | Sets how the map draws a route (`mode`): `native` draws a received S-421 route with its dataset's own portrayal, and `planning` hides the dataset and draws the route on the route overlay. The dataset's visibility in the Datasets panel follows the mode, and turning the dataset on or off there switches the mode. Only received S-421 routes have a Native mode. Returns the updated route. |
| `set_check_mode` | Yes | Turns **Check route** on (`on: true`) or off for the active route, or for `routeId` (made active first), as the Routes panel's toggle does. The route needs at least two waypoints; without a draft only the route checks run, and the clearance parts (minimum UKC, Clearance tab, window) are absent (`hasDraft: false`) and the route keeps its normal colour. While it's on, the map colours the route by UKC state at ETA when a draft is set, Object Information folds to its edge tab, and the panel shows the verdict card and leg chips. By default it waits for the result (`wait: false` returns at once). Returns what `get_check_state` returns. |
| `get_check_state` | No | Returns what Check route is showing: `on`, `routeId`, `hasDraft` (false when the clearance check doesn't run), `clearanceChecked`, `computing`, `stale` (the result is being recomputed after an edit, so the route draws grey and dashed), and the verdict card: one verdict over every check, worst first, as `verdictKind` (`must_review`, `data_ends`, `check` or `clear`), `verdictTone`, `verdictTitle` (naming the worst finding, such as "Crosses drying ground", or without a draft "2 to review"), `verdictSubtitle`, the `summary` rows (`clearance`, `route` and, when S-124 warnings were checked, `warnings`, each with `text`, `chip`, `chipTone` and a `link` such as "+ Add draft") and the `footer`. `verdict` is the clearance band (null without a draft), the `confidence` reasons and the verdict card's `confidenceText` (for example "Vertical datums unconfirmed: depth and water level may not share a reference"), `minUkcMeters` and `minUkcLeg`, any `error`, and the cursor (`cursorNm`, `cursorLatitude`, `cursorLongitude`, `cursorLeg`, `cursorEta`). It also returns the `departure` being checked (`departureIsViewTime` when the route has none) and the departure `window`: `from`, `to` and `stepMinutes` of the sweep across the S-104 range, the `windows` of departures that meet your settings (lower-confidence ones included) with their `confidence` caveat, `arrivalAfterDataEnds` (the departures that arrive after the S-104 data ends), `insideWindow`, `nearestMeetingDeparture`, the verdict card's `text` and its `departButton`. `windowComputing` is true while the window is swept again after an edit. Waits for a pending result unless `wait: false`. For the full numbers, call `get_passage_result`. |
| `set_departure` | Yes | Sets the route's `departure`, as dragging the Clearance tab's departure band does, or with `nearestMeeting: true` jumps to the departure nearest the current one that meets your settings, as the verdict card's **Depart {HH:MM}** does (Check route must be on). One undoable edit. Waits for the new result and window unless `wait: false`, and returns what `get_check_state` returns. |
| `set_follow_passage` | Yes | Turns **Follow passage** on (`on: true`) or off, as the Timeline header's toggle does. While it's on, the view time is pinned to the ETA at the Clearance cursor (`set_passage_cursor`), or at the minimum UKC when there's no cursor, so S-104 and S-111 show what the ship meets. Turning it on pins the Timeline; when the route has no departure, it first sets the departure to the one being checked, as one undoable edit. Turning it off leaves the view time where it is. Choosing another time (`set_view_time`, `step_time`, Go live) or turning Check route off stops it. Needs Check route on with ETAs; otherwise returns `invalid_argument`. Returns what `get_timeline_state` returns. |
| `set_passage_cursor` | Yes | Moves the Check route cursor and the ghost vessel to `distanceNm` along the checked route, as hovering a leg row does, or hides them when `distanceNm` is omitted. Returns the state, including the cursor's position and ETA. |
| `get_clearance_state` | No | Returns what the **Clearance** tab shows while Check route is on: the profile's `axis` (`distance` or `time`), `canShowTime`, `sourcesOpen`, and the readout at the cursor (`readoutSample`, the sample index in `get_passage_result`; `readoutPosition`, `readoutLeg`, `depth` and `depthSource` with its cell, `waterLevel` and `waterLevelSource` with its station or grid and time step, `draftSquat`, `ukc` and `state`, such as "Meets your settings · lower confidence"). The readout's `band` (`meets_settings`, `tight`, `below_setting` or `no_data`) and `confidence` reasons are reported separately. It also returns the `sources` groups, and the `departureBand`: the `departure` beside it, the `knobFraction` (0 to 1 across the band), the `startLabel`, `middleLabel` and `endLabel` ("12:00 · S-104 ends"), `stale`, and the `segments`, each a run of departures with its `kind` (`meets_settings`, `below_setting`, `arrival_after_data_ends` or `no_data`). Without a cursor, the readout describes the minimum. Open the tab with `set_panel` `Clearance`. |
| `set_clearance_view` | Yes | Sets the Clearance profile's `axis` (`distance` or `time`; time needs ETAs) and opens or closes Sources (`sourcesOpen`), as the tab's header controls do. The cursor stays on the same sample. Returns what `get_clearance_state` returns. |
| `get_findings` | No | Returns a route's findings list, as the Routes panel's **Findings** tab shows it in Check route mode: `count`, the counts per group (`mustReview`, `check`, `info`), the `summary` (such as "2 to review", the verdict card's title without a draft), `stale` while a check is recomputing, `selectedId`, and `findings` in list order. Each finding has an `id`, a `code` (e.g. `clearance.below_setting`, `confidence.charted_depth_from_s101`, `data.water_level_ends`, `data.s111_forecast_expired`, or the route checks' `route_check.crosses_land`, `route_check.crosses_drying`, `route_check.land_in_corridor`, `route_check.drying_in_corridor` and `data.outside_chart_coverage`; see `check_route`), its `group` (`must_review`, `check` or `info`), its `source` (`clearance`, `route_check`, `s124`, `confidence` or `data`) and display `tag`, the `title` and `meta` line, `legIndex`, `distanceNm` (where the profile cursor moves to), a `location` (`point` or `span`, with `startNm`, `endNm` and the `path`), and a `feature` reference (dataset and feature id) when one is behind it. Pass `routeId` for another route; omit it for the active route. For the checked route (`live: true`) it's the live list; otherwise the route is checked once, without changing the view. The route needs at least two waypoints; without a draft there are no clearance findings, but the route checks still run. | It also reports the list's view: the `filter` chip (`all`, `clearance`, `route` or `warnings`) and the `counts` per chip, the `merged` rows (check and info findings that share a code show once with a count; each gives its `id`, `code`, `group`, `count`, `meta`, member `ids` and whether it's `expanded`), `infoOpen` (INFO is folded by default) and `expandedId`.
| `select_finding` | Yes | Selects the finding `id` in the checked route's list, as clicking its row does: the map zooms to it (unless `zoom: false`) and outlines it dashed in its tone with the chip "Zoomed to finding · leg 6 → 7 · 1:31 300", and the profile cursor and ghost vessel move to it. Omit `id` to clear the selection. Needs Check route on. Returns what `get_findings` returns. |
| `set_findings_view` | Yes | Sets the checked route's findings list view, as its controls do: the `filter` chip (`all`, `clearance`, `route` or `warnings`; the verdict card's summary rows set it too), `infoOpen` to unfold or fold the INFO group (remembered per route), and `expandedId` to expand a merged row from `get_findings`' `merged` (an empty string collapses it). Needs Check route on. Returns what `get_findings` returns. |

A typical sequence:

1. `create_route` makes a new active route.
2. `append_waypoint` and `insert_waypoint` lay down the geometry. Use the query
   tools, such as `sample_coverage`, `nearest_features` and `find_at`, to check
   each leg.
3. `move_waypoint` and `delete_waypoint` refine it, and `undo_route_edit`
   steps back.
4. `set_passage` sets the draft, speed and other values every leg inherits,
   and `set_leg_attributes` overrides them on a leg and sets its geometry type
   and navigational envelope. `set_route_info` sets the metadata and vessel
   particulars.
5. `get_route` or `list_routes` reads back the result.
6. `set_check_mode` turns on Check route to see the verdict on the map and
   in the panel, and `get_passage_result` returns the numbers behind it.
7. `get_findings` lists what to review, check or note along the route, and
   `select_finding` zooms the map to one and moves the profile cursor there.

### Test tools

SoundCharts registers these tools only when it starts with `--mcp-test-hooks`.
Use them for scripted testing only: the `ui_*` tools can do anything a user can
do.

| Tool | Changes state | What it does |
|---|---|---|
| `set_test_clock` | Yes | Moves (`now`, or `advance` such as `+1h`), freezes (`freeze`) or resets (`reset`) the app's notion of now, so forecasts age, runs expire and a Live Timeline advances without waiting. The Timeline and the Library's expiry check react at once. |
| `ui_tree` | No | Lists the UI as an accessibility client sees it: one root per window and open popup (`kind` `window` or `popup`, such as context menus and flyouts), each a tree of elements. Each element has a `ref` (such as `e12`, valid while the element stays on screen), `id` (its automation id, such as `Datasets.DatasetsTab`, `Library.Tree` or `ActivityBar.Datasets`; see the convention in the [viewer tests README](https://github.com/philliphoff/EncDotNet.S100/blob/main/tests/EncDotNet.S100.Viewer.Tests/README.md)), `role` (`button`, `listItem`, `tabItem`, `treeItem`, `edit`, `checkBox`, `menuItem` and so on), `name`, `text` (a row's visible text, or an icon button's tooltip, when it has no name), `enabled`, `focused`, `patterns` (the actions it supports: `invoke`, `toggle`, `value`, `rangeValue`, `selectionItem`, `expandCollapse`) with their state (`toggle`, `value`, `selected`, `expanded`), and `bounds` in its window. `filter` `interactive` (default) keeps elements with an id or an action and lifts the children of the rest; `all` lists everything. `root` (an id or ref) lists one subtree. `depth` (default 30) and `maxNodes` (default 400; `truncated` says when it cut) limit the size. The map is one element; use the map tools for chart content. |
| `ui_invoke`, `ui_set_value`, `ui_toggle`, `ui_select`, `ui_expand`, `ui_collapse`, `ui_focus`, `ui_context_menu` | Yes | Act on one element as the user does: click a button or menu item (`ui_invoke`); replace a text box's text or set a slider (`ui_set_value`, `value`); flip a check box or toggle button, or set it with `state` `on` or `off` (`ui_toggle`); select a list row, tab, tree node or radio button (`ui_select`); expand or collapse a tree node, expander, combo box or submenu; move keyboard focus (`ui_focus`; leaving a text box runs its lost-focus behaviour, so an in-place rename commits); or open a context menu as a right-click does, selecting the row or node first (`ui_context_menu`). Target an element by `id` or `ref`, not both. An id repeated in every row (such as `Datasets.Row.Remove`) is ambiguous alone: scope it with `within` (the row's ref or an ancestor's id), or use the ref. Returns the element afterwards. Errors: `ui_element_not_found` (with similar ids on screen), `ui_element_ambiguous` (each match's `ref` and row text), `ui_element_disabled` and `ui_action_not_supported` (with the element's `patterns`). A closing dialog or popup animates briefly after the call returns, so read `ui_tree` again before relying on it being gone. |

## Check a route against the chart

`check_route` checks a route against the loaded S-101 cells (and S-401, and
translated S-57) with the same engine as SoundCharts' findings list
(`RouteCheckEngine` in `EncDotNet.S100.Navigation`). It needs no draft. The
result isn't for navigation; show its `disclaimer` once, with the findings.

Pass the route in one of two ways:

- **A polyline.** `polyline` is `{"vertices":[[lat,lon],...]}`, and each pair
  of consecutive vertices is a leg. Both hosts accept this form.
- **A route.** In SoundCharts, pass `routeId`, or neither `routeId` nor
  `polyline` for the active route. Each leg uses its port and starboard
  cross-track limits (XTD).

`corridorHalfWidthMeters` overrides every leg's limits. It's a **half-width**:
how far the corridor reaches to port, and to starboard, of the track, so the
corridor is twice as wide. Any side without a limit uses 50 m.

| Code | Group | When |
|---|---|---|
| `route_check.crosses_land` | `must_review` | The track crosses a `LandArea`, or a `Coastline` where no land area is charted. |
| `route_check.crosses_drying` | `must_review` | The track crosses a `DepthArea` that dries (DRVAL1 below the datum). |
| `route_check.land_in_corridor` | `check` | Land reaches inside the cross-track limits, but the track doesn't cross it. |
| `route_check.drying_in_corridor` | `check` | The same, for drying ground. |
| `data.outside_chart_coverage` | `check` | The track leaves the `DataCoverage` of every loaded cell (source `data`). |

Pass `safetyContourMeters` and `safetyDepthMeters` (together) to add the
depth checks, as SoundCharts' findings list runs them with the mariner's
settings (`get_mariner_settings`). `draftMeters` also judges dangers against
draft + squat + required UKC; a route's own draft is used when you omit it.

| Code | Group | When |
|---|---|---|
| `route_check.danger_in_corridor` | `must_review` | A wreck, obstruction or rock shallower than the threshold, or of unknown depth, inside the cross-track limits. |
| `route_check.danger_near_route` | `check` | Such a danger within 0,1 NM beyond the limits. |
| `route_check.crosses_safety_contour` | `must_review` | The track crosses depth, dredged or drying areas shallower than the safety contour. |
| `route_check.safety_contour_in_corridor` | `check` | Such areas inside the limits, off the track. |
| `route_check.soundings_below_safety_depth` | `check` | Soundings shallower than the safety depth inside the limits. |
| `route_check.crosses_s102_shoal` | `must_review` | The track crosses S-102 cells shallower than the safety contour. |
| `route_check.s102_shoal_in_corridor` | `check` | Such S-102 cells inside the limits, off the track. |

Where cells overlap, only the finest cell at a place counts, so a coarse
cell's generalised land doesn't fire where a finer cell charts water.
Crossings of the track are exact; ground that only reaches into the corridor
is found at cross-sections every 10 m.

The result has the `findings` (with `id`, `code`, `group`, `source`, `title`,
`meta`, `legIndex`, `distanceNm`, a `location` and the `feature` behind it), a
`summary` with the counts per group and its `text` ("2 to review", "1 to
check" or "No findings for your settings"), each leg's corridor (`legs`, with
`portMeters` and `starboardMeters`), the `datasets` it read and the
`disclaimer`. In SoundCharts, `get_findings` shows the same findings in the
route's list.

## Check a passage's clearance

`get_passage_result` computes, at each sample's ETA, UKC = charted depth +
water level − draft − squat, and compares it with the required UKC. It uses
the same engine as SoundCharts' Check route (the `EncDotNet.S100.Navigation`
package). The result isn't for navigation; show its `disclaimer` once, with
the verdict.

Pass the passage in one of two ways:

- **A polyline and a draft.** `polyline` is `{"vertices":[[lat,lon],...]}`,
  and each pair of consecutive vertices is a leg. `draftMeters` is required.
  Both hosts accept this form.
- **A route.** In SoundCharts, pass `routeId`, or neither `routeId` nor
  `polyline` to check the active route. The values are the ones Check route
  uses: the route's passage values with each leg's overrides (`get_passage`,
  `set_passage`, `set_leg_attributes`), and the route's departure, or the
  view time when it has none. `s100 mcp serve` has no routes and returns
  `invalid_argument` for this form.

These arguments override every leg's value:

- `draftMeters`
- `speedKnots`: the speed over ground. S-111 currents aren't applied.
- `squatMeters`: a fixed allowance; it isn't modelled from speed.
- `requiredUkcMeters`: when unset, the check uses 0 m and sets `noMarginSet`.

`departure` defaults to the view time for a route, or the current time for a
polyline. Without a speed, every sample uses the departure time.

How it samples and classifies:

- **Charted depth** comes from S-102. Where there's no S-102, or a cell is
  NoData, it falls back to S-101: the shallowest covering `DredgedArea`, then
  `DepthArea` (DRVAL1), then a `Sounding` within 250 m. S-101 depths are
  lower confidence (`charted_depth_from_s101`).
- **Spacing.** Along S-102, the check steps at half the finest grid cell so no
  cell is skipped. Each sample stands for at most `sampleSpacingMeters`
  (default 50) and keeps the shallowest depth in its stretch.
- **Water level** comes from S-104 at the ETA, interpolated between time
  steps. Grids are used first, then the nearest station within 10 km. Outside
  the S-104 extent or time range, or where a value is NoData, the sample is
  `no_data` ("tide not applied").
- **Datums.** When the S-102 or S-101 and S-104 vertical datums differ, the
  water level is reported but not applied, and the stretch is a
  `datum_mismatch` gap. When a datum isn't declared, or isn't an S-100 code,
  the water level is applied and the stretch is lower confidence
  (`datum_unconfirmed`).
- **States:**
  - `meets_settings`
  - `tight`: less than 0,5 m above the required UKC.
  - `below_setting`
  - `no_data`
- **Confidence** is reported separately from the state, as a list of
  reasons: `charted_depth_from_s101` and `datum_unconfirmed`, or an empty
  list at full confidence. A sample on S-101 depth that meets the required
  UKC is `meets_settings` with `confidence: ["charted_depth_from_s101"]`.
  SoundCharts draws it in the band's colour, dashed. The result, `minimum`,
  each leg, each sample and each swept departure has a `confidence` list.
- **Verdict**, in priority order:
  1. `water_level_ends_before_arrival`
  2. `below_setting`
  3. `incomplete_data`
  4. `meets_settings_tight`
  5. `meets_settings`

  `verdictTitle` gives the same verdict in the app's words, such as "Below
  your UKC setting in 2 places".

The result has:

- `minimum`: the smallest margin over the required UKC, with where and when.
- `minutesBelow` and `placesBelow`.
- `confidence`: the lower-confidence reasons anywhere on the passage. The
  verdict follows the clearance band; confidence qualifies it.
- `legs`: per leg, the values used, the minimum, the `worstState`, the
  minutes below the setting, `lowConfidence`, its `confidence` reasons and
  `noWaterLevel`.
- `gaps` and `lowConfidence`: stretches by kind, with their distances and
  times.
- `sources`: the S-102 and S-101 datasets, the S-104 datasets and stations,
  the datums and the largest S-102 uncertainty.

Set `includeSamples` to get per-sample values. Each sample has its depth
source, dataset and cell, uncertainty, water level, station, time step and
datum agreement. `maxSamples` (default 500) caps the list, and the samples
with the least clearance are kept.

To find a departure window, set `windowFrom` and `windowTo` (and optionally
`windowStepMinutes`, default 5), or set `includeWindow` to sweep the whole
S-104 time range that covers the passage, as SoundCharts' departure band does.
That sweep starts at the first whole step after the data starts and ends where
it ends; over a long forecast the step widens to stay under 2000 departures.
`window` then lists every departure's verdict and confidence, the runs of
departures that meet your settings, the meeting departure nearest
`departure`, `windowConfidence`, and
`arrivalAfterDataEnds`: the departures that arrive after the S-104 data ends,
which never meet your settings. A departure that meets your settings with
lower confidence still counts as meeting them; `windowConfidence` lists the
reasons among the meeting departures, as the caveat on the window.

## Times outside the data

`sample_coverage` and `sample_coverage_along` don't return a value for a time
the data doesn't cover unless you ask for one. This matches the Timeline, which
shows that there's no data at that time instead of a stale frame.

- **Dataset selection depends on time.** Of the S-104 and S-111 datasets that
  cover the point, those whose time range contains the requested time are used.
  The finest grid wins among them, then the newest run (by `issueDate` and
  `issueTime`). With two runs or two models loaded at one point, the one that
  covers the time is sampled even if the other is finer. For station series,
  runs that report the same station are chosen between the same way.
- **Tolerance.** A time within one time-step interval of either end of a
  dataset's range counts as in range and samples the nearest step. With hourly
  steps ending at 21:00Z, 21:20Z samples 21:00Z.
- **A single instant is strict by default.** For `time`, or `times` with
  `kind: "instant"`, a time outside every covering dataset's range returns
  `time_out_of_range`. Its `details` give `requestedTime`, `datasetId`,
  `validFrom`, `validTo`, `run` (when the dataset declares an issue time),
  `nearestStep` and `candidates` (every covering dataset with its range). Ask
  again at `nearestStep`, or tell the user the forecast doesn't reach that far.
- **Opt in to the nearest step** with `outOfRange: "nearest"`. The result then
  has `timeStatus: "before_start"` or `"after_end"`, and `value.sampleTime` is
  the step used. In-range results always have `timeStatus: "in_range"`.
  `outOfRange` defaults to `"error"`.
- **Ranges and series return the covered part.** When the window reaches past
  the data, the result has `truncated: true`, and `coveredFrom` and `coveredTo`
  give the part the data spans. Series instants beyond the data are dropped
  instead of all snapping to the last step. `time_out_of_range` is returned only
  when the window doesn't overlap the data at all. `outOfRange` doesn't affect
  windows.

## Images

`render_to_image` returns a result whose `Content` array holds, in order:

1. an `ImageContentBlock` with the base64-encoded PNG and
   `mimeType: "image/png"`, which MCP clients show inline;
2. a `TextContentBlock` with JSON metadata: `width`, `height`, `pixelDensity`,
   `imageFormat`, `byteLength`, and optionally `viewportWidth`,
   `viewportHeight` and `notes`.

`capture_app_screenshot` returns the same two blocks for the whole app window.

In SoundCharts:

- When you omit both `width` and `height`, the capture is sized to the
  on-screen map, so the PNG matches the user's view pixel for pixel. Otherwise
  the default is 1024 × 768, which letterboxes the map when its shape differs.
  If you pass only one dimension, the other keeps the default.
- `viewportWidth` and `viewportHeight` report the on-screen map size on every
  capture once the map is laid out. Use them to request a matching aspect
  ratio, or pass them to `pick_features` as `imageWidth` and `imageHeight`.
- The capture is taken from a copy of the map that shares its layers but has
  its own view, so it doesn't disturb the user's view or redraw the screen. The
  view, palette, time step and loaded datasets match what the user sees.

In `s100 mcp serve`, the image is a headless composite of the session's
datasets. It fits all of them unless `set_viewport` has set a view.

## Differences between the hosts

Most tools run the same code in both hosts. These differ:

### set_viewport

SoundCharts moves the live map. Pass exactly one of:

- a bounding box: `south`, `west`, `north`, `east`;
- a centre and Web Mercator zoom: `centerLat`, `centerLon`, `zoom`;
- a centre and scale: `centerLat`, `centerLon`, `scaleDenominator` (for
  example `50000` for 1:50,000).

Mixing forms, including `zoom` with `scaleDenominator`, is rejected.
`scaleDenominator` is converted at the centre latitude with the same 0.28 mm
pixel the status bar uses, so the status bar reads it back. `rotation`
(degrees clockwise, `0` is north-up, any finite value, normalised to
`[0, 360)`) turns the map on top of the frame. It must come with a frame form;
to rotate in place, repeat the same centre form with the new `rotation`.

The result echoes the applied rotation and `scaleDenominator`, read back from
the map after the change, so it reflects the zoom limits: 1:1,000 at the view's
latitude, and a zoom-out limit of 1:500,000,000 at the equator, which reads as
1:500,000,000 × cos(latitude) elsewhere. For a bounding box it also reflects the
fit to the map; it's omitted for a bounding box before the map is laid out.

SoundCharts draws the basemap and chart data one world copy either side of the
standard world, so longitudes from -540 to 540 are accepted and frame the
matching copy:

- A dataset kept in a continuous frame across the antimeridian, such as the NWS
  Alaska S-411 (bounds 175 to 225 from `open_dataset`), is framed at its own
  longitudes. For the Beaufort Sea, use
  `{south: 69.5, west: 190, north: 74.5, east: 220}` or
  `{centerLat: 72, centerLon: 205, scaleDenominator: 10000000}`. The same place
  in the standard world (`west: -170, east: -140`) shows the same data.
- A box with `west` greater than `east` crosses the antimeridian: its east edge
  is taken one world east, so `west: 170, east: -140` frames 170 to 220. A box
  can be at most one world wide.
- The result reports the framed box in that continuous frame (`west < east`,
  past ±180° where it was framed) and the map centre as `centerLat` and
  `centerLon`. The map keeps its centre within the loaded data and the
  basemap's world, so a far copy with nothing loaded may be pulled back; the
  echoed centre says where the map settled.

`s100 mcp serve` sets the view for later `render_to_image` calls. Pass either a
centre and scale (`centerLongitude`, `centerLatitude`, `scaleDenominator`) or a
bounding box (`minLongitude`, `minLatitude`, `maxLongitude`, `maxLatitude`).
`rotationDegrees` rotates the centre-and-scale form clockwise about the image
centre, and labels stay upright. Longitudes stay within -180 to 180, and the
box's west edge must be less than its east edge. Latitudes must be within the
Web Mercator limit (±85.05112878°). The result returns the applied view and the
previous one.

### Tools only SoundCharts has

`pick_features`, `capture_app_screenshot`, `set_own_ship`, the panel, Timeline,
dataset-state, notification, navigational warning, Library, SECOM and route tools, and the render
statistics tools need the app's UI, so `s100 mcp serve` doesn't provide them.

## Example prompts

- "List the datasets loaded in the viewer and their bounding boxes."
- "What is the depth at 47.6062°N, 122.3321°W in the loaded S-102 dataset?"
- "Describe feature `LIGHTS.123` in the loaded S-201 dataset."
- "Plan a mid-channel route from the harbour entrance to the marina: create a
  route, then add waypoints that stay clear of the charted safety contour, and
  set each leg's safety contour and cross-track limits."

## Security

- SoundCharts listens on `127.0.0.1` by default, so other computers can't
  reach it. `--mcp-bind` can change the address; keep it on loopback unless you
  control the network.
- There's no authentication. Any process on your computer can connect,
  including malicious code. Turn on the server only when you trust everything
  running locally.
- Many tools change SoundCharts' state, such as loading or unloading datasets
  or moving the view, palette or time. The **Changes state** column in
  [MCP tools](#mcp-tools) lists them. None can write arbitrary files.
- The `ui_*` tools can do anything a user can do, so they're registered only
  with `--mcp-test-hooks`, which is never saved.
- `s100 mcp serve` has no network listener. Only the process that starts it can
  talk to it.

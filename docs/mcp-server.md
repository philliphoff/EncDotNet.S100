# MCP server (viewer-hosted)

The S-100 Viewer can host a Model Context Protocol (MCP) server so
external agents — `mcp-inspector`, Claude Desktop, IDE assistants —
can query the datasets you have loaded in the viewer.

The server is **off by default** and **listens on the loopback
address only**. There is no authentication; the loopback isolation is
the only protection.

## Field conventions

Every MCP tool in this server follows the same conventions for the
JSON it returns, so agents do not need to look up units, axes, or
casing per-field. Anything that deviates is called out in the
individual field's `[Description]`.

| Concern | Convention |
|---|---|
| Coordinate reference system | WGS-84 (EPSG:4326). |
| Coordinate values | Decimal degrees. Latitude range `-90..+90`; longitude range `-180..+180`. |
| Bounding boxes | Four scalars labelled `southLatitude`, `westLongitude`, `northLatitude`, `eastLongitude` — never a bare pair. |
| Times | UTC, ISO-8601. Time intervals are inclusive at both ends. |
| Distances | Metres. |
| Depths | Metres, positive down (matches S-102's vertical-datum convention). |
| Water levels | Metres, positive up (matches S-104's vertical-datum convention). |
| Current speeds | Canonical unit is metres per second (S-111 §10.2.5). Knots are provided alongside as a convenience (`speedKnots = m/s × 1.94384`). |
| Bearings | Degrees from true north, clockwise, range `0..360`. |
| JSON property naming | lower camelCase across every tool (driven by `JsonSerializerDefaults.Web`). |
| Discriminated unions | Variant carries a `$kind` discriminator string (e.g. `"depth"`, `"waterLevel"`, `"surfaceCurrent"`). |
| Errors | `isError = true` with payload `{ code, message, details }` — `code` is the stable switch key, `message` is human-readable, `details` carries the typed members of the error. |
| Dataset identifiers | A `datasetId` is an opaque **plain string** in both directions: tools accept it as a bare string argument and emit it as a bare string in results (e.g. `"id": "synth-warn-1"`), so an id read off one tool's output feeds straight back into another's input. (Legacy `{"value":"…"}` wrapped ids are still accepted on input for compatibility.) |

Every public property on a tool request, tool result, or `ToolError`
subtype carries a `[System.ComponentModel.Description]` attribute with
a single sentence stating the unit / CRS / semantics. A reflection
contract test (`AnnotationContractTests`) enforces this.

### Known unit warts

These are pre-existing inconsistencies in the underlying dataset
libraries that the MCP layer does **not** currently normalise. Agents
should consult the `units` field on a per-payload basis rather than
assume canonical units for these cases.

- **S-111 current speed.** S-111 grids encoded as data coding format 2
  (regularly-gridded time-series) surface speeds with `units = "knots"`,
  while data coding format 8 (time series at fixed stations) surface
  speeds with `units = "metres/second"`. The two values come from
  different paths in `EncDotNet.S100.Datasets.S111`. Consult the
  strongly-typed `speedMetresPerSecond` field on `SurfaceCurrentSample` /
  `SurfaceCurrentStationSample` when you need a single canonical unit.

## Enable it

1. Open the viewer.
2. Open **Settings** (gear icon in the activity bar).
3. Scroll to **MCP SERVER**.
4. Tick **Enable MCP server**.
5. Optionally set a fixed **Port**. Leave at `0` (the default) to
   have the OS pick an ephemeral port.
6. The status bar shows `MCP :{port} · {n} clients` once the server
   is listening. The tooltip on that indicator gives you the full
   endpoint URI to hand to your agent.

Untick the checkbox to stop the server; the indicator disappears and
the TCP port is released.

### Enable it from the command line (agent automation)

For headless / scripted runs an agent can enable and configure the
MCP server entirely from the CLI, without opening Settings and without
touching the user's persisted profile:

```bash
dotnet run --project src/EncDotNet.S100.Viewer -- \
  --ephemeral --mcp --mcp-port-file /tmp/run/mcp.url \
  path/to/dataset.h5
```

- `--mcp` starts the server for the run, overriding the persisted
  toggle. `--mcp-port <PORT>` and `--mcp-bind <ADDR>` configure the
  listener (any MCP flag implies `--mcp`).
- `--mcp-port-file <PATH>` writes the bound endpoint URI to a file
  once the server is listening, so an agent can discover an ephemeral
  port (`--mcp-port 0`, the default). The endpoint is also printed to
  stdout as `[MCP] listening on …`.
- A CLI-driven MCP run **never persists** the bound port back to
  `settings.json`. Combine with `--ephemeral` (throwaway settings),
  `--settings <PATH>` (alternate settings file), or `--data-dir <PATH>`
  (redirect settings **and** all disk caches under one disposable
  folder — the cleanest isolation for agent runs) to keep the real
  profile pristine and let parallel runs avoid collisions.

See the **Automation / agent control** section of the
[viewer README](../src/EncDotNet.S100.Viewer/README.md) for the full
flag list (viewport, palette, time step, screenshots, logging) and an
end-to-end walkthrough.

## Connect from `mcp-inspector`

```bash
npx @modelcontextprotocol/inspector
```

Set the transport to **Streamable HTTP**, paste the endpoint from the
viewer's status-bar tooltip (e.g. `http://127.0.0.1:54321/`), and click
**Connect**. You should see the following tools:

| Tool | Purpose |
|---|---|
| `list_datasets` | Summarises every dataset currently loaded in the viewer. |
| `list_specs` | Lists S-100 product specifications the host can read. |
| `list_time_steps` | Lists time steps for a time-varying dataset (S-104, S-111, S-421). |
| `find_at` | Returns every loaded dataset whose declared bounding box contains a lat/lon point (decimal degrees, WGS-84). Bbox-only — does not check per-cell coverage or NoData masks. For the *features* under a point (not just which datasets cover it), use `identify_features`. |
| `identify_features` | Identifies the vector features at a lat/lon point — the ECDIS cursor-pick — ranked most-specific first (point features before curves before areas; within a primitive the smaller / nearer feature wins). The feature-aware complement to `find_at`. Area features use exact point-in-polygon containment (interior-ring holes honoured); point / curve features match within `radiusMeters` (default 50, area features ignore it). Works across every vector spec (incl. S-101). Each match reports the dataset id, spec, feature id and type, geometry primitive, bounds, `containment` (`inside`/`near`), and approximate `distanceMeters`; `maxResults` (default 20) caps the list and sets `truncated`. For S-101 features carrying a `fileReference` / `TXTDSC` / `NTXTDS` attribute, each match also reports `referencedTexts` — the resolved content of the external text files (file name + text) read from the dataset's exchange set — so headless callers see the same referenced text the viewer's pick report shows. |
| `describe_feature` | Returns spec, feature type, attributes, and (for S-101) resolved geometry for a feature id in a given dataset. Numeric attributes whose Feature Catalogue declares a unit of measure (e.g. S-101 depth-valued attributes such as `depthRangeMinimumValue`, `valueOfSounding`) carry a `unit` (symbol, e.g. `"m"`) and `unitName` (e.g. `"metre"`), so the unit need not be inferred; the S-101 `geometry` block's per-point sounding `depths` array is likewise tagged with `depthUnit` (`"metres"`). Supported specs: S-101 (RCID; result carries a `geometry` block with primitive, bounding box, and coordinates), S-102 (`BathymetryCoverage[.01]`), S-104 / S-111 (`WaterLevel`/`SurfaceCurrent[.NN][.Group_KKK]` or bare station identifier), S-124 (`gml:id`), and S-129 (`gml:id` of plan / plan-area / control-point / non-navigable-area). |
| `describe_feature_type` | Introspects a spec's bundled Feature Catalogue (ISO 19110 / S-100 Part 5) — the schema-discovery counterpart to `count_features` / `query_features`. Call with just a `spec` (e.g. `S-101` or `S-124/1.5.0`) to list every feature type with its attribute count; add a `featureType` (code, name, or alias) to get that type's attribute bindings — each attribute's value type, whether it is mandatory and/or repeatable, and its enumerated listed values (set `includeListedValues=false` to omit large enumerations). Lets an agent build valid attribute predicates without a loaded dataset. The `spec` name is normalised — casing and an optional edition suffix (e.g. `s101` or `S-101/1.2.0`) are accepted and the edition is ignored. Specs with no bundled catalogue return `feature_catalogue_not_available`, whose `details.acceptedSpecs` lists the spec names that do have one. |
| `query_features` | Returns features whose geometry intersects a spatial query from loaded vector datasets — every GML vector spec plus S-101 (ISO 8211), adapted through the shared feature interface. By default intersection is bounding-box precision; set `precise=true` for **true full-geometry** intersection — point-in-polygon containment for area features (interior-ring holes honoured) and genuine segment crossing, e.g. "which features does this route leg actually cross?". For S-101 the `featureType` filter matches the feature-type acronym (e.g. `LIGHTS`, `BOYLAT`) and each `featureId` is the feature record's decimal RCID. An optional `attributes` predicate set filters on attribute values: pass a code→value map for equality (`{"categoryOfLateralMark":"1"}`) or an array of explicit predicates (`[{"attribute":"valueOfDepth","op":"ge","value":"10"},{"attribute":"objectName","op":"exists"}]`); operators are `exists`, `notExists`, `eq`, `ne`, `contains`, `startsWith`, `gt`, `ge`, `lt`, `le` and combine with logical AND. The result also carries a `typeBreakdown` (per-feature-type counts of the full all-pages match set, reflecting any attribute filter) so an agent can gauge a result before paging. |
| `count_features` | Enumerates the feature types present in loaded vector datasets and counts how many features of each type they contain — the "what kinds of features, and how many, are in this cell?" discovery question that `describe_feature` can't answer (it needs an id you don't yet have). Works across every vector spec (incl. S-101). Optional `spec`, `datasetId`, and spatial `query` filters. Each tally reports `count` and `withGeometry` (how many are spatially addressable). |
| `nearest_features` | Ranks the vector features nearest to a lat/lon point by **true geometric distance** — the distance-ranking and containment query that `find_at` (dataset-bbox membership) and `query_features` (feature-bbox intersection) can't answer. Answers "nearest light / buoy / berth to my position?" and "is this point inside any restricted area?" in one call: an area feature containing the point is returned at `distanceMeters` 0 with `containment` `inside`; every other feature reports the true distance to the nearest point on its geometry (nearest point on a segment, not just the nearest vertex) plus the `bearingDegrees` toward it. Works across every vector spec (incl. S-101). Optional `spec`, `datasetId`, `featureType`, and `maxDistanceMeters` filters; `limit` (default 10) caps the nearest-first list and sets `truncated`. |
| `search_features` | Finds vector features by name — the "where is the feature called X?" question that `query_features` (geometry-first) and `describe_feature` (needs an id you don't have yet) can't answer. Searches every place a name can live: the simple `OBJNAM` / `NOBJNM` / `objectName` attributes (incl. ISO 8211-encoded S-101) and the repeatable complex `featureName` compound's `name` / `displayName` sub-attributes (GML specs). Case-insensitive substring containment by default; set `exact` for whole-name equality or `caseSensitive` for an exact-case match. Optional `spec`, `datasetId`, and spatial `query` scope. Each match reports `matchedName` and `matchedAttribute`. Paginated. |
| `sample_coverage` | Samples a depth / water-level / current value at a lat/lon from an S-102 / S-104 / S-111 dataset. |
| `sample_coverage_along` | Samples a coverage along a polyline / great-circle path. |
| `render_to_image` *(viewer only, read-only)* | Captures the viewer's current map view as a PNG image, returned as an MCP `ImageContentBlock`. Lets an agent see exactly what the user sees for diagnosis of rendering issues (palette banding, NoData voids, augmented-geometry artefacts, missing features, etc.). When `width`/`height` are both omitted the capture is sized to the live on-screen viewport (when laid out) so the PNG matches the user's view pixel-for-pixel rather than letterboxing under the fixed 1024×768 default; the live viewport size is always echoed back as `viewportWidth`/`viewportHeight` so an agent can request a matching aspect ratio or pass those dimensions to `pick_features`. |
| `set_viewport` *(viewer only, **mutating**)* | Drives the live viewer's map navigator to a specified WGS-84 viewport — either a bbox (`south`/`west`/`north`/`east`) or a centre + web-mercator zoom (`centerLat`/`centerLon`/`zoom`). Mixing the two forms is rejected. An optional `rotation` (degrees clockwise, `0` = north-up; any finite value, normalised to `[0, 360)`) is applied on top of the frame so scripted runs can exercise the rotated-viewport render path (e.g. verifying the live label plane keeps text upright under rotation); it must accompany a frame form — to rotate in place, re-issue the same centre+zoom with the new `rotation`. The applied rotation is echoed back in the result. Antimeridian-crossing bboxes are not supported. The companion of `render_to_image`: drive the navigator with `set_viewport`, then capture with `render_to_image` for scripted measurement runs. |
| `pick_features` *(viewer only)* | The feature-aware inverse of `render_to_image`: resolves the vector features under a point on the live map. Supply EITHER a screen pixel (`x`/`y`) OR a WGS-84 geographic point (`latitude`/`longitude`); mixing or omitting both is rejected. For a pixel measured off a `render_to_image` capture, **also** pass `imageWidth`/`imageHeight` set to the `width`/`height` that tool echoed back — the pick is then resolved with the capture's exact fit geometry, making it a faithful inverse at any image size or aspect ratio. Omit `imageWidth`/`imageHeight` to interpret `x`/`y` in the live on-screen viewport's pixel space instead. The pixel is projected to a geographic point, then delegated to the same ranking as `identify_features`, so the result shape is identical (matches plus `totalMatched`/`truncated`) with an added `source` (`pixel`/`geo`) and the resolved `latitude`/`longitude`. Pixels outside the image/viewport bounds, or before the map is laid out, are rejected. Read-only by default; pass `select: true` to also show the pick on the live viewer — the resolved features populate the Object Information panel and the map draws a **pick highlight** (a screen-constant marker at the pick point plus an outline of the selected feature's geometry), exactly like a user click. The result echoes `selected: true` when this was honoured. Coverage picks (S-102/S-104/S-111) have no vector feature to outline and so are not shown. |
| `set_palette` *(viewer only, **mutating**)* | Sets the live viewer's active map palette to `Day`, `Dusk`, or `Night` (case-insensitive). Idempotent — no-op when already at the requested palette. Returns the applied and previous palette so callers can detect no-ops. Lets scripted measurement runs drive palette-change scenarios from outside the GUI. |
| `set_display_category` *(viewer only, **mutating**)* | Sets the live viewer's active ECDIS display category to `DisplayBase`, `Standard`, `OtherInformation`, or `All` (case-insensitive). Idempotent. Counterpart to the `--display-category` CLI flag, but applicable mid-session. |
| `set_display_mode` *(viewer only, **mutating**)* | Sets the live viewer's explicit per-spec display mode (S-100 Part 9 §11.7). Today only S-411 sea ice declares more than one mode: `ice-concentration` (default), `ice-sod` (stage of development), or `ice-navigational` — a **provisional** concentration-derived preview, *not* a POLARIS/RIO product. Accepts the same friendly tokens as the CLI `render --display-mode` flag, plus raw spec-native mode ids; an optional `spec` selects the product (defaults to `S-411`). Idempotent. Returns the applied and previous mode ids and whether the applied mode is `provisional`. This axis is independent of `set_display_category`. |
| `set_render_subsystem` *(viewer only, **mutating**)* | Switches the live viewer's base-plane render subsystem between `Mapsui` (the "A" arm) and `TiledScene` (the experimental tiled/async "B" arm); the `A`/`B` shorthand is also accepted (case-insensitive). Read per-render, so the change rebinds the active subsystem on the next re-render. Idempotent — no-op when already at the requested subsystem. Returns the applied and previous subsystem. Refused when `S100_RENDER_SUBSYSTEM` pins the subsystem at startup. Intended for scripted A↔B soak runs that exercise the switch teardown path from outside the GUI. |
| `set_time_step` *(viewer only, **mutating**)* | Drives the viewer's global time clock to a specific sample for time-aware datasets (S-104 / S-111 / S-411). Supply EITHER `index` (0-based integer into `list_time_steps`) OR `timestamp` (ISO-8601, snapped to the nearest sample). Returns the resolved index and snapped timestamp. Counterpart to the `--time-step` CLI flag, but applicable mid-session. |
| `set_own_ship` *(viewer only, **mutating**)* | Positions and steers the simulated own-ship. Any subset of `lat`+`lon` (WGS-84 decimal degrees, supplied together), `cog` (course over ground, degrees true `[0, 360)`), `sog` (speed over ground, m/s `>= 0`), `heading` (gyro heading, degrees true — only applied together with `lat`/`lon`), and `hold` (`true` stops the vessel, `false` resumes) may be supplied; at least one actionable field is required. Works independently of the own-ship overlay's visibility, so it can pre-position the vessel before enabling the overlay or capturing a screenshot. Counterpart to the `--own-ship-pos` / `--own-ship-cog` / `--own-ship-sog` CLI flags, but applicable mid-session. |
| `list_panels` *(viewer only, read-only)* | Lists the viewer's activity panels (the tabs in the left / right / bottom docks) and their current visibility, so an agent can drive and verify the non-render UX (action / report / timeline panels), not just the map. Each panel reports `id`, `title`, `dock` (`Left`\|`Right`\|`Bottom`), `available` (registered in the activity bar right now — a few panels are conditional, e.g. `Vessels` only while the AIS overlay is enabled and `Helm` only while own-ship tracking is enabled), `selected` (the active tab in its dock), `dockOpen` (its dock is expanded), and `showing` (actually visible = `available && selected && dockOpen`). Read-only — snapshots the activity bar without changing it. Call it to discover the valid panel ids for `set_panel` and again afterwards to confirm a show / hide took effect. |
| `set_panel` *(viewer only, **mutating**)* | Shows or hides one of the viewer's activity panels (a tab in the left / right / bottom dock). `panel` is a panel id from `list_panels` (case-insensitive), e.g. `Datasets`, `LayerStack`, `PickReport`, `Timeline`. `visible` defaults to `true`: showing selects the panel's tab and opens its dock; hiding (`false`) closes the panel's dock when that panel is the one currently shown there (otherwise a no-op). Idempotent — a panel already in the requested state is left untouched. Returns the resulting `showing` state, the `previousShowing` state, and whether it `changed`. Rejects an unknown id (`panel_not_found`) and an attempt to show a panel that is not currently available (`panel_unavailable`, e.g. `Vessels` while the AIS overlay is disabled). Lets scripted runs drive non-render UX from outside the GUI so a code / run / verify loop can assert panel state. |
| `capture_app_screenshot` *(viewer only, read-only)* | Captures the **whole viewer application window** — the chart plus the surrounding chrome (activity docks, panels, timeline, status bar) — as a PNG, returned as an MCP `ImageContentBlock` alongside a JSON metadata block (`imageFormat`, `byteLength`, and the PNG-decoded `width`/`height`). Complements `render_to_image`, which captures only the map surface: use this to *see* non-render UX (e.g. to visually confirm `set_panel` opened a panel) rather than inferring it from `list_panels`. Read-only and side-effect free; the window is not mutated. Returns `window_not_ready` when the main window has not been attached yet (or has no on-screen size). |
| `await_render_idle` *(viewer only, read-only)* | Blocks until the live map settles — no completed paint, graphics-refresh request, or active layer fetch for a continuous quiet period — or until a timeout elapses (`quietPeriodMs` default 250, clamped `[0, 10000]`; `timeoutMs` default 5000, clamped `[50, 120000]`). Call it between `set_viewport` and `render_to_image` so the screenshot reflects a settled view instead of racing the render pass. Always waits at least the quiet period and measures the on-screen `InstrumentedMapControl` paint loop, not the offscreen `render_to_image` clone. A layer's busy flag only holds the wait open while that layer keeps emitting render activity; a stale busy flag that never clears (with no paint/refresh for the quiet period) is ignored, so a settled map reports `wentIdle` rather than being forced to `timedOut`. Returns `wentIdle`, `timedOut`, `waitedMs`, and `paintsObserved`. |
| `get_render_stats` *(viewer only, read-only)* | Reports the cost of the most recently completed on-screen map paint: wall-clock `frameDurationMs`, `intervalMs` since the previous paint, `totalDrawCalls`, and a per-style breakdown (`style`, `calls`, `durationMs`, ordered by descending duration). Also returns a rolling **`window`** object aggregating the most recent paints (up to 4096) so transient expensive frames are not missed once the view settles to a cheap cached repaint: `count`, `firstSequence`/`lastSequence`, and max / mean / p95 for both wall-clock frame time (`frameMaxMs`/`frameMeanMs`/`frameP95Ms`) and summed `VectorStyle` time (`vectorMaxMs`/`vectorMeanMs`/`vectorP95Ms`), plus `maxTotalDrawCalls`. `window.slowestFrame` identifies the maximum-duration paint with its sequence, completion timestamp, whole-frame duration, summed instrumented-style duration, uninstrumented remainder, and draw-call count so an outlier can be correlated with trace activity. Pass `resetWindow: true` to clear the window after reading (the canonical pattern: read+reset before an interaction burst, read again after to capture just that burst). Use it to measure rendering performance across pan / zoom, palette, or time-step changes. Describes the live map paint, not the offscreen `render_to_image` clone; returns `hasData = false` when no paint has occurred yet (the `window` is still reported). Pair with `await_render_idle` so the reported latest paint reflects a settled view. |
| `open_dataset` *(viewer only, **mutating**)* | Loads a dataset into the live viewer using its existing open code path, so agents can measure the load hot path. `path` is a single file (S-101 `.000`, HDF5 `.h5`, GML, etc.) OR an exchange set (a folder containing `CATALOG.XML`, or a `.zip` of one); the kind is auto-detected. `spec` optionally forces a product-spec hint (e.g. `S-102`) for single-file loads. Returns the resulting catalog id(s), `spec`, bounding box (`southLatitude`/`westLongitude`/`northLatitude`/`eastLongitude`), `count`, `loadDurationMs`, and `timedOut` (exchange-set quiescence). |
| `close_dataset` *(viewer only, **mutating**)* | Unloads a currently-loaded dataset from the live viewer by its catalog `id` (as returned by `list_datasets` / `open_dataset`), using the viewer's existing close code path so agents can measure the unload hot path. An unknown / already-removed id resolves gracefully as a non-error result with `removed = false`. Returns `removed`, `count`, and `removedDatasets` (`id` + `spec`). |
| `close_all_datasets` *(viewer only, **mutating**)* | Unloads every currently-loaded dataset from the live viewer through the same close path used by `close_dataset`. Useful for retention loops that repeatedly load → render → unload without restarting the viewer process. Returns `removed`, `count`, and `removedDatasets` (`id` + `spec`). |
| `create_route` *(viewer only, **mutating**)* | Creates a new, empty editable route in the live viewer's route collection and makes it the active route. Optional `name` and `id` (a GUID is generated when `id` is omitted; ids must be unique). Returns the new route's full state (see `get_route`). Add waypoints with `append_waypoint`. |
| `list_routes` *(viewer only, read-only)* | Lists every editable route with its `routeId`, `name`, `waypointCount`, `legCount`, `totalDistanceNm`, and `isActive`, plus the collection's `activeRouteId`. |
| `get_route` *(viewer only, read-only)* | Returns the full state of one route: `routeId`, `name`, `isActive`, `info` (name/author/description/ports/validity/vessel), `waypoints` (`index`, `lat`, `lon`, optional `number`/`name`/`fixed`/`turnRadiusNm`), `legs` (`index`, `geometryType`, computed `distanceNm`/`initialBearingDegrees`, plus the S-421 navigational envelope), and `totalDistanceNm`. Omit `routeId` to read the active route. |
| `delete_route` *(viewer only, **mutating**)* | Removes a route from the collection. Omit `routeId` to delete the active route. Returns `routeId`, `deleted`, and the new `activeRouteId`. |
| `append_waypoint` *(viewer only, **mutating**)* | Appends a waypoint (`lat`/`lon`, WGS-84 decimal degrees) to the end of a route, with optional `number`/`name`/`fixed`/`turnRadiusNm`. Omit `routeId` to use the active route. Returns the route's full updated state. |
| `insert_waypoint` *(viewer only, **mutating**)* | Inserts a waypoint at `index` (in `[0, waypointCount]`; `0` prepends, `waypointCount` appends), splitting the affected leg. Same optional metadata as `append_waypoint`. Omit `routeId` to use the active route. Returns the route's full updated state. |
| `move_waypoint` *(viewer only, **mutating**)* | Moves the waypoint at `index` (in `[0, waypointCount)`) to a new `lat`/`lon`. Omit `routeId` to use the active route. Returns the route's full updated state. |
| `delete_waypoint` *(viewer only, **mutating**)* | Removes the waypoint at `index` (in `[0, waypointCount)`), merging the adjacent legs. Omit `routeId` to use the active route. Returns the route's full updated state. |
| `set_leg_attributes` *(viewer only, **mutating**)* | Updates one leg (`legIndex`, in `[0, legCount)`): its `geometryType` (`loxodrome`\|`geodesic`) and/or navigational envelope (cross-track / channel limits, safety contour & depth, SOG/STW min & max, draft, static & dynamic UKC, safety margin, note — all metres/knots per S-421). All attributes optional; supplied values overwrite, omitted values are unchanged. Omit `routeId` to use the active route. Returns the route's full updated state. |
| `set_route_info` *(viewer only, **mutating**)* | Updates route metadata (`name`, `author`, `description`, `departurePortId`, `arrivalPortId`, `validityStart`/`validityEnd`) and vessel particulars (`vesselName`/`vesselMmsi`/`vesselImo`/`vesselCallsign`/`vesselLengthMeters`/`vesselBeamMeters`; supplying any vessel field creates the vessel block). All fields optional; supplied values overwrite. Omit `routeId` to use the active route. Returns the route's full updated state. |

> The **(viewer only)** tags above scope each tool to *this* server — the
> surface a running viewer instance exposes. Most of the session tools are in
> fact the shared implementation and are equally available from the headless
> CLI host (`s100 mcp serve`); only the UI-bound tools are truly
> viewer-specific. See
> [Shared vs host-specific tool implementations](#shared-vs-host-specific-tool-implementations)
> below for the exact split.

### Read-only vs mutating tools

Tools fall into two groups:

* **Read-only** — never mutate viewer state. Safe to call from any
  agent at any time. Examples: `list_datasets`, `find_at`,
  `identify_features`, `query_features`, `count_features`,
  `nearest_features`,
  `search_features`,
  `describe_feature_type`, `sample_coverage`, `render_to_image` (which
  snapshots from a clone of the live `Map`), `pick_features` without
  `select` (which projects a pixel through the live viewport without
  changing it),
  `await_render_idle`,
  `get_render_stats` (which observe the live render loop without
  changing it), `list_routes`, and `get_route` (which snapshot the
  editable route collection without changing it), and `list_panels`
  (which snapshots the activity bar without changing it), and
  `capture_app_screenshot` (which snapshots the whole application window
  without changing it).
* **Mutating** — modify the live viewer's state (navigator, palette,
  time step, loaded datasets, routes, etc.). Use only when you intend to
  drive the viewer's UI from outside. Examples: `set_viewport`,
  `pick_features` with `select: true` (which publishes the pick to the
  Object Information panel and draws the pick highlight),
  `set_palette`, `set_display_category`, `set_display_mode`,
  `set_render_subsystem`,
  `set_time_step`,
  `set_own_ship`, `open_dataset`, `close_dataset`,
  `close_all_datasets` (which
  load / unload datasets through the viewer's own open / close code
  path), the route-editing family `create_route`, `delete_route`,
  `append_waypoint`, `insert_waypoint`, `move_waypoint`,
  `delete_waypoint`, `set_leg_attributes`, and `set_route_info`, and
  `set_panel` (which shows / hides the viewer's activity panels).

Tool descriptions in the registered MCP catalogue identify each tool
as one or the other; this table is the canonical reference.

### Shared vs host-specific tool implementations

Most of these tools share one renderer-neutral implementation. The tool
logic and its capability seams live in `EncDotNet.S100.Mcp.Tools`, and
`S100MutableTools` (in `EncDotNet.S100.Mcp`) assembles them for a host.
Both the desktop viewer and the headless CLI session provide the
presentation, time, image-render, and dataset-catalog capabilities
(`IPresentationController`, `ITimeController`, `IImageRenderer`,
`IMutableDatasetCatalog`) and so run the *same* tool code: `set_palette`,
`set_display_category`, `set_display_mode`, `set_time_step`,
`render_to_image` (read-only, but part of the same session tool set),
`open_dataset`, `close_dataset`, and `close_all_datasets`. The viewer
adapts its own services onto the seams (see `Services/McpCapabilities/`).

A few tools stay host-specific where the hosts genuinely diverge, rather
than being forced onto a shape that would fit neither well:

* `set_viewport` — the viewer drives a **live, rotatable** Mapsui map and
  accepts a web-mercator **zoom** level; the CLI renders a **headless,
  north-up** composite addressed by **scale denominator**. The two keep
  separate implementations (the viewer's over `IMapViewportController`,
  the CLI's over `IViewportController`) so the viewer retains arbitrary
  rotation and zoom-level input.
* `pick_features`, `set_render_subsystem`, `capture_app_screenshot`,
  `set_own_ship`, panels, routes, and the render-observability tools —
  these need the live viewer UI and have no headless analogue.

### Image content blocks (`render_to_image`)

`render_to_image` is the first tool in this codebase to return non-text
MCP content. (`capture_app_screenshot` returns the same `ImageContentBlock`
+ metadata shape for the whole application window.) The response payload
is a `CallToolResult` whose `Content` array contains, in order:

1. an `ImageContentBlock` carrying base64-encoded PNG bytes with
   `mimeType: "image/png"` — MCP clients render this inline;
2. a `TextContentBlock` carrying a small JSON metadata envelope
   (`width`, `height`, `pixelDensity`, `imageFormat`, `byteLength`,
   optional `viewportWidth`/`viewportHeight`, optional `notes`) so
   agents still get a structured echo of the rendered dimensions and
   the live viewport size.

When the caller omits both `width` and `height`, the capture is sized
to the live on-screen viewport (when it has been laid out) so the PNG
matches the user's view pixel-for-pixel — the fixed 1024×768 default
otherwise letterboxes the content under `MBoxFit.Fit` against a
differently shaped viewport. A partial request (only one dimension)
keeps the static fallback for the omitted side to avoid an arbitrary
aspect ratio. The live viewport size is reported as
`viewportWidth`/`viewportHeight` on every successful capture (omitted
only when the viewport is not yet laid out), so an agent can request a
matching aspect ratio explicitly or pass those values as the
`imageWidth`/`imageHeight` inputs to `pick_features`.

The snapshot is captured from a **clone** of the live Mapsui `Map`
that shares the layer collection but owns its own navigator. The
live map control is never mutated, so taking a snapshot does not
disturb the user's view or trigger a redraw on screen. Viewport,
palette, time step, and loaded datasets reflect the user's current
view exactly.

`render_to_image` is one of the **shared session tools** (read-only — it
snapshots a clone of the live map without mutating it): its tool logic
lives in `EncDotNet.S100.Mcp.Tools` over the `IImageRenderer` capability
seam and is assembled by `S100MutableTools` (in `EncDotNet.S100.Mcp`),
with each host supplying the renderer. The desktop viewer backs it with a
snapshot of a clone of the live Mapsui `Map` (through a
`ViewerImageRenderer` adapter); the headless CLI backs it with its Skia
composite pipeline. Its inverse, `pick_features`, is viewer-only — it
needs the live navigator to project a screen pixel back to a geographic
point, and has no headless analogue.

## Sample agent prompts

> "List the datasets loaded in the viewer and their bounding boxes."
>
> "What is the depth at 47.6062°N, 122.3321°W in the loaded S-102
> dataset?"
>
> "Describe feature `LIGHTS.123` in the loaded S-201 dataset."
>
> "Plan a mid-channel route from the harbour entrance to the marina:
> create a route, then append waypoints staying clear of the charted
> safety contour, and set each leg's safety contour and cross-track
> limits."

### Route editing workflow

The route family lets an agent close the *create → inspect → refine*
loop against loaded datasets. A typical sequence:

1. `create_route` (optionally `name`/`id`) → becomes the active route.
2. `append_waypoint` / `insert_waypoint` to lay down the geometry; reason
   about placement with the read-only query tools (`sample_coverage`,
   `nearest_features`, `find_at`) along each leg.
3. `move_waypoint` / `delete_waypoint` to refine.
4. `set_leg_attributes` (geometry type + S-421 navigational envelope) and
   `set_route_info` (metadata + vessel particulars).
5. `get_route` / `list_routes` to read back the result.

Edits are applied to the same persistent route collection the viewer's
**Routes** panel and route overlay display, so changes appear live in the
GUI. Most route tools default to the **active** route when `routeId` is
omitted. Waypoints are addressed by zero-based `index`; legs by zero-based
`legIndex` (leg `i` joins waypoint `i` to waypoint `i+1`). The fields
mirror the in-repo S-421 model so a route projects onto S-421 GML with a
near-mechanical mapping.

## Security notes

- The server binds to `127.0.0.1` only. Other machines on your LAN
  **cannot** reach it.
- There is no auth. Any local process on the machine can connect,
  including malicious code. Only enable MCP when you trust everything
  running locally.
- Most tools are read-only, but some **mutate viewer state** (loading
  or unloading datasets, driving the viewport, palette, or time step) —
  see the read-only vs mutating breakdown above. None can write
  arbitrary files.

## Disable from the UI

Untick **Enable MCP server** in Settings. The server stops and the
port is released immediately.

## Troubleshooting

- **Port already in use.** Choose port `0` (auto) or pick a free port
  manually. The viewer logs the bind failure to its standard
  diagnostics output.
- **Agent times out.** Re-check the endpoint URI from the status bar
  tooltip — the port changes when MCP is restarted with port `0`.
- **No datasets show up.** The MCP server only sees datasets that are
  currently loaded in the viewer. Load some data first.

## Hosting outside the viewer

The viewer is one host; the underlying library
(`EncDotNet.S100.Mcp`) is UI-agnostic and can be embedded in CLI
tools or background services. See
[`src/EncDotNet.S100.Mcp/README.md`](../src/EncDotNet.S100.Mcp/README.md)
for the embedding API.

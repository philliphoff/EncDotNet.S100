# MCP server (viewer-hosted)

The viewer (SoundCharts) can host a Model Context Protocol (MCP) server so
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
| Current speeds | S-111 encodes `surfaceCurrentSpeed` in knots for every data coding format. Samples report both `speedKnots` (as encoded) and `speedMetresPerSecond` (`kn × 0.514444`). |
| Bearings | Degrees from true north, clockwise, range `0..360`. |
| JSON property naming | lower camelCase across every tool (driven by `JsonSerializerDefaults.Web`). |
| Discriminated unions | Variant carries a `$kind` discriminator string (e.g. `"depth"`, `"waterLevel"`, `"surfaceCurrent"`). |
| Errors | `isError = true` with payload `{ code, message, details }` — `code` is the stable switch key, `message` is human-readable, `details` carries the typed members of the error. |
| Dataset identifiers | A `datasetId` is an opaque **plain string** in both directions: tools accept it as a bare string argument and emit it as a bare string in results (e.g. `"id": "synth-warn-1"`), so an id read off one tool's output feeds straight back into another's input. (Legacy `{"value":"…"}` wrapped ids are still accepted on input for compatibility.) |

Every public property on a tool request, tool result, or `ToolError`
subtype carries a `[System.ComponentModel.Description]` attribute with
a single sentence stating the unit / CRS / semantics. A reflection
contract test (`AnnotationContractTests`) enforces this.

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
- `--mcp-test-hooks` also registers test-only tools: `set_test_clock`,
  which moves or freezes the viewer's notion of now, and the `ui_*` UI
  automation tools, which can click, type into and select anything in
  the viewer's UI. Use it only for scripted testing; it implies `--mcp`.
  The flag is never saved, so turning MCP on in Settings never offers
  these tools.
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
| `list_datasets` | Summarises every dataset currently loaded in the viewer. Each entry's `spec` is the dataset's product identity: a legacy S-57 cell reports `S-57` (with its real cell bounds) even though it is translated to, and queried through, the S-101 model, so the vector query tools (`identify_features`, `query_features`, `count_features`, `search_features`, …) work on S-57 cells too. |
| `list_specs` | Lists S-100 product specifications the host can read. |
| `list_time_steps` | Lists time steps for a time-varying dataset (S-104, S-111, S-421). |
| `find_at` | Returns every loaded dataset whose declared bounding box contains a lat/lon point (decimal degrees, WGS-84). Bbox-only — does not check per-cell coverage or NoData masks. For the *features* under a point (not just which datasets cover it), use `identify_features`. |
| `identify_features` | Identifies the vector features at a lat/lon point — the ECDIS cursor-pick — ranked most-specific first (point features before curves before areas; within a primitive the smaller / nearer feature wins). The feature-aware complement to `find_at`. Area features use exact point-in-polygon containment (interior-ring holes honoured); point / curve features match within `radiusMeters` (default 50, area features ignore it). Works across every vector spec (incl. S-101). `longitude` may be anywhere in `[-540, 540]`: data kept in a 0…360 or other continuous longitude frame (e.g. the NWS Alaska S-411, 175°E–225°E) is matched at its own longitude and at the same place one world east or west (210 and −150 find the same feature), as the viewer draws it there. Each match reports the dataset id, spec, feature id and type, geometry primitive, bounds, `containment` (`inside`/`near`), and approximate `distanceMeters`; `maxResults` (default 20) caps the list and sets `truncated`. For S-101 features carrying a `fileReference` / `TXTDSC` / `NTXTDS` attribute, each match also reports `referencedTexts` — the resolved content of the external text files (file name + text) read from the dataset's exchange set — so headless callers see the same referenced text the viewer's pick report shows. |
| `describe_feature` | Returns spec, feature type, attributes, and (for S-101) resolved geometry for a feature id in a given dataset. Numeric attributes whose Feature Catalogue declares a unit of measure (e.g. S-101 depth-valued attributes such as `depthRangeMinimumValue`, `valueOfSounding`) carry a `unit` (symbol, e.g. `"m"`) and `unitName` (e.g. `"metre"`), so the unit need not be inferred; the S-101 `geometry` block's per-point sounding `depths` array is likewise tagged with `depthUnit` (`"metres"`). Supported specs: S-101, S-401 and S-57 cells (translated to the S-101 model on load; RCID; result carries a `geometry` block with primitive, bounding box, and coordinates), S-102 (`BathymetryCoverage[.01]`; `boundingBox` is WGS-84 degrees, while `gridMetadata` and `nativeExtent` stay in the grid's native CRS, which is metres for a UTM tile), S-104 / S-111 (`WaterLevel`/`SurfaceCurrent[.NN][.Group_KKK]` or bare station identifier), S-124 (`gml:id`), and S-129 (`gml:id` of plan / plan-area / control-point / non-navigable-area). |
| `describe_feature_type` | Introspects a spec's bundled Feature Catalogue (ISO 19110 / S-100 Part 5) — the schema-discovery counterpart to `count_features` / `query_features`. Call with just a `spec` (e.g. `S-101` or `S-124/1.5.0`) to list every feature type with its attribute count; add a `featureType` (code, name, or alias) to get that type's attribute bindings — each attribute's value type, whether it is mandatory and/or repeatable, and its enumerated listed values (set `includeListedValues=false` to omit large enumerations). Lets an agent build valid attribute predicates without a loaded dataset. The `spec` name is normalised — casing and an optional edition suffix (e.g. `s101` or `S-101/1.2.0`) are accepted and the edition is ignored. Specs with no bundled catalogue return `feature_catalogue_not_available`, whose `details.acceptedSpecs` lists the spec names that do have one. |
| `query_features` | Returns features whose geometry intersects a spatial query from loaded vector datasets — every GML vector spec plus the ISO 8211-encoded S-101 and S-401 (inland ENC), adapted through the shared feature interface. By default intersection is bounding-box precision; set `precise=true` for **true full-geometry** intersection — point-in-polygon containment for area features (interior-ring holes honoured) and genuine segment crossing, e.g. "which features does this route leg actually cross?". For S-101 the `featureType` filter matches the feature-type acronym (e.g. `LIGHTS`, `BOYLAT`) and each `featureId` is the feature record's decimal RCID. An optional `attributes` predicate set filters on attribute values: pass a code→value map for equality (`{"categoryOfLateralMark":"1"}`) or an array of explicit predicates (`[{"attribute":"valueOfDepth","op":"ge","value":"10"},{"attribute":"objectName","op":"exists"}]`); operators are `exists`, `notExists`, `eq`, `ne`, `contains`, `startsWith`, `gt`, `ge`, `lt`, `le` and combine with logical AND. The result also carries a `typeBreakdown` (per-feature-type counts of the full all-pages match set, reflecting any attribute filter) so an agent can gauge a result before paging. |
| `count_features` | Enumerates the feature types present in loaded vector datasets and counts how many features of each type they contain — the "what kinds of features, and how many, are in this cell?" discovery question that `describe_feature` can't answer (it needs an id you don't yet have). Works across every vector spec (incl. S-101). Optional `spec`, `datasetId`, and spatial `query` filters. Each tally reports `count` and `withGeometry` (how many are spatially addressable). |
| `nearest_features` | Ranks the vector features nearest to a lat/lon point by **true geometric distance** — the distance-ranking and containment query that `find_at` (dataset-bbox membership) and `query_features` (feature-bbox intersection) can't answer. Answers "nearest light / buoy / berth to my position?" and "is this point inside any restricted area?" in one call: an area feature containing the point is returned at `distanceMeters` 0 with `containment` `inside`; every other feature reports the true distance to the nearest point on its geometry (nearest point on a segment, not just the nearest vertex) plus the `bearingDegrees` toward it. Works across every vector spec (incl. S-101). Optional `spec`, `datasetId`, `featureType`, and `maxDistanceMeters` filters; `limit` (default 10) caps the nearest-first list and sets `truncated`. |
| `search_features` | Finds vector features by name — the "where is the feature called X?" question that `query_features` (geometry-first) and `describe_feature` (needs an id you don't have yet) can't answer. Searches every place a name can live: the simple `OBJNAM` / `NOBJNM` / `objectName` attributes (incl. ISO 8211-encoded S-101) and the repeatable complex `featureName` compound's `name` / `displayName` sub-attributes (GML specs). Case-insensitive substring containment by default; set `exact` for whole-name equality or `caseSensitive` for an exact-case match. Optional `spec`, `datasetId`, and spatial `query` scope. Each match reports `matchedName` and `matchedAttribute`. Paginated. |
| `sample_coverage` | Samples a depth / water-level / current value at a lat/lon from an S-102 / S-104 / S-111 dataset. For S-104 / S-111, a `time` (or `times` envelope) picks the nearest time step of a dataset whose range contains it. A time outside every covering dataset's range returns `time_out_of_range` by default rather than a value from the wrong time; see [Times outside the data](#times-outside-the-data). |
| `sample_coverage_along` | Samples a coverage at each vertex of a polyline. Time handling matches `sample_coverage` for each vertex. A vertex with no value has a `null` `result` and an `error` (`code`, `message`). When no vertex has a value because of the requested time, the call returns `time_out_of_range`. |
| `render_to_image` *(viewer only, read-only)* | Captures the viewer's current map view as a PNG image, returned as an MCP `ImageContentBlock`. Lets an agent see exactly what the user sees for diagnosis of rendering issues (palette banding, NoData voids, augmented-geometry artefacts, missing features, etc.). When `width`/`height` are both omitted the capture is sized to the live on-screen viewport (when laid out) so the PNG matches the user's view pixel-for-pixel rather than letterboxing under the fixed 1024×768 default; the live viewport size is always echoed back as `viewportWidth`/`viewportHeight` so an agent can request a matching aspect ratio or pass those dimensions to `pick_features`. |
| `set_viewport` *(viewer only, **mutating**)* | Drives the live viewer's map navigator to a specified WGS-84 viewport — exactly one of a bbox (`south`/`west`/`north`/`east`), a centre + web-mercator zoom (`centerLat`/`centerLon`/`zoom`), or a centre + map scale (`centerLat`/`centerLon`/`scaleDenominator`, e.g. `50000` for 1:50 000). Mixing forms, including `zoom` with `scaleDenominator`, is rejected. `scaleDenominator` is converted at the centre latitude with the same 0.28 mm pixel the status bar uses, so the status bar reads it back. An optional `rotation` (degrees clockwise, `0` = north-up; any finite value, normalised to `[0, 360)`) is applied on top of the frame so scripted runs can exercise the rotated-viewport render path (e.g. verifying the live label plane keeps text upright under rotation); it must accompany a frame form — to rotate in place, re-issue the same centre form with the new `rotation`. The result echoes the applied rotation and `scaleDenominator` — the unrounded scale read back from the live map after the change, so it reflects the viewer's zoom limits (1:1 000 at the viewport's own latitude; a zoom-out floor of 1:500 000 000 at the equator, a fixed Mercator resolution that reads 1:500 000 000 × cos(latitude) elsewhere) and, for a bbox, the fit to the control (omitted for a bbox before the map is laid out). **Longitudes past ±180°:** the viewer draws the basemap and chart data one world copy either side of the standard world, so longitudes are accepted in `[-540, 540]` and frame the matching copy. A dataset kept in a continuous frame across the antimeridian (the NWS Alaska S-411, `open_dataset` bounds 175…225) is framed at its own longitudes, e.g. `{south: 69.5, west: 190, north: 74.5, east: 220}` or `{centerLat: 72, centerLon: 205, scaleDenominator: 10000000}` for the Beaufort Sea. The same place in the standard world (`west: -170, east: -140`) shows the same data. A bbox with `west > east` crosses the antimeridian: its east edge is taken one world east (`west: 170, east: -140` frames 170…220); a box may be at most one world wide. The echo reports the framed box in that continuous frame (`west < east`, past ±180° where it was framed) and the live map centre as `centerLat`/`centerLon`. The map keeps its centre within the loaded data and the basemap's world, so a far copy with nothing loaded there may be pulled back; the echoed centre says where the map settled. The companion of `render_to_image`: drive the navigator with `set_viewport`, then capture with `render_to_image` for scripted measurement runs. |
| `pick_features` *(viewer only)* | The feature-aware inverse of `render_to_image`: resolves the vector features under a point on the live map. Supply EITHER a screen pixel (`x`/`y`) OR a WGS-84 geographic point (`latitude`/`longitude`); mixing or omitting both is rejected. For a pixel measured off a `render_to_image` capture, **also** pass `imageWidth`/`imageHeight` set to the `width`/`height` that tool echoed back — the pick is then resolved with the capture's exact fit geometry, making it a faithful inverse at any image size or aspect ratio. Omit `imageWidth`/`imageHeight` to interpret `x`/`y` in the live on-screen viewport's pixel space instead. The pixel is projected to a geographic point, then delegated to the same ranking as `identify_features`, so the result shape is identical (matches plus `totalMatched`/`truncated`) with an added `source` (`pixel`/`geo`) and the resolved `latitude`/`longitude`. Pixels outside the image/viewport bounds, or before the map is laid out, are rejected. Read-only by default; pass `select: true` to also show the pick on the live viewer — the resolved features populate the Object Information panel and the map draws a **pick highlight** (a screen-constant marker at the pick point plus an outline of the selected feature's geometry), exactly like a user click. The result echoes `selected: true` when this was honoured. Coverage picks (S-102/S-104/S-111) have no vector feature to outline and so are not shown. |
| `set_palette` *(viewer only, **mutating**)* | Sets the live viewer's active map palette to `Day`, `Dusk`, or `Night` (case-insensitive). Idempotent — no-op when already at the requested palette. Returns the applied and previous palette so callers can detect no-ops. Lets scripted measurement runs drive palette-change scenarios from outside the GUI. |
| `set_display_category` *(viewer only, **mutating**)* | Sets the live viewer's active ECDIS display category to `DisplayBase`, `Standard`, `OtherInformation`, or `All` (case-insensitive). Idempotent. Counterpart to the `--display-category` CLI flag, but applicable mid-session. |
| `set_display_mode` *(viewer only, **mutating**)* | Sets the live viewer's explicit per-spec display mode (S-100 Part 9 §11.7). Today only S-411 sea ice declares more than one mode: `ice-concentration` (default), `ice-sod` (stage of development), or `ice-navigational` — a **provisional** concentration-derived preview, *not* a POLARIS/RIO product. Accepts the same friendly tokens as the CLI `render --display-mode` flag, plus raw spec-native mode ids; an optional `spec` selects the product (defaults to `S-411`). Idempotent. Returns the applied and previous mode ids and whether the applied mode is `provisional`. This axis is independent of `set_display_category`. |
| `set_time_step` *(viewer only, **mutating**)* | Drives the viewer's global time clock to a specific sample for time-aware datasets (S-104 / S-111 / S-411). Supply EITHER `index` (0-based integer into `list_time_steps`) OR `timestamp` (ISO-8601, snapped to the nearest sample). Returns the resolved index and snapped timestamp. Counterpart to the `--time-step` CLI flag, but applicable mid-session. |
| `set_own_ship` *(viewer only, **mutating**)* | Positions and steers the simulated own-ship. Any subset of `lat`+`lon` (WGS-84 decimal degrees, supplied together), `cog` (course over ground, degrees true `[0, 360)`), `sog` (speed over ground, m/s `>= 0`), `heading` (gyro heading, degrees true — only applied together with `lat`/`lon`), and `hold` (`true` stops the vessel, `false` resumes) may be supplied; at least one actionable field is required. Works independently of the own-ship overlay's visibility, so it can pre-position the vessel before enabling the overlay or capturing a screenshot. Counterpart to the `--own-ship-pos` / `--own-ship-cog` / `--own-ship-sog` CLI flags, but applicable mid-session. |
| `list_panels` *(viewer only, read-only)* | Lists the viewer's activity panels (the tabs in the left / right / bottom docks) and their current visibility, so an agent can drive and verify the non-render UX (action / report / timeline panels), not just the map. Each panel reports `id`, `title`, `dock` (`Left`\|`Right`\|`Bottom`), `available` (registered in the activity bar right now — a few panels are conditional, e.g. `Vessels` only while the AIS overlay is enabled and `Helm` only while own-ship tracking is enabled), `selected` (the active tab in its dock), `dockOpen` (its dock is expanded), and `showing` (actually visible = `available && selected && dockOpen`). Read-only — snapshots the activity bar without changing it. Call it to discover the valid panel ids for `set_panel` and again afterwards to confirm a show / hide took effect. |
| `set_panel` *(viewer only, **mutating**)* | Shows or hides one of the viewer's activity panels (a tab in the left / right / bottom dock). `panel` is a panel id from `list_panels` (case-insensitive), e.g. `Datasets`, `LayerStack`, `PickReport`, `Timeline`. `visible` defaults to `true`: showing selects the panel's tab and opens its dock; hiding (`false`) closes the panel's dock when that panel is the one currently shown there (otherwise a no-op). Idempotent — a panel already in the requested state is left untouched. Returns the resulting `showing` state, the `previousShowing` state, and whether it `changed`. Rejects an unknown id (`panel_not_found`) and an attempt to show a panel that is not currently available (`panel_unavailable`, e.g. `Vessels` while the AIS overlay is disabled). Lets scripted runs drive non-render UX from outside the GUI so a code / run / verify loop can assert panel state. |
| `capture_app_screenshot` *(viewer only, read-only)* | Captures the **whole viewer application window** — the chart plus the surrounding chrome (activity docks, panels, timeline, status bar) — as a PNG, returned as an MCP `ImageContentBlock` alongside a JSON metadata block (`imageFormat`, `byteLength`, and the PNG-decoded `width`/`height`). Complements `render_to_image`, which captures only the map surface: use this to *see* non-render UX (e.g. to visually confirm `set_panel` opened a panel) rather than inferring it from `list_panels`. Pass an optional `scale` (device pixels per logical pixel, clamped to `[0.5, 3]`, default `1`) for a sharper image, e.g. `2` for a Retina-quality capture. Read-only and side-effect free; the window is not mutated. Returns `window_not_ready` when the main window has not been attached yet (or has no on-screen size). |
| `get_timeline_state` *(viewer only, read-only)* | Reads the Timeline as the user sees it: `mode` (`live` while the view time follows now, else `pinned`), `now`, `viewTime`, the loaded range (`minimum`/`maximum`, `sampleCount`), `coverage` windows (gaps lie between them), forecast `runs`, `nowInCoverage`, `forecastEnded`, the displayed `readout`, `offset` and `summary`, the status `message` and its `messageAction`, the axis `window`, its `preset`, the `step` and its `stepDriver`, the collapsed `gaps` (with their labelled `length`), and per time-aware layer (`layers`) its `drawnTime` (null when it has no data near the view time and hides) with its `previousSample`/`nextSample`, its `time` as its row in the Datasets list shows it (`08:00Z · T+20 h`, `no data · last 18:00Z, 6 h earlier`, `drawing…`), and whether it is `hidden` or `drawing`. `inMapView` says whether the In map view filter is on, `layout` is `lanes` or `strip`, `showOnline` whether lanes also show what the Library knows but has not loaded, and `lanes` lists each lane (`id`, `label`, product `group`, whether it is `listed` or folded outside the map view, whether its footprint is `inMapView`, `expired`, its `time`, whether it is a `library` lane of data not loaded, `newRun`, and its Library `windows` — item id, `online`/`on_disk`/`loaded`, start, end, run — whose ids work with `library_action`). While the map draws a new time the `message` reads `Drawing … · N of M layers ready`. Use it to explain why a layer isn't drawn. Layer times settle after the map's time refresh, so call `await_render_idle` after `set_view_time` before reading them. |
| `set_view_time` *(viewer only, **mutating**)* | Moves the Timeline as the user does by scrubbing or pressing Now. `time` is `now` (as the Now button: the view then follows now), an ISO-8601 time, or an offset from the view time (`+6h`, `-30m`, `+1d`). `snap` is `exact` (default; layers apply their own time limits, so times between samples can be tested) or `nearest` (the nearest loaded sample). Choosing a time leaves Live mode. Returns the `get_timeline_state` payload; `view_time_not_applied` when no time-aware dataset is loaded. `now` also works past every loaded window: Live follows the clock and layers without data hide. |
| `step_time` *(viewer only, **mutating**)* | Steps the Timeline as its ‹ › and arrow keys do: `direction` `next` or `previous`, `unit` `10min`, `1h`, `6h`, `1d` (landing on whole units), `sample` (of the step driver layer), `boundary` (dataset/run starts and ends), `data` (the next or previous cluster, skipping gaps) or `current` (the Timeline's chosen step), `count` times. Pins the time. `view_time_not_applied` when there is nothing further that way. |
| `set_timeline_view` *(viewer only, **mutating**)* | Changes what the Timeline shows, as its preset menu, wheel, drag, In map view checkbox and Collapse to strip do: at most one of a `preset` (`now_6h`, `today`, `next_48h`, `this_run`, `in_view` — the data of the layers in the map view — or `all_loaded`), `zoom` `in` or `out` around the view time, or a custom `start`/`end`; and/or `inMapView` (true lists only the layers whose footprint intersects the map view, which then set the axis; the rest fold into one row), `showOnline` (Library data not loaded: online dashed, on disk outlined) and `layout` (`lanes` or `strip`), applied first. Leaves the view time alone. |
| `set_dataset_state` *(viewer only, **mutating**)* | Shows or hides a loaded dataset (`visible`) and sets its `opacity` (0..1), as the Datasets list's eye icon and opacity control do; with neither it only reports the state. Use it to switch on datasets that load hidden (gridded S-104 surfaces, duplicate exchange-set variants). Returns the state before and after and whether it `changed`; `dataset_not_found` for an unknown id. |
| `select_dataset` *(viewer only, **mutating**)* | Selects a loaded dataset in the Datasets panel as a click on its row does (switching the panel to its Datasets tab when needed), so the pinned inspector and the map's validation overlay follow it; `tab` (`dataset` \| `layers` \| `validation`) optionally switches the inspector tab, and the tab is kept otherwise. It does not open the panel: call `set_panel Datasets` first to see it. Returns `id`, `spec`, `previousId`, the `tab` shown, `deferred`, and a `validation` summary: `state` (`ready`, `no_rule_pack`, or `not_loaded`), `total` / `errors` / `warnings` / `infos`, `located` (findings with a location, which the overlay draws) and `message` (the Validation tab's counts summary or empty-state text). Validation runs when a dataset loads, and `open_dataset` returns after it; for a dataset still loading the tool waits up to `timeoutMs` (default 10000). An exchange-set cell deferred until it is in view reports `not_loaded`. Call `await_render_idle` before `capture_app_screenshot` so the overlay has painted. `dataset_not_found` for an unknown id. |
| `list_notifications` *(viewer only, read-only)* | Lists the notifications on screen, oldest first: `id`, `severity`, `title`, `message`, `createdUtc`, `persistent`, and its action labels. |
| `dismiss_notification` *(viewer only, **mutating**)* | Dismisses the notification with `id`, or every one (`all`, the default), as the user's close button does. Returns the ids dismissed; `notification_not_found` for an id not on screen. Useful before a screenshot. |
| `set_test_clock` *(viewer only, **mutating**, test hooks only)* | Registered only when the viewer starts with `--mcp-test-hooks`. Moves (`now`, `advance` such as `+1h`), freezes (`freeze`) or resets (`reset`) the viewer's notion of now, so forecasts age, runs expire and a Live Timeline advances without waiting. Minute-tick consumers (the Timeline, the Library's expiry check) react immediately. |
| `ui_tree` *(viewer only, read-only, test hooks only)* | Lists the UI as an accessibility client sees it, through Avalonia's automation peers: one root per window and open popup (`kind` `window` or `popup`: context menus and flyouts), each a tree of elements with a `ref` (`e12`; valid while the element stays on screen), `id` (its automation id, see the convention in `tests/EncDotNet.S100.Viewer.Tests/README.md`, e.g. `Datasets.DatasetsTab`, `Library.Tree`, `ActivityBar.Datasets`), `role` (`button`, `listItem`, `tabItem`, `treeItem`, `edit`, `checkBox`, `menuItem`…), `name`, `text` (the visible text of a row, or the tooltip of an icon button, when it has no name), `enabled`, `focused`, `patterns` (the actions it supports: `invoke`, `toggle`, `value`, `rangeValue`, `selectionItem`, `expandCollapse`) with their state (`toggle`, `value`, `selected`, `expanded`), and `bounds` in its window. `filter` `interactive` (default) keeps elements with an id or an action and promotes the children of the rest; `all` lists everything. `root` (an id or ref) lists one subtree; `depth` (default 30) and `maxNodes` (default 400; `truncated` says when it cut) bound the size. The map is one element: use the map tools for chart content. |
| `ui_invoke` / `ui_set_value` / `ui_toggle` / `ui_select` / `ui_expand` / `ui_collapse` / `ui_focus` / `ui_context_menu` *(viewer only, **mutating**, test hooks only)* | Act on one element as the user does: click a button or menu item (`ui_invoke`); replace a text box's text or set a slider (`ui_set_value`, `value`); flip a check box or toggle button, or set it with `state` `on`/`off` (`ui_toggle`); select a list row, tab, tree node or radio button (`ui_select`); expand or collapse a tree node, expander, combo box or submenu; move keyboard focus (`ui_focus`; leaving a text box runs its lost-focus behaviour, so an in-place rename commits); or open a context menu as a right-click does, selecting the row or node first (`ui_context_menu`). Target by `id` or `ref` (exactly one). An id repeated per row (`Datasets.Row.Remove`) is ambiguous on its own: scope it with `within` (the row's ref or an ancestor's id) or use the ref. Returns the element afterwards. Errors: `ui_element_not_found` (with similar ids on screen), `ui_element_ambiguous` (each match's `ref` and row text), `ui_element_disabled`, `ui_action_not_supported` (with the element's `patterns`). A dialog or popup closing animates for a moment after the call returns; read `ui_tree` again before relying on it being gone. |
| `list_library_sources` *(viewer only, read-only)* | Lists the Library: each collection (top-level node) with its kind tag (`DIR`, `ZIP`, `WEB`, `AWS`, `LIST`, `FEED`, `JSON`, `S-128`), item count and status line, and each source with its index state, `indexedAt` (how stale a cached online catalogue is), URL (shared-feed tokens masked) and, unless `counts: false`, item counts by state. |
| `query_library_items` *(viewer only, read-only)* | Finds Library datasets, paged (`page`, `pageSize` 1–500, default 50). Filters: `sourceId` (collection or source), `states` (`online`, `local`, `loaded`, `on_pan`, `update`, `expired`, `missing`, `listed`), `validAt` (`view_time`, as the Library's Valid at view time toggle, or an ISO-8601 time: data whose run or time coverage holds it), `spec`, `text` (as the Library filter box), a bounding box (`south`/`west`/`north`/`east`), or a point (`lat`/`lon`: what covers it, most detailed first, as tapping the map does). Each item reports `id` (`<sourceId>:<key>`), spec, state and tags as its row shows them, edition, issue date, size, bounds, local path, and for forecasts the model, run and `validUntil`. |
| `describe_library_item` *(viewer only, read-only)* | Returns one item (by `itemId`) with its details pane: groups of labelled fields (Forecast, Product, Coverage, Source, …). `library_item_not_found` for an unknown id. |
| `list_known_sources` *(viewer only, read-only)* | Lists the Online Catalogue directory: the curated sources and the user's own (`userAdded`), with provider, region, format, URL, edition/size support, product, pilot and not-for-navigation flags, and forecast feeds' models (cadence, horizon). |
| `add_library_source` *(viewer only, **mutating**)* | Adds a Library source through the Add-to-Library dialog's own logic: a known source (`knownSourceId`), a catalogue or feed `url` (including a SECOM service endpoint, read anonymously), or a local `path` (folder, exchange set, manifest, S-128; `kind` overrides the guess). Call with `preview: true` first to load the catalogue and list its `choices` (NOAA states / districts / regions, USACE rivers, S-111 models, S-102 areas, manifest groups, feed or SECOM products) with sizes, plus forecast `shapes`, S-100 `resolutions` and existing `collections`. Then add with `choices` (values or labels) or `includeAll`, into `collectionId` or a new collection (`collectionName`). Only indexes; nothing is downloaded. Returns the new collection and source ids. |
| `refresh_library_source` *(viewer only, **mutating**)* | Re-indexes a collection or source (`id`) or the whole Library, as the panel's Refresh does, waits up to `waitMs` (default 60 s), and reports items `added` / `removed`, items whose state `changed` (by new state, e.g. `update`, `expired`) and `counts` by state. |
| `library_action` *(viewer only, **mutating**)* | `load`, `load_as_you_pan`, `download` (then load), `download_only`, `update` (newer editions or runs) or `cancel`, on `itemIds` or items selected with the `query_library_items` filters; `cancel` with `all: true` cancels every download. Items the action does not apply to are `skipped` (by state). `dryRun: true` reports the count and `bytes` without acting; `maxBytes` refuses larger downloads (`library_change_rejected`). Downloads run in the background (`await_library_idle`). |
| `remove_library_source` *(viewer only, **mutating**)* | Removes a collection or source (`id`) from the Library and deletes its cached index; downloaded files stay on disk. Requires `confirm: true`. |
| `await_library_idle` *(viewer only, read-only)* | Waits up to `timeoutMs` (default 60 s) until no source is indexing, no download is running and every dataset a `library_action` download or load opens has opened; returns `idle`, `timedOut`, `indexing`, `loading` (datasets still to open, including any still downloading) and the running batch's progress. |
| `await_render_idle` *(viewer only, read-only)* | Blocks until the live map settles — no completed paint, graphics-refresh request, or active layer fetch for a continuous quiet period — or until a timeout elapses (`quietPeriodMs` default 250, clamped `[0, 10000]`; `timeoutMs` default 5000, clamped `[50, 120000]`). Call it between `set_viewport` and `render_to_image` so the screenshot reflects a settled view instead of racing the render pass. Always waits at least the quiet period and measures the on-screen `InstrumentedMapControl` paint loop, not the offscreen `render_to_image` clone. A layer's busy flag only holds the wait open while that layer keeps emitting render activity; a stale busy flag that never clears (with no paint/refresh for the quiet period) is ignored, so a settled map reports `wentIdle` rather than being forced to `timedOut`. Returns `wentIdle`, `timedOut`, `waitedMs`, and `paintsObserved`. |
| `get_render_stats` *(viewer only, read-only)* | Reports the cost of the most recently completed on-screen map paint: wall-clock `frameDurationMs`, `intervalMs` since the previous paint, `totalDrawCalls`, and a per-style breakdown (`style`, `calls`, `durationMs`, ordered by descending duration). Also returns a rolling **`window`** object aggregating the most recent paints (up to 4096) so transient expensive frames are not missed once the view settles to a cheap cached repaint: `count`, `firstSequence`/`lastSequence`, and max / mean / p95 for both wall-clock frame time (`frameMaxMs`/`frameMeanMs`/`frameP95Ms`) and summed `VectorStyle` time (`vectorMaxMs`/`vectorMeanMs`/`vectorP95Ms`), plus `maxTotalDrawCalls`. `window.slowestFrame` identifies the maximum-duration paint with its sequence, completion timestamp, whole-frame duration, summed instrumented-style duration, uninstrumented remainder, and draw-call count so an outlier can be correlated with trace activity. Pass `resetWindow: true` to clear the window after reading (the canonical pattern: read+reset before an interaction burst, read again after to capture just that burst). Use it to measure rendering performance across pan / zoom, palette, or time-step changes. Describes the live map paint, not the offscreen `render_to_image` clone; returns `hasData = false` when no paint has occurred yet (the `window` is still reported). Pair with `await_render_idle` so the reported latest paint reflects a settled view. `totalDrawCalls` counts only Mapsui style-renderer draws (basemap, overlays, untiled layers); chart content composited from the tiled vector cache is **not** counted, so a count of 1–2 does not mean the chart drew nothing — confirm visually with `render_to_image` or `capture_app_screenshot`. |
| `open_dataset` *(viewer only, **mutating**)* | Loads a dataset into the live viewer using its existing open code path, so agents can measure the load hot path. `path` is a single file (S-101 `.000`, HDF5 `.h5`, GML, etc.) OR an exchange set (a folder containing `CATALOG.XML`, or a `.zip` of one); the kind is auto-detected. `spec` optionally forces a product-spec hint (e.g. `S-102`) for single-file loads. Returns the resulting catalog id(s), `spec`, bounding box (`southLatitude`/`westLongitude`/`northLatitude`/`eastLongitude`), `count`, `loadDurationMs`, `timedOut` (exchange-set quiescence), and `skipped` (why catalogued datasets were skipped). When nothing portrayable loads, the `dataset_load_failed` reason quotes the first five problems (an unreadable catalogue, unsupported products, orphan updates). |
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

Every `spec` argument accepts either a string (`"S-101"`, `"S-124/1.5.0"`;
the edition is optional) or the `{"name":…,"edition":{"major":…,"minor":…,"clarification":…}}`
object the tools return in their results, so a result's `spec` can be passed
straight into the next call. `edition` is optional in the object form, and an
all-zero edition means "any edition". Any other shape is rejected with an
`invalid_argument` error that names `spec`.

### Times outside the data

`sample_coverage` and `sample_coverage_along` never return a value for a time the data doesn't cover unless the caller asks for it. This matches the timeline, which shows "No data at this time" rather than a stale frame.

- **Dataset selection is time-aware.** Of the S-104 / S-111 datasets covering the point, those whose time range contains the requested time are used. The finest grid wins among them, then the newest run (its `issueDate`/`issueTime`). With two runs or two models loaded at one point, the one that covers the time is sampled even if the other is finer. For station series, runs reporting the same station are chosen between the same way.
- **Tolerance.** A time within one time-step interval of either end of a dataset's range counts as in range and samples the nearest step. With hourly steps ending at 21:00Z, 21:20Z samples 21:00Z.
- **Single instant (`time`, or `times: {kind:"instant"}`), strict by default.** A time outside every covering dataset's range returns `time_out_of_range`. Its `details` give `requestedTime`, `datasetId`, `validFrom`, `validTo`, `run` (when the dataset declares an issue time), `nearestStep`, and `candidates` (every covering dataset with its range). Re-ask at `nearestStep`, or tell the user the forecast doesn't reach that far.
- **Opt in to the nearest step** with `outOfRange: "nearest"`. The result then carries `timeStatus: "before_start"` or `"after_end"`, and `value.sampleTime` is the step that was used. In-range results always carry `timeStatus: "in_range"`. `outOfRange` defaults to `"error"`.
- **Range / series.** The covered part of the window is returned. When the window reaches past the data, the result has `truncated: true`, and `coveredFrom` / `coveredTo` give the part of the window the data spans. Series instants beyond the data are dropped instead of all snapping to the last step. `time_out_of_range` is returned only when the window doesn't overlap the data at all. `outOfRange` does not affect windowed queries.

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
  without changing it), `get_timeline_state` and `list_notifications`
  (which snapshot the Timeline and the notifications), and the Library
  reads `list_library_sources`, `query_library_items`,
  `describe_library_item`, `list_known_sources` and `await_library_idle`,
  and, with `--mcp-test-hooks`, `ui_tree`.
* **Mutating** — modify the live viewer's state (navigator, palette,
  time step, loaded datasets, routes, etc.). Use only when you intend to
  drive the viewer's UI from outside. Examples: `set_viewport`,
  `pick_features` with `select: true` (which publishes the pick to the
  Object Information panel and draws the pick highlight),
  `set_palette`, `set_display_category`, `set_display_mode`,
  `set_time_step`,
  `set_own_ship`, `open_dataset`, `close_dataset`,
  `close_all_datasets` (which
  load / unload datasets through the viewer's own open / close code
  path), the route-editing family `create_route`, `delete_route`,
  `append_waypoint`, `insert_waypoint`, `move_waypoint`,
  `delete_waypoint`, `set_leg_attributes`, and `set_route_info`, and
  `set_panel` (which shows / hides the viewer's activity panels), and
  `set_view_time`, `step_time`, `set_timeline_view`, `set_dataset_state`, `select_dataset`, `dismiss_notification` and, with
  `--mcp-test-hooks`, `set_test_clock` and the `ui_*` actions, and the Library changes
  `add_library_source`, `refresh_library_source`, `library_action` and
  `remove_library_source`.

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

* `set_viewport` — the viewer drives a **live** Mapsui map and accepts a
  web-mercator **zoom** level or a **scale denominator**; the CLI renders a
  **headless** composite addressed by **scale denominator**. The parameter
  names differ (the viewer's `centerLat`/`centerLon`/`scaleDenominator`, the
  CLI's `centerLatitude`/`centerLongitude`/`scaleDenominator`). Both honour a
  clockwise rotation (the viewer's `rotation` on any frame form; the CLI's
  `rotationDegrees` on the centre + scale form), turning the chart about the
  image centre while labels stay upright. Only the viewer accepts
  longitudes past ±180° and antimeridian-crossing boxes; the CLI's stay
  within `[-180, 180]` with `west < east`. The two keep separate
  implementations (the viewer's over `IMapViewportController`, the CLI's over
  `IViewportController`) so the viewer retains zoom-level input.
* `pick_features`, `capture_app_screenshot`,
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
snapshot of a clone of the live Mapsui `Map` (its `MapsuiMapHost` implements
`IImageRenderer`); the headless CLI backs it with its Skia
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
- The `ui_*` tools can do anything a user can do in the UI, so they are
  registered only with `--mcp-test-hooks`, a command-line flag that is
  never saved; turning MCP on in Settings does not offer them.

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

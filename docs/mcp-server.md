# MCP server (viewer-hosted)

The S-100 Viewer can host a Model Context Protocol (MCP) server so
external agents — `mcp-inspector`, Claude Desktop, IDE assistants —
can query the datasets you have loaded in the viewer.

The server is **off by default** and **listens on the loopback
address only**. There is no authentication; the loopback isolation is
the only protection in v1.

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
  different paths in `EncDotNet.S100.Datasets.S111` and have not yet
  been normalised. TODO: pick one canonical unit at the MCP boundary
  (almost certainly metres/second, matching the strongly-typed
  `speedMetresPerSecond` field on `SurfaceCurrentSample` /
  `SurfaceCurrentStationSample`) and rescale the other path.

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

## Connect from `mcp-inspector`

```bash
npx @modelcontextprotocol/inspector
```

Set the transport to **Streamable HTTP**, paste the endpoint from the
viewer's status-bar tooltip (e.g. `http://127.0.0.1:54321/`), and click
**Connect**. You should see three tools:

| Tool | Purpose |
|---|---|
| `list_datasets` | Summarises every dataset currently loaded in the viewer. |
| `find_at` | Returns every loaded dataset whose declared bounding box contains a lat/lon point (decimal degrees, WGS-84). Bbox-only — does not check per-cell coverage or NoData masks. |
| `describe_feature` | Returns spec, feature type, and attributes for a feature id in a given dataset. Supported specs: S-101 (RCID), S-102 (`BathymetryCoverage[.01]`), S-104 / S-111 (`WaterLevel`/`SurfaceCurrent[.NN][.Group_KKK]` or bare station identifier), and S-124 (`gml:id`). |
| `sample_coverage` | Samples a depth / water-level / current value at a lat/lon from an S-102 / S-104 / S-111 dataset. |

## Sample agent prompts

> "List the datasets loaded in the viewer and their bounding boxes."
>
> "What is the depth at 47.6062°N, 122.3321°W in the loaded S-102
> dataset?"
>
> "Describe feature `LIGHTS.123` in the loaded S-201 dataset."

## Security notes

- The server binds to `127.0.0.1` only. Other machines on your LAN
  **cannot** reach it.
- There is no auth. Any local process on the machine can connect,
  including malicious code. Only enable MCP when you trust everything
  running locally.
- The tools are strictly read-only. No tool can write files, load
  data, or mutate viewer state.

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

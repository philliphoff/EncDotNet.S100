# EncDotNet.S100.Mcp

A Model Context Protocol (MCP) server library that exposes the read-only
tools defined in `EncDotNet.S100.Mcp.Tools` over **Streamable HTTP**.
The library is **UI-agnostic** — host it inside the desktop viewer, a
CLI tool, or any process that can spin up an ASP.NET Core Kestrel
listener.

## Stance

- **Loopback-only.** The default `BindAddress` is `IPAddress.Loopback`.
  Hosts may pin a different loopback variant (e.g. `::1`); non-loopback
  bind is not exposed through the viewer UI by design.
- **Off by default.** The viewer ships this library disabled. Operators
  opt in explicitly through their host's settings.
- **No authentication.** Loopback isolation is the only protection in
  v1. Do **not** expose the server to a routable address without
  putting an authenticating reverse proxy in front.
- **Read-only.** Every tool answers questions about the catalog and
  loaded datasets; no tool mutates host state, writes files, or loads
  / unloads data.

## Embedding

```csharp
using EncDotNet.S100.Mcp;
using EncDotNet.S100.Mcp.Tools;

IDatasetCatalog catalog = /* your catalog adapter */;
var options = new S100McpServerOptions
{
    BindAddress = IPAddress.Loopback,
    Port = 0,           // 0 = ephemeral
};

await using var server = new S100McpServer(catalog, options);
await server.StartAsync();
Console.WriteLine($"MCP endpoint: {server.Endpoint}");
```

The server hosts a single Streamable HTTP endpoint on the
`mcp` path. Connect with any MCP client that speaks the
Streamable HTTP transport (the official `ModelContextProtocol`
client, `mcp-inspector`, etc.).

## Tools

The server exposes the read-only tools defined by
`EncDotNet.S100.Mcp.Tools`:

| Tool name | Returns |
|---|---|
| `list_datasets` | Per-dataset summaries (id, spec, name, extent) |
| `describe_feature` | Spec / feature type / attributes for a feature in a dataset |
| `sample_coverage` | Sampled value at a lat/lon for a coverage dataset (S-102 / S-104 / S-111); optional `times` JSON envelope (instant / range / series) populates a per-step `series` array for S-104 / S-111 |
| `find_at` | Datasets whose declared bbox contains a point or intersects a `GeoQuery` envelope |
| `query_features` | Features from loaded GML datasets that intersect a spatial query (point / box / polygon / polyline) |
| `sample_coverage_along` | Per-vertex coverage samples for a polyline (S-102 / S-104 / S-111); supports the same `times` envelope as `sample_coverage`, applied per vertex |
| `list_specs` | Spec catalogue with per-spec capability flags (query / describe / sample / list time-steps) |
| `list_time_steps` | Available UTC time-step instants (+ cadence) for a time-varying coverage dataset (S-104 / S-111) |

Spatial queries are passed as a JSON envelope on the `query` (or
`polyline`) parameter:

```json
{"kind": "point",    "latitude": 47.6, "longitude": -122.3}
{"kind": "box",      "south": 47, "west": -123, "north": 48, "east": -122}
{"kind": "polygon",  "ring": [[47, -123], [48, -123], [48, -122], [47, -123]]}
{"kind": "polyline", "vertices": [[47.6, -122.3], [47.7, -122.4]], "corridorWidthMeters": 1000}
```

See `src/EncDotNet.S100.Mcp.Tools/README.md` for the full tool
contract, request/response schemas, and error codes.

## Public surface

```csharp
public sealed class S100McpServerOptions
{
    public IPAddress BindAddress { get; init; } = IPAddress.Loopback;
    public int Port { get; init; } = 0;
    public int MaxConcurrentConnections { get; init; } = 16;
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromMinutes(10);
}

public sealed class S100McpServer : IAsyncDisposable
{
    public S100McpServer(IDatasetCatalog catalog,
                        S100McpServerOptions options,
                        ILoggerFactory? loggers = null);

    public bool IsRunning { get; }
    public int? Port { get; }
    public Uri? Endpoint { get; }
    public int ConnectionCount { get; }
    public event EventHandler<EventArgs>? StateChanged;
    public event EventHandler<EventArgs>? ConnectionsChanged;

    public Task StartAsync(CancellationToken ct = default);
    public Task StopAsync(CancellationToken ct = default);
    public ValueTask DisposeAsync();
}
```

## Error mapping

Tool failures are returned as MCP tool-call results with `isError=true`
and a structured JSON payload:

```json
{
  "code": "dataset_not_found",
  "message": "Dataset 'foo' not found.",
  "details": { /* optional, error-specific */ }
}
```

The `code` value is the `Code` property of the `ToolError` subtype
emitted by `EncDotNet.S100.Mcp.Tools`. Unexpected exceptions are
surfaced as `code = "internal_error"`.

## Polymorphic payloads

`SampledValue` is an abstract base. The server registers JSON
polymorphism with a `$kind` discriminator so derived shapes
(e.g. `DepthSample`) are visible to clients.

## Dependencies

- [`ModelContextProtocol`](https://www.nuget.org/packages/ModelContextProtocol/) (v1.3.0+)
- [`ModelContextProtocol.AspNetCore`](https://www.nuget.org/packages/ModelContextProtocol.AspNetCore/) (v1.3.0+)
- ASP.NET Core Kestrel (`Microsoft.AspNetCore.App` framework reference)

## Testing

`tests/EncDotNet.S100.Mcp.Tests/` exercises the server end-to-end via
the official MCP client: lifecycle, `tools/list`, the three tool
round-trips, error mapping, loopback binding, and connection counts.

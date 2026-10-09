# EncDotNet.S100.Mcp

`EncDotNet.S100.Mcp` hosts the S-100 Model Context Protocol (MCP) tools from
[`EncDotNet.S100.Mcp.Tools`](../EncDotNet.S100.Mcp.Tools/README.md), so an AI
agent can query loaded datasets. It serves them over Streamable HTTP, through
an in-process ASP.NET Core Kestrel listener, or over stdio. It has no UI
dependency. SoundCharts and `s100 mcp serve` both use it, and you can host it
in your own process.

For the tools, their parameters and how to connect a client, see the
[MCP server](../../docs/mcp-server.md) guide.

## Install

This package isn't published to NuGet. Add a project reference:

```xml
<ProjectReference Include="path/to/src/EncDotNet.S100.Mcp/EncDotNet.S100.Mcp.csproj" />
```

## Serve datasets over HTTP

`S100McpServer` serves the tools for an `IDatasetCatalog`, the set of datasets
the tools can see. `FileDatasetCatalog` is a ready-made catalog built from
dataset files:

```csharp
using System.Net;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Mcp;

string path = args[0];
string spec = DatasetPipelineFactory.DetectProductSpec(path)
    ?? throw new InvalidOperationException($"Couldn't detect the product of {path}.");

var catalog = FileDatasetCatalog.Build(
    [new FileDatasetInput(new DatasetId(Path.GetFileNameWithoutExtension(path)), spec, path)]);

await using var server = new S100McpServer(catalog, new S100McpServerOptions
{
    BindAddress = IPAddress.Loopback,
    Port = 0, // let the operating system pick a free port
});

await server.StartAsync();
Console.WriteLine($"MCP endpoint: {server.Endpoint}");
await Task.Delay(Timeout.Infinite);
```

The server maps one Streamable HTTP endpoint at the root path, so
`server.Endpoint` is `http://127.0.0.1:<port>/`. Connect any MCP client that
supports Streamable HTTP, such as the official `ModelContextProtocol` client
or `mcp-inspector`.

A host with its own live set of datasets, such as a viewer, implements
`IDatasetCatalog` instead of using `FileDatasetCatalog`. See
[`IDatasetCatalog`](../EncDotNet.S100.Mcp.Tools/README.md#idatasetcatalog).

### Server options

| `S100McpServerOptions` property | Default | Description |
|---|---|---|
| `BindAddress` | `IPAddress.Loopback` | The address to listen on. |
| `Port` | `0` | The TCP port. `0` lets the operating system pick one; read it from `S100McpServer.Port`. |
| `MaxConcurrentConnections` | `16` | A soft cap on concurrent sessions. Extra connections are still accepted, but `ConnectionCount` reports them so the host can refuse or throttle. |
| `IdleTimeout` | 10 minutes | How long an idle session is kept. |
| `AdditionalTools` | `null` | Host tools to serve alongside the built-in tools. See [Add host tools](#add-host-tools). |

### Server members

| `S100McpServer` member | Description |
|---|---|
| `StartAsync`, `StopAsync` | Start and stop the listener. Both do nothing if the server is already in that state. |
| `IsRunning`, `Port`, `Endpoint` | The listener's state. `Port` and `Endpoint` are `null` until it starts. |
| `ConnectionCount` | The number of open MCP connections. |
| `StateChanged` | Raised when the server starts or stops. |
| `ConnectionsChanged` | Raised when `ConnectionCount` changes. |
| `DisposeAsync` | Stops the server. |

The constructor takes the catalog, the options and an optional
`ILoggerFactory`. SoundCharts passes its own logger factory so MCP requests
appear in its logs.

## Serve datasets over stdio

`S100McpStdioHost` serves the same tools over standard input and output, for a
client that starts your process itself:

```csharp
await S100McpStdioHost.RunAsync(catalog);
```

It runs until standard input closes or the cancellation token is cancelled.
Standard output carries the protocol, so the host sends all logging to standard
error. Don't write to `Console.Out` from your own code while it runs.

## Built-in tools

`S100McpTools.Create(catalog)` returns the built-in tools, which
`S100McpServer` and `S100McpStdioHost` always serve. They only read the catalog:

`list_datasets`, `describe_feature`, `describe_feature_type`,
`sample_coverage`, `find_at`, `identify_features`, `nearest_features`,
`query_features`, `count_features`, `search_features`,
`sample_coverage_along`, `list_specs` and `list_time_steps`.

[Query tools](../../docs/mcp-server.md#query-tools) in the MCP server guide
describes each one.

## Add host tools

Pass extra `McpServerTool` instances in `S100McpServerOptions.AdditionalTools`,
or as the `additionalTools` argument of `S100McpStdioHost.RunAsync`. Their names
must not match a built-in tool.

`S100MutableTools.Create` (namespace `EncDotNet.S100.Mcp.MutableTools`) builds
the tools that change a session. Each group of tools needs a capability from
the host, and you get only the tools whose capability you supply:

| Capability | Tools |
|---|---|
| `IMutableDatasetCatalog` | `open_dataset`, `close_dataset`, `close_all_datasets` |
| `IPresentationController` | `set_palette`, `set_display_category`, `set_display_mode` |
| `ITimeController` | `set_time_step` |
| `IViewportController` | `set_viewport` |
| `IImageRenderer` | `render_to_image` |

Pass each capability, except the catalog, through an
`ICapabilityAccessor<T>`, such as `StaticCapabilityAccessor<T>` (namespace
`EncDotNet.S100.Hosting`). If the accessor's `Current` is still `null` when a
tool is called, the tool returns a `host_not_ready` error, so a host can attach
a capability after the server starts. `s100 mcp serve` uses this to back the
tools with a headless renderer.

`LibraryMcpAdapters` and `LibraryEditMcpAdapters` (namespace
`EncDotNet.S100.Mcp.Library`) wrap the Library tools from
`EncDotNet.S100.Mcp.Tools` as MCP tools. SoundCharts serves them over its
Library.

## Security

- The server listens on the loopback address by default, so other machines
  can't reach it.
- There's no authentication. Any process on the machine can connect. Don't
  bind it to a routable address without an authenticating reverse proxy in
  front of it.
- SoundCharts starts it only when you turn it on. Its settings offer only a
  loopback address; the `--mcp-bind` command-line option can change it.
- The built-in tools only read the catalog. Host tools you add can change
  host state. The **Changes state** column in
  [MCP tools](../../docs/mcp-server.md#mcp-tools) shows which ones do.

## Errors

A tool that fails returns a tool result with `isError` set to `true` and a JSON
payload:

```json
{
  "code": "dataset_not_found",
  "message": "Dataset 'foo' is not present in the catalog.",
  "details": {}
}
```

`code` is the `Code` of the `ToolError` the tool returned, and `details` holds
that error's fields. An unexpected exception returns the code
`internal_error`. For the list of codes, see
[Errors](../EncDotNet.S100.Mcp.Tools/README.md#errors).

## Coverage sample payloads

A coverage sample's `value` is one of several shapes. The JSON includes a
`$kind` property that names the shape: `depth`, `water_level`,
`water_level_station`, `surface_current` or `surface_current_station`.

## Dependencies

- [`ModelContextProtocol`](https://www.nuget.org/packages/ModelContextProtocol/)
  and
  [`ModelContextProtocol.AspNetCore`](https://www.nuget.org/packages/ModelContextProtocol.AspNetCore/),
  version 1.4.0
- ASP.NET Core, through the `Microsoft.AspNetCore.App` framework reference

## Tests

[`tests/EncDotNet.S100.Mcp.Tests`](https://github.com/philliphoff/EncDotNet.S100/tree/main/tests/EncDotNet.S100.Mcp.Tests)
runs the server end to end with the official MCP client. It covers the server
lifecycle, tool round trips, additional and mutating tools, error mapping, JSON
serialization, port conflicts and connection counts.

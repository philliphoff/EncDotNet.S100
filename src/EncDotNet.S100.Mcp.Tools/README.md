# EncDotNet.S100.Mcp.Tools

Foundation for a Model Context Protocol (MCP) server that exposes
S-100 datasets to LLM-driven tooling. This project ships **the
abstraction and the tool surface only** — no MCP protocol code, no
viewer code, no transport. PR MCP-2 will layer the wire protocol on
top of this.

## Architecture

```
┌────────────────────────────────────────────────┐
│ EncDotNet.S100.Viewer                          │
│  (PR MCP-2: hosts MCP server,                  │
│   implements IDatasetCatalog)                  │
└──────────────────────┬─────────────────────────┘
                       │
┌──────────────────────▼─────────────────────────┐
│ EncDotNet.S100.Mcp                             │
│  (PR MCP-2: MCP server + transports)           │
└──────────────────────┬─────────────────────────┘
                       │
┌──────────────────────▼─────────────────────────┐
│ EncDotNet.S100.Mcp.Tools         ← THIS PROJ   │
│  - IDatasetCatalog + LoadedDataset             │
│  - ToolResult<T> + ToolError                   │
│  - ListDatasetsTool / DescribeFeatureTool /    │
│    SampleCoverageTool                          │
└──────────────────────┬─────────────────────────┘
                       │
┌──────────────────────▼─────────────────────────┐
│ EncDotNet.S100.Core + EncDotNet.S100.Datasets.*│
└────────────────────────────────────────────────┘
```

The project intentionally has **no MCP SDK, no Avalonia, and no viewer
reference**. The same tool surface can therefore be hosted by a CLI, a
headless service, or a different viewer in the future.

## `IDatasetCatalog`

The abstraction is in `EncDotNet.S100.Mcp.Tools.Catalog` and exposes a
single property + an event:

```csharp
public interface IDatasetCatalog
{
    ImmutableArray<LoadedDataset> Datasets { get; }
    event EventHandler<DatasetCatalogChangedEventArgs>? Changed;
}
```

**Why a property, not a method?** "What is loaded right now" reads more
naturally as state than as an operation. The catalog implementation
publishes a fresh `ImmutableArray<LoadedDataset>` on every change.
Consumers capture the reference once and use it for the duration of an
operation without taking any lock.

**Why `LoadedDatasetData` is a discriminated union.** Each spec
contributes either a typed DataModel (vector products — S-122, S-124,
S-125, S-127, S-128, S-129, S-201, S-411, S-421) or a coverage source
(HDF5-encoded coverage products — S-102, S-104, S-111). Tool code
pattern-matches on the variants:

```csharp
return dataset.Data switch
{
    S124DatasetData s124 => DescribeS124(s124.Model, ...),
    S102CoverageData cov => SampleS102(cov.Source, ...),
    _ => ToolResult<T>.Err(new SpecNotSupportedForTool(dataset.Spec, name)),
};
```

**Best-effort coverage handles.** Coverage variants carry live handles
whose lifetime is owned by the host (e.g. the viewer). A dataset can be
unloaded between the catalog snapshot and the actual read. Tool
implementations wrap reads in `try / catch (ObjectDisposedException)`
and surface `DatasetClosedDuringQuery`. The host is responsible for
publishing the next snapshot before disposing the handle.

## Tool contract

Every tool exposes a single async method that returns
`Task<ToolResult<T>>` where `T` is the result record:

```csharp
public sealed class ListDatasetsTool
{
    public ListDatasetsTool(IDatasetCatalog catalog);
    public Task<ToolResult<ListDatasetsResult>> InvokeAsync(
        ListDatasetsRequest request,
        CancellationToken cancellationToken = default);
}
```

`ToolResult<T>` is a small local discriminated union (`OkResult` /
`ErrResult`) — there is no NuGet dependency on
`OneOf` / `LanguageExt`. Tools never throw into the calling code; every
failure case is reified as a typed `ToolError`. This keeps the eventual
MCP-error wire format flexible while giving in-process callers a typed
surface to match on.

The five error variants implemented in this PR:

| Code                          | When                                                                  |
|-------------------------------|-----------------------------------------------------------------------|
| `dataset_not_found`           | The requested `DatasetId` is not in the snapshot.                     |
| `dataset_closed_during_query` | The coverage handle threw `ObjectDisposedException` mid-read.         |
| `no_dataset_covers_point`     | No loaded dataset's bounds contain the requested lat/lon.             |
| `feature_not_found`           | The named feature is not present in the named dataset.                |
| `spec_not_supported_for_tool` | The tool does not (yet) support the requested spec.                   |
| `invalid_argument`            | A request property failed validation (e.g. latitude / longitude out of WGS-84 range). |

## Spec strategy pattern

`DescribeFeatureTool` dispatches per-spec through
`FeatureDescriberRegistry` keyed on `SpecRef.Name`. Each describer
implements an internal `ISpecFeatureDescriber` strategy.

This PR ships `S124FeatureDescriber` end-to-end (lookup, attribute
serialisation, complex-attribute serialisation, xlink-reference
projection across the catalog). Other specs fall through to a
`SpecNotSupportedForTool` error until their describers are added in
follow-up PRs.

## Usage

```csharp
using EncDotNet.S100.Mcp.Tools;
using EncDotNet.S100.Mcp.Tools.Catalog;

// A host (e.g. the viewer) implements IDatasetCatalog and publishes
// LoadedDataset instances whenever its loaded set changes.
IDatasetCatalog catalog = host.Catalog;

var list = new ListDatasetsTool(catalog);
var describe = new DescribeFeatureTool(catalog);
var sample = new SampleCoverageTool(catalog);
var findAt = new FindAtTool(catalog);

var listed = await list.InvokeAsync(new ListDatasetsRequest());
if (listed.TryGetValue(out var summary))
{
    foreach (var ds in summary.Datasets)
    {
        Console.WriteLine($"{ds.Id} ({ds.Spec})");
    }
}

// Which loaded datasets cover this point? (bbox-only — does not check
// per-cell coverage or NoData masks.)
var hits = await findAt.InvokeAsync(new FindAtRequest(
    Latitude: 50.77,
    Longitude: -1.30));
if (hits.TryGetValue(out var hit))
{
    foreach (var ds in hit.Datasets)
    {
        Console.WriteLine($"{ds.Id} ({ds.Spec}) covers the point.");
    }
}

var depth = await sample.InvokeAsync(new SampleCoverageRequest(
    new SpecRef("S-102", new SpecVersion(2, 1, 0)),
    Latitude: 47.6,
    Longitude: -122.3));

if (depth.TryGetValue(out var ok) && ok.Value is DepthSample d)
{
    Console.WriteLine($"Depth at point: {d.DepthMeters} m");
}
```

## Out of scope (PR MCP-1)

- MCP protocol / server / transports — PR MCP-2.
- Viewer changes — PR MCP-2.
- Pan/zoom/screenshot tools.
- Search / NL tools.
- Write-back tools.
- Comprehensive feature describer coverage for every spec.
- S-104 / S-111 sampling (only S-102 is wired end-to-end).

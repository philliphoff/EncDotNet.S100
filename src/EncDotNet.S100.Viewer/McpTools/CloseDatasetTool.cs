using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>Request payload for <see cref="CloseDatasetTool"/>.</summary>
/// <param name="Id">Catalog id of the dataset to unload.</param>
internal sealed record CloseDatasetRequest(string? Id = null);

/// <summary>Metadata for a single dataset removed by a close operation.</summary>
/// <param name="Id">Catalog id that was removed.</param>
/// <param name="Spec">Canonical product specification name (e.g. <c>"S-101"</c>).</param>
internal sealed record RemovedDataset(string Id, string Spec);

/// <summary>Result payload for <see cref="CloseDatasetTool"/>.</summary>
/// <param name="Id">The id that was requested.</param>
/// <param name="Removed"><see langword="true"/> when at least one dataset was removed.</param>
/// <param name="Count">Number of catalog datasets removed (0 for an unknown id).</param>
/// <param name="RemovedDatasets">Metadata for the datasets that were removed.</param>
internal sealed record CloseDatasetResult(
    string Id,
    bool Removed,
    int Count,
    IReadOnlyList<RemovedDataset> RemovedDatasets);

/// <summary>
/// MCP tool that unloads a currently-loaded dataset by its catalog id
/// using the viewer's existing GUI unload code path, so automation agents
/// can measure the unload hot path. Unknown / already-removed ids resolve
/// gracefully as a non-error <c>removed:false</c> result.
/// </summary>
internal sealed class CloseDatasetTool
{
    /// <summary>The MCP tool name as exposed to clients.</summary>
    public const string Name = "close_dataset";

    private readonly IDatasetCatalog _catalog;
    private readonly IDatasetLoadGateway _gateway;

    /// <summary>Creates the tool.</summary>
    /// <param name="catalog">The dataset catalog to diff before / after the unload.</param>
    /// <param name="gateway">The UI-thread load gateway.</param>
    public CloseDatasetTool(IDatasetCatalog catalog, IDatasetLoadGateway gateway)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(gateway);
        _catalog = catalog;
        _gateway = gateway;
    }

    /// <summary>Unloads the dataset(s) matching the requested id.</summary>
    public async Task<ToolResult<CloseDatasetResult>> InvokeAsync(
        CloseDatasetRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var id = request.Id;
        if (string.IsNullOrWhiteSpace(id))
        {
            return ToolResult<CloseDatasetResult>.Err(new InvalidArgument(
                "id", "a non-empty dataset id is required"));
        }
        id = id.Trim();

        if (!_gateway.IsReady)
        {
            return ToolResult<CloseDatasetResult>.Err(new MapNotReady(
                "the dataset loader has not been initialised yet"));
        }

        using var _ = await _gateway.LockAsync(ct).ConfigureAwait(false);

        var before = _catalog.Datasets;
        var matched = before
            .Where(d => string.Equals(d.Id.Value, id, StringComparison.Ordinal))
            .Select(d => new RemovedDataset(d.Id.Value, d.Spec.Name))
            .ToList();

        await _gateway.RemoveAsync(id, ct).ConfigureAwait(false);

        // Diff the catalog so the reported count reflects what actually left
        // the catalog (an unknown id removes nothing → graceful success).
        var afterIds = _catalog.Datasets.Select(d => d.Id.Value).ToHashSet(StringComparer.Ordinal);
        var removedDatasets = matched.Where(m => !afterIds.Contains(m.Id)).ToList();

        return ToolResult<CloseDatasetResult>.Ok(new CloseDatasetResult(
            Id: id,
            Removed: removedDatasets.Count > 0,
            Count: removedDatasets.Count,
            RemovedDatasets: removedDatasets));
    }
}

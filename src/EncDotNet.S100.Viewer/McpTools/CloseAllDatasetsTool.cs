using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>Result payload for <see cref="CloseAllDatasetsTool"/>.</summary>
/// <param name="Removed"><see langword="true"/> when at least one dataset was removed.</param>
/// <param name="Count">Number of catalog datasets removed.</param>
/// <param name="RemovedDatasets">Metadata for the datasets that were removed.</param>
internal sealed record CloseAllDatasetsResult(
    bool Removed,
    int Count,
    IReadOnlyList<RemovedDataset> RemovedDatasets);

/// <summary>
/// MCP tool that unloads every currently-loaded dataset through the viewer's
/// existing GUI unload code path. Intended for retention testing after load /
/// render cycles.
/// </summary>
internal sealed class CloseAllDatasetsTool
{
    /// <summary>The MCP tool name as exposed to clients.</summary>
    public const string Name = "close_all_datasets";

    private readonly IDatasetCatalog _catalog;
    private readonly IDatasetLoadGateway _gateway;

    /// <summary>Creates the tool.</summary>
    /// <param name="catalog">The dataset catalog to diff before / after the unload.</param>
    /// <param name="gateway">The UI-thread load gateway.</param>
    public CloseAllDatasetsTool(IDatasetCatalog catalog, IDatasetLoadGateway gateway)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(gateway);
        _catalog = catalog;
        _gateway = gateway;
    }

    /// <summary>Unloads every currently-loaded dataset.</summary>
    public async Task<ToolResult<CloseAllDatasetsResult>> InvokeAsync(CancellationToken ct = default)
    {
        if (!_gateway.IsReady)
        {
            return ToolResult<CloseAllDatasetsResult>.Err(new MapNotReady(
                "the dataset loader has not been initialised yet"));
        }

        using var _ = await _gateway.LockAsync(ct).ConfigureAwait(false);

        var before = _catalog.Datasets
            .Select(d => new RemovedDataset(d.Id.Value, d.Spec.Name))
            .ToList();

        foreach (var id in before.Select(d => d.Id).Distinct(StringComparer.Ordinal))
        {
            await _gateway.RemoveAsync(id, ct).ConfigureAwait(false);
        }

        var afterIds = _catalog.Datasets.Select(d => d.Id.Value).ToHashSet(StringComparer.Ordinal);
        var removedDatasets = before.Where(m => !afterIds.Contains(m.Id)).ToList();

        return ToolResult<CloseAllDatasetsResult>.Ok(new CloseAllDatasetsResult(
            Removed: removedDatasets.Count > 0,
            Count: removedDatasets.Count,
            RemovedDatasets: removedDatasets));
    }
}

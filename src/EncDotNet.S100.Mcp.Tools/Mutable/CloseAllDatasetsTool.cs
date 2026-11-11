using System.ComponentModel;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Query;

namespace EncDotNet.S100.Mcp.Tools.Mutable;

/// <summary>Result payload for <see cref="CloseAllDatasetsTool"/>.</summary>
public sealed record CloseAllDatasetsResult(
    [property: Description("True when at least one dataset was removed.")] bool Removed,
    [property: Description("Number of catalog datasets removed.")] int Count,
    [property: Description("Metadata for the datasets that were removed.")] IReadOnlyList<RemovedDataset> RemovedDatasets);

/// <summary>
/// Mutating tool that unloads every currently-loaded dataset. Renderer-neutral:
/// it drives the shared <see cref="IMutableDatasetCatalog"/>.
/// </summary>
public sealed class CloseAllDatasetsTool
{
    /// <summary>The MCP tool name as exposed to clients.</summary>
    public const string Name = "close_all_datasets";

    private readonly IMutableDatasetCatalog _catalog;

    /// <summary>Creates the tool bound to a mutable catalog.</summary>
    public CloseAllDatasetsTool(IMutableDatasetCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
    }

    /// <summary>Unloads every currently-loaded dataset.</summary>
    public Task<ToolResult<CloseAllDatasetsResult>> InvokeAsync(CancellationToken ct = default)
    {
        var before = _catalog.Datasets
            .Select(d => new RemovedDataset(d.Id.Value, d.Spec.Name))
            .ToList();

        var count = _catalog.RemoveAll();

        return Task.FromResult(ToolResult<CloseAllDatasetsResult>.Ok(new CloseAllDatasetsResult(
            Removed: count > 0,
            Count: count,
            RemovedDatasets: before)));
    }
}

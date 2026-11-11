using System.ComponentModel;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Query;

namespace EncDotNet.S100.Mcp.Tools.Mutable;

/// <summary>Request payload for <see cref="CloseDatasetTool"/>.</summary>
public sealed record CloseDatasetRequest(
    [property: Description("Catalog id of the dataset to unload.")] string? Id = null);

/// <summary>Metadata for a single dataset removed by a close operation.</summary>
public sealed record RemovedDataset(
    [property: Description("Catalog id that was removed.")] string Id,
    [property: Description("Canonical product specification name (e.g. \"S-101\").")] string Spec);

/// <summary>Result payload for <see cref="CloseDatasetTool"/>.</summary>
public sealed record CloseDatasetResult(
    [property: Description("The id that was requested.")] string Id,
    [property: Description("True when a dataset was removed.")] bool Removed,
    [property: Description("Number of catalog datasets removed (0 for an unknown id).")] int Count,
    [property: Description("Metadata for the datasets that were removed.")] IReadOnlyList<RemovedDataset> RemovedDatasets);

/// <summary>
/// Mutating tool that unloads a currently-loaded dataset by its catalog id.
/// Renderer-neutral: it drives the shared <see cref="IMutableDatasetCatalog"/>.
/// An unknown / already-removed id resolves gracefully as a non-error
/// <c>removed:false</c> result.
/// </summary>
public sealed class CloseDatasetTool
{
    /// <summary>The MCP tool name as exposed to clients.</summary>
    public const string Name = "close_dataset";

    private readonly IMutableDatasetCatalog _catalog;

    /// <summary>Creates the tool bound to a mutable catalog.</summary>
    public CloseDatasetTool(IMutableDatasetCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
    }

    /// <summary>Unloads the dataset matching the requested id.</summary>
    public Task<ToolResult<CloseDatasetResult>> InvokeAsync(
        CloseDatasetRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var id = request.Id;
        if (string.IsNullOrWhiteSpace(id))
        {
            return Task.FromResult(ToolResult<CloseDatasetResult>.Err(new InvalidArgument(
                "id", "a non-empty dataset id is required")));
        }
        id = id.Trim();

        var match = _catalog.Datasets.FirstOrDefault(d => string.Equals(d.Id.Value, id, StringComparison.Ordinal));
        if (match is null)
        {
            // Graceful no-op: the id is not (or no longer) present.
            return Task.FromResult(ToolResult<CloseDatasetResult>.Ok(
                new CloseDatasetResult(id, Removed: false, Count: 0, RemovedDatasets: [])));
        }

        var meta = new RemovedDataset(match.Id.Value, match.Spec.Name);
        var removed = _catalog.Remove(match.Id);

        return Task.FromResult(ToolResult<CloseDatasetResult>.Ok(new CloseDatasetResult(
            Id: id,
            Removed: removed,
            Count: removed ? 1 : 0,
            RemovedDatasets: removed ? [meta] : [])));
    }
}

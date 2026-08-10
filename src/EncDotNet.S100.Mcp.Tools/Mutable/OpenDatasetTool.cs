using System.ComponentModel;
using System.Diagnostics;
using EncDotNet.S100.Datasets.Pipelines.Query;

namespace EncDotNet.S100.Mcp.Tools.Mutable;

/// <summary>Request payload for <see cref="OpenDatasetTool"/>.</summary>
public sealed record OpenDatasetRequest(
    [property: Description("Local filesystem path to a dataset file or an exchange set (folder containing a catalogue, or a .zip of one).")] string Path,
    [property: Description("Optional explicit product-spec hint (e.g. \"S-102\") for single-file loads; ignored for exchange sets.")] string? Spec = null);

/// <summary>Metadata for a single dataset added by an open operation.</summary>
public sealed record OpenedDataset(
    [property: Description("Catalog id (stable for the host session).")] string Id,
    [property: Description("Canonical product specification name (e.g. \"S-101\").")] string Spec,
    [property: Description("South edge of the bounds (decimal degrees, WGS-84).")] double SouthLatitude,
    [property: Description("West edge of the bounds (decimal degrees, WGS-84).")] double WestLongitude,
    [property: Description("North edge of the bounds (decimal degrees, WGS-84).")] double NorthLatitude,
    [property: Description("East edge of the bounds (decimal degrees, WGS-84).")] double EastLongitude);

/// <summary>Result payload for <see cref="OpenDatasetTool"/>.</summary>
public sealed record OpenDatasetResult(
    [property: Description("The path that was opened.")] string Path,
    [property: Description("How the path was loaded: \"file\" or \"exchangeSet\".")] string Kind,
    [property: Description("Number of datasets newly added to the catalog.")] int Count,
    [property: Description("Wall-clock duration of the catalog load hot path, in milliseconds.")] double LoadDurationMs,
    [property: Description("Whether an exchange-set load did not settle before the host's ceiling.")] bool TimedOut,
    [property: Description("The datasets added to the catalog by this operation.")] IReadOnlyList<OpenedDataset> Datasets);

/// <summary>
/// Mutating tool that loads a dataset file or exchange set into the session's
/// catalog, so an agent can add data mid-session. Renderer-neutral: it drives
/// the shared <see cref="IMutableDatasetCatalog"/>. Returns the resulting
/// catalog id(s) plus spec and bounding box.
/// </summary>
public sealed class OpenDatasetTool
{
    /// <summary>The MCP tool name as exposed to clients.</summary>
    public const string Name = "open_dataset";

    private readonly IMutableDatasetCatalog _catalog;

    /// <summary>Creates the tool bound to a mutable catalog.</summary>
    public OpenDatasetTool(IMutableDatasetCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
    }

    /// <summary>Loads the requested dataset and returns the resulting catalog entries.</summary>
    public async Task<ToolResult<OpenDatasetResult>> InvokeAsync(
        OpenDatasetRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var path = request.Path;
        if (string.IsNullOrWhiteSpace(path))
        {
            return ToolResult<OpenDatasetResult>.Err(new InvalidArgument(
                "path", "a non-empty filesystem path is required"));
        }
        path = path.Trim();

        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return ToolResult<OpenDatasetResult>.Err(new InvalidArgument(
                "path", $"no file or directory exists at '{path}'"));
        }

        var stopwatch = Stopwatch.StartNew();
        DatasetLoadOutcome outcome;
        try
        {
            outcome = await _catalog.LoadAsync(path, request.Spec, ct).ConfigureAwait(false);
        }
        catch (DatasetCatalogNotReadyException ex)
        {
            // The host's load path is not initialised yet (e.g. the viewer
            // before its window has wired up the loader). Surface a clean,
            // retryable host_not_ready rather than an internal error.
            return ToolResult<OpenDatasetResult>.Err(new HostNotReady(ex.Message));
        }
        var loadDurationMs = stopwatch.Elapsed.TotalMilliseconds;

        if (outcome.Added.Count == 0)
        {
            return ToolResult<OpenDatasetResult>.Err(new DatasetLoadFailed(
                outcome.Kind == DatasetSourceKind.File
                    ? "the file loaded but produced no portrayable dataset"
                    : "the exchange set contained no datasets the host can portray"));
        }

        // Enrich the ids with spec / bounds from the post-load catalog snapshot.
        var added = outcome.Added.Count;
        var snapshot = _catalog.Datasets;
        var addedIds = outcome.Added.Select(id => id.Value).ToHashSet(StringComparer.Ordinal);
        var datasets = snapshot
            .Where(d => addedIds.Contains(d.Id.Value))
            .Select(d => new OpenedDataset(
                d.Id.Value,
                d.Spec.Name,
                d.Bounds.SouthLatitude,
                d.Bounds.WestLongitude,
                d.Bounds.NorthLatitude,
                d.Bounds.EastLongitude))
            .ToList();

        return ToolResult<OpenDatasetResult>.Ok(new OpenDatasetResult(
            Path: path,
            Kind: outcome.Kind == DatasetSourceKind.File ? "file" : "exchangeSet",
            Count: added,
            LoadDurationMs: loadDurationMs,
            TimedOut: outcome.TimedOut,
            Datasets: datasets));
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EncDotNet.S100.Mcp.Tools;
using EncDotNet.S100.Mcp.Tools.Catalog;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>Request payload for <see cref="OpenDatasetTool"/>.</summary>
/// <param name="Path">Local filesystem path to a dataset file or an exchange set (folder / ZIP).</param>
/// <param name="Spec">Optional explicit product-spec hint (e.g. <c>"S-102"</c>) for single-file loads.</param>
internal sealed record OpenDatasetRequest(string? Path = null, string? Spec = null);

/// <summary>Metadata for a single dataset surfaced by an open operation.</summary>
/// <param name="Id">Catalog id (stable for the host session).</param>
/// <param name="Spec">Canonical product specification name (e.g. <c>"S-101"</c>).</param>
/// <param name="SouthLatitude">South edge of the bounds (decimal degrees, WGS-84).</param>
/// <param name="WestLongitude">West edge of the bounds (decimal degrees, WGS-84).</param>
/// <param name="NorthLatitude">North edge of the bounds (decimal degrees, WGS-84).</param>
/// <param name="EastLongitude">East edge of the bounds (decimal degrees, WGS-84).</param>
internal sealed record OpenedDataset(
    string Id,
    string Spec,
    double SouthLatitude,
    double WestLongitude,
    double NorthLatitude,
    double EastLongitude);

/// <summary>Result payload for <see cref="OpenDatasetTool"/>.</summary>
/// <param name="Path">The path that was opened.</param>
/// <param name="Kind">How the path was loaded — <c>"file"</c> or <c>"exchangeSet"</c>.</param>
/// <param name="Count">Number of datasets newly added to the catalog.</param>
/// <param name="LoadDurationMs">Wall-clock duration of the load hot path, in milliseconds.</param>
/// <param name="TimedOut"><see langword="true"/> when an exchange-set load did not quiesce before the max wait.</param>
/// <param name="Datasets">The datasets observed added to the catalog during the operation.</param>
internal sealed record OpenDatasetResult(
    string Path,
    string Kind,
    int Count,
    double LoadDurationMs,
    bool TimedOut,
    IReadOnlyList<OpenedDataset> Datasets);

/// <summary>
/// MCP tool that loads a dataset file or exchange set into the live
/// viewer using its existing GUI load code path, so automation agents can
/// measure the load hot path. Returns the resulting catalog id(s) plus
/// basic metadata (spec, bbox) and the measured load duration.
/// </summary>
internal sealed class OpenDatasetTool
{
    /// <summary>The MCP tool name as exposed to clients.</summary>
    public const string Name = "open_dataset";

    private readonly IDatasetCatalog _catalog;
    private readonly IDatasetLoadGateway _gateway;
    private readonly int _quietMs;
    private readonly int _maxWaitMs;

    /// <summary>Creates the tool with production quiescence timings.</summary>
    public OpenDatasetTool(IDatasetCatalog catalog, IDatasetLoadGateway gateway)
        : this(catalog, gateway, quietMs: 600, maxWaitMs: 30_000)
    {
    }

    /// <summary>
    /// Test seam: allows tests to shorten the exchange-set quiescence
    /// debounce so timing paths are exercised quickly.
    /// </summary>
    /// <param name="catalog">The dataset catalog to diff before / after the load.</param>
    /// <param name="gateway">The UI-thread load gateway.</param>
    /// <param name="quietMs">Quiet window (no new datasets) that signals quiescence.</param>
    /// <param name="maxWaitMs">Hard ceiling on the exchange-set quiescence wait.</param>
    internal OpenDatasetTool(IDatasetCatalog catalog, IDatasetLoadGateway gateway, int quietMs, int maxWaitMs)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(gateway);
        _catalog = catalog;
        _gateway = gateway;
        _quietMs = quietMs;
        _maxWaitMs = maxWaitMs;
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

        if (!_gateway.IsReady)
        {
            return ToolResult<OpenDatasetResult>.Err(new MapNotReady(
                "the dataset loader has not been initialised yet"));
        }

        using var _ = await _gateway.LockAsync(ct).ConfigureAwait(false);

        var kind = _gateway.Classify(path);
        var before = _catalog.Datasets.Select(d => d.Id.Value).ToHashSet(StringComparer.Ordinal);

        // Subscribe BEFORE triggering so synchronous adds (single-file load)
        // and the first asynchronous add (exchange set) are both observed.
        var activity = new SemaphoreSlim(0);
        void OnChanged(object? sender, DatasetCatalogChangedEventArgs e)
        {
            if (e.Kind is DatasetCatalogChangeKind.Added or DatasetCatalogChangeKind.Batch)
            {
                activity.Release();
            }
        }
        _catalog.Changed += OnChanged;

        var stopwatch = Stopwatch.StartNew();
        var timedOut = false;
        try
        {
            if (kind == DatasetPathKind.File)
            {
                var recognised = await _gateway.LoadFileAsync(path, request.Spec, ct).ConfigureAwait(false);
                if (!recognised)
                {
                    return ToolResult<OpenDatasetResult>.Err(new InvalidArgument(
                        "path", $"the file type of '{path}' is not a recognised S-100 product"));
                }
                // Single-file load updates the catalog synchronously during
                // the awaited LoadAsync, so no quiescence wait is needed.
            }
            else
            {
                var dispatched = await _gateway.TriggerExchangeSetAsync(path, ct).ConfigureAwait(false);
                if (dispatched == 0)
                {
                    // The exchange set contained no datasets this viewer can
                    // read — fail fast rather than waiting out the quiet window.
                    return ToolResult<OpenDatasetResult>.Err(new DatasetLoadFailed(
                        "the exchange set contained no datasets this viewer can portray"));
                }
                timedOut = await WaitForQuiescenceAsync(
                    activity, dispatched, () => CountAdded(before), ct).ConfigureAwait(false);
            }
        }
        finally
        {
            stopwatch.Stop();
            _catalog.Changed -= OnChanged;
            // The SemaphoreSlim is intentionally NOT disposed: a catalog
            // event could still be racing toward OnChanged after the
            // unsubscribe above, and Release on a disposed semaphore throws.
            // It uses no wait handle (only WaitAsync), so it needs no
            // deterministic disposal.
        }

        var added = _catalog.Datasets
            .Where(d => !before.Contains(d.Id.Value))
            .Select(d => new OpenedDataset(
                d.Id.Value,
                d.Spec.Name,
                d.Bounds.SouthLatitude,
                d.Bounds.WestLongitude,
                d.Bounds.NorthLatitude,
                d.Bounds.EastLongitude))
            .ToList();

        if (added.Count == 0)
        {
            return ToolResult<OpenDatasetResult>.Err(new DatasetLoadFailed(
                kind == DatasetPathKind.File
                    ? "the file loaded but produced no portrayable dataset"
                    : "the exchange set contained no datasets this viewer can portray"));
        }

        return ToolResult<OpenDatasetResult>.Ok(new OpenDatasetResult(
            Path: path,
            Kind: kind == DatasetPathKind.File ? "file" : "exchangeSet",
            Count: added.Count,
            LoadDurationMs: stopwatch.Elapsed.TotalMilliseconds,
            TimedOut: timedOut,
            Datasets: added));
    }

    private int CountAdded(System.Collections.Generic.HashSet<string> before)
        => _catalog.Datasets.Count(d => !before.Contains(d.Id.Value));

    /// <summary>
    /// Waits for an exchange-set load (which dispatched
    /// <paramref name="expectedCount"/> datasets fire-and-forget) to settle:
    /// returns once every dispatched dataset has been added, or a full quiet
    /// window has elapsed after at least one add (some dispatched loads may
    /// have failed). Returns <see langword="true"/> when the
    /// <see cref="_maxWaitMs"/> ceiling is hit first (timed out).
    /// </summary>
    /// <remarks>
    /// Crucially, a quiet window with <em>zero</em> adds does NOT resolve as
    /// quiescent: because datasets were dispatched, "no events yet" means the
    /// first load is still in flight (a slow first load must not be reported
    /// as a failure). Only the max-wait ceiling ends that case.
    /// </remarks>
    private async Task<bool> WaitForQuiescenceAsync(
        SemaphoreSlim activity, int expectedCount, Func<int> addedCount, CancellationToken ct)
    {
        var deadline = Stopwatch.StartNew();
        while (true)
        {
            if (addedCount() >= expectedCount)
            {
                // Every dispatched dataset arrived.
                return false;
            }

            var remaining = _maxWaitMs - (int)deadline.ElapsedMilliseconds;
            if (remaining <= 0)
            {
                return true;
            }

            var wait = Math.Min(_quietMs, remaining);
            var signalled = await activity.WaitAsync(wait, ct).ConfigureAwait(false);
            if (!signalled)
            {
                if (wait < _quietMs)
                {
                    // The wait was truncated by the max-wait deadline, not a
                    // genuine quiet window — report a timeout.
                    return true;
                }
                if (addedCount() >= 1)
                {
                    // A full quiet window elapsed after at least one add; the
                    // remaining dispatched loads must have failed. Settled.
                    return false;
                }
                // No adds yet though datasets were dispatched: the first load
                // is still in flight. Keep waiting up to the max.
                continue;
            }

            // Drain any adds that piled up so the next wait measures a fresh
            // quiet window rather than immediately returning.
            while (activity.Wait(0))
            {
            }
        }
    }
}

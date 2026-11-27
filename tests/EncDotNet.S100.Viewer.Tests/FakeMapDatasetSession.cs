using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Viewer.Services;
using Mapsui;
using Mapsui.Layers;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// An <see cref="IMapDatasetSession"/> that records what it was asked to do,
/// for the split-map loader tests (#1731). It holds the datasets it was given
/// and their paint order, and draws a dataset when it is rendered or
/// published.
/// </summary>
internal sealed class FakeMapDatasetSession : IMapDatasetSession
{
    private readonly Dictionary<MapDatasetId, MapDataset> _datasets = [];
    private readonly List<MapDatasetId> _order = [];

    /// <summary>Every call, in order, as "Name(argument)".</summary>
    public List<string> Calls { get; } = [];

    /// <summary>Every dataset state set on this session, in order.</summary>
    public List<MapDataset> SetDatasets { get; } = [];

    /// <summary>The datasets drawn on this map.</summary>
    public HashSet<MapDatasetId> Drawn { get; } = [];

    /// <summary>The datasets the session holds, bottom to top.</summary>
    public IReadOnlyList<MapDatasetId> Order => _order;

    /// <summary>The last mariner settings set.</summary>
    public MarinerSettings? Mariner { get; private set; }

    /// <summary>The last clock set.</summary>
    public DateTime? CurrentTime { get; private set; }

    /// <summary>The viewport the last presentation refresh was limited to.</summary>
    public MRect? LastRefreshViewport { get; private set; }

    /// <summary>The time state <see cref="GetTimeSnapshot"/> reports.</summary>
    public MapsuiMapTimeSnapshot TimeSnapshot { get; set; } = MapsuiMapTimeSnapshot.Empty;

    /// <summary>When set, renders throw this.</summary>
    public Exception? RenderFailure { get; set; }

    /// <inheritdoc />
    public TimeProvider Clock { get; set; } = TimeProvider.System;

    /// <inheritdoc />
    public IS100MapQuery Query => throw new NotSupportedException();

    /// <inheritdoc />
    public event EventHandler? LayersChanged;

    /// <inheritdoc />
    public event EventHandler? TimeRangeChanged;

    /// <inheritdoc />
    public event EventHandler<MapSessionCurrentTimeEventArgs>? CurrentTimeChanged;

    /// <inheritdoc />
    public event EventHandler<MapSessionDatasetRenderEventArgs>? DatasetRenderStarted;

    /// <inheritdoc />
    public event EventHandler<MapSessionDatasetRenderEventArgs>? DatasetRenderCompleted;

    /// <inheritdoc />
    public event EventHandler<MapSessionDatasetRenderFailedEventArgs>? DatasetRenderFailed;

    /// <summary>Whether anything is subscribed to the session's layer or time events.</summary>
    public bool HasSubscribers =>
        LayersChanged is not null || TimeRangeChanged is not null || CurrentTimeChanged is not null
        || DatasetRenderStarted is not null || DatasetRenderCompleted is not null || DatasetRenderFailed is not null;

    /// <summary>Raises <see cref="LayersChanged"/>.</summary>
    public void RaiseLayersChanged() => LayersChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>Raises <see cref="TimeRangeChanged"/> after replacing the time state.</summary>
    /// <param name="snapshot">The new time state.</param>
    public void RaiseTimeRangeChanged(MapsuiMapTimeSnapshot snapshot)
    {
        TimeSnapshot = snapshot;
        TimeRangeChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public MapsuiMapDatasetSnapshot? GetDataset(MapDatasetId datasetId) =>
        _datasets.TryGetValue(datasetId, out var dataset) ? Snapshot(dataset) : null;

    /// <inheritdoc />
    public IReadOnlyList<MapsuiMapDatasetSnapshot> GetDatasets() =>
        _order.Select(id => Snapshot(_datasets[id])).ToArray();

    /// <inheritdoc />
    public IReadOnlyList<LayerStackEntry> GetLayerStackEntries() => [];

    /// <inheritdoc />
    public IReadOnlyList<ILayer> GetStackedLayers() => [];

    /// <inheritdoc />
    public MapsuiMapTimeSnapshot GetTimeSnapshot() => TimeSnapshot;

    /// <inheritdoc />
    public void SetDataset(MapDataset dataset, int? minimumDisplayScale, int? maximumDisplayScale)
    {
        Calls.Add($"SetDataset({dataset.Id.Value})");
        SetDatasets.Add(dataset);
        if (!_datasets.ContainsKey(dataset.Id))
            _order.Add(dataset.Id);
        _datasets[dataset.Id] = dataset;
    }

    /// <inheritdoc />
    public Task<bool> PublishStoredAsync(MapDatasetId datasetId, MapsuiDatasetResult result, CancellationToken cancellationToken)
    {
        Calls.Add($"PublishStored({datasetId.Value})");
        if (!_datasets.ContainsKey(datasetId))
            return Task.FromResult(false);
        Drawn.Add(datasetId);
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<MapsuiDatasetResult?> RenderAsync(MapDatasetId datasetId, MapPresentationState presentation, CancellationToken cancellationToken)
    {
        Calls.Add($"Render({datasetId.Value})");
        if (RenderFailure is { } failure)
            throw failure;
        if (!_datasets.ContainsKey(datasetId))
            return Task.FromResult<MapsuiDatasetResult?>(null);
        Drawn.Add(datasetId);
        return Task.FromResult<MapsuiDatasetResult?>(Result(datasetId));
    }

    /// <inheritdoc />
    public void ClearLayers(MapDatasetId datasetId)
    {
        Calls.Add($"ClearLayers({datasetId.Value})");
        Drawn.Remove(datasetId);
    }

    /// <inheritdoc />
    public Task<bool> RefreshAsync(MapPresentationState presentation, MRect? viewport, CancellationToken cancellationToken)
    {
        Calls.Add("Refresh");
        LastRefreshViewport = viewport;
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<bool> RefreshDepthAdjustedAsync(MapPresentationState presentation, MRect? viewport, CancellationToken cancellationToken)
    {
        Calls.Add("RefreshDepthAdjusted");
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<bool> RefreshRevealedAsync(MRect viewport, CancellationToken cancellationToken)
    {
        Calls.Add("RefreshRevealed");
        return Task.FromResult(false);
    }

    /// <inheritdoc />
    public Task RefreshTimeAsync(MapPresentationState presentation, CancellationToken cancellationToken)
    {
        Calls.Add("RefreshTime");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void SetCurrentTime(DateTime time)
    {
        Calls.Add($"SetCurrentTime({time:O})");
        CurrentTime = time;
    }

    /// <inheritdoc />
    public void SetMarinerSettings(MarinerSettings mariner)
    {
        Calls.Add("SetMarinerSettings");
        Mariner = mariner;
    }

    /// <inheritdoc />
    public void SetOrder(IReadOnlyList<MapDatasetId> datasetIds)
    {
        Calls.Add($"SetOrder({string.Join(",", datasetIds.Select(id => id.Value))})");
        var replacement = datasetIds.Where(_datasets.ContainsKey).Distinct().ToList();
        replacement.AddRange(_order.Where(id => !replacement.Contains(id)));
        _order.Clear();
        _order.AddRange(replacement);
    }

    /// <inheritdoc />
    public int RemoveDatasets(IReadOnlyCollection<MapDatasetId> datasetIds, bool preserveState, bool removeProcessor)
    {
        Calls.Add($"RemoveDatasets({string.Join(",", datasetIds.Select(id => id.Value))}, preserve={preserveState}, processor={removeProcessor})");
        var removed = 0;
        foreach (var id in datasetIds)
        {
            if (!_datasets.ContainsKey(id))
                continue;
            removed++;
            Drawn.Remove(id);
            if (!preserveState)
            {
                _datasets.Remove(id);
                _order.Remove(id);
            }
        }

        return removed;
    }

    /// <summary>A result to publish or return from a render.</summary>
    /// <param name="datasetId">The dataset.</param>
    /// <returns>An empty result.</returns>
    public static MapsuiDatasetResult Result(MapDatasetId datasetId) => new()
    {
        Layers = [],
        Extent = new MRect(0, 0, 1, 1),
        Info = datasetId.Value,
        Spec = new SpecRef("S-101", new SpecVersion(1, 0, 0)),
    };

    private MapsuiMapDatasetSnapshot Snapshot(MapDataset dataset) => new()
    {
        Dataset = dataset,
        Layers = [],
        IsDrawing = Drawn.Contains(dataset.Id),
    };
}

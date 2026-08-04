using EncDotNet.S100.Datasets.Pipelines;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// Default <see cref="IS100MapSession"/> implementation. Owns the composed
/// dataset-layer session, processor ownership, and navigation surface attached
/// to a single <see cref="Mapsui.Map"/>; disposing it releases all of them.
/// </summary>
internal sealed class S100MapSession : IS100MapSession
{
    private readonly DatasetProcessorOwner _processorOwner;
    private readonly MapsuiMapSession _session;
    private readonly MapsuiMapNavigator _navigator;
    private MapPresentationState _presentation = MapPresentationState.Default;
    private bool _disposed;

    internal S100MapSession(
        DatasetProcessorOwner processorOwner,
        MapsuiMapSession session,
        MapsuiMapNavigator navigator)
    {
        _processorOwner = processorOwner
            ?? throw new ArgumentNullException(nameof(processorOwner));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));

        _session.LayersChanged += OnLayersChanged;
        _session.TimeRangeChanged += OnTimeRangeChanged;
        _session.CurrentTimeChanged += OnCurrentTimeChanged;
        _session.DatasetRenderStarted += OnDatasetRenderStarted;
        _session.DatasetRenderCompleted += OnDatasetRenderCompleted;
        _session.DatasetRenderFailed += OnDatasetRenderFailed;
    }

    /// <inheritdoc />
    public MapsuiMapSession Session
    {
        get
        {
            ThrowIfDisposed();
            return _session;
        }
    }

    /// <inheritdoc />
    public MapsuiMapNavigator Navigator
    {
        get
        {
            ThrowIfDisposed();
            return _navigator;
        }
    }

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

    private MapPresentationState CurrentPresentation => Volatile.Read(ref _presentation);

    /// <inheritdoc />
    public async Task<bool> AddDatasetAsync(
        MapDataset dataset,
        IDatasetProcessor processor,
        int? minimumDisplayScale = null,
        int? maximumDisplayScale = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(processor);

        if (!_processorOwner.TryRegister(dataset.Id, processor))
            return false;

        try
        {
            _session.SetDataset(dataset, minimumDisplayScale, maximumDisplayScale);
            var result = await _session.RenderAsync(
                dataset.Id, CurrentPresentation, cancellationToken).ConfigureAwait(true);

            // RenderAsync returns null when the dataset was removed or its
            // processor changed while rendering. If the session no longer owns
            // the processor we registered, the add did not take effect (and the
            // processor was already retired by whoever removed it), so surface
            // it as a failure through the rollback path rather than reporting a
            // success that installed no layers. A null result while we still own
            // the processor means a concurrent render superseded ours but the
            // dataset is present and rendered, which is a successful add.
            if (result is null && !_processorOwner.Owns(dataset.Id, processor))
            {
                throw new InvalidOperationException(
                    $"Dataset '{dataset.Id.Value}' was removed before its layers were installed.");
            }
        }
        catch
        {
            // Roll the registration back so a failed add leaves no orphaned
            // processor or partial dataset state behind. Drop any session state
            // without touching the owner, then retire the processor explicitly:
            // if SetDataset failed and dropped the entry, the session's own
            // removal would not retire the processor, leaving it registered and
            // undisposed in the owner.
            _session.RemoveDataset(
                dataset.Id, preserveState: false, removeProcessor: false);
            _processorOwner.Remove(dataset.Id, processor);
            throw;
        }

        return true;
    }

    /// <inheritdoc />
    public bool RemoveDataset(MapDatasetId datasetId)
    {
        ThrowIfDisposed();
        return _session.RemoveDataset(datasetId);
    }

    /// <inheritdoc />
    public IReadOnlyList<MapsuiMapDatasetSnapshot> GetDatasets()
    {
        ThrowIfDisposed();
        return _session.GetDatasets();
    }

    /// <inheritdoc />
    public MapsuiMapDatasetSnapshot? GetDataset(MapDatasetId datasetId)
    {
        ThrowIfDisposed();
        return _session.GetDataset(datasetId);
    }

    /// <inheritdoc />
    public void SetVisible(MapDatasetId datasetId, bool isVisible) =>
        ApplyDatasetState(datasetId, isVisible: isVisible);

    /// <inheritdoc />
    public void SetActive(MapDatasetId datasetId, bool isActive) =>
        ApplyDatasetState(datasetId, isActive: isActive);

    /// <inheritdoc />
    public void SetOpacity(MapDatasetId datasetId, double opacity)
    {
        // Fail fast on disposal first (consistent with the other members), then
        // validate at the public entry point so callers get an
        // ArgumentOutOfRangeException naming `opacity` without allocating a new
        // MapDataset.
        ThrowIfDisposed();
        if (!double.IsFinite(opacity))
        {
            throw new ArgumentOutOfRangeException(
                nameof(opacity), opacity, "Opacity must be a finite value in the range 0..1.");
        }
        ArgumentOutOfRangeException.ThrowIfLessThan(opacity, 0.0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(opacity, 1.0);
        ApplyDatasetState(datasetId, opacity: opacity);
    }

    /// <inheritdoc />
    public void SetOrder(IReadOnlyList<MapDatasetId> bottomToTopDatasetIds)
    {
        ThrowIfDisposed();
        _session.SetOrder(bottomToTopDatasetIds);
    }

    /// <inheritdoc />
    public async Task SetPresentationAsync(
        MapPresentationState presentation,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(presentation);

        _session.SetMarinerSettings(presentation.Mariner);
        Volatile.Write(ref _presentation, presentation);
        await _session.RefreshAsync(
            presentation, cancellationToken: cancellationToken).ConfigureAwait(true);
    }

    /// <inheritdoc />
    public void SetTime(DateTime time)
    {
        ThrowIfDisposed();
        _session.SetCurrentTime(time);
    }

    /// <inheritdoc />
    public async Task SetTimeAsync(
        DateTime time,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        _session.SetCurrentTime(time);
        await _session.RefreshTimeAsync(
            CurrentPresentation, cancellationToken).ConfigureAwait(true);
    }

    /// <inheritdoc />
    public MapsuiMapTimeSnapshot GetTimeSnapshot()
    {
        ThrowIfDisposed();
        return _session.GetTimeSnapshot();
    }

    /// <inheritdoc />
    public void ZoomToDataset(MapDatasetId datasetId)
    {
        ThrowIfDisposed();
        if (_session.GetDataset(datasetId)?.Extent is { } extent)
            _navigator.ZoomToExtent(extent);
    }

    /// <summary>
    /// Re-applies one dataset's display state through the session by copying its
    /// current snapshot with the requested field overridden — the same way a
    /// host view-model would (visibility / active state / opacity are carried on
    /// <see cref="MapDataset"/>, which the session re-composes on
    /// <see cref="MapsuiMapSession.SetDataset"/>).
    /// </summary>
    private void ApplyDatasetState(
        MapDatasetId datasetId,
        bool? isVisible = null,
        bool? isActive = null,
        double? opacity = null)
    {
        ThrowIfDisposed();
        if (_session.GetDataset(datasetId) is not { } snapshot)
            return;

        var current = snapshot.Dataset;
        var newVisible = isVisible ?? current.IsVisible;
        var newActive = isActive ?? current.IsActive;
        var newOpacity = opacity ?? current.Opacity;

        // Skip the allocation and full SetDataset recomposition when nothing
        // actually changed (e.g. a UI re-applying the same value).
        if (newVisible == current.IsVisible
            && newActive == current.IsActive
            && newOpacity == current.Opacity)
        {
            return;
        }

        var updated = new MapDataset(
            current.Id,
            current.Name,
            current.Metadata,
            newVisible,
            newActive,
            newOpacity,
            current.AvailableTimes,
            current.CurrentTime,
            current.SubLayers,
            current.Validation,
            current.VersionAssessment);
        _session.SetDataset(
            updated, snapshot.MinimumDisplayScale, snapshot.MaximumDisplayScale);
    }

    private void OnLayersChanged(object? sender, EventArgs e) =>
        LayersChanged?.Invoke(this, e);

    private void OnTimeRangeChanged(object? sender, EventArgs e) =>
        TimeRangeChanged?.Invoke(this, e);

    private void OnCurrentTimeChanged(object? sender, MapSessionCurrentTimeEventArgs e) =>
        CurrentTimeChanged?.Invoke(this, e);

    private void OnDatasetRenderStarted(object? sender, MapSessionDatasetRenderEventArgs e) =>
        DatasetRenderStarted?.Invoke(this, e);

    private void OnDatasetRenderCompleted(object? sender, MapSessionDatasetRenderEventArgs e) =>
        DatasetRenderCompleted?.Invoke(this, e);

    private void OnDatasetRenderFailed(object? sender, MapSessionDatasetRenderFailedEventArgs e) =>
        DatasetRenderFailed?.Invoke(this, e);

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _session.LayersChanged -= OnLayersChanged;
        _session.TimeRangeChanged -= OnTimeRangeChanged;
        _session.CurrentTimeChanged -= OnCurrentTimeChanged;
        _session.DatasetRenderStarted -= OnDatasetRenderStarted;
        _session.DatasetRenderCompleted -= OnDatasetRenderCompleted;
        _session.DatasetRenderFailed -= OnDatasetRenderFailed;

        // Dispose the session before the owner so no in-flight render holds a
        // lease past the owner's disposal, which retires and disposes every
        // registered processor.
        _session.Dispose();
        _processorOwner.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}

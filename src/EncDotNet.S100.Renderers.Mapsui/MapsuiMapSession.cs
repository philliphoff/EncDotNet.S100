using System.Runtime.CompilerServices;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Pipelines;
using Mapsui;
using Mapsui.Layers;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// Owns the ordinary lifecycle of processor-rendered dataset layers in a
/// Mapsui map.
/// </summary>
/// <remarks>
/// <para>
/// The session owns generated layer replacement, removal, bottom-to-top order,
/// dataset and sub-layer display state, S-98 cross-product ordering and
/// suppression, whole-cell scale windows, and overlapping-cell suppression.
/// Processor lifetime remains protected by the supplied
/// <see cref="DatasetProcessorOwner"/>; every render uses a lease.
/// Cross-product composition follows S-98 Edition 2.0.0 Main §9.2.1 and
/// Annex A §8.4.1.
/// </para>
/// <para>
/// Rendering work runs on a worker thread. The continuation that installs
/// layers must resume on the map-owning thread; UI hosts should call and await
/// <see cref="RenderAsync"/> from that thread.
/// </para>
/// </remarks>
public sealed class MapsuiMapSession : IDisposable
{
    private readonly object _sync = new();
    private readonly MapsuiLayerBands _layerBands;
    private readonly DatasetProcessorOwner _processorOwner;
    private readonly MapsuiDatasetRenderer _renderer;
    private readonly IInteroperabilityAuthorityProvider _authorityProvider;
    private readonly Dictionary<MapDatasetId, Entry> _entries = [];
    private readonly List<MapDatasetId> _order = [];
    private readonly ConditionalWeakTable<ILayer, LayerVisibilityRange> _visibilityRanges = new();
    private readonly SemaphoreSlim _renderGate = new(1, 1);
    private IReadOnlyList<LayerStackEntry> _stackEntries = [];
    private IReadOnlyList<ILayer> _stackedLayers = [];
    private MapsuiMapTimeSnapshot _time = MapsuiMapTimeSnapshot.Empty;
    private MarinerSettings _mariner = MarinerSettings.Default;
    private CancellationTokenSource? _timeRefreshCts;
    private CancellationTokenSource? _presentationRefreshCts;
    private bool _ignoreScaleMinimum;
    private bool _disposed;

    private static readonly TimeSpan TimeRefreshDebounceWindow =
        TimeSpan.FromMilliseconds(100);

    /// <summary>Creates a Mapsui dataset-layer session.</summary>
    /// <param name="layerBands">The map layer bands the session will mutate.</param>
    /// <param name="processorOwner">The owner from which render leases are acquired.</param>
    /// <param name="renderer">The processor-to-Mapsui renderer.</param>
    public MapsuiMapSession(
        MapsuiLayerBands layerBands,
        DatasetProcessorOwner processorOwner,
        MapsuiDatasetRenderer renderer)
        : this(
            layerBands,
            processorOwner,
            renderer,
            new InteroperabilityAuthorityProvider(new InteroperabilityAuthority()))
    {
    }

    /// <summary>Creates a Mapsui dataset-layer session.</summary>
    /// <param name="layerBands">The map layer bands the session will mutate.</param>
    /// <param name="processorOwner">The owner from which render leases are acquired.</param>
    /// <param name="renderer">The processor-to-Mapsui renderer.</param>
    /// <param name="authorityProvider">
    /// The runtime S-98 cross-product ordering and suppression authority.
    /// </param>
    public MapsuiMapSession(
        MapsuiLayerBands layerBands,
        DatasetProcessorOwner processorOwner,
        MapsuiDatasetRenderer renderer,
        IInteroperabilityAuthorityProvider authorityProvider)
    {
        ArgumentNullException.ThrowIfNull(layerBands);
        ArgumentNullException.ThrowIfNull(processorOwner);
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(authorityProvider);
        _layerBands = layerBands;
        _processorOwner = processorOwner;
        _renderer = renderer;
        _authorityProvider = authorityProvider;
        _authorityProvider.CurrentChanged += OnAuthorityChanged;
    }

    /// <summary>
    /// Raised after the final dataset-band projection changes.
    /// </summary>
    public event EventHandler? LayersChanged;

    /// <summary>Raised after the aggregate registered time range changes.</summary>
    public event EventHandler? TimeRangeChanged;

    /// <summary>Raised after the global map clock changes.</summary>
    public event EventHandler<MapSessionCurrentTimeEventArgs>? CurrentTimeChanged;

    /// <summary>
    /// Raised once a processor lease is held and a registered dataset is about
    /// to render, for single renders and each dataset of a coalesced refresh.
    /// A dataset removed or unregistered before its lease is acquired raises no
    /// lifecycle event.
    /// </summary>
    public event EventHandler<MapSessionDatasetRenderEventArgs>? DatasetRenderStarted;

    /// <summary>
    /// Raised after a registered dataset's generated layers are installed by a
    /// render that was not superseded or removed mid-flight.
    /// </summary>
    public event EventHandler<MapSessionDatasetRenderEventArgs>? DatasetRenderCompleted;

    /// <summary>
    /// Raised when one dataset fails during a coalesced time or presentation
    /// refresh. Other registered datasets continue refreshing. A single
    /// <see cref="RenderAsync"/> surfaces its error by throwing instead.
    /// </summary>
    public event EventHandler<MapSessionDatasetRenderFailedEventArgs>? DatasetRenderFailed;

    /// <summary>
    /// Sets the mariner choices consumed by S-98 cross-product rules and
    /// dataset scale-window projection.
    /// </summary>
    /// <param name="mariner">The current immutable mariner settings.</param>
    public void SetMarinerSettings(MarinerSettings mariner)
    {
        ArgumentNullException.ThrowIfNull(mariner);

        lock (_sync)
        {
            ThrowIfDisposed();
            var previousMariner = _mariner;
            var previousIgnoreScaleMinimum = _ignoreScaleMinimum;
            _mariner = mariner;
            _ignoreScaleMinimum = mariner.IgnoreScaleMinimum;
            try
            {
                ComposeLayers();
            }
            catch
            {
                _mariner = previousMariner;
                _ignoreScaleMinimum = previousIgnoreScaleMinimum;
                throw;
            }
        }

        LayersChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Adds or updates renderer-neutral state for a dataset.
    /// </summary>
    /// <param name="dataset">The authoritative host state.</param>
    /// <param name="minimumDisplayScale">
    /// Optional catalogue coarsest display-scale denominator.
    /// </param>
    /// <param name="maximumDisplayScale">
    /// Optional catalogue compilation-scale denominator.
    /// </param>
    public void SetDataset(
        MapDataset dataset,
        int? minimumDisplayScale = null,
        int? maximumDisplayScale = null)
    {
        ArgumentNullException.ThrowIfNull(dataset);

        TimePolicy? timePolicy = null;
        if (_processorOwner.TryAcquire(dataset.Id, out var processorLease))
        {
            using (processorLease)
            {
                if (processorLease.Processor is ITimeAwareDatasetProcessor timeAware)
                {
                    timePolicy = TimePolicy.TryCreate(
                        dataset.Metadata.Spec.Name,
                        timeAware.AvailableTimes);
                }
            }
        }

        var rangeChanged = false;
        DateTime? changedCurrent = null;
        lock (_sync)
        {
            ThrowIfDisposed();
            var created = false;
            if (!_entries.TryGetValue(dataset.Id, out var entry))
            {
                entry = new Entry(dataset);
                _entries.Add(dataset.Id, entry);
                _order.Add(dataset.Id);
                created = true;
            }

            var previousDataset = entry.Dataset;
            var previousTimePolicy = entry.TimePolicy;
            var previousRenderedTime = entry.RenderedTime;
            var previousMinimum = entry.CatalogueMinimumDisplayScale;
            var previousMaximum = entry.MaximumDisplayScale;
            entry.Dataset = ReconcileDatasetState(
                dataset,
                entry.LayerKeys,
                entry.Dataset.SubLayers);
            entry.TimePolicy = timePolicy;
            if (timePolicy is not null)
            {
                entry.Dataset = CopyDataset(
                    entry.Dataset,
                    entry.Dataset.SubLayers,
                    timePolicy.AvailableTimes,
                    created ? null : previousDataset.CurrentTime);
            }
            else
            {
                entry.Dataset = CopyDataset(
                    entry.Dataset,
                    entry.Dataset.SubLayers,
                    [],
                    currentTime: null);
                entry.RenderedTime = null;
            }
            entry.CatalogueMinimumDisplayScale = minimumDisplayScale;
            entry.MaximumDisplayScale = maximumDisplayScale;
            try
            {
                (rangeChanged, changedCurrent) = RecomputeTimeState();
                if (entry.TimePolicy is not null
                    && (created || previousTimePolicy is null))
                {
                    entry.Dataset = CopyDataset(
                        entry.Dataset,
                        entry.Dataset.SubLayers,
                        entry.TimePolicy.AvailableTimes,
                        _time.Current is { } clock
                            ? entry.TimePolicy.SnapTo(clock)
                            : entry.TimePolicy.AvailableTimes.FirstOrDefault());
                }
                ComposeLayers();
            }
            catch
            {
                if (created)
                {
                    _entries.Remove(dataset.Id);
                    _order.Remove(dataset.Id);
                }
                else
                {
                    entry.Dataset = previousDataset;
                    entry.TimePolicy = previousTimePolicy;
                    entry.RenderedTime = previousRenderedTime;
                    entry.CatalogueMinimumDisplayScale = previousMinimum;
                    entry.MaximumDisplayScale = previousMaximum;
                }
                RecomputeTimeState();
                throw;
            }
        }

        LayersChanged?.Invoke(this, EventArgs.Empty);
        RaiseTimeEvents(rangeChanged, changedCurrent);
    }

    /// <summary>
    /// Renders a registered processor through a safe lease and atomically
    /// replaces its generated layers.
    /// </summary>
    /// <param name="datasetId">The dataset identity.</param>
    /// <param name="presentation">
    /// The immutable map presentation used to construct the product context.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The installed result, or <see langword="null"/> when the dataset was
    /// removed or its processor changed while rendering.
    /// </returns>
    public async Task<MapsuiDatasetResult?> RenderAsync(
        MapDatasetId datasetId,
        MapPresentationState presentation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        CancellationTokenSource localCts;
        DateTime? selectedTime;
        lock (_sync)
        {
            ThrowIfDisposed();
            if (!_entries.TryGetValue(datasetId, out var entry))
                return null;
            entry.RenderCts?.Cancel();
            localCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            entry.RenderCts = localCts;
            selectedTime = entry.TimePolicy is not null
                ? entry.Dataset.CurrentTime
                : null;
        }

        var enteredGate = false;
        try
        {
            await _renderGate.WaitAsync(localCts.Token).ConfigureAwait(true);
            enteredGate = true;
            return await RenderCoreAsync(
                datasetId,
                presentation,
                selectedTime,
                MapSessionRenderKind.Render,
                localCts.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        finally
        {
            if (enteredGate)
                _renderGate.Release();

            lock (_sync)
            {
                if (_entries.TryGetValue(datasetId, out var entry)
                    && ReferenceEquals(entry.RenderCts, localCts))
                {
                    entry.RenderCts = null;
                }
            }
            localCts.Dispose();
        }
    }

    /// <summary>Gets the aggregate time state for all registered datasets.</summary>
    /// <returns>An immutable materialized snapshot.</returns>
    public MapsuiMapTimeSnapshot GetTimeSnapshot()
    {
        lock (_sync)
        {
            return _time;
        }
    }

    /// <summary>
    /// Updates the global map clock without rendering. Hosts should then call
    /// <see cref="RefreshTimeAsync"/> to apply the new time to dataset layers.
    /// </summary>
    /// <param name="time">Requested global clock value.</param>
    public void SetCurrentTime(DateTime time)
    {
        DateTime? changedCurrent = null;
        lock (_sync)
        {
            ThrowIfDisposed();
            if (_time.Minimum is not { } minimum
                || _time.Maximum is not { } maximum)
            {
                return;
            }

            var clamped = time < minimum
                ? minimum
                : time > maximum
                    ? maximum
                    : time;
            if (_time.Current == clamped)
                return;

            _time = CopyTimeSnapshot(_time, clamped);
            changedCurrent = clamped;
        }

        if (changedCurrent is { } current)
            CurrentTimeChanged?.Invoke(this, new MapSessionCurrentTimeEventArgs(current));
    }

    /// <summary>
    /// Coalesces rapid clock changes, cancels the preceding time refresh, and
    /// applies product-specific time gating through the shared render gate.
    /// </summary>
    /// <param name="presentation">
    /// The immutable map presentation used to construct product contexts.
    /// </param>
    /// <param name="cancellationToken">Cancels the requested refresh.</param>
    /// <returns>A task that completes when the applied refresh finishes.</returns>
    public async Task RefreshTimeAsync(
        MapPresentationState presentation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        CancellationTokenSource localCts;
        lock (_sync)
        {
            ThrowIfDisposed();
            _timeRefreshCts?.Cancel();
            localCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            _timeRefreshCts = localCts;
        }

        try
        {
            await Task.Delay(TimeRefreshDebounceWindow, localCts.Token)
                .ConfigureAwait(true);
            await RefreshCoreAsync(
                presentation,
                timeAwareOnly: true,
                localCts.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_timeRefreshCts, localCts))
                    _timeRefreshCts = null;
            }
            localCts.Dispose();
        }
    }

    /// <summary>
    /// Cancels the preceding full refresh and re-renders every registered
    /// dataset through the shared render gate while preserving time gating.
    /// </summary>
    /// <param name="presentation">
    /// The immutable map presentation used to construct product contexts.
    /// </param>
    /// <param name="cancellationToken">Cancels the requested refresh.</param>
    /// <returns>
    /// A task that returns <see langword="true"/> when this refresh was applied,
    /// or <see langword="false"/> when a newer refresh superseded it.
    /// </returns>
    public async Task<bool> RefreshAsync(
        MapPresentationState presentation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        CancellationTokenSource localCts;
        lock (_sync)
        {
            ThrowIfDisposed();
            _presentationRefreshCts?.Cancel();
            localCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            _presentationRefreshCts = localCts;
        }

        try
        {
            await RefreshCoreAsync(
                presentation,
                timeAwareOnly: false,
                localCts.Token).ConfigureAwait(true);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_presentationRefreshCts, localCts))
                    _presentationRefreshCts = null;
            }
            localCts.Dispose();
        }
    }

    private async Task RefreshCoreAsync(
        MapPresentationState presentation,
        bool timeAwareOnly,
        CancellationToken cancellationToken)
    {
        var kind = timeAwareOnly
            ? MapSessionRenderKind.TimeRefresh
            : MapSessionRenderKind.PresentationRefresh;
        await _renderGate.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            MapDatasetId[] datasetIds;
            lock (_sync)
            {
                datasetIds = _order.ToArray();
            }

            foreach (var datasetId in datasetIds)
            {
                cancellationToken.ThrowIfCancellationRequested();

                TimePolicy? policy;
                DateTime? selectedTime;
                bool alreadyCurrent;
                lock (_sync)
                {
                    if (!_entries.TryGetValue(datasetId, out var entry))
                        continue;
                    policy = entry.TimePolicy;
                    if (timeAwareOnly && policy is null)
                        continue;

                    selectedTime = policy is not null && _time.Current is { } clock
                        ? policy.SnapTo(clock)
                        : null;
                    alreadyCurrent = policy is not null
                        && entry.RenderedTime == selectedTime
                        && (selectedTime is null || entry.Layers.Count > 0);
                }

                if (timeAwareOnly && alreadyCurrent)
                    continue;

                try
                {
                    if (policy is not null && selectedTime is null)
                    {
                        ClearLayersCore(datasetId, updateTime: true);
                        continue;
                    }

                    await RenderCoreAsync(
                        datasetId,
                        presentation,
                        selectedTime,
                        kind,
                        cancellationToken).ConfigureAwait(true);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    DatasetRenderFailed?.Invoke(
                        this,
                        new MapSessionDatasetRenderFailedEventArgs(
                            datasetId,
                            kind,
                            exception));
                }
            }
        }
        finally
        {
            _renderGate.Release();
        }
    }

    private async Task<MapsuiDatasetResult?> RenderCoreAsync(
        MapDatasetId datasetId,
        MapPresentationState presentation,
        DateTime? selectedTime,
        MapSessionRenderKind kind,
        CancellationToken cancellationToken)
    {
        Entry renderEntry;
        long generation;
        lock (_sync)
        {
            ThrowIfDisposed();
            if (!_entries.TryGetValue(datasetId, out var entry))
                return null;
            renderEntry = entry;
            generation = ++entry.Generation;
        }

        if (!_processorOwner.TryAcquire(datasetId, out var lease))
            return null;

        // Raise Started only once a lease is held, so it reliably means a
        // render is about to run. A dataset removed/unregistered before this
        // point yields no lifecycle events at all (rather than a Started that
        // is never followed by Completed/Failed).
        DatasetRenderStarted?.Invoke(
            this,
            new MapSessionDatasetRenderEventArgs(datasetId, kind));

        MapsuiDatasetResult result;
        using (lease)
        {
            var context = presentation.CreateRenderContext(
                lease.Processor,
                selectedTime);
            result = await Task.Run(
                () => _renderer.RenderAsync(
                    lease.Processor,
                    context,
                    cancellationToken),
                cancellationToken).ConfigureAwait(true);
        }

        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            ThrowIfDisposed();
            if (!_entries.TryGetValue(datasetId, out var entry)
                || !ReferenceEquals(entry, renderEntry)
                || entry.Generation != generation
                || !_processorOwner.Owns(datasetId, lease.Processor))
            {
                return null;
            }

            var previous = entry.CaptureRendering();
            entry.Apply(result);
            entry.RenderedTime = entry.TimePolicy is null ? null : selectedTime;
            CaptureVisibilityRanges(entry.Layers);
            entry.Dataset = ReconcileDatasetState(
                entry.Dataset,
                entry.LayerKeys,
                entry.Dataset.SubLayers);
            if (entry.TimePolicy is not null)
            {
                entry.Dataset = CopyDataset(
                    entry.Dataset,
                    entry.Dataset.SubLayers,
                    entry.TimePolicy.AvailableTimes,
                    selectedTime);
            }
            try
            {
                ComposeLayers();
            }
            catch
            {
                entry.RestoreRendering(previous);
                throw;
            }
        }

        LayersChanged?.Invoke(this, EventArgs.Empty);
        DatasetRenderCompleted?.Invoke(
            this,
            new MapSessionDatasetRenderEventArgs(datasetId, kind));
        return result;
    }

    private void ClearLayersCore(
        MapDatasetId datasetId,
        bool updateTime)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            if (!_entries.TryGetValue(datasetId, out var entry))
                return;

            var previous = entry.CaptureRendering();
            var previousGeneration = entry.Generation;
            entry.Generation++;
            entry.ClearRendering();
            if (updateTime && entry.TimePolicy is not null)
            {
                entry.RenderedTime = null;
                entry.Dataset = CopyDataset(
                    entry.Dataset,
                    entry.Dataset.SubLayers,
                    entry.TimePolicy.AvailableTimes,
                    currentTime: null);
            }
            try
            {
                ComposeLayers();
            }
            catch
            {
                entry.RestoreRendering(previous);
                entry.Generation = previousGeneration;
                throw;
            }
        }

        LayersChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Removes generated layers while retaining dataset state and ordinary
    /// ordering for lazy reload.
    /// </summary>
    /// <param name="datasetId">The dataset identity.</param>
    public void ClearLayers(MapDatasetId datasetId)
    {
        ClearLayersCore(datasetId, updateTime: false);
    }

    /// <summary>
    /// Removes a dataset's generated layers and optionally its retained state.
    /// </summary>
    /// <param name="datasetId">The dataset identity.</param>
    /// <param name="preserveState">
    /// Whether to retain renderer-neutral state and its order slot for a future
    /// lazy reload.
    /// </param>
    /// <param name="removeProcessor">
    /// Whether to retire the corresponding processor from the supplied owner.
    /// </param>
    /// <returns><see langword="true"/> when managed state existed.</returns>
    public bool RemoveDataset(
        MapDatasetId datasetId,
        bool preserveState = false,
        bool removeProcessor = true)
    {
        DatasetProcessorLease? processorLease = null;
        var rangeChanged = false;
        DateTime? changedCurrent = null;
        lock (_sync)
        {
            ThrowIfDisposed();
            if (!_entries.TryGetValue(datasetId, out var entry))
                return false;

            entry.RenderCts?.Cancel();
            var previous = entry.CaptureRendering();
            var previousGeneration = entry.Generation;
            var previousTimePolicy = entry.TimePolicy;
            var previousRenderedTime = entry.RenderedTime;
            var orderIndex = _order.IndexOf(datasetId);
            entry.Generation++;
            entry.ClearRendering();
            if (!preserveState)
            {
                _entries.Remove(datasetId);
                _order.Remove(datasetId);
            }
            else
            {
                entry.TimePolicy = null;
                entry.RenderedTime = null;
                entry.Dataset = CopyDataset(
                    entry.Dataset,
                    entry.Dataset.SubLayers,
                    availableTimes: [],
                    currentTime: null);
            }

            try
            {
                (rangeChanged, changedCurrent) = RecomputeTimeState();
                ComposeLayers();
            }
            catch
            {
                entry.RestoreRendering(previous);
                entry.Generation = previousGeneration;
                entry.TimePolicy = previousTimePolicy;
                entry.RenderedTime = previousRenderedTime;
                if (!preserveState)
                {
                    _entries.Add(datasetId, entry);
                    _order.Insert(orderIndex, datasetId);
                }
                RecomputeTimeState();
                throw;
            }

            if (removeProcessor)
                _processorOwner.TryAcquire(datasetId, out processorLease);
        }

        if (processorLease is not null)
        {
            using (processorLease)
                _processorOwner.Remove(datasetId, processorLease.Processor);
        }

        LayersChanged?.Invoke(this, EventArgs.Empty);
        RaiseTimeEvents(rangeChanged, changedCurrent);
        return true;
    }

    /// <summary>
    /// Replaces ordinary dataset order.
    /// </summary>
    /// <param name="datasetIds">
    /// Dataset identities in bottom-to-top paint order. Managed identities not
    /// supplied are retained after the supplied sequence.
    /// </param>
    public void SetOrder(IReadOnlyList<MapDatasetId> datasetIds)
    {
        ArgumentNullException.ThrowIfNull(datasetIds);

        lock (_sync)
        {
            ThrowIfDisposed();
            var seen = new HashSet<MapDatasetId>();
            var replacement = new List<MapDatasetId>(_order.Count);
            foreach (var datasetId in datasetIds)
            {
                if (_entries.ContainsKey(datasetId) && seen.Add(datasetId))
                    replacement.Add(datasetId);
            }
            foreach (var datasetId in _order)
            {
                if (seen.Add(datasetId))
                    replacement.Add(datasetId);
            }

            var previous = _order.ToArray();
            _order.Clear();
            _order.AddRange(replacement);
            try
            {
                ComposeLayers();
            }
            catch
            {
                _order.Clear();
                _order.AddRange(previous);
                throw;
            }
        }

        LayersChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Enables or disables whole-cell scale minima and overlap suppression.
    /// </summary>
    /// <param name="ignoreScaleMinimum">
    /// <see langword="true"/> to ignore minimum-display-scale windows.
    /// </param>
    public void SetIgnoreScaleMinimum(bool ignoreScaleMinimum)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            if (_ignoreScaleMinimum == ignoreScaleMinimum)
                return;

            var previous = _ignoreScaleMinimum;
            _ignoreScaleMinimum = ignoreScaleMinimum;
            try
            {
                ComposeLayers();
            }
            catch
            {
                _ignoreScaleMinimum = previous;
                throw;
            }
        }

        LayersChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Gets a snapshot for one managed dataset.</summary>
    /// <param name="datasetId">The dataset identity.</param>
    /// <returns>The snapshot, or <see langword="null"/> when unknown.</returns>
    public MapsuiMapDatasetSnapshot? GetDataset(MapDatasetId datasetId)
    {
        lock (_sync)
        {
            return _entries.TryGetValue(datasetId, out var entry)
                ? CreateSnapshot(entry)
                : null;
        }
    }

    /// <summary>
    /// Gets all managed datasets in ordinary bottom-to-top paint order.
    /// </summary>
    /// <returns>A materialized immutable snapshot.</returns>
    public IReadOnlyList<MapsuiMapDatasetSnapshot> GetDatasets()
    {
        lock (_sync)
        {
            return CreateOrderedSnapshots();
        }
    }

    /// <summary>
    /// Gets the complete S-98-ordered and suppression-applied layer stack,
    /// including entries from inactive datasets for inspection UI.
    /// </summary>
    /// <returns>A materialized bottom-to-top snapshot.</returns>
    public IReadOnlyList<LayerStackEntry> GetLayerStackEntries()
    {
        lock (_sync)
        {
            return _stackEntries.ToArray();
        }
    }

    /// <summary>
    /// Gets the active S-98-projected Mapsui dataset band.
    /// </summary>
    /// <returns>A materialized bottom-to-top layer snapshot.</returns>
    public IReadOnlyList<ILayer> GetStackedLayers()
    {
        lock (_sync)
        {
            return _stackedLayers.ToArray();
        }
    }

    /// <summary>Removes every managed dataset layer and subscription.</summary>
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;

            _disposed = true;
            _timeRefreshCts?.Cancel();
            _presentationRefreshCts?.Cancel();
            CancelEntryRenders();
            _authorityProvider.CurrentChanged -= OnAuthorityChanged;
            _entries.Clear();
            _order.Clear();
            _stackEntries = [];
            _stackedLayers = [];
            _time = MapsuiMapTimeSnapshot.Empty;
            _layerBands.ReplaceDatasetLayers([]);
        }
    }

    private void ComposeLayers()
    {
        var snapshots = CreateOrderedSnapshots();
        var (projected, stackEntries, stackedLayers) = ProjectLayerStack(snapshots);
        ValidateProjection(projected, snapshots);
        ApplyDisplayAndScaleToSourceLayers(snapshots);
        ApplyDisplayAndScaleToProjectedLayers(projected);
        ApplyOverlapSuppression(projected);
        UpdateContentCutoffs(projected);
        _layerBands.ReplaceDatasetLayers(projected.Select(item => item.Layer).ToArray());
        _stackEntries = stackEntries;
        _stackedLayers = stackedLayers;
    }

    private void ApplyDisplayAndScaleToSourceLayers(
        IReadOnlyList<MapsuiMapDatasetSnapshot> snapshots)
    {
        foreach (var snapshot in snapshots)
        {
            for (var index = 0; index < snapshot.Layers.Count; index++)
            {
                var key = LayerKeyAt(snapshot, index);
                var layer = snapshot.Layers[index];
                RestoreVisibilityRange(layer);
                ApplyDisplayState(snapshot.Dataset, key, layer);
            }

            if (!_ignoreScaleMinimum
                && snapshot.MinimumDisplayScale is int minimumDisplayScale)
            {
                MapsuiDatasetRenderer.ApplyCellScaleWindow(
                    snapshot.Layers,
                    minimumDisplayScale);
            }
        }
    }

    private void ApplyDisplayAndScaleToProjectedLayers(
        IReadOnlyList<MapsuiProjectedDatasetLayer> projected)
    {
        foreach (var item in projected)
        {
            if (!_entries.TryGetValue(item.DatasetId, out var entry))
                continue;

            RestoreProjectedVisibilityRange(item, entry);
            ApplyDisplayState(entry.Dataset, item.LayerKey, item.Layer);
            if (!_ignoreScaleMinimum
                && EffectiveMinimumDisplayScale(entry) is int minimumDisplayScale)
            {
                MapsuiDatasetRenderer.ApplyCellScaleWindow(
                    [item.Layer],
                    minimumDisplayScale);
            }
        }
    }

    private void ApplyOverlapSuppression(
        IReadOnlyList<MapsuiProjectedDatasetLayer> projected)
    {
        var allLayers = _entries.Values
            .SelectMany(entry => entry.Layers)
            .Concat(projected.Select(item => item.Layer))
            .Distinct()
            .ToArray();
        OverlapSuppression.ClearAll(
        [
            new OverlapSuppressionCell { Layers = allLayers },
        ]);

        var cells = new List<OverlapSuppressionCell>();
        foreach (var group in projected.GroupBy(item => item.DatasetId))
        {
            if (!_entries.TryGetValue(group.Key, out var entry))
                continue;

            var layers = group.Select(item => item.Layer).ToArray();
            var drawing = layers.Any(layer => layer.Enabled && layer.Opacity > 0);
            cells.Add(new OverlapSuppressionCell
            {
                Layers = layers,
                Coverage = drawing ? entry.CoverageGeometry : null,
                ScaleDenominator = drawing
                    ? EffectiveMinimumDisplayScale(entry)
                    : null,
            });
        }

        if (_ignoreScaleMinimum)
            OverlapSuppression.ClearAll(cells);
        else
            OverlapSuppression.Apply(cells);
    }

    private void UpdateContentCutoffs(
        IReadOnlyList<MapsuiProjectedDatasetLayer> projected)
    {
        foreach (var entry in _entries.Values)
            entry.ContentMaxVisibleResolution = null;

        if (_ignoreScaleMinimum)
            return;

        foreach (var group in projected.GroupBy(item => item.DatasetId))
        {
            if (!_entries.TryGetValue(group.Key, out var entry)
                || EffectiveMinimumDisplayScale(entry) is null)
            {
                continue;
            }

            var cutoff = group
                .Select(item => item.Layer)
                .OfType<BaseLayer>()
                .Where(layer => layer.MaxVisible < double.MaxValue)
                .Select(layer => layer.MaxVisible)
                .DefaultIfEmpty()
                .Max();
            entry.ContentMaxVisibleResolution = cutoff > 0 ? cutoff : null;
        }
    }

    private (
        IReadOnlyList<MapsuiProjectedDatasetLayer> Projected,
        IReadOnlyList<LayerStackEntry> StackEntries,
        IReadOnlyList<ILayer> StackedLayers) ProjectLayerStack(
        IReadOnlyList<MapsuiMapDatasetSnapshot> snapshots)
    {
        var authority = _authorityProvider.Current;
        var perDataset = new List<IReadOnlyList<SubLayerStackItem>>(snapshots.Count);
        var prebuilt = new Dictionary<(string DatasetId, string LayerKey), LayerStackEntry>();
        foreach (var snapshot in snapshots)
        {
            var layers = snapshot.Layers;
            var datasetId = snapshot.Dataset.Id.Value;

            if (snapshot.StackEntries is { Count: > 0 } stack)
            {
                var items = new List<SubLayerStackItem>(stack.Count);
                foreach (var stackEntry in stack)
                {
                    items.Add(stackEntry.Item);
                    prebuilt[LayerStackProjector.KeyOf(stackEntry.Item)] = stackEntry;
                }
                perDataset.Add(items);
                continue;
            }

            var plane = authority.GetDefaultPlane(
                snapshot.Dataset.Metadata.Spec.Name);
            var syntheticItems = new List<SubLayerStackItem>(layers.Count);
            for (var layerIndex = 0; layerIndex < layers.Count; layerIndex++)
            {
                var layerKey = FormattableString.Invariant($"__synth__{layerIndex}");
                var item = new SubLayerStackItem(
                    new SyntheticStackPayload(layerKey),
                    plane,
                    WithinPlanePriority: 0,
                    SourceDatasetId: datasetId);
                syntheticItems.Add(item);
                prebuilt[(datasetId, layerKey)] =
                    new LayerStackEntry(layers[layerIndex], item);
            }
            perDataset.Add(syntheticItems);
        }

        var topFirst = perDataset.AsEnumerable().Reverse().ToArray();
        var sorted = LayerStackBuilder.Build(authority, topFirst);
        var loaded = BuildLoadedDatasetInfos(snapshots);
        var ruled = authority.ApplyRules(sorted, loaded, _mariner);
        var stackEntries = LayerStackProjector.Project(
            ruled,
            prebuilt,
            _renderer.BuildGridCoverageLayer);

        var activeById = snapshots.ToDictionary(
            snapshot => snapshot.Dataset.Id.Value,
            snapshot => snapshot.Dataset.IsActive,
            StringComparer.Ordinal);
        var activeEntries = stackEntries
            .Where(entry => activeById.GetValueOrDefault(entry.SourceDatasetId))
            .ToArray();
        var projected = activeEntries
            .Select(entry =>
            {
                var key = LayerStackProjector.KeyOf(entry.Item);
                return new MapsuiProjectedDatasetLayer(
                    new MapDatasetId(entry.SourceDatasetId),
                    key.LayerKey,
                    entry.Layer);
            })
            .ToArray();
        return (
            projected,
            stackEntries.ToArray(),
            LayerStackProjector.ToLayerList(activeEntries));
    }

    private static IReadOnlyList<LoadedDatasetInfo> BuildLoadedDatasetInfos(
        IReadOnlyList<MapsuiMapDatasetSnapshot> snapshots)
    {
        var result = new List<LoadedDatasetInfo>(snapshots.Count);
        foreach (var snapshot in snapshots)
        {
            var dataset = snapshot.Dataset;
            var active = dataset.IsActive
                && dataset.IsVisible
                && snapshot.IsDrawing;
            result.Add(new LoadedDatasetInfo(
                dataset.Id.Value,
                dataset.Metadata.Spec.Name,
                active));
        }
        return result;
    }

    private static void ValidateProjection(
        IReadOnlyList<MapsuiProjectedDatasetLayer> projected,
        IReadOnlyList<MapsuiMapDatasetSnapshot> snapshots)
    {
        ArgumentNullException.ThrowIfNull(projected);
        var known = snapshots.Select(snapshot => snapshot.Dataset.Id).ToHashSet();
        var layers = new HashSet<ILayer>();
        foreach (var item in projected)
        {
            if (item is null)
                throw new ArgumentException("The layer projection cannot contain null values.", nameof(projected));
            if (!known.Contains(item.DatasetId))
                throw new ArgumentException("The layer projection references an unknown dataset.", nameof(projected));
            if (!layers.Add(item.Layer))
                throw new ArgumentException("The layer projection cannot contain duplicate layers.", nameof(projected));
        }
    }

    private IReadOnlyList<MapsuiMapDatasetSnapshot> CreateOrderedSnapshots()
    {
        var snapshots = new List<MapsuiMapDatasetSnapshot>(_order.Count);
        foreach (var datasetId in _order)
        {
            if (_entries.TryGetValue(datasetId, out var entry))
                snapshots.Add(CreateSnapshot(entry));
        }
        return snapshots;
    }

    private static MapsuiMapDatasetSnapshot CreateSnapshot(Entry entry)
    {
        var isDrawing = IsDrawing(entry);
        return new MapsuiMapDatasetSnapshot
        {
            Dataset = entry.Dataset,
            Layers = entry.Layers,
            LayerKeys = entry.LayerKeys,
            StackEntries = entry.StackEntries,
            Extent = entry.Extent,
            Info = entry.Info,
            CoverageGeometry = entry.CoverageGeometry,
            MinimumDisplayScale = EffectiveMinimumDisplayScale(entry),
            MaximumDisplayScale = entry.MaximumDisplayScale,
            ContentMaxVisibleResolution = entry.ContentMaxVisibleResolution,
            IsDrawing = isDrawing,
        };
    }

    private static MapDataset ReconcileDatasetState(
        MapDataset dataset,
        IReadOnlyList<string>? layerKeys,
        IReadOnlyList<MapDatasetSubLayer> fallbackSubLayers)
    {
        if (layerKeys is null)
        {
            return CopyDataset(
                dataset,
                dataset.SubLayers.Count > 0
                    ? dataset.SubLayers
                    : fallbackSubLayers);
        }
        if (layerKeys.Count <= 1)
            return CopyDataset(dataset, []);

        var existing = fallbackSubLayers.ToDictionary(layer => layer.Key);
        foreach (var layer in dataset.SubLayers)
            existing[layer.Key] = layer;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var subLayers = new List<MapDatasetSubLayer>(layerKeys.Count);
        foreach (var sourceKey in layerKeys)
        {
            var key = sourceKey;
            var suffix = 1;
            while (!seen.Add(key))
                key = $"{sourceKey}#{++suffix}";

            subLayers.Add(existing.TryGetValue(key, out var state)
                ? state
                : new MapDatasetSubLayer(key, key));
        }
        return CopyDataset(dataset, subLayers);
    }

    private static MapDataset CopyDataset(
        MapDataset dataset,
        IReadOnlyList<MapDatasetSubLayer> subLayers) =>
        CopyDataset(
            dataset,
            subLayers,
            dataset.AvailableTimes,
            dataset.CurrentTime);

    private static MapDataset CopyDataset(
        MapDataset dataset,
        IReadOnlyList<MapDatasetSubLayer> subLayers,
        IReadOnlyList<DateTime> availableTimes,
        DateTime? currentTime) =>
        new(
            dataset.Id,
            dataset.Name,
            dataset.Metadata,
            dataset.IsVisible,
            dataset.IsActive,
            dataset.Opacity,
            availableTimes,
            currentTime,
            subLayers,
            dataset.Validation,
            dataset.VersionAssessment);

    private (bool RangeChanged, DateTime? CurrentChanged) RecomputeTimeState()
    {
        var previous = _time;
        var samples = _entries.Values
            .Select(entry => entry.TimePolicy)
            .Where(policy => policy is not null)
            .SelectMany(policy => policy!.AvailableTimes)
            .Distinct()
            .OrderBy(time => time)
            .ToArray();
        var minimum = samples.Length > 0 ? samples[0] : (DateTime?)null;
        var maximum = samples.Length > 0 ? samples[^1] : (DateTime?)null;
        var current = previous.Current;
        if (minimum is null || maximum is null)
        {
            current = null;
        }
        else if (current is null || current < minimum)
        {
            current = minimum;
        }
        else if (current > maximum)
        {
            current = maximum;
        }

        var segments = ComputeCoverageSegments(minimum, maximum);
        _time = new MapsuiMapTimeSnapshot
        {
            Minimum = minimum,
            Maximum = maximum,
            Current = current,
            Samples = samples,
            CoverageSegments = segments,
        };
        var rangeChanged = previous.Minimum != minimum
            || previous.Maximum != maximum
            || !previous.Samples.SequenceEqual(samples)
            || !previous.CoverageSegments.SequenceEqual(segments);
        return (rangeChanged, previous.Current != current ? current : null);
    }

    private IReadOnlyList<MapsuiMapTimeSegment> ComputeCoverageSegments(
        DateTime? minimum,
        DateTime? maximum)
    {
        if (minimum is not { } lower || maximum is not { } upper)
            return [];

        var intervals = new List<MapsuiMapTimeSegment>();
        foreach (var policy in _entries.Values
            .Select(entry => entry.TimePolicy)
            .Where(policy => policy is not null))
        {
            foreach (var segment in policy!.CoverageSegments)
            {
                var start = segment.Start < lower ? lower : segment.Start;
                var end = segment.End > upper ? upper : segment.End;
                if (end >= start)
                    intervals.Add(new MapsuiMapTimeSegment(start, end));
            }
        }
        if (intervals.Count == 0)
            return [];

        intervals.Sort((left, right) => left.Start.CompareTo(right.Start));
        var merged = new List<MapsuiMapTimeSegment>();
        var currentStart = intervals[0].Start;
        var currentEnd = intervals[0].End;
        for (var index = 1; index < intervals.Count; index++)
        {
            var next = intervals[index];
            if (next.Start <= currentEnd)
            {
                if (next.End > currentEnd)
                    currentEnd = next.End;
            }
            else
            {
                merged.Add(new MapsuiMapTimeSegment(currentStart, currentEnd));
                currentStart = next.Start;
                currentEnd = next.End;
            }
        }
        merged.Add(new MapsuiMapTimeSegment(currentStart, currentEnd));
        return merged;
    }

    private static MapsuiMapTimeSnapshot CopyTimeSnapshot(
        MapsuiMapTimeSnapshot source,
        DateTime current) =>
        new()
        {
            Minimum = source.Minimum,
            Maximum = source.Maximum,
            Current = current,
            Samples = source.Samples,
            CoverageSegments = source.CoverageSegments,
        };

    private void RaiseTimeEvents(bool rangeChanged, DateTime? changedCurrent)
    {
        if (rangeChanged)
            TimeRangeChanged?.Invoke(this, EventArgs.Empty);
        if (changedCurrent is { } current)
            CurrentTimeChanged?.Invoke(this, new MapSessionCurrentTimeEventArgs(current));
    }

    private void CancelEntryRenders()
    {
        foreach (var entry in _entries.Values)
            entry.RenderCts?.Cancel();
    }

    private static void ApplyDisplayState(
        MapDataset dataset,
        string layerKey,
        ILayer layer)
    {
        var subLayer = dataset.SubLayers.FirstOrDefault(
            candidate => string.Equals(candidate.Key, layerKey, StringComparison.Ordinal));
        layer.Enabled = dataset.IsActive
            && dataset.IsVisible
            && (subLayer?.IsVisible ?? true);
        layer.Opacity = dataset.Opacity * (subLayer?.Opacity ?? 1.0);
    }

    private void RestoreVisibilityRange(ILayer layer)
    {
        if (layer is not BaseLayer baseLayer)
            return;

        var range = _visibilityRanges.GetValue(
            layer,
            static source => new LayerVisibilityRange(
                ((BaseLayer)source).MinVisible,
                ((BaseLayer)source).MaxVisible));
        baseLayer.MinVisible = range.MinVisible;
        baseLayer.MaxVisible = range.MaxVisible;
    }

    private void RestoreProjectedVisibilityRange(
        MapsuiProjectedDatasetLayer item,
        Entry entry)
    {
        if (item.Layer is not BaseLayer projectedBase)
            return;

        var source = FindSourceLayer(item.LayerKey, entry);
        if (source is not BaseLayer sourceBase || ReferenceEquals(source, item.Layer))
        {
            RestoreVisibilityRange(item.Layer);
            return;
        }

        var range = _visibilityRanges.GetValue(
            source,
            static layer => new LayerVisibilityRange(
                ((BaseLayer)layer).MinVisible,
                ((BaseLayer)layer).MaxVisible));
        projectedBase.MinVisible = range.MinVisible;
        projectedBase.MaxVisible = range.MaxVisible;
        _visibilityRanges.Remove(item.Layer);
        _visibilityRanges.Add(item.Layer, range);
    }

    private static ILayer? FindSourceLayer(string layerKey, Entry entry)
    {
        if (entry.LayerKeys is not null)
        {
            for (var index = 0; index < entry.LayerKeys.Count; index++)
            {
                if (string.Equals(
                        entry.LayerKeys[index],
                        layerKey,
                        StringComparison.Ordinal)
                    && index < entry.Layers.Count)
                {
                    return entry.Layers[index];
                }
            }
        }

        if (entry.StackEntries is not null)
        {
            foreach (var stackEntry in entry.StackEntries)
            {
                if (string.Equals(
                    LayerStackProjector.KeyOf(stackEntry.Item).LayerKey,
                    layerKey,
                    StringComparison.Ordinal))
                {
                    return stackEntry.Layer;
                }
            }
        }

        const string syntheticPrefix = "__synth__";
        if (layerKey.StartsWith(syntheticPrefix, StringComparison.Ordinal)
            && int.TryParse(
                layerKey[syntheticPrefix.Length..],
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var syntheticIndex)
            && syntheticIndex < entry.Layers.Count)
        {
            return entry.Layers[syntheticIndex];
        }

        return null;
    }

    private void CaptureVisibilityRanges(IEnumerable<ILayer> layers)
    {
        foreach (var layer in layers)
        {
            if (layer is BaseLayer baseLayer)
            {
                _visibilityRanges.GetValue(
                    layer,
                    _ => new LayerVisibilityRange(
                        baseLayer.MinVisible,
                        baseLayer.MaxVisible));
            }
        }
    }

    private static bool IsDrawing(Entry entry)
    {
        if (entry.Layers.Count == 0
            || !entry.Dataset.IsActive
            || !entry.Dataset.IsVisible
            || entry.Dataset.Opacity <= 0)
        {
            return false;
        }

        if (entry.LayerKeys is null || entry.Dataset.SubLayers.Count == 0)
            return true;

        var subLayers = entry.Dataset.SubLayers.ToDictionary(layer => layer.Key);
        for (var index = 0; index < entry.Layers.Count; index++)
        {
            var key = index < entry.LayerKeys.Count
                ? entry.LayerKeys[index]
                : string.Empty;
            if (!subLayers.TryGetValue(key, out var subLayer)
                || (subLayer.IsVisible && subLayer.Opacity > 0))
            {
                return true;
            }
        }
        return false;
    }

    private static string LayerKeyAt(
        MapsuiMapDatasetSnapshot snapshot,
        int index) =>
        snapshot.LayerKeys is not null && index < snapshot.LayerKeys.Count
            ? snapshot.LayerKeys[index]
            : $"__layer__{index}";

    private static int? EffectiveMinimumDisplayScale(Entry entry) =>
        entry.CatalogueMinimumDisplayScale ?? entry.CellMinimumDisplayScale;

    private void OnAuthorityChanged()
    {
        lock (_sync)
        {
            if (_disposed)
                return;
            ComposeLayers();
        }

        LayersChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private sealed class TimePolicy
    {
        private readonly TimePolicyKind _kind;
        private readonly DateTime _minimum;
        private readonly DateTime _maximum;
        private readonly TimeSpan _tolerance;

        private TimePolicy(
            TimePolicyKind kind,
            IReadOnlyList<DateTime> availableTimes)
        {
            _kind = kind;
            AvailableTimes = availableTimes
                .Distinct()
                .OrderBy(time => time)
                .ToArray();
            if (AvailableTimes.Count == 0)
                return;

            _minimum = AvailableTimes[0];
            _maximum = AvailableTimes[^1];
            _tolerance = kind == TimePolicyKind.RangeGatedNearest
                && AvailableTimes.Count >= 2
                    ? TimeSpan.FromTicks(
                        (_maximum - _minimum).Ticks
                        / (AvailableTimes.Count - 1))
                    : TimeSpan.Zero;

            CoverageSegments = kind switch
            {
                TimePolicyKind.RangeGatedNearest =>
                [
                    new MapsuiMapTimeSegment(
                        AddClamped(_minimum, -_tolerance),
                        AddClamped(_maximum, _tolerance)),
                ],
                TimePolicyKind.SnapshotAtOrBefore =>
                [
                    new MapsuiMapTimeSegment(
                        _minimum,
                        DateTime.MaxValue),
                ],
                _ =>
                [
                    new MapsuiMapTimeSegment(_minimum, _maximum),
                ],
            };
        }

        public IReadOnlyList<DateTime> AvailableTimes { get; }

        public IReadOnlyList<MapsuiMapTimeSegment> CoverageSegments { get; } = [];

        public static TimePolicy? TryCreate(
            string productSpec,
            IReadOnlyList<DateTime> availableTimes)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(productSpec);
            ArgumentNullException.ThrowIfNull(availableTimes);
            if (availableTimes.Count == 0)
                return null;

            var kind = productSpec.ToUpperInvariant() switch
            {
                "S-111" => TimePolicyKind.RangeGatedNearest,
                "S-411" => TimePolicyKind.SnapshotAtOrBefore,
                _ => TimePolicyKind.Nearest,
            };
            return new TimePolicy(kind, availableTimes);
        }

        public DateTime? SnapTo(DateTime time)
        {
            if (AvailableTimes.Count == 0)
                return null;

            if (_kind == TimePolicyKind.SnapshotAtOrBefore)
            {
                DateTime? selected = null;
                foreach (var sample in AvailableTimes)
                {
                    if (sample <= time)
                        selected = sample;
                    else
                        break;
                }
                return selected;
            }

            if (_kind == TimePolicyKind.RangeGatedNearest
                && AvailableTimes.Count >= 2
                && (time < AddClamped(_minimum, -_tolerance)
                    || time > AddClamped(_maximum, _tolerance)))
            {
                return null;
            }

            var nearest = AvailableTimes[0];
            var nearestDistance = (nearest - time).Duration();
            for (var index = 1; index < AvailableTimes.Count; index++)
            {
                var distance = (AvailableTimes[index] - time).Duration();
                if (distance < nearestDistance)
                {
                    nearest = AvailableTimes[index];
                    nearestDistance = distance;
                }
            }
            return nearest;
        }

        private static DateTime AddClamped(DateTime value, TimeSpan delta)
        {
            if (delta > TimeSpan.Zero
                && DateTime.MaxValue - value < delta)
            {
                return DateTime.MaxValue;
            }
            if (delta < TimeSpan.Zero
                && value - DateTime.MinValue < -delta)
            {
                return DateTime.MinValue;
            }
            return value + delta;
        }
    }

    private enum TimePolicyKind
    {
        Nearest,
        RangeGatedNearest,
        SnapshotAtOrBefore,
    }

    private sealed class Entry(MapDataset dataset)
    {
        public MapDataset Dataset { get; set; } = dataset;

        public TimePolicy? TimePolicy { get; set; }

        public DateTime? RenderedTime { get; set; }

        public CancellationTokenSource? RenderCts { get; set; }

        public IReadOnlyList<ILayer> Layers { get; set; } = [];

        public IReadOnlyList<string>? LayerKeys { get; set; }

        public IReadOnlyList<LayerStackEntry>? StackEntries { get; set; }

        public MRect? Extent { get; set; }

        public string? Info { get; set; }

        public NetTopologySuite.Geometries.Geometry? CoverageGeometry { get; set; }

        public int? CatalogueMinimumDisplayScale { get; set; }

        public int? CellMinimumDisplayScale { get; set; }

        public int? MaximumDisplayScale { get; set; }

        public double? ContentMaxVisibleResolution { get; set; }

        public long Generation { get; set; }

        public void Apply(MapsuiDatasetResult result)
        {
            Layers = result.Layers.ToArray();
            LayerKeys = result.LayerNames?.ToArray();
            StackEntries = result.StackEntries?.ToArray();
            Extent = result.Extent;
            Info = result.Info;
            CoverageGeometry = result.CoverageGeometry;
            CellMinimumDisplayScale = result.CellMinimumDisplayScale;
        }

        public void ClearRendering()
        {
            Layers = [];
            LayerKeys = null;
            StackEntries = null;
            Extent = null;
            Info = null;
            CoverageGeometry = null;
            CellMinimumDisplayScale = null;
            ContentMaxVisibleResolution = null;
        }

        public RenderingState CaptureRendering() => new(
            Dataset,
            Layers,
            LayerKeys,
            StackEntries,
            Extent,
            Info,
            CoverageGeometry,
            CellMinimumDisplayScale,
            ContentMaxVisibleResolution,
            RenderedTime);

        public void RestoreRendering(RenderingState state)
        {
            Dataset = state.Dataset;
            Layers = state.Layers;
            LayerKeys = state.LayerKeys;
            StackEntries = state.StackEntries;
            Extent = state.Extent;
            Info = state.Info;
            CoverageGeometry = state.CoverageGeometry;
            CellMinimumDisplayScale = state.CellMinimumDisplayScale;
            ContentMaxVisibleResolution = state.ContentMaxVisibleResolution;
            RenderedTime = state.RenderedTime;
        }
    }

    private sealed record RenderingState(
        MapDataset Dataset,
        IReadOnlyList<ILayer> Layers,
        IReadOnlyList<string>? LayerKeys,
        IReadOnlyList<LayerStackEntry>? StackEntries,
        MRect? Extent,
        string? Info,
        NetTopologySuite.Geometries.Geometry? CoverageGeometry,
        int? CellMinimumDisplayScale,
        double? ContentMaxVisibleResolution,
        DateTime? RenderedTime);

    private sealed record LayerVisibilityRange(
        double MinVisible,
        double MaxVisible);
}

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
    private IReadOnlyList<LayerStackEntry> _stackEntries = [];
    private IReadOnlyList<ILayer> _stackedLayers = [];
    private MarinerSettings _mariner = MarinerSettings.Default;
    private bool _ignoreScaleMinimum;
    private bool _disposed;

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
    public event Action? LayersChanged;

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

        LayersChanged?.Invoke();
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
            var previousMinimum = entry.CatalogueMinimumDisplayScale;
            var previousMaximum = entry.MaximumDisplayScale;
            entry.Dataset = ReconcileDatasetState(
                dataset,
                entry.LayerKeys,
                entry.Dataset.SubLayers);
            entry.CatalogueMinimumDisplayScale = minimumDisplayScale;
            entry.MaximumDisplayScale = maximumDisplayScale;
            try
            {
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
                    entry.CatalogueMinimumDisplayScale = previousMinimum;
                    entry.MaximumDisplayScale = previousMaximum;
                }
                throw;
            }
        }

        LayersChanged?.Invoke();
    }

    /// <summary>
    /// Renders a registered processor through a safe lease and atomically
    /// replaces its generated layers.
    /// </summary>
    /// <param name="datasetId">The dataset identity.</param>
    /// <param name="context">The host-constructed render context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The installed result, or <see langword="null"/> when the dataset was
    /// removed or its processor changed while rendering.
    /// </returns>
    public async Task<MapsuiDatasetResult?> RenderAsync(
        MapDatasetId datasetId,
        RenderContext? context,
        CancellationToken cancellationToken = default)
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

        MapsuiDatasetResult result;
        using (lease)
        {
            result = await Task.Run(
                () => _renderer.RenderAsync(lease.Processor, context, cancellationToken),
                cancellationToken);
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
            CaptureVisibilityRanges(entry.Layers);
            entry.Dataset = ReconcileDatasetState(
                entry.Dataset,
                entry.LayerKeys,
                entry.Dataset.SubLayers);
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

        LayersChanged?.Invoke();
        return result;
    }

    /// <summary>
    /// Removes generated layers while retaining dataset state and ordinary
    /// ordering, for lazy unload or time gating.
    /// </summary>
    /// <param name="datasetId">The dataset identity.</param>
    public void ClearLayers(MapDatasetId datasetId)
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

        LayersChanged?.Invoke();
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
        lock (_sync)
        {
            ThrowIfDisposed();
            if (!_entries.TryGetValue(datasetId, out var entry))
                return false;

            var previous = entry.CaptureRendering();
            var previousGeneration = entry.Generation;
            var orderIndex = _order.IndexOf(datasetId);
            entry.Generation++;
            entry.ClearRendering();
            if (!preserveState)
            {
                _entries.Remove(datasetId);
                _order.Remove(datasetId);
            }

            try
            {
                ComposeLayers();
            }
            catch
            {
                entry.RestoreRendering(previous);
                entry.Generation = previousGeneration;
                if (!preserveState)
                {
                    _entries.Add(datasetId, entry);
                    _order.Insert(orderIndex, datasetId);
                }
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

        LayersChanged?.Invoke();
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

        LayersChanged?.Invoke();
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

        LayersChanged?.Invoke();
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
            _authorityProvider.CurrentChanged -= OnAuthorityChanged;
            _entries.Clear();
            _order.Clear();
            _stackEntries = [];
            _stackedLayers = [];
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

            var plane = _authorityProvider.Current.GetDefaultPlane(
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

        var authority = _authorityProvider.Current;
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
        new(
            dataset.Id,
            dataset.Name,
            dataset.Metadata,
            dataset.IsVisible,
            dataset.IsActive,
            dataset.Opacity,
            dataset.AvailableTimes,
            dataset.CurrentTime,
            subLayers,
            dataset.Validation,
            dataset.VersionAssessment);

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

        LayersChanged?.Invoke();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private sealed class Entry(MapDataset dataset)
    {
        public MapDataset Dataset { get; set; } = dataset;

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
            ContentMaxVisibleResolution);

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
        double? ContentMaxVisibleResolution);

    private sealed record LayerVisibilityRange(
        double MinVisible,
        double MaxVisible);
}

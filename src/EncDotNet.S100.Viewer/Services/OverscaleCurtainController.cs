using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Threading;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Viewer.Tools;
using EncDotNet.S100.Viewer.ViewModels;
using Mapsui.Layers;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Keeps the on-chart overscale "curtain" overlay (issue #441, S-52 / S-101
/// <c>AP(OVERSC01)</c> Form A) in sync with the live viewport and loaded
/// datasets: a subtle vertical-line pattern is painted over the region of each
/// cell that is currently displayed beyond its compilation scale.
/// </summary>
/// <remarks>
/// <para>
/// The curtain <em>region</em> geometry depends only on the set of loaded cells
/// and the viewport <em>resolution</em> (zoom) — never on pan or rotation —
/// because <see cref="OverscaleCurtain.ComputeRegions"/> works in world
/// coordinates (EPSG:3857) and <see cref="OverscaleCurtainRenderer"/>
/// re-projects and clips the fill per frame. The controller therefore caches the
/// last resolution and skips recomputation on a pure pan, recomputing only when
/// the zoom changes, a dataset is added/removed/(re)loaded/hidden/shown, or the
/// mariner toggles <see cref="SettingsViewModel.ShowOverscaleIndication"/>.
/// </para>
/// <para>
/// The overscale factor per cell is derived from its compilation-scale
/// denominator and the viewport resolution at the cell's latitude — identical to
/// the status-bar overscale pill (<see cref="OverscaleEvaluator"/>) so the two
/// indications always agree.
/// </para>
/// </remarks>
internal sealed class OverscaleCurtainController : IDisposable
{
    private readonly IMapHost _mapHost;
    private readonly DatasetsViewModel _datasets;
    private readonly IDatasetLoaderService _loader;
    private readonly IMapViewportNotifier _viewport;
    private readonly SettingsViewModel _settings;
    private readonly Action<Action> _marshal;
    private readonly MemoryLayer _layer;
    private readonly HashSet<DatasetEntry> _subscribed = new();
    private double _lastResolution = double.NaN;
    private bool _disposed;

    /// <summary>
    /// Creates and attaches the controller. The map host must already be
    /// initialised so the curtain overlay lands above the chart slice.
    /// </summary>
    /// <param name="mapHost">Target map host.</param>
    /// <param name="datasets">The datasets view-model to observe.</param>
    /// <param name="loader">Supplies the overscale cell inputs.</param>
    /// <param name="viewport">Publishes viewport (resolution) changes.</param>
    /// <param name="settings">Settings view-model supplying the on/off toggle.</param>
    /// <param name="marshal">
    /// Optional UI-thread marshalling override. Defaults to
    /// <see cref="Dispatcher.UIThread"/>; tests inject a synchronous
    /// implementation.
    /// </param>
    public OverscaleCurtainController(
        IMapHost mapHost,
        DatasetsViewModel datasets,
        IDatasetLoaderService loader,
        IMapViewportNotifier viewport,
        SettingsViewModel settings,
        Action<Action>? marshal = null)
    {
        ArgumentNullException.ThrowIfNull(mapHost);
        ArgumentNullException.ThrowIfNull(datasets);
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(viewport);
        ArgumentNullException.ThrowIfNull(settings);

        _mapHost = mapHost;
        _datasets = datasets;
        _loader = loader;
        _viewport = viewport;
        _settings = settings;
        _marshal = marshal ?? DispatcherMarshal;

        _layer = OverscaleCurtainOverlayLayer.Create();

        _datasets.Entries.CollectionChanged += OnEntriesChanged;
        foreach (var entry in _datasets.Entries)
            Subscribe(entry);

        _viewport.ViewportChanged += OnViewportChanged;
        _settings.OverscaleIndicationChanged += OnToggleChanged;

        if (_viewport.Current is { } current)
            _lastResolution = current.MercatorResolution;

        _marshal(() =>
        {
            _mapHost.AddOverlayLayer(_layer);
            Rebuild();
        });
    }

    private void OnViewportChanged(object? sender, MapViewportSnapshot snapshot)
    {
        // Region geometry is pan/rotation-invariant; only a zoom (resolution)
        // change alters which cells are overscaled and by how much.
        if (ResolutionsEqual(snapshot.MercatorResolution, _lastResolution))
            return;

        _lastResolution = snapshot.MercatorResolution;
        _marshal(Rebuild);
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (DatasetEntry entry in e.OldItems)
                Unsubscribe(entry);

        if (e.NewItems is not null)
            foreach (DatasetEntry entry in e.NewItems)
                Subscribe(entry);

        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var entry in _subscribed)
                entry.PropertyChanged -= OnEntryChanged;
            _subscribed.Clear();
            foreach (var entry in _datasets.Entries)
                Subscribe(entry);
        }

        _marshal(Rebuild);
    }

    private void Subscribe(DatasetEntry entry)
    {
        if (_subscribed.Add(entry))
            entry.PropertyChanged += OnEntryChanged;
    }

    private void Unsubscribe(DatasetEntry entry)
    {
        if (_subscribed.Remove(entry))
            entry.PropertyChanged -= OnEntryChanged;
    }

    private void OnEntryChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DatasetEntry.IsLoaded)
            or nameof(DatasetEntry.IsVisible))
        {
            _marshal(Rebuild);
        }
    }

    private void OnToggleChanged() => _marshal(Rebuild);

    private void Rebuild()
    {
        if (_disposed) return;

        IReadOnlyList<OverscaleRegion> regions = Array.Empty<OverscaleRegion>();

        if (_settings.ShowOverscaleIndication && !double.IsNaN(_lastResolution))
        {
            var cells = _loader.GetOverscaleCells();
            if (cells.Count > 0)
                regions = OverscaleCurtain.ComputeRegions(cells, _lastResolution);
        }

        OverscaleCurtainOverlayLayer.Update(_layer, regions);
    }

    private static bool ResolutionsEqual(double a, double b)
    {
        if (double.IsNaN(a) || double.IsNaN(b))
            return double.IsNaN(a) && double.IsNaN(b);

        // Treat sub-per-mille resolution changes as "same zoom" so incidental
        // navigator jitter during a pan does not trigger a rebuild.
        return Math.Abs(a - b) <= Math.Abs(b) * 1e-3;
    }

    private static void DispatcherMarshal(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }

    /// <summary>Detaches the overlay layer and unsubscribes. Idempotent.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _datasets.Entries.CollectionChanged -= OnEntriesChanged;
        foreach (var entry in _subscribed)
            entry.PropertyChanged -= OnEntryChanged;
        _subscribed.Clear();

        _viewport.ViewportChanged -= OnViewportChanged;
        _settings.OverscaleIndicationChanged -= OnToggleChanged;

        _marshal(() => _mapHost.RemoveOverlayLayer(_layer));
    }
}

using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Threading;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Viewer.Tools;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Keeps a map overlay outlining the extents of loaded datasets that have
/// zoomed out past their display-scale minimum — and therefore render no
/// content — so a mariner who frames a wide-spread exchange set still sees
/// where the member datasets are and has a target to zoom toward (issue #446).
/// </summary>
/// <remarks>
/// <para>
/// The controller observes the <see cref="DatasetsViewModel"/>'s entries: it
/// rebuilds the overlay whenever a dataset is added, removed, (re)loaded,
/// hidden/shown, or its captured extent / scale cutoff changes. Each qualifying
/// entry contributes one dashed accent rectangle whose border style is gated by
/// <c>MinVisible</c> = the entry's
/// <see cref="DatasetEntry.ContentMaxVisibleResolution"/>, so Mapsui reveals it
/// exactly when the dataset's own content drops out on zoom-out (no navigator
/// subscription required).
/// </para>
/// <para>
/// An entry qualifies only when it is loaded, visible, carries a captured
/// <see cref="DatasetEntry.MercatorExtent"/>, and has a non-null content cutoff
/// (i.e. a display-scale minimum that actually suppresses it). Datasets that
/// never disappear on zoom-out contribute nothing. The whole overlay is
/// suppressed when the mariner disables
/// <see cref="SettingsViewModel.ShowOutOfScaleExtentIndicators"/>.
/// </para>
/// </remarks>
internal sealed class DatasetExtentIndicatorController : IDisposable
{
    private readonly IMapLayerCollection _layers;
    private readonly DatasetsViewModel _datasets;
    private readonly IMeasureOverlayAppearanceProvider _appearance;
    private readonly SettingsViewModel _settings;
    private readonly Action<Action> _marshal;
    private readonly S100DatasetExtentIndicatorLayer _extent;
    private readonly HashSet<DatasetEntry> _subscribed = new();
    private bool _disposed;

    /// <summary>
    /// Creates and attaches the controller. The map host must already be
    /// initialised (basemap added) so the overlay lands above the basemap.
    /// </summary>
    /// <param name="layers">Target map layer collection.</param>
    /// <param name="datasets">The datasets view-model to observe.</param>
    /// <param name="appearance">Accent/theme provider for the border colour.</param>
    /// <param name="settings">Settings view-model supplying the on/off toggle.</param>
    /// <param name="marshal">
    /// Optional UI-thread marshalling override. Defaults to
    /// <see cref="Dispatcher.UIThread"/>; tests inject a synchronous
    /// implementation.
    /// </param>
    public DatasetExtentIndicatorController(
        IMapLayerCollection layers,
        DatasetsViewModel datasets,
        IMeasureOverlayAppearanceProvider appearance,
        SettingsViewModel settings,
        Action<Action>? marshal = null)
    {
        ArgumentNullException.ThrowIfNull(layers);
        ArgumentNullException.ThrowIfNull(datasets);
        ArgumentNullException.ThrowIfNull(appearance);
        ArgumentNullException.ThrowIfNull(settings);

        _layers = layers;
        _datasets = datasets;
        _appearance = appearance;
        _settings = settings;
        _marshal = marshal ?? DispatcherMarshal;

        // Keep the historical layer name so anything that finds the overlay by
        // name (z-order, diagnostics) is unaffected by the reusable-layer default.
        _extent = new S100DatasetExtentIndicatorLayer(name: "Dataset Extent Indicators");

        _datasets.Entries.CollectionChanged += OnEntriesChanged;
        foreach (var entry in _datasets.Entries)
            Subscribe(entry);

        _appearance.Changed += OnAppearanceChanged;
        _settings.ExtentIndicatorsChanged += OnToggleChanged;

        _marshal(() =>
        {
            _layers.AddOverlayLayer(_extent.Layer);
            Rebuild();
        });
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (DatasetEntry entry in e.OldItems)
                Unsubscribe(entry);

        if (e.NewItems is not null)
            foreach (DatasetEntry entry in e.NewItems)
                Subscribe(entry);

        // A reset clears OldItems/NewItems; resync from scratch.
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
        if (e.PropertyName is nameof(DatasetEntry.MercatorExtent)
            or nameof(DatasetEntry.ContentMaxVisibleResolution)
            or nameof(DatasetEntry.IsLoaded)
            or nameof(DatasetEntry.IsDeferred)
            or nameof(DatasetEntry.IsVisible))
        {
            _marshal(Rebuild);
        }
    }

    private void OnAppearanceChanged(object? sender, EventArgs e) => _marshal(Rebuild);

    private void OnToggleChanged() => _marshal(Rebuild);

    private void Rebuild()
    {
        if (_disposed) return;

        var indicators = new List<S100DatasetExtentIndicator>();

        if (_settings.ShowOutOfScaleExtentIndicators)
        {
            foreach (var entry in _datasets.Entries)
            {
                // A cell registered for lazy loading but not yet loaded (issue
                // #458): outline its catalogue footprint at every zoom
                // (MinVisible 0) so the mariner sees where unloaded data lies
                // and that panning/zooming there will pull it in. Honour the
                // visibility toggle here too — a hidden deferred cell shows no
                // outline, consistent with a hidden loaded dataset.
                if (entry.IsDeferred)
                {
                    if (entry.IsVisible && entry.GeographicBounds is { } bounds)
                    {
                        foreach (var deferredExtent in ToMercatorExtents(bounds))
                            indicators.Add(new S100DatasetExtentIndicator(deferredExtent, 0.0));
                    }
                    continue;
                }

                if (!entry.IsLoaded || !entry.IsVisible)
                    continue;
                if (entry.MercatorExtent is not { } extent)
                    continue;
                if (entry.ContentMaxVisibleResolution is not { } cutoff)
                    continue;

                indicators.Add(new S100DatasetExtentIndicator(extent, cutoff));
            }
        }

        _extent.Show(indicators, _appearance.Current.Accent);
    }

    /// <summary>
    /// Projects an EPSG:4326 catalogue footprint to one or two EPSG:3857
    /// (web-mercator) rectangles for the overlay. A footprint that crosses the
    /// ±180° antimeridian seam (west &gt; east) is split into two non-wrapping
    /// boxes — <c>[west, +180]</c> and <c>[-180, east]</c> — so seam-crossing
    /// deferred cells still show an outline. Yields nothing for a degenerate or
    /// unprojectable box.
    /// </summary>
    private static IEnumerable<Mapsui.MRect> ToMercatorExtents(ExchangeSets.BoundingBox bounds)
    {
        var west = bounds.WestBoundLongitude;
        var east = bounds.EastBoundLongitude;
        var south = bounds.SouthBoundLatitude;
        var north = bounds.NorthBoundLatitude;

        if (!double.IsNaN(west) && !double.IsNaN(east) && west > east)
        {
            if (ToMercatorExtent(west, 180.0, south, north) is { } eastSegment)
                yield return eastSegment;
            if (ToMercatorExtent(-180.0, east, south, north) is { } westSegment)
                yield return westSegment;
            yield break;
        }

        if (ToMercatorExtent(west, east, south, north) is { } extent)
            yield return extent;
    }

    /// <summary>
    /// Projects a single non-wrapping EPSG:4326 rectangle to an EPSG:3857
    /// (web-mercator) rectangle for the overlay. Returns <see langword="null"/>
    /// for a degenerate or unprojectable box.
    /// </summary>
    private static Mapsui.MRect? ToMercatorExtent(
        double west, double east, double south, double north)
    {
        if (double.IsNaN(west) || double.IsNaN(east)
            || double.IsNaN(south) || double.IsNaN(north))
        {
            return null;
        }

        // Mercator is undefined at the poles; clamp to the projection's limit.
        south = Math.Clamp(south, -85.05112878, 85.05112878);
        north = Math.Clamp(north, -85.05112878, 85.05112878);

        var (minX, minY) = Mapsui.Projections.SphericalMercator.FromLonLat(west, south);
        var (maxX, maxY) = Mapsui.Projections.SphericalMercator.FromLonLat(east, north);
        if (maxX <= minX || maxY <= minY)
            return null;

        return new Mapsui.MRect(minX, minY, maxX, maxY);
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

        _appearance.Changed -= OnAppearanceChanged;
        _settings.ExtentIndicatorsChanged -= OnToggleChanged;

        _marshal(() => _layers.RemoveOverlayLayer(_extent.Layer));
    }
}

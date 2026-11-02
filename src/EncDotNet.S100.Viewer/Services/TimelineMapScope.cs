using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Threading;
using EncDotNet.S100.Collections;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Viewer.ViewModels;
using Mapsui;
using Mapsui.Projections;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// What the Timeline's lanes need from the map (#710, handoff E4): which
/// time-aware datasets intersect the map view, and a footprint highlight for
/// the lane under the pointer.
/// </summary>
internal interface ITimelineMapScope
{
    /// <summary>
    /// True when the dataset's footprint intersects the map view, false when it
    /// lies outside; <see langword="null"/> when either is not known yet.
    /// </summary>
    /// <param name="datasetId">The session dataset id (<c>MapsuiMapTimedDataset.DatasetId</c>).</param>
    bool? IsInMapView(string datasetId);

    /// <summary>Raised when the map view or a footprint changes.</summary>
    event Action? Changed;

    /// <summary>Outlines the dataset's footprint on the map; <see langword="null"/> clears it.</summary>
    /// <param name="datasetId">The session dataset id, or null.</param>
    /// <param name="color">The outline colour (the lane's product colour).</param>
    void Highlight(string? datasetId, (byte R, byte G, byte B) color = default);

    /// <summary>True when <paramref name="bounds"/> intersect the map view; null before the first view.</summary>
    /// <param name="bounds">A footprint in degrees.</param>
    bool? Intersects(GeoBounds bounds);

    /// <summary>Outlines footprints on the map (a Library lane's tiles, #711); empty clears them.</summary>
    /// <param name="areas">Footprints in degrees.</param>
    /// <param name="color">The outline colour.</param>
    void HighlightAreas(IReadOnlyList<GeoBounds> areas, (byte R, byte G, byte B) color = default);
}

/// <summary>
/// <see cref="ITimelineMapScope"/> over the map's viewport and the loaded
/// datasets' extents: a dataset's footprint is its rendered extent, or the
/// exchange-set catalogue's box before it has drawn.
/// </summary>
internal sealed class TimelineMapScope : ITimelineMapScope
{
    private const string OverlayLayerName = "Timeline Lane Footprint";

    private readonly IMapViewportNotifier _viewport;
    private readonly DatasetsViewModel _datasets;
    private readonly HashSet<DatasetEntry> _subscribed = [];

    // The Timeline asks about every lane on every axis rebuild, and that ran on
    // every pan, load and Library change; each answer looked its entry up by a
    // scan of every entry and re-projected the view. Keep an id index and the
    // answers until the view, the entries or a footprint changes.
    private readonly object _cacheLock = new();
    private readonly Dictionary<string, bool?> _inView = new(StringComparer.Ordinal);
    private Dictionary<string, DatasetEntry>? _byId;
    private MapViewportSnapshot? _viewSnapshot;
    private GeoBounds? _view;
    private readonly S100DatasetExtentIndicatorLayer _overlay = new(
        new S100DatasetExtentIndicatorStyle { OutlineWidth = 2.5, OutlineOpacity = 0.9f },
        OverlayLayerName);

    public TimelineMapScope(IMapViewportNotifier viewport, DatasetsViewModel datasets)
    {
        ArgumentNullException.ThrowIfNull(viewport);
        ArgumentNullException.ThrowIfNull(datasets);
        _viewport = viewport;
        _datasets = datasets;
        _viewport.ViewportChanged += (_, _) => Raise();
        _datasets.Entries.CollectionChanged += OnEntriesChanged;
        foreach (var entry in _datasets.Entries)
            Subscribe(entry);
    }

    /// <inheritdoc />
    public event Action? Changed;

    /// <summary>Adds the footprint highlight to the map (once the map host exists).</summary>
    /// <param name="layers">The map's layers.</param>
    public void Attach(IMapLayerCollection layers)
    {
        ArgumentNullException.ThrowIfNull(layers);
        layers.AddOverlayLayer(_overlay.Layer);
    }

    /// <inheritdoc />
    public bool? IsInMapView(string datasetId)
    {
        lock (_cacheLock)
        {
            var view = CachedViewLocked();
            if (_inView.TryGetValue(datasetId, out var cached))
                return cached;

            bool? inView = view is { } v && Footprint(datasetId) is { } footprint
                ? footprint.Intersects(v)
                : null;
            _inView[datasetId] = inView;
            return inView;
        }
    }

    /// <summary>
    /// The view in degrees for the current viewport, converted once per
    /// viewport; a new viewport drops the cached answers.
    /// </summary>
    private GeoBounds? CachedViewLocked()
    {
        var snapshot = _viewport.Current;
        if (!ReferenceEquals(snapshot, _viewSnapshot))
        {
            _viewSnapshot = snapshot;
            _view = ViewOf(snapshot);
            _inView.Clear();
        }
        return _view;
    }

    private void InvalidateCache(bool entries)
    {
        lock (_cacheLock)
        {
            _inView.Clear();
            if (entries)
                _byId = null;
        }
    }

    /// <inheritdoc />
    public void Highlight(string? datasetId, (byte R, byte G, byte B) color = default)
    {
        var extent = datasetId is null ? null : Entry(datasetId)?.MercatorExtent ?? CatalogueExtent(datasetId);
        if (extent is null)
        {
            _overlay.Clear();
            return;
        }
        _overlay.Show([new S100DatasetExtentIndicator(extent, 0)], color == default ? ((byte)0x25, (byte)0x63, (byte)0xEB) : color);
    }

    /// <inheritdoc />
    public bool? Intersects(GeoBounds bounds)
    {
        GeoBounds? view;
        lock (_cacheLock)
            view = CachedViewLocked();
        return view is { } v ? bounds.Intersects(v) : null;
    }

    /// <inheritdoc />
    public void HighlightAreas(IReadOnlyList<GeoBounds> areas, (byte R, byte G, byte B) color = default)
    {
        if (areas.Count == 0)
        {
            _overlay.Clear();
            return;
        }
        _overlay.Show(
            areas.Select(a => new S100DatasetExtentIndicator(Mercator(a), 0)),
            color == default ? ((byte)0x25, (byte)0x63, (byte)0xEB) : color);
    }

    private static MRect Mercator(GeoBounds bounds)
    {
        var east = bounds.CrossesAntimeridian ? bounds.East + 360 : bounds.East;
        var (minX, minY) = SphericalMercator.FromLonLat(bounds.West, Math.Max(bounds.South, -85));
        var (maxX, maxY) = SphericalMercator.FromLonLat(east, Math.Min(bounds.North, 85));
        return new MRect(minX, minY, maxX, maxY);
    }

    private DatasetEntry? Entry(string datasetId)
    {
        lock (_cacheLock)
        {
            if (_byId is null)
            {
                // The first entry with an id wins, as a scan in order would find.
                _byId = new Dictionary<string, DatasetEntry>(StringComparer.Ordinal);
                foreach (var entry in _datasets.Entries)
                    _byId.TryAdd(entry.Id.Value, entry);
            }
            return _byId.GetValueOrDefault(datasetId);
        }
    }

    /// <summary>The dataset's footprint in degrees: its drawn extent, else its catalogue box.</summary>
    private GeoBounds? Footprint(string datasetId) =>
        Entry(datasetId) is { } entry ? FootprintOf(entry) : null;

    /// <summary>
    /// A dataset's footprint in degrees: its drawn extent, else its catalogue
    /// box; null when neither is known. The depth basis indicator (#1191)
    /// tests the same footprint.
    /// </summary>
    /// <param name="entry">The dataset.</param>
    internal static GeoBounds? FootprintOf(DatasetEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.MercatorExtent is { } extent)
        {
            var (west, south) = SphericalMercator.ToLonLat(extent.MinX, extent.MinY);
            var (east, north) = SphericalMercator.ToLonLat(extent.MaxX, extent.MaxY);
            return new GeoBounds(south, west, north, east);
        }
        return entry.GeographicBounds is { } box
            ? new GeoBounds(box.SouthBoundLatitude, box.WestBoundLongitude, box.NorthBoundLatitude, box.EastBoundLongitude)
            : null;
    }

    private MRect? CatalogueExtent(string datasetId)
    {
        if (Entry(datasetId)?.GeographicBounds is not { } box)
            return null;
        var (minX, minY) = SphericalMercator.FromLonLat(box.WestBoundLongitude, Math.Max(box.SouthBoundLatitude, -85));
        var (maxX, maxY) = SphericalMercator.FromLonLat(box.EastBoundLongitude, Math.Min(box.NorthBoundLatitude, 85));
        return new MRect(minX, minY, maxX, maxY);
    }


    /// <summary>
    /// A viewport in degrees, or null before the first viewport. A view that
    /// spans the globe covers every longitude.
    /// </summary>
    /// <param name="snapshot">The viewport, or null.</param>
    internal static GeoBounds? ViewOf(MapViewportSnapshot? snapshot)
    {
        if (snapshot is null)
            return null;
        if (snapshot.LongitudeSpanDegrees >= 359)
            return new GeoBounds(snapshot.MinLatitude, -180, snapshot.MaxLatitude, 180);
        var west = GeoBounds.NormalizeLongitude(snapshot.MinLongitude);
        var east = west + snapshot.LongitudeSpanDegrees;
        if (east > 180)
            east -= 360;
        return new GeoBounds(snapshot.MinLatitude, west, snapshot.MaxLatitude, east);
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var entry in _subscribed)
                entry.PropertyChanged -= OnEntryChanged;
            _subscribed.Clear();
            foreach (var entry in _datasets.Entries)
                Subscribe(entry);
        }
        else
        {
            foreach (DatasetEntry entry in e.OldItems ?? Array.Empty<DatasetEntry>())
            {
                if (_subscribed.Remove(entry))
                    entry.PropertyChanged -= OnEntryChanged;
            }
            foreach (DatasetEntry entry in e.NewItems ?? Array.Empty<DatasetEntry>())
                Subscribe(entry);
        }
        InvalidateCache(entries: true);
        Raise();
    }

    private void Subscribe(DatasetEntry entry)
    {
        if (_subscribed.Add(entry))
            entry.PropertyChanged += OnEntryChanged;
    }

    private void OnEntryChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DatasetEntry.MercatorExtent))
        {
            InvalidateCache(entries: false);
            Raise();
        }
    }

    private void Raise()
    {
        if (Dispatcher.UIThread.CheckAccess())
            Changed?.Invoke();
        else
            Dispatcher.UIThread.Post(() => Changed?.Invoke());
    }
}

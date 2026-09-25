using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Threading;
using EncDotNet.S100.Collections;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Viewer.Library;
using EncDotNet.S100.Viewer.Services.LazyLoading;
using EncDotNet.S100.Viewer.Tools;
using EncDotNet.S100.Viewer.ViewModels;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Styles;
using NetTopologySuite.Geometries;
using MapsuiColor = Mapsui.Styles.Color;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Draws the coverage of the datasets listed in the Library panel on the map
/// (issue #655), without loading them, and keeps map and panel selection in
/// step.
/// </summary>
/// <remarks>
/// <para>
/// The overlay shows exactly what the panel lists — the selected collection
/// or source, or the datasets at a tapped location, after the panel's text
/// and "show cancelled" filters — and only while the Library tab is showing
/// and its coverage toggle is on. Outlines are styled by availability
/// (local solid, online dashed, catalogue-only dotted, missing red) and
/// gated like lazy loading: ENC cells by usage band for the current scale,
/// other datasets by display scale, and everything by the viewport. The
/// selected dataset is drawn on top in the accent colour with a faint fill.
/// </para>
/// <para>
/// A plain map tap while the overlay is active lists and selects the
/// datasets under the tap (<see cref="LibraryPanelViewModel.SelectAt"/>);
/// the panel's "Zoom to" moves the map to a dataset's bounds.
/// </para>
/// </remarks>
internal sealed class LibraryCoverageOverlayController : IDisposable
{
    private const string LayerName = "Library Coverage";

    /// <summary>Upper bound on outlined datasets per redraw, to keep panning smooth.</summary>
    internal const int MaxOutlinedItems = 2500;

    private static readonly ConditionalWeakTable<CollectionItem, IReadOnlyList<(double X, double Y)[]>> RingCache = new();

    private readonly IMapLayerCollection _layers;
    private readonly LibraryPanelViewModel _panel;
    private readonly MainViewModel _main;
    private readonly IMapViewportNotifier _viewport;
    private readonly IMeasureOverlayAppearanceProvider _appearance;
    private readonly Func<IMapViewportController?> _viewportController;
    private readonly Action<Action> _marshal;
    private readonly MemoryLayer _layer;
    private bool _disposed;
    private bool _rebuildPosted;

    public LibraryCoverageOverlayController(
        IMapLayerCollection layers,
        LibraryPanelViewModel panel,
        MainViewModel main,
        IMapViewportNotifier viewport,
        IMeasureOverlayAppearanceProvider appearance,
        Func<IMapViewportController?> viewportController,
        Action<Action>? marshal = null)
    {
        ArgumentNullException.ThrowIfNull(layers);
        ArgumentNullException.ThrowIfNull(panel);
        ArgumentNullException.ThrowIfNull(main);
        ArgumentNullException.ThrowIfNull(viewport);
        ArgumentNullException.ThrowIfNull(appearance);
        ArgumentNullException.ThrowIfNull(viewportController);

        _layers = layers;
        _panel = panel;
        _main = main;
        _viewport = viewport;
        _appearance = appearance;
        _viewportController = viewportController;
        _marshal = marshal ?? DispatcherMarshal;
        _layer = new MemoryLayer { Name = LayerName, Style = null, Features = new List<IFeature>() };

        _panel.PropertyChanged += OnPanelChanged;
        _panel.ZoomRequested += OnZoomRequested;
        _main.PropertyChanged += OnMainChanged;
        _viewport.ViewportChanged += OnViewportChanged;
        _appearance.Changed += OnAppearanceChanged;

        _marshal(() =>
        {
            _layers.AddOverlayLayer(_layer);
            Rebuild();
        });
    }

    /// <summary>True when the Library tab is showing and its coverage toggle is on.</summary>
    public bool IsActive =>
        _panel.ShowCoverage
        && _main.IsLeftDockOpen
        && string.Equals(_main.SelectedLeftTabId, LibraryImportCoordinator.LibraryPanelId, StringComparison.Ordinal);

    /// <summary>The number of datasets outlined by the last redraw (for tests and diagnostics).</summary>
    public int OutlinedCount { get; private set; }

    /// <summary>
    /// Handles a plain map tap: when the overlay is active, lists and selects
    /// the library datasets under <paramref name="position"/>.
    /// </summary>
    public bool HandleTap(GeoPosition position) => IsActive && _panel.SelectAt(position);

    /// <summary>The overlay layer (for tests).</summary>
    internal MemoryLayer Layer => _layer;

    private void OnPanelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LibraryPanelViewModel.Items)
            or nameof(LibraryPanelViewModel.SelectedItem)
            or nameof(LibraryPanelViewModel.ShowCoverage))
        {
            ScheduleRebuild();
        }
    }

    private void OnMainChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.SelectedLeftTab)
            or nameof(MainViewModel.SelectedLeftTabId)
            or nameof(MainViewModel.IsLeftDockOpen))
        {
            ScheduleRebuild();
        }
    }

    private void OnViewportChanged(object? sender, MapViewportSnapshot e)
    {
        if (IsActive)
            ScheduleRebuild();
    }

    private void OnAppearanceChanged(object? sender, EventArgs e) => ScheduleRebuild();

    private void OnZoomRequested(object? sender, GeoBounds bounds)
    {
        _marshal(() =>
        {
            if (_viewportController() is not { } controller)
                return;

            var east = bounds.CrossesAntimeridian ? bounds.East + 360 : bounds.East;
            var (minX, minY) = Mapsui.Projections.SphericalMercator.FromLonLat(bounds.West, Math.Max(bounds.South, -85));
            var (maxX, maxY) = Mapsui.Projections.SphericalMercator.FromLonLat(east, Math.Min(bounds.North, 85));
            if (maxX > minX && maxY > minY)
                controller.ZoomToExtent(new MRect(minX, minY, maxX, maxY).Grow((maxX - minX) * 0.1, (maxY - minY) * 0.1));
        });
    }

    private void ScheduleRebuild()
    {
        if (_rebuildPosted)
            return;
        _rebuildPosted = true;
        _marshal(() =>
        {
            _rebuildPosted = false;
            Rebuild();
        });
    }

    /// <summary>Recomputes the overlay's features.</summary>
    internal void Rebuild()
    {
        if (_disposed)
            return;

        var features = new List<IFeature>();
        OutlinedCount = 0;

        if (IsActive)
        {
            var snapshot = _viewport.Current;
            var view = ViewBounds(snapshot);
            var scale = snapshot is null
                ? double.NaN
                : LazyCellGate.ScaleDenominator(snapshot.MercatorResolution, (snapshot.MinLatitude + snapshot.MaxLatitude) / 2);
            var selected = _panel.SelectedItem;

            var candidates = _panel.Items
                // Loaded datasets speak for themselves on the chart.
                .Where(i => !ReferenceEquals(i, selected) && i.Item.Bounds is { } b
                    && i.Availability != LibraryAvailability.Loaded
                    && (view is null || b.Intersects(view.Value))
                    && CoverageGeometry.IsVisibleAtScale(i.Item, scale))
                // Most detailed last, so they draw on top; keep the most detailed when capping.
                .OrderByDescending(i => CoverageGeometry.Area(i.Item))
                .TakeLast(MaxOutlinedItems);

            foreach (var item in candidates)
            {
                AddOutline(features, item, StyleFor(item.Availability));
                OutlinedCount++;
            }

            if (selected?.Item.Bounds is not null)
            {
                var accent = _appearance.Current.Accent;
                AddOutline(features, selected, new OutlineStyle(new MapsuiColor(accent.R, accent.G, accent.B), 3.0, null, 0.12f));
                OutlinedCount++;
            }
        }

        _layer.Features = features;
        _layer.DataHasChanged();
    }

    private static void AddOutline(List<IFeature> features, LibraryItemViewModel item, OutlineStyle style)
    {
        var rings = RingCache.GetValue(item.Item, static i => CoverageGeometry.ToMercatorRings(i));
        foreach (var ring in rings)
        {
            if (ring.Length < 2)
                continue;

            var coordinates = new Coordinate[ring.Length];
            for (var i = 0; i < ring.Length; i++)
                coordinates[i] = new Coordinate(ring[i].X, ring[i].Y);

            if (style.FillOpacity > 0 && ring.Length >= 4 && coordinates[0].Equals2D(coordinates[^1]))
            {
                var fill = new GeometryFeature(new Polygon(new LinearRing(coordinates)));
                fill.Styles.Add(new VectorStyle
                {
                    Fill = new Brush { Color = style.Color },
                    Outline = null,
                    Line = null,
                    Opacity = style.FillOpacity,
                });
                features.Add(fill);
            }

            var line = new GeometryFeature(new LineString(coordinates));
            line.Styles.Add(new VectorStyle
            {
                Line = new Pen
                {
                    Color = style.Color,
                    Width = style.Width,
                    PenStyle = style.DashArray is null ? PenStyle.Solid : PenStyle.UserDefined,
                    DashArray = style.DashArray,
                    PenStrokeCap = PenStrokeCap.Round,
                },
                Opacity = 0.9f,
            });
            features.Add(line);
        }
    }

    private static OutlineStyle StyleFor(LibraryAvailability availability) => availability switch
    {
        LibraryAvailability.Local => new OutlineStyle(new MapsuiColor(0x3d, 0x8a, 0x5a), 1.6, null, 0),
        LibraryAvailability.Online => new OutlineStyle(new MapsuiColor(0x3f, 0x6f, 0xb5), 1.6, [6f, 4f], 0),
        LibraryAvailability.Missing => new OutlineStyle(new MapsuiColor(0xc0, 0x50, 0x4d), 1.6, [4f, 3f], 0),
        LibraryAvailability.Deferred => new OutlineStyle(new MapsuiColor(0x8a, 0x6f, 0xb8), 1.6, [6f, 3f], 0.06f),
        _ => new OutlineStyle(new MapsuiColor(0x80, 0x86, 0x90), 1.4, [1.5f, 3f], 0),
    };

    private static GeoBounds? ViewBounds(MapViewportSnapshot? snapshot)
    {
        if (snapshot is null || snapshot.LongitudeSpanDegrees >= 359)
            return null;

        var west = GeoBounds.NormalizeLongitude(snapshot.MinLongitude);
        var east = west + snapshot.LongitudeSpanDegrees;
        if (east > 180)
            east -= 360;
        return new GeoBounds(snapshot.MinLatitude, west, snapshot.MaxLatitude, east);
    }

    private static void DispatcherMarshal(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _panel.PropertyChanged -= OnPanelChanged;
        _panel.ZoomRequested -= OnZoomRequested;
        _main.PropertyChanged -= OnMainChanged;
        _viewport.ViewportChanged -= OnViewportChanged;
        _appearance.Changed -= OnAppearanceChanged;

        _marshal(() => _layers.RemoveOverlayLayer(_layer));
    }

    private readonly record struct OutlineStyle(MapsuiColor Color, double Width, float[]? DashArray, float FillOpacity);
}

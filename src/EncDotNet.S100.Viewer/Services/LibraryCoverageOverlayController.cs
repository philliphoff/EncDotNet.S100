using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Threading;
using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Library;
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
/// A plain map tap while the overlay is active selects the most detailed
/// outlined dataset under the tap, and leaves the list alone
/// (<see cref="LibraryPanelViewModel.SelectTapHits"/>). Only what is drawn can
/// be hit: the same candidates the overlay outlines, plus loaded datasets.
/// Tapping the same spot again steps to the next hit; tapping where nothing
/// is outlined clears the tap. The panel's "Zoom to" moves the map to a
/// dataset's bounds.
/// </para>
/// <para>
/// Zoomed out past <see cref="AreaScaleThreshold"/>, a remote S-100
/// catalogue's tiles (thousands for NOAA's S-102) are drawn as one outline per
/// area instead: solid when every listed tile is local, amber when any has a
/// newer edition, dashed otherwise. Tapping an area selects its node in the tree.
/// </para>
/// </remarks>
internal sealed class LibraryCoverageOverlayController : IDisposable
{
    private const string LayerName = "Library Coverage";

    /// <summary>Upper bound on outlined datasets per redraw, to keep panning smooth.</summary>
    internal const int MaxOutlinedItems = 2500;

    /// <summary>
    /// Zoomed out beyond this scale (1:1 500 000), a remote S-100 catalogue's
    /// tiles are drawn as one outline per area (#685, handoff E2).
    /// </summary>
    internal const double AreaScaleThreshold = 1_500_000;

    /// <summary>
    /// An item whose coverage is smaller than this on screen (pixels, either
    /// side) is drawn as a marker, not an outline: a point-sized warning
    /// (S-124) would otherwise draw nothing (#809).
    /// </summary>
    internal const double MarkerThresholdPixels = 6;

    /// <summary>The marker's diameter, in pixels; taps within it select the item.</summary>
    internal const double MarkerPixels = 13;

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
    private MapTap? _lastTap;

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
        _panel.CenterRequested += OnCenterRequested;
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
    /// Handles a plain map tap: when the overlay is active, selects the most
    /// detailed outlined dataset under <paramref name="tap"/> (the next one,
    /// when the same spot is tapped again), or clears the tap when nothing
    /// outlined is there. Returns true when something was hit.
    /// </summary>
    public bool HandleTap(MapTap tap)
    {
        ArgumentNullException.ThrowIfNull(tap);
        if (!IsActive)
            return false;

        var scale = CurrentScale() is var current && !double.IsNaN(current)
            ? current
            : LazyCellGate.ScaleDenominator(tap.Resolution, tap.Position.Latitude);

        // A forecast model's domain (E1): list everything under the point, the
        // smallest domain's model selected. Zoomed out, an S-102 area selects its node.
        var areaHits = AreaHits(Areas(_panel.Items, scale), tap.Position);
        if (areaHits.Where(a => a.Area.Model is not null).ToArray() is { Length: > 0 } models)
        {
            _lastTap = null;
            _panel.ListModelsAt(tap.Position, models.Select(m => (m.SourceId, m.Area.Model!)).ToArray());
            return true;
        }

        if (areaHits.FirstOrDefault() is { Area: { } area } hit)
        {
            _lastTap = null;
            _panel.ClearTap();
            _panel.SelectArea(hit.SourceId, area.Folder);
            return true;
        }

        var hits = Hits(_panel.Items, _panel.SelectedItem, tap.Position, scale, tap.Resolution);
        if (hits.Count == 0)
        {
            _lastTap = null;
            _panel.ClearTap();
            return false;
        }

        var sameSpot = _panel.HasTap && MapTap.IsSameSpot(_lastTap, tap);
        _lastTap = tap;
        _panel.SelectTapHits(tap.Position, hits, sameSpot);
        return true;
    }

    /// <summary>
    /// The datasets a tap at <paramref name="position"/> hits: the
    /// <see cref="Candidates"/> at <paramref name="scale"/> (including loaded
    /// datasets, which the overlay leaves to the chart, and the selection,
    /// which is always drawn) whose coverage contains it, most detailed
    /// (highest usage band, then smallest area) first.
    /// </summary>
    /// <remarks>
    /// With <paramref name="metresPerPixel"/>, an item drawn as a marker (see
    /// <see cref="IsPointSized"/>) is hit by a tap within the marker.
    /// </remarks>
    internal static IReadOnlyList<LibraryItemViewModel> Hits(
        IEnumerable<LibraryItemViewModel> listed, LibraryItemViewModel? selected, GeoPosition position, double scale,
        double metresPerPixel = 0) =>
        Candidates(listed, scale)
            .Concat(selected?.Item.Bounds is not null && !selected.IsGroupHeader ? [selected] : [])
            .Distinct()
            .Where(i => CoverageHitTest.Contains(i.Item, position) || IsMarkerHit(i.Item, position, metresPerPixel))
            .OrderByDescending(i => i.Item.UsageBand ?? 0)
            .ThenBy(i => CoverageHitTest.Area(i.Item))
            .ToArray();

    /// <summary>
    /// The listed datasets that have an outline at <paramref name="scale"/>
    /// (ENC cells by usage band, others by display scale), whatever the
    /// viewport — the one candidate set both drawing and tapping use, so they
    /// cannot drift apart. Package header rows are never candidates.
    /// </summary>
    internal static IEnumerable<LibraryItemViewModel> Candidates(IEnumerable<LibraryItemViewModel> listed, double scale) =>
        listed.Where(i => !i.IsGroupHeader
            && i.Item.Bounds is not null
            && CoverageGeometry.IsVisibleAtScale(i.Item, scale)
            && !IsInArea(i, scale));

    /// <summary>
    /// True when <paramref name="item"/> is drawn as part of an area at
    /// <paramref name="scale"/>, not on its own: a forecast model's tile (its
    /// domain, at every scale), or — zoomed out — a remote catalogue's tile (its area).
    /// </summary>
    private static bool IsInArea(LibraryItemViewModel item, double scale) =>
        item.Source.Index is not null
        && (item.IsForecast && ForecastRuns.ModelOf(item.Item) is not null
            || (scale > AreaScaleThreshold
                && item.QuietUpdates
                && item.Item.Properties.ContainsKey(EncDotNet.S100.Collections.RemoteCatalogues.RemoteS100Catalogue.FolderProperty)));

    /// <summary>
    /// The areas drawn at <paramref name="scale"/>: one per folder of a remote
    /// catalogue among the <paramref name="listed"/> datasets (none when zoomed
    /// in), with the listed tiles in each.
    /// </summary>
    internal static IReadOnlyList<(Guid SourceId, LibraryArea Area, IReadOnlyList<LibraryItemViewModel> Items)> Areas(
        IEnumerable<LibraryItemViewModel> listed, double scale) =>
        listed
            // A collapsed forecast model lists only its row: its tiles make its domain.
            .SelectMany(i => i.IsModelHeader ? i.Members : i.IsGroupHeader ? [] : [i])
            .Distinct()
            .Where(i => IsInArea(i, scale))
            .GroupBy(i => (i.Source, Model: i.IsForecast ? ForecastRuns.ModelOf(i.Item) : null,
                Folder: EncDotNet.S100.Collections.RemoteCatalogues.RemoteS100Catalogue.FolderOf(i.Item)))
            .Select(g => (g.Key.Source.Id,
                Area: g.Key.Model is { } model
                    ? CoverageAreas.GetModel(g.Key.Source.Index!, model)
                    : CoverageAreas.Get(g.Key.Source.Index!, g.Key.Folder),
                Items: (IReadOnlyList<LibraryItemViewModel>)g.ToArray()))
            .Where(a => a.Area is not null)
            .Select(a => (a.Id, a.Area!, a.Items))
            .ToArray();

    /// <summary>The areas containing <paramref name="position"/>, smallest first.</summary>
    private static IReadOnlyList<(Guid SourceId, LibraryArea Area, IReadOnlyList<LibraryItemViewModel> Items)> AreaHits(
        IReadOnlyList<(Guid SourceId, LibraryArea Area, IReadOnlyList<LibraryItemViewModel> Items)> areas, GeoPosition position)
    {
        if (areas.Count == 0)
            return [];
        var (x, y) = Mapsui.Projections.SphericalMercator.FromLonLat(position.Longitude, position.Latitude);
        return areas.Where(a => a.Area.Contains(x, y)).OrderBy(a => a.Area.Shape.Area).ToArray();
    }

    /// <summary>
    /// An area's outline (handoff E2): amber when any listed tile has a newer
    /// edition, solid when every listed tile is local, dashed otherwise.
    /// </summary>
    internal static LibraryPrimaryAvailability AreaState(IReadOnlyList<LibraryItemViewModel> items) =>
        items.Any(i => i.PrimaryAvailability == LibraryPrimaryAvailability.Update) ? LibraryPrimaryAvailability.Update
        : items.Any(i => i.PrimaryAvailability == LibraryPrimaryAvailability.Expired) ? LibraryPrimaryAvailability.Expired
        : items.All(i => i.PrimaryAvailability == LibraryPrimaryAvailability.Local) ? LibraryPrimaryAvailability.Local
        : LibraryPrimaryAvailability.Online;

    /// <summary>
    /// True when <paramref name="item"/>'s coverage is smaller than
    /// <see cref="MarkerThresholdPixels"/> on screen at
    /// <paramref name="metresPerPixel"/> (Web Mercator), so it is drawn as a marker.
    /// </summary>
    internal static bool IsPointSized(CollectionItem item, double metresPerPixel)
    {
        if (item.Bounds is not { } b || !(metresPerPixel > 0))
            return false;
        var east = b.CrossesAntimeridian ? b.East + 360 : b.East;
        var (minX, minY) = Mapsui.Projections.SphericalMercator.FromLonLat(b.West, Math.Max(b.South, -85));
        var (maxX, maxY) = Mapsui.Projections.SphericalMercator.FromLonLat(east, Math.Min(b.North, 85));
        var limit = MarkerThresholdPixels * metresPerPixel;
        return maxX - minX < limit && maxY - minY < limit;
    }

    /// <summary>The marker's centre (Web Mercator metres): the middle of the item's bounds.</summary>
    internal static (double X, double Y) MarkerCentre(CollectionItem item)
    {
        var b = item.Bounds!.Value;
        var east = b.CrossesAntimeridian ? b.East + 360 : b.East;
        return Mapsui.Projections.SphericalMercator.FromLonLat(
            (b.West + east) / 2, (Math.Max(b.South, -85) + Math.Min(b.North, 85)) / 2);
    }

    /// <summary>True when a tap at <paramref name="position"/> falls within <paramref name="item"/>'s marker.</summary>
    private static bool IsMarkerHit(CollectionItem item, GeoPosition position, double metresPerPixel)
    {
        if (!IsPointSized(item, metresPerPixel))
            return false;
        var (x, y) = MarkerCentre(item);
        var (tx, ty) = Mapsui.Projections.SphericalMercator.FromLonLat(position.Longitude, position.Latitude);
        var radius = MarkerPixels / 2 * metresPerPixel;
        return (x - tx) * (x - tx) + (y - ty) * (y - ty) <= radius * radius;
    }

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

    /// <summary>Pans the map to a dataset (a double-clicked row) without changing the zoom.</summary>
    private void OnCenterRequested(object? sender, GeoPosition center) =>
        _marshal(() => _viewportController()?.CenterOn(center.Latitude, center.Longitude));

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
            var view = ViewBounds(_viewport.Current);
            var scale = CurrentScale();
            var resolution = _viewport.Current?.MercatorResolution ?? 0;
            var selected = _panel.SelectedItem;
            var appearance = _appearance.Current;
            var casing = new MapsuiColor(appearance.ChartBackground.R, appearance.ChartBackground.G, appearance.ChartBackground.B);

            // While a dataset is selected, the others fade so it stands out without being thick.
            var opacity = selected is null ? LibraryOutlineStyles.LineOpacity : LibraryOutlineStyles.DimmedLineOpacity;

            var mercatorView = view is { } v ? MercatorView(v) : null;
            foreach (var (_, area, items) in Areas(_panel.Items, scale))
            {
                if (mercatorView is { } mv && !mv.Intersects(area.Extent))
                    continue;
                AddRings(features, area.Rings, StyleFor(AreaState(items)), casing, opacity);
                OutlinedCount++;
            }

            var candidates = Candidates(_panel.Items, scale)
                // Loaded datasets speak for themselves on the chart.
                .Where(i => !ReferenceEquals(i, selected)
                    && i.Availability != LibraryAvailability.Loaded
                    && (view is null || i.Item.Bounds!.Value.Intersects(view.Value)))
                // Most detailed last, so they draw on top; keep the most detailed when capping.
                .OrderByDescending(i => CoverageHitTest.Area(i.Item))
                .TakeLast(MaxOutlinedItems);

            foreach (var item in candidates)
            {
                AddCoverage(features, item, StyleFor(item.PrimaryAvailability), casing, opacity, resolution);
                OutlinedCount++;
            }

            if (selected?.Item.Bounds is not null && !selected.IsGroupHeader)
            {
                var accent = appearance.Accent;
                AddCoverage(features, selected,
                    new OutlineStyle(new MapsuiColor(accent.R, accent.G, accent.B), LibraryOutlineStyles.SelectedWidth, null,
                        LibraryOutlineStyles.SelectedFillOpacity, RoundCap: false),
                    casing, 1f, resolution);
                OutlinedCount++;
            }
        }

        _layer.Features = features;
        _layer.DataHasChanged();
    }

    /// <summary>A dataset's outline, or a marker when it is point-sized on screen.</summary>
    private static void AddCoverage(
        List<IFeature> features, LibraryItemViewModel item, OutlineStyle style, MapsuiColor casing, float opacity, double metresPerPixel)
    {
        if (IsPointSized(item.Item, metresPerPixel))
            AddMarker(features, item.Item, style, casing, opacity);
        else
            AddOutline(features, item, style, casing, opacity);
    }

    /// <summary>
    /// Adds a point-sized dataset's marker: a ring in the outline's colour over
    /// a casing, with the outline's faint fill.
    /// </summary>
    private static void AddMarker(List<IFeature> features, CollectionItem item, OutlineStyle style, MapsuiColor casing, float opacity)
    {
        var (x, y) = MarkerCentre(item);
        var marker = new GeometryFeature(new Point(x, y));
        marker.Styles.Add(new SymbolStyle
        {
            SymbolType = SymbolType.Ellipse,
            SymbolScale = MarkerPixels / 32,
            Fill = new Brush { Color = new MapsuiColor(style.Color.R, style.Color.G, style.Color.B, (int)(255 * Math.Max(style.FillOpacity, 0.15f))) },
            Outline = new Pen { Color = casing, Width = style.Width + LibraryOutlineStyles.CasingExtraWidth },
            Opacity = LibraryOutlineStyles.CasingOpacity,
        });
        marker.Styles.Add(new SymbolStyle
        {
            SymbolType = SymbolType.Ellipse,
            SymbolScale = MarkerPixels / 32,
            Fill = null,
            Outline = new Pen { Color = style.Color, Width = style.Width },
            Opacity = opacity,
        });
        features.Add(marker);
    }

    /// <summary>
    /// Adds a dataset's outline: an optional faint fill, a solid casing in the
    /// chart's background colour (so thin lines stay readable over depth
    /// contours and land), then the line itself.
    /// </summary>
    private static void AddOutline(
        List<IFeature> features, LibraryItemViewModel item, OutlineStyle style, MapsuiColor casing, float opacity) =>
        AddRings(features, RingCache.GetValue(item.Item, static i => CoverageGeometry.ToMercatorRings(i)), style, casing, opacity);

    /// <summary>Adds outline rings (Web Mercator metres) with a casing and an optional fill.</summary>
    private static void AddRings(
        List<IFeature> features, IReadOnlyList<(double X, double Y)[]> rings, OutlineStyle style, MapsuiColor casing, float opacity)
    {
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

            // Outline must be null: Mapsui otherwise draws its default grey
            // outline pen under a line string, as a heavy casing of its own.
            var line = new GeometryFeature(new LineString(coordinates));
            line.Styles.Add(new VectorStyle
            {
                Outline = null,
                Line = new Pen
                {
                    Color = casing,
                    Width = style.Width + LibraryOutlineStyles.CasingExtraWidth,
                    PenStyle = PenStyle.Solid,
                    PenStrokeCap = PenStrokeCap.Butt,
                },
                Opacity = LibraryOutlineStyles.CasingOpacity,
            });
            line.Styles.Add(new VectorStyle
            {
                Outline = null,
                Line = new Pen
                {
                    Color = style.Color,
                    Width = style.Width,
                    PenStyle = style.DashArray is null ? PenStyle.Solid : PenStyle.UserDefined,
                    DashArray = style.DashArray,
                    PenStrokeCap = style.RoundCap ? PenStrokeCap.Round : PenStrokeCap.Butt,
                },
                Opacity = opacity,
            });
            features.Add(line);
        }
    }

    /// <summary>
    /// The outline for a dataset's primary availability (where its data is),
    /// shared with the Library panel's row swatches so the list is the map's legend.
    /// </summary>
    private static OutlineStyle StyleFor(LibraryPrimaryAvailability availability)
    {
        var style = LibraryOutlineStyles.For(availability);
        return new OutlineStyle(
            new MapsuiColor(style.Color.R, style.Color.G, style.Color.B), style.Width, style.DashArray?.ToArray(), style.FillOpacity,
            style.RoundCap);
    }

    /// <summary>The scale the overlay gates outlines at: the viewport's, at its middle latitude (NaN before the first viewport).</summary>
    private double CurrentScale() => _viewport.Current is { } snapshot
        ? LazyCellGate.ScaleDenominator(snapshot.MercatorResolution, (snapshot.MinLatitude + snapshot.MaxLatitude) / 2)
        : double.NaN;

    /// <summary>The view in Web Mercator metres (for area extents).</summary>
    private static Envelope MercatorView(GeoBounds view)
    {
        var east = view.CrossesAntimeridian ? view.East + 360 : view.East;
        var (minX, minY) = Mapsui.Projections.SphericalMercator.FromLonLat(view.West, Math.Max(view.South, -85));
        var (maxX, maxY) = Mapsui.Projections.SphericalMercator.FromLonLat(east, Math.Min(view.North, 85));
        return new Envelope(minX, maxX, minY, maxY);
    }

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
        _panel.CenterRequested -= OnCenterRequested;
        _main.PropertyChanged -= OnMainChanged;
        _viewport.ViewportChanged -= OnViewportChanged;
        _appearance.Changed -= OnAppearanceChanged;

        _marshal(() => _layers.RemoveOverlayLayer(_layer));
    }

    private readonly record struct OutlineStyle(MapsuiColor Color, double Width, float[]? DashArray, float FillOpacity, bool RoundCap);
}

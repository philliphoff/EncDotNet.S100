using Avalonia.Threading;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Viewer.Resources;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Styles;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Union;
using MapsuiColor = Mapsui.Styles.Color;

namespace EncDotNet.S100.Viewer.Services.Depth;

/// <summary>
/// Draws the edge of the area where chart-wide water level adjustment
/// applies (S-98 Appendix D-3, <c>docs/design/chart-wide-water-level-adjustment.md</c>
/// §4): one dash-dot line in its own colour, above the chart, so no fill
/// hides it. Inside it the depths include the water level; outside they're
/// charted.
/// </summary>
/// <remarks>
/// The line follows <see cref="IWaterLevelAdjustmentState.Current"/>, the
/// adjustment the drawn layers apply, so it moves with the depths and never
/// ahead of them. Overlapping S-104 footprints are merged, so only the outer
/// edge is drawn.
/// </remarks>
internal sealed class AdjustedAreaBoundaryController : IDisposable
{
    /// <summary>The overlay layer's name.</summary>
    public static string LayerName => Strings.Map_WaterLevelAdjustmentBoundary;

    // The artboard's B4 boundary: teal, 2 px, dash-dot.
    private static readonly MapsuiColor LineColor = new(0x0F, 0x76, 0x6E);
    private static readonly MapsuiColor CasingColor = new(0xFF, 0xFF, 0xFF);
    private static readonly float[] DashArray = [10, 4, 2, 4];
    private const double LineWidth = 2;
    private const double CasingWidth = 4;
    private const float CasingOpacity = 0.6f;

    private readonly IMapLayerCollection _layers;
    private readonly IWaterLevelAdjustmentState _state;
    private readonly Action<Action> _marshal;
    private readonly MemoryLayer _layer;
    private IReadOnlyList<CoverageArea>? _drawn;
    private bool _disposed;

    /// <summary>Creates the controller and adds its layer to the map.</summary>
    /// <param name="layers">The map's layer collection.</param>
    /// <param name="state">The chart adjustment the drawn layers apply.</param>
    /// <param name="marshal">Runs an action on the UI thread.</param>
    public AdjustedAreaBoundaryController(IMapLayerCollection layers, IWaterLevelAdjustmentState state, Action<Action> marshal)
    {
        ArgumentNullException.ThrowIfNull(layers);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(marshal);
        _layers = layers;
        _state = state;
        _marshal = marshal;
        _layer = new MemoryLayer { Name = LayerName, Style = null, Features = new List<IFeature>() };
        _state.Changed += OnStateChanged;
        _marshal(() =>
        {
            _layers.AddOverlayLayer(_layer);
            Rebuild();
        });
    }

    /// <summary>Creates the controller on the Avalonia UI thread.</summary>
    /// <param name="layers">The map's layer collection.</param>
    /// <param name="state">The chart adjustment the drawn layers apply.</param>
    public AdjustedAreaBoundaryController(IMapLayerCollection layers, IWaterLevelAdjustmentState state)
        : this(layers, state, action => Dispatcher.UIThread.Post(action))
    {
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _state.Changed -= OnStateChanged;
        _marshal(() => _layers.RemoveOverlayLayer(_layer));
    }

    private void OnStateChanged(object? sender, EventArgs e) => _marshal(Rebuild);

    private void Rebuild()
    {
        if (_disposed)
            return;

        var footprint = _state.IsEnabled && _state.Current is { AppliedTime: not null } current
            ? current.Footprint
            : null;
        if (ReferenceEquals(footprint, _drawn))
            return;

        _drawn = footprint;
        var features = new List<IFeature>();
        if (footprint is { Count: > 0 })
        {
            foreach (var ring in BoundaryRings(footprint))
                AddLine(features, ring);
        }

        _layer.Features = features;
        _layer.DataHasChanged();
    }

    /// <summary>The outer edges of the merged footprint, in Web Mercator metres.</summary>
    /// <param name="footprint">The footprint areas (WGS-84).</param>
    /// <returns>Each boundary ring.</returns>
    internal static IReadOnlyList<Coordinate[]> BoundaryRings(IReadOnlyList<CoverageArea> footprint)
    {
        var factory = new GeometryFactory();
        var polygons = new List<Geometry>();
        foreach (var area in footprint)
        {
            if (Ring(factory, area.ExteriorRing) is not { } shell)
                continue;

            var holes = area.InteriorRings.Select(r => Ring(factory, r)).OfType<LinearRing>().ToArray();
            var polygon = factory.CreatePolygon(shell, holes);
            polygons.Add(polygon.IsValid ? polygon : polygon.Buffer(0));
        }

        if (polygons.Count == 0)
            return [];

        var merged = CascadedPolygonUnion.Union(polygons);
        var rings = new List<Coordinate[]>();
        for (var i = 0; i < merged.NumGeometries; i++)
        {
            if (merged.GetGeometryN(i) is not Polygon polygon)
                continue;

            rings.Add(polygon.ExteriorRing.Coordinates);
            foreach (var hole in polygon.InteriorRings)
                rings.Add(hole.Coordinates);
        }

        return rings;
    }

    private static LinearRing? Ring(GeometryFactory factory, IReadOnlyList<GeoPosition> ring)
    {
        if (ring.Count < 3)
            return null;

        var coordinates = new List<Coordinate>(ring.Count + 1);
        foreach (var p in ring)
        {
            var (x, y) = Mapsui.Projections.SphericalMercator.FromLonLat(p.Longitude, Math.Clamp(p.Latitude, -85, 85));
            coordinates.Add(new Coordinate(x, y));
        }

        if (!coordinates[0].Equals2D(coordinates[^1]))
            coordinates.Add(coordinates[0]);

        return coordinates.Count >= 4 ? factory.CreateLinearRing([.. coordinates]) : null;
    }

    private static void AddLine(List<IFeature> features, Coordinate[] ring)
    {
        if (ring.Length < 2)
            return;

        // Outline must be null: Mapsui otherwise draws its default grey
        // outline pen under a line string.
        var line = new GeometryFeature(new LineString(ring));
        line.Styles.Add(new VectorStyle
        {
            Outline = null,
            Line = new Pen { Color = CasingColor, Width = CasingWidth, PenStyle = PenStyle.Solid, PenStrokeCap = PenStrokeCap.Butt },
            Opacity = CasingOpacity,
        });
        line.Styles.Add(new VectorStyle
        {
            Outline = null,
            Line = new Pen
            {
                Color = LineColor,
                Width = LineWidth,
                PenStyle = PenStyle.UserDefined,
                DashArray = DashArray,
                PenStrokeCap = PenStrokeCap.Butt,
            },
        });
        features.Add(line);
    }
}

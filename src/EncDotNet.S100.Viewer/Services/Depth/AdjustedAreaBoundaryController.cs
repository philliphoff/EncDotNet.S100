using Avalonia.Threading;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Navigation;
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
    private const double SeamWidth = 1.25;
    private static readonly float[] SeamDashArray = [1, 3];

    private readonly IMapLayerCollection _layers;
    private readonly IWaterLevelAdjustmentState _state;
    private readonly Action<Action> _marshal;
    private readonly Func<IWaterMask?> _waterMask;
    private int _version;
    private readonly MemoryLayer _layer;
    private IReadOnlyList<CoverageArea>? _drawn;
    private bool _disposed;

    /// <summary>Creates the controller and adds its layer to the map.</summary>
    /// <param name="layers">The map's layer collection.</param>
    /// <param name="state">The chart adjustment the drawn layers apply.</param>
    /// <param name="marshal">Runs an action on the UI thread.</param>
    /// <param name="waterMask">The loaded charts' water mask the edge is clipped to (D23); null where there's none.</param>
    public AdjustedAreaBoundaryController(
        IMapLayerCollection layers,
        IWaterLevelAdjustmentState state,
        Action<Action> marshal,
        Func<IWaterMask?> waterMask)
    {
        ArgumentNullException.ThrowIfNull(layers);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(marshal);
        ArgumentNullException.ThrowIfNull(waterMask);
        _layers = layers;
        _state = state;
        _marshal = marshal;
        _waterMask = waterMask;
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
    /// <param name="waterMask">The loaded charts' water mask the edge is clipped to (D23); null where there's none.</param>
    public AdjustedAreaBoundaryController(IMapLayerCollection layers, IWaterLevelAdjustmentState state, Func<IWaterMask?> waterMask)
        : this(layers, state, action => Dispatcher.UIThread.Post(action), waterMask)
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
        var predicted = _state.Current?.PredictedFootprint ?? [];
        var version = ++_version;
        if (footprint is not { Count: > 0 })
        {
            Show([], []);
            return;
        }

        var mask = _waterMask();
        _ = Task.Run(() =>
        {
            var (edges, seams) = Lines(footprint, predicted, mask);
            _marshal(() =>
            {
                if (!_disposed && version == _version)
                    Show(edges, seams);
            });
        });
    }

    private void Show(IReadOnlyList<Coordinate[]> edges, IReadOnlyList<Coordinate[]> seams)
    {
        var features = new List<IFeature>();
        foreach (var line in edges)
            AddLine(features, line);
        foreach (var line in seams)
            AddSeam(features, line);
        _layer.Features = features;
        _layer.DataHasChanged();
    }

    /// <summary>
    /// The adjusted-area edge and the seam where S-104 hands over to
    /// predictions (tide predictions design D13), clipped to charted water
    /// with <paramref name="mask"/> (D23): segments over a loaded chart's land
    /// are left out; where no chart is loaded the lines are unclipped.
    /// </summary>
    /// <param name="footprint">Every area the water level adjusts.</param>
    /// <param name="predicted">The areas of <paramref name="footprint"/> where predictions, not S-104, adjust.</param>
    /// <param name="mask">The loaded charts' water mask; null for none.</param>
    /// <returns>The edge lines and the seam lines, in Web Mercator.</returns>
    internal static (IReadOnlyList<Coordinate[]> Edges, IReadOnlyList<Coordinate[]> Seams) Lines(
        IReadOnlyList<CoverageArea> footprint,
        IReadOnlyList<CoverageArea> predicted,
        IWaterMask? mask)
    {
        var factory = new GeometryFactory();
        var predictedSet = new HashSet<CoverageArea>(predicted, ReferenceEqualityComparer.Instance);
        if (Union(factory, footprint) is not { } all)
            return ([], []);

        Geometry edge = all.Boundary;
        Geometry? seam = null;
        if (predictedSet.Count > 0
            && Union(factory, footprint.Where(a => !predictedSet.Contains(a)).ToList()) is { } s104
            && Union(factory, predicted) is { } predictedArea)
        {
            // Where S-104's edge runs inside the predicted area, not along the outer edge.
            seam = s104.Boundary.Intersection(predictedArea).Difference(all.Boundary);
        }

        if (mask is not null && Land(factory, all.EnvelopeInternal, mask) is { } land)
        {
            edge = edge.Difference(land);
            seam = seam?.Difference(land);
        }

        return (Parts(edge), seam is null ? [] : Parts(seam));
    }

    private static Geometry? Union(GeometryFactory factory, IReadOnlyList<CoverageArea> areas)
    {
        var polygons = new List<Geometry>();
        foreach (var area in areas)
        {
            if (Ring(factory, area.ExteriorRing) is not { } shell)
                continue;

            var holes = area.InteriorRings.Select(r => Ring(factory, r)).OfType<LinearRing>().ToArray();
            var polygon = factory.CreatePolygon(shell, holes);
            polygons.Add(polygon.IsValid ? polygon : polygon.Buffer(0));
        }

        return polygons.Count == 0 ? null : CascadedPolygonUnion.Union(polygons);
    }

    // The loaded charts' land in an envelope, as merged grid cells (Web Mercator).
    private static Geometry? Land(GeometryFactory factory, Envelope envelope, IWaterMask mask)
    {
        const int maxCells = 320;
        var (west, south) = Mapsui.Projections.SphericalMercator.ToLonLat(envelope.MinX, envelope.MinY);
        var (east, north) = Mapsui.Projections.SphericalMercator.ToLonLat(envelope.MaxX, envelope.MaxY);
        var aspect = Math.Max(1e-9, envelope.Height) / Math.Max(1e-9, envelope.Width);
        var columns = aspect >= 1 ? Math.Max(2, (int)(maxCells / aspect)) : maxCells;
        var rows = aspect >= 1 ? maxCells : Math.Max(2, (int)(maxCells * aspect));
        var latStep = (north - south) / (rows - 1);
        var lonStep = (east - west) / (columns - 1);
        if (!(latStep > 0) || !(lonStep > 0))
            return null;

        var grid = new WaterMaskGrid(south, west, latStep, lonStep, rows, columns);
        var classes = new WaterMaskClass[grid.Count];
        mask.Classify(grid, classes);
        var cells = new List<Geometry>();
        for (var row = 0; row < rows; row++)
        {
            var col = 0;
            while (col < columns)
            {
                if (classes[(row * columns) + col] != WaterMaskClass.Land)
                {
                    col++;
                    continue;
                }

                var start = col;
                while (col < columns && classes[(row * columns) + col] == WaterMaskClass.Land)
                    col++;

                // Each sample stands for the cell centred on it.
                var (x0, y0) = Mapsui.Projections.SphericalMercator.FromLonLat(west + ((start - 0.5) * lonStep), south + ((row - 0.5) * latStep));
                var (x1, y1) = Mapsui.Projections.SphericalMercator.FromLonLat(west + ((col - 0.5) * lonStep), south + ((row + 0.5) * latStep));
                cells.Add(factory.ToGeometry(new Envelope(x0, x1, y0, y1)));
            }
        }

        return cells.Count == 0 ? null : CascadedPolygonUnion.Union(cells);
    }

    private static List<Coordinate[]> Parts(Geometry lines)
    {
        var parts = new List<Coordinate[]>();
        for (var i = 0; i < lines.NumGeometries; i++)
        {
            if (lines.GetGeometryN(i) is LineString line && line.NumPoints >= 2)
                parts.Add(line.Coordinates);
        }

        return parts;
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

    // The seam (D13): a thin teal dotted line where S-104 hands over to predictions.
    private static void AddSeam(List<IFeature> features, Coordinate[] line)
    {
        if (line.Length < 2)
            return;

        var feature = new GeometryFeature(new LineString(line));
        feature.Styles.Add(new VectorStyle
        {
            Outline = null,
            Line = new Pen
            {
                Color = LineColor,
                Width = SeamWidth,
                PenStyle = PenStyle.UserDefined,
                DashArray = SeamDashArray,
                PenStrokeCap = PenStrokeCap.Round,
            },
        });
        features.Add(feature);
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

using System.Globalization;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Viewer.Geodesy;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Styles;
using NetTopologySuite.Geometries;
using MapsuiColor = Mapsui.Styles.Color;

namespace EncDotNet.S100.Viewer.Tools;

/// <summary>
/// Builds the Mapsui overlay for the Range &amp; bearing tool (#1130 D15, D16,
/// D18, D22): the bearing line from the origin to the end, the VRM through the
/// range and any extra range rings, all drawn as densified lines cut at the
/// antimeridian. Rebuilt from scratch on every change, like
/// <see cref="MeasureOverlayLayer"/>.
/// </summary>
/// <remarks>
/// The VRM and rings are geodesic circles: every point is the range from the
/// origin along a great circle, whatever the bearing line's geometry. A circle
/// round a pole is drawn as an open line, never filled; only lines are drawn.
/// </remarks>
internal static class RangeBearingOverlayLayer
{
    /// <summary>Stable layer name.</summary>
    public const string LayerName = "Range & Bearing Overlay";

    /// <summary>Highest latitude projected, so circles near a pole stay finite in Mercator.</summary>
    private const double MaxMercatorLatitude = 89.5;

    private const double LineWidth = 2.5;
    private const float LineOpacity = 0.9f;
    private const double RingWidth = 1.5;
    private const float RingOpacity = 0.6f;

    /// <summary>Creates a fresh, empty overlay layer.</summary>
    public static MemoryLayer Create() => new()
    {
        Name = LayerName,
        Style = null,
        Features = new List<IFeature>(),
    };

    /// <summary>
    /// Replaces <paramref name="layer"/>'s features with a drawing of
    /// <paramref name="state"/>. Labels show distances in <paramref name="unit"/>.
    /// </summary>
    public static void Update(MemoryLayer layer, RangeBearingState state, MeasureOverlayAppearance appearance, DistanceUnit unit)
    {
        layer.Features = Build(state, appearance, unit);
        layer.DataHasChanged();
    }

    /// <summary>The overlay's features for <paramref name="state"/>; exposed for tests.</summary>
    internal static List<IFeature> Build(RangeBearingState state, MeasureOverlayAppearance appearance, DistanceUnit unit)
    {
        var accent = new MapsuiColor(appearance.Accent.R, appearance.Accent.G, appearance.Accent.B);
        var fill = Lighten(appearance.Accent);
        var (labelBg, labelFg, labelHalo) = appearance.IsDarkTheme
            ? (new MapsuiColor(38, 38, 42, 235), new MapsuiColor(245, 245, 245), new MapsuiColor(0, 0, 0, 200))
            : (new MapsuiColor(248, 248, 250, 235), new MapsuiColor(20, 20, 24), new MapsuiColor(255, 255, 255, 200));
        var features = new List<IFeature>();
        if (state.Origin is not { } origin)
            return features;

        foreach (var ring in state.RingRanges)
        {
            var circle = MarineGeodesy.GeodesicCircle(origin, ring);
            AddLine(features, circle, accent, RingWidth, RingOpacity);
            AddLabel(features, circle[0], MeasureFormat.Distance(ring, unit), labelBg, labelFg, labelHalo, offsetY: -10);
        }

        if (state.RangeMetres is { } range)
            AddLine(features, MarineGeodesy.GeodesicCircle(origin, range), accent, LineWidth, LineOpacity);

        if (state.End is { } end && state.BearingDegrees is { } bearing && state.RangeMetres is { } length)
        {
            var line = BearingLine(state.Geometry, origin, bearing, length, end);
            AddLine(features, line, accent, LineWidth, LineOpacity);
            var text = string.Create(CultureInfo.CurrentCulture,
                $"{MeasureFormat.Distance(length, unit)} · {MeasureFormat.Bearing(bearing)}");
            AddLabel(features, line[line.Count / 2], text, labelBg, labelFg, labelHalo, offsetY: -14);
            AddDisc(features, end, fill, accent);
        }

        AddDisc(features, origin, fill, accent);
        return features;
    }

    /// <summary>
    /// Points along the bearing line from <paramref name="origin"/> to
    /// <paramref name="end"/>: a densified great circle, or a rhumb line
    /// sampled as often so it can be cut at the antimeridian.
    /// </summary>
    internal static IReadOnlyList<GeoPosition> BearingLine(LegGeometry geometry, GeoPosition origin, double bearing, double metres, GeoPosition end)
    {
        if (geometry == LegGeometry.GreatCircle)
            return MarineGeodesy.DensifyGreatCircle(origin, end);

        var segments = Math.Max(1, (int)Math.Ceiling(metres / MarineGeodesy.MaxSegmentMetres));
        var points = new GeoPosition[segments + 1];
        points[0] = origin;
        for (var i = 1; i < segments; i++)
            points[i] = MarineGeodesy.Destination(LegGeometry.Rhumb, origin, bearing, metres * i / segments);
        points[segments] = end;
        return points;
    }

    private static void AddLine(List<IFeature> features, IReadOnlyList<GeoPosition> points, MapsuiColor color, double width, float opacity)
    {
        foreach (var part in MarineGeodesy.SplitAtAntimeridian(points))
        {
            if (part.Count < 2)
                continue;
            var coords = new Coordinate[part.Count];
            for (var i = 0; i < part.Count; i++)
            {
                var (x, y) = Project(part[i]);
                coords[i] = new Coordinate(x, y);
            }

            var feature = new GeometryFeature(new LineString(coords));
            feature.Styles.Add(new VectorStyle
            {
                Line = new Pen { Color = color, Width = width },
                Opacity = opacity,
            });
            features.Add(feature);
        }
    }

    private static void AddLabel(List<IFeature> features, GeoPosition at, string text, MapsuiColor background, MapsuiColor foreground, MapsuiColor halo, double offsetY)
    {
        var (x, y) = Project(at);
        var feature = new GeometryFeature(new Point(x, y));
        feature.Styles.Add(new LabelStyle
        {
            Text = text,
            Font = new Font { FontFamily = "Menlo,Consolas,Courier New,monospace", Size = 12 },
            ForeColor = foreground,
            BackColor = new Brush(background),
            Halo = new Pen { Color = halo, Width = 2.5 },
            HorizontalAlignment = LabelStyle.HorizontalAlignmentEnum.Center,
            VerticalAlignment = LabelStyle.VerticalAlignmentEnum.Center,
            Offset = new Offset(0, offsetY),
        });
        features.Add(feature);
    }

    private static void AddDisc(List<IFeature> features, GeoPosition at, MapsuiColor fill, MapsuiColor outline)
    {
        var (x, y) = Project(at);
        var feature = new GeometryFeature(new Point(x, y));
        feature.Styles.Add(new SymbolStyle
        {
            SymbolType = SymbolType.Ellipse,
            SymbolScale = 0.7,
            Fill = new Brush { Color = fill },
            Outline = new Pen { Color = outline, Width = 2.0 },
        });
        features.Add(feature);
    }

    private static (double X, double Y) Project(GeoPosition p) =>
        SphericalMercator.FromLonLat(p.Longitude, Math.Clamp(p.Latitude, -MaxMercatorLatitude, MaxMercatorLatitude));

    private static MapsuiColor Lighten((byte R, byte G, byte B) c, float mix = 0.55f)
    {
        byte M(byte v) => (byte)(v + (255 - v) * mix);
        return new MapsuiColor(M(c.R), M(c.G), M(c.B));
    }
}

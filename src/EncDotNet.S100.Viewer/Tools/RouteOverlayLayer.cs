using EncDotNet.S100.DataModel;
using System.Collections.Generic;
using System.Globalization;
using EncDotNet.S100.Viewer.Geodesy;
using EncDotNet.S100.Viewer.Routing;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Styles;
using NetTopologySuite.Geometries;
using MapsuiColor = Mapsui.Styles.Color;

namespace EncDotNet.S100.Viewer.Tools;

/// <summary>
/// Builds the persistent Mapsui <see cref="MemoryLayer"/> overlay for the
/// editable route collection. Unlike <see cref="MeasureOverlayLayer"/> (a
/// transient ruler shown only while its tool is active) this overlay
/// reflects every <see cref="Route"/> in the collection at all times; the
/// active route and the selected waypoint are emphasised.
/// </summary>
/// <remarks>
/// Like the measure overlay it is rebuilt from scratch on every change —
/// route feature counts are small (waypoints, legs, labels) so the cost is
/// negligible and the renderer stays declarative. Geodesic legs are
/// densified via <see cref="MarineGeodesy.GreatCircleIntermediatePoints"/>
/// so they read as curves, while loxodrome legs draw as straight Mercator
/// segments (matching their constant-bearing nature).
/// </remarks>
internal static class RouteOverlayLayer
{
    /// <summary>Stable layer name; reused so the host can find/remove it.</summary>
    public const string LayerName = "Route Overlay";

    /// <summary>Number of straight segments used to approximate a geodesic leg.</summary>
    private const int GeodesicSegments = 24;

    private const float ActiveLineOpacity = 0.85f;
    private const float InactiveLineOpacity = 0.4f;

    private static MapsuiColor Lighten((byte R, byte G, byte B) c, float mix = 0.55f)
    {
        byte M(byte v) => (byte)(v + (255 - v) * mix);
        return new MapsuiColor(M(c.R), M(c.G), M(c.B));
    }

    private static MapsuiColor Desaturate((byte R, byte G, byte B) c)
    {
        // Pull toward mid-grey so inactive routes recede behind the active one.
        var gray = (byte)((c.R + c.G + c.B) / 3);
        byte M(byte v) => (byte)((v + gray) / 2);
        return new MapsuiColor(M(c.R), M(c.G), M(c.B));
    }

    /// <summary>Creates a fresh, empty overlay layer.</summary>
    public static MemoryLayer Create() => new()
    {
        Name = LayerName,
        Style = null,
        Features = new List<IFeature>(),
    };

    /// <summary>
    /// Replaces <paramref name="layer"/>'s features with a freshly built
    /// representation of <paramref name="routes"/>, emphasising the active
    /// route and the waypoint at <paramref name="selectedWaypointIndex"/>
    /// (within the active route). Caller invalidates the map afterwards.
    /// </summary>
    public static void Update(
        MemoryLayer layer,
        RouteCollection routes,
        int? selectedWaypointIndex,
        MeasureOverlayAppearance appearance)
    {
        var (labelBg, labelFg, labelHalo) = appearance.IsDarkTheme
            ? (new MapsuiColor(38, 38, 42, 235), new MapsuiColor(245, 245, 245), new MapsuiColor(0, 0, 0, 200))
            : (new MapsuiColor(248, 248, 250, 235), new MapsuiColor(20, 20, 24), new MapsuiColor(255, 255, 255, 200));

        var features = new List<IFeature>();

        foreach (var route in routes.Routes)
        {
            var isActive = ReferenceEquals(route, routes.ActiveRoute);
            var accent = appearance.Accent;
            var lineColor = isActive
                ? new MapsuiColor(accent.R, accent.G, accent.B)
                : Desaturate(accent);
            var fillColor = isActive ? Lighten(accent) : Desaturate(accent);

            var densified = DensifyRoute(route);
            if (densified.Count >= 2)
                AddPolyline(features, densified, lineColor, isActive ? ActiveLineOpacity : InactiveLineOpacity);

            // Per-leg distance/bearing labels only on the active route to
            // keep the chart legible when several routes overlap.
            if (isActive)
            {
                var metrics = route.ComputeAllLegMetrics();
                for (var i = 0; i < metrics.Count; i++)
                {
                    var a = route.Waypoints[i].Position;
                    var b = route.Waypoints[i + 1].Position;
                    AddLegLabel(features, a, b, metrics[i], labelBg, labelFg, labelHalo);
                }
            }

            for (var i = 0; i < route.Waypoints.Count; i++)
            {
                var pos = route.Waypoints[i].Position;
                var selected = isActive && selectedWaypointIndex == i;
                AddWaypointMarker(features, pos.Latitude, pos.Longitude, i + 1, fillColor, lineColor, selected);
            }
        }

        layer.Features = features;
        layer.DataHasChanged();
    }

    /// <summary>
    /// Flattens a route into a single ordered point list, densifying
    /// geodesic legs into great-circle arcs and leaving loxodrome legs as
    /// straight segments.
    /// </summary>
    private static List<GeoPosition> DensifyRoute(Route route)
    {
        var points = new List<GeoPosition>();
        if (route.Waypoints.Count == 0)
            return points;

        var first = route.Waypoints[0].Position;
        points.Add(new GeoPosition(first.Latitude, first.Longitude));

        for (var i = 0; i < route.Legs.Count; i++)
        {
            var a = route.Waypoints[i].Position;
            var b = route.Waypoints[i + 1].Position;
            if (route.Legs[i].GeometryType == RouteLegGeometryType.Geodesic)
            {
                var arc = MarineGeodesy.GreatCircleIntermediatePoints(
                    a.Latitude, a.Longitude, b.Latitude, b.Longitude, GeodesicSegments);
                // Skip the first arc point (== current end) to avoid duplicates.
                for (var j = 1; j < arc.Count; j++)
                    points.Add(arc[j]);
            }
            else
            {
                points.Add(new GeoPosition(b.Latitude, b.Longitude));
            }
        }

        return points;
    }

    private static void AddPolyline(List<IFeature> features, IReadOnlyList<GeoPosition> points, MapsuiColor strokeColor, float opacity)
    {
        foreach (var subPath in MarineGeodesy.SplitAtAntimeridian(points))
        {
            if (subPath.Count < 2) continue;
            var coords = new Coordinate[subPath.Count];
            for (var i = 0; i < subPath.Count; i++)
            {
                var (mx, my) = SphericalMercator.FromLonLat(subPath[i].Longitude, subPath[i].Latitude);
                coords[i] = new Coordinate(mx, my);
            }

            var feature = new GeometryFeature(new LineString(coords));
            feature.Styles.Add(new VectorStyle
            {
                Line = new Pen { Color = strokeColor, Width = 4.0 },
                Opacity = opacity,
            });
            features.Add(feature);
        }
    }

    private static void AddLegLabel(
        List<IFeature> features,
        EncDotNet.S100.DataModel.GeoPosition a,
        EncDotNet.S100.DataModel.GeoPosition b,
        RouteLegMetrics metrics,
        MapsuiColor backgroundColor,
        MapsuiColor foregroundColor,
        MapsuiColor haloColor)
    {
        var (ax, ay) = SphericalMercator.FromLonLat(a.Longitude, a.Latitude);
        var (bx, by) = SphericalMercator.FromLonLat(b.Longitude, b.Latitude);
        var feature = new GeometryFeature(new Point((ax + bx) / 2.0, (ay + by) / 2.0));

        var text = string.Format(
            CultureInfo.CurrentCulture,
            EncDotNet.S100.Viewer.Resources.Strings.Route_LegLabel,
            metrics.DistanceNm,
            metrics.InitialBearingDegrees);

        feature.Styles.Add(new LabelStyle
        {
            Text = text,
            Font = new Font { FontFamily = "Menlo,Consolas,Courier New,monospace", Size = 12 },
            ForeColor = foregroundColor,
            BackColor = new Brush(backgroundColor),
            Halo = new Pen { Color = haloColor, Width = 2.5 },
            HorizontalAlignment = LabelStyle.HorizontalAlignmentEnum.Center,
            VerticalAlignment = LabelStyle.VerticalAlignmentEnum.Center,
            Offset = new Offset(0, -14),
        });
        features.Add(feature);
    }

    private static void AddWaypointMarker(List<IFeature> features, double lat, double lon, int index, MapsuiColor fillColor, MapsuiColor borderColor, bool selected)
    {
        var (mx, my) = SphericalMercator.FromLonLat(lon, lat);
        var disc = new GeometryFeature(new Point(mx, my));
        disc.Styles.Add(new SymbolStyle
        {
            SymbolType = SymbolType.Ellipse,
            // Selected waypoint is drawn larger with a heavier outline.
            SymbolScale = selected ? 1.25 : 0.9,
            Fill = new Brush { Color = fillColor },
            Outline = new Pen { Color = borderColor, Width = selected ? 3.0 : 2.0 },
        });
        features.Add(disc);

        var labelFeature = new GeometryFeature(new Point(mx, my));
        labelFeature.Styles.Add(new LabelStyle
        {
            Text = index.ToString(CultureInfo.InvariantCulture),
            Font = new Font { FontFamily = "Menlo,Consolas,Courier New,monospace", Size = 11, Bold = true },
            ForeColor = new MapsuiColor(0, 0, 0),
            BackColor = new Brush(MapsuiColor.Transparent),
            HorizontalAlignment = LabelStyle.HorizontalAlignmentEnum.Center,
            VerticalAlignment = LabelStyle.VerticalAlignmentEnum.Center,
        });
        features.Add(labelFeature);
    }
}

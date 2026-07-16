using EncDotNet.S100.DataModel;
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
/// Read-only geometry of the feature a pick currently refers to, expressed
/// in WGS-84 lat/lon. Mirrors the primitive shape exposed by
/// <see cref="EncDotNet.S100.Features.IS100Feature"/> so the controller can
/// project a picked feature into the overlay without leaking the feature
/// abstraction into the renderer.
/// </summary>
/// <param name="ExteriorRing">Surface exterior ring (empty when not an area).</param>
/// <param name="InteriorRings">Surface interior rings / holes.</param>
/// <param name="Curves">Curve coordinate sequences.</param>
/// <param name="Points">Point coordinates.</param>
internal readonly record struct PickHighlightGeometry(
    IReadOnlyList<GeoPosition> ExteriorRing,
    IReadOnlyList<IReadOnlyList<GeoPosition>> InteriorRings,
    IReadOnlyList<IReadOnlyList<GeoPosition>> Curves,
    IReadOnlyList<GeoPosition> Points)
{
    /// <summary>True when the geometry carries at least one drawable vertex.</summary>
    public bool HasGeometry =>
        ExteriorRing.Count > 0 || InteriorRings.Count > 0 || Curves.Count > 0 || Points.Count > 0;
}

/// <summary>
/// Snapshot of what the pick-highlight overlay should draw: the click
/// position (the "where") plus, when resolvable, the geometry of the
/// selected feature (the "what"). Either part may be absent.
/// </summary>
/// <param name="Location">
/// Geographic position of the pick, or <c>null</c> when the current pick
/// carries no location (e.g. a programmatic feature open).
/// </param>
/// <param name="Geometry">
/// Geometry of the selected feature, or <c>null</c> when no feature is
/// selected or its geometry could not be resolved (e.g. coverage picks or
/// container features without geometry).
/// </param>
internal readonly record struct PickHighlightState(
    GeoPosition? Location,
    PickHighlightGeometry? Geometry)
{
    /// <summary>True when there is nothing to draw.</summary>
    public bool IsEmpty => Location is null && (Geometry is null || !Geometry.Value.HasGeometry);
}

/// <summary>
/// Read-only appearance bundle for the pick-highlight overlay.
/// </summary>
/// <param name="Accent">Primary accent colour as RGB bytes.</param>
/// <param name="IsDarkBasemap">
/// True when the active chart palette is dark (Dusk or Night). This drives
/// the marker's casing colour, which must be dimmed against a dark basemap
/// to avoid glare — it is independent of the application's chrome theme.
/// </param>
internal readonly record struct PickHighlightAppearance(
    (byte R, byte G, byte B) Accent,
    bool IsDarkBasemap)
{
    /// <summary>Default appearance — application accent placeholder, light (Day) basemap.</summary>
    public static PickHighlightAppearance Default { get; } =
        new(PickHighlightOverlayLayer.DefaultAccent, IsDarkBasemap: false);
}

/// <summary>
/// Builds the Mapsui <see cref="MemoryLayer"/> overlay that highlights the
/// current pick. Two visual elements are drawn:
/// <list type="bullet">
/// <item>an <em>object outline</em> tracing the selected feature's geometry
/// (area outline + faint fill, curve stroke, or point ring) so the pick
/// stays anchored to the feature as the user pans; and</item>
/// <item>a <em>position marker</em> (a screen-constant accent ring with a
/// centre dot) at the click location — mirroring a typical ECDIS cursor
/// pick echo.</item>
/// </list>
/// Re-built from scratch on every pick change; the feature count is tiny so
/// the cost is negligible and the code stays declarative (mirrors
/// <see cref="MeasureOverlayLayer"/>).
/// </summary>
internal static class PickHighlightOverlayLayer
{
    /// <summary>Stable layer name; reused so the host can find/remove it.</summary>
    public const string LayerName = "Pick Highlight";

    /// <summary>
    /// Default accent (matches <c>ViewerSettings.AccentColor</c> default of
    /// <c>#007ACC</c>). Used when no accent has been pushed to the overlay yet.
    /// </summary>
    public static readonly (byte R, byte G, byte B) DefaultAccent = (0x00, 0x7A, 0xCC);

    // Object-outline strokes are geometry-space (scale with zoom). The faint
    // area fill keeps the interior readable without obscuring the chart.
    private const double OutlineWidth = 3.0;
    private const float OutlineOpacity = 0.9f;
    // Selected-area shading: accent colour at 15% (per UX spec).
    private const float AreaFillOpacity = 0.15f;

    // The position marker is screen-space (constant pixel size at any zoom) so
    // it always reads as a cursor echo rather than a feature. It is a thin
    // accent ring cased in white (no centre dot, so the selected feature stays
    // visible through the ring).
    private const double MarkerRingScale = 1.4;
    private const double MarkerCasingWidth = 5.0;
    private const double MarkerRingWidth = 2.0;

    /// <summary>Creates a fresh, empty overlay layer.</summary>
    public static MemoryLayer Create() => new()
    {
        Name = LayerName,
        Style = null,
        Features = new List<IFeature>(),
    };

    /// <summary>
    /// Replaces <paramref name="layer"/>'s features with a freshly built
    /// representation of <paramref name="state"/>, drawn using the supplied
    /// <paramref name="appearance"/>. An empty state clears the overlay.
    /// </summary>
    public static void Update(MemoryLayer layer, PickHighlightState state, PickHighlightAppearance appearance)
    {
        var features = new List<IFeature>();

        var accent = appearance.Accent;
        var accentColor = new MapsuiColor(accent.R, accent.G, accent.B);
        // The accent ring is cased in white so it reads against any basemap.
        // Dusk/Night palettes dim the casing to a mid grey so the marker
        // doesn't glare on a dark chart — a bright-white ring harms night
        // vision, which the dark palettes exist to protect.
        var casingColor = appearance.IsDarkBasemap
            ? new MapsuiColor(150, 150, 150, 235)
            : new MapsuiColor(255, 255, 255, 255);

        // 1) Object outline (drawn first so the marker sits on top).
        if (state.Geometry is { } geometry && geometry.HasGeometry)
        {
            AddObjectOutline(features, geometry, accentColor);
        }

        // 2) Position marker (cursor echo) at the click location.
        if (state.Location is { } loc)
        {
            AddPositionMarker(features, loc.Latitude, loc.Longitude, accentColor, casingColor);
        }

        layer.Features = features;
        layer.DataHasChanged();
    }

    private static void AddObjectOutline(
        List<IFeature> features,
        PickHighlightGeometry geometry,
        MapsuiColor accentColor)
    {
        // Area: faint fill under the exterior ring, then accent outlines for
        // the exterior ring and any interior holes.
        if (geometry.ExteriorRing.Count >= 3)
        {
            AddAreaFill(features, geometry.ExteriorRing, geometry.InteriorRings, accentColor);
            AddRingOutline(features, geometry.ExteriorRing, accentColor);
            foreach (var hole in geometry.InteriorRings)
            {
                AddRingOutline(features, hole, accentColor);
            }
        }

        // Curves: stroke each curve in the accent colour.
        foreach (var curve in geometry.Curves)
        {
            AddPolyline(features, curve, accentColor);
        }

        // Points: a geometry-space ring around each point so the highlight
        // hugs the feature even if the click landed slightly off it.
        foreach (var (lat, lon) in geometry.Points)
        {
            AddPointRing(features, lat, lon, accentColor);
        }
    }

    private static void AddAreaFill(
        List<IFeature> features,
        IReadOnlyList<GeoPosition> exterior,
        IReadOnlyList<IReadOnlyList<GeoPosition>> holes,
        MapsuiColor accentColor)
    {
        // A filled polygon does not split sensibly at the antimeridian, so the
        // faint fill is skipped for shapes that wrap it; the outline (which
        // does split) still conveys the shape. This is a rare edge case for a
        // single picked feature.
        var shell = ToLinearRing(exterior);
        if (shell is null) return;

        var interior = new List<LinearRing>();
        foreach (var hole in holes)
        {
            if (ToLinearRing(hole) is { } ring) interior.Add(ring);
        }

        var polygon = new Polygon(shell, interior.ToArray());
        var feature = new GeometryFeature(polygon);
        feature.Styles.Add(new VectorStyle
        {
            Fill = new Brush { Color = accentColor },
            Outline = null,
            Line = null,
            Opacity = AreaFillOpacity,
        });
        features.Add(feature);
    }

    private static LinearRing? ToLinearRing(IReadOnlyList<GeoPosition> ring)
    {
        if (ring.Count < 3) return null;

        var closedCount = ring.Count;
        var first = ring[0];
        var last = ring[ring.Count - 1];
        var alreadyClosed = first.Latitude == last.Latitude && first.Longitude == last.Longitude;
        var coords = new Coordinate[alreadyClosed ? closedCount : closedCount + 1];
        for (var i = 0; i < closedCount; i++)
        {
            var (mx, my) = SphericalMercator.FromLonLat(ring[i].Longitude, ring[i].Latitude);
            coords[i] = new Coordinate(mx, my);
        }
        if (!alreadyClosed)
        {
            coords[closedCount] = coords[0].Copy();
        }
        return new LinearRing(coords);
    }

    private static void AddRingOutline(
        List<IFeature> features,
        IReadOnlyList<GeoPosition> ring,
        MapsuiColor accentColor)
    {
        // Close the ring for outlining (a ring's last vertex may or may not
        // repeat the first).
        var points = new List<GeoPosition>(ring);
        if (points.Count >= 2 &&
            (points[0].Latitude != points[^1].Latitude || points[0].Longitude != points[^1].Longitude))
        {
            points.Add(points[0]);
        }
        AddPolyline(features, points, accentColor);
    }

    private static void AddPolyline(
        List<IFeature> features,
        IReadOnlyList<GeoPosition> points,
        MapsuiColor accentColor)
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
                Line = new Pen { Color = accentColor, Width = OutlineWidth },
                Opacity = OutlineOpacity,
            });
            features.Add(feature);
        }
    }

    private static void AddPointRing(
        List<IFeature> features,
        double lat,
        double lon,
        MapsuiColor accentColor)
    {
        var (mx, my) = SphericalMercator.FromLonLat(lon, lat);
        var feature = new GeometryFeature(new Point(mx, my));
        feature.Styles.Add(new SymbolStyle
        {
            SymbolType = SymbolType.Ellipse,
            SymbolScale = MarkerRingScale,
            Fill = null,
            Outline = new Pen { Color = accentColor, Width = OutlineWidth },
        });
        features.Add(feature);
    }

    private static void AddPositionMarker(
        List<IFeature> features,
        double lat,
        double lon,
        MapsuiColor accentColor,
        MapsuiColor casingColor)
    {
        var (mx, my) = SphericalMercator.FromLonLat(lon, lat);
        var point = new Point(mx, my);

        // White casing (wider stroke, drawn first) gives the accent ring crisp
        // light edges against any basemap.
        var casing = new GeometryFeature(point.Copy());
        casing.Styles.Add(new SymbolStyle
        {
            SymbolType = SymbolType.Ellipse,
            SymbolScale = MarkerRingScale,
            Fill = null,
            Outline = new Pen { Color = casingColor, Width = MarkerCasingWidth },
        });
        features.Add(casing);

        // Thin accent ring on top. No centre fill/dot, so the selected feature
        // remains visible through the ring.
        var ring = new GeometryFeature(point.Copy());
        ring.Styles.Add(new SymbolStyle
        {
            SymbolType = SymbolType.Ellipse,
            SymbolScale = MarkerRingScale,
            Fill = null,
            Outline = new Pen { Color = accentColor, Width = MarkerRingWidth },
        });
        features.Add(ring);
    }
}

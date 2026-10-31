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
/// Builds the Mapsui <see cref="MemoryLayer"/> overlay for the Measure
/// Mode tool. Re-built from scratch on every state change — the feature
/// count is small (a handful of waypoints + labels) so cost is
/// negligible and the code stays declarative.
/// </summary>
internal static class MeasureOverlayLayer
{
    /// <summary>Stable layer name; reused so the host can find/remove it.</summary>
    public const string LayerName = "Measure Overlay";

    /// <summary>
    /// Default accent (matches <c>ViewerSettings.AccentColor</c> default
    /// of <c>#007ACC</c>). Used when no accent has been pushed to the
    /// overlay yet.
    /// </summary>
    public static readonly (byte R, byte G, byte B) DefaultAccent = (0x00, 0x7A, 0xCC);

    // Line is a single stroke (no border casing) drawn in the accent
    // colour, so the path reads as one continuous shape. Transparency is
    // applied via Style.Opacity (NOT pen colour alpha — Mapsui 5.0.2
    // pre-multiplies the alpha into the RGB and discards it for
    // blending, which produces a darker fully-opaque stroke).
    // Style.Opacity works correctly on a single VectorStyle; stacking
    // two translucent styles composites toward opaque, so we
    // intentionally avoid a separate border casing on the line.
    private const float LineOpacity = 0.5f;

    /// <summary>
    /// Lightens an accent toward white so the waypoint disc fill stays
    /// visually distinct from the (full-strength accent) outline. Mix
    /// factor 0.55 keeps enough hue to read as the same family.
    /// </summary>
    private static MapsuiColor Lighten((byte R, byte G, byte B) c, float mix = 0.55f)
    {
        byte M(byte v) => (byte)(v + (255 - v) * mix);
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
    /// representation of <paramref name="state"/>, drawn using the
    /// supplied <paramref name="appearance"/>. Caller is responsible for
    /// invalidating the map (e.g. via
    /// <see cref="MapToolContext.RefreshGraphics"/>).
    /// </summary>
    public static void Update(MemoryLayer layer, MeasurePathState state, MeasureOverlayAppearance appearance, DistanceUnit unit = DistanceUnit.NauticalMiles)
    {
        var accent = appearance.Accent;
        var borderColor = new MapsuiColor(accent.R, accent.G, accent.B);
        var fillColor = Lighten(accent);
        var (labelBg, labelFg, labelHalo) = appearance.IsDarkTheme
            // Dark theme: chip surface + light text, dark halo for outline.
            ? (new MapsuiColor(38, 38, 42, 235), new MapsuiColor(245, 245, 245), new MapsuiColor(0, 0, 0, 200))
            // Light theme: light surface + dark text, light halo so the
            // text stays legible over coloured basemap features.
            : (new MapsuiColor(248, 248, 250, 235), new MapsuiColor(20, 20, 24), new MapsuiColor(255, 255, 255, 200));
        var features = new List<IFeature>();
        var legs = state.ComputeLegs();

        // Solid polyline through the placed legs, then the dashed rubber-band
        // tail. Great-circle legs are densified so they draw as curves (D2,
        // D18); each line is split at the antimeridian so e.g.
        // Tokyo→Honolulu doesn't smear across the whole world.
        var solidPoints = new List<GeoPosition>();
        foreach (var leg in legs)
        {
            if (leg.IsRubberBand)
            {
                AddPolyline(features, LegPath(leg), dashed: true, borderColor);
                continue;
            }

            var path = LegPath(leg);
            solidPoints.AddRange(solidPoints.Count == 0 ? path : path.Skip(1));
        }

        if (solidPoints.Count >= 2)
            AddPolyline(features, solidPoints, dashed: false, borderColor);

        // Per-segment label at the leg's midpoint; the card's leg is bold
        // when the path has more than one (D9).
        var current = state.CurrentLeg?.Index;
        foreach (var leg in legs)
        {
            var selected = legs.Count > 1 && leg.Index == current;
            AddLegLabel(features, leg, LegLabelText(leg, unit), selected, labelBg, labelFg, labelHalo);
        }

        // Waypoint markers with index numbers.
        for (int i = 0; i < state.Waypoints.Count; i++)
        {
            var (lat, lon) = state.Waypoints[i];
            AddWaypointMarker(features, lat, lon, i + 1, fillColor, borderColor);
        }

        // Unnumbered preview marker at the rubber-band cursor so the user
        // always sees a circle showing where the next click will land.
        if (state.Phase == MeasurePathState.MeasurePhase.Drawing && state.RubberBand is { } rbMarker)
        {
            AddWaypointDisc(features, rbMarker.Latitude, rbMarker.Longitude, fillColor, borderColor);
        }

        layer.Features = features;
        layer.DataHasChanged();
    }

    /// <summary>
    /// The map label of a leg in the primary unit only (D5, D6):
    /// <c>17.90 NM · 295.7°</c>.
    /// </summary>
    public static string LegLabelText(MeasureLeg leg, DistanceUnit unit) => string.Format(
        CultureInfo.CurrentCulture,
        EncDotNet.S100.Viewer.Resources.Strings.Status_MeasureLegLabel,
        MeasureFormat.Distance(leg.DistanceMetres, unit),
        MeasureFormat.Bearing(leg.BearingDeg));

    /// <summary>
    /// Where a leg's label sits: the middle of the great circle, or the
    /// Mercator midpoint of a rhumb line (visually centred on the drawn
    /// segment, taking the short way across the antimeridian).
    /// </summary>
    public static GeoPosition LabelAnchor(MeasureLeg leg)
    {
        if (leg.Geometry == LegGeometry.GreatCircle)
        {
            return MarineGeodesy.GreatCircleIntermediatePoints(
                leg.From.Latitude, leg.From.Longitude, leg.To.Latitude, leg.To.Longitude, 2)[1];
        }

        var toLon = leg.From.Longitude + NormalizeLongitude(leg.To.Longitude - leg.From.Longitude);
        var (ax, ay) = SphericalMercator.FromLonLat(leg.From.Longitude, leg.From.Latitude);
        var (bx, by) = SphericalMercator.FromLonLat(toLon, leg.To.Latitude);
        var (lon, lat) = SphericalMercator.ToLonLat((ax + bx) / 2.0, (ay + by) / 2.0);
        return new GeoPosition(lat, NormalizeLongitude(lon));
    }

    /// <summary>
    /// The points a leg is drawn through: its ends for a rhumb line (straight
    /// in Mercator), or its densified great circle.
    /// </summary>
    internal static IReadOnlyList<GeoPosition> LegPath(MeasureLeg leg) =>
        leg.Geometry == LegGeometry.GreatCircle
            ? MarineGeodesy.DensifyGreatCircle(leg.From, leg.To)
            : [leg.From, leg.To];

    private static double NormalizeLongitude(double lon) => ((lon + 540.0) % 360.0) - 180.0;

    private static void AddPolyline(List<IFeature> features, IReadOnlyList<GeoPosition> points, bool dashed, MapsuiColor strokeColor)
    {
        foreach (var subPath in MarineGeodesy.SplitAtAntimeridian(points))
        {
            if (subPath.Count < 2) continue;
            var coords = new Coordinate[subPath.Count];
            for (int i = 0; i < subPath.Count; i++)
            {
                var (mx, my) = SphericalMercator.FromLonLat(subPath[i].Longitude, subPath[i].Latitude);
                coords[i] = new Coordinate(mx, my);
            }
            var line = new LineString(coords);

            var feature = new GeometryFeature(line);
            var pen = new Pen { Color = strokeColor, Width = 5.0 };
            if (dashed) pen.PenStyle = PenStyle.Dash;
            feature.Styles.Add(new VectorStyle
            {
                Line = pen,
                Opacity = LineOpacity,
            });
            features.Add(feature);
        }
    }

    private static void AddLegLabel(List<IFeature> features, MeasureLeg leg, string text, bool selected, MapsuiColor backgroundColor, MapsuiColor foregroundColor, MapsuiColor haloColor)
    {
        var anchor = LabelAnchor(leg);
        var (mx, my) = SphericalMercator.FromLonLat(anchor.Longitude, anchor.Latitude);
        var feature = new GeometryFeature(new Point(mx, my));

        feature.Styles.Add(new LabelStyle
        {
            Text = text,
            Font = new Font { FontFamily = "Menlo,Consolas,Courier New,monospace", Size = LabelFontSize, Bold = selected },
            ForeColor = foregroundColor,
            BackColor = new Brush(backgroundColor),
            Halo = new Pen { Color = haloColor, Width = 2.5 },
            HorizontalAlignment = LabelStyle.HorizontalAlignmentEnum.Center,
            VerticalAlignment = LabelStyle.VerticalAlignmentEnum.Center,
            Offset = new Offset(0, LabelOffsetY),
        });
        features.Add(feature);
    }

    /// <summary>Leg label font size, in DIPs.</summary>
    internal const double LabelFontSize = 12;

    /// <summary>Leg label offset from its anchor, in DIPs (negative is up).</summary>
    internal const double LabelOffsetY = -14;

    /// <summary>
    /// Adds a numbered waypoint marker (filled disc with index label).
    /// </summary>
    private static void AddWaypointMarker(List<IFeature> features, double lat, double lon, int index, MapsuiColor fillColor, MapsuiColor borderColor)
    {
        AddWaypointDisc(features, lat, lon, fillColor, borderColor);

        var (mx, my) = SphericalMercator.FromLonLat(lon, lat);
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

    private static void AddWaypointDisc(List<IFeature> features, double lat, double lon, MapsuiColor fillColor, MapsuiColor borderColor)
    {
        var (mx, my) = SphericalMercator.FromLonLat(lon, lat);
        var feature = new GeometryFeature(new Point(mx, my));

        feature.Styles.Add(new SymbolStyle
        {
            SymbolType = SymbolType.Ellipse,
            SymbolScale = 0.9,
            Fill = new Brush { Color = fillColor },
            Outline = new Pen { Color = borderColor, Width = 2.0 },
        });

        features.Add(feature);
    }
}

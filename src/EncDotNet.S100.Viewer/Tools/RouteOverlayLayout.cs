using Avalonia;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Viewer.Geodesy;
using Mapsui.Projections;

namespace EncDotNet.S100.Viewer.Tools;

/// <summary>
/// Lays out a route's legs on screen for the current viewport: each leg's
/// screen path and length, its midpoint (where the insert handle sits), and
/// where its label goes. Pure, so the renderer and the edit tool's hit-testing
/// share one answer and tests can pin the rules.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>An insert handle shows, while editing, on legs longer than
/// <see cref="InsertHandleMinLegPx"/> on screen.</item>
/// <item>A label runs along its leg and is turned so it is never upside down
/// (its angle is in (-90°, 90°]). It sits <see cref="LabelOffsetPx"/> to the
/// up-side of the line, and beside the insert handle when there is one.</item>
/// <item>A label is dropped when its leg is shorter on screen than the label
/// plus clearance, except on the selected leg, whose label always shows.</item>
/// <item>A leg that crosses the antimeridian gets no handle or label.</item>
/// </list>
/// </remarks>
internal static class RouteOverlayLayout
{
    /// <summary>Legs at least this long on screen get an insert handle while editing.</summary>
    public const double InsertHandleMinLegPx = 40.0;

    /// <summary>The insert handle's radius.</summary>
    public const double InsertHandleRadiusPx = 5.5;

    /// <summary>How far a label's centre sits from its line, to the label's up side: clear of the 9 px casing.</summary>
    public const double LabelOffsetPx = 13.0;

    /// <summary>How far along the line a label moves to clear an insert handle.</summary>
    public const double LabelHandleClearancePx = 30.0;

    /// <summary>Clearance added to a label's width when deciding whether its leg is long enough.</summary>
    public const double LabelClearancePx = 20.0;

    /// <summary>
    /// Lays out every leg of <paramref name="route"/>.
    /// </summary>
    /// <param name="route">The route.</param>
    /// <param name="toScreen">Projects a position to screen coordinates, or
    /// returns <c>null</c> when it can't.</param>
    /// <param name="measureLabel">The on-screen width of a label.</param>
    /// <param name="selectedLeg">The selected leg, whose label always shows.</param>
    /// <param name="isEditing">Whether insert handles show.</param>
    public static IReadOnlyList<LegLayout> Layout(
        RouteOverlayScene.SceneRoute route,
        Func<GeoPosition, Point?> toScreen,
        Func<string, double> measureLabel,
        int? selectedLeg,
        bool isEditing)
    {
        var result = new LegLayout[route.Legs.Count];
        for (var i = 0; i < result.Length; i++)
        {
            var leg = route.Legs[i];
            result[i] = LayoutLeg(i, leg.Path, leg.Label, toScreen, measureLabel, selectedLeg == i, isEditing);
        }
        return result;
    }

    /// <summary>Lays out one leg.</summary>
    internal static LegLayout LayoutLeg(
        int index,
        IReadOnlyList<GeoPosition> path,
        string label,
        Func<GeoPosition, Point?> toScreen,
        Func<string, double> measureLabel,
        bool isSelected,
        bool isEditing)
    {
        var parts = new List<IReadOnlyList<Point>>();
        var geoParts = MarineGeodesy.SplitAtAntimeridian(path);
        foreach (var geoPart in geoParts)
        {
            var screen = new List<Point>(geoPart.Count);
            foreach (var p in geoPart)
            {
                if (toScreen(p) is { } s)
                    screen.Add(s);
            }
            if (screen.Count >= 2)
                parts.Add(screen);
        }

        var length = parts.Sum(PathLength);
        var continuous = geoParts.Count == 1 && parts.Count == 1 && parts[0].Count == path.Count;
        if (!continuous)
            return new LegLayout(index, parts, length, null, null, false, null);

        var (midpoint, angle, segment, t) = PointAtHalfLength(parts[0]);
        var midpointPosition = InterpolateMercator(path[segment], path[segment + 1], t);
        var showHandle = isEditing && length > InsertHandleMinLegPx;
        var placement = PlaceLabel(midpoint, angle, length, measureLabel(label), isSelected, showHandle);
        return new LegLayout(index, parts, length, midpoint, midpointPosition, showHandle, placement);
    }

    /// <summary>
    /// Places a label for a leg whose screen midpoint is <paramref name="midpoint"/>
    /// and whose direction there is <paramref name="angleDegrees"/>, or returns
    /// <c>null</c> when the leg is too short to show it.
    /// </summary>
    internal static LabelPlacement? PlaceLabel(
        Point midpoint, double angleDegrees, double legLengthPx, double labelWidthPx, bool isSelected, bool besideHandle)
    {
        var shift = besideHandle && !isSelected ? LabelHandleClearancePx : 0.0;
        if (!isSelected && legLengthPx < labelWidthPx + LabelClearancePx + 2 * shift)
            return null;

        var upright = Upright(angleDegrees);
        var radians = upright * Math.PI / 180.0;
        var (cos, sin) = (Math.Cos(radians), Math.Sin(radians));
        // Along the line by the shift, then "up" in the label's own frame
        // (local (0, -offset) rotated by the label angle).
        var center = new Point(
            midpoint.X + cos * shift + sin * LabelOffsetPx,
            midpoint.Y + sin * shift - cos * LabelOffsetPx);
        return new LabelPlacement(center, upright, labelWidthPx);
    }

    /// <summary>Turns a line direction into a text angle in (-90°, 90°], so text reads left to right.</summary>
    internal static double Upright(double angleDegrees)
    {
        var a = angleDegrees % 360.0;
        if (a > 180.0) a -= 360.0;
        if (a <= -180.0) a += 360.0;
        if (a > 90.0) a -= 180.0;
        else if (a <= -90.0) a += 180.0;
        return a;
    }

    private static double PathLength(IReadOnlyList<Point> points)
    {
        double total = 0;
        for (var i = 1; i < points.Count; i++)
            total += Distance(points[i - 1], points[i]);
        return total;
    }

    private static (Point Point, double AngleDegrees, int Segment, double T) PointAtHalfLength(IReadOnlyList<Point> points)
    {
        var half = PathLength(points) / 2.0;
        double walked = 0;
        for (var i = 1; i < points.Count; i++)
        {
            var a = points[i - 1];
            var b = points[i];
            var step = Distance(a, b);
            if (walked + step >= half || i == points.Count - 1)
            {
                var t = step < 1e-9 ? 0.0 : Math.Clamp((half - walked) / step, 0.0, 1.0);
                var angle = Math.Atan2(b.Y - a.Y, b.X - a.X) * 180.0 / Math.PI;
                return (new Point(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t), angle, i - 1, t);
            }
            walked += step;
        }
        return (points[0], 0.0, 0, 0.0);
    }

    private static GeoPosition InterpolateMercator(GeoPosition a, GeoPosition b, double t)
    {
        // Screen space is linear in Mercator, so interpolating there lands the
        // position exactly under the handle, for rhumb and great-circle legs alike.
        var (ax, ay) = SphericalMercator.FromLonLat(a.Longitude, a.Latitude);
        var (bx, by) = SphericalMercator.FromLonLat(b.Longitude, b.Latitude);
        var (lon, lat) = SphericalMercator.ToLonLat(ax + (bx - ax) * t, ay + (by - ay) * t);
        return new GeoPosition(lat, lon);
    }

    private static double Distance(Point a, Point b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}

/// <summary>One leg laid out on screen.</summary>
/// <param name="Index">The leg index.</param>
/// <param name="Parts">The leg's screen path, split where it crosses the antimeridian.</param>
/// <param name="LengthPx">The leg's length on screen.</param>
/// <param name="Midpoint">The screen point halfway along the leg, or <c>null</c> for a leg that crosses the antimeridian.</param>
/// <param name="MidpointPosition">The position under <see cref="Midpoint"/>: where an insert handle inserts.</param>
/// <param name="ShowInsertHandle">Whether the leg shows an insert handle.</param>
/// <param name="Label">Where the leg's label goes, or <c>null</c> when it is dropped.</param>
internal sealed record LegLayout(
    int Index,
    IReadOnlyList<IReadOnlyList<Point>> Parts,
    double LengthPx,
    Point? Midpoint,
    GeoPosition? MidpointPosition,
    bool ShowInsertHandle,
    LabelPlacement? Label);

/// <summary>A label's placement: its centre, its upright angle and its width.</summary>
internal readonly record struct LabelPlacement(Point Center, double AngleDegrees, double Width);

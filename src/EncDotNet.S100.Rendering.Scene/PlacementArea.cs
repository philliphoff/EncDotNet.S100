using EncDotNet.S100.Pipelines;
using NetTopologySuite.Geometries;

namespace EncDotNet.S100.Rendering.Scene;

/// <summary>
/// The area a point symbol or text is kept in when it is placed in the visible
/// parts of its surface (S-100 Part 9 <c>AreaPlacement:VisibleParts</c>).
/// </summary>
/// <remarks>
/// The op's own anchor is a representative point inside the area. While that
/// point is in view the symbol stays there, so it doesn't move as the chart
/// pans. Once it leaves the view, <see cref="AnchorIn"/> moves the symbol to a
/// representative point of the largest part of the area that is still in view.
/// </remarks>
public sealed class PlacementArea
{
    private readonly Geometry _area;
    private Placed? _last;

    /// <summary>Creates a placement area over a polygon or multipolygon in EPSG:3857 metres.</summary>
    /// <param name="area">The surface, in EPSG:3857 metres.</param>
    public PlacementArea(Geometry area)
    {
        ArgumentNullException.ThrowIfNull(area);
        _area = area;
        var envelope = area.EnvelopeInternal;
        MinX = envelope.MinX;
        MinY = envelope.MinY;
        MaxX = envelope.MaxX;
        MaxY = envelope.MaxY;
    }

    /// <summary>West edge of the area, in EPSG:3857 metres.</summary>
    public double MinX { get; }

    /// <summary>South edge of the area, in EPSG:3857 metres.</summary>
    public double MinY { get; }

    /// <summary>East edge of the area, in EPSG:3857 metres.</summary>
    public double MaxX { get; }

    /// <summary>North edge of the area, in EPSG:3857 metres.</summary>
    public double MaxY { get; }

    /// <summary>
    /// The anchor to draw at in the view (<paramref name="minX"/>,
    /// <paramref name="minY"/>)–(<paramref name="maxX"/>, <paramref name="maxY"/>),
    /// in EPSG:3857 metres: <paramref name="anchor"/> while it is in view or
    /// the area is entirely out of view, otherwise a representative point of
    /// the largest part of the area in view.
    /// </summary>
    /// <remarks>
    /// The last result is kept, so the declutter and draw passes of a frame
    /// work it out once. Safe to call from several threads.
    /// </remarks>
    public (double X, double Y) AnchorIn(
        (double X, double Y) anchor, double minX, double minY, double maxX, double maxY)
    {
        if (anchor.X >= minX && anchor.X <= maxX && anchor.Y >= minY && anchor.Y <= maxY)
            return anchor;
        if (MaxX < minX || MinX > maxX || MaxY < minY || MinY > maxY)
            return anchor;

        if (_last is { } last && last.MinX == minX && last.MinY == minY && last.MaxX == maxX && last.MaxY == maxY)
            return last.Anchor;

        var placed = VisibleAnchor(minX, minY, maxX, maxY) ?? anchor;
        _last = new Placed(minX, minY, maxX, maxY, placed);
        return placed;
    }

    private (double X, double Y)? VisibleAnchor(double minX, double minY, double maxX, double maxY)
    {
        Geometry visible;
        try
        {
            visible = _area.Intersection(_area.Factory.ToGeometry(new Envelope(minX, maxX, minY, maxY)));
        }
        catch (Exception)
        {
            // OverlayNG can fail on invalid input (it wraps the failure in an
            // AggregateException); keep the symbol at its anchor.
            return null;
        }

        Polygon? largest = null;
        for (int i = 0; i < visible.NumGeometries; i++)
        {
            if (visible.GetGeometryN(i) is Polygon { IsEmpty: false } part
                && (largest is null || part.Area > largest.Area))
            {
                largest = part;
            }
        }

        return largest is null ? null : RepresentativePoint(largest);
    }

    /// <summary>
    /// A point inside <paramref name="polygon"/>: its centroid when that lies
    /// inside, otherwise an interior point.
    /// </summary>
    public static (double X, double Y) RepresentativePoint(Geometry polygon)
    {
        ArgumentNullException.ThrowIfNull(polygon);
        Point point;
        try
        {
            var centroid = polygon.Centroid;
            point = !centroid.IsEmpty && polygon.Contains(centroid) ? centroid : polygon.InteriorPoint;
        }
        catch (Exception)
        {
            // An invalid polygon can fail the containment test.
            point = polygon.InteriorPoint;
        }
        return (point.X, point.Y);
    }

    private sealed record Placed(double MinX, double MinY, double MaxX, double MaxY, (double X, double Y) Anchor);
}

/// <summary>
/// The part of the world a view shows, in EPSG:3857 metres, for keeping
/// area-placed symbols and text in view (see <see cref="PlacementArea"/>).
/// </summary>
/// <remarks>
/// It is the viewport's north-up frame; on a rotated display the corners of
/// the screen and of this frame differ slightly.
/// </remarks>
/// <param name="MinX">West edge.</param>
/// <param name="MinY">South edge.</param>
/// <param name="MaxX">East edge.</param>
/// <param name="MaxY">North edge.</param>
public readonly record struct ViewExtent(double MinX, double MinY, double MaxX, double MaxY)
{
    /// <summary>The extent of <paramref name="viewport"/>.</summary>
    public static ViewExtent Of(Viewport viewport)
    {
        ArgumentNullException.ThrowIfNull(viewport);
        var (minX, minY) = WebMercator.FromLonLat(viewport.MinLongitude, viewport.MinLatitude);
        var (maxX, maxY) = WebMercator.FromLonLat(viewport.MaxLongitude, viewport.MaxLatitude);
        return new ViewExtent(minX, minY, maxX, maxY);
    }

    /// <summary>Where to draw <paramref name="op"/> in this view.</summary>
    public (double X, double Y) Anchor(PointPaintOp op)
    {
        ArgumentNullException.ThrowIfNull(op);
        return op.PlacementArea is { } area ? area.AnchorIn(op.World, MinX, MinY, MaxX, MaxY) : op.World;
    }

    /// <summary>Where to draw <paramref name="op"/> in this view.</summary>
    public (double X, double Y) Anchor(TextPaintOp op)
    {
        ArgumentNullException.ThrowIfNull(op);
        return op.PlacementArea is { } area ? area.AnchorIn(op.World, MinX, MinY, MaxX, MaxY) : op.World;
    }
}

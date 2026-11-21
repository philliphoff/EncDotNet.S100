namespace EncDotNet.S100.Rendering.Engine;

/// <summary>
/// What a host shows: the live map view the engine composites for, in
/// EPSG:3857 metres and device-independent pixels. This is the host-neutral
/// replacement for a map framework's own viewport type (map rendering
/// slice&#160;7); a host adapter fills it from its viewport each frame.
/// </summary>
/// <remarks>
/// The projection helpers (<see cref="WorldToScreenXY"/>, <see cref="ToExtent"/>)
/// follow the same conventions as Mapsui's viewport, so a frame composited from
/// a <see cref="ViewState"/> lines up with the host's other layers: screen
/// <c>y</c> grows downwards and <see cref="Rotation"/> turns the map clockwise
/// on screen, in degrees.
/// </remarks>
/// <param name="CenterX">View centre easting, EPSG:3857 metres.</param>
/// <param name="CenterY">View centre northing, EPSG:3857 metres.</param>
/// <param name="Resolution">EPSG:3857 metres per device-independent pixel.</param>
/// <param name="Rotation">Map rotation in degrees; 0 is north-up.</param>
/// <param name="Width">View width in device-independent pixels.</param>
/// <param name="Height">View height in device-independent pixels.</param>
public readonly record struct ViewState(
    double CenterX,
    double CenterY,
    double Resolution,
    double Rotation,
    double Width,
    double Height)
{
    // Half a ULP of 1.0: the threshold Mapsui uses, so the same views count as rotated.
    private const double RotationEpsilon = 1.1102230246251565E-16;

    /// <summary>Whether the view has a positive width and height.</summary>
    public bool HasSize => Width > 0.0 && Height > 0.0;

    /// <summary>Whether the map is drawn rotated (any rotation that is not a whole turn).</summary>
    public bool IsRotated => !double.IsNaN(Rotation) && Math.Abs(Rotation % 360.0) > RotationEpsilon;

    /// <summary>
    /// The axis-aligned EPSG:3857 rectangle the view covers. For a rotated view
    /// it is the bounding box of the rotated view, so it can include parts that
    /// are off screen.
    /// </summary>
    /// <returns>The view's world extent.</returns>
    public WorldRect ToExtent()
    {
        var halfWidth = Width * Resolution * 0.5;
        var halfHeight = Height * Resolution * 0.5;
        var minX = CenterX - halfWidth;
        var minY = CenterY - halfHeight;
        var maxX = CenterX + halfWidth;
        var maxY = CenterY + halfHeight;
        if (!IsRotated)
        {
            return new WorldRect(minX, minY, maxX, maxY);
        }

        var degrees = 0.0 - Rotation;
        var (x0, y0) = RotateAbout(minX, minY, degrees, CenterX, CenterY);
        var (x1, y1) = RotateAbout(minX, maxY, degrees, CenterX, CenterY);
        var (x2, y2) = RotateAbout(maxX, maxY, degrees, CenterX, CenterY);
        var (x3, y3) = RotateAbout(maxX, minY, degrees, CenterX, CenterY);
        return new WorldRect(
            Math.Min(Math.Min(x0, x1), Math.Min(x2, x3)),
            Math.Min(Math.Min(y0, y1), Math.Min(y2, y3)),
            Math.Max(Math.Max(x0, x1), Math.Max(x2, x3)),
            Math.Max(Math.Max(y0, y1), Math.Max(y2, y3)));
    }

    /// <summary>
    /// Projects an EPSG:3857 position to device-independent screen pixels,
    /// honouring <see cref="Rotation"/>.
    /// </summary>
    /// <param name="worldX">Easting, EPSG:3857 metres.</param>
    /// <param name="worldY">Northing, EPSG:3857 metres.</param>
    /// <returns>The screen position; <c>y</c> grows downwards.</returns>
    public (double ScreenX, double ScreenY) WorldToScreenXY(double worldX, double worldY)
    {
        var screenCenterX = Width / 2.0;
        var screenCenterY = Height / 2.0;
        var x = (worldX - CenterX) / Resolution + screenCenterX;
        var y = (CenterY - worldY) / Resolution + screenCenterY;
        return IsRotated
            ? RotateAbout(x, y, 0.0 - Rotation, screenCenterX, screenCenterY)
            : (x, y);
    }

    // Rotates (x, y) clockwise by degrees about (cx, cy), with the same
    // arithmetic as Mapsui's Algorithms.RotateClockwiseDegrees so composited
    // frames and clip paths land on exactly the pixels the host expects.
    private static (double X, double Y) RotateAbout(double x, double y, double degrees, double cx, double cy)
    {
        var dx = x - cx;
        var dy = y - cy;
        var radians = Math.IEEERemainder(degrees, 360.0) * (Math.PI / 180.0);
        var (sin, cos) = Math.SinCos(Math.IEEERemainder(radians, Math.PI * 2.0));
        var negSin = 0.0 - sin;
        return (dx * cos - dy * negSin + cx, dx * negSin + dy * cos + cy);
    }
}

/// <summary>An axis-aligned EPSG:3857 rectangle, in metres.</summary>
/// <param name="MinX">West edge.</param>
/// <param name="MinY">South edge.</param>
/// <param name="MaxX">East edge.</param>
/// <param name="MaxY">North edge.</param>
public readonly record struct WorldRect(double MinX, double MinY, double MaxX, double MaxY);

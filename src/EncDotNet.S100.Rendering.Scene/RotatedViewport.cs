using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Rendering.Scene;

/// <summary>
/// Geometry of a rotated <see cref="Viewport"/> (issue #578). A rotated display
/// is drawn by rendering a north-up <em>cover</em> — an axis-aligned viewport at
/// the same scale and centre, large enough that the rotated output rectangle
/// fits inside it — and rotating that onto the output about its centre.
/// </summary>
public static class RotatedViewport
{
    /// <summary>
    /// The north-up viewport covering <paramref name="viewport"/> once rotated:
    /// same centre and metres per pixel, with a pixel size that holds the
    /// rotated output's bounding box. Returns <paramref name="viewport"/> itself
    /// when it is not rotated.
    /// </summary>
    /// <remarks>
    /// The cover's width and height differ from the output's by an even number
    /// of pixels, so its centre lands on the output's centre exactly, with no
    /// half-pixel shift.
    /// </remarks>
    public static Viewport NorthUpCover(Viewport viewport)
    {
        ArgumentNullException.ThrowIfNull(viewport);

        double rotation = viewport.RotationDegrees % 360.0;
        if (rotation == 0)
            return viewport;

        double rad = rotation * Math.PI / 180.0;
        double cos = Math.Abs(Math.Cos(rad));
        double sin = Math.Abs(Math.Sin(rad));
        int width = viewport.WidthPixels;
        int height = viewport.HeightPixels;
        int coverWidth = CoverSize(width * cos + height * sin, width);
        int coverHeight = CoverSize(width * sin + height * cos, height);

        var (minX, minY) = WebMercator.FromLonLat(viewport.MinLongitude, viewport.MinLatitude);
        var (maxX, maxY) = WebMercator.FromLonLat(viewport.MaxLongitude, viewport.MaxLatitude);
        double growX = (maxX - minX) / width * (coverWidth - width) / 2.0;
        double growY = (maxY - minY) / height * (coverHeight - height) / 2.0;
        var (minLon, minLat) = WebMercator.ToLonLat(minX - growX, minY - growY, clampLatitude: false);
        var (maxLon, maxLat) = WebMercator.ToLonLat(maxX + growX, maxY + growY, clampLatitude: false);

        return viewport with
        {
            MinLongitude = minLon,
            MinLatitude = minLat,
            MaxLongitude = maxLon,
            MaxLatitude = maxLat,
            WidthPixels = coverWidth,
            HeightPixels = coverHeight,
            RotationDegrees = 0,
        };
    }

    // The smallest size >= extent whose difference from the output size is even.
    private static int CoverSize(double extent, int size)
    {
        int cover = Math.Max(1, (int)Math.Ceiling(extent - 1e-9));
        return (cover - size) % 2 == 0 ? cover : cover + 1;
    }
}

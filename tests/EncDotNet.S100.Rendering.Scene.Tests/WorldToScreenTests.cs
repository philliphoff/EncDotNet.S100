using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Rendering.Scene.Tests;

/// <summary>
/// Parity tests for <see cref="WorldToScreen"/> — the EPSG:3857 world → pixel
/// affine extracted out of the Skia backend into the backend-neutral
/// <c>Rendering.Scene</c> assembly (issue #400). Each case asserts the shared
/// helper reproduces the exact projection the Skia renderer applied inline
/// before extraction, so a Skia-free backend projects identically.
/// </summary>
public sealed class WorldToScreenTests
{
    private static Viewport MakeViewport(
        double minLon, double minLat, double maxLon, double maxLat, int width, int height) =>
        new()
        {
            MinLongitude = minLon,
            MinLatitude = minLat,
            MaxLongitude = maxLon,
            MaxLatitude = maxLat,
            WidthPixels = width,
            HeightPixels = height,
            ScaleDenominator = 50_000,
        };

    /// <summary>
    /// The reference projection: the exact formula the Skia renderer used inline
    /// (project the viewport corners to EPSG:3857, then map linearly to pixels
    /// with origin top-left and +Y down). The extracted <see cref="WorldToScreen"/>
    /// must match this bit-for-bit.
    /// </summary>
    private static (float X, float Y) ReferenceProject(Viewport vp, (double X, double Y) world)
    {
        var (minX, minY) = WebMercator.FromLonLat(vp.MinLongitude, vp.MinLatitude);
        var (maxX, maxY) = WebMercator.FromLonLat(vp.MaxLongitude, vp.MaxLatitude);
        double spanX = maxX - minX;
        double spanY = maxY - minY;
        double scaleX = spanX != 0 ? vp.WidthPixels / spanX : 0;
        double scaleY = spanY != 0 ? vp.HeightPixels / spanY : 0;
        float sx = (float)((world.X - minX) * scaleX);
        float sy = (float)((maxY - world.Y) * scaleY);
        return (sx, sy);
    }

    public static IEnumerable<object[]> Cases()
    {
        // Representative viewports (equatorial, mid-latitude, southern) and points
        // (interior, both corners, mid-edge).
        var equator = MakeViewport(-10, -5, 10, 5, 1024, 768);
        var northSea = MakeViewport(2.0, 51.0, 5.0, 53.0, 1200, 900);
        var southern = MakeViewport(150.0, -40.0, 155.0, -35.0, 800, 600);

        foreach (var vp in new[] { equator, northSea, southern })
        {
            // Corners of the viewport in lon/lat, projected to world.
            var (bl0, bl1) = WebMercator.FromLonLat(vp.MinLongitude, vp.MinLatitude);
            var (tr0, tr1) = WebMercator.FromLonLat(vp.MaxLongitude, vp.MaxLatitude);
            var midLon = (vp.MinLongitude + vp.MaxLongitude) / 2;
            var midLat = (vp.MinLatitude + vp.MaxLatitude) / 2;
            var (mid0, mid1) = WebMercator.FromLonLat(midLon, midLat);

            yield return new object[] { vp, (bl0, bl1) };   // bottom-left
            yield return new object[] { vp, (tr0, tr1) };   // top-right
            yield return new object[] { vp, (mid0, mid1) }; // centre
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Project_matches_reference_formula(Viewport vp, (double X, double Y) world)
    {
        var transform = WorldToScreen.Create(vp);

        var (ax, ay) = transform.Project(world);
        var (ex, ey) = ReferenceProject(vp, world);

        Assert.Equal(ex, ax, 3);
        Assert.Equal(ey, ay, 3);
    }

    [Fact]
    public void Corners_map_to_pixel_rectangle()
    {
        var vp = MakeViewport(2.0, 51.0, 5.0, 53.0, 1200, 900);
        var transform = WorldToScreen.Create(vp);

        var bottomLeft = WebMercator.FromLonLat(vp.MinLongitude, vp.MinLatitude);
        var topRight = WebMercator.FromLonLat(vp.MaxLongitude, vp.MaxLatitude);

        var (blx, bly) = transform.Project(bottomLeft);
        var (trx, trY) = transform.Project(topRight);

        // Origin top-left, +Y down: bottom-left of the map is (0, height),
        // top-right is (width, 0).
        Assert.Equal(0f, blx, 3);
        Assert.Equal(900f, bly, 3);
        Assert.Equal(1200f, trx, 3);
        Assert.Equal(0f, trY, 3);
    }

    [Fact]
    public void Degenerate_viewport_collapses_to_axis_origin()
    {
        // Zero-width longitude span → x scale is 0; every point projects to x = 0.
        var vp = MakeViewport(3.0, 51.0, 3.0, 53.0, 1000, 800);
        var transform = WorldToScreen.Create(vp);

        var world = WebMercator.FromLonLat(3.0, 52.0);
        var (x, _) = transform.Project(world);

        Assert.Equal(0f, x, 3);
    }
}

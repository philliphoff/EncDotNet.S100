using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Rendering.Scene.Tests;

/// <summary>
/// The north-up cover of a rotated viewport (issue #578): same centre and
/// scale, sized to hold the rotated output.
/// </summary>
public sealed class RotatedViewportTests
{
    private static Viewport View(double rotation) => new()
    {
        MinLongitude = -1,
        MaxLongitude = 1,
        MinLatitude = -0.5,
        MaxLatitude = 0.5,
        WidthPixels = 200,
        HeightPixels = 100,
        ScaleDenominator = 4_000_000,
        RotationDegrees = rotation,
    };

    [Theory]
    [InlineData(0)]
    [InlineData(360)]
    public void NorthUp_IsItsOwnCover(double rotation)
    {
        var viewport = View(rotation);

        Assert.Same(viewport, RotatedViewport.NorthUpCover(viewport));
    }

    [Theory]
    [InlineData(90, 100, 200)]
    [InlineData(-90, 100, 200)]
    [InlineData(180, 200, 100)]
    [InlineData(45, 214, 214)]  // 212.1 each way, rounded up to an even difference from 200 / 100
    public void Cover_HoldsTheRotatedOutput(double rotation, int width, int height)
    {
        var cover = RotatedViewport.NorthUpCover(View(rotation));

        Assert.Equal(width, cover.WidthPixels);
        Assert.Equal(height, cover.HeightPixels);
        Assert.Equal(0, cover.RotationDegrees);
    }

    [Fact]
    public void Cover_KeepsTheCentreAndScale()
    {
        var viewport = View(30);
        var cover = RotatedViewport.NorthUpCover(viewport);

        var (minX, minY) = WebMercator.FromLonLat(viewport.MinLongitude, viewport.MinLatitude);
        var (maxX, maxY) = WebMercator.FromLonLat(viewport.MaxLongitude, viewport.MaxLatitude);
        var (coverMinX, coverMinY) = WebMercator.FromLonLat(cover.MinLongitude, cover.MinLatitude);
        var (coverMaxX, coverMaxY) = WebMercator.FromLonLat(cover.MaxLongitude, cover.MaxLatitude);

        Assert.Equal((minX + maxX) / 2, (coverMinX + coverMaxX) / 2, 3);
        Assert.Equal((minY + maxY) / 2, (coverMinY + coverMaxY) / 2, 3);
        Assert.Equal((maxX - minX) / viewport.WidthPixels, (coverMaxX - coverMinX) / cover.WidthPixels, 6);
        Assert.Equal((maxY - minY) / viewport.HeightPixels, (coverMaxY - coverMinY) / cover.HeightPixels, 6);
        Assert.Equal(viewport.ScaleDenominator, cover.ScaleDenominator);
    }
}

using EncDotNet.S100.Cli.Infrastructure;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// Unit tests for <see cref="CompositeViewportBuilder"/>: the CLI helper that
/// maps <c>--bbox</c> / <c>--center</c>+<c>--scale</c> into a shared
/// <see cref="EncDotNet.S100.Pipelines.Viewport"/>.
/// </summary>
public sealed class CompositeViewportBuilderTests
{
    [Theory]
    [InlineData("-1.5,50.0,-1.0,50.5", 4, true)]
    [InlineData("-1.25,50.25", 2, true)]
    [InlineData("1,2,3", 4, false)]
    [InlineData("1,2,3,4,5", 4, false)]
    [InlineData("a,b,c,d", 4, false)]
    [InlineData("", 2, false)]
    public void TryParseDoubles_enforces_arity_and_numeric_tokens(string value, int expected, bool ok)
    {
        bool parsed = CompositeViewportBuilder.TryParseDoubles(value, expected, out var values);
        Assert.Equal(ok, parsed);
        if (ok)
            Assert.Equal(expected, values.Length);
    }

    [Fact]
    public void FromBoundingBox_frames_the_box_and_keeps_pixel_dimensions()
    {
        var viewport = CompositeViewportBuilder.FromBoundingBox(
            minLon: -1.5, minLat: 50.0, maxLon: -1.0, maxLat: 50.5, width: 400, height: 400);

        Assert.Equal(400, viewport.WidthPixels);
        Assert.Equal(400, viewport.HeightPixels);

        // The box is fully contained (aspect expansion only ever grows the
        // extent), and the requested extent's centre is preserved.
        Assert.True(viewport.MinLongitude <= -1.5 + 1e-9);
        Assert.True(viewport.MaxLongitude >= -1.0 - 1e-9);
        Assert.True(viewport.MinLatitude <= 50.0 + 1e-9);
        Assert.True(viewport.MaxLatitude >= 50.5 - 1e-9);

        Assert.Equal(-1.25, (viewport.MinLongitude + viewport.MaxLongitude) / 2.0, 6);
        Assert.True(viewport.ScaleDenominator > 0);
    }

    [Fact]
    public void FromCenterScale_centres_on_the_point_and_recovers_the_scale()
    {
        const double centerLon = -1.25;
        const double centerLat = 50.25;
        const double scale = 50_000;

        var viewport = CompositeViewportBuilder.FromCenterScale(
            centerLon, centerLat, scale, width: 512, height: 512);

        Assert.Equal(512, viewport.WidthPixels);
        Assert.Equal(512, viewport.HeightPixels);

        Assert.Equal(centerLon, (viewport.MinLongitude + viewport.MaxLongitude) / 2.0, 6);
        Assert.Equal(centerLat, (viewport.MinLatitude + viewport.MaxLatitude) / 2.0, 4);

        // The round-trip through the compositor's denom maths recovers the
        // requested scale to within a fraction of a percent (the small residual
        // is the mercator non-linearity between the centre and mean latitude).
        Assert.InRange(viewport.ScaleDenominator, scale * 0.99, scale * 1.01);
    }
}

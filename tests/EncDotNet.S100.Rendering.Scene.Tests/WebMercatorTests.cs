namespace EncDotNet.S100.Rendering.Scene.Tests;

/// <summary>
/// Tests for <see cref="WebMercator"/>, focused on the clamped vs. lossless
/// inverse used when reconstructing a render viewport from EPSG:3857 world
/// bounds. The lossless (<c>clampLatitude: false</c>) inverse must round-trip
/// exactly through <see cref="WebMercator.FromLonLat"/> even for a northing
/// beyond the ±85.05° pole limit; the clamped inverse must not, and the two
/// must agree for any in-range northing. Regression cover for the high-latitude
/// zoomed-out drift (US NWS S-411 sea-ice product) where clamping the viewport
/// edge pulled geometry poleward off its labels.
/// </summary>
public sealed class WebMercatorTests
{
    private const double PoleExtent = System.Math.PI * WebMercator.EarthRadius;

    [Fact]
    public void ToLonLat_Unclamped_RoundTripsBeyondPoleExactly()
    {
        // A northing well beyond the pole limit (as produced by a zoomed-out
        // viewport edge or a top-row tile gutter at high latitude).
        double y = PoleExtent * 1.25;

        var (_, lat) = WebMercator.ToLonLat(0, y, clampLatitude: false);
        var (_, back) = WebMercator.FromLonLat(0, lat);

        Assert.True(lat > WebMercator.MaxLatitude, $"expected lat beyond pole limit, got {lat}");
        Assert.Equal(y, back, 3); // exact round-trip to the millimetre
    }

    [Fact]
    public void ToLonLat_Clamped_DoesNotRoundTripBeyondPole()
    {
        double y = PoleExtent * 1.25;

        var (_, lat) = WebMercator.ToLonLat(0, y, clampLatitude: true);
        var (_, back) = WebMercator.FromLonLat(0, lat);

        Assert.Equal(WebMercator.MaxLatitude, lat, 6);
        // The clamp collapses the northing back to the pole extent, losing the
        // overhang — this is exactly why viewport construction must not clamp.
        Assert.Equal(PoleExtent, back, 0);
        Assert.True(back < y);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1_000_000.0)]
    [InlineData(9_970_000.0)]   // ~66°N (S-411 south edge)
    [InlineData(15_500_000.0)]  // ~80°N (S-411 north edge)
    [InlineData(-15_500_000.0)]
    public void ToLonLat_ClampedAndUnclamped_AgreeInRange(double y)
    {
        // For any northing inside the pole limit the clamp never triggers, so
        // the two inverses are identical — the lossless variant is a no-op
        // except in the overflow case.
        var clamped = WebMercator.ToLonLat(0, y, clampLatitude: true);
        var unclamped = WebMercator.ToLonLat(0, y, clampLatitude: false);

        Assert.Equal(clamped.Latitude, unclamped.Latitude, 9);
        Assert.Equal(clamped.Longitude, unclamped.Longitude, 9);
    }
}

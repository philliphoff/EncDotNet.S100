using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Rendering.Scene;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// EPSG:3857 from <see cref="ProjNetCrsTransformFactory"/> is WGS 84 /
/// Pseudo-Mercator (EPSG method 1024, spherical formulas on WGS 84
/// coordinates), matching the Web Mercator the renderers draw in. It used to be
/// ProjNet's ellipsoidal Mercator, which put station glyphs ~20 km south at 50°N
/// once a viewport or another layer fixed the frame (issue #191).
/// </summary>
public class ProjNetWebMercatorTests
{
    private static readonly ProjNetCrsTransformFactory Factory = new();

    [Theory]
    [InlineData(-1.30, 50.75)]
    [InlineData(151.25, -33.99)]
    [InlineData(0.0, 0.0)]
    [InlineData(-122.3, 47.6)]
    public void Wgs84ToWebMercator_MatchesTheRenderersWebMercator(double lon, double lat)
    {
        var (x, y) = Factory.Create("EPSG:4326", "EPSG:3857").Transform(lon, lat);
        var (ex, ey) = WebMercator.FromLonLat(lon, lat);

        Assert.Equal(ex, x, 6);
        Assert.Equal(ey, y, 6);
    }

    [Fact]
    public void Wgs84ToWebMercator_AtFiftyDegrees_IsTheSphericalNorthing()
    {
        // Spherical Pseudo-Mercator at 50.75°N is ~6,577,200 m; the ellipsoidal
        // Mercator the factory used to return is ~6,544,100 m.
        var (_, y) = Factory.Create("EPSG:4326", "EPSG:3857").Transform(-1.30, 50.75);

        Assert.InRange(y, 6_576_500, 6_578_000);
    }

    [Theory]
    [InlineData(-1.30, 50.75)]
    [InlineData(151.25, -33.99)]
    public void WebMercatorToWgs84_RoundTrips(double lon, double lat)
    {
        var (x, y) = Factory.Create("EPSG:4326", "EPSG:3857").Transform(lon, lat);
        var (rlon, rlat) = Factory.Create("EPSG:3857", "EPSG:4326").Transform(x, y);

        Assert.Equal(lon, rlon, 9);
        Assert.Equal(lat, rlat, 9);
    }

    [Fact]
    public void UtmToWebMercator_GoesThroughWgs84()
    {
        // UTM zone 30N easting/northing near the Solent.
        var (lon, lat) = Factory.Create("EPSG:32630", "EPSG:4326").Transform(620_000, 5_623_000);
        var (x, y) = Factory.Create("EPSG:32630", "EPSG:3857").Transform(620_000, 5_623_000);
        var (ex, ey) = WebMercator.FromLonLat(lon, lat);

        Assert.Equal(ex, x, 3);
        Assert.Equal(ey, y, 3);

        var (bx, by) = Factory.Create("EPSG:3857", "EPSG:32630").Transform(x, y);
        Assert.Equal(620_000, bx, 3);
        Assert.Equal(5_623_000, by, 3);
    }
}

using EncDotNet.S100.Renderers.Mapsui;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Verifies the web-mercator latitude correction applied when converting an
/// S-100 Part 9 §11.1 true-scale denominator to a Mapsui EPSG:3857 resolution
/// (metres/pixel at the equator). Web-mercator inflates ground distances by
/// <c>1/cos φ</c>, so a true-scale denominator maps to a larger equator-
/// referenced resolution as latitude increases; omitting the term (the prior
/// behaviour) suppressed detail too early off the equator.
/// </summary>
public class MapsuiScaleDenominatorConversionTests
{
    private const double WebMercatorEarthRadius = 6378137.0;

    private static double LatitudeToWebMercatorY(double latitudeRadians)
        => WebMercatorEarthRadius * Math.Log(Math.Tan(Math.PI / 4.0 + latitudeRadians / 2.0));

    [Fact]
    public void DenominatorToResolution_AtEquator_IsUncorrected()
    {
        var res = MapsuiDisplayListRenderer.DenominatorToResolution(22000, 0.0);

        Assert.Equal(22000 * MapsuiDisplayListRenderer.DenomToResolutionMetres, res, 6);
    }

    [Fact]
    public void DenominatorToResolution_OffEquator_DividesByCosLatitude()
    {
        var latitudeRadians = 50.77 * Math.PI / 180.0;

        var res = MapsuiDisplayListRenderer.DenominatorToResolution(22000, latitudeRadians);

        var expected = 22000 * MapsuiDisplayListRenderer.DenomToResolutionMetres / Math.Cos(latitudeRadians);
        Assert.Equal(expected, res, 6);
    }

    [Fact]
    public void DenominatorToResolution_OffEquator_IsLargerThanEquator()
    {
        var equator = MapsuiDisplayListRenderer.DenominatorToResolution(22000, 0.0);
        var cowes = MapsuiDisplayListRenderer.DenominatorToResolution(22000, 50.77 * Math.PI / 180.0);

        Assert.True(cowes > equator);
    }

    [Fact]
    public void DenominatorToResolution_ClampsNearPoles()
    {
        // cos(90°) = 0 would divide by zero; the helper clamps cos to 1e-6.
        var res = MapsuiDisplayListRenderer.DenominatorToResolution(22000, Math.PI / 2.0);

        var expected = 22000 * MapsuiDisplayListRenderer.DenomToResolutionMetres / 1e-6;
        Assert.Equal(expected, res, 3);
    }

    [Fact]
    public void WebMercatorYToLatitudeRadians_RoundTrips()
    {
        var latitudeRadians = 50.77 * Math.PI / 180.0;
        var y = LatitudeToWebMercatorY(latitudeRadians);

        var recovered = MapsuiDisplayListRenderer.WebMercatorYToLatitudeRadians(y);

        Assert.Equal(latitudeRadians, recovered, 9);
    }

    [Fact]
    public void WebMercatorYToLatitudeRadians_AtEquatorIsZero()
    {
        Assert.Equal(0.0, MapsuiDisplayListRenderer.WebMercatorYToLatitudeRadians(0.0), 12);
    }

    [Fact]
    public void Cowes22000Cap_MakesZoom14Visible()
    {
        // 101GB00602793 (Cowes/Solent): DataCoverage.minimumDisplayScale = 22000.
        // res(z) = 156543.03392804097 / 2^z.  Uncorrected the cap = 6.16 m/px sits
        // between z14 (9.55) and z15 (4.78), hiding linework at z14. Corrected at
        // φ ≈ 50.77° the cap ≈ 9.74 m/px, which now admits z14.
        var latitudeRadians = 50.77 * Math.PI / 180.0;
        var cap = MapsuiDisplayListRenderer.DenominatorToResolution(22000, latitudeRadians);

        const double z14 = 156543.03392804097 / (1 << 14);
        const double uncorrected = 22000 * MapsuiDisplayListRenderer.DenomToResolutionMetres;

        Assert.True(uncorrected < z14, "precondition: uncorrected cap hides z14");
        Assert.True(cap > z14, "corrected cap should admit z14 linework");
    }
}

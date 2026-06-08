using EncDotNet.S100.Viewer.ViewModels;
using Xunit;

namespace EncDotNet.S100.Viewer.Tests.ViewModels;

/// <summary>
/// Tests for <see cref="VesselGeoMath"/> great-circle range/bearing.
/// </summary>
public class VesselGeoMathTests
{
    [Fact]
    public void DistanceMetres_OneDegreeLongitudeAtEquator_IsAboutOneEleventhOfMeridian()
    {
        // 1° of longitude at the equator ≈ 111.19 km on the authalic sphere.
        var d = VesselGeoMath.DistanceMetres(0, 0, 0, 1);
        Assert.Equal(111_194.9, d, 0);
    }

    [Fact]
    public void DistanceMetres_IsSymmetric()
    {
        var ab = VesselGeoMath.DistanceMetres(10, 20, 11, 21);
        var ba = VesselGeoMath.DistanceMetres(11, 21, 10, 20);
        Assert.Equal(ab, ba, 6);
    }

    [Theory]
    [InlineData(1, 0, 0.0)]    // due north
    [InlineData(0, 1, 90.0)]   // due east
    [InlineData(-1, 0, 180.0)] // due south
    [InlineData(0, -1, 270.0)] // due west
    public void InitialBearing_CardinalDirections(double dLat, double dLon, double expected)
    {
        var bearing = VesselGeoMath.InitialBearingDegrees(0, 0, dLat, dLon);
        Assert.Equal(expected, bearing, 3);
    }

    [Fact]
    public void InitialBearing_IsNormalisedToZeroTo360()
    {
        var bearing = VesselGeoMath.InitialBearingDegrees(0, 0, -0.0001, -1);
        Assert.InRange(bearing, 0.0, 360.0);
    }
}

using EncDotNet.S100.Geodesy;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Tests for the spherical geodesic helpers used by augmented line geometry
/// tessellation.
/// </summary>
public class GeodesicHelperTests
{
    private const double ToleranceDegrees = 0.001; // ~111 m at equator

    [Fact]
    public void DirectProblem_DueNorth_IncreasesLatitude()
    {
        var (lat, lon) = GeodesicHelper.DirectProblem(0.0, 0.0, 0.0, 111_320.0);

        // ~1 degree north from the equator at 0° longitude.
        Assert.InRange(lat, 0.99, 1.01);
        Assert.InRange(lon, -0.01, 0.01);
    }

    [Fact]
    public void DirectProblem_DueEast_OnEquator_IncreasesLongitude()
    {
        var (lat, lon) = GeodesicHelper.DirectProblem(0.0, 0.0, 90.0, 111_320.0);

        Assert.InRange(lat, -0.01, 0.01);
        Assert.InRange(lon, 0.99, 1.01);
    }

    [Fact]
    public void DirectProblem_DueSouth_DecreasesLatitude()
    {
        var (lat, lon) = GeodesicHelper.DirectProblem(45.0, 10.0, 180.0, 111_320.0);

        Assert.True(lat < 45.0);
        Assert.InRange(lat, 43.99, 44.01);
    }

    [Fact]
    public void DirectProblem_ZeroDistance_ReturnsSamePoint()
    {
        // Zero distance is not valid for TessellateRay (throws), but
        // DirectProblem should handle it gracefully.
        var (lat, lon) = GeodesicHelper.DirectProblem(51.5, -0.1, 45.0, 0.0);

        Assert.Equal(51.5, lat, precision: 10);
        Assert.Equal(-0.1, lon, precision: 10);
    }

    [Fact]
    public void TessellateArc_FullCircle_FirstAndLastPointsCoincide()
    {
        var points = GeodesicHelper.TessellateArc(60.0, 25.0, 1000.0, 0.0, 360.0);

        Assert.True(points.Count >= 3);
        Assert.Equal(points[0].Latitude, points[^1].Latitude, precision: 10);
        Assert.Equal(points[0].Longitude, points[^1].Longitude, precision: 10);
    }

    [Fact]
    public void TessellateArc_HalfCircle_CoversExpectedRange()
    {
        // Arc from bearing 0° sweeping 180° clockwise (north → east → south).
        var points = GeodesicHelper.TessellateArc(0.0, 0.0, 10_000.0, 0.0, 180.0);

        Assert.True(points.Count >= 3);

        // First point should be roughly due north of the centre.
        Assert.True(points[0].Latitude > 0.0);
        // Last point should be roughly due south.
        Assert.True(points[^1].Latitude < 0.0);
    }

    [Fact]
    public void TessellateArc_NegativeSweep_ProducesCounterClockwise()
    {
        // Negative sweep should go counter-clockwise.
        var points = GeodesicHelper.TessellateArc(0.0, 0.0, 10_000.0, 0.0, -90.0);

        Assert.True(points.Count >= 3);
        // Counter-clockwise from north = towards west (negative longitude).
        Assert.True(points[^1].Longitude < 0.0);
    }

    [Fact]
    public void TessellateRay_ShortDistance_ReturnsTwoPoints()
    {
        // < 10 km → simple two-point segment.
        var points = GeodesicHelper.TessellateRay(51.5, -0.1, 45.0, 5_000.0);

        Assert.Equal(2, points.Count);

        // First point is the origin.
        Assert.Equal(51.5, points[0].Latitude, precision: 10);
        Assert.Equal(-0.1, points[0].Longitude, precision: 10);

        // Second point should be NE of origin.
        Assert.True(points[1].Latitude > 51.5);
        Assert.True(points[1].Longitude > -0.1);
    }

    [Fact]
    public void TessellateRay_LongDistance_ProducesMultipleSegments()
    {
        // > 10 km → tessellated.
        var points = GeodesicHelper.TessellateRay(0.0, 0.0, 0.0, 50_000.0);

        Assert.True(points.Count > 2);
        Assert.Equal(0.0, points[0].Latitude, precision: 10);
        Assert.True(points[^1].Latitude > 0.0);
    }

    [Fact]
    public void TessellateArc_NegativeRadius_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            GeodesicHelper.TessellateArc(0, 0, -1, 0, 90));
    }

    [Fact]
    public void TessellateRay_NegativeDistance_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            GeodesicHelper.TessellateRay(0, 0, 0, -1));
    }
}

using EncDotNet.S100.Viewer.Geodesy;
using Xunit;

namespace EncDotNet.S100.Viewer.Tests;

public class MarineGeodesyTests
{
    // New York (40.7128°N, 74.0060°W) → Lisbon (38.7223°N, 9.1393°W).
    // Reference rhumb-line distance ≈ 5550 km / 2998 NM (computed with the
    // Movable Type Scripts rhumb formulas using mean Earth radius 6371 km
    // = 3440.065 NM). Tolerance allows for small differences if the radius
    // constant changes.
    [Fact]
    public void RhumbDistance_NewYorkToLisbon_MatchesReference()
    {
        var nm = MarineGeodesy.RhumbDistanceNm(40.7128, -74.0060, 38.7223, -9.1393);
        Assert.InRange(nm, 2980.0, 3020.0);
    }

    [Fact]
    public void RhumbBearing_NewYorkToLisbon_MatchesReference()
    {
        var deg = MarineGeodesy.RhumbBearingDegrees(40.7128, -74.0060, 38.7223, -9.1393);
        // Eastward, slightly south of due-east.
        Assert.InRange(deg, 90.0, 100.0);
    }

    [Fact]
    public void RhumbBearing_PureEast_Returns090()
    {
        // Same latitude → bearing should be exactly due east.
        var deg = MarineGeodesy.RhumbBearingDegrees(0.0, 0.0, 0.0, 1.0);
        Assert.InRange(deg, 89.99, 90.01);
    }

    [Fact]
    public void RhumbBearing_PureNorth_Returns000()
    {
        // Normalised to [0, 360); pure north is 0.
        var deg = MarineGeodesy.RhumbBearingDegrees(0.0, 0.0, 1.0, 0.0);
        Assert.InRange(deg, 0.0, 0.01);
    }

    [Fact]
    public void RhumbBearing_TakesShortWayAcrossAntimeridian()
    {
        // 179°E → 179°W is 2° east, not 358° west.
        var deg = MarineGeodesy.RhumbBearingDegrees(0.0, 179.0, 0.0, -179.0);
        Assert.InRange(deg, 89.0, 91.0);
    }

    [Fact]
    public void RhumbDistance_ZeroLengthLeg_IsZero()
    {
        var nm = MarineGeodesy.RhumbDistanceNm(45.0, -10.0, 45.0, -10.0);
        Assert.Equal(0.0, nm, precision: 6);
    }

    [Fact]
    public void RhumbDistance_HighLatitudeIsClamped_NotInfinite()
    {
        // Without latitude clamping the Mercator stretch blows up at ±90°
        // and the resulting distance becomes NaN/∞. Verify the result is
        // a finite, sensible value.
        var nm = MarineGeodesy.RhumbDistanceNm(89.999, 0.0, 89.999, 90.0);
        Assert.True(double.IsFinite(nm));
        Assert.True(nm >= 0.0);
    }

    [Fact]
    public void SplitAtAntimeridian_NoCrossing_ReturnsSinglePath()
    {
        var input = new[] { (0.0, 10.0), (5.0, 20.0), (10.0, 30.0) };
        var split = MarineGeodesy.SplitAtAntimeridian(input);
        Assert.Single(split);
        Assert.Equal(3, split[0].Count);
    }

    [Fact]
    public void SplitAtAntimeridian_CrossingFromEastToWest_SplitsIntoTwoSubPaths()
    {
        // 179°E → -179°E (i.e., 181°E) crosses the antimeridian going east.
        // The renderer should treat this as two sub-paths.
        var input = new[] { (0.0, 179.0), (0.0, -179.0) };
        var split = MarineGeodesy.SplitAtAntimeridian(input);
        Assert.Equal(2, split.Count);
        Assert.Single(split[0]);
        Assert.Single(split[1]);
    }

    [Fact]
    public void SplitAtAntimeridian_EmptyInput_ReturnsEmptyList()
    {
        var split = MarineGeodesy.SplitAtAntimeridian(System.Array.Empty<(double, double)>());
        Assert.Empty(split);
    }

    [Fact]
    public void GreatCircleDistance_NewYorkToLisbon_MatchesReference()
    {
        // Reference great-circle distance ≈ 5414 km / 2923 NM.
        var nm = MarineGeodesy.GreatCircleDistanceNm(40.7128, -74.0060, 38.7223, -9.1393);
        Assert.InRange(nm, 2900.0, 2945.0);
    }

    [Fact]
    public void GreatCircleDistance_IsShorterThanRhumbOnHighLatitudeEastWestLeg()
    {
        var gc = MarineGeodesy.GreatCircleDistanceNm(60.0, -10.0, 60.0, 10.0);
        var rl = MarineGeodesy.RhumbDistanceNm(60.0, -10.0, 60.0, 10.0);
        Assert.True(gc < rl);
    }

    [Fact]
    public void GreatCircleDistance_ZeroLengthLeg_IsZero()
    {
        var nm = MarineGeodesy.GreatCircleDistanceNm(45.0, -10.0, 45.0, -10.0);
        Assert.Equal(0.0, nm, precision: 6);
    }

    [Fact]
    public void GreatCircleInitialBearing_PureEastAlongEquator_Returns090()
    {
        // Along the equator the great circle coincides with the parallel, so
        // the initial bearing is due east.
        var deg = MarineGeodesy.GreatCircleInitialBearingDegrees(0.0, 0.0, 0.0, 10.0);
        Assert.InRange(deg, 89.99, 90.01);
    }

    [Fact]
    public void GreatCircleInitialBearing_NorthernEastWardLeg_StartsNorthOfEast()
    {
        // Heading east at a positive latitude, the great circle initially
        // bears north of due east (poleward), unlike the constant-east rhumb.
        var deg = MarineGeodesy.GreatCircleInitialBearingDegrees(60.0, -10.0, 60.0, 10.0);
        Assert.InRange(deg, 0.0, 90.0);
    }

    [Fact]
    public void GreatCircleIntermediatePoints_ReturnsSegmentsPlusOnePoints()
    {
        var pts = MarineGeodesy.GreatCircleIntermediatePoints(0.0, 0.0, 0.0, 30.0, 6);
        Assert.Equal(7, pts.Count);
        Assert.Equal(0.0, pts[0].Lat, 6);
        Assert.Equal(0.0, pts[0].Lon, 6);
        Assert.Equal(0.0, pts[^1].Lat, 6);
        Assert.Equal(30.0, pts[^1].Lon, 6);
    }

    [Fact]
    public void GreatCircleIntermediatePoints_EquatorLeg_StaysOnEquator()
    {
        var pts = MarineGeodesy.GreatCircleIntermediatePoints(0.0, -20.0, 0.0, 20.0, 8);
        foreach (var (lat, _) in pts)
            Assert.InRange(lat, -1e-6, 1e-6);
    }

    [Fact]
    public void GreatCircleIntermediatePoints_NorthernLeg_BowsPoleward()
    {
        // The mid-arc of a high-latitude east-west leg lies poleward of the
        // straight rhumb (constant-latitude) line.
        var pts = MarineGeodesy.GreatCircleIntermediatePoints(60.0, -30.0, 60.0, 30.0, 10);
        var mid = pts[pts.Count / 2];
        Assert.True(mid.Lat > 60.0);
    }

    [Fact]
    public void GreatCircleIntermediatePoints_CoincidentPoints_ReturnsEndpoints()
    {
        var pts = MarineGeodesy.GreatCircleIntermediatePoints(12.0, 34.0, 12.0, 34.0, 5);
        Assert.Equal(2, pts.Count);
    }

    [Fact]
    public void GreatCircleIntermediatePoints_SegmentsClampedToAtLeastOne()
    {
        var pts = MarineGeodesy.GreatCircleIntermediatePoints(0.0, 0.0, 0.0, 10.0, 0);
        Assert.Equal(2, pts.Count);
    }
}

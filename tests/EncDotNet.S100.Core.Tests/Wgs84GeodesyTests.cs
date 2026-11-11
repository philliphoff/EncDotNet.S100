using EncDotNet.S100.DataModel;
using EncDotNet.S100.Geodesy;

namespace EncDotNet.S100.Core.Tests;

public class Wgs84GeodesyTests
{
    // S-164 2.0.1 Part 4, Accuracy1 and Accuracy7: Viking to Corund Cape.
    private static readonly GeoPosition Viking = new(-(32 + 35.224 / 60), 61 + 17.710 / 60);
    private static readonly GeoPosition CorundCape = new(-(32 + 27.436 / 60), 60 + 58.609 / 60);

    [Fact]
    public void Geodesic_matches_the_S164_reference_to_the_millimetre()
    {
        Assert.Equal(33_193.554, Wgs84Geodesy.GeodesicDistanceMetres(Viking, CorundCape), 0.001);
        Assert.Equal(295.614, Wgs84Geodesy.GeodesicInitialBearingDegrees(Viking, CorundCape), 0.001);
        Assert.Equal(115.785, Wgs84Geodesy.GeodesicInitialBearingDegrees(CorundCape, Viking), 0.001);
    }

    [Fact]
    public void Rhumb_line_matches_the_S164_reference_to_the_millimetre()
    {
        Assert.Equal(33_193.567, Wgs84Geodesy.RhumbDistanceMetres(Viking, CorundCape), 0.001);
        Assert.Equal(295.699, Wgs84Geodesy.RhumbBearingDegrees(Viking, CorundCape), 0.001);
    }

    [Fact]
    public void The_direct_problems_invert_the_measurements()
    {
        var geodesic = Wgs84Geodesy.GeodesicDestination(
            Viking,
            Wgs84Geodesy.GeodesicInitialBearingDegrees(Viking, CorundCape),
            Wgs84Geodesy.GeodesicDistanceMetres(Viking, CorundCape));
        var rhumb = Wgs84Geodesy.RhumbDestination(
            Viking,
            Wgs84Geodesy.RhumbBearingDegrees(Viking, CorundCape),
            Wgs84Geodesy.RhumbDistanceMetres(Viking, CorundCape));

        Assert.True(Wgs84Geodesy.GeodesicDistanceMetres(geodesic, CorundCape) < 1e-6);
        Assert.True(Wgs84Geodesy.GeodesicDistanceMetres(rhumb, CorundCape) < 1e-6);
    }

    [Fact]
    public void A_degree_is_the_WGS84_length_at_the_equator()
    {
        var (north, east) = Wgs84Geodesy.MetresPerDegree(0);

        Assert.Equal(110_574.27, north, 0.01);
        Assert.Equal(111_319.49, east, 0.01);
    }

    [Fact]
    public void Coincident_positions_are_zero_apart_on_bearing_zero()
    {
        Assert.Equal(0, Wgs84Geodesy.GeodesicDistanceMetres(Viking, Viking));
        Assert.Equal(0, Wgs84Geodesy.GeodesicInitialBearingDegrees(Viking, Viking));
        Assert.Equal(0, Wgs84Geodesy.RhumbBearingDegrees(Viking, Viking));
    }

    [Fact]
    public void A_rhumb_line_past_the_pole_ends_at_it()
    {
        var end = Wgs84Geodesy.RhumbDestination(new GeoPosition(89, 0), 10, 1_000_000);

        Assert.Equal(new GeoPosition(90, 0), end);
    }

    [Fact]
    public void Interpolation_splits_the_distance_in_proportion()
    {
        var geodesic = Wgs84Geodesy.GeodesicInterpolate(Viking, CorundCape, 0.25);
        var rhumb = Wgs84Geodesy.RhumbInterpolate(Viking, CorundCape, 0.25);

        Assert.Equal(33_193.554 / 4, Wgs84Geodesy.GeodesicDistanceMetres(Viking, geodesic), 0.001);
        Assert.Equal(33_193.567 / 4, Wgs84Geodesy.RhumbDistanceMetres(Viking, rhumb), 0.001);
        Assert.Equal(Viking, Wgs84Geodesy.RhumbInterpolate(Viking, CorundCape, 0));
        Assert.Equal(CorundCape, Wgs84Geodesy.GeodesicInterpolate(Viking, CorundCape, 1));
    }

    [Fact]
    public void Geodesic_points_are_evenly_spaced_and_end_on_the_given_positions()
    {
        var points = Wgs84Geodesy.GeodesicPoints(Viking, CorundCape, 4);

        Assert.Equal(5, points.Length);
        Assert.Equal(Viking, points[0]);
        Assert.Equal(CorundCape, points[^1]);
        for (var i = 1; i < points.Length; i++)
        {
            Assert.Equal(33_193.554 / 4, Wgs84Geodesy.GeodesicDistanceMetres(points[i - 1], points[i]), 0.001);
        }
    }
}

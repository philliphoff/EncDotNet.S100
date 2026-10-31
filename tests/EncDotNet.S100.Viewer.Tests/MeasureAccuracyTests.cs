using EncDotNet.S100.DataModel;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Viewer.Geodesy;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The S-164 Part 4 Accuracy1–9 checks on the measure maths: distances to
/// within 1% or 30 m (whichever is greater) and bearings to within 1°.
/// </summary>
public class MeasureAccuracyTests
{
    private const double BearingToleranceDegrees = 1.0;

    // Positions from the S-164 PowerUp exchange set, in degrees and minutes.
    private static readonly GeoPosition Viking = Dm(32, 35.224, 'S', 61, 17.710, 'E');
    private static readonly GeoPosition CorundCape = Dm(32, 27.436, 'S', 60, 58.609, 'E');
    private static readonly GeoPosition Castlerigg = Dm(32, 23.280, 'S', 60, 58.496, 'E');
    private static readonly GeoPosition CorundCapeAccuracy3 = Dm(32, 27.447, 'S', 60, 58.599, 'E');
    private static readonly GeoPosition WormHead = Dm(32, 31.958, 'S', 60, 54.337, 'E');

    public static TheoryData<string, LegGeometry, string, string, double, double, double> Measurements => new()
    {
        { "Accuracy1", LegGeometry.GreatCircle, nameof(Viking), nameof(CorundCape), 33193.554, 295.614, 115.785 },
        { "Accuracy2", LegGeometry.GreatCircle, nameof(Viking), nameof(Castlerigg), 37326.351, 306.172, 126.344 },
        { "Accuracy3", LegGeometry.GreatCircle, nameof(CorundCapeAccuracy3), nameof(WormHead), 10680.859, 218.665, 38.703 },
        { "Accuracy7", LegGeometry.Rhumb, nameof(Viking), nameof(CorundCape), 33193.567, 295.699, 115.699 },
        { "Accuracy8", LegGeometry.Rhumb, nameof(Viking), nameof(Castlerigg), 37326.365, 306.258, 126.258 },
        { "Accuracy9", LegGeometry.Rhumb, nameof(CorundCapeAccuracy3), nameof(WormHead), 10680.859, 218.684, 38.684 },
    };

    public static TheoryData<string, string, double, double, string> DirectProblems => new()
    {
        { "Accuracy4", nameof(Viking), 33193.554, 295.614, nameof(CorundCape) },
        { "Accuracy5", nameof(Viking), 37326.351, 306.172, nameof(Castlerigg) },
        { "Accuracy6", nameof(CorundCapeAccuracy3), 10680.859, 218.665, nameof(WormHead) },
    };

    [Theory]
    [MemberData(nameof(Measurements))]
    public void Distance_and_bearings_are_within_tolerance(
        string test, LegGeometry geometry, string from, string to, double metres, double forward, double reverse)
    {
        var a = Position(from);
        var b = Position(to);

        var distance = MarineGeodesy.DistanceMetres(geometry, a, b);

        Assert.True(Math.Abs(distance - metres) <= DistanceTolerance(metres), $"{test}: {distance:F1} m vs {metres} m");
        AssertBearing(test, forward, MarineGeodesy.InitialBearingDegrees(geometry, a, b));
        AssertBearing(test, reverse, MarineGeodesy.ReverseBearingDegrees(geometry, a, b));
    }

    [Theory]
    [MemberData(nameof(DirectProblems))]
    public void Direct_problem_ends_within_tolerance(string test, string origin, double metres, double bearing, string expected)
    {
        var end = MarineGeodesy.Destination(LegGeometry.GreatCircle, Position(origin), bearing, metres);

        var miss = MarineGeodesy.DistanceMetres(LegGeometry.GreatCircle, end, Position(expected));
        Assert.True(miss <= DistanceTolerance(metres), $"{test}: end is {miss:F1} m from the listed position");
    }

    [Theory]
    [InlineData(LegGeometry.GreatCircle)]
    [InlineData(LegGeometry.Rhumb)]
    public void Measuring_then_running_the_direct_problem_returns_to_the_point(LegGeometry geometry)
    {
        // One earth radius for both directions: measure a leg, then lay the
        // same range and bearing off from its start, and land on its end.
        var distance = MarineGeodesy.DistanceMetres(geometry, Viking, CorundCape);
        var bearing = MarineGeodesy.InitialBearingDegrees(geometry, Viking, CorundCape);

        var end = MarineGeodesy.Destination(geometry, Viking, bearing, distance);

        Assert.True(MarineGeodesy.DistanceMetres(LegGeometry.GreatCircle, end, CorundCape) < 0.001);
    }

    [Fact]
    public void Rhumb_reverse_bearing_is_the_forward_bearing_plus_180()
    {
        var forward = MarineGeodesy.InitialBearingDegrees(LegGeometry.Rhumb, Viking, Castlerigg);
        var reverse = MarineGeodesy.ReverseBearingDegrees(LegGeometry.Rhumb, Viking, Castlerigg);

        Assert.Equal((forward + 180.0) % 360.0, reverse, 9);
    }

    [Fact]
    public void Great_circle_reverse_bearing_differs_from_the_forward_bearing_plus_180()
    {
        var from = new GeoPosition(30, -60);
        var to = new GeoPosition(60, -30);

        var forward = MarineGeodesy.InitialBearingDegrees(LegGeometry.GreatCircle, from, to);
        var reverse = MarineGeodesy.ReverseBearingDegrees(LegGeometry.GreatCircle, from, to);

        Assert.True(Math.Abs(((forward + 180.0) % 360.0) - reverse) > 10.0);
    }

    private static double DistanceTolerance(double metres) => Math.Max(0.01 * metres, 30.0);

    private static void AssertBearing(string test, double expected, double actual)
    {
        var difference = Math.Abs(((actual - expected + 540.0) % 360.0) - 180.0);
        Assert.True(difference <= BearingToleranceDegrees, $"{test}: {actual:F3}° vs {expected}°");
    }

    private static GeoPosition Position(string name) => name switch
    {
        nameof(Viking) => Viking,
        nameof(CorundCape) => CorundCape,
        nameof(Castlerigg) => Castlerigg,
        nameof(CorundCapeAccuracy3) => CorundCapeAccuracy3,
        nameof(WormHead) => WormHead,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
    };

    private static GeoPosition Dm(int latDeg, double latMin, char ns, int lonDeg, double lonMin, char ew)
    {
        var lat = latDeg + latMin / 60.0;
        var lon = lonDeg + lonMin / 60.0;
        return new GeoPosition(ns == 'S' ? -lat : lat, ew == 'W' ? -lon : lon);
    }
}

using System.Globalization;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Viewer.Geodesy;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The S-164 Part 4 Accuracy1–15 checks on the measure maths. S-164 asks for
/// distances within 1% or 30 m (whichever is greater) and bearings within 1°;
/// on the WGS-84 ellipsoid the tools agree with its values to a metre and
/// 0.001°, so that's what these check. Accuracy10–15 check that long great
/// circles, rhumb lines and the 2,000,000 m geodesic circle pass through a
/// sample of the positions S-164 lists for them.
/// </summary>
public class MeasureAccuracyTests
{
    // S-164 gives distances to the millimetre, bearings to 0.001° and
    // positions to 0.0001' (about 0.2 m).
    private const double DistanceToleranceMetres = 1.0;
    private const double BearingToleranceDegrees = 0.001;

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

        Assert.True(Math.Abs(distance - metres) <= DistanceToleranceMetres, $"{test}: {distance:F3} m vs {metres} m");
        AssertBearing(test, forward, MarineGeodesy.InitialBearingDegrees(geometry, a, b));
        AssertBearing(test, reverse, MarineGeodesy.ReverseBearingDegrees(geometry, a, b));
    }

    [Theory]
    [MemberData(nameof(DirectProblems))]
    public void Direct_problem_ends_within_tolerance(string test, string origin, double metres, double bearing, string expected)
    {
        var end = MarineGeodesy.Destination(LegGeometry.GreatCircle, Position(origin), bearing, metres);

        var miss = MarineGeodesy.DistanceMetres(LegGeometry.GreatCircle, end, Position(expected));
        Assert.True(miss <= DistanceToleranceMetres, $"{test}: end is {miss:F3} m from the listed position");
    }

    [Theory]
    [InlineData(LegGeometry.GreatCircle)]
    [InlineData(LegGeometry.Rhumb)]
    public void Measuring_then_running_the_direct_problem_returns_to_the_point(LegGeometry geometry)
    {
        // Measure a leg, then lay the same range and bearing off from its
        // start, and land on its end.
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

    // Long lines: test, geometry, start, end, a listed position on the line.
    // Positions read "deg min hemisphere deg min hemisphere".
    public static TheoryData<string, LegGeometry, string, string, string> LongLines => new()
    {
        { "Accuracy10 set 2", LegGeometry.GreatCircle, "30 0 N 60 0 W", "60 0 N 30 0 W", "38 6.0926 N 55 8.5692 W" },
        { "Accuracy10 set 2", LegGeometry.GreatCircle, "30 0 N 60 0 W", "60 0 N 30 0 W", "45 56.5257 N 49 5.7067 W" },
        { "Accuracy10 set 2", LegGeometry.GreatCircle, "30 0 N 60 0 W", "60 0 N 30 0 W", "53 20.9554 N 41 7.1195 W" },
        { "Accuracy10 set 3", LegGeometry.GreatCircle, "30 0 N 30 0 W", "60 0 N 60 0 W", "38 6.0926 N 34 51.4308 W" },
        { "Accuracy10 set 3", LegGeometry.GreatCircle, "30 0 N 30 0 W", "60 0 N 60 0 W", "53 20.9554 N 48 52.8805 W" },
        { "Accuracy10 set 4", LegGeometry.GreatCircle, "45 0 N 60 0 W", "45 0 N 30 0 W", "45 44.7022 N 52 33.9908 W" },
        { "Accuracy10 set 4", LegGeometry.GreatCircle, "45 0 N 60 0 W", "45 0 N 30 0 W", "45 59.7767 N 45 0 W" },
        { "Accuracy10 set 5", LegGeometry.GreatCircle, "30 0 N 45 0 W", "60 0 N 45 0 W", "45 0 N 45 0 W" },
        { "Accuracy11 set 7", LegGeometry.GreatCircle, "15 0 N 60 0 W", "15 0 S 30 0 W", "7 33.9393 N 52 22.2057 W" },
        { "Accuracy11 set 7", LegGeometry.GreatCircle, "15 0 N 60 0 W", "15 0 S 30 0 W", "0 0 N 45 0 W" },
        { "Accuracy11 set 8", LegGeometry.GreatCircle, "15 0 N 30 0 W", "15 0 S 60 0 W", "7 33.9393 S 52 22.2058 W" },
        { "Accuracy11 set 9", LegGeometry.GreatCircle, "0 0 N 60 0 W", "0 0 N 30 0 W", "0 0 N 45 0 W" },
        { "Accuracy11 set 10", LegGeometry.GreatCircle, "15 0 S 45 0 W", "15 0 N 45 0 W", "0 0 N 45 0 W" },
        { "Accuracy12 set 12", LegGeometry.GreatCircle, "30 0 S 60 0 W", "60 0 S 30 0 W", "45 56.5257 S 49 5.7067 W" },
        { "Accuracy12 set 13", LegGeometry.GreatCircle, "30 0 S 30 0 W", "60 0 S 60 0 W", "53 20.9554 S 48 52.8805 W" },
        { "Accuracy12 set 14", LegGeometry.GreatCircle, "45 0 S 60 0 W", "45 0 S 30 0 W", "45 59.7767 S 45 0 W" },
        { "Accuracy12 set 15", LegGeometry.GreatCircle, "30 0 S 45 0 W", "60 0 S 45 0 W", "45 0 S 45 0 W" },
        { "Accuracy13 set 2", LegGeometry.Rhumb, "30 0 N 30 0 W", "60 0 N 60 0 W", "37 30.8656 N 36 9.8628 W" },
        { "Accuracy13 set 2", LegGeometry.Rhumb, "30 0 N 30 0 W", "60 0 N 60 0 W", "45 1.1601 N 42 58.9871 W" },
        { "Accuracy13 set 2", LegGeometry.Rhumb, "30 0 N 30 0 W", "60 0 N 60 0 W", "52 30.8645 N 50 45.6910 W" },
        { "Accuracy13 set 3", LegGeometry.Rhumb, "60 0 N 30 0 W", "30 0 N 60 0 W", "45 1.1601 N 47 1.0129 W" },
        { "Accuracy13 set 4", LegGeometry.Rhumb, "45 0 N 60 0 W", "45 0 N 30 0 W", "45 0 N 45 0 W" },
        { "Accuracy13 set 5", LegGeometry.Rhumb, "30 0 N 45 0 W", "60 0 N 45 0 W", "45 0 N 45 0 W" },
        { "Accuracy14 set 6", LegGeometry.Rhumb, "15 0 N 60 0 W", "15 0 S 30 0 W", "7 30.0761 N 52 26.1306 W" },
        { "Accuracy14 set 6", LegGeometry.Rhumb, "15 0 N 60 0 W", "15 0 S 30 0 W", "7 30.0761 S 37 33.8694 W" },
        { "Accuracy14 set 7", LegGeometry.Rhumb, "15 0 N 30 0 W", "15 0 S 60 0 W", "7 30.0761 N 37 33.8694 W" },
        { "Accuracy14 set 8", LegGeometry.Rhumb, "0 0 N 60 0 W", "0 0 N 30 0 W", "0 0 N 45 0 W" },
        { "Accuracy14 set 9", LegGeometry.Rhumb, "15 0 S 45 0 W", "15 0 N 45 0 W", "0 0 N 45 0 W" },
        { "Accuracy15 set 10", LegGeometry.Rhumb, "30 0 S 30 0 W", "60 0 S 60 0 W", "45 1.1601 S 42 58.9871 W" },
        { "Accuracy15 set 11", LegGeometry.Rhumb, "60 0 S 30 0 W", "30 0 S 60 0 W", "37 30.8656 S 53 50.1372 W" },
        { "Accuracy15 set 12", LegGeometry.Rhumb, "45 0 S 60 0 W", "45 0 S 30 0 W", "45 0 S 45 0 W" },
        { "Accuracy15 set 13", LegGeometry.Rhumb, "30 0 S 45 0 W", "60 0 S 45 0 W", "45 0 S 45 0 W" },
    };

    // Geodesic circles of 2,000,000 m: test, centre, a listed position on the circle.
    public static TheoryData<string, string, string> CirclePoints => new()
    {
        { "Accuracy10 set 6", "45 0 N 45 0 W", "62 58.1482 N 45 0 W" },
        { "Accuracy10 set 6", "45 0 N 45 0 W", "42 16.1548 N 20 24.1958 W" },
        { "Accuracy10 set 6", "45 0 N 45 0 W", "26 58.5455 N 45 0 W" },
        { "Accuracy10 set 6", "45 0 N 45 0 W", "42 16.1548 N 69 35.8042 W" },
        { "Accuracy11 set 11", "0 0 N 45 0 W", "18 4.8887 N 45 0 W" },
        { "Accuracy11 set 11", "0 0 N 45 0 W", "0 0 N 27 2.0217 W" },
        { "Accuracy11 set 11", "0 0 N 45 0 W", "18 4.8887 S 45 0 W" },
        { "Accuracy11 set 11", "0 0 N 45 0 W", "0 0 N 62 57.9783 W" },
        { "Accuracy12 set 16", "45 0 S 45 0 W", "62 58.1482 S 45 0 W" },
        { "Accuracy12 set 16", "45 0 S 45 0 W", "42 16.1548 S 20 24.1958 W" },
        { "Accuracy12 set 16", "45 0 S 45 0 W", "26 58.5455 S 45 0 W" },
        { "Accuracy12 set 16", "45 0 S 45 0 W", "42 16.1548 S 69 35.8042 W" },
    };

    [Theory]
    [MemberData(nameof(LongLines))]
    public void Long_lines_pass_close_to_the_listed_positions(
        string test, LegGeometry geometry, string start, string end, string listed)
    {
        var a = Parse(start);
        var b = Parse(end);
        var point = Parse(listed);

        // The closest approach of the line: sample it, then narrow down
        // around the nearest sample.
        double Miss(double fraction) =>
            MarineGeodesy.DistanceMetres(LegGeometry.GreatCircle, PassageGeodesy.Interpolate(a, b, fraction, geometry), point);
        const int samples = 1_000;
        var nearest = Enumerable.Range(0, samples + 1).MinBy(i => Miss((double)i / samples));
        var low = Math.Max(0, nearest - 1) / (double)samples;
        var high = Math.Min(samples, nearest + 1) / (double)samples;
        var golden = (Math.Sqrt(5) - 1) / 2;
        for (var k = 0; k < 60; k++)
        {
            var x1 = high - golden * (high - low);
            var x2 = low + golden * (high - low);
            if (Miss(x1) < Miss(x2))
                high = x2;
            else
                low = x1;
        }

        var miss = Miss((low + high) / 2);
        Assert.True(miss <= DistanceToleranceMetres, $"{test}: line passes {miss:F3} m from {listed}");
    }

    [Theory]
    [MemberData(nameof(CirclePoints))]
    public void Geodesic_circle_passes_close_to_the_listed_positions(string test, string centre, string listed)
    {
        const double radius = 2_000_000;
        var c = Parse(centre);
        var point = Parse(listed);

        var circle = MarineGeodesy.GeodesicCircle(c, radius);
        var bearing = MarineGeodesy.InitialBearingDegrees(LegGeometry.GreatCircle, c, point);
        var drawn = MarineGeodesy.Destination(LegGeometry.GreatCircle, c, bearing, radius);

        var miss = MarineGeodesy.DistanceMetres(LegGeometry.GreatCircle, drawn, point);
        Assert.True(miss <= DistanceToleranceMetres, $"{test}: circle passes {miss:F3} m from {listed}");
        Assert.All(circle, p => Assert.Equal(radius, MarineGeodesy.DistanceMetres(LegGeometry.GreatCircle, c, p), 3));
    }

    // "deg min N deg min W" → decimal degrees.
    private static GeoPosition Parse(string text)
    {
        var parts = text.Split(' ');
        var lat = int.Parse(parts[0], CultureInfo.InvariantCulture) + double.Parse(parts[1], CultureInfo.InvariantCulture) / 60.0;
        var lon = int.Parse(parts[3], CultureInfo.InvariantCulture) + double.Parse(parts[4], CultureInfo.InvariantCulture) / 60.0;
        return new GeoPosition(parts[2] == "S" ? -lat : lat, parts[5] == "W" ? -lon : lon);
    }

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
